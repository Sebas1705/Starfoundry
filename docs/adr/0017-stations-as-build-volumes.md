# 0017. Stations as build volumes

- Status: proposed
- Date: 2026-11-21
- Area: simulation, gameplay

## Context
- In the reference game (*Main Sequence*) machines are built only inside a station that the player owns and enlarges module by module; ore reaches it from the ship. In Starfoundry today the player builds anywhere within 24 cells of the ship and puts drills straight on deposits.
- The owner wants the reference rule: structures exist only inside a station, the station is built and changed by the player, and ore reaches it through the ship (automation comes later with research).
- Every change to the world goes through a logged command and the simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md), [ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)). The command log header already carries a threats flag, and 401 tests, old logs and the scripted balance player depend on the free-build rules staying as they are.
- The full design is in the [stations design spec](../superpowers/specs/2026-11-21-stations-design.md). This is project 1 of 4 (stations and build rules; power; interiors and walking; automatic mining ships).

## Decision
- **Module model (B).** A station is a set of cube modules on a grid aligned at multiples of the module size, 8 x 8 x 4 cells (one constant). The first module is the core; every later module must touch an existing module face to face. In this step modules have two kinds, `Core` and `Hull`. Station state is per player and is part of clone, checksum and `ValidateInvariants` (exactly one core, every module connected, none outside the world limits).
- **A rule flag in the log header.** A world setting `StationRules` (bool) is stored in the command log header and in saves. With it off, behaviour is exactly today's. Old logs and saves read as rules off.
- **Commands.** `PlaceModule(mx, my, mz)` and `RemoveModule(mx, my, mz)`. Placing needs the module centre within the ship's build range and pays an item cost from the hold that grows with the module count (content `modules`: `core_cost`, `hull_cost`, `growth_percent`). Removing is refused if a machine or belt sits in the module or if it would disconnect the station, and refunds half the cost. Module commands work whatever the flag says; the flag only gates other placement.
- **Placement gating.** With `StationRules` on, `PlaceMachine` and `PlaceSegment` are rejected with `Reject.OutsideStation` unless every cell they cover lies in a module. The 24-cell build range from the ship still applies on top.
- **Drills and the cargo port.** With the rules on, drill machines are not placeable (`Reject.NotAllowedHere`): they need a deposit, and deposits lie outside any station. The cargo port (`Unloader`, [ADR 0015](0015-cargo-ports-unload-the-hold-onto-belts.md)) is available from the start with no research prerequisite, so ore flows hand mining, hold, cargo port, belt, smelter.
- **Old rules stay.** The sandbox, the tests and the scripted balance player keep the free-build rules. The campaign turns the rules on; a launch flag, `--station-rules=false`, starts a campaign with the old rules for comparison.

## Consequences
- Benefits: the reference build rule with a small change to the simulation; one flag and two commands, all logged, so replays, saves and the checksum cover stations with no extra save code ([ADR 0016](0016-saves-are-the-command-log-replayed.md)); old logs, saves and tests are untouched.
- Costs: more state and more rejects to explain in the interface (outside the station, not adjacent, occupied, not allowed here); a module ghost and a station view in the game.
- Pacing risk: with no drills, the first hour depends on hand-mined ore. It may be slower than the first wave of the scripted player (12:28), so the core cost and the start of the first wave need re-tuning, and threats may have to start counting from the core in this mode. The numbers are measured by the pacing task, not decided here.
- Pacing (measured, see `docs/prototypes/stations.md`): the core is affordable by hand at 2.4 minutes, the port and smelter by 4.2 minutes, and the first wave comes 13.7 minutes after the core, so the module costs stay and the sim needs no change to start the wave clock at the core (alert only comes from machines, which need a core). One decision changed: the cargo port now costs ingots only, because its plates could not be made without a port.
- The balance numbers recorded for the old rules (pacing bands, wave ramp, expansion times) no longer describe the campaign.
- There is no scripted bot for the station rules yet, so this mode has no measured balance beyond the pacing check. A bot script is a follow-up.
- The cargo port only moves what is in the hold, and the hold is small, so long sessions of flying back and forth may feel tedious until mining ships exist (project 4).
- How it is checked: simulation tests for placement outside and inside a module, the core, adjacency, range, cost, removal rules, drills refused, the cargo port with no research, old rules unchanged with the flag off, replay and save round trips, and checksum changes; the existing tests and balance tests must give identical results with the flag off.
- Not decided here: interior spaces and walking, docking animation, power and hydrogen, mining ships, module kinds beyond Core and Hull, several stations per system.

## Alternatives considered
- **A. Build zone:** the station is a point and the player builds within a radius. Almost no change from today, but it does not make the station something the player builds and shapes. Rejected.
- **C. Free station:** the player places walls and floors cell by cell and the station is what they enclose. It needs room detection and is too large for this phase. Rejected for now.
