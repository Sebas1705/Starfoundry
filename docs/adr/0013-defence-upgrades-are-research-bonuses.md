# 0013. Defence upgrades are research bonuses

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, gameplay

## Context
- Defence so far is built: turrets, walls, repair bays, each with fixed numbers. The research tree has nothing that makes the defence itself better, and the [research spec](../specs/05-research-progression.md) wants upgrades that improve what the player already has.
- Research already costs plates delivered by belt to a lab and unlocks machines, recipes, belts and abilities; it has no way to change a number.

## Decision
- A research node may list `bonuses`: each has a `kind` (`turret_damage`, `turret_rate`, `machine_hp`, `repair_heal`, `ship_hull`, `ship_damage`) and a `percent`. A node with only bonuses is valid. A kind appears once per node; `turret_rate` is at most 75, the others at most 1000. Bonuses of finished research add up as percentages and are part of the content hash.
- Effects, all integer: turret damage per shot `damage * (100 + p) / 100`; turret interval `interval * (100 - p) / 100` (never below one tick); maximum hit points of every machine `hp * (100 + p) / 100`; hit points per repairer use `heal * (100 + p) / 100`. The ship's maximum hull is `hull * (100 + p) / 100` and its weapon deals `damage * (100 + p) / 100` per shot. A bonus is read from the network (`BonusPercent`, `MaxHp`, `TurretDamage`, `TurretInterval`, `RepairHeal`), never from the content directly.
- When the hit point bonus grows, every machine standing keeps its share of the new maximum (a full machine stays full, a half-dead one stays half), so finishing the research neither heals nor hurts. New machines start at the higher maximum. The ship does the same with its hull (a destroyed ship, at zero, stays at zero and is rebuilt at the new maximum). Repair, repairers and the invariant check use the effective maximum.
- Six shipped nodes, all plates only: turret calibration (150, needs Logistics, +25 percent damage), reinforced plating (200, needs Logistics, +50 percent machine hit points), rapid loader (250, needs calibration, 25 percent shorter interval), field repair (250, needs plating, +100 percent repair), ship armour (200, needs reinforced plating, +50 percent hull) and ship cannon (250, needs calibration, +50 percent weapon damage).
- The interface shows the active percentages in the status panel and the research names in both languages. The scripted player of ADR 0009 researches them in that order once Logistics is done (`--upgrades=N`, up to six; `--path=ship` takes the ship nodes before the loader and field repair).
- Targets pinned by tests (one fed turret, seeds 20261120 and 1): the four upgrades hold at least two waves longer than none; on a full defence (four walls, two repair bays, no manual repair) at least six waves longer; they arrive one after another in the order above, the first within 5 to 20 minutes of Logistics and all four inside the first two hours.

## Consequences
- Benefits: a reason to keep a lab and a plate line running after Logistics; upgrades multiply what the player built, so they matter most to a player who invested in the defence; all numbers are data.
- Costs: the lab and the building plates compete for one plate line, so the scripted player builds a second plate output; percentages on integers lose a little to rounding for small values.
- A single turret gains only 2 to 4 waves from all four upgrades; the field repair and plating bonuses do little without walls and bays.
- The command log keeps format version 5; the content hash changes.
- The ship upgrades are weak in the scripted runs, whose ship sits at the base and fires its weapon; a person who flies and fights with the ship should gain more.
- Not decided here: carbon in upgrade costs, bonuses to mining, belts or the jump drive. (Repeatable upgrades came later, see [ADR 0014](0014-repeatable-research-with-rising-cost.md).)

## Alternatives considered
- **Upgrade machines that replace the old ones:** needs a notion of tiers and migration of what stands.
- **Flat bonuses instead of percentages:** do not scale with the content; each new machine would need its own numbers.
- **Applying the hit point bonus only to machines built afterwards:** punishes the player who built first.
