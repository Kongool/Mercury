using System.Collections.Generic;

namespace Mercury;

/// <summary>
/// Occult Records that must be unlocked before an Occult Crescent quest can be
/// accepted, keyed by Quest row id. Only the "Survey" quests gate on records;
/// the rest progress through normal story completion. Curated from the wiki.
/// </summary>
public static class QuestRequirements
{
    public static IReadOnlyList<uint>? Get(uint questRowId)
        => Map.TryGetValue(questRowId, out var ids) ? ids : null;

    private static readonly Dictionary<uint, uint[]> Map = new()
    {
        [70851] = new uint[] { 7 },          // A Ruined Land   -> The Lost Citadel
        [70852] = new uint[] { 9, 13, 15 },  // A Common Thread -> Vanishing Slope, Fell Warren, Shadowed City
        [70853] = new uint[] { 19 },         // Past and Crescent -> The Abandoned Ascent
    };
}
