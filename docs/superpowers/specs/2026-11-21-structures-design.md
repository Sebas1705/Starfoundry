# Modular structures: ships and stations as one thing

Status: proposed (2026-11-21). Project 1 of 5 of the "ship-first" redesign (1 structures; 2 power and fuel; 3 veins, materials and density; 4 interior and piloting; 5 defence, life and visuals of the station). Builds on the station modules of ADR 0017.

## Intent
The owner wants the game to start inside a modular ship, as in Main Sequence: the ship is a station that moves. It begins with a reactor, a small item container, an assembler and a smelter, plus a pilot module. A station is the same kind of thing without engines: an enclosed space where you build, with life and defences. Both are made of modules, each with one concrete purpose (fly, power, build space, defence, storage, pilot). The factory inside the ship must keep working while it flies.

## Goals
- A **structure** is a set of modules on a local grid with a world position. Ships and stations are structures; a ship has engines and moves, a station does not.
- Every machine and belt belongs to a structure and uses **local** integer coordinates, so moving a structure moves its whole factory at no cost and belts work unchanged.
- **Module kinds** with a purpose: `core`, `cockpit` (the pilot module), `engine`, `reactor`, `cargo`, `workshop` (build space), `defence`, `hull`. Flight needs a cockpit and at least one engine; the hold capacity comes from cargo modules; machines can only be placed inside workshop modules (and the core/hull of a station), defences inside defence modules.
- The campaign starts in a **starter ship**: cockpit, engine, reactor (inert until project 2), small cargo container, and a workshop holding a smelter and an assembler already working (fed from the container by an ore port, output to the container by a depot).
- The player founds **stations** with a command (spending items next to the ship) and extends both ships and stations with new modules.
- Basic visuals per module kind (procedural), so a ship and a station read as such.

## Non-goals
- Power consumption, ice and hydrogen (project 2); new materials and vein density (project 3); walking inside and the pilot seat (project 4); wave defence of structures and detailed visuals (project 5). Reactors and defence modules exist as kinds with minimal behaviour now.
- Docking, several ships per player, ship-to-ship transfer, free-form hulls.
- Re-balancing the campaign; the balance bot keeps using the legacy free-build structure.

## Anti-goals
- Breaking the 448 tests, the balance bot, old logs or old saves (structure 0 and the existing `StationRules` keep working as today).
- A ship whose flight or factory stutters or desyncs when a structure moves.
- Making the first minutes slower to understand than today: the player must be able to mine and see the factory working at once.

## Constraints
- Deterministic, integer only; structures change only through commands or the fixed-order tick.
- Local coordinates are integer cells; a structure's world origin is integer units (240 per cell).
- Old command tags and the old header stay readable; new behaviour uses new command tags and a header flag (`structures`) in the existing flags byte.
- Checksum: hash nothing new when no structures exist, so old worlds hash exactly as before (the idiom of `MixStation`).
- Content changes alter the content hash (old saves refuse to load, by design).

## Approaches
- A. **Local grids per structure (chosen).** Machines and segments carry a `Structure` id and local cells; each structure has a world origin. Cost: touch occupancy, range checks, threat targeting and rendering; benefit: moving is one integer update, belts stay coordinate-free (they are linked by ids already).
- B. Keep world coordinates and translate every entity of a moving structure each tick. Rejected: O(entities) per tick, occupancy index churn, determinism risk.
- C. Keep the ship as the player and only make stations structures. Rejected: the ship could not host machines, against the owner's request.

## Design

### Simulation
- `Structure` (new, per `BeltNetwork`, sorted by id): `Id`, `Kind` (Ship or Station), `Owner` (player), world origin `(X,Y,Z)` in units, a set of modules `(Mx,My,Mz) -> ModuleKind` in LOCAL module coordinates (module size 8 x 4 x 8 cells, `StationGrid`), a core cell. Structure 0 is the legacy "world" (no modules needed, free-build rules as today) so tests and the bot are unchanged.
- `Machine` and `BeltSegment` get a `Structure` field (default 0), included in clone, checksum (hashed only when non-zero) and invariants. Local cells are bounded (limit constant, for example +-512 cells per axis) so the occupancy key `(structure, x, y, z)` packs into a `long` or uses a per-structure dictionary (the builder's call; the current key already uses 45 of 63 bits, so structure 0 keeps its key and others use a per-structure index). `EntityAt`, `TryClaim`, `Release`, batch rollback and `ValidateInvariants` take the structure.
- New commands (new tags 30+, old ones untouched): `PlaceMachineIn(structure, x,y,z, type, recipe)`, `PlaceSegmentIn(structure, ...)`, `PlaceModuleIn(structure, mx,my,mz, kind)`, `RemoveModuleIn`, `FoundStation(kind core)` (cost items, near the ship, not overlapping another structure), and ship control `SetThrust`/`SetMovement` now move the player's ship structure.
- World position helpers: `WorldCentre(structure, x,y,z)` = origin + local*240 + 120. Replace world-cell uses with it in: `CellInRange` (build range is measured from the ship to a STATION module or machine; inside your own ship range is always satisfied), `SystemOfMachine`, `NearestWall`, `PickTargetMachine`, turret and repairer ranges (local differences within a structure; world for cross-structure), `LinkDeposit` and a per-tick re-check of linked drills when the structure moves, `ModuleCentreInRange`, enemy target points.
- Movement: the player's ship structure origin is the single source of truth; `Player.X/Y/Z` keeps mirroring it (pilot position = origin + cockpit offset), so mining range, scan, jump and the HUD keep working. Flight requires a cockpit and an engine module (else `SetThrust` is rejected with a new reject). Speed scales with engine count (content: base speed per engine). Structure moves are applied at a fixed point in `Step` before `StepThreat` (current order: segments, machines, unloaders, players, research, threat).
- Module kinds and rules from content (`structures` section in `core.json`): per kind cost, hp, and what it allows: `workshop` and `core` allow machines and belts; `defence` allows turrets and walls; `cargo` adds hold capacity (`small container` = 60, the current hold); `engine` adds thrust; `reactor` is a marker for project 2; `cockpit` marks the pilot position; `hull` is structure only. Placement of a machine checks that its cell lies in a module whose kind allows its role.
- Hold: capacity becomes the sum of cargo modules (legacy structure-0 worlds keep `HoldCapacity = 60`). Mining by hand, depots and ore ports keep working on the player's hold.
- Starter ship: content `structure_templates.starter_ship` lists modules and pre-built machines/belts (ore port, belt, smelter, belt, depot, plus an assembler fed from the smelter); the campaign world builds it at creation, deterministically, at no cost (like deposits are created), and it is part of the world before the first command.
- The station rules of ADR 0017 stay for legacy worlds; the campaign switches to `structures` (header flag bit 2). A later cleanup can remove the old path.
- Tests: structure placement, local occupancy independent between structures, a machine moves with its ship (world centre changes, belt flow unchanged over thousands of ticks of flight, outputs identical to a parked ship), flight needs cockpit and engine, engine count scales speed, hold capacity from cargo modules, machine in a hull module rejected, founding a station (cost, range, overlap), module adjacency and removal rules per structure, enemy targeting uses world positions of a moving ship's machines, replay and save round trip with structures, checksum unchanged for worlds without structures, old logs and saves still load, the starter ship produces ingots and plates by itself.

### Game
- One `Node3D` per structure with the structure's world transform; machine and belt models, item MultiMeshes and the module view are children, so a flying ship carries its factory. Item positions are updated in local space.
- Aiming and placing work in the local frame of the structure under the crosshair (ray transformed into local space); the ship's own interior is the default target when you are inside it.
- Module view per kind: cockpit canopy, engine nozzles with glow, reactor core, cargo crates, workshop floor, defence turret mounts, hull plating; a ship reads as a ship (engines at the back) and a station as a station. First-pass procedural models; polish is project 5.
- HUD: structure name and kind, modules list with kinds, flight state (engines, cockpit), hold from cargo modules.
- Start: the campaign opens inside the starter ship with the factory running; guided steps change (mine ore, watch it become ingots and plates, found a station, add a module).

## What is left to the builder
- Exact module size for ship modules (8 x 4 x 8 makes a five-module ship about 40 cells long; if that feels huge, make the module size smaller as one constant for every structure), costs, engine speed, local grid limit, key and button names, wording.
- Packing of the occupancy key, order of tick phases within the stated constraints.
- Which legacy tests keep structure 0 and which move to the new commands.

## Risks
- Large cross-cutting change (about 325 test call sites use the world coordinates; most keep compiling because structure 0 is the default). Adapt incrementally and keep the legacy world intact until the end.
- Rendering of moving structures and per-frame item updates; picking in local space.
- Wave balance and pacing in the new start are unmeasured; the bot does not play structures yet.
- A drill linked to a deposit stops being valid when the structure moves away: re-check each tick (or only while the structure moves).
