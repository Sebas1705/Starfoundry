# Prototype P2: data-driven machines

Status: done (2026-11-19). Builds on the [belt prototype](belts.md) and the [command layer](../specs/13-command-layer.md). Decision: [ADR 0004](../adr/0004-data-driven-content-and-one-cell-machines.md) (proposed).

## What it adds
- `content/core.json`: 4 items, 3 machine types (extractor, smelter, assembler) and 4 recipes, loaded by `ContentDb` with strict validation and a content hash.
- Machines in the simulation: one cell, input and output buffers, one recipe, crafting timer, linked to belts by adjacency.
- Six machine commands (place, remove, set recipe, connect to, connect from, disconnect), usable inside atomic batches with references to machines placed earlier in the batch.
- Command logs carry the content hash (log format version 2).
- Godot: a three-machine chain (iron mine, smelter, plate assembler) is built at startup by one batch; machines are boxes that dim when idle. Run with `--view=chain` to frame it.

## The demo chain
Extractor (1 iron ore per second) feeds a belt into a smelter (1 ingot every 2 seconds), a belt, then an assembler (2 ingots make 1 plate in 2 seconds), then a belt into a sink. The smelter is the bottleneck, so the line settles at 0.25 plates per second.

## Results
- `MachineTests`: a chain built in one batch delivers 28 to 32 plates in 120 seconds after a 60-second ramp (expected 30). An extractor yields 19 to 21 items in 20 seconds. A backed-up output stops the machine with two crafts of buffer and nothing lost. A wrong item jams the belt instead of disappearing.
- Godot run with the full scene (10,000 belt items plus the chain): 13 plates delivered in the first 60 seconds of simulated time, 92 items crafted, frame time unchanged (about 0.9 ms average).
- Tests: 87 in total, including a random command storm with machines on five seeds (3,000 ticks each, invariants checked every tick, replay through the binary format compared by checksum).

## Known limitations
- Machines are one cell; no footprints, rotation, typed ports, power or heat yet.
- Several belts feeding one machine are served in segment order, not fairly (belt merges are fair, machine inputs are not).
- A belt feeding a machine cannot also feed belts, and a belt fed by a machine cannot also be merged into; a splitter in front of a machine needs another approach.
- A machine feeds at most two belts and puts at most one item per belt per tick.
- Rendering offsets (machines drawn half a cell along +X) are only right for belts running along +X.

## Run it
```powershell
dotnet test
dotnet build game -c Debug
```
The chain demo is in the benchmark scene `game/Main.tscn` (the project's main scene is now the [playable loop](playable-loop.md)). Run it from the command line with `godot --path game res://Main.tscn -- --bench --commands --view=chain "--shot=path.png"`.
