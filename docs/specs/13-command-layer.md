# 13 Command layer

Status: implemented for belts (prototype P1.5); other systems will add commands the same way. Decision: [ADR 0003](../adr/0003-commands-as-the-only-way-to-change-the-world.md).

## Flow
```mermaid
sequenceDiagram
  participant UI as UI / blueprint / network
  participant W as SimWorld
  participant N as BeltNetwork
  UI->>W: Submit(player, command)
  Note over W: queued, nothing changes yet
  W->>W: Step(): drain queue in order
  W->>W: validate against world rules
  W->>N: apply, or reject with a reason
  W->>W: append to command log (tick, player, command)
  W->>N: Network.Step()
  UI->>W: LastResults (ok, reason, created ids)
```

## Commands (belts)
| Command | Effect | Main rejections |
| --- | --- | --- |
| `PlaceSegment(x, y, z, dir, length, tier)` | New straight belt, 1 to 128 cells | InvalidArgument, OutOfBounds (coordinates within 10,000), Overlap, TooManySegments |
| `RemoveSegment(id)` | Removes the belt, destroys its items, detaches neighbours, frees its cells | UnknownSegment |
| `ConnectSegments(from, to)` | End of `from` feeds start of `to`; the first cell of `to` must touch the last cell of `from` by a face (ahead or to a side) | UnknownSegment, SelfConnection, FromIsSink, AlreadyConnected, OutputsFull (2), NotAdjacent |
| `DisconnectSegments(from, to)` | Removes the connection | UnknownSegment, NotConnected |
| `SetSource(id, itemTypes)` / `ClearSource(id)` | Feeds the belt start with a repeating pattern of item types (1 to 64) | UnknownSegment, InvalidArgument |
| `SetSink(id)` / `ClearSink(id)` | Consumes items arriving at the end | UnknownSegment, SinkHasOutputs |
| `Batch(commands)` | Atomic group, no nesting; negative ids (-1, -2, ...) refer to the first, second, ... segment placed earlier in the batch | any of the above, plus NestedBatch; `FailedIndex` says which command failed |

## Rules
- Commands apply at the start of the next tick, in submission order. Two commands for the same cell resolve by that order.
- A rejected command changes nothing. A rejected batch restores the world exactly, including id counters.
- Ids are stable and never reused. A removed segment leaves a hole (`IsAlive` is false).
- Items on a removed segment are destroyed and counted in `Destroyed`. Conservation: `Spawned = Consumed + Destroyed + InTransit`.
- Malformed commands (nulls, oversized, nested more than 4 levels in the encoding) throw at `Submit`, because they could never be logged. Game-rule problems are never exceptions.

## Log, codec and replay
- `CommandCodec` encodes commands and logs: little-endian, magic `SFCL`, version byte, bounded sizes, strict decoding (unknown tags, truncation, bad order and trailing bytes are errors).
- `SimWorld.Replay(log, ticks)` rebuilds a world from an empty one; the result has the same checksum as the original. Replaying a prefix reproduces intermediate states.
- The checksum covers geometry, wiring, items and counters, so it doubles as the co-op desync check.

## Validation tooling
- `SimWorld.ValidateInvariants()` checks: wiring symmetry (outputs and inputs), no overlapping cells, cell index matches segments, item spacing and ranges, merge and round-robin indices, conservation.
- A fuzz test fires random valid and invalid commands (including batches with references) for 2,500 ticks on five seeds, validates invariants every tick, then replays the log both directly and through the binary format and compares checksums.

## Cost
Atomic batches snapshot the network. Measured on the benchmark scenario: 0.13 ms median for a tick that applies a batch at 10,000 items, 0.38 ms at 100,000 (first call up to 5 ms from JIT). Plain ticks are unaffected.

## Not done yet
- Permissions per player (host settings) and per-build-volume ownership: commands already carry the player id.
- Commands for machines, ships and research. Each new gameplay action adds a command, a codec tag, reject reasons and tests.
- Undo and redo, blueprint capture and placement (they build on `Batch`).
- Network transport and tick agreement ([07-multiplayer](07-multiplayer.md)).
- A targeted undo journal to replace the batch snapshot if batches become frequent or worlds much larger.
