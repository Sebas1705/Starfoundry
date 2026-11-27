# 0047. The dock is a small collar on a module face, not a module; log version 27

- Status: proposed
- Date: 2026-11-27
- Area: simulation, content, client
- Builds on: [ADR 0041](0041-ship-and-station-dock-through-a-dedicated-dock-module.md) (a dock on both sides), [ADR 0029](0029-docking-ship-and-station-are-different.md) (docking), [ADR 0043](0043-stations-are-edited-as-blueprints-with-applyblueprint.md) (ApplyBlueprint)
- Supersedes in part: ADR 0041's "only a `dock` module is a port". ADR 0041 stays as written; the flag below keeps its rules for old logs.
- Design: [multi-cell machines, large modules and the small dock](../superpowers/specs/2026-11-27-multicell-and-large-modules-design.md) (stage 1 of 3). Log version 26 and ADR 0046 are the rock clusters, so this takes 27 and 0047.

## Context
ADR 0041 made the docking hardware a whole 8 x 4 x 8 module on the ship and on the station, for one doorway. The owner finds that a full module is too much for a port: it widens every ship by a module, takes a module cell of a station, and costs a palette entry. A port should be a small piece on a module face.

## Decision
- **A docking collar is a small piece attached to a horizontal face of an ordinary module, with no volume of its own.** The simulation keeps, per structure, a sorted set of `(module cell, face)` (`Structure.DockPieceSet`); `DockRules.CanHostPiece` says which module kinds carry one (core, hull, cargo, both reactors, shipyard on any horizontal face, a cockpit on its two flanks; not engines, hardpoints, antennas, shields nor open decks) and at most `DockRules.MaxPieces` (16) per structure.
- **A port is the free face of a collar** (`DockPorts` lists the collars; a module built against the face closes it). Range, speed, alignment, the pose, the stock, repair, defence and the walk across are unchanged, because they read ports through `DockPorts`. The ship's host module lands on the module cell beyond the station's host module, as the two dock modules did, and the airlock doorway opens between the hosts.
- **Starting layout.** The starter ship, scenario ships and pre-built structures get a collar when the world is built (`BeltNetwork.EnsureDockPiece`): a ship on the -X flank of its cockpit (where its old port was), a station on the -Z face of its core (then +Z, -X, +X). `FoundStation` puts a collar on the new core's -Z face at no extra cost; it no longer needs a clear module cell beside the core. No content changes, so the content hash and the golden checksums do not move.
- **The dock module is retired.** In a new world `PlaceModule`, `DesignerApply` and `ApplyBlueprint` refuse the `dock` kind (`KindNotAllowed`); the kind stays in the content so old logs replay.
- **A structure keeps at least one collar.** Removing the module that holds the last one is refused (`LastDockModule`); a design or blueprint that leaves none is refused (`NoDockModule`); a face that cannot carry one (`BadDockPiece`), a duplicate or more than 16 (`InvalidArgument`); a collar a ship is docked at cannot move or go (`ModuleOccupied`, undock first). A module that goes takes its collars along.
- **Price.** A collar costs half a dock module (rounded up) and refunds half; a collar that only moves is free; each collar adds the stock capacity a dock module did (`DockStockCapacity`). It is priced inside the same function as modules (`PriceDesign`), so the preview and the apply agree.
- **Commands.** `DesignerApply` and `ApplyBlueprint` carry the collar set (tags 67, 68 and 69: the designer, the blueprint, the blueprint with an interior); without it the collars that still have a host are kept, so tags 46, 65 and 66 and every client that does not know collars keep working. The log version is 27.
- **Compatibility.** `WorldRules.LegacyDockPieces` (third flags byte, bit 2, value 4; off in every preset and in the new-game screen) keeps ADR 0041 as it was: dock modules are the ports and collars are refused. Logs of versions 5 to 26 read with it on and replay as recorded; it hashes as default rules like the other legacy flags; the reader refuses tags 67 to 69 in them. Checksum: a collar set is hashed only when a structure has any. `LegacyDock` (ADR 0041, ADR 0029 ports) wins over it: such a world has no collars.
- **Client.** A collar is drawn as a flange with eight capture clamps and a ring of lamps on its face (`ModuleModels.CollarPiece`) over the host module, which is not rebuilt; port markers, guide lights, the assisted approach and the airlock follow `DockPorts` unchanged. The blueprint editor has a *Dock collar* tool (es *Enganche*): each click on a module turns its collar to the next free face and then takes it off; collars are in the undo history, move with a moved module and go with a removed one. The palette no longer lists the dock module in a new world. The old ship designer keeps the collars and no longer lists the dock.

## Consequences
- Benefits: a port that is a piece and not a room; ships and stations are a module shorter or freer; more than one dock is cheap; nothing in the content or the old logs moves.
- Costs: old fixtures that pin ADR 0041 geometry run with the legacy flag (`TestContent.LegacyShip`, `DockModuleTests`); the ship's cockpit, core and others host the dock, so the dock position is part of a layout decision, not a free-standing module.
- Checked by `DockPieceTests` (collar rule and hosts, docking collar to collar, the retired kind, last-collar rules, the price, moves and removals, 120 random blueprints with collars with preview equal to apply, tags 67 to 69 and the version gate, replay and clone, the flag, logs 22 to 26 replayed with dock modules, texts, and the draft's collar tool and history); the older dock-module tests run with the flag; `--blueprint-check` clicks the collar tool on the docked collar.
- Balance: the collar replaces a module that cost nothing in the starter layouts, so the first minutes are unchanged. Win times of the scripted player (`--structures --minutes 150 --delay=6`), mm:ss:

  | Preset (band) | seed 20261121 | seed 20261120 | seed 777 |
  | --- | --- | --- | --- |
  | Relaxed (44-50) | 46:15 | 46:37 | 45:46 |
  | Normal (48-53) | 48:11 | 49:13 | 48:41 |
  | Hard (51-56) | 51:36 | 55:31 | 51:47 |

  All inside the bands (Normal is back inside its band, where ADR 0041 left it about 15 seconds under).
- Not decided here: a collar on the roof or the floor (vertical docking), shapes that cut a host's face (the hull shapes proposal will check `CanHostPiece` against a full face), a second visual variant for a ship that docks from the nose.

## Alternatives considered
- **A. Keep the dock module (ADR 0041).** Kept for old logs only.
- **B. A small pod module (a half-size module).** Rejected: the grid has one module size; a pod would need sub-cell geometry in the sim, the codec and the models.
- **C. Ports on every module face for free.** Rejected: the owner wants a physical thing to build; the collar is the thing, and it costs a little.
- **D. A collar as part of the module list (a flag on `DesignModule`).** Rejected: it would change the shape of every existing command; a separate optional set keeps the old tags valid.
