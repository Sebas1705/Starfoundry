# 03 Ship and station builder

Status: proposal. Reference states: modular ships and stations, decisions about cargo, reactor, weapons, engines, power; decoration; save, share, creative mode.

## Shared model
Ships and stations use the same **module graph**: a hull frame with typed **sockets** and attached modules. Stations are static; ships add propulsion and a flight model. One editor serves both.

## Hull frames
| Frame | Sockets | Mass cap | Use |
| --- | --- | --- | --- |
| Skiff | 8 | 40 t | Starter scout / miner |
| Tug | 16 | 120 t | Starter ship *Halcyon* |
| Hauler | 32 | 600 t | Cargo |
| Frigate | 32 | 500 t | Combat |
| Capital | 96 | 4,000 t | Endgame battleship |
| Station core | 64 + expandable | n/a | Anchor |

Sockets are typed (internal, external, hardpoint, engine, utility). Modules declare the socket type and footprint they need.

## Module categories
- **Propulsion:** main engines, thrusters, jump drive (tier 3+).
- **Power:** reactors, batteries, capacitors.
- **Cargo:** holds (solid, fluid, bulk), docking ports, drone bays.
- **Industry:** mining laser, drill arm, micro-refinery, fabricator.
- **Defence:** armour plates, shield projector, point defence.
- **Weapons:** kinetic railgun, beam laser, missile rack (names illustrative).
- **Utility:** scanner, sensor mast, repair beam, cloak plate (tier 4).
- **Cosmetic:** paint, decals, lights, interior props.

## Stats model
Derived, not hand-set, so trade-offs are real:
- mass = hull + sum(modules + cargo carried)
- thrust / mass = acceleration
- power balance = generation - sum(draw), with heat as a second budget on larger ships
- hit points = hull + armour; shield capacity from projectors
- internal volume limits cargo and crew facilities

The editor shows live stats and warnings (underpowered, overheating, unbalanced mass).

## Interior
Walkable interiors on stations and larger ships: cockpit, cargo bay, workshop. First-person movement continues across the dock. Interiors are optional per frame; skiffs are cockpit-only.

## Editor tools
- Symmetry (mirror X/Y/Z, radial).
- Ghost preview, undo/redo, snap grid.
- Compare design vs current ship.
- Save as **design template**; order a build from a station fabricator (consumes materials).
- Creative mode: unlimited materials, no research gate, for sharing and testing designs.

## Sharing
Design files (see [10-data-model](10-data-model.md)) are plain, versioned JSON plus a thumbnail; sharing through file export in the MVP, Steam Workshop later (TBD).

## Validation
A design is valid when sockets are respected, parts are connected, and required modules exist (power source for powered parts; engine for ships). Invalid designs can be saved as drafts but not built.

## Open questions
Voxel-style freeform hulls vs socketed frames (current proposal is socketed, simpler to balance and network). See [12-open-questions](12-open-questions.md); a decision here will need its own ADR.
