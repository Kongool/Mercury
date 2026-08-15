# Mercury

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **FINAL FANTASY XIV** that tracks your **Occult Records** collection in the Occult Crescent (The South Horn / The North Horn).

## What it does

- **Auto-opens** the records window the moment you enter an Occult Crescent instance, and closes it when you leave.
- Lists **every Occult Record** in the game (loaded from the game's `MKDLore` data), each with its icon, plus a collected-progress bar.
- **Three tabs**, each with a live count:
  - **Acquired** - records you've collected.
  - **Missing** - every record you haven't collected, each with its acquisition hint.
  - **Quests** - the full Occult Crescent story quest line (read live from the game), grouped by South/North Horn, with a green check on completed quests and a `done / total` count on the tab.
- Every record shows **how to get it** underneath its name - the exact quest name for quest-gated records (highlighted), or the survey-point coordinates / Critical Encounter / Forked Tower source for the rest.
- A **CEs tab** shows live Critical Encounter timers - every active/upcoming CE with its status (join window / starting / active with progress), player count, and a countdown, read straight from the game's dynamic-event data.
- A **Map tab** renders the real in-game zone map (loaded from the game's own map texture) with every survey point plotted on it - green if collected, amber if still to find - plus live **Critical Encounter markers** (red) at their real world positions. Hover any marker for details. Follows your current zone, or switch between South Horn and North Horn.
- **Live and accurate:** your collected state is read from the game's persistent lore book (`MKDLoreModule.SeenLore`), so it reflects real progress whether or not the in-game book is open.
- Search by name, and an optional inline descriptions view (hover a record for its description otherwise).

Type `/mercury` to toggle the window manually at any time.

## How it works (for the curious)

| Concern | Source |
| --- | --- |
| List of records | Lumina `MKDLore` sheet (`Name`, `Description`, `Image`) |
| Your collected records | `MKDLoreModule.Instance()->SeenLore` (a `StdVector<byte>` of collected IDs) |
| How each record is acquired (quest name, coords, CE, etc.) | Curated table in `RecordSources.cs`, keyed by `MKDLore` row id — the game exposes no per-record source |
| Occult Crescent quest line + completion | `Quest` sheet filtered to JournalGenre 111; completion via `QuestManager.IsQuestComplete(rowId)` |
| Live Critical Encounter timers | `DynamicEventContainer.GetInstance()->Events` (`State`, `SecondsLeft`, `Progress`, `Participants`) |
| Zone map image | Game map texture via `ITextureProvider.GetFromGame` (South Horn `o6b1/01`, North Horn `o6b2/01`) |
| Survey-point placement | Map coordinate → texture pixel: `(coord - 1) / 41` (both maps are `SizeFactor` 100) |
| "Am I in the Occult Crescent?" | `PublicContentOccultCrescent.GetInstance() != null` — true in both South Horn and North Horn |

## Building

Requires the **.NET 10 SDK** (`dotnet --version` should report 10.x) and an installed Dalamud (for testing). The build itself pulls Dalamud/Lumina/FFXIVClientStructs from NuGet via `Dalamud.NET.Sdk`, so no local reference paths are needed.

```bash
dotnet build -c Release
```

The built `Mercury.dll` (plus its generated `Mercury.json` manifest) lands in `bin/Release/`.

## Installing for testing (dev plugin)

1. Build in `Release`.
2. In-game, open Dalamud settings (`/xlsettings`) → **Experimental** → **Dev Plugin Locations**.
3. Add the folder containing `bin/Release/Mercury.dll` (or the `Mercury.json`).
4. Open the plugin installer (`/xlplugins`) → **Dev Tools** and enable **Mercury**.

## Notes

- Built against **Dalamud API 15 / .NET 10** (`Dalamud.NET.Sdk/15.0.0`), matching the current live Dalamud.
- To publish to a plugin repo you'll additionally want an `images/icon.png` (512×512) referenced by the manifest.
