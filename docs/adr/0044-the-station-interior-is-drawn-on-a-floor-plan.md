# 0044. The station interior is drawn on a floor plan and applied with the blueprint

- Status: proposed
- Date: 2026-11-26
- Area: simulation, tooling

## Context
- [ADR 0043](0043-stations-are-edited-as-blueprints-with-applyblueprint.md) put station modules in the blueprint editor. Machines, belts, turrets and walls are still placed one by one in first person (`PlaceMachineIn`, `PlaceSegmentIn`), and a module that holds any of them cannot be removed or replaced.
- The spec ([phase 4](../superpowers/specs/2026-11-26-blueprint-editor-design.md)) asks for a top-down floor plan per bay, grid-snapped belts with a flow arrow, ghost rules, and a cost and validation panel that covers the interior.

## Decision
- `ApplyBlueprint` may carry an interior (`BlueprintInterior`: machines, belt segments and links), written as **tag 66** (tag 65 stays the module-only form). With an interior, the same atomic step also makes the station's machines, belts, turrets and walls exactly that interior; without one the interior is left alone. A ship takes none (`Reject.UseStation`).
- Parts that match a current one are kept with their state (machines by cell and type, a recipe change is a plain recipe change; belts by cell, direction, length and tier); the rest are built at full price or removed with half back, merged with the module price into one net per item, paid from the hold then the station stock. A machine that moves is a removal and a build.
- Validation is the cell rules of the single-part commands applied to the blueprint's own modules: a module under every cell, its kind allows the machine's role or belts, the cell is on its floor (`NotOnFloor`), no two parts share a cell, recipes and tiers exist and are unlocked, no new ship machines or drills, counts within limits, build range unless docked here. Links must be what the link commands accept (touching ends, roles, one input and at most two outputs per belt, two per machine, a sink or source belt keeps its rules); a belt with two outputs is a splitter. A blueprint that removes a module together with what stands in it is valid; the module-only form still refuses it (`ModuleOccupied`).
- Links are explicit in the command: what exists between parts that stay and is not in the blueprint is disconnected, what the blueprint has and is not there is connected. The editor makes the links the way the game does when you place something that touches.
- `CheckBlueprint(..., interior)` is the preview and runs the same code, so preview and apply cannot disagree; the apply guards itself with a snapshot so a refusal cannot leave half a blueprint. A test pins preview against apply on random interiors, checks the world invariants and replays the log.
- In new worlds `PlaceMachineIn` and `PlaceSegmentIn` on a station are refused with `Reject.UseBlueprint`; removing, linking, recipes, repair and the machine panel keep their commands (the demolish and repair shortcuts of the spec are phase 5). The gate is `WorldRules.LegacyInterior` (bit 0 of a third flags byte, rules format 3, written only when set). **Command log version 25**: logs of versions 5 to 24 read with the flag on and replay byte for byte; the reader refuses tag 66 in them.
- The editor's floor plan: select a module that allows building (or double click it) and press the Floor plan button; its 8 by 8 floor is drawn top-down with the machine palette (the build menu's machines, so turrets and walls only go in defence modules), the belt tiers, select, erase, undo and redo, a machine panel (recipe cycle) and a clear-the-module button. Belts are dragged along the grid (an L: along X then Z, `Shift` for Z first), split at the bends, and every cell shows an arrow. Red and amber dots mark belt ends that lead nowhere and starts nothing feeds. The cost, stats (the machines' power draw makes the power balance) and validation panels cover the interior; belt gaps are warnings, parts in a missing module and parts off the floor are errors.
- The scripted player places station machines, belts, turrets and walls with `ApplyBlueprint` in new worlds (it sends the station's modules and its interior plus the new part; its explicit link commands still work).

## Consequences
- Benefits: the whole station is previewed and applied as one atomic, priced step; modules can be rebuilt with their floors in one blueprint; legacy logs untouched.
- Costs: removing a part in the plan is a half refund (the single-part removal refunds all; it stays for the shortcut); splitters are belts that feed two belts, not a separate machine (the simulation has none); a storage crate machine was not added; stacks on the floor are not carried with a ship (not required); the visual QA of the plan is still to do.
- Balance: scripted-player win times (`--structures --minutes 120`), mm:ss, all inside their bands:

  | Preset (band) | seed 20261121 | seed 20261120 | seed 777 |
  | --- | --- | --- | --- |
  | Relaxed (44-50) | 46:15 | 46:33 | 45:46 |
  | Normal (48-53) | 48:11 | 49:05 | 48:41 |
  | Hard (51-56) | 51:36 | 55:31 | 51:47 |

- Checked by `BlueprintInteriorTests` (codec and version gate, build, keep, remove, refund, recipe change, every refusal, preview equals apply on random interiors, replay, the in-world gate) and `BlueprintFloorPlanTests` (belt paths and bends, ghost rules, the session's cost equals the simulation's, gap and orphan warnings), and by the second half of the game's headless `--blueprint-check` (floor plan nodes, layouts in both languages, drawing, applying and reloading).

## Alternatives considered
- **A. Let the simulation link what touches.** Rejected: the bot's layouts have touching parts that are not meant to be linked, and a blueprint should say what it links.
- **B. One flag for modules and interior.** Rejected: logs recorded between the two changes would replay differently; two versions keep both exact.
- **C. Price moved machines as free.** Rejected for now: a machine loses its buffers when it moves; a free move can come with the demolish and repair shortcuts.
