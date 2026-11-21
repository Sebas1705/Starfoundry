# Stations as build volumes: design

Status: proposed (2026-11-21). Project 1 of 4 (stations and build rules; power; interiors and walking; automatic mining ships). Station model B (modules) chosen by the owner.

## Intent
In Main Sequence you mine and refine from the ship, and build machines only inside a station you own and enlarge module by module. Starfoundry today lets you build anywhere within 24 cells of the ship and put drills on deposits. The owner wants the Main Sequence rule: structures exist only inside a station, the station is built and modified by the player, and ore reaches it from the ship (automation arrives later with research).

## Goals
- A station is a set of modules on a module grid; machines, belts, turrets and walls can only be placed on cells inside a module.
- The first module (the core) is built by the player from refined ingots; further modules extend the station.
- Ore reaches the station through the ship: hand mining, then the hold unloads into the station through the cargo port (the existing `Unloader`).
- The campaign uses these rules; sandbox, tests and the scripted bot keep the old free-build rules unchanged.

## Non-goals
- Power, ice and hydrogen (project 2), interiors and walking (project 3), automatic mining ships (project 4).
- Ship design and several stations per system.
- Re-measuring the whole balance with a new bot script (planned, not part of this step).

## Anti-goals
- Breaking old logs, saves, replays or any of the 401 tests.
- A first hour where the player cannot do anything useful because the core costs too much.

## Constraints
- The simulation stays deterministic and integer only; stations are changed only through commands.
- Module grid: modules are 8 x 8 x 4 cells and sit on a grid aligned at multiples of the module size.
- Build range from the ship (24 cells) still applies on top of the station rule.
- Command log header already carries a threats flag; a new header version adds the rule flag and old logs load as the old rules.

## Approaches
- A. Build zone: the station is a point and you build within a radius. Almost no change from today. Rejected.
- B. Modules (chosen): a core plus cube modules glued face to face; machines only on module cells; the station grows by adding modules.
- C. Free station: place walls and floors cell by cell and the station is what you enclose. Needs room detection. Rejected as too large for this phase.

## Design

### Simulation
- New world setting `StationRules` (bool), part of `SimWorld.CreateGalaxy`/constructor options and of the log header; false keeps today's behaviour exactly.
- Station state per player: a set of module cells (module coordinates `mx,my,mz`), each with a kind (`Core` for the first, `Hull` for the others in this step). Included in clone, checksum, `ValidateInvariants` (a module must touch another module face to face, exactly one core, none outside world limits).
- New commands: `PlaceModule(int Mx, int My, int Mz)` and `RemoveModule(int Mx, int My, int Mz)`. First module becomes the core. Later ones must be face-adjacent to an existing module and within the ship's build range of the module centre. Cost in items from the hold, growing with the module count (content `core.json`: `modules.core_cost`, `modules.hull_cost`, `modules.growth_percent`). Removing is refused if any machine or belt sits in it or if it would disconnect the station, and refunds half the cost.
- With `StationRules`, `PlaceMachine` and `PlaceSegment` are rejected with a new `Reject.OutsideStation` unless every cell they cover lies in a module. Existing machines are not moved by a module removal (removal is refused instead).
- With `StationRules`, `Drill` machines are not placeable (they need a deposit, which lies outside any station): a new reject `Reject.NotAllowedHere`. The `Unloader` (cargo port) is unlocked from the start and has no research prerequisite in this mode, so ore flows hold, belt, smelter. Everything else (smelter, assembler, lab, depot, turret, wall, repair bay) works inside the station.
- Hand mining stays 2 ore a second; the hold cap and refinery are unchanged. Content gets a starting stock rule: none; the core cost must be reachable by hand-refined ingots in a few minutes (see Risks).
- Tests: placement outside a module rejected, inside accepted, first module is the core, adjacency enforced, range enforced, cost deducted, removal refused with machines, removal refunds, disconnect refused, drill refused, unloader allowed with no research, old rules unchanged when the flag is off, replay and save round trip with modules, checksum changes with a module.

### Game (Godot)
- Modules are drawn as a translucent floor plate and edge frame (cell grid visible inside), lit by the sky; the core has a distinct marker.
- A new build palette entry "Module": the ghost snaps to the module grid face-adjacent to the existing station and goes red when invalid (not adjacent, too far, too costly). First placement shows "Core".
- Normal building shows a ghost that goes red outside a module, with the reason in the target panel.
- Guided steps in the campaign become: fly, mine, refine, build the core, build a cargo port, build a smelter, deliver ingots to the hold through a depot... (final list is the builder's call in `ObjectiveTracker`, with tests); the guide text explains hand-mining delivery through the cargo port.
- HUD: the station appears as a marker; the status shows module count.

### Not decided here
- Interior spaces, walking, docking animation, power and hydrogen, mining ships, module kinds other than Core and Hull.

## What is left to the builder
- The exact module dimensions if 8 x 8 x 4 proves awkward (keep a single constant), costs and growth values, ghost visuals, wording of guided steps.
- How `StationRules` is chosen in the game (campaign default, sandbox off, a flag for tests).

## Risks
- Pacing: hand-mined ore only, with no drills, makes the first hour slower than the 12:28 first wave of the scripted player; the first wave and the core cost need re-tuning, possibly delaying threats until the core exists.
- Balance numbers for the old rules no longer describe the campaign; a bot script for the station rules is needed (follow-up).
- The cargo port only moves what is in the hold, and the hold holds 60; long sessions of flying back and forth could feel tedious until mining ships exist (project 4).
