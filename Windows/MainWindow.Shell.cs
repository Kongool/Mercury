using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Mercury.Windows.Components;
using Mercury.Windows.Dock;

namespace Mercury.Windows;

// The window shell: header strip, a sidebar that lists only the pages for the zone you're in, and the
// page area. The pages themselves live in MainWindow.cs (Occult Crescent) and MainWindow.FieldOps.cs.
public sealed partial class MainWindow
{
    private enum Page
    {
        Overview,
        Ces,
        Fates,
        Pots,
        Weather,
        Map,
        Records,
        Quests,
        ChallengeLog,
        EurekaLog,
        PhantomBlu,
        Martial,
        EurekaStory,
        BozjaStory,
        Progress,
        Settings,
    }

    private sealed record NavItem(Page Page, string Label, FontAwesomeIcon Icon, string? Badge = null, Vector4? BadgeColor = null);

    private sealed record NavGroup(string Label, bool Here, List<NavItem> Items);

    private readonly List<NavGroup> nav = new();
    private Page page = Page.Progress;
    private FieldZone lastZone = (FieldZone)(-1);

    public override void Draw()
    {
        using var style = MercuryTheme.PushWindowStyle();
        var zone = FieldZones.Current;
        var collected = this.plugin.RecordService.GetCollectedIds();
        this.BuildNav(zone, collected);

        // Entering or leaving a zone lands on that zone's overview, or Progress outside one.
        if (zone != this.lastZone)
        {
            this.lastZone = zone;
            this.page = FieldZones.OpOf(zone) == FieldOp.None ? Page.Progress : Page.Overview;
        }

        if (this.page != Page.Settings && !this.NavHas(this.page))
            this.page = Page.Progress;

        this.DrawHeader(zone);

        var scale = ImGuiHelpers.GlobalScale;
        var sidebarW = MercuryTheme.SidebarWidth * scale;
        using (var side = ImRaii.Child("##mercuryNav", new Vector2(sidebarW, -1), false))
        {
            if (side.Success)
                this.DrawSidebar();
        }

        // hairline between the sidebar and the page
        var lineX = ImGui.GetItemRectMax().X + 6f * scale;
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(new Vector2(lineX, ImGui.GetItemRectMin().Y), new Vector2(lineX, ImGui.GetItemRectMax().Y), ImGui.GetColorU32(MercuryTheme.Hairline), 1f);

        ImGui.SameLine(0, 14f * scale);
        using (var body = ImRaii.Child("##mercuryPage", new Vector2(-1, -1), false))
        {
            if (body.Success)
                this.DrawPage(zone, collected);
        }
    }

    /// <summary>Jump to the Settings page (the plugin installer's Settings button).</summary>
    public void ShowSettings()
    {
        this.lastZone = FieldZones.Current; // so the zone-change reset doesn't override it
        this.page = Page.Settings;
    }

    private bool NavHas(Page p)
    {
        foreach (var g in this.nav)
            foreach (var i in g.Items)
                if (i.Page == p)
                    return true;
        return false;
    }

    private void BuildNav(FieldZone zone, HashSet<uint> collected)
    {
        this.nav.Clear();
        var op = FieldZones.OpOf(zone);
        var all = this.plugin.Config.ShowAllZones;
        var here = FieldZones.DisplayName(zone).ToUpperInvariant();

        var (ceUp, ceTotal, ceJoin) = this.CeSummary();
        string? ceBadge = ceJoin > 0 ? $"{ceJoin} JOIN" : ceTotal > 0 ? $"{ceUp}/{ceTotal}" : null;
        Vector4? ceColor = ceJoin > 0 ? MercuryTheme.Amber : null;
        var fatesUp = this.ActiveFateCount();
        string? fateBadge = fatesUp > 0 ? $"{fatesUp} up" : null;

        switch (op)
        {
            case FieldOp.Occult:
            {
                var items = new List<NavItem>
                {
                    new(Page.Overview, "Overview", FontAwesomeIcon.ThLarge),
                    new(Page.Ces, "Critical Encounters", FontAwesomeIcon.Bolt, ceBadge, ceColor),
                    new(Page.Fates, "FATEs", FontAwesomeIcon.Flag, fateBadge),
                    new(Page.Pots, "Pots", FontAwesomeIcon.Box),
                };
                if (zone == FieldZone.NorthHorn)
                    items.Add(new(Page.Weather, "Weather", FontAwesomeIcon.Cloud));
                items.Add(new(Page.Map, "Map", FontAwesomeIcon.Map));
                this.nav.Add(new NavGroup(here, true, items));
                break;
            }

            case FieldOp.Eureka:
                this.nav.Add(new NavGroup(here, true, new List<NavItem>
                {
                    new(Page.Overview, "Overview", FontAwesomeIcon.ThLarge),
                    new(Page.Fates, "Notorious Monsters", FontAwesomeIcon.Skull, fateBadge, fatesUp > 0 ? MercuryTheme.Amber : null),
                    new(Page.Weather, "Weather", FontAwesomeIcon.Cloud),
                }));
                break;

            case FieldOp.Bozja:
                this.nav.Add(new NavGroup(here, true, new List<NavItem>
                {
                    new(Page.Overview, "Overview", FontAwesomeIcon.ThLarge),
                    new(Page.Ces, "Critical Engagements", FontAwesomeIcon.Bolt, ceBadge, ceColor),
                    new(Page.Fates, "Skirmishes", FontAwesomeIcon.Flag, fateBadge),
                    new(Page.Weather, "Weather", FontAwesomeIcon.Cloud),
                }));
                break;
        }

        if (op == FieldOp.Occult || all)
        {
            var records = this.plugin.RecordService.Records;
            var have = 0;
            foreach (var r in records)
                if (collected.Contains(r.Id))
                    have++;
            var quests = this.plugin.RecordService.Quests;
            var questsDone = 0;
            foreach (var q in quests)
                if (this.plugin.RecordService.IsQuestComplete(q.RowId))
                    questsDone++;
            var log = this.plugin.ChallengeLog.For(FieldOp.Occult);
            var logDone = this.ChallengesDone(log);
            var blu = 0;
            foreach (var s in PhantomBlueMage.Spells)
                if (this.plugin.Config.LearnedBlueSpells.Contains(s.Name))
                    blu++;

            var items = new List<NavItem>
            {
                new(Page.Records, "Records", FontAwesomeIcon.Book, $"{have}/{records.Count}"),
                new(Page.Quests, "Quests", FontAwesomeIcon.Tasks, $"{questsDone}/{quests.Count}"),
                new(Page.ChallengeLog, "Field Ops Log", FontAwesomeIcon.ClipboardList, log.Count > 0 ? $"{logDone}/{log.Count}" : null),
                new(Page.PhantomBlu, "Phantom BLU", FontAwesomeIcon.Magic, $"{blu}/{PhantomBlueMage.Spells.Count}"),
                new(Page.Martial, "Martial Memories", FontAwesomeIcon.FistRaised),
            };

            // The map lives with the zone pages while you're in the Occult Crescent, here otherwise.
            if (op != FieldOp.Occult)
                items.Add(new(Page.Map, "Map", FontAwesomeIcon.Map));
            this.nav.Add(new NavGroup("OCCULT CRESCENT", false, items));
        }

        if (op == FieldOp.Eureka || all)
        {
            var log = this.plugin.ChallengeLog.For(FieldOp.Eureka);
            this.nav.Add(new NavGroup("EUREKA", false, new List<NavItem>
            {
                new(Page.EurekaStory, "Story & Unlocks", FontAwesomeIcon.Scroll, $"{QuestChains.DoneCount(QuestChains.Eureka)}/{QuestChains.Eureka.Count}"),
                new(Page.EurekaLog, "Challenge Log", FontAwesomeIcon.ClipboardList, log.Count > 0 ? $"{this.ChallengesDone(log)}/{log.Count}" : null),
            }));
        }

        if (op == FieldOp.Bozja || all)
        {
            this.nav.Add(new NavGroup("SAVE THE QUEEN", false, new List<NavItem>
            {
                new(Page.BozjaStory, "Story & Unlocks", FontAwesomeIcon.Scroll, $"{QuestChains.DoneCount(QuestChains.Bozja)}/{QuestChains.Bozja.Count}"),
            }));
        }

        this.nav.Add(new NavGroup("ALL FIELD OPS", false, new List<NavItem>
        {
            new(Page.Progress, "Progress", FontAwesomeIcon.ChartLine),
        }));
    }

    private void DrawHeader(FieldZone zone)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = 46f * scale;
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetWindowFontScale(1.35f);
        dl.AddText(origin + new Vector2(2f, 1f) * scale, ImGui.GetColorU32(MercuryTheme.Accent), "MERCURY");
        var wordmark = ImGui.CalcTextSize("MERCURY");
        ImGui.SetWindowFontScale(0.82f);
        dl.AddText(new Vector2(origin.X + 2f * scale, origin.Y + 3f * scale + wordmark.Y), ImGui.GetColorU32(MercuryTheme.TextDim), "Field operations companion");
        ImGui.SetWindowFontScale(1f);

        // Right side, laid out right to left: the status pills.
        var x = origin.X + width;
        var pillY = origin.Y + 9f * scale;

        var pills = new List<(string Label, Vector4 Color)>();
        var op = FieldZones.OpOf(zone);
        pills.Add(op == FieldOp.None ? ("NOT IN A FIELD OP", MercuryTheme.Grey) : (FieldZones.DisplayName(zone).ToUpperInvariant(), MercuryTheme.Accent));

        var p = this.plugin.Progress.Current;
        switch (op)
        {
            case FieldOp.Eureka when p.ElementalLevel > 0:
                pills.Add(($"Elemental Lv {p.ElementalLevel}", MercuryTheme.TextSecondary));
                break;
            case FieldOp.Bozja when p.ResistanceRank > 0:
                pills.Add(($"Resistance Rank {p.ResistanceRank}", MercuryTheme.TextSecondary));
                break;
            case FieldOp.Occult:
                if (p.KnowledgeLevel > 0)
                    pills.Add(($"Knowledge Lv {p.KnowledgeLevel}", MercuryTheme.TextSecondary));
                if (this.plugin.RecordService.GetInstanceTimer() is { } t)
                    pills.Add(($"Instance {FormatTime(t.Remaining)} left", MercuryTheme.TextSecondary));
                break;
        }

        for (var i = pills.Count - 1; i >= 0; i--)
        {
            var size = Pill.Measure(pills[i].Label);
            x -= size.X;
            if (x < origin.X + wordmark.X + 20f * scale)
                break; // narrow window: drop pills rather than overlap the wordmark
            Pill.DrawAt(new Vector2(x, pillY), pills[i].Label, pills[i].Color);
            x -= 6f * scale;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
        var y = origin.Y + height - 6f * scale;
        dl.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + width, y), ImGui.GetColorU32(MercuryTheme.Hairline), 1f);
    }

    private void DrawSidebar()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var anyHere = false;
        foreach (var g in this.nav)
        {
            MercuryTheme.VSpace(4f);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * scale);
            ImGui.SetWindowFontScale(0.85f);
            if (g.Here)
            {
                anyHere = true;
                MercuryTheme.Text(g.Label, MercuryTheme.Accent);
            }
            else
            {
                MercuryTheme.Text(g.Label, MercuryTheme.TextDim);
            }

            ImGui.SetWindowFontScale(1f);
            foreach (var item in g.Items)
            {
                if (SidebarTab.Draw(item.Label, item.Icon, this.page == item.Page, item.Badge, item.BadgeColor))
                    this.page = item.Page;
            }
        }

        if (!anyHere)
        {
            MercuryTheme.VSpace(10f);
            ImGui.Indent(6f * scale);
            MercuryTheme.TextWrapped("Zone pages show up here when you enter Eureka, Bozja, Zadnor or the Occult Crescent.", MercuryTheme.TextDim);
            ImGui.Unindent(6f * scale);
        }

        // Settings sits at the bottom.
        var tabH = 32f * scale;
        var spare = ImGui.GetContentRegionAvail().Y - tabH - 4f * scale;
        if (spare > 0)
            ImGui.Dummy(new Vector2(0, spare));
        if (SidebarTab.Draw("Settings", FontAwesomeIcon.Cog, this.page == Page.Settings))
            this.page = Page.Settings;
    }

    private void DrawPage(FieldZone zone, HashSet<uint> collected)
    {
        var op = FieldZones.OpOf(zone);
        var map = MapCatalog.FromTerritory(Service.ClientState.TerritoryType);

        switch (this.page)
        {
            case Page.Overview:
                switch (op)
                {
                    case FieldOp.Occult:
                        this.DrawOccultOverview(zone, collected);
                        break;
                    case FieldOp.Eureka:
                        this.DrawEurekaOverview(zone);
                        break;
                    case FieldOp.Bozja:
                        this.DrawBozjaOverview(zone);
                        break;
                }

                break;

            case Page.Ces:
                MercuryTheme.PageHeader(op == FieldOp.Bozja ? "Critical Engagements" : "Critical Encounters", FieldZones.DisplayName(zone));
                this.DrawCriticalEncounters();
                break;

            case Page.Fates:
                MercuryTheme.PageHeader(
                    op switch { FieldOp.Eureka => "Notorious Monsters", FieldOp.Bozja => "Skirmishes", _ => "FATEs" },
                    op == FieldOp.Eureka ? "Notorious monsters respawn about 2 hours after they're defeated" : FieldZones.DisplayName(zone));
                this.DrawFates();
                break;

            case Page.Pots:
                MercuryTheme.PageHeader("Pots", FieldZones.DisplayName(zone));
                this.DrawInstanceTimer(map);
                this.DrawPots();
                break;

            case Page.Weather:
                if (zone == FieldZone.NorthHorn)
                {
                    MercuryTheme.PageHeader("Weather", "North Horn weather and its weather mobs, in your local time");
                    this.DrawSearchBox("Search weather mobs...");
                    this.DrawWeatherSearchResult();
                    this.DrawWeather();
                }
                else
                {
                    MercuryTheme.PageHeader("Weather", $"{FieldZones.DisplayName(zone)} forecast, in your local time");
                    this.DrawZoneForecast(zone, 18);
                }

                break;

            case Page.Map:
                MercuryTheme.PageHeader("Map", "Occult Crescent survey points, encounters and aetherytes");
                this.DrawMap(collected);
                break;

            case Page.Records:
                this.DrawRecordsPage(collected);
                break;

            case Page.Quests:
                MercuryTheme.PageHeader("Occult Crescent quests", "The story quest line, read live from the game");
                this.DrawSearchBox("Search quests...");
                this.DrawQuests(this.plugin.RecordService.Quests, collected);
                break;

            case Page.ChallengeLog:
                MercuryTheme.PageHeader("Field Ops Challenge Log", "This week's Occult Crescent entries");
                this.DrawSearchBox("Search entries...");
                this.DrawChallengeLog(FieldOp.Occult);
                break;

            case Page.EurekaLog:
                MercuryTheme.PageHeader("Eureka Challenge Log", "This week's Eureka entries");
                this.DrawSearchBox("Search entries...");
                this.DrawChallengeLog(FieldOp.Eureka);
                break;

            case Page.PhantomBlu:
                MercuryTheme.PageHeader("Phantom Blue Mage", "Spells you've marked as learned");
                this.DrawPhantomBlue();
                break;

            case Page.Martial:
                MercuryTheme.PageHeader("Martial Memories", "Phantom weapon objectives");
                this.DrawSearchBox("Search objectives...");
                this.DrawMartial();
                break;

            case Page.EurekaStory:
                this.DrawChainPage("Eureka story & unlocks", QuestChains.Eureka, this.plugin.Progress.Current.ElementalLevel, "Elemental Lv",
                    "These are the four zone-unlock quests. The story steps with Krile inside each zone aren't real quests, so Mercury can't track them.");
                break;

            case Page.BozjaStory:
                this.DrawChainPage("Save the Queen story & unlocks", QuestChains.Bozja, this.plugin.Progress.Current.ResistanceRank, "Rank",
                    "The main Bozja and Zadnor story. Resistance weapon quests aren't listed.");
                break;

            case Page.Progress:
                this.DrawProgressPage(collected);
                break;

            case Page.Settings:
                this.DrawSettingsPage();
                break;
        }
    }

    // A search box for pages that filter by text. All of them share the one search string.
    private void DrawSearchBox(string hint)
    {
        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##search", hint, ref this.search, 128);
        ImGui.Spacing();
    }

    // (active, total roster, open for registration) among the zone's CEs.
    private unsafe (int Up, int Total, int Joinable) CeSummary()
    {
        var container = FieldEvents.Container();
        if (container is null)
            return (0, 0, 0);

        int up = 0, total = 0, join = 0;
        var events = container->Events;
        for (var i = 0; i < events.Length; i++)
        {
            ref var e = ref events[i];
            var named = !string.IsNullOrEmpty(e.Name.ToString());
            if (e.State != DynamicEventState.Inactive)
            {
                up++;
                total++;
                if (e.State == DynamicEventState.Register)
                    join++;
            }
            else if (named)
            {
                total++;
            }
        }

        return (up, total, join);
    }

    private void DrawRecordsPage(HashSet<uint> collected)
    {
        this.acquired.Clear();
        this.missing.Clear();
        foreach (var r in this.plugin.RecordService.Records)
        {
            if (collected.Contains(r.Id))
                this.acquired.Add(r);
            else
                this.missing.Add(r);
        }

        var total = this.plugin.RecordService.Records.Count;
        MercuryTheme.PageHeader("Occult Records", $"{this.acquired.Count} of {total} collected");
        MercuryTheme.Bar(total == 0 ? 0f : (float)this.acquired.Count / total, ImGui.GetContentRegionAvail().X);
        MercuryTheme.VSpace(6f);

        ImGui.SetNextItemWidth(240f * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##search", "Search records...", ref this.search, 128);
        ImGui.SameLine();
        var showDesc = this.plugin.Config.ShowDescriptions;
        if (ImGui.Checkbox("Descriptions", ref showDesc))
        {
            this.plugin.Config.ShowDescriptions = showDesc;
            this.plugin.Config.Save();
        }

        ImGui.Spacing();
        if (!ImGui.BeginTabBar("##recordTabs"))
            return;

        if (ImGui.BeginTabItem($"Missing ({this.missing.Count})###missing"))
        {
            DrawSyncHint();
            this.DrawTable("##tblMissing", this.missing, showDesc, "You have collected every record.");
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"Acquired ({this.acquired.Count})###acquired"))
        {
            DrawSyncHint();
            this.DrawTable("##tblAcquired", this.acquired, showDesc, "No records collected yet.");
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawOccultOverview(FieldZone zone, HashSet<uint> collected)
    {
        var p = this.plugin.Progress.Current;
        MercuryTheme.PageHeader(FieldZones.DisplayName(zone),
            p.KnowledgeLevel > 0 ? $"Knowledge Lv {p.KnowledgeLevel} · read live from the game" : "Read live from the game");

        var records = this.plugin.RecordService.Records;
        var have = 0;
        foreach (var r in records)
            if (collected.Contains(r.Id))
                have++;
        var (ceUp, _, ceJoin) = this.CeSummary();
        var timer = this.plugin.RecordService.GetInstanceTimer();

        StatTile.Row(
            ("Records", $"{have} / {records.Count}", $"{records.Count - have} missing", records.Count == 0 ? 0f : (float)have / records.Count),
            ("Critical encounters", ceUp == 1 ? "1 active" : $"{ceUp} active", ceJoin > 0 ? $"{ceJoin} open to join now" : "none open to join", null),
            ("FATEs", $"{this.ActiveFateCount()} up", $"{this.plugin.Config.AlertFates.Count} on your watch list", null),
            ("Instance", timer is { } t ? FormatTime(t.Remaining) : "-", timer is { } t2 ? $"{FormatTime(t2.Elapsed)} elapsed" : "waiting for game data", null));

        MercuryTheme.VSpace(4f);
        if (zone == FieldZone.NorthHorn)
        {
            var now = DateTimeOffset.UtcNow;
            var w = OccultWeatherForecast.At(now);
            var left = (uint)Math.Max(0, Math.Ceiling((w.End - now).TotalSeconds));
            MercuryTheme.SectionHeader("Weather");
            MercuryTheme.Text($"{w.Pattern.Name} · changes in {FormatTime(left)}", MercuryTheme.TextPrimary);
            if (w.Pattern.Mob is { } mob)
                MercuryTheme.Text($"{mob} is up now", MercuryTheme.Green);
        }

        MercuryTheme.SectionHeader("Watching");
        this.DrawWatchPanel();

        MercuryTheme.SectionHeader("Pots");
        this.DrawInstanceTimer(MapCatalog.FromTerritory(Service.ClientState.TerritoryType));
        this.DrawPots();
    }

    private void DrawSettingsPage()
    {
        var cfg = this.plugin.Config;
        MercuryTheme.PageHeader("Settings", "Saved as you change them");

        using (var g = SettingsGroup.Begin("Pages"))
        {
            var all = cfg.ShowAllZones;
            if (g.Toggle("Show pages for other zones", "Off: the sidebar lists only the zone you're in, plus Progress. On: every field op's collection and story pages are always listed.", ref all))
            {
                cfg.ShowAllZones = all;
                cfg.Save();
            }

            var desc = cfg.ShowDescriptions;
            if (g.Toggle("Show record descriptions inline", "May contain lore spoilers. Off: hover a record to read it.", ref desc))
            {
                cfg.ShowDescriptions = desc;
                cfg.Save();
            }
        }

        using (var g = SettingsGroup.Begin("Alerts"))
        {
            var alerts = cfg.FateAlertsEnabled;
            if (g.Toggle("Alert when a watched FATE or CE starts", "A chat line and a toast with sound. Right-click a FATE or CE to watch it.", ref alerts))
            {
                cfg.FateAlertsEnabled = alerts;
                cfg.Save();
            }

            var any = cfg.AlertOnAnyFate;
            if (g.Toggle("Alert on every FATE", "Noisy: alerts for every FATE, not just watched ones.", ref any))
            {
                cfg.AlertOnAnyFate = any;
                cfg.Save();
            }

            var panel = cfg.ShowWatchPanel;
            if (g.Toggle("Show watched encounters on the overview", null, ref panel))
            {
                cfg.ShowWatchPanel = panel;
                cfg.Save();
            }

            var scan = cfg.ScanAlertEnabled;
            if (g.Toggle("Enemy scan alert", "Beeps and prints map coordinates when an enemy whose name contains the text below loads in nearby.", ref scan))
            {
                cfg.ScanAlertEnabled = scan;
                cfg.Save();
            }

            g.Row("Enemy to scan for", null, 200f, () =>
            {
                ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
                var name = cfg.ScanAlertName;
                if (ImGui.InputTextWithHint("##scanName", "e.g. Crescent Fool", ref name, 64))
                {
                    cfg.ScanAlertName = name;
                    cfg.Save();
                }
            });
        }

        using (var g = SettingsGroup.Begin("Edge dock"))
        {
            var dock = cfg.EdgeDockEnabled;
            if (g.Toggle("Show the Mercury tab on the screen edge", "Point at the tab to pop Mercury out; click it to keep it out. Drag the tab to any edge. Off hides Mercury completely; type /mercury to bring it back.", ref dock))
            {
                cfg.EdgeDockEnabled = dock;
                cfg.Save();
            }

            g.Row("Screen edge", null, 110f, () =>
            {
                ImGui.SetNextItemWidth(110f * ImGuiHelpers.GlobalScale);
                if (ImGui.BeginCombo("##dockEdge", cfg.EdgeDockEdge.ToString()))
                {
                    foreach (var e in Enum.GetValues<DockEdge>())
                    {
                        if (ImGui.Selectable(e.ToString(), e == cfg.EdgeDockEdge))
                        {
                            cfg.EdgeDockEdge = e;
                            cfg.Save();
                        }
                    }

                    ImGui.EndCombo();
                }
            });

            g.Row("Tab position", "Puts the tab back on the right edge, in the middle.", 110f, () =>
            {
                if (ImGui.Button("Reset position", new Vector2(110f * ImGuiHelpers.GlobalScale, 0)))
                {
                    cfg.ResetEdgeDockPlacement();
                    cfg.Save();
                }
            });
        }
    }
}
