using System;
using System.Collections.Generic;
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
/// Reads the live Challenge Log count out of the open ContentsNote addon. The game keeps no
/// persistent per-entry counter, so this only works while that window is open on the Field
/// Operations tab. The addon lays out one 11-value numeric record per displayed entry
/// (icon, required, current, ...), in menu order; we find that block by matching the
/// "required" column against our known entries' required amounts, then read the current
/// count beside each. Wrapped so a mismatch yields no data rather than throwing. Use
/// <see cref="DumpToLog"/> (via "/mercury cl") to re-capture the layout if it ever changes.
/// </summary>
public sealed class ChallengeLogScraper
{
    // Each entry's numeric record in the addon: [icon, required, current, ...] (see /mercury cl).
    private const int BlockWidth = 11;
    private const int RequiredOffset = 1;
    private const int CurrentOffset = 2;

    private readonly Dictionary<string, uint> nameToRow = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<ChallengeEntry> order;
    private readonly Configuration config;

    // The Occult Crescent CE / FATE challenges, identified by objective text, whose counts
    // Mercury can auto-increment live from its own trackers between window syncs.
    private readonly List<uint> ceChallengeRows = new();
    private readonly List<uint> fateChallengeRows = new();

    // Best-known counts, kept so they persist after the window closes (the game only exposes
    // them while it is open). Seeded from persisted config and written back on change.
    private readonly Dictionary<uint, ChallengeProgress> cache = new();

    private long? lastResetUnix; // most recent weekly-reset time seen, for persisting increments

    /// <summary>When the counts were last captured from the open window, or null if never.</summary>
    public DateTime? LastSyncUtc { get; private set; }

    public ChallengeLogScraper(ChallengeLog data, Configuration config)
    {
        this.order = data.Entries; // already sorted by menu order, matching the addon's blocks
        this.config = config;
        foreach (var e in data.Entries)
        {
            this.nameToRow[e.Name] = e.RowId;

            // classify by objective so the auto-increment isn't tied to hard-coded row ids
            if (e.Description.Contains("critical encounters on the Occult Crescent", StringComparison.OrdinalIgnoreCase))
                this.ceChallengeRows.Add(e.RowId);
            else if (e.Description.Contains("FATEs on the Occult Crescent", StringComparison.OrdinalIgnoreCase))
                this.fateChallengeRows.Add(e.RowId);
        }

        // seed the cache from the last persisted sync so counts show before the window is
        // reopened (staleness across a weekly reset is handled in Update)
        foreach (var (rowId, cm) in config.ChallengeCounts)
            if (cm.Length == 2)
                this.cache[rowId] = new ChallengeProgress(cm[0], cm[1]);
        if (config.ChallengeCountsSyncedUnix > 0)
            this.LastSyncUtc = DateTimeOffset.FromUnixTimeSeconds(config.ChallengeCountsSyncedUnix).UtcDateTime;
    }

    /// <summary>
    /// Refreshes the cache from the open Challenge Log window. Called every frame from the
    /// plugin update loop so the counts are captured whenever the window is open, regardless
    /// of which Mercury tab (if any) is being drawn. Cheap and silent when it isn't open.
    /// <paramref name="nextResetUnix"/> is the game's next weekly reset time, used to discard
    /// persisted counts once the week rolls over.
    /// </summary>
    public void Update(long? nextResetUnix)
    {
        this.lastResetUnix = nextResetUnix ?? this.lastResetUnix;

        // once the weekly reset boundary moves, last week's counts are stale - drop them
        if (nextResetUnix is { } nr && this.config.ChallengeCountsResetUnix != 0 &&
            this.config.ChallengeCountsResetUnix != nr)
        {
            this.cache.Clear();
            this.config.ChallengeCounts.Clear();
            this.config.ChallengeCountsResetUnix = nr;
            this.config.ChallengeCountsSyncedUnix = 0;
            this.LastSyncUtc = null;
            this.config.Save();
        }

        this.RefreshFromWindow(nextResetUnix);
    }

    /// <summary>
    /// The best-known counts keyed by ContentsNote row id: the values last captured from the
    /// window (kept after it closes, across reloads). Empty until synced at least once.
    /// </summary>
    public Dictionary<uint, ChallengeProgress> TryScrape()
        => new(this.cache);

    /// <summary>
    /// Live estimate: a Critical Encounter you completed advances the "Critically Endangered"
    /// challenges by one, from the last synced baseline. Corrected to the true value the next
    /// time the Challenge Log window is opened. No-op until synced at least once.
    /// </summary>
    public void OnCriticalEncounterCompleted() => this.Bump(this.ceChallengeRows);

    /// <summary>As <see cref="OnCriticalEncounterCompleted"/>, for the "Fateful Contention" FATE challenges.</summary>
    public void OnFateCompleted() => this.Bump(this.fateChallengeRows);

    // Adds one to each given challenge's current count (never past its max), but only for
    // entries we already have a baseline for - so this refines a synced count rather than
    // inventing one. Persists when anything changed.
    private void Bump(IReadOnlyList<uint> rows)
    {
        var changed = false;
        foreach (var rowId in rows)
        {
            if (this.cache.TryGetValue(rowId, out var p) && p.Current < p.Max)
            {
                this.cache[rowId] = new ChallengeProgress(p.Current + 1, p.Max);
                changed = true;
            }
        }

        if (changed)
            this.Persist(this.lastResetUnix);
    }

    // Reads the numeric block out of the open ContentsNote addon into the cache, persisting
    // to config only when a value actually changed. No-op if the window isn't open or its
    // layout doesn't match (e.g. a different category tab is showing).
    private unsafe void RefreshFromWindow(long? nextResetUnix)
    {
        try
        {
            var unit = this.FindWindow();
            if (unit == null || this.order.Count == 0)
                return;

            var count = (int)unit->AtkValuesCount;
            var values = unit->AtkValues;
            var n = this.order.Count;

            // Find where the numeric block region starts: the first offset s at which the
            // "required" column of n consecutive blocks equals our entries' required amounts,
            // in order. This is unique enough to be safe and only matches the Field Ops tab.
            for (var s = 0; s + (n * BlockWidth) <= count; s++)
            {
                var match = true;
                for (var k = 0; k < n && match; k++)
                {
                    if (!TryInt(values, s + (k * BlockWidth) + RequiredOffset, out var req) ||
                        req != this.order[k].RequiredAmount)
                        match = false;
                }

                if (!match)
                    continue;

                var changed = false;
                for (var k = 0; k < n; k++)
                {
                    var baseIdx = s + (k * BlockWidth);
                    TryInt(values, baseIdx + RequiredOffset, out var req);
                    TryInt(values, baseIdx + CurrentOffset, out var cur);
                    var progress = new ChallengeProgress(cur, req);
                    var rowId = this.order[k].RowId;
                    if (!this.cache.TryGetValue(rowId, out var prev) || prev != progress)
                    {
                        this.cache[rowId] = progress;
                        changed = true;
                    }
                }

                this.LastSyncUtc = DateTime.UtcNow;
                if (changed || this.config.ChallengeCountsSyncedUnix == 0)
                    this.Persist(nextResetUnix);
                break;
            }
        }
        catch
        {
            // best-effort only - keep whatever is already cached
        }
    }

    // Writes the current cache to config so the counts survive reloads and sessions.
    private void Persist(long? nextResetUnix)
    {
        var saved = new Dictionary<uint, int[]>();
        foreach (var (rowId, p) in this.cache)
            saved[rowId] = new[] { p.Current, p.Max };

        this.config.ChallengeCounts = saved;
        this.config.ChallengeCountsSyncedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nextResetUnix is { } nr)
            this.config.ChallengeCountsResetUnix = nr;
        this.config.Save();
    }

    // Reads an Int/UInt AtkValue as int (the union stores both in the same slot; our counts
    // are small and non-negative). False for any other value type.
    private static unsafe bool TryInt(AtkValue* values, int i, out int val)
    {
        val = 0;
        ref var v = ref values[i];
        if (v.Type is not (AtkValueType.Int or AtkValueType.UInt))
            return false;
        val = v.Int;
        return true;
    }

    /// <summary>
    /// Diagnostic: lists every visible addon and dumps the raw AtkValues of any that look
    /// like the Challenge Log (by addon name or by containing our entry names), so the value
    /// layout can be finalised even when the name-based <see cref="FindWindow"/> misses it.
    /// </summary>
    public unsafe string DumpToLog()
    {
        try
        {
            var atk = RaptureAtkModule.Instance();
            if (atk == null)
                return "UI module not available.";

            var entries = atk->RaptureAtkUnitManager.AllLoadedUnitsList.Entries;
            var listed = 0;
            var dumped = 0;

            for (var e = 0; e < entries.Length; e++)
            {
                var unit = entries[e].Value;
                if (unit == null || !unit->IsVisible)
                    continue;

                var name = unit->NameString ?? string.Empty;
                var count = unit->AtkValuesCount;
                var values = unit->AtkValues;
                listed++;

                // dump the values if the addon name mentions "Content" or it carries any of
                // our known Challenge Log entry names
                var nameHit = name.Contains("Content", StringComparison.OrdinalIgnoreCase);
                var contentHit = false;
                for (var i = 0; i < count && !contentHit; i++)
                    if (values[i].Type == AtkValueType.String && values[i].String.Value != null)
                    {
                        var t = MemoryHelper.ReadSeStringNullTerminated((nint)values[i].String.Value).TextValue;
                        if (this.nameToRow.ContainsKey(t.Trim()))
                            contentHit = true;
                    }

                if (nameHit || contentHit)
                {
                    Service.Log.Information($"[Mercury] === addon '{name}' ({count} values) ===");
                    for (var i = 0; i < count; i++)
                    {
                        ref var v = ref values[i];
                        var s = v.Type == AtkValueType.String && v.String.Value != null
                            ? MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue
                            : v.Int.ToString();
                        Service.Log.Information($"  [{i}] {v.Type} = {s}");
                    }
                    dumped++;
                }
                else
                {
                    Service.Log.Information($"[Mercury] visible addon '{name}' ({count} values)");
                }
            }

            return dumped > 0
                ? $"Dumped {dumped} likely Challenge Log addon(s) to /xllog (of {listed} visible)."
                : $"No Challenge Log addon matched among {listed} visible addons (listed to /xllog). Open the Challenge Log to the Field Operations tab, then run this again.";
        }
        catch (Exception e)
        {
            return "Failed to dump: " + e.Message;
        }
    }

    /// <summary>
    /// Diagnostic for "/mercury cl": reports whether the window is found and whether the
    /// numeric-block match succeeds, logging each entry's parsed count (or, on failure, every
    /// Int/UInt value so the layout can be re-checked).
    /// </summary>
    public unsafe string Diagnose()
    {
        try
        {
            var req = new System.Text.StringBuilder();
            foreach (var e in this.order)
                req.Append(e.RequiredAmount).Append(',');
            Service.Log.Information($"[Mercury] cl-diag: order.Count={this.order.Count} required=[{req}]");

            var unit = this.FindWindow();
            if (unit == null)
            {
                Service.Log.Information("[Mercury] cl-diag: FindWindow=NULL (ContentsNote not matched among visible addons)");
                return "Scrape: Challenge Log window not found. Open it to the Field Operations tab and rerun /mercury cl.";
            }

            var count = (int)unit->AtkValuesCount;
            var values = unit->AtkValues;
            var n = this.order.Count;
            Service.Log.Information($"[Mercury] cl-diag: FindWindow='{unit->NameString}' AtkValuesCount={count}");

            var foundS = -1;
            for (var s = 0; s + (n * BlockWidth) <= count && foundS < 0; s++)
            {
                var match = true;
                for (var k = 0; k < n && match; k++)
                    if (!TryInt(values, s + (k * BlockWidth) + RequiredOffset, out var r) || r != this.order[k].RequiredAmount)
                        match = false;
                if (match)
                    foundS = s;
            }

            if (foundS >= 0)
            {
                Service.Log.Information($"[Mercury] cl-diag: matched {n} blocks at offset {foundS}");
                for (var k = 0; k < n; k++)
                {
                    var b = foundS + (k * BlockWidth);
                    TryInt(values, b + RequiredOffset, out var r);
                    TryInt(values, b + CurrentOffset, out var cur);
                    Service.Log.Information($"    {this.order[k].Name}: {cur}/{r}");
                }
                return $"Scrape OK: matched {n} entries at offset {foundS}. See /xllog.";
            }

            Service.Log.Information("[Mercury] cl-diag: NO block match. Int/UInt values:");
            for (var i = 0; i < count; i++)
            {
                ref var v = ref values[i];
                if (v.Type is AtkValueType.Int or AtkValueType.UInt)
                    Service.Log.Information($"    [{i}] {v.Type}={v.Int}");
            }
            return $"Scrape: found '{unit->NameString}' ({count} values) but no block match - dumped Int/UInt values to /xllog.";
        }
        catch (Exception e)
        {
            return "cl-diag failed: " + e.Message;
        }
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
