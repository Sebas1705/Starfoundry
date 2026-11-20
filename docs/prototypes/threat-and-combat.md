# Prototype P6: the Remnant threat and combat

Status: done (2026-11-20). Builds on the [galaxy](galaxy-exploration.md) and the [playable loop](playable-loop.md). Decision: [ADR 0008](../adr/0008-remnant-threat-as-signature-driven-waves.md) (proposed).

## What it adds
- **Signature and alert**: machines carry a signature (smelter and assembler 3, lab 4, drills and the free extractor 2, depot 1, turret 0). A system's signature above the threshold (8) fills its alert meter by the excess every tick; at or below it the meter decays. A full meter schedules a wave with a 30 second warning.
- **Waves and enemies**: a wave spawns about 70 cells from the system anchor at a seed-derived position. Two enemy types (scout, striker) in `content/core.json`; wave size is `base + per_wave * waveNumber` per type (scouts 2, 3, 4 ...; strikers 0, 1, 2 ...), capped at 24 alive. Enemies attack a ship within 15 cells, otherwise the loudest machine in the system.
- **Damage and loss**: machines have hit points and are destroyed at zero (contents lost, belts detached, cell freed). A ship at zero hull is rebuilt at the system anchor after 5 seconds and keeps its hold.
- **Turrets** (`core:turret`, 10 ingots and 4 plates): a machine role that takes `core:ammo` from belts (ammo comes from a new assembler recipe, 1 ingot for 4 ammo), holds up to 20, and shoots the nearest enemy within 18 cells every 15 ticks. Anything else on its belt jams it.
- **Ship weapon**: `SetWeapons` (key `V`) switches it on; it fires at the nearest enemy within 25 cells.
- **Opt in**: threats are a world creation setting (`SimWorld.CreateGalaxy(content, mode, seed, threats: true)`), stored in the command-log header (format version 5). The playable scene turns them on; the production and exploration demos keep them off.
- **Godot**: enemies as boxes, beams for turret, ship and enemy shots, damaged machines turn red, turrets dim when out of ammo, a THREAT line in the HUD (signature, alert or wave countdown, hull, weapons, kills and losses) and toasts for warnings, arrivals and losses. `--threat-demo` builds a loud base with two ammo-fed turrets and watches a forced wave.

## Results
- 258 tests (50 new): signature sums per system, alert rise and decay by exact amounts, the warning countdown and spawn, wave growth (2, 4, 6 enemies) and the cap, waves not stacking, enemies destroying the loudest machine first and freeing its cell, contents lost, ships attacked and rebuilt at the anchor with the hold kept, turrets filling with ammo and jamming on other items, fed turrets protecting the base, empty or out-of-range turrets doing nothing, the fire interval, the ship weapon, command codec and header v5 round trips, replays that diverge if the starting world is wrong, content validation for turrets, threat, enemies and waves, and a random combat storm on five seeds (sandbox and campaign, invariants every tick including the cell index, replay through the binary format).
- A test found a real bug: with integer movement an enemy could stop one unit short of its range and never attack. Attack range now has a few units of tolerance.
- `--threat-demo`: a forced wave of two scouts is killed by the turrets after 6 shots, no machine lost, invariants valid. The production and exploration demos behave as before.

## Known limitations
- All numbers are placeholders; there was no balance pass and no difficulty setting.
- Two enemy types and one behaviour; no fleet, no orders, no assault on Remnant nodes, no stealth or quiet-play modules.
- Enemies pick targets by scanning all machines; a large base needs a spatial index.
- Only the first system with a base is exercised in the demo; waves in several systems at once are covered by the storm tests, not by a demo.
- Verified through the demo bot and screenshots, not by a human at the keyboard.

## Run it
```powershell
dotnet test
dotnet build game -c Debug
```
Then open `game/` in Godot 4.7.2 (Mono) and press F5. From the command line, after the project path: `-- --threat-demo --speed=3 "--shot=fight.png"`.
