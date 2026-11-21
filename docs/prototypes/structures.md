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
- Power, materials, walking inside, defence of structures and detailed visuals are later projects. Reactors and defence modules exist as kinds with minimal behaviour.
- The balance bot does not play structures, so earlier balance numbers do not describe this mode.
- No docking, one ship per player, no ship-to-ship transfer.
