# Prototype: drone automation

Status: built (2026-11-21), simulation, content and Ui logic only. Decision: [ADR 0020](../adr/0020-drone-automation.md). Roadmap: part 3 of the [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md).

## What was built
| Area | Change |
| --- | --- |
| Content | `structures.drone_bay` (12 iron plate, 8 titanium plate, 8 nickel plate; hp 150; `drone_range_cells` 60; `drone_interval_ticks` 90; `power_use` 20); research `core:drone_automation` (ability `drone_bay`, requires `core:logistics` and `core:advanced_plating`, costs 150 iron plate, 20 titanium plate, 20 quartz) |
| Simulation | `ModuleKind.DroneBay`; command `AssignDrones` (tag 35); `Reject.NoDroneBay`; a drone step after hand mining; per-structure assignment and per-bay progress; `MinedByDrones` |
| Accessors | `DroneBays(structure)`, `AssignedDeposit(structure)`, `DroneStatus(structure)` |
| Ui logic | Names for the module, research and status lines; tip `learn.drones`; objective `Automate` between `Defend` and `Expand`; reject texts for `NoDroneBay` and `Locked`; es and en |

## Rules
- Each bay adds the structure's power satisfaction (per mille) to its progress every tick and mines one item per `interval * 100 / density` ticks of progress.
- The hold limit is the hand mining one: no delivery while the hold total is at or above 75 percent of its capacity.
- Range 60 cells from the structure's origin cell; a structure that moves away or an empty deposit drops the assignment.
- A bay draws 20 power while a deposit is assigned (the reactor makes 100).

## Measured numbers
Rates are asserted by `DroneTests` (counts after a fixed number of ticks). Pay-back figures are computed from them and the recipes (one ore to one ingot, two ingots to one plate).

| Density | Items per minute per bay | Hand mining per minute |
| --- | --- | --- |
| 25 | 5 | 30 |
| 50 | 10 | 60 |
| 100 | 20 | 120 |
| 200 | 40 | 240 |

- One bay is one sixth of hand mining at any density, so about 6 bays match a player who mines all the time.
- Cost in ore: 12 iron plates are 24 iron ore, 8 titanium plates 16 titanium ore, 8 nickel plates 16 nickel ore: 56 ore in all. Hand mining that takes 28 s at density 100. One bay on a density 100 deposit returns the same amount in 2.8 minutes, two bays in 1.4 minutes; one bay on a density 200 deposit in 1.4 minutes.
- Fuel: one bay is 20 of the reactor's 100 power, about one hydrogen per 150 s of mining (0.4 per minute) on top of the rest of the structure.
- The throttle is the hold: with the 60-item hold of the test ship, drones and hand mining stop at 45 items. Cargo modules and ports that move ore out let automation keep running.

## Not done
- (Done in the game, see Game below.)
- A bot that builds bays; balance by hand play.

## Game
- **Module.** `ModuleModels` draws the drone bay (ship and station variants, `--focus=modules` in the gallery): an open hangar with low side walls, a glowing launch door in the back wall, two launch rails, three cradles with a small drone craft each (one cradle empty in variant 1), a gantry and an amber status light. `N` cycles to it after the hull; while `core:drone_automation` is not researched the ghost and the kind toast show `toast.reject.locked`.
- **Assigning.** `F7` sends `AssignDrones` for the structure aimed at or entered (else the ship, else the nearest bay) with the selected deposit (`[ ]` or click). It is checked on the client with `DepositInDroneRange`; pressing it again on the same deposit, or with none selected, unassigns. Results show as toasts, sim rejects through `RejectTexts`.
- **HUD and markers.** The status panel shows `Drones (Ship): 1 bay(s), mining Ice at 12/min` (or no power, hold full, idle) from `DroneFormat`. The selected deposit marker adds "Drones here" when assigned, other assigned deposits get their own marker, and the guided step Automate marks the nearest deposit in range.
- **Flying drones.** While a structure's bays work, `min(bays * 2, 6)` drone craft fly from its bays to the deposit, hover around it and return in a loop, along a faint beam. They are reused nodes moved per frame (no allocations), hidden when idle, and have no effect on the simulation.
- **Screenshots.** `--drone-shot=path` (structures campaign): adds a bay through `AddStructureModuleUnchecked`, assigns the nearest deposit in range and saves `path`, `path.cockpit.png` and `path.module.png`.
