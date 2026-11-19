# Prototype P5: galaxy and exploration

Status: done (2026-11-20). Builds on the [ship in the world](world-and-ship.md). Decision: [ADR 0007](../adr/0007-galaxy-as-regions-of-one-space-with-deterministic-generation.md) (proposed).

## What it adds
- **A generated galaxy**: 12 systems (home plus 11) laid out from a 64-bit seed with a deterministic generator (SplitMix64, integers only). Archetypes (dense belt, sparse belt, carbon rich, and home) come from `content/core.json` and decide each system's deposits. Lanes form a connected graph with at least two lanes per system.
- **Discovery**: only home's deposits start revealed. `Scan` (`K`) reveals hidden deposits within 80 cells of the ship, with a 5 second cooldown. Hidden deposits cannot be mined or linked. Systems become known through lanes of visited systems.
- **Jumps**: `Jump` (`J`) along a lane from inside a system to a known system, a 3 second charge during which the ship is frozen, arrival at the anchor. In campaign mode it needs the new **Jump Drive** research (an *ability* unlocked by a research node) and costs 5 carbon. Flying into a system by hand also visits it.
- **Drill reach**: a drill links only to deposits within 16 cells.
- **Command log header v4** carries the galaxy seed; `SimWorld.CreateGalaxy(content, mode, seed)` creates the same world everywhere.
- **Godot**: star map panel (`G`) with lanes, visited and known systems, the current system and a selection (`Up`/`Down`), a list with distances, scan and jump messages, a system line in the HUD, hidden deposits not drawn. `--explore-demo` runs a sandbox bot that scans and jumps lane by lane until seven systems are visited.

## Results
- 208 tests (59 new): SplitMix64 reference values pinned, same seed same galaxy and different seeds differ, home at the origin with the starter field, systems inside the world and spread out, lanes connected, symmetric and with the wanted degree over 300 seeds, deposits within their archetype's ranges, archetype frequencies follow the weights; scanning range and cooldown, hidden deposits refused, jumps (charge, frozen ship, arrival at the anchor, visits and known neighbours, lanes only, no jump from deep space), campaign gating and cost, refused jumps costing nothing, drill reach, replays; content validation for the galaxy, archetypes, abilities and jump cost; and a random exploration storm on five seeds, sandbox and campaign, with invariants every tick and replay through the binary format.
- `--explore-demo`: seven of twelve systems visited in 30 simulated seconds, 7 scans, 6 jumps, 27 of 47 deposits revealed, invariants valid. The campaign demo bot still researches Logistics in 2.9 simulated minutes in the generated world.

## Known limitations
- No trade, NPCs, wrecks, anomalies or hazards, and archetypes only differ by deposits.
- Hauling between systems is manual: there are no cargo ships or cross-system supply routes yet.
- The galaxy is bounded by the world cube (about 64 systems at the current spacing).
- Names are syllable noise; there is no star type or solar power yet.
- The map and jump controls were verified through the demo bot and screenshots, not by a human at the keyboard.

## Run it
```powershell
dotnet test
dotnet build game -c Debug
```
Then open `game/` in Godot 4.7.2 (Mono) and press F5. From the command line, after the project path: `-- --explore-demo --speed=30 "--shot=map.png"` for the exploration bot, or `-- --demo --speed=60` for the production loop.
