# 0007. The galaxy is a set of regions of one shared space, generated deterministically

- Status: proposed
- Date: 2026-11-20
- Area: simulation, world

## Context
- [04-world-exploration](../specs/04-world-exploration.md) wants procedurally generated systems with their own logistics challenges, scanning, and jumps between systems.
- Belts, machines, ships, ranges and the cell index already work in one integer coordinate space ([ADR 0006](0006-ship-movement-as-intent-commands-and-ranges.md)). Per-system worlds would mean per-system networks, cell indices and snapshots for every command.
- Co-op needs every peer to see the same galaxy, and replays must reproduce it exactly ([ADR 0002](0002-deterministic-simulation-core.md)).

## Decision
- **Systems are regions of the one world space.** Each system has an anchor in cells; the world cube is large enough (10,000 cells per axis) that systems sit hundreds of cells apart. A ship within the system radius (150 cells) of an anchor is "in" that system. Everything built, flown or mined works the same everywhere; no new network, index or snapshot structure.
- **Generation is a pure function of the content and a 64-bit seed**, using SplitMix64 (never `System.Random`) and integer math only. Home sits at the origin; the others sit on a jittered grid around it. Archetypes (data in the content file) are picked by weight and define the deposits of a system (item, count range, amount range, distance range). Lanes always form a connected graph (minimum spanning tree, then nearest neighbours up to the wanted degree). The seed, mode and content hash are stored in the command-log header (format version 4); a replay creates the world the same way.
- **Discovery is shared state of the world.** Deposits are created for the whole galaxy up front, so ids are stable, but only home's start revealed. The scanner (`Scan`, 80 cells, 5 second cooldown) reveals hidden deposits around the ship; hidden deposits cannot be mined or linked. A system becomes known when a visited system has a lane to it, and visited when a ship jumps or flies into it.
- **Jumping** (`Jump(system)`): from inside a system, along a lane to a known system, after a 3 second charge during which the ship is frozen, arriving at the target's anchor. In campaign mode it needs the `jump` ability, unlocked by research (content declares abilities per research node), and costs items (carbon in the starter content). Flying between systems by hand also works and takes minutes.
- **Drills reach only 16 cells**, so a base cannot drain a deposit in another system.

## Consequences
- Benefits: exploration reuses every existing mechanic; the galaxy costs nothing to replay or sync beyond the seed; the content file controls the galaxy's shape.
- Costs: all systems share one coordinate cube, so galaxy size is bounded by the world limit (about 64 systems at the current spacing); deposits of the whole galaxy live in the state; a base far from the rest has no logistics link to other systems beyond manual hauling.
- Not decided here: trade and NPCs, wrecks and anomalies, hauling ships and cross-system supply routes, a larger galaxy (would need a bigger world limit or streaming), hazards that depend on the archetype.

## Alternatives considered
- **One network per system:** closest to the original spec, but it multiplies every structure and complicates commands and batches.
- **Generating systems lazily on arrival:** smaller state, but ids would depend on visit order and replays would need the full visit history.
- **Floating-point noise for placement:** easy, but not reproducible across machines.
