# Prototype: scenarios, goals and the five story levels

Status: built (2026-11-22). Decision: [ADR 0025](../adr/0025-scenarios-and-goals.md). Design: [modes, scenarios and resources](../superpowers/specs/2026-11-22-modes-and-scenarios-design.md), part B. Code: `src/Starfoundry.Sim/ScenarioDef.cs`, `ScenarioCatalog.cs`, `BeltNetwork.Counters.cs`, `SimWorld.CreateScenario`, `src/Starfoundry.Ui/ScenarioProgress.cs`, the bot in `tools/Starfoundry.Balance/StructureBot.Scenario.cs`. Data: `content/scenarios/*.json`. Tests: `ScenarioTests`, `ScenarioProgressTests`, `ScenarioContentTests`.

## The question
Can a level be a data file (rules, start, goals, story), checked by tests and a scripted player, with a goals engine that does not need the game engine?

## The schema
Every file starts with `"schema": 1`. The id equals the file name. Unknown properties are errors.

| Field | Meaning |
| --- | --- |
| `id`, `order` | Unique id (lower case, digits, underscores) and the position in the level list |
| `name_es/en`, `description_es/en`, `briefing_es/en` | Texts (name up to 60 characters, description 400, briefing 1500) |
| `mode` | `campaign` (default: costs and research gates) or `sandbox` (everything unlocked and free) |
| `player_rules` | True for the campaign and the sandbox: the game lets the player choose the rules and the seed (the file's own rules are the default) |
| `rules` | `preset` (`relaxed`, `normal`, `hard`, `brutal`) plus optional overrides of any `WorldRules` field: `enemy_damage_percent`, `enemy_hp_percent`, `wave_size_percent`, `wave_interval_percent`, `first_wave_grace_seconds`, `deposit_richness_percent`, `power_output_percent`, `threats_enabled`, `free_build`, `goal_waves`, `starting_items` |
| `start.structures` | Templates built at world creation: `template` (a content `structure_templates` id), `kind` (`ship` or `station`; the first is the player's ship), `offset` (cells from the home origin). Default: the starter ship |
| `start.starting_items` | Stacks added to the hold (merged with `rules.starting_items`) |
| `start.seed` | A fixed galaxy seed, or absent/0 for "the game picks one" |
| `start.home_archetype` | Optional; only the content's home archetype is accepted today |
| `goals.primary`, `goals.secondary` | Lists of goals (below); the scenario is won when every primary goal is done |
| `story` | Beats: `trigger`, optional `speaker` (default ORCA), `text_es`, `text_en` |
| `stars` | `two_star_seconds`, `three_star_seconds` (0 = no bound), `two_star_secondary`, `three_star_secondary` (a count, or -1 = all) |

Goals: `id`, `type`, its fields, `text_es`, `text_en` (up to 160 characters) and an optional `time_limit_seconds` (at least 30).

| Type | Fields | Done when |
| --- | --- | --- |
| `survive_waves` | `count` (omit for the rules' goal waves) | that many waves were cleared |
| `research` | `node` | the node is researched |
| `produce` | `item`, `count` | that many were made (crafting, drills, hand mining, drones) |
| `mine` | `item`, `count` | that many were taken out of deposits (hand, drones, drills) |
| `visit_systems` | `count` | the player has been in that many systems, home included |
| `found_stations` | `count` | that many stations were founded |
| `build_module` | `kind`, `count` | that many modules of the kind were placed (template modules do not count) |
| `keep_alive` | `kind` (omit for any kind) | never "done": fails when such a module is destroyed |
| `keep_fuel` | none | never "done": fails when a reactor runs dry (no hydrogen in the owner's hold) |

Story triggers: `start`, `win`, `lose`, `goal:<id>` (a goal is done), `fail:<id>` (a goal failed), `time:<seconds>` (that much play time has passed). Each beat fires once.

An example (the first level, shortened):
```json
{
  "schema": 1, "id": "01_first_light", "order": 1,
  "name_es": "Primera luz", "name_en": "First Light",
  "description_es": "...", "description_en": "...", "briefing_es": "...", "briefing_en": "...",
  "rules": { "preset": "relaxed", "threats_enabled": false },
  "start": { "seed": 20261121 },
  "goals": {
    "primary": [
      { "id": "mine_iron", "type": "mine", "item": "core:iron_ore", "count": 60, "text_es": "...", "text_en": "Mine 60 iron ore" },
      { "id": "found_station", "type": "found_stations", "count": 1, "text_es": "...", "text_en": "Found a station" }
    ],
    "secondary": [
      { "id": "quick_station", "type": "found_stations", "count": 1, "time_limit_seconds": 480, "text_es": "...", "text_en": "Found the station in under 8 minutes" }
    ]
  },
  "story": [ { "trigger": "start", "text_es": "...", "text_en": "Good morning. ..." } ],
  "stars": { "two_star_seconds": 900, "three_star_seconds": 480, "two_star_secondary": 0, "three_star_secondary": -1 }
}
```

## The goals engine
`new ScenarioProgress(content, scenario)`, then `Update(net, player)` every frame or tick-batch. It returns the story beats that fired in that call. `Goals` has a `GoalState` per goal (status, current and target for a progress bar, when it was done or failed), `Outcome`/`Reason` (`Playing`, `Won`, `Lost`; reasons `WavesSurvived`, `GoalsDone`, `ShipLost`, `GoalFailed`), `Stars`, `Story` and `SecondaryCount`. Rules worth knowing: a done goal stays done; a goal that is not done when its time limit passes (or met only after it) is failed, and a failed primary goal loses the game; a keep goal that never failed counts as done when the scenario is won; a won or lost game is final. The tracker is recomputed from the world, so after loading a save the completion times start again unless the game stores them.

## The levels
Measured with the scenario mode of the structure bot (`dotnet run --project tools/Starfoundry.Balance -- --scenario=<id> [--trace]`), action delay 6 s, the level's own seed. The bot plays levels 1 to 3 with real commands only. Levels 4 and 5 are validated by reachability analysis (`ScenarioContentTests`), not played.

| Level | Rules | Start | Primary goals | Secondary | Bot result |
| --- | --- | --- | --- | --- | --- |
| `01_first_light` | Relaxed, threats off, seed 20261121 | Starter ship | Mine 60 iron ore, make 10 iron plates, found a station | Found the station in under 8 minutes | Won at 1:51, 3 stars |
| `02_cold_fuel` | Relaxed, threats off, seed 20261121 | `cold_ship` (6 hydrogen), 20 plates and 30 ingots | Make 10 hydrogen, build a reactor module, found a station | Never let a reactor run dry | Won at 23:10, 2 stars; hydrogen made at 1:09, so the reactor never ran dry |
| `03_under_siege` | Normal with first wave grace 120 s, wave interval 25 %, wave size 130 %, 5 goal waves, seed 20261121 | `siege_ship` (eight cargo modules) and `siege_station` (core, workshop with smelter, assembler, depot, ports and two ammo belts, an empty defence module, a reactor) 10 cells to the side; 80 plates, 100 ingots, 80 extra hydrogen | Survive 5 waves; keep the station core | Lose no module | Won at 17:35, 3 stars, no module lost; wave 1 warning at 3:03 |
| `04_deep_vein` | Normal, first wave grace 900 s, seed 20261121 | Starter ship | Research titanium smelting and advanced plating, visit 2 systems, make 20 titanium plates | The plates in under 40 minutes | Not played; the galaxy has 2155 titanium ore, 2526 nickel ore and 1118 quartz within three jumps of home against 120, 80 and 30 needed |
| `05_long_watch` | Hard (12 waves), random seed | Starter ship | Survive the waves, found a station, visit 2 systems; keep the core | Lose no module | Not played; the standard campaign on Hard is measured in [difficulty.md](difficulty.md) (the bot wins Hard on three seeds at minutes 45 to 50) |
| `campaign` | Player's choice (default Normal) | Starter ship | Survive the goal waves, found a station, visit 2 systems | none | Not played here; the standard bot wins the campaign ([structure-bot.md](structure-bot.md)) |
| `sandbox`, `sandbox_station` | Player's choice, sandbox mode, threats off | Starter ship (plus a bare station core 10 cells to the side) | none | none | Never ends |

Design decisions the numbers come from:
- **Capacity.** The starter hold is 60 and hand mining stops at 75 percent of it, so level 3 gives its ship seven more cargo modules instead of 200 plates and 200 ingots in a 60-slot hold, and level 2 starts with 20 plates and 30 ingots (a cracker and an ice port) that the first builds spend.
- **No ammo item.** Turrets are fed by belts from the station's own ammo assembler, which takes ingots from the hold; ammo in the hold would never reach a turret, so level 3 starts with ingots.
- **Waves.** The alert meter fills at the signature above the threshold, so level 3 shortens the wave interval to 25 percent to bring five waves into about 17 minutes. The first turret is up at minute 0:01 for the bot; a person has until minute 3 (the first warning).
- **The fuel warning** of level 2 is a story beat at 120 s: six hydrogen last 180 s (each reactor burns one per 30 s).
- **Levels 2 and 3 drop two ideas of the first draft**: "keep the reactor burning to the end" became the secondary `keep_fuel`, because a primary one would end the game the first time a reactor dipped; "start in the ship and the station with 200 plates" became the eight-cargo ship above.

## How to add a level
1. Write `content/scenarios/<id>.json` (id equals the file name; pick an unused `order`). Reuse a template from `structure_templates` in `content/core.json` or add one there (modules face-connected, machines and belts inside modules that allow them; the template's `hold` is the owner's hold at the start).
2. Put the numbers in the rules block, the items in `start.starting_items` (they must fit the ship's hold under the 75 percent mining limit), a fixed seed when the level needs known deposits.
3. Write the texts in es and en (the content test checks both exist and differ), the story beats and the star rules.
4. Run `dotnet test`: the catalog test counts the files and `ScenarioContentTests` checks texts, numbers and that every goal is deliverable. Add the level to `StructureBot.PlayableScenarios` with a plan in `StructureBot.Scenario.cs` and a `Bot_wins_level_NN` test, or state in the table above that it is validated by reachability only.
5. Add the row to the table above with the measured time.

## The game UI (part D)
Built: the New Game screen (`game/NewGameScreen.cs`: Campaign, Story, Sandbox), the goals section of the HUD (`GoalsView` builds it from `ScenarioProgress`), the briefing dialog and story toasts, the scenario end screens with stars and records, `progress.cfg` (`ProgressRecord`), `sandbox_presets.cfg` (`SandboxPresets`) and the `<slot>.goals` sidecar that fixes the "completion times restart after a load" gap (`ProgressSnapshot`; ignored when its tick does not match the save). How to use it is in [the play-test guide](../playtest.md). The sandbox form picks one of six open-ended scenarios: `sandbox`, `sandbox_station`, `sandbox_siege` (free build, sandbox mode) and `custom`, `custom_station`, `custom_siege` (costs and research gates, campaign mode, no goals), because the world mode belongs to the scenario and is part of its hash.

## What is not done
Packaging the `content/scenarios` folder in the export (the loaders take a directory next to `core.json`); a bot plan for levels 4 and 5; the campaign and the sandbox are not played by the scenario bot.
