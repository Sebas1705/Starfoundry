# Architecture decision records

One decision per file, `NNNN-slug.md`. An accepted ADR is never rewritten; add a new one and mark the old one superseded.

| # | Decision | Status | Date |
| --- | --- | --- | --- |
| [0001](0001-engine.md) | Use Godot 4 with C# for the game layer | accepted | 2026-11-19 |
| [0002](0002-deterministic-simulation-core.md) | Engine-independent deterministic simulation core | accepted | 2026-11-19 |
| [0003](0003-commands-as-the-only-way-to-change-the-world.md) | All world changes go through validated, logged commands | accepted | 2026-11-19 |
| [0004](0004-data-driven-content-and-one-cell-machines.md) | Data-driven content and one-cell buffered machines | proposed | 2026-11-19 |
| [0005](0005-campaign-economy-hold-costs-and-research-gates.md) | Campaign mode: build costs paid from the hold, research gates content | proposed | 2026-11-19 |
| [0006](0006-ship-movement-as-intent-commands-and-ranges.md) | Ship movement as intent commands, with mining and build ranges | proposed | 2026-11-20 |
| [0007](0007-galaxy-as-regions-of-one-space-with-deterministic-generation.md) | The galaxy is a set of regions of one shared space, generated deterministically | proposed | 2026-11-20 |
| [0008](0008-remnant-threat-as-signature-driven-waves.md) | The Remnant threat: signature fills an alert meter, the meter sends waves | proposed | 2026-11-20 |
| [0009](0009-pacing-targets-measured-by-a-scripted-player-and-a-per-item-depot-cap.md) | Pacing targets are measured by a scripted player; depots deliver at most 40 percent of the hold per item | proposed | 2026-11-20 |
| [0010](0010-waves-can-start-later-and-the-ramp-is-measured-per-turret.md) | Waves can start later, and the wave ramp is measured against the number of fed turrets | proposed | 2026-11-20 |
| [0011](0011-walls-draw-enemies-and-repairs-restore-hit-points.md) | Walls draw enemies, repairs restore hit points | proposed | 2026-11-20 |
| [0012](0012-repair-bays-heal-slowly-from-a-belt-fed-item.md) | Repair bays heal slowly from a belt-fed item | proposed | 2026-11-20 |
| [0013](0013-defence-upgrades-are-research-bonuses.md) | Defence upgrades are research bonuses | proposed | 2026-11-20 |
| [0014](0014-repeatable-research-with-rising-cost.md) | Repeatable research with rising cost | proposed | 2026-11-20 |
| [0015](0015-cargo-ports-unload-the-hold-onto-belts.md) | Cargo ports unload the hold onto belts | proposed | 2026-11-20 |
| [0016](0016-saves-are-the-command-log-replayed.md) | Saves are the command log, replayed | proposed | 2026-11-20 |
| [0017](0017-stations-as-build-volumes.md) | Stations are the only places to build, made of player-placed modules | proposed | 2026-11-21 |
| [0018](0018-modular-structures.md) | Ships and stations are one thing: structures of typed modules on a local grid | proposed | 2026-11-21 |
| [0019](0019-power-and-fuel.md) | Reactors burn hydrogen from the owner's hold; power satisfaction scales machines and engines | proposed | 2026-11-21 |
| [0020](0020-drone-automation.md) | Drone bays mine an assigned deposit for the owner without moving entities in the simulation | proposed | 2026-11-21 |
| [0021](0021-structure-defence.md) | Waves hit structure modules, modules have hit points, a station dies with its core and a ship with its last cockpit | proposed | 2026-11-21 |
| [0022](0022-structure-economy-fixes.md) | Structure worlds: free ports, quartz and carbon ports, emergency reactor power, cheaper defence and reactor modules, no removing the last essential ship module | proposed | 2026-11-21 |
| [0023](0023-world-rules-and-difficulty.md) | World rules (integer percentages and flags) and difficulty presets are stored in the log header, version 7 | proposed | 2026-11-22 |
| [0024](0024-resource-expansion.md) | Silicon, circuits, titanium alloy and fuel cells, with shield, sensor and Mk2 reactor modules | proposed | 2026-11-22 |
| [0025](0025-scenarios-and-goals.md) | Scenarios are data files with goals, a story and an engine-free goals engine; the log header (version 8) carries the scenario id | proposed | 2026-11-22 |
| [0026](0026-science-fields-and-research-expansion.md) | Three science fields, dedicated machines and 15 research nodes; harder Normal waves with the bulwark | proposed | 2026-11-22 |
| [0027](0027-mining-laser-rules.md) | The mining laser is a simulation rule: a trigger command, a power draw served before the machines and `mining_rate` research; log version 9 | proposed | 2026-11-22 |
| [0028](0028-world-generation-obstacles-and-collisions.md) | A wider galaxy with obstacles: seeded star types, rocks, mines, clouds, wrecks and debris, boost and collision damage; log version 10 | proposed | 2026-11-22 |
| [0029](0029-docking-ship-and-station-are-different.md) | The ship moves and the station is fixed; the ship docks at ports (core, cockpit flanks, dock module), is locked to the port, shares an airlock and a station stock; log version 11 | proposed | 2026-11-22 |
| [0030](0030-ship-attitude-in-the-simulation.md) | The ship attitude is simulation state: a fixed-point quaternion set by SetAttitude (tag 42), hit volumes, ports and module positions turn with it, docking needs alignment; log version 12 | proposed | 2026-11-22 |
| [0031](0031-construction-rules-fabrication-bays-and-floor-building.md) | Machines and belts are built on the floor of a fabrication bay; the starter ship has a workbench, not a factory; log version 13 | proposed | 2026-11-22 |
| [0033](0033-flight-inertia-solid-deposits-and-hit-volumes.md) | The ship has inertia (a thrust sets a target velocity under acceleration and braking limits), deposits are solid, hit volumes follow the drawn models plus a hull padding, engine research raises the speed; log version 15, legacy-gated | proposed | 2026-11-24 |
| [0032](0032-weapons-as-hardpoint-modules-and-the-shipyard.md) | Weapons are hardpoint modules of the ship (mining laser and gun, mark 1 and 2) fitted in a shipyard's ship designer (DesignerApply, tag 46); one module catalogue for ships and stations; log version 14, legacy-gated | proposed | 2026-11-22 |
| [0034](0034-optional-material-chains-and-read-only-goal-types.md) | Five optional materials (composites, coolant, wreck-only resonant crystals and lenses) behind Mk2 hardpoints, and six goal types that only read the world; no log version change | proposed | 2026-11-24 |
| [0037](0037-external-assets-policy.md) | External assets policy: accepted and rejected licences for bundled fonts, sounds and art, a row per file in ASSETS.md, a manifest test and an in-game Credits page | proposed | 2026-11-25 |
| [0038](0038-dropping-items-and-picking-them-up-by-hand.md) | Items can be dropped onto the ground and picked up by hand: DropItems and PickUp (tags 60 and 61), dropped stacks share the ore-pickup entity list; log version 19 |
| [0039](0039-ships-hold-no-automation-stations-do-and-the-workbench-is-slow.md) | Ships hold no automation (Reject.UseStation), stations do; the workbench is a slow queue (QueueBench and ClearBench, tags 63 and 64); log version 20 | proposed | 2026-11-26 |
| [0040](0040-infinite-rock-field-generated-on-demand.md) | An infinite field of resource asteroids generated on demand from the world seed, sector by sector: size times density decides the ore, emptied rocks are destroyed, no world boundary; log version 21, legacy-gated (0039 and version 20 are the blueprint editor) | proposed | 2026-11-26 |
| [0041](0041-ship-and-station-dock-through-a-dedicated-dock-module.md) | Ship and station each carry a dock module and dock only through it: starter ship, scenario ships and founded stations get one, the last one cannot be removed or designed away; log version 22, legacy-gated (partly supersedes the rejected alternative C of 0029) | proposed | 2026-11-26 |
| [0042](0042-antenna-variants-heavy-cannon-and-flared-walls.md) | Antenna variants (short and long range sensors, comms antenna) with stacking and navigation reach, a slow heavy cannon, flared wall sections; log version 23, legacy-gated (0041 and version 22 are the dock module) | proposed | 2026-11-26 |
| [0043](0043-stations-are-edited-as-blueprints-with-applyblueprint.md) | Stations are edited as blueprints: ApplyBlueprint (tag 65) replaces the station module tool, with the same verdict and price as the editor's preview; log version 24, legacy-gated | proposed | 2026-11-26 |
| [0044](0044-the-station-interior-is-drawn-on-a-floor-plan.md) | The station interior (machines, belts, turrets, walls, links) is drawn on a floor plan and applied with ApplyBlueprint (tag 66); in-world placement is refused in new worlds; log version 25, legacy-gated | proposed | 2026-11-26 |
| [0045](0045-saved-blueprints-live-in-user-data-and-the-walkthrough-is-client-only.md) | Saved blueprints (slots, variants, presets) live in user data, the walkthrough is a client camera mode, on foot the build tool closes machines and belts while demolish and repair stay; no simulation change, no log version | proposed | 2026-11-26 |
| [0046](0046-clustered-rock-fields-with-voids-size-classes-and-giants.md) | Clustered rock fields: cluster parents per 800-cell super-sector (spheres, belts, shells) with voids between, size classes and rare giants, rock radius times 2.5, a batched MultiMesh renderer with far glow; log version 26, legacy-gated by LegacyRockClusters | proposed | 2026-11-27 |
| [0047](0047-the-dock-is-a-small-collar-on-a-module-face.md) | The dock is a small collar on a module face, not a module: collar set per structure, tags 67 to 69, price, legacy flag `LegacyDockPieces`, log version 27 | proposed | 2026-11-27 |
| [0048](0048-windows-release-pipeline.md) | Windows release pipeline: a VERSION file, the pack beside the exe, icon and version stamped by the Win32 API, an Inno Setup installer, and a release workflow that only drafts | proposed | 2026-11-28 |
| [0049](0049-save-slots-checkpoints-and-slot-files.md) | Save slots: one checked file per save (name, screenshot, checksum), automatic checkpoints at key moments, a quick slot and a Save/Load screen; no simulation change | proposed | 2026-11-28 |
| [0050](0050-machines-cover-several-cells-in-irregular-footprints.md) | Machines cover several cells in irregular footprints (L, T, long, 2 x 2, plus) with a turn: mask in content, anchor, tag 70, `LegacyFootprints`, log version 28 | proposed | 2026-11-28 |
| [0051](0051-large-modules-are-spans-of-ordinary-cells-with-a-joined-floor.md) | Large modules are spans (2 x 2, 3 x 3) of ordinary cells with a joined floor: tags 71 and 72, price discount, research gate, `LegacyLargeModules`, log version 29 | proposed | 2026-11-29 |
| [0052](0052-physical-units-are-a-display-layer-one-cell-is-one-metre.md) | Physical units are a display layer: one cell is one metre, every item has a mass, power is in kilowatts; metric auto-scaling formatters and a client-side physical table; no simulation change, no log version | proposed | 2026-11-28 |
| [0036](0036-free-fire-recoil-hold-overflow-and-resource-density.md) | Guns fire along an aim with projectiles and recoil, a full hold overflows into ore pickups, the galaxy holds more resources; log version 17 | proposed | 2026-11-24 |
| [0035](0035-personal-inventory-container-moves-and-reactor-fuel-slots.md) | A personal inventory of 24 slots, one MoveItems command for every pair of containers (bag, hold, station stock, reactor fuel slot, machine buffers, bin), a feed-source setting per reactor and SortInventory; log version 18, legacy-gated, hashed only when used | proposed | 2026-11-24 |
| [0053](0053-world-scale-realistic-spacing-with-a-cruise-drive.md) | World scale: realistic spacing (rocks 33 m to 1.6 km, clusters 100 km apart, bubbles of 1.5 Gm, light-year jumps) with a cruise drive and a time-to-contact governor, a floating origin in the view; `LegacyWorldScale`, log version 30 | accepted | 2026-11-28 |

## Template
```markdown
# NNNN. Title as a decision

- Status: proposed | accepted | superseded by ADR NNNN
- Date: YYYY-MM-DD
- Area: engine | simulation | data | network | tooling

## Context
Facts only.

## Decision
Present tense.

## Consequences
Benefits, costs, how it is checked.

## Alternatives considered
```
