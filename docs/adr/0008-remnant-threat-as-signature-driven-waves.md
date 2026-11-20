# 0008. The Remnant threat: signature fills an alert meter, the meter sends waves

- Status: proposed
- Date: 2026-11-20
- Area: simulation, gameplay

## Context
- [06-threats-combat](../specs/06-threats-combat.md) wants threat to follow the player's industrial footprint, give warning, and be answerable by design (turrets, fleet) or by playing quiet, never by random timer spam.
- Combat must obey the rules of the rest of the simulation: deterministic, integer-only, commands-only input, replayable ([ADR 0002](0002-deterministic-simulation-core.md), [ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)).
- Existing worlds, tests and demos have no threat, and adding one must not change them.

## Decision
- **Signature.** Each machine type has a `signature` in the content. A system's signature is the sum over the machines inside it. Turrets and belts are quiet (0).
- **Alert.** Each system has an alert meter. Above a signature threshold it rises every tick by the excess; at or below it, it decays. A full meter schedules a wave after a warning time (30 seconds) and the meter then restarts at a lower level. The meter freezes while a wave is scheduled or enemies are alive in the system, so waves never stack.
- **Waves.** A wave spawns at a node about 70 cells from the system anchor, at a position derived from the galaxy seed, the system and the wave number (no global random state). Its size is data: for each wave line, `base + per_wave * waveNumber` enemies of a type, capped by a global enemy cap.
- **Enemies** have hit points, speed, damage, range and an attack interval, all in the content. Each tick an enemy attacks the nearest ship within 15 cells, otherwise flies to the machine with the highest signature in its system, using integer vectors and an integer square root.
- **Defence.** Machines have hit points and are destroyed (contents lost, belts detached) when they reach zero. Turrets are a machine role fed ammo items by belts; they shoot the nearest enemy in range, one ammo per shot. The ship has a weapon that is switched on with `SetWeapons` and fires at the nearest enemy in range. A ship at zero hull is rebuilt at its system's anchor after a delay and keeps its hold.
- **Opt in.** Threats are a creation setting of the world (with the galaxy seed and the game mode), stored in the command-log header (format version 5) and in the checksum. Without it, or without a galaxy, nothing changes.
- The command layer frees the cells of machines destroyed in a tick right after the tick, so the cell index stays consistent.

## Consequences
- Benefits: threat scales with what the player builds and can be managed (stay quiet, defend, fight); waves, fights and losses replay exactly and are covered by the same invariant and storm tests as everything else; all numbers are data.
- Costs: per-tick scans over machines, enemies and players (fine at current scale; spatial indexing is needed for large bases); a second source of truth for the cell index that must be drained after combat; balance numbers are placeholders.
- Not decided here: fleets and orders, enemy variety beyond two types, assaulting Remnant nodes, quiet play with stealth modules, difficulty settings, power and heat as signature sources.

## Alternatives considered
- **Random timed raids:** simplest, but ignores the player's footprint and makes replays depend on a random stream.
- **Enemies as engine physics objects:** easier visuals, but not deterministic and not replayable.
- **Turrets that need no ammo:** removes the logistics link between factory and defence, which is the point of the design.
