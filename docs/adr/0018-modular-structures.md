# 0018. Modular structures: ships and stations as one thing

- Status: proposed
- Date: 2026-11-21
- Area: simulation, gameplay

## Context
- In the reference game (*Main Sequence*) the player starts in a modular ship that is a mobile station: a space where machines run, which also flies. Starfoundry today has a player ship that flies, fixed station modules ([ADR 0017](0017-stations-as-build-volumes.md)) and machines placed at world coordinates, so the factory cannot move with the ship.
- The owner wants the game to start inside a working modular ship and wants stations to be the same kind of thing without engines.
- Every change to the world goes through a logged command and the simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md), [ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)). 448 tests, the scripted balance player, old logs and old saves depend on the existing free-build and station rules staying as they are.
- The full design is in the [structures design spec](../superpowers/specs/2026-11-21-structures-design.md). This is project 1 of 5 of the ship-first redesign (structures; power and fuel; veins, materials and density; interior and piloting; defence, life and visuals of the station).

## Decision
- **Structures.** A structure is a set of typed modules on a local grid (module size 8 x 4 x 8 cells, one constant) with an integer world origin. Ships and stations are both structures. A ship has a cockpit and engines and moves; a station does not.
- **Local coordinates.** Machines and belt segments carry a structure id and local cell coordinates. Moving a structure changes only its origin, so the factory inside keeps working in flight and belts stay unchanged. World positions come from one helper, `WorldCentre(structure, x, y, z)`, used by ranges, targeting and threats.
- **Structure 0 is the legacy world.** It needs no modules and keeps today's free-build rules, so existing tests, the balance bot, old logs and old saves behave as before. The checksum hashes nothing new when no structure exists.
- **Commands and header flag.** New command tags (30 and up) for placing machines, segments and modules in a structure, removing modules, and founding a station; old tags are untouched and target structure 0. A `structures` bit in the existing header flags byte turns the rules on; old headers read as off.
- **Module kinds.** `core` and `workshop` allow machines and belts; `defence` allows turrets and walls; `cargo` adds hold capacity (the small container holds 100 since the second balance pass, was 60); `engine` adds thrust, and speed scales with engine count; `cockpit` marks the pilot position; flight needs a cockpit and at least one engine; `reactor` is a marker until the power project; `hull` is structure only. A machine is placed only on a cell inside a module whose kind allows its role. Costs, hit points and allowed roles come from content.
- **Starter ship.** A content template, built with the world at no cost before the first command: cockpit, engine, reactor, cargo container, and a workshop holding an ore port, belt, smelter, belt and depot, plus an assembler fed from the smelter. Hand-mined ore becomes ingots and plates by itself.
- **FoundStation.** A command that spends items next to the ship to create a station structure with a core module; it is refused if too far or overlapping another structure. Both ships and stations are extended with new modules.
- **Campaign.** The campaign uses the structure rules by default; `--structures=false` starts it with the old rules.

## Consequences
- Benefits: moving a structure is one integer update; belts stay coordinate-free because they are linked by ids; the factory runs in flight; one flag and new tags, all logged, so replays and saves cover structures; old worlds hash and load exactly as before.
- Costs: a cross-cutting change (range checks, target picking, occupancy, drill links, rendering); occupancy is per structure, so the cell index is no longer a single world index; the game draws one node per structure and aims in the local frame of the structure under the crosshair; a drill linked to a deposit must be re-checked when its structure moves.
- Pacing: measured with a scripted player (see `docs/prototypes/structures.md`). Hand mining stops at 75 percent of the hold (`PlayerRules.MiningHoldLimitPercent`, structure worlds only) so the factory can always deliver, and the first station costs 16 ingots and 12 plates, which the depot (capped at 24 of each item in the hold) can deliver: the first plate comes about 18 s after mining starts and the first station is affordable about 106 s after it. The starter factory's signature equals the threat threshold, so no wave comes from the starter ship alone. The balance bot still does not play structures, so earlier balance numbers do not describe this mode.
- How it is checked: tests for independent local occupancy between structures, a ship's factory giving identical output while flying and parked, flight needing a cockpit and engine, engine count scaling speed, hold capacity from cargo modules, machines refused in the wrong module kind, founding a station, module adjacency and removal, enemy targeting by world position, replay and save round trips, and an unchanged checksum for worlds without structures. The existing tests and balance tests must give identical results.
- Content changes alter the content hash, so old saves refuse to load by design.
- Not decided here: power and fuel, new materials and vein density, interiors and walking, wave defence of structures, detailed visuals, docking, several ships per player.

## Alternatives considered
- **A. Local grids per structure (chosen).** Each machine and segment carries a structure id and local cells. It touches occupancy, range checks, targeting and rendering, but moving is one update.
- **B. Keep world coordinates and translate every entity each tick.** Rejected: the cost is proportional to the number of entities per tick, the occupancy index churns, and it adds determinism risk.
- **C. The ship stays the player and only stations are structures.** Rejected: the ship could not host machines, against the owner's request.
