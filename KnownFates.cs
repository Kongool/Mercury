using System;
using System.Collections.Generic;

namespace Mercury;

/// <summary>
/// Curated FATE names per Occult Crescent zone, so the FATEs tab can show the whole roster
/// as "not seen yet" before any spawn this session. Verified against the game's Fate sheet
/// (South Horn rows 1962-1977, North Horn rows 2072-2084). Note: the pot FATEs are included
/// here as well as on the Pots tab. An incomplete list is fine - FATEs seen live still appear.
/// </summary>
public static class KnownFates
{
    public static readonly IReadOnlyList<string> SouthHorn = new[]
    {
        "A Delicate Balance",
        "A Prying Eye",
        "An Unending Duty",
        "Brain Drain",
        "Fatal Allure",
        "King of the Crescent",
        "Persistent Pots",
        "Pleading Pots",
        "Rough Waters",
        "Serving Darkness",
        "Sworn to Soil",
        "The Golden Guardian",
        "The Winged Terror",
    };

    public static readonly IReadOnlyList<string> NorthHorn = new[]
    {
        "A Rotten Affair",
        "Allure of the Occult",
        "Daylight Pottery",
        "Eye to Eye",
        "Gale-force Encounter",
        "In a Pot of Bother",
        "Inconstant Gardener",
        "Raging Thrall",
        "Scale Model",
        "Shoreline Showdown",
        "Territorial Dispute",
        "Thunderregnum",
        "Waved Away",
    };

    public static IReadOnlyList<string> For(OccultMap map) => map switch
    {
        OccultMap.SouthHorn => SouthHorn,
        OccultMap.NorthHorn => NorthHorn,
        _ => Array.Empty<string>(),
    };
}
