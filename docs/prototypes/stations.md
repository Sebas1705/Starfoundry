# Prototype: stations as build volumes

Status: in progress (2026-11-21). Decision: [ADR 0017](../adr/0017-stations-as-build-volumes.md). Design: [stations design spec](../superpowers/specs/2026-11-21-stations-design.md). Plan: [implementation plan](../superpowers/plans/2026-11-21-stations.md).

## The question
What happens to the first hour if machines can only be built inside a station that the player builds module by module, and ore can only reach it through the ship?

## What is being built
| Area | Change |
| --- | --- |
| Simulation | A station is a set of 8 x 8 x 4 cell modules per player, the first one the core. Commands `PlaceModule` and `RemoveModule`; costs from the hold in content (`modules`: `core_cost`, `hull_cost`, `growth_percent`). New rejects: outside station, not adjacent, module occupied, not allowed here |
| Rules flag | `StationRules` in the command log header and in saves. Off keeps the old rules exactly; old logs and saves read as off |
| Placement | With the rules on, machines, belts, turrets and walls only go on cells inside a module (every covered cell), within the 24-cell build range from the ship |
| Drills and cargo port | Drills are not placeable with the rules on. The cargo port is available from the start, so hand-mined ore goes hold, port, belt, smelter |
| Interface | Guided steps for the station (fly, mine, refine, core, cargo port, smelter and on), new strings in es and en, a hint about delivering hand-mined ore through the port |
| Game | Modules drawn as a floor plate and edge frame with a core marker, a "Module" palette entry with a ghost snapped to the module grid, machine ghosts that go red outside a module with the reason, a station marker and module count in the HUD |

## The rules
- The first module you place is the core; each later one must touch an existing module on a face (a diagonal neighbour does not count) and be within build range of the ship.
- A module costs items from the hold, more for each module already placed.
- A module cannot be removed while a machine or belt is inside it, or if removing it would split the station. Removing gives back half the cost.
- Nothing else can be built outside a module. A belt that crosses a module edge is refused.
- The sandbox, the tests and the scripted balance player keep the old free-build rules.

## How to run it
- The campaign uses the station rules by default.
- `--station-rules=false` starts a campaign with the old rules, to compare.
- Sandbox and the `--shot` demos keep free building.

## Measured
To be filled in by the pacing task.

## Limits
- There is no scripted bot for the station rules, so the balance numbers for the old rules (pacing bands, wave ramp, expansion) do not describe this mode.
- Only two module kinds exist, core and hull. Power, ice and hydrogen, interiors and walking, and automatic mining ships are later projects.
- The cargo port only moves what is in the hold, so hand-mined delivery means flying back and forth until mining ships exist.
- Per the owner, no live play test this round: checks are the build, the tests and headless screenshots.
