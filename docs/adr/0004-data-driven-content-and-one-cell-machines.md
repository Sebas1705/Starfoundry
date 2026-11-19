# 0004. Data-driven content and one-cell buffered machines

- Status: proposed
- Date: 2026-11-19
- Area: simulation, data

## Context
- [10-data-model](../specs/10-data-model.md) wants items, machines and recipes in data files so balance and mods do not need code changes.
- Machines are the next gameplay layer on top of belts and commands ([ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)).
- Replays and co-op need to know they run the same content, or states silently diverge.
- The prototype needs a machine model small enough to finish and test, while keeping the door open for footprints, ports and power later.

## Decision
- Content lives in `content/*.json` (items, machine types, recipes). A strict loader validates it (schema version, unknown properties, duplicate ids, dangling references, bounds) and converts recipe times from seconds to whole ticks once at load. Simulation code uses indices; files use `pack:name` ids.
- A content hash covers everything that affects the simulation. It is part of the checksum and is stored in command-log headers; a replay against different content is detected.
- Machines are one-cell nodes with an input buffer and an output buffer per item and one active recipe. A craft starts when all inputs are present and the outputs fit (two crafts of room); inputs are taken at the start, outputs appear when the time is up.
- Belts link to machines by adjacency: a belt end feeds a machine, and a machine feeds the start of up to two belts. A belt that feeds a machine cannot also feed belts or be a sink; a belt fed by a machine cannot have a source or input belts. This avoids merge arbitration with machines for now.
- An item a machine cannot use jams the belt (head-of-line blocking), as in comparable games. Changing a recipe or removing a machine destroys what is inside and counts it as destroyed, so conservation holds: spawned plus made equals consumed plus destroyed plus used plus in transit plus held.

## Consequences
- Benefits: balance changes need no code; mismatched content is caught; machine behaviour is fully testable headless (chain throughput, jams, rollback, fuzz and replay).
- Costs: every new machine capability needs schema, loader checks and tests. One-cell machines and exclusive links are a simplification: footprints, rotations, multiple typed ports, power, heat and fair arbitration between several belts feeding one machine are not modelled yet.
- Checked by: `ContentTests`, `MachineTests` (including a random command storm with machines, replayed through the binary format) and the Godot chain demo.

## Alternatives considered
- **Hard-code machine types in C#:** fastest now, but every balance change becomes a code change and mods are impossible.
- **Full footprints and typed ports now:** closer to the final game, but it multiplies placement, rotation and rendering work before the factory loop is proven fun.
- **Machines as belt segments with a processing flag:** reuses code but mixes two concepts and makes buffers and recipes awkward.
