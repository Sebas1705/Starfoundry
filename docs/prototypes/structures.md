# Prototype: modular structures

Status: in progress (2026-11-21). Decision: [ADR 0018](../adr/0018-modular-structures.md). Design: [structures design spec](../superpowers/specs/2026-11-21-structures-design.md). Plan: [implementation plan](../superpowers/plans/2026-11-21-structures.md).

## The question
What changes if the game starts inside a modular ship whose factory keeps working while it flies, and a station is the same kind of thing without engines?

## What is being built
| Area | Change |
| --- | --- |
| Simulation | A structure is a set of typed modules on a local grid with a world origin. Ships and stations are structures. Machines and belts carry a structure id and local cells; structure 0 is the legacy world |
| Commands | New tags for placing machines, segments and modules in a structure, removing modules and founding a station; a `structures` bit in the log header flag. Old logs and saves read as off |
| Module kinds | Core, cockpit, engine, reactor, cargo, workshop, defence and hull, each with one purpose; costs and allowed roles come from content |
| Starter ship | The campaign opens in a ship with cockpit, engine, reactor, cargo container and a workshop with an ore port, smelter, assembler and depot already working |
| Game | One node per structure, aiming and placing in local space, a procedural model per module kind, HUD with structure name, modules and flight state |

## The rules
- A structure is made of modules; every machine and belt sits on a cell inside a module of its structure.
- Machines and belts go only in modules that allow them (core and workshop); turrets and walls go in defence modules; hull is structure only.
- A ship flies only with a cockpit and at least one engine; more engines mean more speed.
- The hold holds what its cargo modules hold; the starter container holds 60.
- Moving a ship moves its whole factory, which keeps running.
- A station is founded next to the ship for an item cost and cannot overlap another structure. New modules must touch an existing module of the same structure.
- The sandbox, the tests and the scripted balance player keep the old free-build rules.

## How to run it
- The campaign starts in the starter ship.
- `--structures=false` starts a campaign with the old rules, to compare.

## Measured
Measured by `StructurePacingTests` (scripted player, sim commands only, 30 ticks per second, time counted from the first tick of hand mining at the home iron deposit):

| Event | Ticks | Time |
| --- | --- | --- |
| First ore in the hold | 14 | 0.5 s |
| First ingot in the hold | 255 | 8 s |
| First plate in the hold | 525 | 18 s |
| Station affordable (16 ingots + 12 plates) | 3165 | 106 s |
| Station founded next to the ship | 3166 | 106 s |
| First wave | none in 12 minutes | the starter factory signature (8) equals the threshold, so the alert never rises |

Before the fix (hand mining filled the whole hold, core cost 36 ingots): the hold sat at ore 49 of 60, the depot could not deliver, and the station was never affordable. The depot also caps each item at 24 in the hold, so a 36-ingot cost could never be reached at all.

Rules that came out of it:
- Hand mining stops while the hold is at or above 75 percent (`PlayerRules.MiningHoldLimitPercent`), only with structure rules, so 25 percent stays free for factory output.
- The core costs 16 ingots and 12 plates, so both the smelter and the assembler output are spent. Other module costs are unchanged; no single-item cost may exceed the depot cap of 24.
- Over ten minutes without building, ingots rise to the depot cap (24) with no stall longer than 254 ticks (8.5 s), and plates follow.

Not measured: real hand play after this change, flight time to a deposit other than the home one, and later stages (more modules, a station factory).

## Limits
- Walking: `E` stands you up from the pilot seat (and sits you down within about 2.6 cells of it); `WASD` walk (`Shift` runs, 4 and 7 cells per second, eye 1.7 cells above the module floor) in the local space of your ship, clamped to the union of its module boxes (adjacent modules of the same level connect; no jump, no gravity, machines are not obstacles, one level only). The body is client-only state, the sim is unchanged; flight and the autopilot are refused while standing. Walking inside stations works only while docked: the station's modules join the walkable volumes through the airlock ([docking](docking.md)). `--walk-shot=path` saves the workshop, cargo and seat views. Repair moved to `Backspace`.
- Power, materials, defence of structures and detailed visuals are later projects. Reactors and defence modules exist as kinds with minimal behaviour.
- The balance bot does not play structures, so earlier balance numbers do not describe this mode.
- Docking exists since [ADR 0029](../adr/0029-docking-ship-and-station-are-different.md) ([prototype](docking.md)); still one ship per player and no ship-to-ship transfer.
- Thrust in the command log: a save is the command log replayed, and every change of the ship's thrust is a logged `SetThrust`. Steering with the mouse changes the thrust vector every frame, so the game now sends it through `ThrustThrottle` (`src/Starfoundry.Ui`): components quantised to steps of 64/1024 (typically under 2 degrees of heading error, 5.5 at worst, for directions almost along an axis where rounding up would make the vector too long and it is truncated instead), at most ten changes a second, and a stop is sent at once. The final wanted value is always delivered because the game offers it every frame. The simulation is unchanged. Measured with `Godot --path game --headless -- --load-bench` (a scripted pilot for 30 minutes of game time at 60 frames a second, the view turning all the time, W held, now and then a strafe, a straight stretch and a stop; one routine of 60 s repeated; the starter ship, no threats, JIT warmed up by a first pass, three runs agree):

  | Sending rule | Thrust commands in the log | Save size | Load (replay 54 000 ticks) |
  | --- | --- | --- | --- |
  | Parked (no thrust, the floor) | 0 | 52 bytes | 54 to 64 ms |
  | Old (every change, at most 30 a second) | 25 549 | 638 777 bytes | 98 to 105 ms |
  | New (64/1024 steps, at most 10 a second) | 8 609 | 215 277 bytes | 80 to 82 ms |

  So the log and the save are about a third of their size. The load time of this nearly idle world is dominated by replaying the ticks (and by the sim moving the ship), so the saving there is small, about 20 ms; the commands matter more for long games with big bases, which were not measured here. Not measured: real mouse steering by hand, and the feel of the quantised heading in flight.
- The build menu and research panel are documented in the [play-test guide](../playtest.md); the `ResearchView` and `BuildCatalog` models are covered by tests, the drawing by screenshots only (`--ui-shot`).
