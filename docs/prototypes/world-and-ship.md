# Prototype P4: the ship in the world

Status: done (2026-11-20). Builds on the [playable loop](playable-loop.md). Decision: [ADR 0006](../adr/0006-ship-movement-as-intent-commands-and-ranges.md) (proposed).

## What it adds
- A **ship per player** with an integer position (240 units per cell) that starts at the origin and flies with `SetMovement` intents: 6 cells per second per axis, slower per axis when combining axes, and a world limit.
- **Deposits have positions.** Hand mining needs the ship within 6 cells of the deposit (it pauses when the ship leaves and resumes on return). In campaign mode, building and removing needs the ship within 24 cells.
- Position, movement intent and deposit positions are in the state checksum, the invariants and the command log, so replays reproduce flights.
- Godot (`Play.tscn`): WASD flies along the world axes, Space and C go up and down, the camera follows the ship. A white box is the ship, with a blue disc for the build range and an orange disc for the mining range. The build ghost turns red out of range, the HUD shows the distance to the selected deposit, and refusals say "too far from the ship".
- The demo bot now flies to the iron deposit before mining, and tries one far-away placement to prove the range rule.

## Results
- 149 tests (12 new world tests): speed and axis combinations, the world limit, independent players, mining at exactly 6 cells and just beyond, pause and resume when leaving range, build range for belts (both ends), machines and removal, atomic batches that fail on range and refund, replays and checksums including position. The random campaign storm now steers ships around, so it also exercises range rejections.
- `--demo --speed=60`: the ship starts at the origin, flies to 6.0 cells from the iron deposit, and the full loop still finishes (Logistics researched in 2.9 simulated minutes). The far placement was refused with `OutOfRange`.

## Known limitations
- No collisions, inertia or fuel; movement is along world axes only.
- Deposits are a fixed starter field and the world is empty space around the build area.
- Controls were verified through the bot and screenshots, not by a human at the keyboard.
- Mining range is checked against the deposit centre; large deposits do not have a radius yet.

## Run it
```powershell
dotnet test
dotnet build game -c Debug
```
Then open `game/` in Godot 4.7.2 (Mono) and press F5, or run the scripted loop with `-- --demo --speed=60 "--shot=path.png"` after the project path.
