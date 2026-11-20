# 0012. Repair bays heal slowly from a belt-fed item

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, gameplay

## Context
- [ADR 0011](0011-walls-draw-enemies-and-repairs-restore-hit-points.md) added walls and a manual `Repair` command. Repairing a wall while it is being shot is worth a lot (21 to 28 waves held against 15 to 19 for never repairing), but it asks for constant attention and the player must be in building range.
- Between waves nothing repairs a damaged wall or machine: the scripted player that repaired only between waves gained almost nothing over not repairing, because the real cost of damage is the walls that fall and must be rebuilt.
- Turrets already show the pattern for a machine that works on what a belt delivers: a role, a definition in the content, a buffer capped per item, a cooldown.

## Decision
- A new machine role `repairer` with a `repairer` block in the content: `range_cells`, `heal` (hit points per use), `interval_ticks`, `item` and `item_cap`. It takes the item from a belt like a turret takes ammo, produces nothing and accepts no output belt (`WrongRole`).
- Every interval, a repairer with the item in its buffer heals the damaged machine in range with the lowest share of hit points left (lowest id on ties; walls and the bay itself count) and uses one item. It only runs while threats are active, since nothing is damaged otherwise. Hit points restored are counted in `AutoRepaired`.
- The shipped `core:repair_bay`: 120 hit points, 14 ingots and 6 plates, signature 1, range 14 cells, 20 hit points per ingot, one use every 30 ticks, buffer of 20 ingots. That is about 20 hit points a second at most and 400 hit points from a full buffer, small next to the damage of a late wave (hundreds a second) and large between waves.
- The scripted player builds bays on a second output of its first two smelters, so they share the ingots with the depots; the target, pinned by a test, is that two bays cut the walls lost over the first 15 waves by a quarter or more with no manual repair at all, while still losing some.
- The manual `Repair` stays the emergency tool and is not replaced.

## Consequences
- Benefits: upkeep without attention, paid for with the logistics the game is about (an ingot line to the bay); walls last longer so fewer plates go into rebuilding them; the repairer reuses the turret machinery.
- Costs: the bay competes with the depot for ingots on a shared smelter; a bay does not change the wave at which a base is first overrun (the bays did not move that number), only how much it costs to get there.
- Results per seed: walls lost in 100 minutes went from 10 to 5, 13 to 7 and 12 to 8 with two bays (default seed, 1 and 2).
- The command log keeps format version 5; the content hash changes.
- Not decided here: bay variants (bigger range, faster), repairing the ship hull, repair that uses plates, repair drones that fly to the damage.

## Alternatives considered
- **Passive regeneration of every machine:** removes the reason to return to the base and gives items no use.
- **A repair aura without items:** no logistics link, and no limit on how much it can heal.
- **A much faster bay:** it would make manual repair and walls redundant; 20 hit points a second keeps the emergency repair worth pressing.
