using System;
using System.Collections.Generic;

namespace Mercury;

/// <summary>
/// Curated "nearest crystal" overrides for Occult Crescent encounters, keyed by exact
/// encounter name (case-insensitive). Mercury normally learns each FATE/CE position live
/// and derives the nearest aetheryte, but the game exposes no static spawn coords for these,
/// so this table lets a known encounter show its crystal hint before it is ever seen live.
/// A live-learned position always takes precedence (see <see cref="Plugin.NearestCrystal"/>).
/// Each crystal name must match one in <see cref="OccultAetherytes"/>.
/// </summary>
public static class EncounterCrystals
{
    private static readonly Dictionary<string, string> Nearest =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // North Horn
            ["Many Mouths to Feed"] = "Moldering Outskirts",
        };

    /// <summary>The curated nearest-crystal name for this encounter, or null if none on file.</summary>
    public static string? For(string name)
        => name is not null && Nearest.TryGetValue(name, out var crystal) ? crystal : null;
}
