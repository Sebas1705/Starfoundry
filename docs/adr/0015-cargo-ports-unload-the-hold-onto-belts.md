# 0015. Cargo ports unload the hold onto belts

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, gameplay

## Context
- Depots move items from belts into the owner's hold, wherever the ship is. Nothing moved items the other way, so what a remote deposit produced could only fund construction. A defence's ammo and a factory's ingots come from belts fed by a deposit in the same system, so when the home deposit ran out the turrets starved, whatever the player had mined elsewhere.
- The [expansion](../prototypes/expansion.md) scenario showed it: a player who expands has remote lines delivering ingots and plates to the hold, and the home ammo lines still go dry.

## Decision
- A new machine role `unloader` with an `unloader` block in the content: `item`, `interval_ticks` and `reserve`. Every interval, if its owner's hold holds more than the reserve of that item and an output belt can take it, it moves one item from the hold to the belt. It accepts no input belt (`WrongRole`) and may feed up to two output belts, sharing them fairly.
- The item leaves the hold like a payment (`HoldOut`) and enters the belts like a source's (`Spawned`), so both the hold accounting and the item conservation check hold. It works whether or not threats are on, in every mode.
- The shipped `core:unloader` (the cargo port): 80 hit points, 12 ingots and 4 plates, signature 1, one ingot every 30 ticks (two a second), reserve 10. The reserve keeps the player able to build, and sits below what depots deliver (24 of an item), so a hold fed by a depot can keep it running.
- It is unlocked by the jump drive research, since moving a hold's contents across systems is what that technology is for.
- The scripted player (`--port`) replaces the drill and smelter of every ammo line with a cargo port after it comes back from the expansion.
- The log keeps format version 5; the content hash changes.

## Consequences
- Benefits: remote mining can now feed a factory far from the deposit; the one-way hold becomes a bus the player has to manage; the reserve prevents locking oneself out.
- Costs: a port drains the hold while the remote supply is slower than its draw (one a second against what a depot delivers), so a poorly supplied port leaves the hold at its reserve and construction waits for ingots.
- In the scripted runs (three fed turrets, four walls, two repair bays, four upgrades) the base saw 34 and 35 waves with ports against 30 without, the first machine lost still in wave 30 or 31: the supply stops being the limit, the strength of the waves is.
- Only ingots are carried; plates and ammo have no port yet.
- Not decided here: a port for other items, a port that takes from a chosen player's hold, a cap on the total unloaded per minute.

## Alternatives considered
- **Belts across systems:** the world has no notion of a belt leaving a system.
- **Letting depots deliver straight to a machine:** a depot that is also a factory input blurs the two roles.
- **No reserve:** simpler, but a port would empty the hold and the player could not build until the remote supply caught up.
