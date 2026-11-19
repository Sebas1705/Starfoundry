# 0005. Campaign mode: build costs paid from the hold, research gates content

- Status: proposed
- Date: 2026-11-19
- Area: simulation, gameplay

## Context
- The core loop in [01-core-loop](../specs/01-core-loop.md) starts with a lone ship: mine by hand, refine on board, build a station, automate, research.
- Until now everything was free, which suits tests and experiments but not a game.
- Co-op needs costs and unlocks to be rules of the simulation, not UI conventions, so they stay in sync and replay identically ([ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)).

## Decision
- Two game modes, recorded in the state checksum and the command-log header. **Sandbox** keeps free building and nothing locked (tests, demos, debug machines such as the free extractor and belt sources). **Campaign** enforces everything below.
- Each player has a **hold** with a fixed capacity (60 items). Hand mining (2 ore per second) and the ship's **micro-refinery** (a recipe on a ship-only machine type, half speed) work on the hold. Hand mining pauses when the hold is full.
- **Building costs items** taken from the commanding player's hold (machines have a cost, belts cost per cell) and is refunded in full when the player removes the thing. A failed atomic batch refunds exactly what it paid.
- **Depots** (a machine role) deliver every item that reaches them into the hold of the player who built them, while there is room; otherwise the belt backs up. They close the loop between automation and building.
- **Drills** take ore from a linked **deposit** (finite, shared) instead of creating it. The free extractor stays sandbox-only.
- **Research** is a graph in the content files. Labs consume the items the current node needs, delivered by belt; items a lab does not need jam the belt. Progress is kept per node. Anything unlocked by some node (machines, recipes, belt tiers) is locked until that node is finished; everything else is available from the start.
- Accounting is checked by invariants: hold items in minus out equals items held; deposit extraction equals hand mining plus drilling; machine and belt conservation include depots and labs as consumers.

## Consequences
- Benefits: a complete first loop that can be played, scripted by a bot and replayed; costs, locks and rollbacks are one rule set tested together; content stays data.
- Costs: more state in the simulation (players, deposits, research), a larger snapshot per atomic batch, and a hold cap that can trap a careless player (ore filling the hold blocks depot deliveries until something is spent). The UI must make that visible.
- Not decided here: balance numbers (all placeholders), hold upgrades, where deposits come from (a fixed starter field for now), per-player research, shared team storage.

## Alternatives considered
- **Costs enforced only by the UI:** simpler, but not authoritative and not replayable.
- **A separate item system for the ship:** rejected, the same item indices and recipes serve belts, machines and the hold.
- **Unlimited storage:** removes the pressure to spend and automate; the cap can be raised later by research.
