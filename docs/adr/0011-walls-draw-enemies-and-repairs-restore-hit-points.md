# 0011. Walls draw enemies, repairs restore hit points

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, gameplay

## Context
- After the [wave ramp](0010-waves-can-start-later-and-the-ramp-is-measured-per-turret.md) the only answer to bigger waves was more turrets, each with its own ammo line.
- Enemies attack the machine with the highest signature, which is usually the lab or a smelter with 100 to 120 hit points. A late wave kills it in about a second once it is in range, and a destroyed machine is lost for good.
- Machine hit points never came back, so any damage was permanent until the machine fell.

## Decision
- **Wall.** A new machine role `wall`: no recipe, no belts (belt connections are refused with `WrongRole`), signature 0 so it never calls waves, 400 hit points, 12 ingots and 4 plates. In its system, enemies attack the nearest wall before any other machine (ties go to the lowest id); with no wall they keep going for the loudest machine. A ship within 15 cells is still attacked first.
- **Repair.** A new command `Repair(machine)` restores a damaged machine to full hit points. In campaign mode the ship must be within building range and the hold pays half the build cost scaled by the share of hit points missing, at least one of each item; in sandbox it is free. Refused with `NotDamaged`, `OutOfRange`, `NotEnoughItems` or `UnknownMachine`. It works during a fight, so cost is the only limit.
- A destroyed wall is counted in `WallsLost`, not in `MachinesLost`: walls are built to be lost.
- The scripted player of [ADR 0009](0009-pacing-targets-measured-by-a-scripted-player-and-a-per-item-depot-cap.md) can place four walls around the base and repair never, between waves or all the time. Targets, pinned by tests (one fed turret, seeds 20261120 and 1): four walls hold at least 3 waves longer than none even without repairs; four walls with repairs during the fights hold at least 8 waves longer than none and at least 2 longer than the same walls never repaired.
- The command log keeps format version 5: `Repair` is a new tag, and old logs decode as before.

## Consequences
- Benefits: a second way to defend that uses plates and attention instead of an ammo line; hit points stop being a one-way street; the decoy rule is simple to explain and to test.
- Costs: walls do not block movement, they only draw fire, so placement matters only through the turrets' reach and the enemies' approach side; a player who repairs mid-fight is much stronger than one who does not (21 to 28 waves against 15 to 19 in the scripted runs), by design but worth watching.
- A wall near the turrets can pull a whole wave towards them; one far from the turrets does nothing but delay.
- Not decided here: shields, walls that block enemies, repairing the ship hull, wall variants. (Automatic repair was added in [ADR 0012](0012-repair-bays-heal-slowly-from-a-belt-fed-item.md).)

## Alternatives considered
- **Walls as pure hit-point sponges around every machine:** needs a notion of cover and line of fire the simulation does not have.
- **Slow automatic repair:** removes the reason to come back to the base and gives no use for items.
- **Repair at a cost of the full build cost:** a full price per fix makes keeping a wall up cost more than rebuilding it.
