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
Measured by `StationPacingTests` (scripted player, sim commands only, campaign world, station rules on, seed 20261121, 30 ticks a second). The player flies to the home iron deposit (10 cells from the ship start, in range after about one second), hand-mines with the ship refinery on, and builds from the hold as soon as it can pay. The ship refinery makes one ingot every 4 s (recipe 2 s, ship multiplier 2), so it, not hand mining (2 ore a second), sets the pace.

| Step | Cost (ingots) | Reached at |
| --- | --- | --- |
| Core module affordable and placed | 36 | 146 s (2.4 min) |
| Cargo port | 12 | 194 s (3.2 min) |
| Smelter | 14 | 250 s (4.2 min) |
| Depot | 8 | 282 s (4.7 min) |
| First wave (script keeps adding smelters and depots), warning at 939 s | | 969 s (16.2 min), 823 s after the core |

Bounds pinned by the tests: core within 4 minutes, port and smelter within 8 minutes, first wave at least 3 minutes after the core.

Before tuning, the core and module costs already met the targets (core at 146 s), so they are unchanged (core 36, hull 24, growth 25%). One change was needed: the cargo port cost 12 ingots and 4 iron plates, and plates need an assembler fed by belts, which needs the port first, so the port could never be built by hand. Its cost is now 12 ingots. Before that change the port was never reached.

The wave clock needed no sim change: the alert rises only from machine signatures, and with the rules on no machine can exist before the core. A station of core, port, smelter and depot has signature 5, below the threshold of 8, so the alert does not rise at all until the player adds more machines. With the rules off the clock is unchanged (a test checks the alert rises as soon as machines exist).

## Limits
- There is no scripted bot for the station rules, so the balance numbers for the old rules (pacing bands, wave ramp, expansion) do not describe this mode.
- Only two module kinds exist, core and hull. Power, ice and hydrogen, interiors and walking, and automatic mining ships are later projects.
- The pacing script does not run the delivery loop: it only builds the port, smelter and depot. The cargo port moves iron ingots only (not ore), so hand-mined ore reaches belts as ingots made by the ship refinery; a smelter fed by the port has no ore to take. The loop needs a design decision before the guide text promises it.
- The cargo port only moves what is in the hold, so hand-mined delivery means flying back and forth until mining ships exist.
- Per the owner, no live play test this round: checks are the build, the tests and headless screenshots.
