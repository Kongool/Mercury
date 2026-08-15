using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mercury;

/// <summary>Live progress for one objective scraped from the in-game window.</summary>
public readonly record struct MartialProgress(int Current, int Max)
{
    public bool Done => this.Current >= this.Max && this.Max > 0;
}

/// <summary>
/// Best-effort reader for live Martial Memories progress. The game exposes no module for
/// this data, so we locate the open "Martial Memories" window among the loaded UI units by
/// matching known objective strings, then read any "current/max" fractions it lists.
///
/// This is inherently fragile (it depends on the addon's value layout) and only works while
/// the window is open. Everything is wrapped so a mismatch simply yields no progress rather
/// than throwing. Use <see cref="DumpToLog"/> (via "/mercury mm") to capture the raw layout.
/// </summary>
public sealed class MartialMemoriesScraper
{
    private static readonly Regex Fraction = new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", RegexOptions.Compiled);

    private readonly Dictionary<string, int> textToDetailId = new(StringComparer.OrdinalIgnoreCase);
    private const int Window = 6; // how many neighbouring values to search for a fraction

    public MartialMemoriesScraper(MartialMemories data)
    {
        foreach (var cat in data.Categories)
        foreach (var group in cat.Groups)
        foreach (var o in group.Objectives)
            this.textToDetailId[o.Text] = o.DetailId;
    }

    /// <summary>
    /// Reads live progress keyed by objective DetailId, or an empty map if the window
    /// isn't open / couldn't be parsed. Never throws.
    /// </summary>
    public unsafe Dictionary<int, MartialProgress> TryScrape()
    {
        var result = new Dictionary<int, MartialProgress>();
        try
        {
            var unit = this.FindWindow();
            if (unit == null)
                return result;

            var count = unit->AtkValuesCount;
            var values = unit->AtkValues;

            // index -> string for every string value, so we can look at neighbours cheaply
            var strings = new Dictionary<int, string>();
            for (var i = 0; i < count; i++)
                if (values[i].Type == AtkValueType.String && values[i].String.Value != null)
                    strings[i] = MemoryHelper.ReadSeStringNullTerminated((nint)values[i].String.Value).TextValue;

            foreach (var (idx, text) in strings)
            {
                if (!this.textToDetailId.TryGetValue(text, out var detailId))
                    continue;

                // find the nearest "x/y" fraction string around this objective's label
                for (var d = 1; d <= Window; d++)
                {
                    if (TryFraction(strings, idx + d, out var p) || TryFraction(strings, idx - d, out p))
                    {
                        result[detailId] = p;
                        break;
                    }
                }
            }
        }
        catch
        {
            // best-effort only - fall back to no live progress
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
                return "Martial Memories window not found. Open it in-game, then run this again.";

            var count = unit->AtkValuesCount;
            var values = unit->AtkValues;
            Service.Log.Information($"[Mercury] Martial Memories addon '{unit->NameString}' has {count} AtkValues:");
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
            return "Failed to dump Martial Memories window: " + e.Message;
        }
    }

    // Locates the loaded UI unit that contains several of our known objective strings.
    private unsafe AtkUnitBase* FindWindow()
    {
        var atk = RaptureAtkModule.Instance();
        if (atk == null)
            return null;

        var list = atk->RaptureAtkUnitManager.AllLoadedUnitsList;
        var entries = list.Entries;
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
                if (this.textToDetailId.ContainsKey(text))
                    hits++;
            }

            if (hits >= 3)
                return unit;
        }

        return null;
    }

    private static bool TryFraction(Dictionary<int, string> strings, int idx, out MartialProgress p)
    {
        p = default;
        if (!strings.TryGetValue(idx, out var s))
            return false;
        var m = Fraction.Match(s);
        if (!m.Success)
            return false;
        p = new MartialProgress(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        return true;
    }
}
