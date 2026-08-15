using System;
using System.Collections.Generic;
using System.Linq;

namespace Mercury;

/// <summary>
/// A pot FATE and, where known, its fixed spawn map coordinate. Field-op FATEs have no
/// static Level location in the sheet, so all four pot coords are hardcoded (from the
/// wiki) for always-on map markers. A null coord falls back to the live FateTracker position.
/// </summary>
public sealed record PotFate(string Name, float? MapX = null, float? MapY = null);

/// <summary>The two pot FATEs per Occult Crescent zone. Single source for the Pots tab and the map.</summary>
public static class PotFates
{
    public static readonly IReadOnlyList<PotFate> SouthHorn = new[]
    {
        new PotFate("Persistent Pots", 25.6f, 17.1f),
        new PotFate("Pleading Pots", 11.9f, 32.0f),
    };

    public static readonly IReadOnlyList<PotFate> NorthHorn = new[]
    {
        new PotFate("Daylight Pottery", 26.2f, 11.6f),
        new PotFate("In a Pot of Bother", 11.0f, 25.8f),
    };

    public static IReadOnlyList<PotFate> For(OccultMap map) => map switch
    {
        OccultMap.SouthHorn => SouthHorn,
        OccultMap.NorthHorn => NorthHorn,
        _ => Array.Empty<PotFate>(),
    };

    public static bool IsPot(string name)
        => SouthHorn.Any(p => p.Name == name) || NorthHorn.Any(p => p.Name == name);
}
