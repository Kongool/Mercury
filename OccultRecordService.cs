using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace Mercury;

/// <summary>A single Occult Record definition, loaded from the game's MKDLore sheet.</summary>
public sealed record OccultRecord(uint Id, string Name, string Description, uint Icon);

/// <summary>An Occult Crescent story quest (JournalGenre 111), loaded from the Quest sheet.</summary>
public sealed record OccultQuest(uint RowId, string Name, bool IsNorthHorn);

/// <summary>
/// Loads the static Occult Records and Occult Crescent quest line from Lumina, and
/// reads the player's live collection / quest-completion state out of the game client.
/// </summary>
public sealed class OccultRecordService
{
    // The Occult Crescent story quests all share this JournalGenre.
    private const uint OccultJournalGenre = 111;

    private readonly List<OccultRecord> records = new();
    private readonly Dictionary<uint, OccultRecord> recordsById = new();
    private readonly List<OccultQuest> quests = new();

    /// <summary>Every Occult Record that exists in the game (60 as of patch 7.55).</summary>
    public IReadOnlyList<OccultRecord> Records => this.records;

    /// <summary>Look up a record by its MKDLore row id, or null if not present.</summary>
    public OccultRecord? GetRecord(uint id) => this.recordsById.TryGetValue(id, out var r) ? r : null;

    /// <summary>The Occult Crescent story quest line, in journal order.</summary>
    public IReadOnlyList<OccultQuest> Quests => this.quests;

    public OccultRecordService()
    {
        var sheet = Service.DataManager.GetExcelSheet<MKDLore>();
        foreach (var row in sheet)
        {
            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue; // skip empty/placeholder rows

            var record = new OccultRecord(
                row.RowId,
                name,
                row.Description.ExtractText(),
                (uint)row.Image);
            this.records.Add(record);
            this.recordsById[record.Id] = record;
        }

        var questSheet = Service.DataManager.GetExcelSheet<Quest>();
        foreach (var q in questSheet)
        {
            if (q.JournalGenre.RowId != OccultJournalGenre)
                continue;

            var name = q.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // South Horn quests are the 708xx block; North Horn (7.55) are 710xx.
            this.quests.Add(new OccultQuest(q.RowId, name, q.RowId >= 71000));
        }
    }

    /// <summary>
    /// Reads the IDs of records the player has collected from
    /// <c>MKDLoreModule.SeenLore</c> - a persistent list backed by the character
    /// save file, so it is accurate even when the in-game book is closed.
    /// </summary>
    public unsafe HashSet<uint> GetCollectedIds()
    {
        var result = new HashSet<uint>();

        var module = MKDLoreModule.Instance();
        if (module == null)
            return result;

        ref var seen = ref module->SeenLore;
        for (var p = seen.First; p != seen.Last; p++)
            result.Add(*p);

        return result;
    }

    /// <summary>True if the given Occult Crescent quest has been completed.</summary>
    public unsafe bool IsQuestComplete(uint questRowId)
        => QuestManager.IsQuestComplete(questRowId);

    /// <summary>
    /// True when the player is inside an Occult Crescent instance
    /// (The South Horn or The North Horn). No hardcoded territory IDs required.
    /// </summary>
    public unsafe bool InOccultCrescent()
        => PublicContentOccultCrescent.GetInstance() != null;
}
