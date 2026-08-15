using System;
using System.Collections.Generic;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Mercury;

/// <summary>What kind of Martial Memories objective this is.</summary>
public enum MartialObjectiveKind
{
    Slay,        // "Slay a megamaguey" - an overworld mob (radar target)
    FateRating,  // "Receive highest rating possible in FATEs: /5"
    Clear,       // "Clear Ihuykatumu" - a duty (dungeon/trial/raid)
}

/// <summary>
/// A single Martial Memories objective. <see cref="DetailId"/> is the
/// PhantomWeaponExTodoDetailTxt row id - a stable key used for manual completion
/// tracking and for matching live progress scraped from the in-game window.
/// </summary>
public sealed record MartialObjective(
    int DetailId,
    MartialObjectiveKind Kind,
    string Text,
    string? MobName,
    string? UnlockNote,
    int Target);

/// <summary>A zone (or content) grouping within a category, e.g. "Urqopacha".</summary>
public sealed record MartialGroup(string Name, uint TerritoryType, List<MartialObjective> Objectives);

/// <summary>A top-level Martial Memories category, e.g. "Yok Tural Field Operations".</summary>
public sealed record MartialCategory(string Name, List<MartialGroup> Groups);

/// <summary>An overworld zone with its map and the slay objectives the radar hunts for.</summary>
public sealed record MartialZone(
    string Name,
    uint TerritoryType,
    uint MapRowId,
    string TexturePath,
    IReadOnlyList<MartialObjective> Targets);

/// <summary>
/// Loads the Martial Memories (phantom weapon knowledge crystal) objective list from the
/// game's PhantomWeaponExTodo* sheets. This is static reference data; live per-objective
/// progress is not exposed by any game module, so it is scraped from the open window
/// (see <see cref="MartialMemoriesScraper"/>) or tracked manually.
/// </summary>
public sealed class MartialMemories
{
    // "Receive highest rating possible in FATEs" objectives are always out of 5; the
    // sheet leaves the numbers blank for the game to fill at runtime.
    private const int FateRatingTarget = 5;

    private readonly List<MartialCategory> categories = new();
    private readonly List<MartialZone> zones = new();

    /// <summary>All categories in game order.</summary>
    public IReadOnlyList<MartialCategory> Categories => this.categories;

    /// <summary>The overworld zones that have "Slay a X" objectives (radar targets).</summary>
    public IReadOnlyList<MartialZone> Zones => this.zones;

    public MartialMemories()
    {
        var excel = Service.DataManager.GameData.Excel;
        var todo = excel.GetSheet<RawRow>(null, "PhantomWeaponExTodo");
        var details = excel.GetSubrowSheet<RawSubrow>(null, "PhantomWeaponExTodoDetails");
        var txt = excel.GetSheet<RawRow>(null, "PhantomWeaponExTodoDetailTxt");

        var cfc = Service.DataManager.GetExcelSheet<ContentFinderCondition>();
        var places = Service.DataManager.GetExcelSheet<PlaceName>();

        string DetailText(uint id) => txt.TryGetRow(id, out var r) ? r.ReadStringColumn(0).ExtractText() : string.Empty;
        string DetailNote(uint id) => txt.TryGetRow(id, out var r) ? r.ReadStringColumn(1).ExtractText() : string.Empty;
        string ContentName(uint id) => cfc.TryGetRow(id, out var r) ? r.Name.ExtractText() : string.Empty;
        string PlaceOf(uint id) => places.TryGetRow(id, out var r) ? r.Name.ExtractText() : string.Empty;

        for (var cat = 1u; cat < todo.Count; cat++)
        {
            if (!todo.TryGetRow(cat, out var catRow))
                continue;
            var catName = catRow.ReadStringColumn(0).ExtractText();
            if (string.IsNullOrWhiteSpace(catName))
                continue;
            if (!details.TryGetRow(cat, out var subrows))
                continue;

            // Group objectives by their zone (PlaceName, col4); content categories have
            // col4 == 0 and are collected under a single unnamed group.
            var groups = new List<MartialGroup>();
            var byPlace = new Dictionary<uint, MartialGroup>();

            for (var s = 0; s < subrows.Count; s++)
            {
                var row = subrows[s];
                var detailId = (int)row.ReadUInt32Column(2);
                var cfcId = row.ReadUInt32Column(3);
                uint placeId = row.ReadUInt16Column(4);
                var territory = row.ReadUInt32Column(5);

                var text = DetailText((uint)detailId);
                var note = DetailNote((uint)detailId);
                var kind = ClassifyObjective(text);

                string? mob = null;
                if (kind == MartialObjectiveKind.Clear)
                {
                    var content = ContentName(cfcId);
                    if (!string.IsNullOrEmpty(content))
                        text = $"Clear {content}";
                }
                else if (kind == MartialObjectiveKind.Slay)
                {
                    mob = ParseSlayTarget(text);
                }
                else if (kind == MartialObjectiveKind.FateRating)
                {
                    // the sheet leaves the counters blank ("...FATEs: /"); drop the dangling slash
                    text = text.TrimEnd().TrimEnd('/').TrimEnd().TrimEnd(':').TrimEnd();
                }

                var target = kind == MartialObjectiveKind.FateRating ? FateRatingTarget : 1;
                var objective = new MartialObjective(
                    detailId, kind, text, mob,
                    string.IsNullOrWhiteSpace(note) ? null : note, target);

                if (!byPlace.TryGetValue(placeId, out var group))
                {
                    var zoneName = placeId != 0 ? PlaceOf(placeId) : string.Empty;
                    group = new MartialGroup(zoneName, territory, new List<MartialObjective>());
                    byPlace[placeId] = group;
                    groups.Add(group);
                }

                group.Objectives.Add(objective);
            }

            this.categories.Add(new MartialCategory(catName, groups));
        }

        this.BuildZones();
    }

    /// <summary>The zone (by territory) the player is standing in, or null if not in a Martial zone.</summary>
    public MartialZone? ZoneForTerritory(uint territory)
    {
        foreach (var z in this.zones)
            if (z.TerritoryType == territory)
                return z;
        return null;
    }

    // Distil the per-zone radar catalog from the slay objectives, resolving each zone's
    // overworld map (texture path + map row) from its TerritoryType.
    private void BuildZones()
    {
        var territories = Service.DataManager.GetExcelSheet<TerritoryType>();

        foreach (var cat in this.categories)
        foreach (var group in cat.Groups)
        {
            if (group.TerritoryType == 0 || string.IsNullOrEmpty(group.Name))
                continue;

            var targets = new List<MartialObjective>();
            foreach (var o in group.Objectives)
                if (o.Kind == MartialObjectiveKind.Slay && o.MobName != null)
                    targets.Add(o);
            if (targets.Count == 0)
                continue;

            if (!territories.TryGetRow(group.TerritoryType, out var terr))
                continue;
            var map = terr.Map.Value;

            this.zones.Add(new MartialZone(
                group.Name, group.TerritoryType, map.RowId,
                MapTexturePath(map.Id.ExtractText()), targets));
        }
    }

    // Map Id "y6f1/00" -> "ui/map/y6f1/00/y6f100_m.tex" (slash removed in the file name).
    private static string MapTexturePath(string mapId)
    {
        if (string.IsNullOrEmpty(mapId))
            return string.Empty;
        var flat = mapId.Replace("/", string.Empty);
        return $"ui/map/{mapId}/{flat}_m.tex";
    }

    private static MartialObjectiveKind ClassifyObjective(string text)
    {
        if (text.StartsWith("Clear", StringComparison.OrdinalIgnoreCase))
            return MartialObjectiveKind.Clear;
        if (text.Contains("rating", StringComparison.OrdinalIgnoreCase))
            return MartialObjectiveKind.FateRating;
        return MartialObjectiveKind.Slay;
    }

    // "Slay a megamaguey" / "Slay an uolon" -> "megamaguey" / "uolon".
    private static string? ParseSlayTarget(string text)
    {
        const string slay = "Slay ";
        if (!text.StartsWith(slay, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = text[slay.Length..].Trim();
        foreach (var article in new[] { "an ", "a " })
            if (rest.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[article.Length..];
                break;
            }
        return rest.Length == 0 ? null : rest;
    }
}
