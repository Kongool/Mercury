using System;
using System.Collections.Generic;

namespace Mercury;

/// <summary>
/// A curated hint table for the Occult Crescent FATEs worth stopping for - chiefly the
/// ones that drop phantom-job shards. Keyed by exact FATE name (case-insensitive). This
/// only supplies the reward label shown in the UI and alert; whether a FATE actually
/// alerts is driven by <see cref="Configuration.AlertFates"/>, which the player can edit.
/// </summary>
public static class NotableFates
{
    private static readonly Dictionary<string, string> Rewards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dark Artistry"] = "Necromancer shard",
            ["Appalling Behavior"] = "Blue Mage shard",
        };

    /// <summary>The reward hint for this FATE, or null if we have none on file.</summary>
    public static string? Reward(string name)
        => name is not null && Rewards.TryGetValue(name, out var r) ? r : null;
}
