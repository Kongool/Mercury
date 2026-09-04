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
    int RewardAmount,
    int Reward0Type,
    int Reward1Type,
    byte MenuOrder)
{
    /// <summary>
    /// Human-readable reward, resolved from the ContentsNote reward-type codes. The Occult
    /// Crescent Field Ops entries reward Knowledge EXP (type 12) + Phantom EXP (type 13,
    /// amount in the "gil" field), not gil; Eureka entries reward EXP + gil.
    /// </summary>
    public string RewardText()
    {
        var parts = new List<string>(2);

        // The amount-bearing slot (the sheet's "GilRward" field carries this slot's value).
        switch (this.Reward1Type)
        {
            case 13: parts.Add($"{this.RewardAmount:N0} Phantom EXP"); break;
            case 3: parts.Add($"{this.RewardAmount:N0} gil"); break;
            case 8: parts.Add($"{this.RewardAmount:N0} MGP"); break;
            case 6: parts.Add($"{this.RewardAmount:N0} GC seals"); break;
            case 11: parts.Add($"{this.RewardAmount:N0} cowries"); break;
        }

        // The EXP-type slot. The game scales and shows these in the zone ("applicable
        // areas"), so no fixed number is listed - render the type as a label.
        switch (this.Reward0Type)
        {
            case 12: parts.Add("Knowledge EXP"); break;
            case 5 or 10 or 14 or 15: parts.Add("EXP"); break;
        }

        return string.Join("  -  ", parts);
    }
}

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
                n.Reward0,
                n.Reward1,
                n.MenuOrder));
        }

        this.entries.Sort((a, b) => a.MenuOrder != b.MenuOrder
            ? a.MenuOrder.CompareTo(b.MenuOrder)
            : a.RowId.CompareTo(b.RowId));
    }
}
