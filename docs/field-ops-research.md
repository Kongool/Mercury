# Field ops research and redesign notes

Notes from the November 2026 redesign that took Mercury from Occult Crescent only to Eureka and Bozja
as well. Covers what was decided, what's built, what isn't, and the game data the new pages read.

UI mockup (private, share it from its Share menu): https://claude.ai/artifact/XNdZh959S613NPhT3pwi5g

## Decisions

- **Look:** the same shell as the newer plugins (Charon, Argus, Hephaestus): a header with the zone and
  level, a sidebar of pages with count badges, and pages built from cards. Theme and widgets are ported
  from Hephaestus (`Windows/MercuryTheme.cs`, `Windows/Components/`), in Daedalus gold.
- **Zone-aware:** the sidebar lists only the zone you're in, plus Progress and Settings. The "Show pages
  for other zones" setting lists every field op's collection and story pages.
- **Tabs only:** Mercury has no window of its own and never pops up. It shows only in the edge-dock tab.
  `/mercury` and the installer's Open / Settings buttons pin the tab open, and turn it back on if it was off.

## Status

Built:
- Occult Crescent pages, moved into the sidebar (unchanged otherwise).
- Eureka (Anemos, Pagos, Pyros, Hydatos): overview, notorious monsters, weather forecast, story & unlocks.
- Bozjan Southern Front and Zadnor: overview, Critical Engagements, skirmishes, weather, story & unlocks.
- Progress page: last-seen levels per character, unlock status, and the next quest in each chain.
- Settings page.

Not built yet (from the mockup):
- Bozja Field Records: which records you have isn't cleanly readable (see below).
- Lost Actions holster panel.
- Eureka NM spawn conditions (trigger mob, weather, night). Not in any game sheet; needs a hand-written table.
- Magia board slot progress.
- Maps for Eureka and Bozja (the Map page is still Occult only).
- A choice of accent colour (gold only).

Not yet checked in game:
- Elemental Level / EXP and the elemental wheel inside Eureka.
- Resistance Rank / Mettle and the CE list inside Bozja and Zadnor. The CE list first tries
  `DynamicEventContainer.GetInstance()`, then falls back to Bozja's own container (`FieldEvents.cs`).
- Completion of the hidden quests (Journal genre 0), e.g. A Seaside Story and the later Eureka unlocks.
  `QuestManager.IsQuestComplete` should work on them, but it hasn't been confirmed.

## Shared game data

Local FFXIVClientStructs at the time: 7.57.0.9414 (commit dd57986b, 2026-10-20).

- **Levels:** `PlayerState.Instance()->GetContentValue(key)`. Only loaded inside the content, so Mercury
  saves the last value per character (`FieldProgress.cs`, keyed by `PlayerState.ContentId`).

  | Key | Content | Value |
  |---|---|---|
  | 2 | Eureka | Effective (synced) Elemental Level |
  | 3 | Eureka | Is Elemental Level synced |
  | 4 | Eureka | Current Elemental Level |
  | 5 | Bozja | Current Resistance Rank |
  | 6 | Occult Crescent | Effective Knowledge Level |
  | 7 | Occult Crescent | Current Knowledge Level |

- **Weather:** deterministic. Rates come from the Lumina `WeatherRate` sheet; `ZoneWeather.cs` reuses the
  roll from `OccultWeatherForecast`.

## Eureka

| Zone | Elemental Level | TerritoryType | Map row | Map Id | WeatherRate |
|---|---|---|---|---|---|
| Anemos | 1–20 | 732 | 414 | z3fa/00 | 91 |
| Pagos | 20–35 | 763 | 467 | z3fb/00 | 94 |
| Pyros | 35–50 | 795 | 484 | z3fc/00 | 96 |
| Hydatos | 50–60 | 827 | 515 | z3fd/01 | 100 |

- **Quests (hardcoded in `QuestChains.cs`):** only the first one has a journal genre (90). The rest are
  genre 0 "quasi-quests"; their internal Ids share the prefix `StmBdy40x`.

  | Row | Quest | Needs | NPC | Unlocks |
  |---|---|---|---|---|
  | 68614 | And We Shall Call It Eureka | Lv 70 + Stormblood MSQ | Galiena, Rhalgr's Reach (9.8, 12.5) | Anemos |
  | 68478 | And We Shall Call It Pagos | Elemental Lv 20 + Anemos story | Rodney, Kugane (8.6, 14.2) | Pagos |
  | 68148 | And We Shall Call It Pyros | Elemental Lv 35 + Pagos story | Rodney | Pyros |
  | 68149 | And We Shall Call It Hydatos | Elemental Lv 50 + Pyros story | Rodney | Hydatos |

- **The story steps with Krile inside each zone aren't quests** (they're `CtsErkEureka*` event sheets), and
  no ClientStructs field for them was found, so Mercury can't track them.
- **Zone detection:** TerritoryType in {732, 763, 795, 827}, or
  `EventFramework.GetPublicContentDirectorByType(PublicContentDirectorType.Eureka)`.
- **Live state:** `PublicContentEureka.GetState()` returns `EurekaState`: `CurrentExperience`,
  `NeededExperience`, `Fire`/`Ice`/`Wind`/`Earth`/`Lightning`/`Water`, `Magicite`, `MagiaAether`,
  `MagiaAetherCharge`. The director also has `MaxElementalLevel` (0x1384).
- **NMs** are FATEs with `FateMode` 1 (Fate sheet). They respawn about 2 hours after defeat. Spawn rules are
  not in any sheet.
- **Weather rates:**
  - Anemos: Fair 30 / Gales 30 / Showers 30 / Snow 10
  - Pagos: Fair 10, then Fog / Heat Waves / Snow / Thunder / Blizzards at 18 each
  - Pyros: Fair 10, then Heat Waves / Thunder / Blizzards / Umbral Wind / Snow at 18 each
  - Hydatos: Fair 12, then Showers / Gloom / Thunderstorms / Snow at 22 each
- **Baldesion Arsenal:** Hydatos story + Elemental Lv 60. Portals open after the NM FATE "I Don't Want to
  Believe" (Umbral Turbulence weather, which is server-driven), inside Hydatos.
- **Other sheets:** `EurekaMagiciteItem`, `EurekaMagiaAction`, `EurekaAetherItem`, `EurekaAethernet`,
  `EurekaGrowData`.

## Bozja (Save the Queen)

- **Resistance Rank** caps at 25 and is one rank shared by both zones (Zadnor doesn't have its own).
- **Zones:**
  - Bozjan Southern Front: TerritoryType 920, WeatherRate 124, map 606 `n4b4/01`.
  - Zadnor: TerritoryType 975, WeatherRate 130, map 665 `n4b6/01`.
  - Delubrum Reginae: 936 (Savage 937).
  - Castrum Lacus Litore and the Dalriada run inside their zone's territory (DynamicEvent 16 and 32).
- **Zone detection:** `PublicContentBozja.GetInstance() != null`.
- **Mettle:** `PublicContentBozja.GetState()` returns `BozjaState` (`CurrentExperience`, `NeededExperience`,
  and `HolsterActions` with 100 MYCTemporaryItem ids).
- **Quests:** journal genre 91, which also holds the relic quests. Hidden genre-0 ones have Id prefix
  `LucKsa`. Trim the leading U+E0BF glyph from repeatable quest names.
- **Story chain (hardcoded in `QuestChains.cs`):**

  | Row | Quest | Rank | Unlocks |
  |---|---|---|---|
  | 69477 | Where Eagles Nest | – | Bozjan Southern Front |
  | 69479 | An Expected Engagement | 1 | |
  | 69480 | Lost No Longer | 2 | Lost Actions |
  | 69481 | On the Offensive | 5 | Old Bozja (area 2) |
  | 69482 | Time to Focus | 6 | |
  | 69483 | Third Time's the Charm | 7 | |
  | 69484 | Pressing Forward | 8 | Alermuc Climb (area 3) |
  | 69485 | Signature Acquired | 9 | |
  | 69486 | Picking Up the Trail | 9 | |
  | 69487 | The Lady of Blades | 10 | Castrum Lacus Litore |
  | 69561 | A Sign of What's to Come | 10 | |
  | 69562 | Fit for a Queen | 10 | Delubrum Reginae |
  | 69563 | A Seaside Story | 15 + 700k Mettle | Delubrum Reginae (Savage) |
  | 69620 | A New Playing Field | – (needs MSQ 69552) | Zadnor |
  | 69621 | Resolve Unshaken | 16 | |
  | 69628 | Where Eagles Roost | 18 | Zadnor western plateau |
  | 69622 | Reaching Out | 20 | |
  | 69629 | In Their Shadow | 22 | Zadnor northern plateau |
  | 69623 | Renewed Focus | 23 | |
  | 69624 | March of the Bloody Queen | 25 | The Dalriada |

  Not in the chain: 69478 Memoirs from the Front, 69564 One Man's Relic (weekly), 69565 Let Me Holster That
  for You, 69630 An Honor to Serve, 69631 A Ruined Opportunity.
- **Relic weapon quests:**
  - Lead-in: 69370 → 69371 → 69372.
  - Resistance: 69380 / 69381.
  - Augmented: 69505 / 69506.
  - Recollection: 69507.
  - Law's Order: 69573 / 69574.
  - Augmented Law's Order: 69575 / 69576.
  - Blade's: 69632–69637.
- **CEs:** DynamicEvent rows 1–16 are the Southern Front (16 = CLL); 17–32 are Zadnor (32 = Dalriada).
- **Field Records:** `MYCWarResultNotebook` rows 1–50 (1–20 Southern Front, 21–30 Delubrum, 31–50 Zadnor).
  Which ones you have **isn't cleanly readable**. Two candidates to test in game:
  - `MycNoteModule` `Unk48` / `Unk80` (two 55-entry bool arrays; meaning unverified).
  - Reading the in-game Field Records window (`AddonMYCWarResultNotebook`) while it's open, like
    `ChallengeLogScraper`.
- **Clusters:** item 31135.

## Sources

- xivapi v2 (live sheets): Quest, JournalGenre, TerritoryType, Map, WeatherRate, Fate, MYCWarResultNotebook
- FFXIVClientStructs (github.com/aers/FFXIVClientStructs) and the local DLL / XML doc tables
- FFXIV Console Games Wiki: Eureka, Bozja and Field Notes pages
