# 0043. Stations are edited as blueprints: ApplyBlueprint replaces the module tool

- Status: proposed
- Date: 2026-11-26
- Area: simulation, tooling

## Context
- Ships are redesigned at a shipyard with `DesignerApply` ([ADR 0032](0032-weapons-as-hardpoint-modules-and-the-shipyard.md)); the blueprint editor shell of phase 2 previews that in 3D ([prototype](../prototypes/blueprint-editor.md)). Stations are still extended one module at a time with `PlaceModuleIn` and `RemoveModuleIn` from first person.
- The owner asked for one editor that shows how a structure will look and what it costs before anything is built ([spec](../superpowers/specs/2026-11-26-blueprint-editor-design.md), section 3 and phase 3).
- Ships and stations always keep a dock module ([ADR 0041](0041-ship-and-station-dock-through-a-dedicated-dock-module.md)); a station has no cockpit, engine or hardpoint ([ADR 0029](0029-docking-ship-and-station-are-different.md)).

## Decision
- A new command `ApplyBlueprint(structure, modules)` (tag 65) replaces the whole module list of one of the player's structures in one atomic step. For the player's own ship it is the same code as `DesignerApply`; for a station it is the new station path below. Tags 65 to 70 are reserved for the blueprint editor.
- `SimWorld.CheckBlueprint(player, structure, modules)` returns the verdict and the price from the same code the command runs, so the editor's preview and the apply cannot disagree. A test pins that on 300 random drafts and checks that a refused draft changes nothing.
- A station blueprint is refused when: a kind is undefined or not allowed on a station (cockpit, engine, hardpoints), the core is removed, moved or duplicated, no dock module is left, the modules are not one connected piece, a new kind lacks its research (campaign), a removed or replaced module holds a machine or belt or is the port a ship is docked with, a new module overlaps a docked ship or a deposit's rock, a changed module is out of build range (unless the ship is docked at this station), or the items are missing.
- Price: a module that only moves is free, a new one pays full price, a removed one refunds half (per item), the hold pays first and the station stock the rest, the refund goes to the hold. In sandbox and free-build worlds nothing is paid.
- In worlds with `BeltNetwork.BlueprintRules` (structure rules on and `WorldRules.LegacyBlueprint` off) `PlaceModuleIn` and `RemoveModuleIn` on a station are refused with `Reject.UseBlueprint`; ships keep the single-module tools. `FoundStation` is unchanged. In a legacy world `ApplyBlueprint` is refused with `NotAllowedHere`.
- The flag is `WorldRules.LegacyBlueprint` (bit 7 of the second flags byte, not a difficulty setting). **Command log version 24**: logs of versions 5 to 23 read with the flag on, so they replay byte for byte; the reader refuses `ApplyBlueprint` in them. New worlds write version 24 with the flag off.
- The editor opens on a station at its core console (`F`, aiming at the core) and from the ship computer's Designer tab when the ship is docked at one of the player's stations without a shipyard. It reuses the ship editor (ghosts, cost and stats panels, validation list, undo and redo) with the station's catalogue, a core and dock check, and the stats that matter to a station.
- The scripted player places station modules with `ApplyBlueprint` in new worlds.

## Consequences
- Benefits: one atomic, previewable way to change a station; the same rules and numbers for the preview and the sim; legacy logs untouched; a base for interior editing (machines, belts, turrets) in the next phase.
- Costs: station growth is no longer a first-person action; a module that holds a machine or belt cannot be removed or replaced until the interior is cleared (the interior moves into the blueprint in the next phase); stations are limited to 256 modules per blueprint.
- Balance: win times of the scripted player (`--structures --minutes 120`), mm:ss, all inside their bands:

  | Preset (band) | seed 20261121 | seed 20261120 | seed 777 |
  | --- | --- | --- | --- |
  | Relaxed (44-50) | 46:15 | 46:33 | 45:46 |
  | Normal (48-53) | 48:23 | 49:05 | 48:41 |
  | Hard (51-56) | 51:51 | 55:31 | 51:47 |

- Checked by `BlueprintStationTests` (codec round trip and version gate, price and pay order, refund, free moves, atomic refusal, core and dock rules, the module tool gate, legacy log replay, preview equals apply on random drafts, the station session) and by the headless `--blueprint-check` of the game, whose second half opens the editor on a station and applies a module.

## Alternatives considered
- **A. Keep the module tool and only add a preview.** Rejected: two ways to change a station that can disagree, and no atomic multi-module change.
- **B. A separate command for stations.** Rejected: `DesignerApply` already is the ship's blueprint; one name for both keeps the editor and the log simple (the ship keeps `DesignerApply` for its own logs and the shipyard panel).
- **C. Allow the module tool in new worlds as a shortcut.** Rejected for now: the spec wants every structure edit to go through the editor; demolish and repair shortcuts are phase 5.
