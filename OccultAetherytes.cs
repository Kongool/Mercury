using System.Collections.Generic;

namespace Mercury;

/// <summary>An Occult Crescent aetheryte "crystal" you can teleport to, at a map coordinate.</summary>
public sealed record Crystal(string Name, float X, float Y);

/// <summary>
/// The aetheryte crystals for each Occult Crescent zone, extracted from the game's
/// MapMarker data (aetheryte icon 60959) and converted to map coordinates via the
/// standard mapCoord = pixel / 2048 * 41 + 1. Used for the nearest-crystal hint and to
/// plot crystals on the Map tab. Zone-aware so South Horn FATEs get South Horn crystals.
/// </summary>
public static class OccultAetherytes
{
    // South Horn (map 967, MapMarkerRange 659).
    public static readonly IReadOnlyList<Crystal> SouthHorn = new[]
    {
        new Crystal("Expedition Base Camp", 38.1f, 7.6f),
        new Crystal("The Wanderer's Haven", 18.0f, 9.3f),
        new Crystal("Crystallized Caverns", 14.3f, 19.1f),
        new Crystal("Eldergrowth", 27.6f, 27.6f),
        new Crystal("Stonemarsh", 13.8f, 27.1f),
    };

    // North Horn (map 1135, MapMarkerRange 741).
    public static readonly IReadOnlyList<Crystal> NorthHorn = new[]
    {
        new Crystal("North Horn Base Camp", 39.1f, 39.1f),
        new Crystal("The Crown of Karnak", 30.5f, 32.1f),
        new Crystal("Sinking Sanctuary", 28.6f, 10.4f),
        new Crystal("Unhallowed Hamlet", 21.2f, 20.7f),
        new Crystal("Moldering Outskirts", 13.7f, 12.7f),
        new Crystal("Suspended Masonry", 10.5f, 33.4f),
    };

    /// <summary>The crystal list for a zone (empty outside the Occult Crescent).</summary>
    public static IReadOnlyList<Crystal> For(OccultMap map) => map switch
    {
        OccultMap.SouthHorn => SouthHorn,
        OccultMap.NorthHorn => NorthHorn,
        _ => System.Array.Empty<Crystal>(),
    };

    /// <summary>Map coordinate (1-42) -> world position on the zone plane.</summary>
    private static float ToWorld(float mapCoord) => ((mapCoord - 1f) / 41f * 2048f) - 1024f;

    /// <summary>The crystal in the given zone closest to a world (X, Z) position, or null.</summary>
    public static Crystal? Nearest(OccultMap map, float worldX, float worldZ)
    {
        Crystal? best = null;
        var bestSq = float.MaxValue;
        foreach (var c in For(map))
        {
            var dx = ToWorld(c.X) - worldX;
            var dz = ToWorld(c.Y) - worldZ;
            var sq = (dx * dx) + (dz * dz);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = c;
            }
        }

        return best;
    }
}
