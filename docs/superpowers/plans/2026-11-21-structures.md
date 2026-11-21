# Modular structures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ships and stations are structures of typed modules on a local grid with a world position; the campaign starts in a working starter ship whose factory keeps running while it flies.

**Architecture:** Machines and belt segments get a `Structure` id and local cells; each structure has an integer world origin. Structure 0 is the legacy world (free build, unchanged tests and bot). New commands use new tags and a header flag; module kinds and the starter ship come from content. The Godot game renders one node per structure and aims in local space.

**Tech Stack:** C# 12 / .NET 8 (`src/Starfoundry.Sim`, `src/Starfoundry.Ui`, xUnit), Godot 4.7.2 Mono (`game/`), content in `content/core.json`.

**Spec:** [docs/superpowers/specs/2026-11-21-structures-design.md](../specs/2026-11-21-structures-design.md)

## Global Constraints
- Everything committed is in English; UI strings in `Loc.cs` as Spanish and English (the Loc tests scan keys).
- Commits use the user's own git identity, NO `Co-Authored-By` or "Generated with" trailers, never `--no-verify`.
- Simulation: deterministic, integer only, structures change only through commands or the fixed tick order (segments, machines, unloaders, players, research, threat; structure moves apply before threat).
- Structure 0 = legacy world: every existing test (448) and the balance tests keep passing unchanged; old logs and saves still load.
- New command tags 30+ (old tags untouched); the header flags byte gets bit 2 = `structures` (bit 0 threats, bit 1 station rules already used); bump the log version only if the reader needs it and keep reading older versions.
- Checksum hashes nothing new when no structure exists (the `MixStation` idiom).
- Module size stays 8 x 4 x 8 cells in one constant (`StationGrid`); a structure's local grid is bounded (one named constant).
- Content changes alter the content hash; that is expected.
- The owner does not want a long live-play round: verify with build, tests and headless screenshots, plus one short hand check at the end; say what was not played.
- Branch `feat/structures` from `main`; open the PR and merge when CI is registered and green (standing permission for this repository).

## Review Focus
- A machine placed in structure A at the same local cell as one in structure B: both allowed, independent occupancy, neither overwrites the other.
- A ship flying thousands of ticks with its smelter and assembler running: outputs identical to a parked ship, no item lost, no divergence on replay.
- Founding a station on top of another structure or too far from the ship: rejected with a reason, hold unchanged.
- Cockpit or last engine removed while thrust is set: thrust stops, no movement, no exception.
- Loading an old save or log (no structures) with the new code, and a structures save with the checksum check: old loads as before; a mismatch is refused.

---

### Task 1: Structures and local occupancy
**Files:** Create `src/Starfoundry.Sim/Structure.cs` (class, `StructureKind`, `ModuleKind`), `src/Starfoundry.Sim/BeltNetwork.Structures.cs` (partial: registry, queries, clone, checksum, invariants); Modify `Machine.cs`, `BeltSegment.cs` (`Structure` field), `SimWorld.cs` (occupancy `_cells` per structure, `EntityAt(structure, x,y,z)`, `TryClaim`, `Release`, rollback copy, `ValidateInvariants`), `BeltNetwork.Machines.cs`/`BeltNetwork.cs` (`AddMachine`/`AddSegment` take an optional structure, default 0). Test: `tests/Starfoundry.Sim.Tests/StructureTests.cs`.
**Interfaces:** Produces `enum StructureKind { Ship, Station }`, `enum ModuleKind { Core, Cockpit, Engine, Reactor, Cargo, Workshop, Defence, Hull }`, `BeltNetwork.CreateStructure(int owner, StructureKind kind, long ox, long oy, long oz) -> int`, `BeltNetwork.Structures()`, `GetStructure(id)`, `WorldCentre(int structure, int x, int y, int z) -> (long,long,long)` in units, `SimWorld.EntityAt(int structure, int x, int y, int z)` (the 3-argument form stays and means structure 0), `Machine.Structure`, `BeltSegment.Structure`.
- [ ] Step 1: Write failing tests: `Same_local_cell_in_two_structures_is_independent`, `Structure_zero_behaves_as_before`, `WorldCentre_is_origin_plus_local`, `Moving_origin_changes_world_centre_not_local_cell`, `Checksum_unchanged_without_structures`, `Checksum_changes_with_a_structure`, `Clone_and_rollback_keep_structure_occupancy`, `Invariants_detect_a_machine_in_a_missing_structure`, `Local_coordinates_beyond_the_limit_rejected`.
- [ ] Step 2: Run `dotnet test --filter StructureTests`; expected FAIL.
- [ ] Step 3: Implement. Occupancy: keep the current packed key for structure 0, add a per-structure dictionary for others (or an equivalent that stays deterministic).
- [ ] Step 4: Run full `dotnet test`; expected all pass.
- [ ] Step 5: Commit `Add structures with local occupancy`.

### Task 2: Module kinds, structure commands and the header flag
**Files:** Modify `Commands.cs` (records below, new rejects appended), `CommandCodec.cs` (tags 30-34, well-formed list, header flag bit 2, `ReadLog/WriteLog` flag), `SimWorld.cs` (dispatch and `PlaceMachineIn`, `PlaceSegmentIn`, `PlaceModuleIn`, `RemoveModuleIn`), `BeltNetwork.Structures.cs`, `ContentDb.cs` + `content/core.json` (`structures` section: per kind cost, hp, allowed roles, hold capacity for cargo, speed for engine), `SaveGame.cs`/`SimWorld.CreateGalaxy` option `structures`. Test: `StructureCommandTests.cs`.
**Interfaces:** Produces `PlaceMachineIn(int Structure,int X,int Y,int Z,int Type,int Recipe)`, `PlaceSegmentIn(int Structure,int X,int Y,int Z,Dir Dir,int Length,BeltTier Tier)`, `PlaceModuleIn(int Structure,int Mx,int My,int Mz,ModuleKind Kind)`, `RemoveModuleIn(int Structure,int Mx,int My,int Mz)`; rejects `KindNotAllowed`, `NoSuchStructure`; `BeltNetwork.StructureRules` bool (the `structures` flag); `ContentDb.StructureRules` section with `AllowedRoles(ModuleKind)`.
- [ ] Step 1: Failing tests: `Machine_allowed_only_in_matching_module_kind` (smelter in workshop ok, in hull rejected, turret only in defence), `Module_adjacent_to_existing_only`, `Module_cost_taken_from_hold`, `Remove_refused_when_occupied_or_disconnected`, `Belt_must_lie_inside_modules_of_its_structure`, `Old_commands_still_target_structure_zero`, `Header_flag_roundtrip_and_old_headers_read_as_off`, `Replay_and_save_roundtrip_with_structure_commands`.
- [ ] Step 2: Run the filter; expected FAIL.
- [ ] Step 3: Implement commands, codec tags, header flag, content section with strict loader and hash mixing, gating by kind.
- [ ] Step 4: Run full `dotnet test`; expected all pass.
- [ ] Step 5: Commit `Add module kinds and the structure commands`.

### Task 3: Moving ships, world positions and flight rules
**Files:** Modify `BeltNetwork.Economy.cs` (`MoveShip`, hold capacity from cargo modules, ship structure follows the player), `BeltNetwork.Threat.cs` (`SystemOfMachine`, `NearestWall`, `PickTargetMachine`, enemy target points, turret and repairer ranges via `WorldCentre`), `SimWorld.cs` (`CellInRange`/`InBuildRange` for structures, `LinkDeposit` and a per-tick drill re-check when a structure moves), `BeltNetwork.Structures.cs` (flight requirements, engine speed). Test: `StructureFlightTests.cs`.
**Interfaces:** Consumes tasks 1 and 2. Produces `BeltNetwork.ShipStructure(int player)`, `CanFly(int player)`, `HoldCapacity(int player)` (legacy worlds return `PlayerRules.HoldCapacity`), reject `NoCockpitOrEngine` for `SetThrust`/`SetMovement` when rules are on; the player's ship structure origin is the single source of truth and `Player.X/Y/Z` mirrors it.
- [ ] Step 1: Failing tests: `Factory_runs_while_the_ship_flies` (smelter and assembler outputs equal to a parked ship after 5,000 ticks), `Machines_move_with_the_ship_world_centre`, `Flight_needs_cockpit_and_engine`, `Engine_count_scales_speed`, `Hold_capacity_comes_from_cargo_modules`, `Removing_last_engine_stops_the_ship`, `Enemy_targets_use_world_positions_of_a_moving_ship`, `Drill_link_dropped_when_the_structure_leaves_range`, `Replay_with_flight_matches_checksum`.
- [ ] Step 2: Run the filter; expected FAIL.
- [ ] Step 3: Implement with `WorldCentre`; apply structure moves before the threat phase.
- [ ] Step 4: Run full `dotnet test`; all pass, BalanceTests unchanged.
- [ ] Step 5: Commit `Move structures and take positions from their world origin`.

### Task 4: Starter ship and founding stations
**Files:** Modify `content/core.json` (`structure_templates.starter_ship`), `ContentDb.cs`, `SimWorld.cs` (`CreateGalaxy` builds the template when `structures` is on; `FoundStation` command tag 35 and handler), `CommandCodec.cs`. Test: `StarterShipTests.cs`.
**Interfaces:** Produces `FoundStation(int Dx, int Dy, int Dz)` (a world cell near the ship) creating a Station structure with a Core module, cost from content; the starter ship: cockpit, engine, reactor, cargo (60), workshop holding an ore port, belt, smelter, belt, depot (to the container), and an assembler fed from the smelter; all pre-built at no cost before the first command.
- [ ] Step 1: Failing tests: `Starter_ship_has_the_listed_modules_and_machines`, `Starter_ship_turns_hand_mined_ore_into_ingots_and_plates_by_itself`, `Starter_ship_can_fly`, `Found_station_costs_items_and_creates_a_core`, `Found_station_rejected_too_far_or_overlapping`, `Template_is_part_of_the_world_checksum_deterministically`.
- [ ] Step 2: Run the filter; expected FAIL.
- [ ] Step 3: Implement the template builder (a deterministic setup like deposits) and `FoundStation`.
- [ ] Step 4: Run full `dotnet test`; all pass.
- [ ] Step 5: Commit `Start in a starter ship and found stations`.

### Task 5: Interface logic and strings
**Files:** Modify `src/Starfoundry.Ui/Objectives.cs` (structures step list), `Loc.cs` (module kind names, structure kinds, hints, rejects `KindNotAllowed`, `NoCockpitOrEngine`, `NoSuchStructure`), `Tips.cs`, `RejectTexts.cs`. Test: `StructureObjectivesTests.cs`.
**Interfaces:** Consumes `ModuleKind`, structure queries (task 1, 2); produces `ObjectiveTracker(content, stationRules, structures)` with steps Fly, Mine, WatchFactory (first plate), FoundStation, AddModule, Research, Scan, Defend, Expand; key families `module.<kind>`, `structure.ship`, `structure.station`.
- [ ] Step 1: Failing tests for ordering and completion, old lists unchanged when the flag is off, every new key in both languages, reject texts exist.
- [ ] Step 2: Run the filter; expected FAIL. Step 3: Implement. Step 4: Run full `dotnet test`; all pass. Step 5: Commit `Add structure guided steps and strings`.

### Task 6: Module visuals
**Files:** Create `game/ModuleModels.cs` (procedural meshes per `ModuleKind`: cockpit canopy, engine nozzles with glow, reactor core, cargo crates, workshop floor and rails, defence mounts, hull plating), extend `game/Gallery.cs` to render them, update `game/StationView.cs` to draw modules by kind with the right shell for ships (engines at the back) and stations.
**Interfaces:** Produces `ModuleModels.Build(ModuleKind kind, bool ship) -> Node3D` sized to the 8 x 4 x 8 box, and `StationView.Sync` per structure.
- [ ] Step 1: Implement the models and the per-kind view. Step 2: Render the gallery and a demo ship and station headlessly and LOOK at the images (each kind recognisable, engines at the back). Step 3: `dotnet build game` and full `dotnet test`. Step 4: Commit `Add module visuals for ships and stations`.

### Task 7: Game wiring: structure nodes, local aiming, start and HUD
**Files:** Modify `game/Play.cs`, `game/Play.Ui.cs`, `game/StationView.cs`, `game/MarkerLayer.cs` and the UI code that reads machines, segments, deposits and the ship.
**Interfaces:** Consumes tasks 1 to 6. Produces one `Node3D` root per structure with the world transform (machine models, belt slabs and item MultiMeshes are children; item positions updated in local space), `CellUnderAim` returning `(structure, x, y, z)` after transforming the ray into the structure's local frame, palette entries for module kinds (the single "Module" tool offers a kind cycle key), `--structures=false` launch flag for the old campaign rules, campaign start inside the starter ship, HUD showing structure name/kind, modules by kind, flight readiness (cockpit and engines) and hold from cargo modules.
- [ ] Step 1: Implement the node-per-structure rendering and the local-frame aiming. Step 2: Implement module-kind placement (ghost per kind with the reason when the kind or place is not allowed) and `FoundStation` on its own key. Step 3: Start the campaign in the starter ship; update the markers (ship, station). Step 4: Verify `dotnet build game`, full `dotnet test`, and headless screenshots from `--hud-shot`/a structures demo flag showing the starter ship with the factory running and a flying ship with items moving; look at them. Step 5: Commit `Play from inside a modular ship and found stations`.

### Task 8: Docs, pacing check and PR
**Files:** Create `docs/adr/0018-modular-structures.md`, `docs/prototypes/structures.md`; Modify `docs/playtest.md`, `README.md` links; Test `StructurePacingTests.cs`.
- [ ] Step 1: Test `Starter_ship_makes_its_first_plate_within_two_minutes_of_mining` and `First_wave_waits_until_a_station_exists_or_five_minutes_pass` (sim only), tune content so they hold and record the measured numbers.
- [ ] Step 2: Write the ADR (decision, consequences, alternatives A/B/C) and the prototype note with the measured numbers and limits (no invented numbers).
- [ ] Step 3: Full `dotnet build game` and `dotnet test`; a short hand check of the start and of flight if possible, saying what was not played.
- [ ] Step 4: Commit, push, open the PR, wait until the CI check is registered and green, then squash-merge.

## Parallelism
Task 1 then Task 2 run first. After Task 2: Tasks 3, 5 and 6 run in parallel (disjoint files: sim, Ui, game visuals). Task 4 follows Task 3. Task 7 needs 4, 5 and 6. Task 8 is last; its ADR and prototype note can be drafted by a docs agent from the start.
