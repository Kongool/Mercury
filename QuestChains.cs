using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Mercury;

/// <summary>
/// One step of a field-op progression chain. <paramref name="MinLevel"/> is the Elemental Level or
/// Resistance Rank needed to pick it up (0 = none).
/// </summary>
public sealed record ChainStep(uint QuestId, int MinLevel, string Where, string Unlocks);

/// <summary>
/// The Eureka and Save the Queen progression quests, in order. Hardcoded because the game files don't
/// link them: most Eureka unlocks have no journal genre, and the Bozja genre (91) also holds relic quests.
/// Row ids from the Quest sheet; requirements and NPCs from the FFXIV Console Games Wiki.
/// </summary>
public static class QuestChains
{
    public static readonly IReadOnlyList<ChainStep> Eureka = new[]
    {
        new ChainStep(68614, 0, "Galiena · Rhalgr's Reach (9.8, 12.5)", "Eureka Anemos"),
        new ChainStep(68478, 20, "Rodney · Kugane (8.6, 14.2)", "Eureka Pagos"),
        new ChainStep(68148, 35, "Rodney · Kugane (8.6, 14.2)", "Eureka Pyros"),
        new ChainStep(68149, 50, "Rodney · Kugane (8.6, 14.2)", "Eureka Hydatos"),
    };

    public static readonly IReadOnlyList<ChainStep> Bozja = new[]
    {
        new ChainStep(69477, 0, "Marsak · Gangos", "Bozjan Southern Front"),
        new ChainStep(69479, 1, "Dmitar · Southern Front", ""),
        new ChainStep(69480, 2, "Dmitar · Southern Front", "Lost Actions"),
        new ChainStep(69481, 5, "Dmitar · Southern Front", "Old Bozja (area 2)"),
        new ChainStep(69482, 6, "Mikoto · Southern Front", ""),
        new ChainStep(69483, 7, "Mikoto · Southern Front", ""),
        new ChainStep(69484, 8, "Dmitar · Southern Front", "Alermuc Climb (area 3)"),
        new ChainStep(69485, 9, "Mikoto · Southern Front", ""),
        new ChainStep(69486, 9, "Lilja · Southern Front", ""),
        new ChainStep(69487, 10, "Bajsaljen · Southern Front", "Castrum Lacus Litore"),
        new ChainStep(69561, 10, "Marsak · Gangos", ""),
        new ChainStep(69562, 10, "Bajsaljen · Gangos", "Delubrum Reginae"),
        new ChainStep(69563, 15, "Troubled gentleman · Gangos · also needs 700,000 Mettle", "Delubrum Reginae (Savage)"),
        new ChainStep(69620, 0, "Marsak · Gangos · needs the 5.5 main story", "Zadnor"),
        new ChainStep(69621, 16, "Mikoto · Zadnor", ""),
        new ChainStep(69628, 18, "Dmitar · Zadnor", "Zadnor western plateau"),
        new ChainStep(69622, 20, "Mikoto · Zadnor", ""),
        new ChainStep(69629, 22, "Dmitar · Zadnor", "Zadnor northern plateau"),
        new ChainStep(69623, 23, "Bajsaljen · Zadnor", ""),
        new ChainStep(69624, 25, "Bajsaljen · Zadnor", "The Dalriada"),
    };

    private static readonly Dictionary<uint, string> Names = new();

    /// <summary>The quest's in-game name (repeatable-quest glyph trimmed).</summary>
    public static string Name(uint questId)
    {
        if (Names.TryGetValue(questId, out var n))
            return n;

        var row = Service.DataManager.GetExcelSheet<Quest>().GetRowOrDefault(questId);
        n = row is { } q ? q.Name.ExtractText().Trim('', ' ') : $"Quest {questId}";
        Names[questId] = n;
        return n;
    }

    public static bool Done(ChainStep step) => QuestManager.IsQuestComplete(step.QuestId);

    public static int DoneCount(IReadOnlyList<ChainStep> chain)
    {
        var n = 0;
        foreach (var s in chain)
            if (Done(s))
                n++;
        return n;
    }

    /// <summary>The first step not yet completed, or null when the chain is finished.</summary>
    public static ChainStep? Next(IReadOnlyList<ChainStep> chain)
    {
        foreach (var s in chain)
            if (!Done(s))
                return s;
        return null;
    }
}
