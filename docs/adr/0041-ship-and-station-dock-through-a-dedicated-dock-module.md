# 0041. Ship and station each carry a dock module, and dock only through it; log version 22

- Status: proposed
- Date: 2026-11-26
- Area: simulation, content, client
- Builds on: [ADR 0029](0029-docking-ship-and-station-are-different.md) (docking), [ADR 0032](0032-weapons-as-hardpoint-modules-and-the-shipyard.md) (the designer)
- Supersedes in part: ADR 0029 alternative C ("ships always dock with a dedicated dock module"), rejected there for balance reasons and chosen now by the owner. ADR 0029 itself stays as written; the flag below keeps its rules for old logs.
- Version 20 and ADR 0039 belong to the blueprint editor; 21 and 0040 are the rock field; this change takes 22 and 0041.

## Context
Since ADR 0029 a port was a free horizontal face of a station's core, of a ship's cockpit flanks or of a `dock` module, so a ship could dock with no dock module at all. The owner wants the docking hardware to be a thing on both sides: the ship carries a dock module and the station carries one, and a ship docks by joining its dock module to the station's.

## Decision
- **Only dock modules are ports.** With the new rule a port is a free horizontal face of a `dock` module, on a ship or a station (`BeltNetwork.ModuleHasPorts`). The core and the cockpit stop being ports. Everything else of ADR 0029 (range, speed, alignment, the pose, the stock, repair, defence, walking across) is unchanged, because it reads the ports through `DockPorts`.
- **Ships have one by default.** The starter ship, the scenario ships and every pre-built structure get a dock module when the world is built (`BeltNetwork.EnsureDockModule`): a ship's on the -X flank of its cockpit, where its old docking port was; a station's on the -Z side of its core (then +Z, -X, +X), so that its two X faces stay free ports. The content files do not change (so the content hash and the golden checksums do not move); the module is added in code at world creation, only when the content defines the `dock` kind.
- **A founded station comes with one.** `FoundStation` creates the core and a dock module beside it, at no extra cost (the dock module is part of the core's package; more docks are bought as ADR 0029 says). It takes the first side whose volume is clear (-Z, +Z, -X, +X); if no side is clear the founding is refused (`Overlap` or `OverlapsDeposit`).
- **They cannot be lost by design.** A ship or station keeps at least one dock module:
  - removing the last one is refused (`Reject.LastDockModule`, in the module removal);
  - a design (`DesignerApply`) without one is refused (`Reject.NoDockModule`); the old design editor refuses the removal (`DesignEditResult.LastDock`) and the blueprint editor lists a blocking error (`bp.val.noDock`) and cannot apply. A ship's docked port keeps its own rule (the same module, the face free). Spanish and English texts exist for all of them.
  - A module destroyed in combat is not a design: a structure that loses its last dock simply cannot dock until it builds one.
- **Compatibility.**
  - `WorldRules.LegacyDock` (bit 5, value 32, of the second flags byte; off in every preset and in the new-game screen) keeps the ADR 0029 ports (core, cockpit flanks, dock module), adds no dock module to any template or founded station and lifts the two refusals. Logs older than version 22 read with it on, so they replay as recorded; it hashes as default rules like the other legacy flags. The log version is 22 and adds no command.
  - Bit 3 (8) of that byte is left free for the blueprint editor's flag.
  - Tests that pin the ADR 0029 geometry (`DockingTests`, the docked inventory tests, the oriented docking tests) run in worlds with the flag on; `DockModuleTests` covers the new rule.
- **Client.** Port markers, guide lights, the assisted approach, the tip and the walk across read the ports from the simulation, so they follow the rule without changes. A station module wears docking collars only if it is a dock module (a core or a bay with collars would read as a port it is not); the dock module model (teal role colour, hazard lanes, collars on a ship's open flanks) is the one readable cue on both sides. The module is called "Enganche" in Spanish and "Dock" in English; its purpose text says that it is the only port.

## Consequences
- Benefits: a clear rule a player can read (the thing with the collars is the port), a ship that docks only if it was built to, a designer that cannot build an undockable ship, and no change to the content hash or old saves.
- Costs: a ship's dock module widens the starter ship by one module on its -X flank (the scripted player's layout moves its fourth cargo spot and its station drone bays to cells the docks do not use). A station's first dock sits beside its core, so the first module a player builds on that side is a second step away. The scripted player now docks through the dock-module pair (`DockAtStation`).
- Balance: the dock module costs nothing at the start (it is part of the starter ship and of the founded station) and adds no mass to the pacing steps measured before; the Normal, Relaxed and Hard win times of the scripted player are in the pull request.
- Not decided here: presets of ship designs in the shipyard (the blueprint editor's phase 1) must include a dock module, which its validation enforces; a second dock on the starter ship so that a ship can also come from the other side of a station; removing the last dock of a station to demolish it whole.

## Alternatives considered
- **A. Ports on the core and the cockpit as well, dock module optional (ADR 0029).** Kept for old logs only.
- **B. Put the dock module in the content templates.** Rejected: it changes the content hash, so every save and golden checksum would move, and a legacy world could not keep the old templates.
- **C. A dock module bought at founding.** Rejected: it moves the first minutes of the campaign and every scripted time; the owner asked for it to be part of founding.
- **D. Refuse a structure's last dock only for the player's ship.** Rejected: a station without a dock cannot be docked at either, so the rule is the same for both.
