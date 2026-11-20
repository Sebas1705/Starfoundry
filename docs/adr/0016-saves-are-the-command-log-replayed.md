# 0016. Saves are the command log, replayed

- Status: proposed
- Date: 2026-11-20
- Area: simulation, tooling, gameplay

## Context
- The game could not be saved: closing it lost the base. The [technical architecture](../specs/09-technical-architecture.md) wants autosaves, and a played game of an hour or two is not something to throw away.
- The simulation is deterministic and every change to the world goes through a command that is logged ([ADR 0002](0002-deterministic-simulation-core.md), [ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)). A world fed the same log ends in the same state, and replays are already tested.
- Writing out the whole state (every belt, item, machine, deposit, researched level, enemy and counter) would be a second serialiser to keep in step with every new feature, and a second source of bugs.

## Decision
- A save is the world's command log plus the number of ticks played, the checksum at that moment and the player. Loading creates the same starting world (from the mode, seed and threat flag in the log header) and replays the log for that many ticks. The checksum is compared: a save that does not replay to the state it was saved in, was recorded with other content (different content hash), or is corrupt or truncated is refused with a clear message instead of being loaded wrong.
- `SaveGame` (`Write`, `Read`, `Peek`) lives in the simulation library and is tested without the engine. The file starts with the magic `SFSV`, a version, ticks, checksum and player, followed by the existing command log format.
- Only what went through commands is saved. Setup done by calling the network directly (granting research, scheduling waves, as tests and demos do) is not part of a save.
- The game saves to `user://saves`: `F6` writes the quick save, `F9` loads it (the autosave when there is no quick save), and a played game autosaves every five minutes of real time. Demos never save. The interface state (camera, selection, hints) is not saved: after a load, the guided steps and the play log are recomputed from the loaded world, and hints may repeat. A save made in another galaxy is refused.
- `--save-test` plays a few commands, saves, changes the world, loads and prints whether the loaded world is the saved one, to see it work in the real game without pressing keys.

## Consequences
- Benefits: a new feature needs no save code; whatever the replay reproduces is saved; the format is the one that already carries logs; saves are small (about 465 KB for a scripted base of 14,000 commands after 90 minutes).
- Costs: loading replays the whole game: 1.9 seconds for that 90-minute base, growing with game length; the log keeps rejected commands, so a noisy player's file is larger; a content change invalidates old saves (the hash differs), by design, since the replay would diverge.
- A save is not portable across versions that change simulation behaviour: a fix that alters the order of items (like the output sharing fix) makes older saves fail the checksum and be refused.
- Not decided here: snapshots to shorten long loads, several named save slots in the interface, a menu, saving the camera and selection, cloud saves.

## Alternatives considered
- **Serialise the full state:** fast to load, but a second format to maintain for every machine, counter and field, and bugs there would silently lose or corrupt a base.
- **Periodic snapshots plus a log tail:** the right answer when loads get slow; not needed yet at two seconds.
