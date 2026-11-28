# Prototype: defence and survival of structures

Status: built (2026-11-21), simulation, content and Ui logic. Decision: [ADR 0021](../adr/0021-structure-defence.md). Roadmap: part 4 of the [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md).

## What was built
| Area | Change |
| --- | --- |
| Content | `structures.defence`: 400 hp (was 250), `defence_bonus_percent` 10; `threat.module_regen_seconds` 10 and `threat.first_wave_grace_seconds` 480 |
| Simulation | Module hit points (damaged modules only), targeting of modules, module destruction with contents, station loss, ship loss, bay and passive repair, defence bonus, first wave grace |
| Accessors | `ModuleHp`, `ModuleMaxHp`, `StructureHpFraction` (per mille), `ShipLost(player)`, `StationsLost`, `ModulesLost`, `DefenceBonusPercent(structure)`, `ModuleWorldCentre`, `ModuleHits` (events), `TakeDestroyedSegments`, `EnemyInfo.TargetStructure/TargetMx/TargetMy/TargetMz` |
| Ui logic | Toasts `toast.moduleDestroyed`, `toast.stationLost`, `toast.shipLostCockpit` (and `toast.waveWarning` as before); tip `learn.defence` at the first warning without a turret; objective `Defend` needs a powered, loaded turret in a defence module in structure worlds; play log lines |

## Rules worth remembering
- Targets in order: walls (decoys), the ship's nearest module within 15 cells, otherwise the nearest module of a structure that holds a machine or is a ship. Machines inside a module are not hurt by hits on it.
- A module at 0 is removed with the machines and belt segments inside it. A station dies with its core. The ship is lost with its last cockpit and does not respawn.
- Repair: a powered bay (ingot per heal) mends the first damaged module of its structure in range, 20 hp per 30 ticks; every module regenerates 1 hp per 10 s at satisfaction 1000.
- Turret damage rises 10 percent per defence module, capped at 50.
- The first warning of a system waits for 8 minutes spent above the signature threshold.

## Measured numbers
Measured by `StructureDefenceTests.Measure_what_a_wave_does_and_what_holds_it` and the other tests (30 ticks a second, seed 20261121, one home system, 70 cells from the node to the base).

Time for wave 0 (two scouts, 4 damage every 20 ticks each) to destroy a lone undefended module:

| Module | Hit points | After the spawn | After the first hit |
| --- | --- | --- | --- |
| Cockpit | 150 | 1005 ticks (33.5 s) | 360 ticks (12 s) |
| Workshop | 200 | 1145 ticks (38.2 s) | 500 ticks (16.7 s) |
| Core | 300 | 1385 ticks (46.2 s) | 740 ticks (24.7 s) |
| Defence | 400 | 1645 ticks (54.8 s) | 1000 ticks (33.3 s) |

An undefended ship of three modules (cockpit, engine, workshop) is lost to wave 0 in 2462 ticks (82 s); an undefended two-module station is razed in 2285 ticks (76 s).

Ammo-fed turrets in one defence module next to a cockpit, a workshop and an engine, waves 0 to 4 in sequence (a 120 s pause between waves, no repair bay), module lost means a wave got through:

| Turrets | Result |
| --- | --- |
| 0 | wave 0 destroys the whole ship |
| 1 | holds waves 0 to 2, loses a module in wave 3 |
| 2 | holds waves 0 to 4 (structure at 888 per mille after wave 3) |
| 3 to 5 | hold waves 0 to 4 with 954 to 990 per mille left after wave 4 |
| 6 to 8 | hold waves 0 to 4 without losing hit points |

Defence bonus: with three turrets, killing waves 0 to 2 took 33 shots with one defence module and 30 with five.

First wave: the starter ship plus six labs in its workshop (signature 32, 24 above the threshold) fills the alert at tick 9003 (5.0 min); the warning comes at tick 14400 (8 min, the grace) and the first wave arrives at tick 15299 (8.5 min). A world with a quieter base is later: the grace counts time above the threshold, and the alert needs 216000 divided by the excess in ticks.

## Notes
- Turrets (range 18 cells) cover the modules next to them: an enemy attacking a module far from the turrets is out of reach, so spread-out bases need turrets in more than one defence module.
- The old hand repair command still heals machines only.

## Game
What the player sees (game project, no simulation changes). `Play.Defence.cs` reads `ModuleHits` after every tick, because the sim clears the list each tick and the game steps several ticks a frame at higher speeds.

| Area | Behaviour |
| --- | --- |
| Damage | `Models.SetDamage` on the module model (darker, redder, dimmer lights); below 50 % hp a `CpuParticles3D` of sparks and smoke and a flickering orange light, stronger with the damage |
| Hits | the module's box flashes for 0.3 s; a hit on the player's ship raises a red vignette (decays in about a second); enemy beams are thick and end at the module centre (`EnemyInfo.Target*`) |
| Falls | an explosion (ball, particles, light) where the module stood; a station collapses in a chain of blasts over its modules ending in a shockwave ring; positions come from a snapshot of the structures kept while enemies are about, since the structure may be gone when the event is read |
| HUD | integrity bar (`StructureHpFraction`) in the status block, per cent for stations in the details; big wave countdown, then enemy count; hit points of the module under the crosshair (panel) and floating bars over it and over modules hit in the last 3.5 s |
| Toasts | module destroyed (with its kind), station lost, ship lost (`toast.shipLostCockpit`), all category Threat; the machine-lost line is skipped when a module falls with them |

### Goal and end
`GameOutcome.Evaluate(net, player, goal)` (engine-free, `src/Starfoundry.Ui`): Lost when the ship is lost in a structure world (older worlds never lose); Won when the waves cleared reach `GoalSettings.WavesToWin` (the setting `goal_waves`, 3 to 30, default 10) with the ship alive and a station of the player standing. Waves cleared are `WavesSpawned` while no enemy is alive and one less while a wave is being fought. Stats: play time, modules built (standing plus lost, minus the starter ship's five), stations founded and lost, waves, items mined by hand and by drones, modules and machines lost.

The end screen (`EndScreen.cs`) pauses the simulation. Victory writes the game's victory save ([saves](saves.md)) and offers Keep playing (endless, never shown again), Main menu and Quit. Defeat writes a play log line and keeps the autosave and quick save untouched: autosaving is skipped for a lost world, and the Main menu button there does not autosave. It offers Load last save (when one exists), New game and Main menu.

### Checks
- Tests: `GameOutcomeTests` (win, loss, wave in progress, station required, station lost, legacy worlds) and the `goal_waves` cases of `GameSettingsTests`.
- Headless images: `--defence-shot=path` (see the play-test guide) and `--end-shot=path`.

## Wave balance pass (second audit, 2026-11-21)
The audit found a cliff at wave 3: a brute (300 hp, about 22 damage per second) out-damaged the ship cannon (6 damage every 10 ticks, 18 per second). Changes:

| Change | Value |
| --- | --- |
| `threat.structure_ship_damage_percent` (new key, structure worlds only, 100 to 1000, default 100) | 150: the cannon does 9 per shot (27 per second); the `ship_cannon` research adds 50 percent on top (13 per shot) |
| `structures.defence.regen_hp` (new key, 1 to 1000, default 1) | 5: a damaged defence module regains 5 hit points per regeneration step (every 10 s) instead of 1 |

Measured by `WaveBalanceTests.Measure_the_wave_table` (seed 20261121, a compact cross: workshop with the ammo belts, a defence module with the turrets in the middle, cockpit and engine on its other two sides; a 120 s pause between waves, no repair bay; `held` means every enemy died and no module was lost; the number is the structure's integrity in per mille after the wave). Wave numbers are zero-based (wave 3 is the first brute).

| Turrets | Cannon | Result |
| --- | --- | --- |
| 0 | off | wave 0 destroys the ship |
| 0 | on (150 %) | holds waves 0 to 5 (integrity 925), loses the ship in wave 6 |
| 0 | on (100 %, old) | falls in wave 4 (loses a module) |
| 1 | off | holds waves 0 to 3 (integrity 926 after the first brute), loses a module in wave 4 |
| 1 | on (150 %) | holds waves 0 to 5, loses the ship in wave 6 |
| 2 | off | holds waves 0 to 5, loses the ship in wave 6 |
| 2 | on (150 %) | holds waves 0 to 6 (integrity 895), loses the ship in wave 7 |
| 2 | on (100 %, old) | holds waves 0 to 5, loses the ship in wave 6 |
| 3 | on | holds waves 0 to 8 (integrity 967 after wave 8) |
| 4 | on | holds waves 0 to 8 without losing hit points |

Intent, each pinned by a test: undefended bases fall by wave 2 (`Undefended_base_falls_by_wave_2`, cannon off; cannon alone lasts to wave 5); one turret is not enough once the brutes come (`One_turret_is_not_enough_from_wave_3`, cannon off: wave 3 leaves a mark and the base falls by wave 5); two turrets and the cannon hold waves 0 to 6 (`Two_turrets_and_the_ship_cannon_hold_waves_0_to_6`). Layout matters: a spread-out ship (cockpit, workshop, defence, engine in a row) loses its far module at wave 6 even with 2 turrets and the cannon, because the turrets (range 18 cells) cannot reach an enemy that attacks the end module from 5 cells further out.

## Waves checked again (second balance pass, 2026-11-22)
The wave table was not changed. The structure bot ([structure-bot.md](structure-bot.md)) reaches the win with 8 turrets (two ammo batteries of two on the ship and on the station), two walls per structure and a repair bay each, and lost no module and no machine before wave 10 on any of five seeds: waves 0 to 10 are close to trivial for a player who builds that defence by minute 14. See the "needs decision" list in that document.

## Harder Normal waves (2026-11-22)
Owner decision ([ADR 0026](../adr/0026-science-fields-and-research-expansion.md)): waves 3 to 10 are 25 to 40 percent larger and a new enemy, `core:bulwark` (600 hp, speed 8, 14 damage every 30 ticks), joins from wave 4 (one; two from wave 8). `EnemyDef` has no armour value, so the bulwark is hit points and slowness only. New table entries: two more scouts and one more striker from wave 3, a striker entry growing by one a wave from wave 7, the bulwark from waves 4 and 8. Waves 0 to 2 are unchanged; the world rule presets scale on top.

Measured by `WaveBalanceTests.Measure_the_wave_table` (same compact layout, seed 20261121, 120 s pause, no repair bay; waves zero-based; `holds 0 to N` means no module was lost up to wave N):

| Turrets | Cannon | No research | With turret calibration |
| --- | --- | --- | --- |
| 0 | off | wave 0 destroys the ship | the same |
| 0 | on | holds 0 to 3, loses a module in wave 4 | the same |
| 1 | off | holds 0 to 3 (integrity 865 after wave 3), loses a module in wave 4 | holds 0 to 3 (962), loses a module in wave 4 |
| 1 | on | holds 0 to 5, loses the ship in wave 6 | the same |
| 2 | off | holds 0 to 5 (839), loses the ship in wave 6 | holds 0 to 5, loses the ship in wave 6 |
| 2 | on | holds 0 to 5, loses the ship in wave 6 | the same |
| 3 | off | holds 0 to 5, loses the ship in wave 6 | holds 0 to 6, loses the ship in wave 7 |
| 3 | on | holds 0 to 6 (933), loses the ship in wave 7 | holds 0 to 8, loses the ship in wave 9 |
| 4 | off | holds 0 to 6, loses the ship in wave 7 | holds 0 to 8, loses the ship in wave 9 |
| 4 | on | holds 0 to 8, loses the ship in wave 9 | holds 0 to 8, loses the ship in wave 9 |
| 5 | on | holds 0 to 8, loses the ship in wave 9 | the same |
| 6 | off | holds 0 to 8, loses the ship in wave 9 | holds 0 to 10 (830) |
| 6 | on | holds 0 to 10 (820) | holds 0 to 10 (876) |

So waves 0 to 10 need six turrets with the ship cannon on the compact layout (five fall in wave 9, the wave with two bulwarks); with the cannon off, six turrets need turret calibration. (Before the change four turrets and the cannon held 0 to 8 without losing hit points.) Pinned by `Three_turrets_and_the_ship_cannon_hold_waves_0_to_6`, `Six_turrets_and_the_ship_cannon_hold_waves_0_to_10_and_five_do_not` and `The_bulwark_appears_from_wave_4_and_waves_3_to_10_are_about_30_percent_larger`. Seven or eight turrets in the one defence module fall in wave 9 in the test: an artefact of that layout, not a result.

The structure bot needed no change: it still builds eight turrets (two batteries on the ship and on the station), two walls and a repair bay each, and loses no module and no machine before the win. Normal win minute on the main seed: 45:01 (43:54 in the run just before the change); other seeds 43:50 and 47:59. See [difficulty.md](difficulty.md) for Hard and Brutal.
