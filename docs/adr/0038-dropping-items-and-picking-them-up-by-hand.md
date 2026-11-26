# 0038. Items can be dropped onto the ground and picked up by hand; log version 19

- Status: proposed
- Date: 2026-11-25
- Area: simulation, client
- Design: [inventory prototype](../prototypes/inventory.md#dropping-and-picking-up-adr-0038)
- Builds on: [ADR 0035](0035-personal-inventory-container-moves-and-reactor-fuel-slots.md) (inventory) and [ADR 0036](0036-free-fire-recoil-hold-overflow-and-resource-density.md) (ore pickups)

## Context
The bag and the hold only exchanged items with machines and stocks; nothing could be left in the world or taken from it on foot. ADR 0036 already has world item entities (item, count, position, timer, tractor range) for hold overflow.

## Decision
- **Same entity list.** A dropped stack is an ore pickup entity with a `Dropped` flag and `Source = -1`: it does not drift, no tractor pulls it, it lies 30 minutes (`FeelRules.DropLifeTicks`) and then fades. The cap of 64 entities is shared; stacks of one item within one cell merge.
- **DropItems (tag 60)**: `(container, slot, count, x, y, z)`, from the person or the hold; zero count means the whole slot. The sim does not know where a player stands on foot, so the client sends the point; the sim checks it is within 40 cells of the ship (`OutOfRange`), `NotEnoughItems`, `NoRoom` (64 stacks), `NotAllowedHere` with `LegacyInventory` or `LegacyFeel`.
- **PickUp (tag 61)**: `(entity, seated, x, y, z)`: on foot the stack must be within 3 cells of the given point (itself within 40 cells of the ship), seated within the tractor range (6 cells) of the ship's nearest module. Items go to the bag, then the hold; the rest stays; nothing fitting is `NoRoom`. Ore pickups can be taken by hand too.
- **No new legacy flag.** Both commands need a world without `LegacyInventory`, which every log older than 18 already sets. Log version 19 only makes the reader refuse the commands in logs of versions 5 to 18. Dropped stacks are hashed only when present.
- **Client.** Dragging a stack out of the inventory window drops it (in front of the player, ahead of the ship when seated); `F` on an aimed stack picks it up (lowest in the interact order: it competes with machines by being the nearest thing under the crosshair); stacks are small crates tinted by item, the tooltip gives name and count (es/en), toasts say what moved.

## Consequences
- Benefits: items can be parked and moved without a machine; no new entity type, hash or save format beyond two commands.
- Costs: the position comes from the client, so a tampered client could drop or reach within the 40-cell bound; stacks live in world space, so a ship that flies off leaves them behind; no confirmation for large drops (a drop is undone by picking up).
- Checked by: `DropTests` (round trip, validation, merge, reach, bag then hold, the cap, expiry, checksum, codec, version gate, legacy), `--drop-shot`.
- Not decided here: storage crates, stacks that ride with a ship, a drop count prompt.

## Alternatives considered
- **A. Reuse the pickup list with a flag (chosen).** Least code, one hash block.
- **B. A new entity type with a ship-frame position.** Rejected for now: needs a structure id and frame maths for little gain.
- **C. A legacy flag bit.** Rejected: `LegacyInventory` already gates it and the log version gates old logs.
