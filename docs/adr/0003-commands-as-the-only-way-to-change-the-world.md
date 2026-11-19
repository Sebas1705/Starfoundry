# 0003. All world changes go through validated, logged commands

- Status: accepted
- Date: 2026-11-19
- Area: simulation

## Context
- Co-op ([07-multiplayer](../specs/07-multiplayer.md)) is host-authoritative and sends commands, not state.
- Blueprints, undo and replays all need a description of "what the player asked for" that is separate from "what changed".
- The simulation is deterministic ([ADR 0002](0002-deterministic-simulation-core.md)): the same inputs must give the same state, so inputs must be explicit, ordered and serialisable.
- Retrofitting this after machines, ships and a UI call the network directly would touch everything.

## Decision
Gameplay and UI code change the world only by submitting commands to `SimWorld`. A command is a plain record (place, remove, connect, disconnect, set or clear source or sink, and an atomic batch). `SimWorld` queues commands, applies them in submission order at the start of the next tick, validates each against the world rules, and records every command with its tick in a log. A rejected command changes nothing and returns a reason. A batch is all-or-nothing and can refer to segments it creates (negative ids). Segment ids are stable and never reused. Commands have a versioned binary encoding, and a log replays to an identical state (checked by checksum).

`BeltNetwork` remains a public low-level API for tests and scenarios, but game code must not mutate it directly.

## Consequences
- Benefits: the same path serves single player, co-op, replays, tests and blueprints; rule checks live in one place; a fuzz test replays random command storms and compares checksums.
- Costs: every new gameplay action needs a command, a codec tag, a rejection reason and tests. An atomic batch clones the network for rollback, which costs O(world) per batch (0.13 ms median at 10,000 items, 0.38 ms at 100,000); a targeted undo journal replaces it if batches become frequent.
- Checked by: unit tests and the fuzz and replay test in `CommandTests`, plus the benchmark's batch timing in CI.

## Alternatives considered
- **Call the network directly and sync state:** simplest now, but co-op would need full or diffed state sync, and there would be no replays.
- **Event sourcing of state changes instead of requests:** records outcomes, not intent, so it cannot be validated or re-applied against a different world.
- **Per-command rollback instead of a snapshot for batches:** cheaper per batch but every command needs an inverse, which is error-prone. Deferred until measurements justify it.
