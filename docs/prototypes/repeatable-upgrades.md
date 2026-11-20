# Prototype P14: repeatable upgrades

Status: mechanism done, balance not proven, awaiting a human play-test (2026-11-20). Decision: [ADR 0014](../adr/0014-repeatable-research-with-rising-cost.md) (proposed). Builds on [defence upgrades](defense-upgrades.md).

## What it adds
| Area | Change |
| --- | --- |
| Content format | A research node may be `repeatable`: `max_levels` and `cost_growth_percent`; part of the content hash |
| Simulation | Research state is a level per node; each level costs the previous one times the growth, rounded up; a bonus applies per level; progress restarts when a level completes and more remain; `ResearchLevel`, `ResearchCost`, `GrantResearch(node, levels)`; checksum and invariants cover levels |
| Research | Three nodes, plates only, 35 percent growth: **overclock** (+8 percent turret damage per level, 8 levels, needs calibration), **alloy refinement** (+10 percent machine hit points per level, 8 levels, needs plating), **nanite repair** (+25 percent repair per level, 6 levels, needs field repair); the first level of each costs 300 plates |
| Game | The research name shows its level (being researched or reached), costs shown per level |
| Balance simulator | The upgrade order now continues with the three repeatable nodes (up to nine upgrades); `--extra-plates` builds a second plate line for building when the lab keeps the first; `--grant=id,id` starts with research finished, for what-if comparisons |

Level costs of a 300-plate node at 35 percent: 300, 405, 547, 739, 998, 1,348, 1,820, 2,457 plates.

## What the scripted player showed
- **The lab can starve building.** The assembler fills its first output belt first. A lab that is always researching takes almost every plate of its line, and a second output to a depot receives nothing: with that layout the hold held one plate for the whole run and the first wall never got built. A player who researches continuously needs one plate line for the lab and another for building (`--extra-plates`).
- **More upgrades did not mean a longer defence in these runs.** First machine lost, one fed turret, four walls, two repair bays, dedicated plate line, seeds 20261120 and 1: four upgrades 25 and 26; six upgrades 22 and 21; seven or nine upgrades the same as six, because the repeatable nodes were never reached: the lab needs about a quarter of an hour per upgrade and the base fell around wave 22, 140 minutes in. Giving the same nodes at the start does not lower the result (the ship nodes, calibration and plating all gave 28 to 30 on the default seed), so the drop comes from when and how they are researched, not from the bonuses. I could not isolate the cause.
- **With free plates the defence never fell** (no first loss in 400 minutes with either four or six upgrades), so plates are the limiting resource of this defence, and research competes with building for them.

## Targets pinned by tests
None for balance: the repeatable nodes are placeholders until the long game can be measured. Unit tests cover content loading, validation and hash, the level costs (growth and rounding up), the bonus per level, the last level reporting its cost, a lab researching level after level with the cost rising and progress restarting, bonuses surviving a rolled-back batch, and determinism. 332 tests.

## Known limitations
- Nobody has played it by hand and the shipped nodes are unbalanced: their value is unmeasured and the first level arrives late or not at all in the scripted runs.
- No carbon in costs, no cap on the total bonus, no research queue.
- The unexplained drop between four and six upgrades is open: it needs a closer look at what the scripted player does between waves while the lab is busy.
