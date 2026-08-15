using System.Collections.Generic;

namespace Mercury;

public enum RecordSourceKind
{
    Quest,
    SurveyPoint,
    CriticalEncounter,
    NotoriousMonster,
    ForkedTower,
    Other,
}

/// <summary>Which Occult Crescent map a survey point sits on.</summary>
public enum OccultMap
{
    None,
    SouthHorn,
    NorthHorn,
}

/// <summary>
/// How a given Occult Record is acquired. For survey points, <see cref="Map"/>,
/// <see cref="X"/> and <see cref="Y"/> give the in-game map coordinate so it can
/// be plotted on the zone map.
/// </summary>
public sealed record RecordSource(
    RecordSourceKind Kind,
    string Text,
    OccultMap Map = OccultMap.None,
    float X = 0f,
    float Y = 0f);

/// <summary>Texture paths and territory ids for the Occult Crescent zone maps.</summary>
public static class MapCatalog
{
    public const uint SouthHornTerritory = 1252;
    public const uint NorthHornTerritory = 1346;

    /// <summary>Map sheet row id for the zone (used for in-game flag placement).</summary>
    public static uint MapRowId(OccultMap map) => map == OccultMap.NorthHorn ? 1135u : 967u;

    /// <summary>TerritoryType row id for the zone.</summary>
    public static uint TerritoryId(OccultMap map) => map == OccultMap.NorthHorn ? NorthHornTerritory : SouthHornTerritory;

    // Map texture path: the file name is the Map Id with the slash removed, then "_m.tex".
    // e.g. Map Id "o6b1/01" -> ui/map/o6b1/01/o6b101_m.tex
    public static string TexturePath(OccultMap map) => map switch
    {
        OccultMap.NorthHorn => "ui/map/o6b2/01/o6b201_m.tex",
        _ => "ui/map/o6b1/01/o6b101_m.tex",
    };

    public static string DisplayName(OccultMap map) => map switch
    {
        OccultMap.NorthHorn => "North Horn",
        _ => "South Horn",
    };

    public static OccultMap FromTerritory(uint territoryType) => territoryType switch
    {
        NorthHornTerritory => OccultMap.NorthHorn,
        SouthHornTerritory => OccultMap.SouthHorn,
        _ => OccultMap.None,
    };
}

/// <summary>
/// Curated acquisition data for each Occult Record, keyed by its MKDLore row id.
/// Sourced from the community wiki (rows 1-30 South Horn, 31-58 North Horn).
/// This is static reference data - the game does not expose a per-record source,
/// so if the wiki and the in-game sheet ever diverge, update this table.
/// </summary>
public static class RecordSources
{
    public static RecordSource? Get(uint rowId)
        => Map.TryGetValue(rowId, out var s) ? s : null;

    private static readonly Dictionary<uint, RecordSource> Map = new()
    {
        // --- The South Horn (patch 7.2) ---
        [1] = new(RecordSourceKind.Quest, "One Last Hurrah"),
        [2] = new(RecordSourceKind.SurveyPoint, "Phantom Village (6.1, 4.9)", OccultMap.SouthHorn, 6.1f, 4.9f),
        [3] = new(RecordSourceKind.Quest, "The Phantom Village"),
        [4] = new(RecordSourceKind.Quest, "Unfamiliar Territory"),
        [5] = new(RecordSourceKind.SurveyPoint, "South Horn (38.6, 7.6)", OccultMap.SouthHorn, 38.6f, 7.6f),
        [6] = new(RecordSourceKind.Quest, "The Ancient Arts of War"),
        [7] = new(RecordSourceKind.SurveyPoint, "South Horn (31.4, 17.0)", OccultMap.SouthHorn, 31.4f, 17.0f),
        [8] = new(RecordSourceKind.CriticalEncounter, "Cursed Concern"),
        [9] = new(RecordSourceKind.SurveyPoint, "South Horn (20.2, 12.2)", OccultMap.SouthHorn, 20.2f, 12.2f),
        [10] = new(RecordSourceKind.CriticalEncounter, "Shark Attack"),
        [11] = new(RecordSourceKind.NotoriousMonster, "Notorious Monsters"),
        [12] = new(RecordSourceKind.SurveyPoint, "South Horn (23.2, 21.5)", OccultMap.SouthHorn, 23.2f, 21.5f),
        [13] = new(RecordSourceKind.SurveyPoint, "South Horn (10.2, 22.5)", OccultMap.SouthHorn, 10.2f, 22.5f),
        [14] = new(RecordSourceKind.CriticalEncounter, "From Times Bygone"),
        [15] = new(RecordSourceKind.SurveyPoint, "South Horn (24.2, 32.8)", OccultMap.SouthHorn, 24.2f, 32.8f),
        [16] = new(RecordSourceKind.CriticalEncounter, "The Black Regiment"),
        [17] = new(RecordSourceKind.CriticalEncounter, "The Unbridled"),
        [18] = new(RecordSourceKind.SurveyPoint, "South Horn (18.5, 33.8)", OccultMap.SouthHorn, 18.5f, 33.8f),
        [19] = new(RecordSourceKind.SurveyPoint, "South Horn (15.6, 29.5)", OccultMap.SouthHorn, 15.6f, 29.5f),
        [20] = new(RecordSourceKind.CriticalEncounter, "Calamity Bound"),
        [21] = new(RecordSourceKind.SurveyPoint, "South Horn (36.6, 33.7)", OccultMap.SouthHorn, 36.6f, 33.7f),
        [22] = new(RecordSourceKind.SurveyPoint, "South Horn (36.0, 22.6)", OccultMap.SouthHorn, 36.0f, 22.6f),
        [23] = new(RecordSourceKind.SurveyPoint, "South Horn (3.8, 5.8)", OccultMap.SouthHorn, 3.8f, 5.8f),
        [24] = new(RecordSourceKind.SurveyPoint, "South Horn (8.7, 35.9)", OccultMap.SouthHorn, 8.7f, 35.9f),
        [25] = new(RecordSourceKind.Quest, "Past and Crescent"),
        [26] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Blood"),
        [27] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Blood"),
        [28] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Blood"),
        [29] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Blood"),
        [30] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Blood (final room)"),

        // --- The North Horn (patch 7.55) ---
        [31] = new(RecordSourceKind.SurveyPoint, "North Horn (39.1, 38.0)", OccultMap.NorthHorn, 39.1f, 38.0f),
        [32] = new(RecordSourceKind.SurveyPoint, "North Horn (36.4, 32.2)", OccultMap.NorthHorn, 36.4f, 32.2f),
        [33] = new(RecordSourceKind.CriticalEncounter, "Forbidden Folios"),
        [34] = new(RecordSourceKind.CriticalEncounter, "Tiny Terror"),
        [35] = new(RecordSourceKind.CriticalEncounter, "Imbalanced Diet"),
        [36] = new(RecordSourceKind.SurveyPoint, "North Horn (24.4, 28.7)", OccultMap.NorthHorn, 24.4f, 28.7f),
        [37] = new(RecordSourceKind.SurveyPoint, "North Horn (39.7, 22.6)", OccultMap.NorthHorn, 39.7f, 22.6f),
        [38] = new(RecordSourceKind.CriticalEncounter, "Accept No Imitators"),
        [39] = new(RecordSourceKind.SurveyPoint, "North Horn (27.0, 14.0)", OccultMap.NorthHorn, 27.0f, 14.0f),
        [40] = new(RecordSourceKind.SurveyPoint, "North Horn (40.0, 3.9)", OccultMap.NorthHorn, 40.0f, 3.9f),
        [41] = new(RecordSourceKind.CriticalEncounter, "Appalling Behavior"),
        [42] = new(RecordSourceKind.CriticalEncounter, "Dark Artistry"),
        [43] = new(RecordSourceKind.SurveyPoint, "North Horn (17.5, 5.2)", OccultMap.NorthHorn, 17.5f, 5.2f),
        [44] = new(RecordSourceKind.CriticalEncounter, "Lost on the Wind"),
        [45] = new(RecordSourceKind.SurveyPoint, "North Horn (11.1, 38.8)", OccultMap.NorthHorn, 11.1f, 38.8f),
        [46] = new(RecordSourceKind.SurveyPoint, "North Horn (4.8, 36.4)", OccultMap.NorthHorn, 4.8f, 36.4f),
        [47] = new(RecordSourceKind.SurveyPoint, "North Horn (2.1, 23.0)", OccultMap.NorthHorn, 2.1f, 23.0f),
        [48] = new(RecordSourceKind.CriticalEncounter, "Cursed Resurgence"),
        [49] = new(RecordSourceKind.SurveyPoint, "North Horn (7.4, 14.0)", OccultMap.NorthHorn, 7.4f, 14.0f),
        [50] = new(RecordSourceKind.CriticalEncounter, "Quarried Away"),
        [51] = new(RecordSourceKind.SurveyPoint, "North Horn (3.9, 2.1)", OccultMap.NorthHorn, 3.9f, 2.1f),
        [52] = new(RecordSourceKind.SurveyPoint, "North Horn (21.3, 19.7)", OccultMap.NorthHorn, 21.3f, 19.7f),
        [53] = new(RecordSourceKind.CriticalEncounter, "Doubled Trouble"),
        [54] = new(RecordSourceKind.SurveyPoint, "North Horn (22.7, 33.9)", OccultMap.NorthHorn, 22.7f, 33.9f),
        [55] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Magic"),
        [56] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Magic"),
        [57] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Magic"),
        [58] = new(RecordSourceKind.ForkedTower, "The Forked Tower: Magic"),
    };
}
