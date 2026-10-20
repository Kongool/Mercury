using System;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Toast;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Mercury.Windows;
using Mercury.Windows.Dock;

namespace Mercury;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/mercury";

    public Configuration Config { get; }
    public OccultRecordService RecordService { get; }
    public MartialMemories MartialMemories { get; }
    public MartialMemoriesScraper MartialScraper { get; }
    public ChallengeLog ChallengeLog { get; }
    public ChallengeLogScraper ChallengeScraper { get; }
    public CeTracker CeTracker { get; } = new();
    public FateTracker FateTracker { get; } = new();
    public ScanAlerter ScanAlerter { get; } = new();

    private readonly WindowSystem windowSystem = new("Mercury");
    private readonly MainWindow mainWindow;
    private readonly DockLayoutShare dockLayout;
    private readonly EdgeDockWindow edgeDock;
    private bool wasInOccult;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        this.Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.RecordService = new OccultRecordService();
        this.MartialMemories = new MartialMemories();
        this.MartialScraper = new MartialMemoriesScraper(this.MartialMemories);
        this.ChallengeLog = new ChallengeLog();
        this.ChallengeScraper = new ChallengeLogScraper(this.ChallengeLog, this.Config);

        this.mainWindow = new MainWindow(this);
        this.windowSystem.AddWindow(this.mainWindow);
        // Order 30 settles ties after Daedalus (10) and Charon (20). Fixed size because the body's scrolling
        // tables and map take whatever height they're given.
        this.dockLayout = new DockLayoutShare(Service.PluginInterface, "Mercury", order: 30);
        this.edgeDock = new EdgeDockWindow(
            [new EdgeDockWindow.Tab("main", "Mercury", () => true, () => this.mainWindow.Draw(), 760f, 900f)],
            _ => (this.Config.EdgeDockEdge, this.Config.EdgeDockAlong),
            (_, edge, along) =>
            {
                this.Config.EdgeDockEdge = edge;
                this.Config.EdgeDockAlong = along;
                this.Config.Save();
            },
            this.dockLayout);
        this.windowSystem.AddWindow(this.edgeDock);

        this.FateTracker.FateActivated += this.OnFateActivated;
        this.CeTracker.CeActivated += this.OnCeActivated;
        this.FateTracker.LocationLearned += this.OnLocationLearned;
        this.CeTracker.LocationLearned += this.OnLocationLearned;
        this.CeTracker.CeCompleted += this.OnCeCompleted;
        this.FateTracker.FateCompleted += this.OnFateCompleted;
        this.ScanAlerter.EnemyAppeared += this.OnScanEnemyAppeared;

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Toggle the Mercury window. \"/mercury mm\" / \"/mercury cl\" dump the open Martial Memories / Challenge Log window to the log.",
        });

        Service.PluginInterface.UiBuilder.Draw += this.DrawUi;
        Service.PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi += this.OpenMain;
        Service.Framework.Update += this.OnUpdate;
    }

    private void DrawUi()
    {
        // AnyTab() also refreshes which tabs the dock shows this frame, so it runs right before drawing.
        this.edgeDock.IsOpen = this.Config.EdgeDockEnabled && Service.ClientState.IsLoggedIn && this.edgeDock.AnyTab();
        this.windowSystem.Draw();
    }

    private void OnUpdate(IFramework framework)
    {
        this.CeTracker.Update();
        this.FateTracker.Update();
        this.ScanAlerter.Update(this.Config.ScanAlertEnabled ? this.Config.ScanAlertName : null);
        this.ChallengeScraper.Update(this.RecordService.GetChallengeResetUnix());


        var inOccult = this.RecordService.InOccultCrescent();

        if (this.Config.AutoOpenInOccult && inOccult != this.wasInOccult)
            this.mainWindow.IsOpen = inOccult;

        this.wasInOccult = inOccult;
    }

    // Raised the instant a FATE or CE goes up. The watch-list (AlertFates) is shared
    // across both systems - a name alerts whether it's a FATE or a Critical Encounter,
    // which is what lets the phantom-job shard CEs (Dark Artistry / Appalling Behavior)
    // alert even though they are not FATEs.
    private void OnFateActivated(string name)
        => this.AlertEncounter(name, "FATE up", this.Config.AlertOnAnyFate);

    private void OnCeActivated(string name)
        => this.AlertEncounter(name, "CE up", alertAny: false);

    // A completed CE / FATE nudges the matching Challenge Log count live, between window syncs.
    private void OnCeCompleted(string name) => this.ChallengeScraper.OnCriticalEncounterCompleted();

    private void OnFateCompleted(string name) => this.ChallengeScraper.OnFateCompleted();

    // Persists a learned encounter position so the nearest-crystal hint outlives the session.
    private void OnLocationLearned(string name, float x, float z)
    {
        this.Config.EncounterLocations[name] = new[] { x, z };
        this.Config.Save();
    }

    /// <summary>
    /// Best-known world (X, Z) for an encounter: this session's live sighting first, else a
    /// position learned in a past session. Used for the "nearest crystal" hint.
    /// </summary>
    public bool TryGetLocation(string name, out float x, out float z)
    {
        if (this.FateTracker.TryGetLocation(name, out x, out z))
            return true;
        if (this.CeTracker.TryGetLocation(name, out x, out z))
            return true;
        if (this.Config.EncounterLocations.TryGetValue(name, out var p) && p.Length == 2)
        {
            x = p[0];
            z = p[1];
            return true;
        }

        x = z = 0f;
        return false;
    }

    /// <summary>The name of the crystal nearest a known encounter, or null if position unknown.</summary>
    public string? NearestCrystal(string name)
    {
        // A live-learned position is most accurate; fall back to a curated override so
        // known encounters show their crystal before the plugin has ever seen them.
        if (this.TryGetLocation(name, out var x, out var z))
        {
            var zone = MapCatalog.FromTerritory(Service.ClientState.TerritoryType);
            return OccultAetherytes.Nearest(zone, x, z)?.Name;
        }

        return EncounterCrystals.For(name);
    }

    private void AlertEncounter(string name, string prefix, bool alertAny)
    {
        if (!this.Config.FateAlertsEnabled)
            return;
        if (!alertAny && !this.Config.AlertFates.Contains(name))
            return;

        var reward = NotableFates.Reward(name);
        var crystal = this.NearestCrystal(name);
        var label = reward is null ? name : $"{name} ({reward})";
        var message = crystal is null ? $"{prefix}: {label}" : $"{prefix}: {label}  ->  {crystal}";
        var toast = crystal is null ? $"{prefix}: {label}" : $"{prefix}: {label}\nTake: {crystal}";

        Service.ChatGui.Print($"[Mercury] {message}");
        Service.ToastGui.ShowQuest(toast, new QuestToastOptions
        {
            Position = QuestToastPosition.Centre,
            DisplayCheckmark = false,
            PlaySound = true,
        });
    }

    // Raised when a scanned enemy first streams in. Reports its map coordinate.
    private void OnScanEnemyAppeared(string name, float worldX, float worldZ)
    {
        var mx = ((worldX + 1024f) / 2048f * 41f) + 1f;
        var my = ((worldZ + 1024f) / 2048f * 41f) + 1f;

        Service.ChatGui.Print($"[Mercury] {name} loaded in nearby  ->  ({mx:0.0}, {my:0.0})");
        Service.ToastGui.ShowQuest($"{name} nearby\n({mx:0.0}, {my:0.0})", new QuestToastOptions
        {
            Position = QuestToastPosition.Centre,
            DisplayCheckmark = false,
            PlaySound = false,
        });

        // audible beep - <se.6>, one of the game's chat alert sounds
        UIGlobals.PlayChatSoundEffect(6);
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim();
        if (arg.Equals("mm", StringComparison.OrdinalIgnoreCase) || arg.Equals("dump", StringComparison.OrdinalIgnoreCase))
        {
            Service.ChatGui.Print("[Mercury] " + this.MartialScraper.DumpToLog());
            return;
        }

        if (arg.Equals("cl", StringComparison.OrdinalIgnoreCase))
        {
            Service.ChatGui.Print("[Mercury] " + this.ChallengeScraper.Diagnose());
            return;
        }

        if (arg.Equals("cldump", StringComparison.OrdinalIgnoreCase))
        {
            Service.ChatGui.Print("[Mercury] " + this.ChallengeScraper.DumpToLog());
            return;
        }

        this.mainWindow.Toggle();
    }

    private void OpenMain() => this.mainWindow.IsOpen = true;

    public void Dispose()
    {
        this.FateTracker.FateActivated -= this.OnFateActivated;
        this.CeTracker.CeActivated -= this.OnCeActivated;
        this.CeTracker.CeCompleted -= this.OnCeCompleted;
        this.FateTracker.FateCompleted -= this.OnFateCompleted;
        this.ScanAlerter.EnemyAppeared -= this.OnScanEnemyAppeared;
        this.FateTracker.LocationLearned -= this.OnLocationLearned;
        this.CeTracker.LocationLearned -= this.OnLocationLearned;
        Service.Framework.Update -= this.OnUpdate;
        Service.PluginInterface.UiBuilder.Draw -= this.DrawUi;
        Service.PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenMain;
        this.windowSystem.RemoveAllWindows();
        this.mainWindow.Dispose();
        this.dockLayout.Dispose();
        Service.CommandManager.RemoveHandler(CommandName);
    }
}
