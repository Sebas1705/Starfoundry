# Prototype: weapons as hardpoint modules and the shipyard

Status: done, awaiting a human play-test (2026-11-22). Rules in [ADR 0032](../adr/0032-weapons-as-hardpoint-modules-and-the-shipyard.md); builds on the [mining laser](mining-laser.md), [docking](docking.md) and [ship attitude](free-flight.md).

## Goal
Owner feedback: ships and stations are completely modular and customisable, so a ship mines and shoots with the weapons it carries, and each weapon is added or upgraded in a ship designer at a shipyard of the station.

## What changed (proposal)
| Area | Change |
| --- | --- |
| Modules | `mining_laser` (strength 100, reach 10 cells, 5 power), `mining_laser_mk2` (170, 14, 8), `gun` (100), `gun_mk2` (170) for ships; `shipyard` for stations. One catalogue for ships and stations: `ModuleKindIds.AllowedOn` (no cockpit or engine or hardpoint on a station, no core or shipyard on a ship). |
| Starter ship | A Mk1 laser on the nose (module (0,0,-1)) and a Mk1 gun on top of the cockpit (module (0,1,0)). A station founded next to the ship starts 16 cells ahead (the nose module is in the way at 8). |
| Mining | Each laser module that reaches the deposit adds its strength: two Mk1 lasers halve the ticks per item (15 to 7 at density 100), a Mk2 counts 170 percent. One Mk1 laser mines exactly as the implicit laser did. Each laser draws its own power. No laser module, no mining (`NoLaser`). The beam is drawn from the tip of each emitter in the ship's attitude. |
| Guns | `SetWeapons` needs a gun module (`NoGun`); every gun in reach adds its strength to a volley, the tracer leaves the nearest gun. |
| Controls | `V` picks the weapon group (mining laser or guns, key panel rows in es and en); the fire button uses it: hold the click on a deposit for the laser, hold it for the guns (they arm while held). `M` still takes the laser in hand. |
| Shipyard | A station module (24 plates, 16 ingots, 8 carbon), built with the module tool (`N`). Docked at one, `F2` opens the ship designer. |
| Designer | A grid layer of the ship (bow at the top, `Capa +/-` change height), the module list, a spinning 3D preview built from the real models, add on a free cell next to the ship, select to remove, move or upgrade, the verdict and the price from the simulation, apply (one `DesignerApply`) or revert. |
| Command | `DesignerApply(modules)`, tag 46, log version 14. Atomic; only docked at a shipyard of the owner; keeps a cockpit, engine, reactor and cargo, one connected piece, the docked port, every machine-holding module and the hold; no overlap with the station. Moved modules are free; replaced or removed modules refund half; the hold pays first, then the station stock. |
| Goal | The campaign has an optional secondary goal `build_shipyard`; the scripted player does not build one (it never needs a second laser), so its wins are unchanged apart from the ship's two extra modules. |
| Models | New models in `game/ModuleModels.Hardpoints.cs`: a turntable with an emitter and a lit lens (twin emitters on the Mk2), a gun mount with one or two turrets, and a gantry with a lit cradle for the shipyard. |

## Compatibility
Log version 14; `WorldRules.LegacyHardpoints` (flags bit 16) is on for older logs, so they keep the implicit laser (reach 6 cells from the reference point) and gun; golden checksums unchanged (the stripped content has none of the five kinds and no hardpoints on the starter ship). Details in the ADR.

## Measured: the scripted player
`StructureBot` (`dotnet run --project tools/Starfoundry.Balance -- --structures --seed N --difficulty D`, 6 s per action), minutes to the win. The bot now founds its station 16 cells ahead of the ship. Before: main at a51f2f5.

| Run | Before (main) | After |
| --- | --- | --- |
| Normal, seed 20261121 | 45:21 | 46:54 |
| Normal, seed 777 | 44:16 | 44:18 |
| Relaxed (8 waves), seed 20261121 | 41:11 | 46:21 |
| Relaxed, seed 777 | 44:25 | 41:36 |
| Hard (12 waves), seed 20261121 | 44:42 | 48:33 |
| Hard, seed 777 | 47:49 | 46:21 |

All six runs win with no module and no machine lost. The wave clock decides the win, but the two extra modules and the station founded 8 cells further away move the base's geometry, so single runs shift by up to a few minutes either way (the Relaxed and Hard "before" figures are from [difficulty](difficulty.md), measured earlier than the Normal ones). The first version of this change, with the station still founded 8 cells ahead, overlapped the nose module, delayed the station and lost the ship at wave 10; the bot's founding offset is the only bot change.

## Checked by tests
`HardpointTests`, `ShipDesignerTests` (see the ADR), the existing suite with its hand-built ships given a laser module (or the legacy rules where they measure the implicit gun) and the golden checksums unchanged.

## Headless captures
- `--designer-shot=path`: the designer with a second laser, a Mk2 gun and the nose laser upgraded (valid design, price in plates, circuits and titanium alloy, weapons line, new modules highlighted), the applied design, and the refitted ship from outside.
- `--laser-shot=path`: a Mk1 and a Mk2 laser mining one rock, two beams from the two emitters (`path`), and the cockpit view.

## Not done (TBD)
- Not tested by hand: real mouse play of the designer and of the weapon groups, the designer at 720p and on small windows, the sounds, a refit during a wave.
- A firing arc, station-mounted weapons, gun ammunition, carrying module damage through a move, research nodes for the mark 2, defence module tiers, undocked refits.
- The balance numbers (strengths, reach, costs, power) are proposals.
