using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mercury;

/// <summary>Live count for one Challenge Log entry scraped from the open in-game window.</summary>
public readonly record struct ChallengeProgress(int Current, int Max)
{
    public bool HasCount => this.Max > 0;
}

/// <summary>
/// Best-effort reader for the live Challenge Log count. The game keeps no persistent
/// per-entry counter, so we locate the open Challenge Log window among the loaded UI units
/// by matching known entry names, then read the "current/max" the game prints beside each.
///
/// This depends on the addon's value layout and only works while the window is open on the
/// Field Operations tab. Everything is wrapped so a mismatch yields no data rather than
/// throwing. Use <see cref="DumpToLog"/> (via "/mercury cl") to capture the raw layout.
/// </summary>
public sealed class ChallengeLogScraper
{
    private static readonly Regex Fraction = new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", RegexOptions.Compiled);

    private readonly Dictionary<string, uint> nameToRow = new(StringComparer.OrdinalIgnoreCase);

    private const int Window = 12; // neighbouring values to search around a name

    public ChallengeLogScraper(ChallengeLog data)
    {
        foreach (var e in data.Entries)
            this.nameToRow[e.Name] = e.RowId;
    }

    /// <summary>
    /// Reads the live count keyed by ContentsNote row id, or an empty map if the window
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

            // snapshot string values so neighbours are cheap to inspect
            var strings = new Dictionary<int, string>();
            for (var i = 0; i < count; i++)
            {
                ref var v = ref values[i];
                if (v.Type == AtkValueType.String && v.String.Value != null)
                    strings[i] = MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue;
            }

            foreach (var (idx, text) in strings)
            {
                if (!this.nameToRow.TryGetValue(text.Trim(), out var rowId))
                    continue;

                // look outward from the name for its "x/y" count
                for (var d = 1; d <= Window; d++)
                {
                    if (TryFraction(strings, idx + d, out var p) || TryFraction(strings, idx - d, out p))
                    {
                        result[rowId] = p;
                        break;
                    }
                }
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

    private static bool TryFraction(Dictionary<int, string> strings, int idx, out ChallengeProgress p)
    {
        p = default;
        if (!strings.TryGetValue(idx, out var s))
            return false;
        var m = Fraction.Match(s);
        if (!m.Success)
            return false;
        p = new ChallengeProgress(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        return true;
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
