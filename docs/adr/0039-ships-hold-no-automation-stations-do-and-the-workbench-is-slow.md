# 0039. Ships hold no automation, stations do, and the workbench is a slow queue; log version 20

- Status: proposed
- Date: 2026-11-26
- Area: simulation, content, gameplay, client
- Design: [blueprint editor spec](../superpowers/specs/2026-11-26-blueprint-editor-design.md) (phase 1)
- Builds on: [ADR 0031](0031-construction-rules-fabrication-bays-and-floor-building.md) (floor building, the workbench), [ADR 0029](0029-docking-ship-and-station-are-different.md) and [ADR 0032](0032-weapons-as-hardpoint-modules-and-the-shipyard.md) (what a ship or a station may hold)

## Context
Since ADR 0031 a player could add a Fabrication bay to the starter ship and build the whole factory aboard, and the first story levels and the bot did exactly that. The owner wants a ship to be a ship (cockpit, engine, hold, reactor, hardpoints, no on-foot building) and a station to be where automation lives, with the ship's workbench kept as a deliberately inefficient manual option that has a real interface, like the machines.

## Decision
- **Rule flag.** `WorldRules.LegacyShipAutomation` (bit 3 of the second flags byte; true means the old behaviour) is off for every new world and on for logs older than version 20. Like the other legacy flags it is hashed away, so the golden checksums of old logs do not move. The new rules apply to structure worlds only (`BeltNetwork.StationFirst`): worlds without structures are untouched.
- **A ship holds no automation.** With the rule on, `PlaceMachineIn`, `PlaceSegmentIn`, `PlaceModuleIn` (Fabrication bay) and `DesignerApply` (a design with a bay) on a ship are refused with the new `Reject.UseStation` (es and en texts). That covers machines, belts, turrets and walls (all are machines or belts) and bays. `ModuleKindIds.AllowedOn(structure, kind, stationFirst)` says a ship takes no Workshop. Stations keep today's rules.
- **The workbench is a queue.** New commands `QueueBench(recipe, count)` (tag 63) and `ClearBench` (tag 64). The bench makes one craft at a time from a queue of at most five, taking the inputs from the hold when a craft starts, in eight times the recipe time (8 s for the shipped 1 s recipes), with the three recipes only (ingot, plate, ammo). `PlayerInfo.BenchQueue` exposes the queue; the queue is mixed into the checksum only when not empty. In a station-first world `SetShipRecipe` (the repeating recipe) is refused; in a legacy world it works as before and the queue commands are refused.
- **Command log version 20.** It adds tags 63 and 64 (the reader refuses them in older logs) and reads older logs with `LegacyShipAutomation`.
- **Client.** Aboard a ship of a station-first world the build tool and the build menu entries that need a bay are closed, with the hint "Automation is built in a station". `F` on the workbench opens a workbench panel (recipes, queue, progress, a "manual, slow" label); `F` on a machine of a station opens a machine panel (recipes, status, throughput, enable and disable). Both are in the key hints and the F1 actions.
- **Content (tuning, measured with the structure bot).** The station core costs 12 ingots and 8 plates (was 16 and 12) and the reactor module 8 plates and 8 carbon (was 16 and 8), so the first station is founded in about five game minutes from hand mining and the workbench alone and its reactor shortly after (a station with machines needs a reactor). The first factory (ore port, smelter, depot) stands on the core floor, the first bay follows. The alert meter's capacity and recovery (`alert_max`, `alert_after_wave`) are lowered so that the later start does not move the win out of the difficulty bands. Levels 01 to 03 and 07 are rewritten for the station-first loop (see [scenarios](../prototypes/scenarios.md)).

## Consequences
- The early game is slower by design (bench versus factory is an order of magnitude); the bot opens with the workbench, founds the station, powers it and builds the factory there.
- A ship has no turrets, so the station carries the whole defence; the bot keeps the ship beside it.
- Old saves and logs replay as recorded. Content changes mean saves made with older content refuse to load (ADR 0024).
- The blueprint editor (phases 2 to 5) will apply drafts with `DesignerApply` and `ApplyBlueprint`, and the in-world station building of phase 1 stays until then.

## Alternatives considered
- **A. Flag plus new rejects (chosen).** The same machinery as ADR 0027 to 0036: smallest change, old logs replay byte for byte.
- **B. Remove the old commands.** Rejected: old logs and the golden checksum tests need them.
- **C. A repeating bench with a longer time.** Rejected: the spec asks for a queue so a player can plan a few crafts and walk away.
- **D. Raise the bench speed instead of lowering the core cost.** Rejected: the spec fixes the bench at 8 s to keep automation clearly better.
