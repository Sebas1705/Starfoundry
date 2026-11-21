# Prototype: power and fuel

Status: built (2026-11-21), simulation, content and Ui logic only. Decision: [ADR 0019](../adr/0019-power-and-fuel.md). Roadmap: part 2 of the [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md).

## What was built
| Area | Change |
| --- | --- |
| Content | `core:hydrogen` item; `core:hydrogen_cracker` (crafter, 20 iron plate + 10 iron ingot, 12 power); recipe `core:crack_ice` (2 ice to 1 hydrogen, 4 s); `core:ice_unloader` (ice port, 2 power, no research gate); `power_use` on machines; `structures.reactor.power_output` 100 and `burn_interval_ticks` 900; the starter ship template has `hold`: 20 hydrogen |
| Simulation | A power step at the start of every tick (structure worlds, structures other than 0): reactors burn hydrogen from the owner's hold, satisfaction per mille per structure, a work credit per machine |
| What it scales | Crafting (advance and start only on credited ticks), labs, depots, unloaders, repair bays (credited ticks), engines (speed times satisfaction), turrets (fire at 500 per mille or more) |
| Accessors | `BeltNetwork.PowerStatus(structure)`, `BeltNetwork.ShipPower(player)`: `PowerStatus(Production, Demand, SatisfactionPermille, BurnRemainingTicks, HydrogenInHold, Burning)` |
| Ui logic | Tips `learn.noFuel` and `learn.lowPower`; objective `MakeFuel` right after `FoundStation` (structures mode only); es and en strings |

## Power use per machine
Smelter 10, assembler 15, hydrogen cracker 12, lab 20, depot 1, every unloader and ore port 2, turret 5 (constant), repair bay 10, wall 0, drill and extractor 0. The starter ship's demand is 28 against 100 produced.

## Rules worth remembering
- A reactor takes one hydrogen when its burn ends and it has none running, so one hydrogen is exactly 900 ticks of 100 power.
- Satisfaction is 1000 when production covers demand. With a reactor and nothing burning it is 0, even with no machines, so a bare ship without fuel drifts. A structure with no reactor and no demand stays at 1000.
- Machines in a structure without any reactor have no power (demand above 0, production 0): a station needs its own reactor module and the owner's hydrogen.
- Only structures under the structure rules are affected; structure 0 and legacy worlds are unchanged.

## Measured numbers
Measured by the tests in `PowerTests.cs` and `HydrogenTests.cs` (30 ticks a second):
- Starter ship fuel: 20 hydrogen x 900 ticks = 18000 ticks. Satisfaction drops below 1000 at tick 18001 (10 minutes), then to 0 (28 demand, no production), and the ship stops accelerating.
- Hand-mined ice to hydrogen: the home ice deposit has density 42 percent in the test seed, so hand mining takes 35 ticks per ice. From the start of mining to the first hydrogen delivered to the hold: 283 ticks (9.4 s) with the ice port, cracker and depot built beside the starter factory.
- Six minutes of hand mining ice with that loop made 17 hydrogen while the reactor burned 13 (24 left of the 20 at the start): the loop out-produces the reactor, so one miner keeps the starter ship alive indefinitely.
- At half satisfaction (demand twice the production) a smelter makes half the items in the same time (18 to 20 crafts at full power against half of that, within one craft, over 1200 ticks), and a ship covers half the distance.
- Pacing tests of the structure worlds pass unchanged: the 20 hydrogen only take 20 hold slots, hand mining stops at 45 items instead of lower.

## Limits
- Reactors are all the same size; no batteries and no per-structure tank.
- The HUD and the game do not show power yet (the Ui logic and accessors are ready).
- The cracker needs an ice port from the hold: ice reaches the ship only by hand mining until drones come (roadmap part 3).
