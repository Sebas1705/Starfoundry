# 05 Research and progression

Status: proposal. Reference states: produced items feed research; unlocks add ships, station modules, items and upgrades; research lets you travel farther and reach more valuable resources.

> **Implementation status:** a first version exists ([playable loop](../prototypes/playable-loop.md)): a research graph in `content/core.json`, labs that consume the current node's items from belts, progress kept per node, and unlocks of belt tiers and recipes. It uses plates as the only datum and three placeholder nodes; the tier and datum design below is still the target.

## Model
Research is a graph of **nodes**. Each node has a cost expressed in **datums**: produced items delivered to a Research Lab. Nodes unlock recipes, parts, buildings, ship frames and upgrades.

## Tiers
| Tier | Theme | Gate to next tier |
| --- | --- | --- |
| 1 Salvage | Hand mining, belts, basic power | Build first station anchor and automate ingots |
| 2 Industry | Assemblers, fluids, silicon, hauler ships | Run a multi-stop supply route |
| 3 Expansion | Heat management, jump drive, frigates | Survive and clear the first Remnant wave |
| 4 Dominion | Remnant tech, capital ships, system-spanning logistics | Build a capital ship and unlock the capstone |

## Datum types
Each tier adds a datum type, produced by a dedicated chain (Salvage datum, Industry datum, Expansion datum, Dominion datum). Higher nodes cost mixed datums, forcing parallel production.

## Nodes (illustrative sample)
| Node | Tier | Cost | Unlocks |
| --- | --- | --- | --- |
| Basic belts | 1 | 20 Salvage | Belt Mk1, splitter |
| Fabricator | 1 | 40 Salvage | Printer machine |
| Fluid handling | 2 | 80 Industry | Pipes, tanks |
| Hauler hull | 2 | 120 Industry | Hauler frame, supply routes |
| Radiators | 3 | 100 Expansion | Heat rejection panels |
| Jump drive | 3 | 250 Expansion | Inter-system lanes |
| Remnant analysis | 4 | 400 Dominion | Remnant alloy recipes |

Full tree in data files (see [10-data-model](10-data-model.md)); content is tuned by simulation, not by hand.

## Upgrades
Repeatable upgrades improve mining speed, belt tier, ship thrust and shield capacity with diminishing returns and a clear cost curve (cost x 1.35 per level, TBD).

## Progression pacing targets
| Milestone | Time target |
| --- | --- |
| Tier 1 done | 2 hours |
| Tier 2 done | 8 hours |
| First jump to another system | 12 hours |
| Tier 3 done | 20 hours |
| Capstone | 30 to 40 hours |

## Co-op research
Research is shared across the world; any player's labs feed the same progress. Player-specific unlocks are limited to ship customisation.

## Open questions
Tree shape (linear tiers vs branches), whether datums can be traded with NPCs. See [12-open-questions](12-open-questions.md).
