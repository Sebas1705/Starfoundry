# Performance baseline (2026-11-25)

Measured on the owner's PC (Ryzen 9 7950X class), simulation only, no rendering. The run is a StructureBot Normal campaign, seed 20261121, won at 47.6 game minutes (85,680 ticks at 30 ticks per second, 2,302 logged commands). A throwaway console program referenced the Sim, Ui and Balance projects, ran the bot, wrote a save with `SaveGame.Write` and loaded it back with `SaveGame.Read` (a full replay). Numbers are wall-clock and do not include Godot.

| Measurement | Release | Debug (what `dotnet build game -c Debug` and the editor run) |
| --- | --- | --- |
| Bot playing the whole 47.6 minutes (with bot logic) | 3.7 s (22.9k ticks/s) | 8.2 s (10.5k ticks/s) |
| Loading a save of that game (full replay) | 1.35 s (63k ticks/s, three runs within 2 percent) | 7.1 to 7.2 s (12k ticks/s) |
| Replay to 25 percent (11.9 min) | 0.19 s | 1.06 s |
| Replay to 50 percent (23.8 min) | 0.51 s | 2.69 s |
| Save file size | 54.6 KB | same |
| Memory allocated while replaying | about 938 MB (about 11 KB per tick) | about 931 MB |
| Peak working set | 77 MB | 76 MB |

## Reading
- **Loading is linear in game length but each tick costs more as the factory grows**: the first 25 percent replays at about 111k ticks per second and the whole game at 63k (Release). A 2 to 3 hour game with a large factory could take several seconds in Release and 20 seconds or more in Debug (an extrapolation, not measured).
- **A tick costs about 16 microseconds on average in Release**, far below the 33 ms budget of a 30 Hz tick, so the live simulation is not a performance risk at this size.
- **Memory allocation is the one thing to watch**: about 11 KB of garbage per tick (330 KB per second of play). It is harmless today; pooling in the hottest paths would remove GC pressure if frame hitches ever show up.
- **Save slots add almost nothing to a load** ([saves](saves.md), 2026-11-28, Debug, headless): the slot container (checksum included) packs a 600 KB save in 0.5 ms and verifies it in 0.3 ms, and writing a small save takes about 3 ms; the replay above is still the whole cost of a load.
- **Saves are tiny** (command log only), which stays true for long games.

## Not measured (needs the game running)
Cost of rebuilding the models of a large station after a structure change, frame time with a big station, startup time to the main menu, and the texture build (about 0.5 s the first time, from an earlier agent run). The `--stress` flags measured about 5 ms per frame at 1000 stress objects on an RTX 4080 SUPER.

## If load time ever becomes a problem
The world already supports cloning. Save a checkpoint (a serialised world or a clone) every N game minutes next to the log and replay only from the last checkpoint. Not needed yet: 1.35 s for a 48-minute game in Release.
