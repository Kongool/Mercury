using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Mercury.Windows.Components;

namespace Mercury.Windows;

// Eureka, Bozja, story-chain and Progress pages.
public sealed partial class MainWindow
{
    private const ImGuiTableFlags ListTable = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;

    private static string Ago(long unix)
    {
        if (unix <= 0)
            return "never";
        var s = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix;
        return s switch
        {
            < 90 => "just now",
            < 3600 => $"{s / 60} min ago",
            < 172800 => $"{s / 3600} h ago",
            _ => $"{s / 86400} days ago",
        };
    }

    private void DrawEurekaOverview(FieldZone zone)
    {
        var p = this.plugin.Progress.Current;
        var (min, cap) = FieldZones.EurekaRange(zone);
        var lv = p.ElementalLevel;
        MercuryTheme.PageHeader(FieldZones.DisplayName(zone),
            lv > 0 ? $"Elemental Lv {lv} · zone range {min}–{cap} · read live from the game" : $"Zone range {min}–{cap} · waiting for game data");

        var now = DateTimeOffset.UtcNow;
        var weather = ZoneWeather.Upcoming(FieldZones.WeatherRate(zone), now, 1);
        var next = QuestChains.Next(QuestChains.Eureka);
        var fatesUp = this.ActiveFateCount();

        StatTile.Row(
            ("Elemental level", lv > 0 ? lv.ToString() : "-", lv >= 60 ? "max level" : $"{p.ElementalExpFraction * 100f:0}% to {lv + 1}", lv >= 60 ? 1f : p.ElementalExpFraction),
            ("Weather", weather.Count > 0 ? weather[0].Name : "-", weather.Count > 0 ? $"changes in {FormatTime((uint)Math.Max(0, (weather[0].End - now).TotalSeconds))}" : null, null),
            ("Notorious monsters", $"{fatesUp} up", "respawn ~2 h after defeat", null),
            ("Next unlock", next is null ? "All open" : next.Unlocks, next is null ? "every Eureka zone is unlocked" : $"Elemental Lv {next.MinLevel} · {QuestChains.Name(next.QuestId)}", null));

        MercuryTheme.VSpace(4f);
        if (ImGui.BeginTable("##eurekaCols", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            MercuryTheme.SectionHeader("Weather forecast");
            this.DrawZoneForecast(zone, 5);

            ImGui.TableNextColumn();
            MercuryTheme.SectionHeader("Elemental wheel");
            if (FieldProgress.Eureka() is { } s)
            {
                MercuryTheme.Text($"Fire {s.Fire}   Ice {s.Ice}   Wind {s.Wind}", MercuryTheme.TextPrimary);
                MercuryTheme.Text($"Earth {s.Earth}   Lightning {s.Lightning}   Water {s.Water}", MercuryTheme.TextPrimary);
                MercuryTheme.Text($"Magicite {s.Magicite}", MercuryTheme.TextDim);
            }
            else
            {
                MercuryTheme.Text("Waiting for game data", MercuryTheme.TextDim);
            }

            if (next is not null)
            {
                MercuryTheme.SectionHeader("Next unlock");
                MercuryTheme.Text(QuestChains.Name(next.QuestId), MercuryTheme.TextPrimary);
                MercuryTheme.TextWrapped($"{next.Where} · needs Elemental Lv {next.MinLevel}", lv >= next.MinLevel ? MercuryTheme.Green : MercuryTheme.TextDim);
            }

            ImGui.EndTable();
        }

        MercuryTheme.SectionHeader("Notorious monsters");
        this.DrawFates();
    }

    private void DrawBozjaOverview(FieldZone zone)
    {
        var p = this.plugin.Progress.Current;
        var rank = p.ResistanceRank;
        MercuryTheme.PageHeader(FieldZones.DisplayName(zone),
            rank > 0 ? $"Resistance Rank {rank} of 25 · read live from the game" : "Waiting for game data");

        var (ceUp, _, ceJoin) = this.CeSummary();
        var next = QuestChains.Next(QuestChains.Bozja);
        var mettle = FieldProgress.Mettle();

        StatTile.Row(
            ("Resistance rank", rank > 0 ? rank.ToString() : "-", rank >= 25 ? "max rank" : mettle is { Needed: > 0 } m ? $"{m.Current:N0} / {m.Needed:N0} Mettle" : $"{p.MettleFraction * 100f:0}% to {rank + 1}", rank >= 25 ? 1f : p.MettleFraction),
            ("Critical engagements", ceUp == 1 ? "1 active" : $"{ceUp} active", ceJoin > 0 ? $"{ceJoin} open to join now" : "none open to join", null),
            ("Skirmishes", $"{this.ActiveFateCount()} up", null, null),
            ("Next quest", next is null ? "All done" : $"Rank {Math.Max(1, next.MinLevel)}", next is null ? "Save the Queen story complete" : QuestChains.Name(next.QuestId), null));

        MercuryTheme.VSpace(4f);
        if (ImGui.BeginTable("##bozjaCols", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            MercuryTheme.SectionHeader("Weather forecast");
            this.DrawZoneForecast(zone, 5);

            ImGui.TableNextColumn();
            MercuryTheme.SectionHeader("Unlocks");
            foreach (var step in QuestChains.Bozja)
            {
                if (step.Unlocks.Length == 0)
                    continue;
                var done = QuestChains.Done(step);
                MercuryTheme.Text(done ? "●" : "○", done ? MercuryTheme.Green : MercuryTheme.Grey);
                ImGui.SameLine();
                MercuryTheme.Text(step.Unlocks, done ? MercuryTheme.TextPrimary : MercuryTheme.TextDim);
            }

            ImGui.EndTable();
        }

        MercuryTheme.SectionHeader("Critical engagements");
        this.DrawCriticalEncounters();
    }

    /// <summary>The next <paramref name="count"/> weather periods for a zone, in local time.</summary>
    private void DrawZoneForecast(FieldZone zone, int count)
    {
        var now = DateTimeOffset.UtcNow;
        var windows = ZoneWeather.Upcoming(FieldZones.WeatherRate(zone), now, count);
        if (windows.Count == 0)
        {
            MercuryTheme.Text("No forecast for this zone.", MercuryTheme.TextDim);
            return;
        }

        if (!ImGui.BeginTable("##zoneForecast", 3, ListTable))
            return;

        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed, 110f * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Weather", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Chance", ImGuiTableColumnFlags.WidthFixed, 52f * ImGuiHelpers.GlobalScale);

        foreach (var w in windows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var current = w.Start <= now && now < w.End;
            if (current)
                MercuryTheme.Text($"Now · {FormatTime((uint)Math.Max(0, (w.End - now).TotalSeconds))}", MercuryTheme.Accent);
            else
                MercuryTheme.Text(w.Start.ToLocalTime().ToString("ddd h:mm tt"), MercuryTheme.TextSecondary);
            ImGui.TableNextColumn();
            MercuryTheme.Text(w.Name, current ? MercuryTheme.TextPrimary : MercuryTheme.TextSecondary);
            ImGui.TableNextColumn();
            MercuryTheme.Text($"{w.Chance}%", MercuryTheme.TextDim);
        }

        ImGui.EndTable();
    }

    private void DrawChainPage(string title, IReadOnlyList<ChainStep> chain, int level, string levelLabel, string note)
    {
        var done = QuestChains.DoneCount(chain);
        MercuryTheme.PageHeader(title, level > 0 ? $"{done} of {chain.Count} done · {levelLabel} {level}" : $"{done} of {chain.Count} done");
        MercuryTheme.TextWrapped(note, MercuryTheme.TextDim);
        ImGui.Spacing();

        var next = QuestChains.Next(chain);
        if (!ImGui.BeginTable("##chain", 4, ListTable | ImGuiTableFlags.ScrollY))
            return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 22f * scale);
        ImGui.TableSetupColumn("Quest", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Needs", ImGuiTableColumnFlags.WidthFixed, 90f * scale);
        ImGui.TableSetupColumn("Where / unlocks", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();

        foreach (var step in chain)
        {
            var isDone = QuestChains.Done(step);
            var isNext = ReferenceEquals(step, next);
            var ready = level >= step.MinLevel;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (isDone)
                    MercuryTheme.Text(FontAwesomeIcon.Check.ToIconString(), MercuryTheme.Green);
                else if (isNext)
                    MercuryTheme.Text(FontAwesomeIcon.ChevronRight.ToIconString(), MercuryTheme.Accent);
                else
                    MercuryTheme.Text(FontAwesomeIcon.Lock.ToIconString(), MercuryTheme.TextMuted);
            }

            ImGui.TableNextColumn();
            MercuryTheme.Text(QuestChains.Name(step.QuestId), isNext ? MercuryTheme.Accent : isDone ? MercuryTheme.TextSecondary : MercuryTheme.TextPrimary);

            ImGui.TableNextColumn();
            if (step.MinLevel <= 0)
                MercuryTheme.Text("-", MercuryTheme.TextDim);
            else
                MercuryTheme.Text($"{levelLabel} {step.MinLevel}", isDone ? MercuryTheme.TextDim : ready ? MercuryTheme.Green : MercuryTheme.Amber);

            ImGui.TableNextColumn();
            MercuryTheme.TextWrapped(step.Where, MercuryTheme.TextDim);
            if (step.Unlocks.Length > 0)
                MercuryTheme.Text($"Unlocks {step.Unlocks}", isDone ? MercuryTheme.TextDim : MercuryTheme.TextSecondary);
        }

        ImGui.EndTable();
    }

    private void DrawProgressPage(HashSet<uint> collected)
    {
        var p = this.plugin.Progress.Current;
        MercuryTheme.PageHeader("Progress", "Levels are remembered from the last time you were in each zone · quests are read live");

        if (ImGui.BeginTable("##progressCards", 3, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableNextColumn();
            this.DrawEurekaCard(p);
            ImGui.TableNextColumn();
            this.DrawBozjaCard(p);
            ImGui.TableNextColumn();
            this.DrawOccultCard(p, collected);
            ImGui.EndTable();
        }

        MercuryTheme.SectionHeader("Next steps");
        if (!ImGui.BeginTable("##nextSteps", 3, ListTable))
            return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupColumn("Op", ImGuiTableColumnFlags.WidthFixed, 110f * scale);
        ImGui.TableSetupColumn("Quest", ImGuiTableColumnFlags.WidthStretch);
        // Wide enough for the longest "Needs" text, e.g. "Elemental Lv 60 · you're 59".
        var needsWidth = Math.Max(150f * scale, ImGui.CalcTextSize("Elemental Lv 60 · you're 59").X + (12f * scale));
        ImGui.TableSetupColumn("Needs", ImGuiTableColumnFlags.WidthFixed, needsWidth);

        NextRow("SAVE THE QUEEN", QuestChains.Next(QuestChains.Bozja), p.ResistanceRank, "Rank");
        NextRow("EUREKA", QuestChains.Next(QuestChains.Eureka), p.ElementalLevel, "Elemental Lv");

        OccultQuest? occultNext = null;
        foreach (var q in this.plugin.RecordService.Quests)
        {
            if (!this.plugin.RecordService.IsQuestComplete(q.RowId))
            {
                occultNext = q;
                break;
            }
        }

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        MercuryTheme.Text("OCCULT", MercuryTheme.TextDim);
        ImGui.TableNextColumn();
        if (occultNext is null)
        {
            MercuryTheme.Text("Occult Crescent quests complete", MercuryTheme.TextSecondary);
        }
        else
        {
            MercuryTheme.Text(occultNext.Name, MercuryTheme.TextPrimary);
            MercuryTheme.Text(occultNext.IsNorthHorn ? "North Horn" : "South Horn", MercuryTheme.TextDim);
        }

        ImGui.TableNextColumn();
        MercuryTheme.Text(occultNext is null ? "-" : "see the Quests page", MercuryTheme.TextDim);

        ImGui.EndTable();

        static void NextRow(string op, ChainStep? step, int level, string label)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            MercuryTheme.Text(op, MercuryTheme.TextDim);
            ImGui.TableNextColumn();
            if (step is null)
            {
                MercuryTheme.Text("Story complete", MercuryTheme.TextSecondary);
                ImGui.TableNextColumn();
                MercuryTheme.Text("-", MercuryTheme.TextDim);
                return;
            }

            MercuryTheme.Text(QuestChains.Name(step.QuestId), MercuryTheme.TextPrimary);
            MercuryTheme.TextWrapped(step.Unlocks.Length > 0 ? $"{step.Where} · unlocks {step.Unlocks}" : step.Where, MercuryTheme.TextDim);
            ImGui.TableNextColumn();
            if (step.MinLevel <= 0)
                MercuryTheme.Text("Ready now", MercuryTheme.Green);
            else if (level >= step.MinLevel)
                MercuryTheme.Text($"{label} {step.MinLevel} · ready", MercuryTheme.Green);
            else
                MercuryTheme.Text(level > 0 ? $"{label} {step.MinLevel} · you're {level}" : $"{label} {step.MinLevel}", MercuryTheme.Amber);
        }
    }

    private static void CardTop(string title, string big, string caption, float fraction, string seen)
    {
        MercuryTheme.SectionLabel(title);
        MercuryTheme.TextScaled(big, MercuryTheme.TextPrimary, 1.3f);
        MercuryTheme.TextWrapped(caption, MercuryTheme.TextDim);
        MercuryTheme.Bar(fraction, ImGui.GetContentRegionAvail().X);
        MercuryTheme.TextWrapped(seen, MercuryTheme.TextDim);
        MercuryTheme.VSpace(4f);
    }

    // The status sits at the right edge of the name line. In a narrow column it moves to the detail
    // line, or to a line of its own, instead of running past the edge.
    private static void StatusRow(string name, string detail, string status, Vector4 color)
    {
        var indent = 16f * ImGuiHelpers.GlobalScale;
        MercuryTheme.Text("●", color);
        ImGui.SameLine();
        MercuryTheme.Text(name, MercuryTheme.TextPrimary);
        var placed = TryRightAlign(status, color);

        ImGui.Indent(indent);
        if (detail.Length > 0)
        {
            MercuryTheme.Text(detail, MercuryTheme.TextDim);
            placed = placed || TryRightAlign(status, color);
        }

        if (!placed)
            MercuryTheme.Text(status, color);
        ImGui.Unindent(indent);
    }

    // Draws text at the right edge of the current line if it fits there; otherwise moves to a new line
    // and returns false.
    private static bool TryRightAlign(string text, Vector4 color)
    {
        ImGui.SameLine();
        var w = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail < w + (8f * ImGuiHelpers.GlobalScale))
        {
            ImGui.NewLine();
            return false;
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - w);
        MercuryTheme.Text(text, color);
        return true;
    }

    private void DrawEurekaCard(CharacterProgress p)
    {
        var lv = p.ElementalLevel;
        var where = FieldZones.DisplayName(FieldZones.FromTerritory(p.ElementalTerritory));
        CardTop("Eureka", lv > 0 ? $"Lv {lv}" : "Not seen yet", "Elemental Level · cap 60", lv / 60f,
            lv > 0 ? $"Last seen in {where} · {Ago(p.ElementalSeenUnix)}" : "Enter Eureka once to record your level");

        for (var i = 0; i < FieldZones.EurekaZones.Length; i++)
        {
            var zone = FieldZones.EurekaZones[i];
            var (min, cap) = FieldZones.EurekaRange(zone);
            var unlocked = QuestChains.Done(QuestChains.Eureka[i]);
            var (status, color) = !unlocked ? ("LOCKED", MercuryTheme.Grey)
                : lv >= cap ? ("DONE", MercuryTheme.Green)
                : lv >= min ? ("IN PROGRESS", MercuryTheme.Accent)
                : ("OPEN", MercuryTheme.Green);
            StatusRow(FieldZones.DisplayName(zone).Replace("Eureka ", string.Empty), $"Lv {min}–{cap}", status, color);
        }

        var hydatos = QuestChains.Done(QuestChains.Eureka[^1]);
        StatusRow("Baldesion Arsenal", "Hydatos story + Lv 60", hydatos && lv >= 60 ? "LV 60 REACHED" : "LOCKED", hydatos && lv >= 60 ? MercuryTheme.Green : MercuryTheme.Grey);
    }

    private void DrawBozjaCard(CharacterProgress p)
    {
        var rank = p.ResistanceRank;
        var where = FieldZones.DisplayName(FieldZones.FromTerritory(p.BozjaTerritory));
        CardTop("Save the Queen", rank > 0 ? $"Rank {rank}" : "Not seen yet", "Resistance Rank · cap 25", rank / 25f,
            rank > 0 ? $"Last seen in {where} · {Ago(p.BozjaSeenUnix)}" : "Enter Bozja once to record your rank");

        foreach (var step in QuestChains.Bozja)
        {
            // Only the steps that open new content; the Lost Actions / plateau ones live on the story page.
            if (step.Unlocks.Length == 0 || step.Unlocks.Contains("area") || step.Unlocks.Contains("plateau") || step.Unlocks == "Lost Actions")
                continue;

            var done = QuestChains.Done(step);
            var (status, color) = done ? ("UNLOCKED", MercuryTheme.Green)
                : rank >= step.MinLevel ? ("READY", MercuryTheme.Amber)
                : ($"RANK {step.MinLevel}", MercuryTheme.Grey);
            StatusRow(step.Unlocks, QuestChains.Name(step.QuestId), status, color);
        }
    }

    private void DrawOccultCard(CharacterProgress p, HashSet<uint> collected)
    {
        var records = this.plugin.RecordService.Records;
        var have = 0;
        foreach (var r in records)
            if (collected.Contains(r.Id))
                have++;

        var where = FieldZones.DisplayName(FieldZones.FromTerritory(p.OccultTerritory));
        CardTop("Occult Crescent", p.KnowledgeLevel > 0 ? $"Lv {p.KnowledgeLevel}" : "Not seen yet", $"Knowledge Level · records {have} / {records.Count}",
            records.Count == 0 ? 0f : (float)have / records.Count,
            p.KnowledgeLevel > 0 ? $"Last seen in {where} · {Ago(p.OccultSeenUnix)}" : "Enter the Occult Crescent once to record your level");

        var quests = this.plugin.RecordService.Quests;
        var questsDone = 0;
        foreach (var q in quests)
            if (this.plugin.RecordService.IsQuestComplete(q.RowId))
                questsDone++;
        var blu = 0;
        foreach (var s in PhantomBlueMage.Spells)
            if (this.plugin.Config.LearnedBlueSpells.Contains(s.Name))
                blu++;

        StatusRow("Records", string.Empty, $"{have} / {records.Count}", have == records.Count ? MercuryTheme.Green : MercuryTheme.Accent);
        StatusRow("Quests", string.Empty, $"{questsDone} / {quests.Count}", questsDone == quests.Count ? MercuryTheme.Green : MercuryTheme.Accent);
        StatusRow("Phantom BLU spells", string.Empty, $"{blu} / {PhantomBlueMage.Spells.Count}", blu == PhantomBlueMage.Spells.Count ? MercuryTheme.Green : MercuryTheme.Accent);
    }
}
