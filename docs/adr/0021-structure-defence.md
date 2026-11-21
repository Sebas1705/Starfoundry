# 0021. Structure defence: waves hit modules, modules have hit points

- Status: proposed
- Date: 2026-11-21
- Area: simulation, gameplay

## Context
- Waves ([ADR 0008](0008-remnant-threat.md) style) used to attack machines one by one and the player's ship hull. With modular structures ([ADR 0018](0018-modular-structures.md)) the thing worth defending is the structure: its modules, the machines inside them, and for the ship the cockpit that lets the player fly.
- The [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md) (part 4) asks that enemies pick a module, that destroyed modules take their contents with them, that a station dies with its core and that losing the ship is a defeat.
- The simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md)). Worlds without the structure rules, and structure 0, must keep their behaviour and checksum.

## Decision
- **Module hit points.** Under the structure rules every module has hit points, maximum from content `structures.<kind>.hp` (cockpit 150, core 300, workshop 200, defence 400, and so on). The structure keeps only the damaged modules (a map from module to current hit points), so an undamaged structure hashes exactly as before. `BeltNetwork.ModuleHp(structure, mx, my, mz)`, `ModuleMaxHp(kind)` and `StructureHpFraction(structure)` (per mille) read it.
- **Targeting.** An enemy in a structure world attacks, in this order: (1) the nearest wall in its system (walls stay decoys, in any structure); (2) the module of the player's ship nearest to it when that module centre is within 15 cells; (3) otherwise the nearest module (by distance between the enemy and the module centre) of any structure in its system that holds at least one machine or is a ship. It flies to its attack range of the module centre and hits it with its usual damage and interval. A bare station is not a target. `EnemyInfo` carries `TargetStructure` and `TargetMx/My/Mz`.
- **Machines are shielded.** A hit on a module does not hurt the machines inside it; they are lost when the module is. Only walls take hits as machines.
- **Destruction.** At 0 the module is removed with the existing bookkeeping: machines inside are destroyed (counted in `MachinesLost`, walls in `WallsLost`), every belt segment with a cell inside is removed (items on it are lost and counted as destroyed), links to removed machines are cut, the module leaves the structure, and the command layer frees the cells through `TakeDestroyedMachines` and `TakeDestroyedSegments`. Modules left disconnected stay. If the module is the core of a station, the whole station goes (`StationsLost`). If it is the last cockpit of the player's ship, the ship is lost: `ShipLost(player)`, thrust stops, flight is refused, there is no respawn (the game shows a defeat screen). Legacy ship death and respawn are unchanged.
- **Repair.** A powered repair bay heals the first damaged module (in module order) of its structure within its range by its `repair_heal`, using one ingot, when it has no damaged machine to mend. Every module also regenerates 1 hit point every `threat.module_regen_seconds` (10 s) while its structure has satisfaction 1000.
- **Defence modules.** They have more hit points and each raises the turret damage of its structure by `structures.defence.defence_bonus_percent` (10), up to 50 percent in total (`DefenceBonusPercent(structure)`).
- **First wave.** In structure worlds the first warning of a system waits until the system has spent `threat.first_wave_grace_seconds` (480, 8 minutes) above the signature threshold. The alert meter fills as before. Legacy worlds have no grace.
- **Events.** `ModuleHits` lists the hits of the last tick (`ModuleHitEvent`: module, damage, hit points left, destroyed, station lost, ship lost) for effects and toasts; `ModulesLost`, `StationsLost`, `ShipsLost` count. `HitModule` is a setup call for tests and demos.
- **State and checks.** Module damage, the ship lost flag, the system grace counters and the new counters are cloned, validated and hashed only when set, so legacy and undamaged worlds hash as before (golden checksums in the tests). The content hash mixes the new fields only when they differ from their defaults.

## Consequences
- Benefits: a base can be lost piece by piece, which makes defence modules, turrets, walls and repair bays matter; contents are shielded so one stray hit does not delete a machine; the model adds a handful of integers.
- Costs: the module hit points are not repaired by the old hand repair command (that heals machines); a module that falls takes machines and belts with it, so spreading machines over modules limits a loss; turrets only cover modules within their range.
- Pacing: measured numbers are in [docs/prototypes/defence.md](../prototypes/defence.md).
- How it is checked: `StructureDefenceTests` (targeting, destruction, occupancy, station and ship loss, repair, regeneration, walls, turrets, bonus, grace, golden checksums, replay and save round trips).
- Not decided here: enemy types that outrange turrets, shields, rebuilding a lost station, boarding.

## Alternatives considered
- **A. Damage machines directly.** Rejected: a wave would delete the machine with the highest signature, so nothing but walls could soften it, and a structure would never have a shape to defend.
- **B. Hit points per module (chosen).** Enemies pick the nearest module, contents are lost only when it falls, and every module kind can have its own toughness and role (cockpit is fragile and decisive, defence is sturdy).
- **C. One pool of hit points per structure.** Rejected: it hides which part is lost and makes the cockpit rule and the core rule arbitrary.
