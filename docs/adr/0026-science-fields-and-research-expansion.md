# 0026. Science fields and the research expansion (and harder Normal waves)

- Status: proposed
- Date: 2026-11-22
- Area: simulation, content, gameplay

## Context
Two owner decisions. First, Normal was too easy: the structure bot wins with eight turrets and loses nothing before wave 10 ([defence](../prototypes/defence.md)). Second, the research tree ended after 22 nodes that cost only plates and a few ores; the owner wants a deeper tree that is researched in one specific building fed by belt, with research items of different fields: engineering, biology and physics. Every research cost must stay deliverable by belt (ADR 0022), legacy worlds and their golden checksums must not move, and the default bot path (win the campaign) must not depend on the new tier.

## Decision
### Harder waves
- A new enemy `core:bulwark` (600 hp, speed 8, 14 damage every 30 ticks, range 5 cells). `EnemyDef` has no armour value, so "armoured" is hit points and slowness only.
- The wave table keeps its first five entries and gains five: from wave 3 two more scouts and one more striker, from wave 7 a striker entry that grows by one a wave, and a bulwark from wave 4 (one) and a second one from wave 8. Waves 3 to 10 are 25 to 40 percent larger than before (waves 0 to 2 are unchanged). `WorldRules` presets scale on top, unchanged.

### Science
- **Four items** appended after the fuel cell: `core:science_engineering`, `core:science_biology`, `core:science_physics`, `core:biomass`.
- **Three machines** appended (crafters, placeable in workshop and core modules, free in the build menu, costs in ingots and plates only): `core:engineering_bench` (power 15), `core:physics_lab` (18), `core:greenhouse` (12). `core:research_lab` stays the only building that researches and takes science and plates by belt (`TryFeedLab` takes any item of the current node's cost, so no simulation change was needed for feeding).
- **Five recipes**, none research-gated except the last: `make_engineering_science` (bench, 2 iron plate + 1 circuit, 6 s), `make_physics_science` (physics lab, 1 silicon + 1 nickel plate, 8 s), `grow_biomass` (greenhouse, 2 ice + 1 carbon, 10 s), `make_biology_science` (greenhouse, 1 biomass + 1 iron plate, 6 s), `biomass_to_hydrogen` (hydrogen cracker, 1 biomass -> 2 hydrogen, 6 s; unlocked by `closed_loop`). The science chains need no unlock: the ingredients (circuits, silicon, nickel plates) are already gated by the electronics branch, and the new nodes require it.
- **Fifteen research nodes**, five per field, each costing 10 to 30 science of its field plus 100 to 200 iron plates; one optional key `field` (`basic` default, `engineering`, `biology`, `physics`) is read by `ContentDb` and exposed on `ResearchDef.Field` and `ResearchEntry.Field`. The 22 existing nodes keep their ids and costs. The graph and numbers are in [research.md](../prototypes/research.md).
- **New bonus kinds** appended to `BonusKind`: `module_hp`, `hold_capacity`, `fuel_burn`, `sensor_range`, `drone_rate`, `shield_absorb` (percentage points), `power_output`, `jump_discount` (items, not percent). Each is read in one place (`ModuleMaxHp`, `HoldCapacityOf`, `BurnInterval`, `SensorRangeCells`, `DroneInterval`, `ShieldPercentFor`, `StepPower`, `JumpCost`). `JumpCost()` is now the discounted cost and `SimWorld`, `CanAffordJump`, the tips and the dump logic use it.
- **Research panel** logic: `ResearchView.BuildByField` groups the nodes (basic first, then the three fields), `FieldName`, `ScienceColour` (amber, green, blue) for the cost lines, and Loc keys for fields, bonuses and the new unlock texts. The game panel (`game/`) is another change.
- **Compatibility.** Items, machines, recipes and research are part of the hashed state and content, so the golden checksum test strips all of them (and the new enemy and wave entries) from its content. The legacy balance-bot tests run against `TestContent.CoreClassicWaves`, the content with the old wave table.

## Consequences
- Benefits: a tier of play after the basic tree with real production chains (circuits and silicon, nickel plates, ice and carbon), with bonuses that matter for defence, power, drones and exploration; Normal asks for six turrets and the ship cannon by wave 9.
- Costs: the tier needs far more ore than the home system holds (the scripted player needs rich deposits and one expedition per batch); the closed loop makes biomass a better hydrogen source than ice at the price of carbon; the forecast reach of quantum sensors is not modelled (they extend the sensor reach only).
- How it is checked: `ScienceFieldsTests` (recipes, tree shape, bonuses, panel grouping, strings, three chains run end to end with raw ore only), `StructureEconomyTests.Every_science_item_has_a_chain`, the existing deliverability and completability analyses, `WaveBalanceTests`, `StructureBotTests` (Normal win, science mode).
- Not decided here: models and icons for the three machines and the science items (an existing model is reused), a research panel grouped by field in the game, balance of the science tier against a human player.

## Alternatives considered
- Gating the three machines behind research: rejected, the first node of a field would then need a plate-only node just to unlock its machine; the ingredients already gate the chains.
- One generic science item: rejected by the owner (three fields).
- A new `armour` value on enemies: rejected as too much for one enemy; hit points and speed give the same feel.
