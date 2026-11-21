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
