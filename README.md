# Mercury

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **FINAL FANTASY XIV** field operations: the **Occult Crescent** (South Horn / North Horn), **Eureka** (Anemos, Pagos, Pyros, Hydatos) and **Save the Queen** (Bozjan Southern Front, Zadnor).

## What it does

- **Lives in an edge tab.** A slim "Mercury" tab sits on the screen edge, alongside the Daedalus and Charon tabs. Point at it to slide Mercury out, and click it to keep it out. Drag it to any edge. There is no separate window and nothing pops up on its own.
- **Shows only the zone you're in.** The sidebar lists the pages for your current zone, plus **Progress** and **Settings**. A setting lists every zone's pages instead.
- **Progress page:** your Elemental Level, Resistance Rank and Knowledge Level (remembered from the last time you were in each zone), which areas you've unlocked, and the next quest in each chain.
- **Eureka:** Elemental Level and EXP, weather forecast, elemental wheel, live notorious monsters, the four zone-unlock quests, and the 10 weekly Eureka Challenge Log entries.
- **Bozja / Zadnor:** Resistance Rank and Mettle, live Critical Engagements and skirmishes, weather forecast, and the Save the Queen story chain with the rank each step needs.
- **Occult Crescent:**
  - Lists **every Occult Record** in the game (loaded from the game's `MKDLore` data), each with its icon, split into **Missing** and **Acquired**, plus a collected-progress bar.
  - The **Quests** page shows the full Occult Crescent story quest line (read live from the game), grouped by South/North Horn, with a green check on completed quests.
- Every record shows **how to get it** underneath its name - the exact quest name for quest-gated records (highlighted), or the survey-point coordinates / Critical Encounter / Forked Tower source for the rest.
- The **Critical Encounters** page shows live CE timers - every active/upcoming CE with its status (join window / starting / active with progress), player count, and a countdown, read straight from the game's dynamic-event data.
- The **Weather** page (North Horn) calculates the deterministic weather cycle, shows the current weather and countdown, maps every weather to its weather-specific mob, and lists upcoming Crescent Bomb / Crescent Fool windows in the computer's local time.
- The **Pots** page reads the live public-content instance timer, shows elapsed/remaining time, detects a fresh North Horn instance, and counts down to the first expected spawn: **Daylight Pottery (north)** at approximately 20 minutes.
- The **Map** page renders the real in-game zone map (loaded from the game's own map texture) with every survey point plotted on it - green if collected, amber if still to find - plus live **Critical Encounter markers** (red) at their real world positions. Hover any marker for details. Zoom with the mouse wheel or `-` / `+` buttons, pan with middle/right drag, use **Reset** to return to the full map, and adjust the persistent **Map size** slider from 400 to 1000 pixels. Follows your current zone, or switch between South Horn and North Horn.
- **Live and accurate:** your collected state is read from the game's persistent lore book (`MKDLoreModule.SeenLore`), so it reflects real progress whether or not the in-game book is open.
- Search by record or weather-mob name. Matches such as `fool`, `bomb`, or `mimic` show the required weather, whether the mob is up now, and its next three exact spawn windows. An optional inline descriptions view is available for records (hover a record for its description otherwise).

Type `/mercury` to pin or unpin the edge tab's panel. If the tab is switched off, `/mercury` turns it back on.

Research notes, game data ids and what's still to build are in [docs/field-ops-research.md](docs/field-ops-research.md).

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

## Installing

1. In-game, open Dalamud settings (`/xlsettings`) → **Experimental** → **Custom Plugin Repositories**.
2. Add `https://raw.githubusercontent.com/Kongool/Mercury/main/repo.json`, tick **Enabled**, and save.
3. Open the plugin installer (`/xlplugins`), search for **Mercury**, and install it.

## Installing for testing (dev plugin)

1. Build in `Release`.
2. In-game, open Dalamud settings (`/xlsettings`) → **Experimental** → **Dev Plugin Locations**.
3. Add the folder containing `bin/Release/Mercury.dll` (or the `Mercury.json`).
4. Open the plugin installer (`/xlplugins`) → **Dev Tools** and enable **Mercury**.

## Notes

- Built against **Dalamud API 15 / .NET 10** (`Dalamud.NET.Sdk/15.0.0`), matching the current live Dalamud.
- The publishable plugin icon is available at `images/icon.png` (512×512, transparent PNG).
