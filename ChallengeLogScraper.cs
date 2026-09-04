using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mercury;

/// <summary>Live data for one Challenge Log entry scraped from the open in-game window.</summary>
public readonly record struct ChallengeProgress(int Current, int Max, int Exp)
{
    public bool HasCount => this.Max > 0;
    public bool HasExp => this.Exp > 0;
}

/// <summary>
/// Best-effort reader for live Challenge Log data. The game keeps no persistent per-entry
/// counter and no flat EXP value (the sheet stores only a level-scaling multiplier), so we
/// locate the open Challenge Log window among the loaded UI units by matching known entry
/// names, then read the "current/max" count and the EXP reward the game itself prints.
///
/// This depends on the addon's value layout and only works while the window is open on the
/// Field Operations tab. Everything is wrapped so a mismatch yields no data rather than
/// throwing. Use <see cref="DumpToLog"/> (via "/mercury cl") to capture the raw layout.
/// </summary>
public sealed class ChallengeLogScraper
{
    private static readonly Regex Fraction = new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", RegexOptions.Compiled);

    private readonly Dictionary<string, uint> nameToRow = new(StringComparer.OrdinalIgnoreCase);

    private const int Window = 12;            // neighbouring values to search around a name
    private const int ExpFloor = 2000;        // EXP dwarfs gil (<=1400) and counts (<=60)
    private const int ExpCeiling = 90_000_000; // ... but stays well under a unix timestamp

    public ChallengeLogScraper(ChallengeLog data)
    {
        foreach (var e in data.Entries)
            this.nameToRow[e.Name] = e.RowId;
    }

    /// <summary>
    /// Reads live count + EXP keyed by ContentsNote row id, or an empty map if the window
    /// isn't open / couldn't be parsed. Never throws.
    /// </summary>
    public unsafe Dictionary<uint, ChallengeProgress> TryScrape()
    {
        var result = new Dictionary<uint, ChallengeProgress>();
        try
        {
            var unit = this.FindWindow();
            if (unit == null)
                return result;

            var count = unit->AtkValuesCount;
            var values = unit->AtkValues;

            // snapshot strings + ints so neighbours are cheap to inspect
            var strings = new Dictionary<int, string>();
            var ints = new Dictionary<int, int>();
            for (var i = 0; i < count; i++)
            {
                ref var v = ref values[i];
                if (v.Type == AtkValueType.String && v.String.Value != null)
                    strings[i] = MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue;
                else if (v.Type == AtkValueType.Int)
                    ints[i] = v.Int;
            }

            foreach (var (idx, text) in strings)
            {
                if (!this.nameToRow.TryGetValue(text.Trim(), out var rowId))
                    continue;

                var current = 0;
                var max = 0;
                var exp = 0;

                // look outward from the name for its "x/y" count and its EXP reward (the
                // largest sane number nearby - gil and the count are far smaller)
                for (var d = 1; d <= Window; d++)
                {
                    foreach (var j in new[] { idx + d, idx - d })
                    {
                        if (max == 0 && strings.TryGetValue(j, out var s))
                        {
                            var m = Fraction.Match(s);
                            if (m.Success)
                            {
                                current = int.Parse(m.Groups[1].Value);
                                max = int.Parse(m.Groups[2].Value);
                            }
                        }

                        var candidate = NumberAt(strings, ints, j);
                        if (candidate > ExpFloor && candidate < ExpCeiling && candidate > exp)
                            exp = candidate;
                    }
                }

                if (max > 0 || exp > 0)
                    result[rowId] = new ChallengeProgress(current, max, exp);
            }
        }
        catch
        {
            // best-effort only - fall back to no live data
        }

        return result;
    }

    /// <summary>Logs the raw AtkValues of the located window so the layout can be finalised.</summary>
    public unsafe string DumpToLog()
    {
        try
        {
            var unit = this.FindWindow();
            if (unit == null)
                return "Challenge Log window not found. Open it to the Field Operations tab, then run this again.";

            var count = unit->AtkValuesCount;
            var values = unit->AtkValues;
            Service.Log.Information($"[Mercury] Challenge Log addon '{unit->NameString}' has {count} AtkValues:");
            for (var i = 0; i < count; i++)
            {
                ref var v = ref values[i];
                var s = v.Type == AtkValueType.String && v.String.Value != null
                    ? MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue
                    : v.Int.ToString();
                Service.Log.Information($"  [{i}] {v.Type} = {s}");
            }
            return $"Dumped {count} values from '{unit->NameString}' to the Dalamud log (/xllog).";
        }
        catch (Exception e)
        {
            return "Failed to dump Challenge Log window: " + e.Message;
        }
    }

    // Reads a plain integer at index j - either an Int AtkValue or a comma-formatted number
    // string (big UI numbers like EXP are usually preformatted strings). 0 if neither.
    private static int NumberAt(Dictionary<int, string> strings, Dictionary<int, int> ints, int j)
    {
        if (ints.TryGetValue(j, out var iv))
            return iv;
        if (strings.TryGetValue(j, out var sv))
        {
            var digits = sv.Replace(",", string.Empty).Trim();
            if (digits.Length > 0 && int.TryParse(digits, out var pv))
                return pv;
        }
        return 0;
    }

    // Locates the loaded UI unit that contains several of our known Challenge Log entry names.
    private unsafe AtkUnitBase* FindWindow()
    {
        var atk = RaptureAtkModule.Instance();
        if (atk == null)
            return null;

        var entries = atk->RaptureAtkUnitManager.AllLoadedUnitsList.Entries;
        for (var e = 0; e < entries.Length; e++)
        {
            var unit = entries[e].Value;
            if (unit == null || !unit->IsVisible)
                continue;

            var count = unit->AtkValuesCount;
            if (count < 4)
                continue;
            var values = unit->AtkValues;

            var hits = 0;
            for (var i = 0; i < count && hits < 3; i++)
            {
                if (values[i].Type != AtkValueType.String || values[i].String.Value == null)
                    continue;
                var text = MemoryHelper.ReadSeStringNullTerminated((nint)values[i].String.Value).TextValue;
                if (this.nameToRow.ContainsKey(text.Trim()))
                    hits++;
            }

            if (hits >= 3)
                return unit;
        }

        return null;
    }
}
