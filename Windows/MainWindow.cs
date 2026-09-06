using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Mercury.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string search = string.Empty;
    private OccultMap mapSelection = OccultMap.None; // None = follow the current zone
    private uint highlightRecordId;                  // survey point to emphasise on the map (0 = none)
    private bool selectMapTab;                        // request to jump to the Map tab next frame
    private string scanTerm = string.Empty;          // enemy name filter for the map scan
    private int martialZoneIdx = -1;                  // selected zone on the Martial radar (-1 = follow current)
    private float mapZoom = 1f;
    private Vector2 mapCenter = new(0.5f, 0.5f);
    private OccultMap zoomMap = OccultMap.None;

    // reused each frame to avoid per-draw allocations
    private readonly List<OccultRecord> acquired = new();
    private readonly List<OccultRecord> missing = new();

    public MainWindow(Plugin plugin)
        : base("Mercury - Occult Records###MercuryMain")
    {
        this.plugin = plugin;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(340, 340),
            MaximumSize = new Vector2(1200, 1600),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var records = this.plugin.RecordService.Records;
        var collected = this.plugin.RecordService.GetCollectedIds();

        this.acquired.Clear();
        this.missing.Clear();

        foreach (var r in records)
        {
            if (collected.Contains(r.Id))
                this.acquired.Add(r);
            else
                this.missing.Add(r);
        }

        var quests = this.plugin.RecordService.Quests;
        var questsDone = 0;
        foreach (var q in quests)
            if (this.plugin.RecordService.IsQuestComplete(q.RowId))
                questsDone++;

        var total = records.Count;

        // --- progress ---
        var frac = total == 0 ? 0f : (float)this.acquired.Count / total;
        ImGui.Text($"Collected {this.acquired.Count} / {total}");
        ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{frac * 100f:0}%");

        // --- search + descriptions toggle ---
        ImGui.SetNextItemWidth(200 * ImGui.GetIO().FontGlobalScale);
        ImGui.InputTextWithHint("##search", "Search records...", ref this.search, 128);

        var showDesc = this.plugin.Config.ShowDescriptions;
        ImGui.SameLine();
        if (ImGui.Checkbox("Descriptions", ref showDesc))
        {
            this.plugin.Config.ShowDescriptions = showDesc;
            this.plugin.Config.Save();
        }

        var zone = MapCatalog.FromTerritory(Service.ClientState.TerritoryType);

        // --- always-visible watched-FATE status and instance timer ---
        this.DrawWatchPanel();
        this.DrawInstanceTimer(zone);

        // --- per-tab counts (up / total where it makes sense) ---
        var (ceUp, ceTotal) = this.CeCounts();
        var ceLabel = ceTotal > 0 ? $"CEs ({ceUp}/{ceTotal})###ce" : "CEs###ce";

        var fateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in this.plugin.FateTracker.TrackedNames)
            fateNames.Add(n);
        foreach (var n in KnownFates.For(zone))
            fateNames.Add(n);
        var fateLabel = fateNames.Count > 0 ? $"FATEs ({this.ActiveFateCount()}/{fateNames.Count})###fate" : "FATEs###fate";

        var potNames = PotFates.For(zone);
        var potsUp = 0;
        foreach (var p in potNames)
            if (this.plugin.FateTracker.IsActive(p.Name))
                potsUp++;
        var potLabel = potNames.Count > 0 ? $"Pots ({potsUp}/{potNames.Count})###pots" : "Pots###pots";

        var martialTotal = 0;
        foreach (var cat in this.plugin.MartialMemories.Categories)
            foreach (var g in cat.Groups)
                martialTotal += g.Objectives.Count;
        var martialLabel = martialTotal > 0 ? $"Martial Memories ({martialTotal})###martial" : "Martial Memories###martial";

        var challengeEntries = this.plugin.ChallengeLog.Entries;
        var challengeDone = 0;
        foreach (var e in challengeEntries)
            if (this.plugin.RecordService.IsChallengeComplete(e.RowId))
                challengeDone++;
        var challengeLabel = challengeEntries.Count > 0
            ? $"Field Ops Log ({challengeDone}/{challengeEntries.Count})###chal"
            : "Field Ops Log###chal";

        // --- tabs ---
        if (!ImGui.BeginTabBar("##tabs"))
            return;

        if (ImGui.BeginTabItem($"Acquired ({this.acquired.Count})###acquired"))
        {
            this.DrawTable("##tblAcquired", this.acquired, showDesc, "No records collected yet.");
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"Missing ({this.missing.Count})###missing"))
        {
            this.DrawTable("##tblMissing", this.missing, showDesc, "You have collected every record.");
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"Quests ({questsDone}/{quests.Count})###quest"))
        {
            this.DrawQuests(quests, collected);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(ceLabel))
        {
            this.DrawCriticalEncounters();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(fateLabel))
        {
            this.DrawFates();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(potLabel))
        {
            this.DrawPots();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(martialLabel))
        {
            this.DrawMartial();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(challengeLabel))
        {
            this.DrawChallengeLog();
            ImGui.EndTabItem();
        }

        var bluLearned = 0;
        foreach (var s in PhantomBlueMage.Spells)
            if (this.plugin.Config.LearnedBlueSpells.Contains(s.Name))
                bluLearned++;
        if (ImGui.BeginTabItem($"Phantom BLU ({bluLearned}/{PhantomBlueMage.Spells.Count})###pblu"))
        {
            this.DrawPhantomBlue();
            ImGui.EndTabItem();
        }

        var mapFlags = this.selectMapTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        this.selectMapTab = false;
        if (ImGui.BeginTabItem("Map###map", mapFlags))
        {
            this.DrawMap(collected);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private static string FormatTime(uint seconds)
        => $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>Drops a real in-game map flag / waypoint at the given world position.</summary>
    private static unsafe void SetGameFlag(OccultMap map, Vector3 world)
    {
        var agent = AgentMap.Instance();
        if (agent is null)
            return;

        agent->SetFlagMapMarker(MapCatalog.TerritoryId(map), MapCatalog.MapRowId(map), world);
        agent->OpenMapByMapId(MapCatalog.MapRowId(map), MapCatalog.TerritoryId(map));
    }

    // (currently up, total roster) among the zone's Critical Encounters.
    private unsafe (int Up, int Total) CeCounts()
    {
        var container = DynamicEventContainer.GetInstance();
        if (container is null)
            return (0, 0);

        int up = 0, total = 0;
        var events = container->Events;
        for (var i = 0; i < events.Length; i++)
        {
            ref var e = ref events[i];
            if (e.State != DynamicEventState.Inactive)
            {
                up++;
                total++;
            }
            else if (!string.IsNullOrEmpty(e.Name.ToString()))
            {
                total++;
            }
        }

        return (up, total);
    }

    // How many FATEs are running or preparing right now.
    private unsafe int ActiveFateCount()
    {
        var fm = FateManager.Instance();
        if (fm is null)
            return 0;

        var n = 0;
        ref var fates = ref fm->Fates;
        for (var i = 0; i < fates.Count; i++)
        {
            var f = fates[i].Value;
            if (f != null && f->State is FateState.Running or FateState.Preparing)
                n++;
        }

        return n;
    }

    private static int StatePriority(DynamicEventState state) => state switch
    {
        DynamicEventState.Battle => 0,
        DynamicEventState.Warmup => 1,
        DynamicEventState.Register => 2,
        _ => 3,
    };

    private unsafe void DrawCriticalEncounters()
    {
        var container = DynamicEventContainer.GetInstance();
        if (container is null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.TextWrapped("Enter the Occult Crescent to track Critical Encounters.");
            ImGui.PopStyleColor();
            return;
        }

        var events = container->Events;

        // active fights first, then warmups, registration windows, then idle roster entries
        var order = new List<(int Idx, int Prio, uint Secs)>();
        for (var i = 0; i < events.Length; i++)
        {
            ref var e = ref events[i];
            var named = !string.IsNullOrEmpty(e.Name.ToString());
            if (e.State != DynamicEventState.Inactive || named)
                order.Add((i, StatePriority(e.State), e.SecondsLeft));
        }

        order.Sort((a, b) => a.Prio != b.Prio ? a.Prio.CompareTo(b.Prio) : a.Secs.CompareTo(b.Secs));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped("Active rows show time left. Idle CEs show how long since last up this session. Right-click a CE (bell) to alert with sound when it spawns - e.g. the shard CEs. Pots have their own tab.");
        ImGui.PopStyleColor();

        // diagnostics first so it is always reachable (the list below scrolls internally)
        DrawCeDiagnostics(container, events);
        ImGui.Spacing();

        if (!ImGui.BeginTable("##ceTable", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("Encounter", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        foreach (var (idx, _, _) in order)
        {
            ref var e = ref events[idx];
            var name = e.Name.ToString();
            var active = e.State != DynamicEventState.Inactive;

            ImGui.TableNextRow();

            // name (+ reward hint / participants), with a bell + right-click watch toggle
            ImGui.TableNextColumn();
            this.DrawWatchableName(
                name,
                active ? new Vector4(0.89f, 0.89f, 0.90f, 1f) : new Vector4(0.60f, 0.60f, 0.60f, 1f),
                "Critical Encounter");
            var ceReward = NotableFates.Reward(name);
            if (ceReward != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.75f, 0.35f, 1f));
                ImGui.TextUnformatted(ceReward);
                ImGui.PopStyleColor();
            }
            this.DrawCrystalHint(name);
            if (active && e.MaxParticipants > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.6f, 0.7f, 1f));
                ImGui.TextUnformatted($"{e.Participants}/{e.MaxParticipants} players");
                ImGui.PopStyleColor();
            }

            // status
            ImGui.TableNextColumn();
            (string label, Vector4 colour) = e.State switch
            {
                DynamicEventState.Register => ("Join now", new Vector4(0.95f, 0.70f, 0.25f, 1f)),
                DynamicEventState.Warmup => ("Starting", new Vector4(0.45f, 0.70f, 0.95f, 1f)),
                DynamicEventState.Battle => ($"Active {e.Progress}%", new Vector4(0.35f, 0.85f, 0.35f, 1f)),
                _ => ("Idle", new Vector4(0.5f, 0.5f, 0.5f, 1f)),
            };
            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();

            // time: countdown while active, else "time since last up" from Mercury's tracker
            ImGui.TableNextColumn();
            if (active)
            {
                ImGui.TextUnformatted(FormatTime(e.SecondsLeft));
            }
            else
            {
                var since = this.plugin.CeTracker.SecondsSinceLastActive(name);
                if (since is { } s)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                    ImGui.TextUnformatted($"{FormatTime((uint)s)} ago");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.TextDisabled("-");
                }
            }
        }

        ImGui.EndTable();
    }

    private static int FateStatePriority(FateState state) => state switch
    {
        FateState.Running => 0,
        FateState.Preparing => 1,
        _ => 2,
    };

    // A compact, tab-independent panel that keeps the watched FATEs (shard FATEs by
    // default) in view at all times while you're in the Occult Crescent, so you can see
    // at a glance whether one is up without switching to the FATEs tab.
    private unsafe void DrawWatchPanel()
    {
        if (!this.plugin.Config.ShowWatchPanel || this.plugin.Config.AlertFates.Count == 0)
            return;
        if (!this.plugin.RecordService.InOccultCrescent())
            return;
        var manager = FateManager.Instance();
        if (manager == null)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // snapshot the live active FATEs by name (values only - no pointers escape)
        var live = new Dictionary<string, (bool Preparing, long Remaining, byte Progress)>(
            StringComparer.OrdinalIgnoreCase);
        ref var fates = ref manager->Fates;
        for (var i = 0; i < fates.Count; i++)
        {
            var f = fates[i].Value;
            if (f == null || f->State is not (FateState.Running or FateState.Preparing))
                continue;
            var nm = f->Name.ToString();
            if (string.IsNullOrEmpty(nm))
                continue;
            var remaining = f->State == FateState.Running ? (long)f->StartTimeEpoch + f->Duration - now : 0;
            live[nm] = (f->State == FateState.Preparing, remaining, f->Progress);
        }

        // snapshot live CEs by name - the shard encounters live here, not in FateManager
        var liveCe = new Dictionary<string, (string Label, Vector4 Colour, uint SecondsLeft)>(
            StringComparer.OrdinalIgnoreCase);
        var ceContainer = DynamicEventContainer.GetInstance();
        if (ceContainer != null)
        {
            var events = ceContainer->Events;
            for (var i = 0; i < events.Length; i++)
            {
                ref var e = ref events[i];
                if (e.State == DynamicEventState.Inactive)
                    continue;
                var nm = e.Name.ToString();
                if (string.IsNullOrEmpty(nm))
                    continue;
                liveCe[nm] = e.State switch
                {
                    DynamicEventState.Register => ("Join now", new Vector4(0.95f, 0.70f, 0.25f, 1f), e.SecondsLeft),
                    DynamicEventState.Warmup => ("Starting", new Vector4(0.45f, 0.70f, 0.95f, 1f), e.SecondsLeft),
                    _ => ($"UP {e.Progress}%", new Vector4(0.35f, 0.85f, 0.35f, 1f), e.SecondsLeft),
                };
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.75f, 0.35f, 1f));
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextUnformatted(FontAwesomeIcon.Bell.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextUnformatted("Watched");
        ImGui.PopStyleColor();

        if (ImGui.BeginTable("##watchPanel", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Encounter", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 150);

            foreach (var name in this.plugin.Config.AlertFates)
            {
                ImGui.TableNextRow();

                // name (+ reward hint), dimmed unless it's up
                var up = live.ContainsKey(name) || liveCe.ContainsKey(name);
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, up
                    ? new Vector4(0.95f, 0.75f, 0.35f, 1f)
                    : new Vector4(0.70f, 0.70f, 0.72f, 1f));
                ImGui.TextUnformatted(name);
                ImGui.PopStyleColor();
                var reward = NotableFates.Reward(name);
                if (reward != null)
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.58f, 1f));
                    ImGui.TextUnformatted($"- {reward}");
                    ImGui.PopStyleColor();
                }
                this.DrawCrystalHint(name);

                // status
                ImGui.TableNextColumn();
                if (live.TryGetValue(name, out var info))
                {
                    if (info.Preparing)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.70f, 0.95f, 1f));
                        ImGui.TextUnformatted("Spawning");
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.35f, 1f));
                        var time = info.Remaining > 0 ? FormatTime((uint)info.Remaining) : "0:00";
                        ImGui.TextUnformatted($"UP {info.Progress}%  {time}");
                        ImGui.PopStyleColor();
                    }
                }
                else if (liveCe.TryGetValue(name, out var ce))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ce.Colour);
                    ImGui.TextUnformatted($"{ce.Label}  {FormatTime(ce.SecondsLeft)}");
                    ImGui.PopStyleColor();
                }
                else
                {
                    var since = this.plugin.FateTracker.SecondsSinceLastActive(name)
                                ?? this.plugin.CeTracker.SecondsSinceLastActive(name);
                    if (since is { } s)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                        ImGui.TextUnformatted($"{FormatTime((uint)s)} ago");
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.TextDisabled("not seen yet");
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
    }

    private unsafe void DrawFates()
    {
        var inOccult = this.plugin.RecordService.InOccultCrescent();
        var manager = FateManager.Instance();
        if (!inOccult || manager == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.TextWrapped("Enter the Occult Crescent to track FATEs.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped("Live FATEs in this zone, then the known North Horn roster (listed even before they spawn). Watched FATEs (bell) pop a toast + chat alert with sound when they go up. Right-click any FATE to add or remove it from the watch-list.");
        ImGui.PopStyleColor();

        var alerts = this.plugin.Config.FateAlertsEnabled;
        if (ImGui.Checkbox("Alerts", ref alerts))
        {
            this.plugin.Config.FateAlertsEnabled = alerts;
            this.plugin.Config.Save();
        }
        ImGui.SameLine();
        var anyFate = this.plugin.Config.AlertOnAnyFate;
        if (ImGui.Checkbox("Alert on every FATE", ref anyFate))
        {
            this.plugin.Config.AlertOnAnyFate = anyFate;
            this.plugin.Config.Save();
        }
        ImGui.SameLine();
        var showPanel = this.plugin.Config.ShowWatchPanel;
        if (ImGui.Checkbox("Watch panel", ref showPanel))
        {
            this.plugin.Config.ShowWatchPanel = showPanel;
            this.plugin.Config.Save();
        }
        ImGui.Spacing();

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Collect the currently-live FATEs, ordered running-first then by time remaining.
        ref var fates = ref manager->Fates;
        var order = new List<(int Idx, int Prio, long Secs)>();
        var activeNames = new HashSet<string>();
        for (var i = 0; i < fates.Count; i++)
        {
            var f = fates[i].Value;
            if (f == null)
                continue;
            var name = f->Name.ToString();
            if (string.IsNullOrEmpty(name) || f->State is not (FateState.Running or FateState.Preparing))
                continue;

            activeNames.Add(name);
            var remaining = f->State == FateState.Running
                ? (long)f->StartTimeEpoch + f->Duration - now
                : long.MaxValue;
            order.Add((i, FateStatePriority(f->State), remaining));
        }

        order.Sort((a, b) => a.Prio != b.Prio ? a.Prio.CompareTo(b.Prio) : a.Secs.CompareTo(b.Secs));

        if (!ImGui.BeginTable("##fateTable", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("FATE", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        foreach (var (idx, _, _) in order)
        {
            var f = fates[idx].Value;
            var name = f->Name.ToString();

            ImGui.TableNextRow();

            // name (+ reward hint / objective; the level fields are unusable here -
            // Occult Crescent leaves them unpopulated, e.g. "Lv. 1-255")
            ImGui.TableNextColumn();
            this.DrawWatchableName(name, new Vector4(0.89f, 0.89f, 0.90f, 1f));

            var reward = NotableFates.Reward(name);
            if (reward != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.75f, 0.35f, 1f));
                ImGui.TextUnformatted(reward);
                ImGui.PopStyleColor();
            }
            else
            {
                var objective = f->Objective.ToString();
                if (!string.IsNullOrEmpty(objective))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.6f, 0.7f, 1f));
                    ImGui.TextUnformatted(objective);
                    ImGui.PopStyleColor();
                }
            }

            this.DrawCrystalHint(name);

            // status
            ImGui.TableNextColumn();
            (string label, Vector4 colour) = f->State == FateState.Running
                ? ($"Active {f->Progress}%", new Vector4(0.35f, 0.85f, 0.35f, 1f))
                : ("Spawning", new Vector4(0.45f, 0.70f, 0.95f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            ImGui.TextUnformatted(label);
            ImGui.PopStyleColor();

            // time left (running only; a preparing FATE has no meaningful countdown yet)
            ImGui.TableNextColumn();
            if (f->State == FateState.Running)
            {
                var remaining = (long)f->StartTimeEpoch + f->Duration - now;
                ImGui.TextUnformatted(remaining > 0 ? FormatTime((uint)remaining) : "0:00");
            }
            else
            {
                ImGui.TextDisabled("-");
            }
        }

        // Idle roster: the known FATE catalog for the current zone plus any FATE seen live
        // this session, minus the ones currently up. Seen ones count up since last active;
        // catalogued-but-unseen ones read "not seen yet". Sorted for a stable order.
        var roster = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in this.plugin.FateTracker.TrackedNames)
            roster.Add(n);
        foreach (var n in KnownFates.For(MapCatalog.FromTerritory(Service.ClientState.TerritoryType)))
            roster.Add(n);

        foreach (var name in roster)
        {
            if (activeNames.Contains(name))
                continue;

            var since = this.plugin.FateTracker.SecondsSinceLastActive(name);

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            this.DrawWatchableName(name, new Vector4(0.60f, 0.60f, 0.60f, 1f));

            var idleReward = NotableFates.Reward(name);
            if (idleReward != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.70f, 0.58f, 0.32f, 1f));
                ImGui.TextUnformatted(idleReward);
                ImGui.PopStyleColor();
            }

            this.DrawCrystalHint(name);

            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            ImGui.TextUnformatted(since is null ? "-" : "Idle");
            ImGui.PopStyleColor();

            ImGui.TableNextColumn();
            if (since is { } s)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                ImGui.TextUnformatted($"{FormatTime((uint)s)} ago");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.TextDisabled("not seen yet");
            }
        }

        ImGui.EndTable();
    }

    // Cyan "-> Crystal" hint naming the aetheryte nearest a FATE/CE, once its position is
    // known (learned live this session, or remembered from a past one). Matches the cyan
    // crystal markers on the map.
    private void DrawCrystalHint(string name)
    {
        var crystal = this.plugin.NearestCrystal(name);
        if (crystal is null)
            return;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.95f, 1f));
        ImGui.TextUnformatted($"-> {crystal}");
        ImGui.PopStyleColor();
    }

    // Renders a FATE or CE name cell: a bell glyph + gold text when it's on the shared
    // alert watch-list, otherwise the given base colour. Right-click toggles the watch-list.
    private void DrawWatchableName(string name, Vector4 baseColour, string fallbackLabel = "FATE")
    {
        var watched = this.plugin.Config.AlertFates.Contains(name);
        var gold = new Vector4(0.95f, 0.75f, 0.35f, 1f);

        if (watched)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, gold);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Bell.ToIconString());
            ImGui.PopFont();
            ImGui.PopStyleColor();
            ImGui.SameLine();
        }

        var label = string.IsNullOrEmpty(name) ? fallbackLabel : name;
        ImGui.PushStyleColor(ImGuiCol.Text, watched ? gold : baseColour);
        ImGui.Selectable($"{label}##watch_{label}", false);
        ImGui.PopStyleColor();

        if (!string.IsNullOrEmpty(name) && ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            if (!this.plugin.Config.AlertFates.Remove(name))
                this.plugin.Config.AlertFates.Add(name);
            this.plugin.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(watched
                ? "Alerting when this is up. Right-click to stop."
                : "Right-click to alert when this is up.");
    }

    private void DrawPots()
    {
        var zone = MapCatalog.FromTerritory(Service.ClientState.TerritoryType);

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped("The two pot FATEs for the zone you're in, tracked live: UP now or how long since last up, plus the nearest crystal. The northern/southern label is set once both have been seen this session.");
        ImGui.PopStyleColor();

        ImGui.Spacing();

        if (!ImGui.BeginTable("##potsTable", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupColumn("Pot", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        // only the current zone's two pot FATEs (all four are real FATEs)
        switch (zone)
        {
            case OccultMap.SouthHorn:
                this.DrawZonePots("Persistent Pots", "Pleading Pots");
                break;

            case OccultMap.NorthHorn:
                this.DrawZonePots("Daylight Pottery", "In a Pot of Bother");
                break;

            default:
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextDisabled("Enter South or North Horn to see its pot spawns.");
                break;
        }

        ImGui.EndTable();
    }

    private void DrawInstanceTimer(OccultMap zone)
    {
        if (zone == OccultMap.None)
            return;

        var timer = this.plugin.RecordService.GetInstanceTimer();
        if (timer is null)
        {
            ImGui.TextDisabled("Instance timer: waiting for game data...");
            return;
        }

        var value = timer.Value;
        ImGui.TextDisabled($"Instance timer: {FormatTime(value.Remaining)} remaining ({FormatTime(value.Elapsed)} elapsed)");

        if (zone != OccultMap.NorthHorn)
            return;

        const uint firstNorthPotSeconds = 20 * 60;
        if (value.Elapsed < firstNorthPotSeconds)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.35f, 1f));
            ImGui.TextWrapped($"Fresh North Horn: Daylight Pottery (north) should spawn first in about {FormatTime(firstNorthPotSeconds - value.Elapsed)}.");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("The initial Daylight Pottery (north) spawn window has passed; using live pot observations.");
        }
    }

    // Draw a zone's two pot FATEs, labelling which is northern / southern once both
    // positions have been observed (smaller Z is further north in FFXIV world space).
    private void DrawZonePots(string a, string b)
    {
        var tagA = string.Empty;
        var tagB = string.Empty;
        if (this.plugin.FateTracker.TryGetLocation(a, out _, out var za) &&
            this.plugin.FateTracker.TryGetLocation(b, out _, out var zb) &&
            za != zb)
        {
            (tagA, tagB) = za < zb ? ("(northern)", "(southern)") : ("(southern)", "(northern)");
        }

        this.DrawFatePotRow(a, tagA);
        this.DrawFatePotRow(b, tagB);
    }

    // A North Horn pot FATE rendered in the pot section. These are real FATEs, so their
    // state comes live from the FATE tracker (up now, or how long since last up) - no
    // manual pop needed. Right-click toggles the alert watch-list; a crystal hint shows
    // which aetheryte is nearest once the FATE's position has been learned.
    private void DrawFatePotRow(string fateName, string tag)
    {
        var active = this.plugin.FateTracker.IsActive(fateName);
        var since = this.plugin.FateTracker.SecondsSinceLastActive(fateName);

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        this.DrawWatchableName(fateName, new Vector4(0.89f, 0.89f, 0.90f, 1f));
        if (!string.IsNullOrEmpty(tag))
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.58f, 1f));
            ImGui.TextUnformatted(tag);
            ImGui.PopStyleColor();
        }
        this.DrawCrystalHint(fateName);

        ImGui.TableNextColumn();
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.35f, 1f));
            ImGui.TextUnformatted("UP now");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            ImGui.TextUnformatted("Idle");
            ImGui.PopStyleColor();
        }

        ImGui.TableNextColumn();
        if (!active && since is { } s)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.TextUnformatted($"{FormatTime((uint)s)} ago");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("-");
        }
    }

    // A pot CE rendered as a normal row in the CE table. Left-click marks it popped
    // (starts the fixed respawn countdown), right-click resets it.
    private void DrawPotRow(string label, ref long poppedUnix, long now, long respawn)
    {
        var remaining = poppedUnix > 0 ? poppedUnix + respawn - now : -1;

        ImGui.TableNextRow();

        // name (clickable across the whole row)
        ImGui.TableNextColumn();
        if (ImGui.Selectable(label, false, ImGuiSelectableFlags.SpanAllColumns))
        {
            poppedUnix = now;
            this.plugin.Config.Save();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            poppedUnix = 0;
            this.plugin.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Left-click: mark popped. Right-click: reset.");

        // status
        ImGui.TableNextColumn();
        (string status, Vector4 colour) = poppedUnix <= 0
            ? ("Idle", new Vector4(0.5f, 0.5f, 0.5f, 1f))
            : remaining > 0
                ? ("Cooldown", new Vector4(0.95f, 0.70f, 0.25f, 1f))
                : ("Ready", new Vector4(0.35f, 0.85f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextUnformatted(status);
        ImGui.PopStyleColor();

        // time
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(remaining > 0 ? FormatTime((uint)remaining) : "-");
    }

    // ---- Field Operations Challenge Log ----

    private void DrawChallengeLog()
    {
        var log = this.plugin.ChallengeLog;
        var entries = log.Entries;
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("Challenge Log data unavailable.");
            return;
        }

        var done = 0;
        foreach (var e in entries)
            if (this.plugin.RecordService.IsChallengeComplete(e.RowId))
                done++;

        var frac = (float)done / entries.Count;
        ImGui.Text($"{log.CategoryName}  {done} / {entries.Count}");
        ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{frac * 100f:0}%");

        // weekly reset countdown, once the game has told us when it is
        var reset = this.plugin.RecordService.GetChallengeResetUnix();
        if (reset is { } r)
        {
            var left = r - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (left > 0)
            {
                var days = left / 86400;
                var hours = (left % 86400) / 3600;
                var mins = (left % 3600) / 60;
                ImGui.TextDisabled($"Weekly reset in {days}d {hours}h {mins}m");
            }
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped("Weekly Field Operations challenges (Occult Crescent, then Eureka). Completion is read live and resets each week. Running counts come from the in-game Challenge Log: open it once to the Field Operations tab to sync (Mercury then remembers them). The CE and FATE challenges also advance live as you complete encounters - reopen the log any time to correct the estimate.");
        ImGui.PopStyleColor();

        // live count scraped from the open in-game window, keyed by row id (cached after close)
        var scraped = this.plugin.ChallengeScraper.TryScrape();
        var lastSync = this.plugin.ChallengeScraper.LastSyncUtc;
        if (lastSync is { } sync)
        {
            var ago = (uint)Math.Max(0, (DateTimeOffset.UtcNow - new DateTimeOffset(sync, TimeSpan.Zero)).TotalSeconds);
            ImGui.TextDisabled($"Counts synced {FormatTime(ago)} ago");
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.70f, 0.25f, 1f));
            ImGui.TextUnformatted("Open the in-game Challenge Log (Field Operations tab) to sync counts.");
            ImGui.PopStyleColor();
        }
        ImGui.Spacing();

        if (!ImGui.BeginTable("##challengeTable", 3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("##chalStatus", ImGuiTableColumnFlags.WidthFixed, 24);
        ImGui.TableSetupColumn("Challenge", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableHeadersRow();

        var green = new Vector4(0.35f, 0.85f, 0.35f, 1f);
        var amber = new Vector4(0.95f, 0.70f, 0.25f, 1f);
        var dim = new Vector4(0.60f, 0.60f, 0.60f, 1f);

        foreach (var e in entries)
        {
            if (this.search.Length > 0 &&
                e.Name.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.Description.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var complete = this.plugin.RecordService.IsChallengeComplete(e.RowId);
            var hasProgress = scraped.TryGetValue(e.RowId, out var prog);
            var hasLive = !complete && hasProgress && prog.HasCount;
            var current = complete ? e.RequiredAmount : prog.Current;

            ImGui.TableNextRow();

            // completion glyph
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, complete ? green : dim);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(complete
                ? FontAwesomeIcon.Check.ToIconString()
                : FontAwesomeIcon.Circle.ToIconString());
            ImGui.PopFont();
            ImGui.PopStyleColor();

            // name + objective description + reward
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, complete ? green : new Vector4(0.88f, 0.88f, 0.9f, 1f));
            ImGui.TextUnformatted(e.Name);
            ImGui.PopStyleColor();
            if (!string.IsNullOrEmpty(e.Description))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, dim);
                ImGui.TextWrapped(e.Description);
                ImGui.PopStyleColor();
            }
            var reward = e.RewardText();
            if (reward.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.70f, 0.58f, 0.32f, 1f));
                ImGui.TextUnformatted(reward);
                ImGui.PopStyleColor();
            }

            // progress: X / required, with a bar. Completed rows read full; live counts only
            // appear while the in-game Challenge Log window is open on this tab.
            ImGui.TableNextColumn();
            if (complete)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, green);
                ImGui.TextUnformatted($"{e.RequiredAmount} / {e.RequiredAmount}");
                ImGui.PopStyleColor();
            }
            else if (hasLive)
            {
                var barFrac = e.RequiredAmount > 0 ? Math.Clamp((float)current / e.RequiredAmount, 0f, 1f) : 0f;
                ImGui.PushStyleColor(ImGuiCol.Text, amber);
                ImGui.TextUnformatted($"{current} / {e.RequiredAmount}");
                ImGui.PopStyleColor();
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, amber);
                ImGui.ProgressBar(barFrac, new Vector2(-1, 4), string.Empty);
                ImGui.PopStyleColor();
            }
            else
            {
                // no live buffer (Challenge Log window not open on this tab): current unknown
                ImGui.TextDisabled($"- / {e.RequiredAmount}");
            }
        }

        ImGui.EndTable();
    }

    // ---- Martial Memories (phantom weapon knowledge crystal) ----

    private void DrawPhantomBlue()
    {
        // live Phantom Blue Mage level (JobIndex 14), when in the Occult Crescent
        var level = this.plugin.RecordService.GetSupportJobLevel(14);
        if (level > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.7f, 0.95f, 1f));
            ImGui.TextUnformatted($"Phantom Blue Mage - Level {level}");
            ImGui.PopStyleColor();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped("Tick a spell once you've learned it (the game doesn't expose learned state). Click an enemy to scan the map for it.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        if (!ImGui.BeginTable("##pbluTable", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("##chk", ImGuiTableColumnFlags.WidthFixed, 24);
        ImGui.TableSetupColumn("Spell", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 28);
        ImGui.TableSetupColumn("Learn from", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var s in PhantomBlueMage.Spells)
        {
            ImGui.TableNextRow();

            // learned checkbox (manual, persisted)
            ImGui.TableNextColumn();
            var learned = this.plugin.Config.LearnedBlueSpells.Contains(s.Name);
            if (ImGui.Checkbox($"##bluchk_{s.Name}", ref learned))
            {
                if (learned)
                    this.plugin.Config.LearnedBlueSpells.Add(s.Name);
                else
                    this.plugin.Config.LearnedBlueSpells.Remove(s.Name);
                this.plugin.Config.Save();
            }

            // spell name (dimmed until learned)
            ImGui.TableNextColumn();
            if (!learned)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.60f, 0.60f, 0.60f, 1f));
            ImGui.TextUnformatted(s.Name);
            if (!learned)
                ImGui.PopStyleColor();

            ImGui.TableNextColumn();
            ImGui.TextDisabled(s.Level.ToString());

            ImGui.TableNextColumn();
            if (s.Enemy is null)
            {
                ImGui.TextDisabled(s.LearnFrom);
            }
            else
            {
                // clickable: send the enemy name to the map scanner and jump to the Map tab
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.75f, 0.95f, 1f));
                if (ImGui.Selectable($"{s.LearnFrom}###pblu_{s.Name}"))
                {
                    this.scanTerm = s.Enemy;
                    this.mapSelection = OccultMap.NorthHorn;
                    this.selectMapTab = true;
                }
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Scan the map for {s.Enemy}");
            }
        }

        ImGui.EndTable();
    }

    private void DrawMartial()
    {
        var data = this.plugin.MartialMemories;

        // Live progress overlay, scraped from the open in-game window if enabled. The game
        // exposes no module for this data, so anything not scraped falls back to manual ticks.
        var progress = this.plugin.Config.MartialUseScraping
            ? this.plugin.MartialScraper.TryScrape()
            : new Dictionary<int, MartialProgress>();

        if (!ImGui.BeginTabBar("##martialTabs"))
            return;

        if (ImGui.BeginTabItem("Checklist###mmChecklist"))
        {
            this.DrawMartialChecklist(data, progress);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Enemy Radar###mmRadar"))
        {
            this.DrawMartialRadar(data, progress);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    // True if an objective counts as complete. A manual tick always wins so you can clear a
    // step yourself; a scraped "done" from the open in-game window also completes it.
    private bool MartialDone(MartialObjective o, IReadOnlyDictionary<int, MartialProgress> progress)
        => this.plugin.Config.MartialDone.Contains(o.DetailId)
            || (progress.TryGetValue(o.DetailId, out var p) && p.Done);

    private void DrawMartialChecklist(MartialMemories data, Dictionary<int, MartialProgress> progress)
    {
        // overall progress
        var total = 0;
        var done = 0;
        foreach (var cat in data.Categories)
        foreach (var g in cat.Groups)
        foreach (var o in g.Objectives)
        {
            total++;
            if (this.MartialDone(o, progress))
                done++;
        }

        var frac = total == 0 ? 0f : (float)done / total;
        ImGui.Text($"Martial Memories {done} / {total}");
        ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{frac * 100f:0}%");

        var scrape = this.plugin.Config.MartialUseScraping;
        if (ImGui.Checkbox("Sync from open window", ref scrape))
        {
            this.plugin.Config.MartialUseScraping = scrape;
            this.plugin.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Overlay live progress read from the in-game Martial Memories window while it is open.\nRun \"/mercury mm\" with the window open to help improve detection.");

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(progress.Count > 0
            ? "Live progress is being read from the open in-game window. Click a row to also tick it manually."
            : "No live data (open the in-game Martial Memories window to sync). Click a row to tick it off manually.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var green = new Vector4(0.35f, 0.85f, 0.35f, 1f);
        var amber = new Vector4(0.95f, 0.70f, 0.25f, 1f);
        var dim = new Vector4(0.60f, 0.60f, 0.60f, 1f);
        var zoneColour = new Vector4(0.55f, 0.7f, 0.95f, 1f);

        if (!ImGui.BeginChild("##mmList"))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var cat in data.Categories)
        {
            var (cDone, cTotal) = (0, 0);
            foreach (var g in cat.Groups)
            foreach (var o in g.Objectives)
            {
                cTotal++;
                if (this.MartialDone(o, progress))
                    cDone++;
            }

            if (!ImGui.CollapsingHeader($"{cat.Name}  ({cDone}/{cTotal})###mm_{cat.Name}", ImGuiTreeNodeFlags.DefaultOpen))
                continue;

            foreach (var g in cat.Groups)
            {
                if (!string.IsNullOrEmpty(g.Name))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, zoneColour);
                    ImGui.TextUnformatted(g.Name);
                    ImGui.PopStyleColor();
                    ImGui.Separator();
                }

                foreach (var o in g.Objectives)
                {
                    if (this.search.Length > 0 &&
                        o.Text.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var scraped = progress.TryGetValue(o.DetailId, out var p);
                    var isDone = this.MartialDone(o, progress);

                    // completion glyph
                    ImGui.PushStyleColor(ImGuiCol.Text, isDone ? green : dim);
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextUnformatted(isDone ? FontAwesomeIcon.Check.ToIconString() : FontAwesomeIcon.Circle.ToIconString());
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    ImGui.SameLine();

                    // label - clicking toggles the manual tick
                    ImGui.PushStyleColor(ImGuiCol.Text, isDone ? green : new Vector4(0.88f, 0.88f, 0.9f, 1f));
                    ImGui.Selectable($"{o.Text}###mmobj{o.DetailId}", false);
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemClicked())
                    {
                        if (!this.plugin.Config.MartialDone.Remove(o.DetailId))
                            this.plugin.Config.MartialDone.Add(o.DetailId);
                        this.plugin.Config.Save();
                    }

                    // scraped fraction / manual-tick hint
                    if (scraped)
                    {
                        ImGui.SameLine();
                        ImGui.PushStyleColor(ImGuiCol.Text, p.Done ? green : amber);
                        ImGui.TextUnformatted($"{p.Current}/{p.Max}");
                        ImGui.PopStyleColor();
                    }

                    if (o.UnlockNote != null)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, dim);
                        ImGui.TextWrapped(o.UnlockNote);
                        ImGui.PopStyleColor();
                    }
                }

                ImGui.Spacing();
            }
        }

        ImGui.EndChild();
    }

    private void DrawMartialRadar(MartialMemories data, Dictionary<int, MartialProgress> progress)
    {
        var zones = data.Zones;
        if (zones.Count == 0)
        {
            ImGui.TextDisabled("No zone data available.");
            return;
        }

        var currentTerritory = Service.ClientState.TerritoryType;
        var currentIdx = -1;
        for (var i = 0; i < zones.Count; i++)
            if (zones[i].TerritoryType == currentTerritory)
                currentIdx = i;

        // pick the zone to show: explicit selection, else the zone you're standing in, else first
        var shownIdx = this.martialZoneIdx >= 0 && this.martialZoneIdx < zones.Count
            ? this.martialZoneIdx
            : currentIdx >= 0 ? currentIdx : 0;

        for (var i = 0; i < zones.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            if (ImGui.RadioButton(zones[i].Name, shownIdx == i))
                this.martialZoneIdx = i;
        }

        var zone = zones[shownIdx];
        var live = currentTerritory == zone.TerritoryType;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(live
            ? "Red dots are the target enemies currently loaded near you. Completed targets stop being tracked. Click a dot to set a map flag."
            : $"Enter {zone.Name} to see live enemy positions. Targets are listed below.");
        ImGui.PopStyleColor();

        // only hunt for targets whose objective isn't finished yet - a completed slay drops
        // off the radar (no marker, no scan)
        var activeMobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in zone.Targets)
            if (t.MobName != null && !this.MartialDone(t, progress))
                activeMobs.Add(t.MobName);

        // gather live target mobs from the object table (only meaningful in the zone itself)
        var found = new List<(string Name, Vector3 World)>();
        if (live && activeMobs.Count > 0)
        {
            foreach (var obj in Service.ObjectTable)
            {
                if (obj.ObjectKind != ObjectKind.BattleNpc)
                    continue;
                var name = obj.Name.TextValue;
                if (!string.IsNullOrEmpty(name) && activeMobs.Contains(name))
                    found.Add((name, obj.Position));
            }
        }

        // legend: each target's state - done (untracked), how many are visible, or none nearby
        if (ImGui.BeginTable("##mmLegend", 2, ImGuiTableFlags.SizingFixedFit))
        {
            foreach (var t in zone.Targets)
            {
                var mob = t.MobName!;
                var done = this.MartialDone(t, progress);
                var count = 0;
                foreach (var f in found)
                    if (f.Name.Equals(mob, StringComparison.OrdinalIgnoreCase))
                        count++;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, done
                    ? new Vector4(0.5f, 0.5f, 0.5f, 1f)
                    : new Vector4(0.88f, 0.88f, 0.9f, 1f));
                ImGui.Selectable($"{char.ToUpper(mob[0])}{mob[1..]}##mmtgt{t.DetailId}", false);
                ImGui.PopStyleColor();
                if (ImGui.IsItemClicked())
                {
                    if (!this.plugin.Config.MartialDone.Remove(t.DetailId))
                        this.plugin.Config.MartialDone.Add(t.DetailId);
                    this.plugin.Config.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(done ? "Completed - click to track again." : "Click to mark done and stop tracking.");
                ImGui.TableNextColumn();
                if (done)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.35f, 1f));
                    ImGui.TextUnformatted("done");
                    ImGui.PopStyleColor();
                }
                else if (count > 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.35f, 1f));
                    ImGui.TextUnformatted($"{count} nearby");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.TextDisabled(live ? "none nearby" : "-");
                }
            }

            ImGui.EndTable();
        }

        // diagnostics: what the object table actually holds, so name mismatches are visible
        this.DrawRadarDiagnostics(currentTerritory, zone, live);

        var tex = Service.TextureProvider.GetFromGame(zone.TexturePath).GetWrapOrDefault();
        if (tex is null)
        {
            ImGui.TextDisabled("Map image is still loading...");
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        var side = MathF.Max(96f, MathF.Min(avail.X, avail.Y));
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Image(tex.Handle, new Vector2(side, side));

        var drawList = ImGui.GetWindowDrawList();
        var mouse = ImGui.GetIO().MousePos;
        var red = ImGui.GetColorU32(new Vector4(0.95f, 0.35f, 0.30f, 1f));
        var outline = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f));

        foreach (var (name, world) in found)
        {
            // world -> texture fraction (SizeFactor 100, offset 0, same as the Occult maps)
            var fx = (world.X + 1024f) / 2048f;
            var fy = (world.Z + 1024f) / 2048f;
            var c = new Vector2(origin.X + (fx * side), origin.Y + (fy * side));

            drawList.AddCircleFilled(c, 5f, red);
            drawList.AddCircle(c, 6f, outline, 0, 1.5f);

            if (Vector2.Distance(mouse, c) <= 8f)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(name);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.65f, 1f));
                ImGui.TextUnformatted("Click to set a map flag");
                ImGui.PopStyleColor();
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    SetGameFlagRaw(zone.TerritoryType, zone.MapRowId, world);
            }
        }
    }

    // Shows what the object table actually contains so we can spot why a target isn't
    // matching (wrong territory, different in-game name, non-BattleNpc kind, etc.).
    private void DrawRadarDiagnostics(uint currentTerritory, MartialZone zone, bool live)
    {
        if (!ImGui.CollapsingHeader("Diagnostics (loaded objects)"))
            return;

        ImGui.TextDisabled($"You are in territory {currentTerritory}; {zone.Name} is territory {zone.TerritoryType}. " +
                           (live ? "Radar is live here." : "Radar is inactive - not in this zone."));

        var battleNpcs = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var obj in Service.ObjectTable)
        {
            total++;
            if (obj.ObjectKind != ObjectKind.BattleNpc)
                continue;
            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name))
                continue;
            battleNpcs.TryGetValue(name, out var n);
            battleNpcs[name] = n + 1;
        }

        ImGui.TextDisabled($"{total} objects loaded, {battleNpcs.Count} distinct battle NPC names.");

        if (ImGui.BeginTable("##radarDiag", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY,
                new Vector2(0, 200)))
        {
            ImGui.TableSetupColumn("Battle NPC", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();

            foreach (var (name, count) in battleNpcs)
            {
                // highlight ones that match a target for this zone
                var isTarget = false;
                foreach (var t in zone.Targets)
                    if (name.Equals(t.MobName, StringComparison.OrdinalIgnoreCase))
                        isTarget = true;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (isTarget)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.85f, 0.35f, 1f));
                ImGui.TextUnformatted(name);
                if (isTarget)
                    ImGui.PopStyleColor();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(count.ToString());
            }

            ImGui.EndTable();
        }
    }

    /// <summary>Drops an in-game map flag for an arbitrary territory/map (non-Occult zones).</summary>
    private static unsafe void SetGameFlagRaw(uint territory, uint mapRowId, Vector3 world)
    {
        var agent = AgentMap.Instance();
        if (agent is null)
            return;
        agent->SetFlagMapMarker(territory, mapRowId, world);
        agent->OpenMapByMapId(mapRowId, territory);
    }

    // Raw dump of the dynamic-event timing fields so we can see whether the game
    // actually schedules CE spawns in this instance (a future StartTimestamp) or not.
    private static unsafe void DrawCeDiagnostics(DynamicEventContainer* container, Span<DynamicEvent> events)
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Diagnostics (raw event data)"))
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ImGui.TextDisabled($"Container: CurrentEventId={container->CurrentEventId}, CurrentEventIndex={container->CurrentEventIndex}, now={now}");

        if (!ImGui.BeginTable("##ceDiag", 8,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
                new Vector2(0, 240)))
            return;

        ImGui.TableSetupColumn("#");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("State");
        ImGui.TableSetupColumn("StartTs");
        ImGui.TableSetupColumn("Start-now");
        ImGui.TableSetupColumn("SecsLeft");
        ImGui.TableSetupColumn("SecsDur");
        ImGui.TableSetupColumn("Dur(min)");
        ImGui.TableHeadersRow();

        for (var i = 0; i < events.Length; i++)
        {
            ref var e = ref events[i];
            var name = e.Name.ToString();
            if (string.IsNullOrEmpty(name) && e.State == DynamicEventState.Inactive && e.StartTimestamp == 0)
                continue; // truly empty slot

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(i.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(string.IsNullOrEmpty(name) ? "-" : name);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.State.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.StartTimestamp.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted((e.StartTimestamp - now).ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.SecondsLeft.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.SecondsDuration.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.Duration.ToString());
        }

        ImGui.EndTable();
    }

    private void DrawQuests(IReadOnlyList<OccultQuest> quests, HashSet<uint> collected)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.75f, 1f));
        ImGui.TextWrapped("The Occult Crescent story quest line. A check means you've completed it. Expand a quest to see the records it needs; click a missing one to show it on the map.");
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var green = new Vector4(0.35f, 0.85f, 0.35f, 1f);
        var amber = new Vector4(0.95f, 0.70f, 0.25f, 1f);
        var dim = new Vector4(0.60f, 0.60f, 0.60f, 1f);

        var lastNorth = (bool?)null;
        foreach (var q in quests)
        {
            if (this.search.Length > 0 &&
                q.Name.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (lastNorth != q.IsNorthHorn)
            {
                lastNorth = q.IsNorthHorn;
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.7f, 0.95f, 1f));
                ImGui.TextUnformatted(q.IsNorthHorn ? "The North Horn" : "The South Horn");
                ImGui.PopStyleColor();
                ImGui.Separator();
            }

            var done = this.plugin.RecordService.IsQuestComplete(q.RowId);
            var reqs = QuestRequirements.Get(q.RowId);

            // leading completion check (fixed-width so labels line up)
            if (done)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, green);
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
                ImGui.PopFont();
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.Dummy(new Vector2(ImGui.GetFrameHeight() * 0.7f, 0));
            }
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Text, done ? green : dim);
            var flags = reqs is { Count: > 0 }
                ? ImGuiTreeNodeFlags.SpanAvailWidth
                : ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.Bullet;
            var open = ImGui.TreeNodeEx($"{q.Name}###q{q.RowId}", flags);
            ImGui.PopStyleColor();

            if (open && reqs is { Count: > 0 })
            {
                foreach (var recId in reqs)
                {
                    var rec = this.plugin.RecordService.GetRecord(recId);
                    var src = RecordSources.Get(recId);
                    if (rec is null)
                        continue;

                    var has = collected.Contains(recId);
                    var coords = src is { Kind: RecordSourceKind.SurveyPoint }
                        ? $"  ({src.X:0.0}, {src.Y:0.0})"
                        : string.Empty;

                    // status glyph
                    ImGui.PushStyleColor(ImGuiCol.Text, has ? green : amber);
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextUnformatted(has ? FontAwesomeIcon.Check.ToIconString() : FontAwesomeIcon.MapPin.ToIconString());
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    ImGui.SameLine();

                    // clickable label -> show on map (only meaningful for missing survey points)
                    ImGui.PushStyleColor(ImGuiCol.Text, has ? dim : amber);
                    ImGui.Selectable($"{rec.Name}{coords}###req{q.RowId}_{recId}", false);
                    ImGui.PopStyleColor();

                    if (ImGui.IsItemClicked() && src is { Kind: RecordSourceKind.SurveyPoint })
                    {
                        this.highlightRecordId = recId;
                        this.mapSelection = src.Map;
                        this.selectMapTab = true;
                    }
                    if (!has && ImGui.IsItemHovered())
                        ImGui.SetTooltip("Show on map");
                }

                ImGui.TreePop();
            }
        }
    }

    private unsafe void DrawMap(HashSet<uint> collected)
    {
        // pick the zone: explicit selection wins, else follow the current zone, else default South Horn
        var current = MapCatalog.FromTerritory(Service.ClientState.TerritoryType);
        var map = this.mapSelection != OccultMap.None
            ? this.mapSelection
            : current != OccultMap.None ? current : OccultMap.SouthHorn;

        if (this.zoomMap != map)
        {
            this.zoomMap = map;
            this.ResetMapView();
        }

        if (ImGui.RadioButton("South Horn", map == OccultMap.SouthHorn))
            this.mapSelection = OccultMap.SouthHorn;
        ImGui.SameLine();
        if (ImGui.RadioButton("North Horn", map == OccultMap.NorthHorn))
            this.mapSelection = OccultMap.NorthHorn;
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextUnformatted("Green = collected, amber = to find, red = CE, purple = FATE, orange = pot, cyan = aetheryte, yellow = scan hit, white arrow = you");

        ImGui.SetNextItemWidth(220 * ImGui.GetIO().FontGlobalScale);
        ImGui.InputTextWithHint("##scan", "Scan for enemy (e.g. mimic)", ref this.scanTerm, 64);
        if (this.scanTerm.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##scan"))
                this.scanTerm = string.Empty;
        }

        // alert-when-it-loads toggle (works in the background via the ScanAlerter)
        var alertOn = this.plugin.Config.ScanAlertEnabled;
        if (ImGui.Checkbox("Alert when it loads in", ref alertOn))
        {
            this.plugin.Config.ScanAlertEnabled = alertOn;
            this.plugin.Config.Save();
        }

        // keep the alert target in sync with the scan box (so typing after ticking works)
        if (this.plugin.Config.ScanAlertEnabled && this.scanTerm.Length >= 2 &&
            this.plugin.Config.ScanAlertName != this.scanTerm)
        {
            this.plugin.Config.ScanAlertName = this.scanTerm;
            this.plugin.Config.Save();
        }
        if (this.plugin.Config.ScanAlertEnabled && !string.IsNullOrEmpty(this.plugin.Config.ScanAlertName))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"(alerting for \"{this.plugin.Config.ScanAlertName}\")");
        }

        ImGui.TextUnformatted("Zoom");
        ImGui.SameLine();
        if (ImGui.SmallButton("-##mapZoom"))
            this.SetMapZoom(this.mapZoom / 1.25f);
        ImGui.SameLine();
        if (ImGui.SmallButton("+##mapZoom"))
            this.SetMapZoom(this.mapZoom * 1.25f);
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##mapZoom"))
            this.ResetMapView();
        ImGui.SameLine();
        ImGui.TextDisabled($"{this.mapZoom * 100f:0}%  (mouse wheel to zoom; middle/right drag to pan)");

        var mapDisplaySize = this.plugin.Config.MapDisplaySize;
        ImGui.SetNextItemWidth(180f * ImGui.GetIO().FontGlobalScale);
        if (ImGui.SliderFloat("Map size", ref mapDisplaySize, 400f, 1000f, "%.0f px"))
            this.plugin.Config.MapDisplaySize = mapDisplaySize;
        if (ImGui.IsItemDeactivatedAfterEdit())
            this.plugin.Config.Save();

        // scan the loaded objects once - drives both the list here and the map dots below
        var scanHits = new List<(string Name, Vector3 Pos, float Dist)>();
        if (this.scanTerm.Length >= 2 && current == map)
        {
            var mePos = Service.ObjectTable.LocalPlayer?.Position ?? default;
            var farthest = 0f;
            foreach (var obj in Service.ObjectTable)
            {
                if (obj.ObjectKind == ObjectKind.BattleNpc)
                    farthest = MathF.Max(farthest, Vector3.Distance(mePos, obj.Position));
                if (!ScanAlerter.IsScanCandidate(obj))
                    continue;
                var onm = obj.Name.TextValue;
                if (string.IsNullOrEmpty(onm) ||
                    onm.IndexOf(this.scanTerm, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                scanHits.Add((onm, obj.Position, Vector3.Distance(mePos, obj.Position)));
            }
            scanHits.Sort((a, b) => a.Dist.CompareTo(b.Dist));

            ImGui.TextDisabled($"Loaded-enemy range right now: farthest is {farthest:0}y (client only streams nearby objects).");

            if (scanHits.Count == 0)
            {
                ImGui.TextDisabled($"No \"{this.scanTerm}\" loaded near you.");
            }
            else
            {
                ImGui.TextDisabled($"{scanHits.Count} found - click one to flag it:");
                var rows = Math.Min(scanHits.Count, 4);
                if (ImGui.BeginChild("##scanResults", new Vector2(0, (rows * ImGui.GetTextLineHeightWithSpacing()) + 8f), true))
                {
                    foreach (var hit in scanHits)
                    {
                        var mx = ((hit.Pos.X + 1024f) / 2048f * 41f) + 1f;
                        var my = ((hit.Pos.Z + 1024f) / 2048f * 41f) + 1f;
                        if (ImGui.Selectable($"{hit.Name}   ({mx:0.0}, {my:0.0})   {hit.Dist:0}y"))
                            SetGameFlag(map, hit.Pos);
                    }
                }

                ImGui.EndChild();
            }
        }

        var tex = Service.TextureProvider.GetFromGame(MapCatalog.TexturePath(map)).GetWrapOrDefault();
        if (tex is null)
        {
            ImGui.TextDisabled("Map image is still loading...");
            return;
        }

        // Use the requested display size and only cap it to the available width. Deliberately
        // do not cap to the remaining height: a large map can extend below the window and use
        // the normal ImGui vertical scrollbar instead of wasting the wide area seen in-game.
        var avail = ImGui.GetContentRegionAvail();
        var side = MathF.Max(96f, MathF.Min(avail.X, this.plugin.Config.MapDisplaySize));
        var origin = ImGui.GetCursorScreenPos();
        var viewSize = 1f / this.mapZoom;
        var halfView = viewSize * 0.5f;
        this.mapCenter = Vector2.Clamp(this.mapCenter, new Vector2(halfView), new Vector2(1f - halfView));
        var uv0 = this.mapCenter - new Vector2(halfView);
        var uv1 = this.mapCenter + new Vector2(halfView);
        ImGui.Image(tex.Handle, new Vector2(side, side), uv0, uv1);

        var mapHovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        if (mapHovered && io.MouseWheel != 0f)
        {
            var relative = Vector2.Clamp((io.MousePos - origin) / side, Vector2.Zero, Vector2.One);
            var anchor = uv0 + (relative * viewSize);
            var nextZoom = Math.Clamp(this.mapZoom * MathF.Pow(1.2f, io.MouseWheel), 1f, 8f);
            var nextView = 1f / nextZoom;
            this.mapZoom = nextZoom;
            this.mapCenter = anchor + ((new Vector2(0.5f) - relative) * nextView);
            var nextHalf = nextView * 0.5f;
            this.mapCenter = Vector2.Clamp(this.mapCenter, new Vector2(nextHalf), new Vector2(1f - nextHalf));
        }

        if (mapHovered && (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)))
        {
            this.mapCenter -= io.MouseDelta / side * viewSize;
            var dragHalf = viewSize * 0.5f;
            this.mapCenter = Vector2.Clamp(this.mapCenter, new Vector2(dragHalf), new Vector2(1f - dragHalf));
        }

        var drawList = ImGui.GetWindowDrawList();
        var mouse = ImGui.GetIO().MousePos;
        var green = ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.35f, 1f));
        var amber = ImGui.GetColorU32(new Vector4(0.95f, 0.70f, 0.25f, 1f));
        var outline = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.85f));

        Vector2 MapPoint(float x, float y)
            => origin + (((new Vector2(x, y) - uv0) / viewSize) * side);

        bool OnMap(Vector2 point, float margin = 0f)
            => point.X >= origin.X - margin && point.X <= origin.X + side + margin &&
               point.Y >= origin.Y - margin && point.Y <= origin.Y + side + margin;

        drawList.PushClipRect(origin, origin + new Vector2(side), true);

        foreach (var r in this.plugin.RecordService.Records)
        {
            var src = RecordSources.Get(r.Id);
            if (src is null || src.Kind != RecordSourceKind.SurveyPoint || src.Map != map)
                continue;

            // map coordinate -> texture fraction. SizeFactor is 100 for both zones, so c = 1.
            var fx = (src.X - 1f) / 41f;
            var fy = (src.Y - 1f) / 41f;
            var center = MapPoint(fx, fy);
            if (!OnMap(center, 12f))
                continue;

            var has = collected.Contains(r.Id);
            var highlighted = r.Id == this.highlightRecordId;

            if (highlighted)
            {
                // pulsing white ring + name label so the quest's target stands out
                var pulse = 9f + (2.5f * MathF.Sin((float)ImGui.GetTime() * 4f));
                drawList.AddCircle(center, pulse, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f)), 0, 2.5f);
                var label = r.Name;
                var ts = ImGui.CalcTextSize(label);
                var lp = new Vector2(center.X - (ts.X / 2f), center.Y - pulse - ts.Y - 3f);
                drawList.AddRectFilled(lp - new Vector2(3, 2), lp + ts + new Vector2(3, 2),
                    ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.75f)), 3f);
                drawList.AddText(lp, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);
            }

            drawList.AddCircleFilled(center, highlighted ? 6f : 5f, has ? green : amber);
            drawList.AddCircle(center, highlighted ? 7f : 6f, outline, 0, 1.5f);

            if (Vector2.Distance(mouse, center) <= 7f)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(r.Name);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.7f, 0.8f, 1f));
                ImGui.TextUnformatted($"({src.X:0.0}, {src.Y:0.0})  -  {(has ? "Collected" : "Not collected")}");
                ImGui.TextUnformatted("Click to set a map flag");
                ImGui.PopStyleColor();
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    // survey map coord -> world: (coord-1)/41 * 2048 - 1024
                    var world = new Vector3(
                        ((src.X - 1f) / 41f * 2048f) - 1024f,
                        0f,
                        ((src.Y - 1f) / 41f * 2048f) - 1024f);
                    SetGameFlag(map, world);
                }
            }
        }

        // aetheryte crystals - static landmarks for whichever zone map is up
        {
            var crystalColour = ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.95f, 1f));
            foreach (var crystal in OccultAetherytes.For(map))
            {
                var cfx = (crystal.X - 1f) / 41f;
                var cfy = (crystal.Y - 1f) / 41f;
                var cc = MapPoint(cfx, cfy);
                if (!OnMap(cc, 8f))
                    continue;

                // small diamond so crystals read differently from the round record/CE dots
                drawList.AddQuadFilled(
                    cc with { Y = cc.Y - 6f }, cc with { X = cc.X + 6f },
                    cc with { Y = cc.Y + 6f }, cc with { X = cc.X - 6f }, crystalColour);
                drawList.AddQuad(
                    cc with { Y = cc.Y - 6f }, cc with { X = cc.X + 6f },
                    cc with { Y = cc.Y + 6f }, cc with { X = cc.X - 6f }, outline, 1.5f);

                if (Vector2.Distance(mouse, cc) <= 8f)
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(crystal.Name);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.8f, 0.9f, 1f));
                    ImGui.TextUnformatted($"Aetheryte  ({crystal.X:0.0}, {crystal.Y:0.0})");
                    ImGui.TextUnformatted("Click to set a map flag");
                    ImGui.PopStyleColor();
                    ImGui.EndTooltip();

                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        var world = new Vector3(
                            ((crystal.X - 1f) / 41f * 2048f) - 1024f,
                            0f,
                            ((crystal.Y - 1f) / 41f * 2048f) - 1024f);
                        SetGameFlag(map, world);
                    }
                }
            }
        }

        // live Critical Encounters - only for the zone you're actually standing in
        if (MapCatalog.FromTerritory(Service.ClientState.TerritoryType) != map)
        {
            drawList.PopClipRect();
            return;
        }

        var container = DynamicEventContainer.GetInstance();
        if (container is null)
        {
            drawList.PopClipRect();
            return;
        }

        var ceColour = ImGui.GetColorU32(new Vector4(0.95f, 0.35f, 0.30f, 1f));
        var events = container->Events;
        for (var i = 0; i < events.Length; i++)
        {
            ref var e = ref events[i];
            if (e.State == DynamicEventState.Inactive)
                continue;

            var pos = e.MapMarker.Position;
            if (pos.X == 0f && pos.Z == 0f)
                continue; // no location yet

            // world coord -> texture fraction: (world + 1024) / 2048 (SizeFactor 100, offset 0)
            var c = MapPoint((pos.X + 1024f) / 2048f, (pos.Z + 1024f) / 2048f);
            if (!OnMap(c, 8f))
                continue;

            drawList.AddCircleFilled(c, 6f, ceColour);
            drawList.AddCircle(c, 7f, outline, 0, 1.5f);

            if (Vector2.Distance(mouse, c) <= 8f)
            {
                var nm = e.Name.ToString();
                var status = e.State switch
                {
                    DynamicEventState.Register => "Join now",
                    DynamicEventState.Warmup => "Starting",
                    DynamicEventState.Battle => $"Active {e.Progress}%",
                    _ => string.Empty,
                };
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(string.IsNullOrEmpty(nm) ? "Critical Encounter" : nm);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.7f, 0.65f, 1f));
                ImGui.TextUnformatted($"{status}  -  {FormatTime(e.SecondsLeft)}");
                ImGui.TextUnformatted("Click to set a map flag");
                ImGui.PopStyleColor();
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    SetGameFlag(map, pos);
            }
        }

        // live FATEs - same zone-you're-standing-in scope as the CEs above
        var fateManager = FateManager.Instance();
        if (fateManager == null)
        {
            drawList.PopClipRect();
            return;
        }

        var fateColour = ImGui.GetColorU32(new Vector4(0.68f, 0.45f, 0.95f, 1f));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ref var fates = ref fateManager->Fates;
        for (var i = 0; i < fates.Count; i++)
        {
            var f = fates[i].Value;
            if (f == null || f->State is not (FateState.Running or FateState.Preparing))
                continue;
            if (PotFates.IsPot(f->Name.ToString()))
                continue; // pots get their own persistent marker below

            var loc = f->Location;
            if (loc.X == 0f && loc.Z == 0f)
                continue; // no location yet

            var fc = MapPoint((loc.X + 1024f) / 2048f, (loc.Z + 1024f) / 2048f);
            if (!OnMap(fc, 8f))
                continue;

            drawList.AddCircleFilled(fc, 6f, fateColour);
            drawList.AddCircle(fc, 7f, outline, 0, 1.5f);

            if (Vector2.Distance(mouse, fc) <= 8f)
            {
                var nm = f->Name.ToString();
                var remaining = (long)f->StartTimeEpoch + f->Duration - now;
                var status = f->State == FateState.Running
                    ? $"Active {f->Progress}%  -  {(remaining > 0 ? FormatTime((uint)remaining) : "0:00")}"
                    : "Spawning";
                var fateReward = NotableFates.Reward(nm);
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(string.IsNullOrEmpty(nm) ? "FATE" : nm);
                if (fateReward != null)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.75f, 0.35f, 1f));
                    ImGui.TextUnformatted(fateReward);
                    ImGui.PopStyleColor();
                }
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.7f, 0.9f, 1f));
                ImGui.TextUnformatted(status);
                ImGui.TextUnformatted("Click to set a map flag");
                ImGui.PopStyleColor();
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    SetGameFlag(map, loc);
            }
        }

        // pot FATEs - orange labelled markers. South Horn spots are hardcoded so they show
        // always; others fall back to the position learned live this session.
        var potColour = ImGui.GetColorU32(new Vector4(0.95f, 0.55f, 0.15f, 1f));
        var potUpRing = ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.35f, 1f));
        foreach (var pot in PotFates.For(map))
        {
            float px, pz;
            if (pot.MapX is { } mx && pot.MapY is { } my)
            {
                // hardcoded map coord -> world: (coord-1)/41 * 2048 - 1024
                px = ((mx - 1f) / 41f * 2048f) - 1024f;
                pz = ((my - 1f) / 41f * 2048f) - 1024f;
            }
            else if (!this.plugin.FateTracker.TryGetLocation(pot.Name, out px, out pz))
            {
                continue; // no coord and not learned yet
            }

            var pc = MapPoint((px + 1024f) / 2048f, (pz + 1024f) / 2048f);
            if (!OnMap(pc, 80f))
                continue;
            var potUp = this.plugin.FateTracker.IsActive(pot.Name);

            drawList.AddCircleFilled(pc, 6f, potColour);
            drawList.AddCircle(pc, 7.5f, outline, 0, 1.5f);
            if (potUp)
                drawList.AddCircle(pc, 9.5f, potUpRing, 0, 2f); // green ring while it's up

            var lts = ImGui.CalcTextSize(pot.Name);
            var lpos = new Vector2(pc.X - (lts.X / 2f), pc.Y + 9f);
            drawList.AddRectFilled(lpos - new Vector2(3, 2), lpos + lts + new Vector2(3, 2),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 3f);
            drawList.AddText(lpos, ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.6f, 1f)), pot.Name);

            if (Vector2.Distance(mouse, pc) <= 8f)
            {
                var since = this.plugin.FateTracker.SecondsSinceLastActive(pot.Name);
                var st = potUp ? "UP now" : since is { } s ? $"Last up {FormatTime((uint)s)} ago" : "Idle";
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(pot.Name);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.8f, 0.6f, 1f));
                ImGui.TextUnformatted(st);
                ImGui.TextUnformatted("Click to set a map flag");
                ImGui.PopStyleColor();
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    SetGameFlag(map, new Vector3(px, 0f, pz));
            }
        }

        // enemy scan hits - bright yellow dots with a name label (collected above)
        var scanColour = ImGui.GetColorU32(new Vector4(1f, 0.92f, 0.15f, 1f));
        foreach (var hit in scanHits)
        {
            var sc = MapPoint((hit.Pos.X + 1024f) / 2048f, (hit.Pos.Z + 1024f) / 2048f);
            if (!OnMap(sc, 80f))
                continue;

            drawList.AddCircleFilled(sc, 5f, scanColour);
            drawList.AddCircle(sc, 6.5f, outline, 0, 1.5f);

            var lts = ImGui.CalcTextSize(hit.Name);
            var lpos = new Vector2(sc.X - (lts.X / 2f), sc.Y + 8f);
            drawList.AddRectFilled(lpos - new Vector2(3, 2), lpos + lts + new Vector2(3, 2),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 3f);
            drawList.AddText(lpos, ImGui.GetColorU32(new Vector4(1f, 1f, 0.7f, 1f)), hit.Name);

            if (Vector2.Distance(mouse, sc) <= 7f)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(hit.Name);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.6f, 1f));
                ImGui.TextUnformatted($"{hit.Dist:0} yalms away");
                ImGui.TextUnformatted("Click to set a map flag");
                ImGui.PopStyleColor();
                ImGui.EndTooltip();

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    SetGameFlag(map, hit.Pos);
            }
        }

        // you - a white arrowhead at the player's live position, pointing where you face
        var player = Service.ObjectTable.LocalPlayer;
        if (player is not null)
        {
            var wpos = player.Position;
            var me = MapPoint((wpos.X + 1024f) / 2048f, (wpos.Z + 1024f) / 2048f);

            // FFXIV rotation: 0 faces south (+Z); forward = (sin, cos) in world (X, Z)
            var rot = player.Rotation;
            var dir = new Vector2(MathF.Sin(rot), MathF.Cos(rot));
            var perp = new Vector2(-dir.Y, dir.X);
            const float r = 7f;
            var tip = me + (dir * r);
            var b1 = me - (dir * (r * 0.6f)) + (perp * (r * 0.6f));
            var b2 = me - (dir * (r * 0.6f)) - (perp * (r * 0.6f));

            if (OnMap(me, 8f))
            {
                drawList.AddTriangleFilled(tip, b1, b2, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
                drawList.AddTriangle(tip, b1, b2, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.9f)), 1.5f);
            }
        }

        drawList.PopClipRect();
    }

    private void ResetMapView()
    {
        this.mapZoom = 1f;
        this.mapCenter = new Vector2(0.5f, 0.5f);
    }

    private void SetMapZoom(float zoom)
    {
        this.mapZoom = Math.Clamp(zoom, 1f, 8f);
        var half = 0.5f / this.mapZoom;
        this.mapCenter = Vector2.Clamp(this.mapCenter, new Vector2(half), new Vector2(1f - half));
    }

    private void DrawTable(string id, List<OccultRecord> list, bool showDesc, string emptyMessage)
    {
        // apply the search filter
        var filtered = list;
        if (this.search.Length > 0)
        {
            filtered = new List<OccultRecord>();
            foreach (var r in list)
                if (r.Name.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(r);
        }

        if (filtered.Count == 0)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.TextWrapped(this.search.Length > 0 ? "No matching records." : emptyMessage);
            ImGui.PopStyleColor();
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg
                                      | ImGuiTableFlags.ScrollY
                                      | ImGuiTableFlags.BordersInnerH;
        if (!ImGui.BeginTable(id, 2, flags))
            return;

        ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, 34);
        ImGui.TableSetupColumn("Record", ImGuiTableColumnFlags.WidthStretch);

        foreach (var r in filtered)
        {
            ImGui.TableNextRow();

            // icon
            ImGui.TableNextColumn();
            if (r.Icon != 0)
            {
                var tex = Service.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(r.Icon))
                    .GetWrapOrDefault();
                if (tex is not null)
                    ImGui.Image(tex.Handle, new Vector2(28, 28));
            }

            // name
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(r.Name);
            var nameHovered = !showDesc && !string.IsNullOrEmpty(r.Description) && ImGui.IsItemHovered();

            // acquisition hint (quest name highlighted; field sources muted)
            var src = RecordSources.Get(r.Id);
            if (src != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, SourceColor(src.Kind));
                ImGui.TextUnformatted(FormatSource(src));
                ImGui.PopStyleColor();
            }

            // description: inline when toggled, otherwise a tooltip on the name
            if (!string.IsNullOrEmpty(r.Description))
            {
                if (showDesc)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1f));
                    ImGui.TextWrapped(r.Description);
                    ImGui.PopStyleColor();
                }
                else if (nameHovered)
                {
                    ImGui.SetTooltip(r.Description);
                }
            }
        }

        ImGui.EndTable();
    }

    private static string FormatSource(RecordSource s) => s.Kind switch
    {
        RecordSourceKind.Quest => $"Quest: {s.Text}",
        RecordSourceKind.SurveyPoint => $"Survey point - {s.Text}",
        RecordSourceKind.CriticalEncounter => $"Critical Encounter: {s.Text}",
        _ => s.Text,
    };

    private static Vector4 SourceColor(RecordSourceKind kind) => kind == RecordSourceKind.Quest
        ? new Vector4(0.95f, 0.75f, 0.35f, 1f)
        : new Vector4(0.55f, 0.60f, 0.70f, 1f);
}
