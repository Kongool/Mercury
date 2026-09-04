using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace Mercury;

/// <summary>A single Challenge Log entry, loaded from the game's ContentsNote sheet.</summary>
public sealed record ChallengeEntry(
    uint RowId,
    string Name,
    string Description,
    int RequiredAmount,
    int Gil,
    byte MenuOrder);

/// <summary>
/// Loads the "Field Operations" Challenge Log entries from the game's ContentsNote sheet -
/// the weekly Occult Crescent challenges (Occult Interloping, Critically Endangered,
/// Fateful Contention, Make It Chain) plus the Eureka "Forbidden ..." entries. This is
/// static reference data; live weekly completion is read from the client
/// (see <see cref="OccultRecordService.IsChallengeComplete"/>) and the running counts and
/// EXP reward are scraped from the open in-game window (see <see cref="ChallengeLogScraper"/>),
/// as the game exposes neither persistently.
/// </summary>
public sealed class ChallengeLog
{
    private readonly List<ChallengeEntry> entries = new();

    /// <summary>The category's display name ("Field Operations"), from ContentsNoteCategory.</summary>
    public string CategoryName { get; } = "Field Operations";

    /// <summary>The Field Operations entries, in the game's menu order.</summary>
    public IReadOnlyList<ChallengeEntry> Entries => this.entries;

    public ChallengeLog()
    {
        // Resolve the Field Operations category by name, so it survives any future
        // reordering of the ContentsNoteCategory sheet.
        uint fieldOpsId = 0;
        foreach (var c in Service.DataManager.GetExcelSheet<ContentsNoteCategory>())
        {
            var catName = c.Name.ExtractText();
            if (catName.Equals("Field Operations", StringComparison.OrdinalIgnoreCase))
            {
                fieldOpsId = c.RowId;
                this.CategoryName = catName;
                break;
            }
        }

        if (fieldOpsId == 0)
            return;

        foreach (var n in Service.DataManager.GetExcelSheet<ContentsNote>())
        {
            if (n.ContentType.RowId != fieldOpsId)
                continue;

            var name = n.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            this.entries.Add(new ChallengeEntry(
                n.RowId,
                name,
                n.Description.ExtractText(),
                n.RequiredAmount,
                n.GilRward,
                n.MenuOrder));
        }

        this.entries.Sort((a, b) => a.MenuOrder != b.MenuOrder
            ? a.MenuOrder.CompareTo(b.MenuOrder)
            : a.RowId.CompareTo(b.RowId));
    }
}
