# 0014. Repeatable research with rising cost

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, gameplay

## Context
- [ADR 0013](0013-defence-upgrades-are-research-bonuses.md) added research bonuses, but each node can be researched once, so the upgrades run out after about an hour and a half of play.
- The [research spec](../specs/05-research-progression.md) wants repeatable upgrades with diminishing returns and a clear cost curve (about 1.35 times per level).
- Research state was a done flag per node.

## Decision
- A research node may have `repeatable` with `max_levels` (2 to 50) and `cost_growth_percent` (1 to 200). It needs bonuses to repeat. Research state becomes a level per node: 0 not done, 1 done, more for repeatable nodes. Requirements, unlocks and abilities count from level 1.
- Each level costs the previous level's cost times `(100 + growth) / 100`, rounded up, item by item (`ResearchCost`). The first level costs the base cost. A finished node reports the cost of its last level.
- A bonus applies once per level: a node with 8 percent turret damage at level 3 gives 24 percent. Diminishing returns come from the cost, not from smaller steps.
- When a level completes and more remain, the node's progress starts again from nothing and the node can be chosen again; the lab, the belts and the research choice work as before.
- The checksum and the invariant check cover levels; the content hash covers the new fields; the command log keeps format version 5.
- Three shipped nodes, plates only, 35 percent growth: overclock (needs turret calibration, +8 percent turret damage per level, 8 levels, 300 plates for the first), alloy refinement (needs reinforced plating, +10 percent machine hit points per level, 8 levels) and nanite repair (needs field repair, +25 percent repair per level, 6 levels).
- `GrantResearch(node, levels)` sets a level for setup and tests.
- The interface shows the level being researched or reached next to the name.

## Consequences
- Benefits: a long-term sink for plates that makes every upgrade bonus keep growing; the cost curve is data; the change is mostly a flag becoming a number.
- Costs: the cost of the later levels is large (the eighth level of overclock costs about 2,450 plates); the scripted player's single plate line takes about an hour for the first level.
- Findings that matter for balance: researching continuously keeps a plate line running, which drains the home deposit (4,000 ore) in under two hours; a first draft also showed that a machine with two outputs gave everything to one belt (fixed, see [fair outputs and the home ore](../prototypes/fair-outputs-and-ore.md)).
- Not measured: whether the shipped nodes help the defence. In the scripted runs they arrive after the first loss or not at all, so they are placeholders until a human play-test or a longer-lived scripted defence says otherwise.
- Not decided here: carbon in upgrade costs, repeatable upgrades for the ship, a cap on the total bonus, research queues.

## Alternatives considered
- **Separate "upgrade level" state outside research:** duplicates requirements, costs and the lab feed.
- **Smaller steps per level:** hides the cost curve in the bonus and makes the numbers harder to read.
- **Unlimited levels:** needs a cap on cost growth to stay inside the integer range and gives no sense of an end.
