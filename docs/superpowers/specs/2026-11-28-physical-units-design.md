# Physical units: real measures for the whole game (design)

Status: **decided for the display layer (step 1, built next); step 2 (real physics) is a proposal that waits for the owner's approval** (2026-11-28). Everything marked *measured* was read from the code or the content today; *proposal* is our choice; *TBD* is unknown. Nothing here is copied from another game.

The owner asked that the game use **real simulated measures**: distances in metres, quantities in kilograms or tonnes, power in kilowatts, and so on, instead of "cells", "units per tick" and bare numbers. This document fixes one coherent unit system, shows the mechanical conversion of every existing quantity, separates what is a **display change** (no simulation change, no log version) from what is a **physics change** (flag, log version, balance), and recommends an order: display first, physics later in measured steps.

## 1. Decisions in one table

| # | Decision | Why |
| --- | --- | --- |
| 1 | **1 grid cell = exactly 1 metre.** One simulation unit (`Units.CellUnits` = 240 per cell) is 1/240 m, about 4.2 mm. No scale factor anywhere. | The client already walks a 1.7 m eye, a 1.8 m body and a 0.3 m radius in cells (`BodyMotion`), door frames of 2.7 m and 3.1 m (`ModuleModels`): the game was drawn at 1 cell = 1 m. Any other value would change the body and the doors. |
| 2 | **A module is 8 m x 8 m on the floor and 4 m high** (`StationGrid.ModuleX/Y/Z` = 8, 4, 8). The 40-cell starter ship is 40 m long. | Follows from 1. A 4 m deck-to-deck height is a cargo-deck height; a 2.7 m door fits. |
| 3 | **Time is already real**: 30 ticks per second, recipes in seconds, the clock in `m:ss`. No change. | |
| 4 | **Speeds are m/s**, sim speed in units per tick times 30 / 240 = **units per tick divided by 8**. Boost and the cruise table stay as they are: 6 m/s per engine, 24 m/s at four engines, boost times 4. | The flight numbers must not change gameplay (the owner's rule); with 1 m per cell they simply read as m/s. |
| 5 | **No "interplanetary drive in km/s" in the display layer.** A drive in km/s would need a world that is 1,000 times larger (system spacing is 1,800 m today). It is listed in step 2 as option P9 and is *not* recommended. | Showing 24 cells/s as 24 km/s would be false: distance over speed would no longer be time. |
| 6 | **One linear scale for everything**, including the galaxy: a "system" is a region of space a few hundred metres across around a star marker, and neighbouring systems are 1.8 km apart (*measured*: `world.spacing_cells` 1,800, `system_radius_cells` 320). We do not claim stellar distances. | Consistency (distance / speed = time) beats literal astronomy; two scales would contradict each other in one nav list. |
| 7 | **Mass**: every item kind has a mass per item (a *lot*) and a bulk density, in a client-side table (`content/physical_units.json`, section 5). Every module kind has a mass (section 6), reusing the relative masses of `ship_handling.json` times 25 t. | Real mass of stacks, holds and ships on screen before it affects gameplay. |
| 8 | **Power: 1 power unit = 100 kW** (so the starter reactor is 10 MW, the mining laser 500 kW, a smelter 1 MW, a Mk2 reactor 25 MW). **Energy**: shown in MJ and GJ. The hydrogen item is **2.5 kg**, and one burns for 30 s at 10 MW = **300 MJ**, which is what real hydrogen gives (120 MJ/kg times 2.5 kg). | Reactor and fuel are the one place where the content can be physically exact, and it is (see "Energy closure" in section 5). |
| 9 | **Display rules** (section 7): SI auto-scaling (m, km, Mm; g, kg, t, kt; W, kW, MW, GW), two significant digits below 100 and whole numbers above, locale decimal separator (Spanish comma, English point), no digit grouping, no imperial toggle now. | |
| 10 | The word **"cell" disappears from distances, speeds and ranges** in the interface (es "celdas", en "cells"). It stays only for the grid square a thing is placed on, renamed **tile** (es "casilla"), and for the item *fuel cell*. | The cell is now a metre; saying "80 cells" next to "80 m" would be two names for one thing. |
| 11 | Hit points and damage are **game points, not physical**: they keep their names and numbers. Temperature and pressure **do not exist** in the game and are not introduced. | A thermal or pressure model is a new system, not a unit change. |

## 2. Scale check against real things (measured values, plausibility is a judgement)

| Quantity | Value in metres | Compare with | Verdict |
| --- | --- | --- | --- |
| Eye height, body, door | 1.7 m, 1.8 m, 2.7 m | adult human, door | exact |
| Walk and run speed (`BodyMotion`) | 4 m/s, 7 m/s | a person walks 1.4 m/s, sprints 7 m/s | walking is brisk; **keep** (feel), shown as is |
| Module | 8 x 8 x 4 m | a shipping container is 12 x 2.4 x 2.6 m | fine for a bay or a hold deck |
| Starter ship | 40 m long | a coastal patrol vessel | fine |
| Station build volume | 10,000 cells per axis (`SimWorld.MaxCoord`) = 10 km cube | a large port | fine |
| Hand reach, tractor, pickup radius | 3 m, 6 m, 2 m | | fine |
| Mining laser range | 10 m (Mk1) and 14 m (Mk2); hand mining 6 m | an industrial laser works at metres | fine |
| Docking range and max speed | 12 m, 1.5 m/s | real docking is 0.1 to 0.3 m/s | game-fast, fine |
| Collision "safe speed" | 1.25 m/s | a walking pace bump | fine |
| Top speed | 6 m/s per engine, 24 m/s with four, boost x4 up to 96 m/s | a ship manoeuvring in a port | slow for "space", right for a salvage tug among rocks; **see 4.2** |
| Acceleration to top speed in 1.2 s, braking in 0.6 s | 5 m/s2 = 0.5 g, brake 10 m/s2 = 1 g; at 24 m/s top speed 20 m/s2 = 2 g and 40 m/s2 = 4 g | crew limit about 3 g sustained | **flag**: the shape is a speed governor, not a force, so the g-load of a four-engine ship is high; shown honestly in the Systems tab |
| Rock radius | median 1.3 m x cube root of mass (1.2 to 12 m; cluster rocks x2.5 so 3 to 30 m) | boulders to small asteroids (a real asteroid of note is over 100 m) | *boulder fields*: acceptable, say "rock", never "asteroid" in numbers |
| Scan range, nav range | 80 m (scan), 400 m (nav base), 520 m (long scanner), 700 m (comms antenna) | | short for sensors, right for a ship that mines at 10 m |
| System radius, spacing | 320 m, 1,800 m | real systems are light-hours across | **off scale, by design (decision 6)** |
| Rock field limit | 4,000,000 cells = 4,000 km | | fine |
| Projectile speed | 150 units per tick = 18.75 m/s | a bullet is 800 m/s | **off**: it is a slow bolt; not displayed anywhere; step 2 option P10 |
| Reactor fuel | 2.5 kg H2 per 30 s at 10 MW | 120 MJ/kg x 2.5 kg = 300 MJ = 10 MW x 30 s | **exact** |

## 3. Mapping of every existing quantity

"Factor" converts the number in the simulation or content to the new unit. A unit is 1/240 m; a tick is 1/30 s.

### 3.1 Lengths, positions, ranges (all pure display; factor 1 per cell)

| Name (where) | Today's unit | Meaning | New unit | Factor |
| --- | --- | --- | --- | --- |
| Positions X/Y/Z of ships, enemies, pickups, projectiles | units | world position | m | / 240 |
| Machine, belt, deposit, module coordinates | cells | grid position | m (tile index in the editor) | x 1 |
| `StationGrid.ModuleX/Y/Z` | cells | module size | m | x 1 (8 x 4 x 8) |
| `PlayerRules.MineRangeCells` (6), `BuildRangeCells` (24) | cells | hand mining reach, campaign build reach | m | x 1 |
| `Inventory.ReachCells` (12), `HandReachUnits` (3 cells), `TractorRangeUnits` (6), `CollectRadiusUnits` (2), `DropActorReachUnits` (40) | cells / units | bag, hand, tractor, collect, drop reach | m | x 1 or / 240 |
| `ScanRangeCells` (80), `WideScanRangeCells` (300), `NavBaseRangeCells` (400) | cells | scanner and nav display reach | m | x 1 |
| `DrillRangeCells` (16), `drone_range_cells` (130), turret `range_cells` (18), repairer `range_cells` (14), `hardpoint_range_cells` (10, 14), `sensor_range_cells`, `nav_range_cells`, `salvage.range_cells` (12), `trigger_cells`, `blast_cells` | cells | machine, weapon, sensor and hazard reach | m | x 1 |
| `DockRules.RangeCells` (12) | cells | docking range | m | x 1 |
| `world.spacing_cells` (1,800), `system_radius_cells` (320), `anchor_clear_cells`, `deposit_clear_cells`, `hazard_clear_cells`, `field.sector_cells` (200), `load_cells`, `keep_cells`, `clusters.super_cells` (800), cluster radii | cells | world layout | m (km from 1,000 m) | x 1 |
| `world_limit_cells` (4,000,000) | cells | rock field limit | Mm (4 Mm = 4,000 km) | x 1 |
| Rock `Radius` (`FieldRock.Radius`, `obstacle radius_tenths`) | units / tenths of a cell | rock size | m | / 240 and / 10 |
| Belt length (`LengthCells`) | cells | belt run | m | x 1 |

### 3.2 Speeds and accelerations (pure display; factor 1/8 per tick, 1 per cell/s)

| Name (where) | Today's unit | New unit | Factor |
| --- | --- | --- | --- |
| `PlayerRules.MoveUnitsPerTick` (48) | units/tick | m/s | / 8 (= 6.0 m/s) |
| Diagonal speeds (34, 28) | units/tick | m/s | / 8 (4.25, 3.5) |
| Engine `thrust_speed_percent` (100 per engine, cap 400) | % of 6 m/s | m/s | x 0.06 |
| Boost `boost_percent` (400), cloud `slow_percent` | % | x4, slowed | none |
| Flight HUD, `hud.flightSpeed*` | cells/s | m/s (km/h in the tooltip) | x 1 |
| `accel_ticks` (36), `brake_ticks` (18) | ticks to top speed | m/s2 and g (derived: top speed / seconds) | x 1/1.2 s, x 1/0.6 s per m/s |
| Docking max speed (12 units/tick), collision `safe_speed_units` (10), `reference_speed_units` (48) | units/tick | m/s | / 8 (1.5, 1.25, 6) |
| Debris `drift_units_per_tick` (12), pickup drift (5), tractor step (72), `ProjectileSpeedUnits` (150) | units/tick | m/s | / 8 (1.5, 0.63, 9, 18.75) |
| Enemy `speed` (24, 18, 10, 8) | *TBD unit, read as units/tick* | m/s | / 8 if units per tick (3, 2.25, 1.25, 1): *to verify before displaying*; **not displayed in step 1** |
| Walk and run (`BodyMotion`) | cells/s | m/s | x 1 |
| Turn rate, turn acceleration (`ship_handling.json`) | deg/s, deg/s2 | deg/s, deg/s2 | none (angles stay degrees) |

### 3.3 Mass and cargo

| Name (where) | Today's unit | New unit | Factor |
| --- | --- | --- | --- |
| Item count in a stack, hold, bag, machine buffer, depot | items | items **and** kg / t | x mass per item (section 5) |
| `PlayerRules.HoldCapacity` (60), module `hold_capacity` (cargo 400), research `HoldPercent` | items | items and nominal t (x 25 kg) | x 0.025 t |
| Deposit `Remaining`, rock `Amount` | items | items and kg / t | x mass per item |
| Rock `Mass` (thousandths of the reference rock) | relative | **rock mass in t** = sphere volume x 2,700 kg/m3, with radius = 1.3 m x cube root (mass / 1000) | derived, display only |
| Module mass (`ship_handling.json` `mass`) | relative, core 3.0 | t | x 25 t |
| Ship mass (`ShipHandling.MassOf`) | relative | t | x 25 t |
| `RecoilReferenceMass` (8 modules) | modules | t | 8 x 25 t = 200 t nominal ship (the 7-module reference ship of `ship_handling.json` is 172.5 t) |
| Turret `ammo_cap`, repairer `item_cap`, depot cap (`DepotItemCap` = 24) | items | items and kg | x mass per item |
| Hit points, damage, `shield_percent`, `hp` | points | **unchanged** (game points) | none |

### 3.4 Power, energy, fuel

| Name (where) | Today's unit | New unit | Factor |
| --- | --- | --- | --- |
| `power_output` (reactor 100, Mk2 250), `emergency_power` (15) | power units | kW / MW | x 100 kW (10 MW, 25 MW, 1.5 MW) |
| `power_use` (smelter 10, assembler 15, lab 20, depot 1, turret 5, repairer 10, cracker 12, laser 5 and 8, shield 25, sensor 5 to 18, comms 12, drone bay 20) | power units | kW / MW | x 100 kW (1 MW, 1.5 MW, 2 MW, 100 kW, 500 kW ...) |
| `PlayerRules.LaserPowerUse` (5) | power units | kW | x 100 kW (500 kW) |
| Power satisfaction | per mille | % | / 10 |
| `burn_interval_ticks` (900) | ticks per hydrogen | s and kg/min | 30 s; 2.5 kg per 30 s = 5 kg/min |
| Hydrogen in the hold | items | items, kg and MJ | x 2.5 kg, x 300 MJ (Mk1; **Mk2 gives 750 MJ per item**: same fuel, 2.5 times the output) |
| Fuel cell | items | items, kg, MJ | x 18 kg; x 1,800 MJ in a Mk1 reactor (burns 6 intervals) |
| Machine throughput (recipe `time_s`, per-minute figures in the machine panel) | items/s, items/min | items/min and kg/min (t/h from 1 t/min) | x mass per item |
| Mining rate (`MineTicksPerItem` 15 = 2 items/s) | items/s | items/s and kg/min | x mass per item |

### 3.5 Not physical, unchanged

Time (`s`, `m:ss`), research costs and times, wave timers, hit points, damage, shield and defence percentages, signatures, scores, percentages of any kind, turn rate in degrees. Temperature and pressure are absent.

## 4. Display change or physics change

### 4.1 Pure display (step 1: no `WorldRules` flag, no log version, no content hash change)
Everything in sections 3.1 to 3.4 above. The conversion happens in one engine-free module (`Starfoundry.Ui.Metric` and `Physical`) and a client-side content file (`content/physical_units.json`, read like `ship_handling.json`, never hashed, never read by the simulation). Because nothing in `core.json` or the simulation changes, **logs and saves replay byte for byte, the bot bands are untouched, and no golden checksum moves**. Effort **M** (about 25 call sites and about 80 Loc strings).

### 4.2 Real physics (step 2, one decision and one measured step each; **not built, awaiting approval**)

| Id | Change | Needs | Effort | Legacy replay | Balance impact and bands |
| --- | --- | --- | --- | --- | --- |
| P1 | **Hold capacity by mass (t) and volume (m3)** instead of an item count; the bag too | `WorldRules.LegacyHoldCount` (third flags byte, next free bit), content fields `mass_kg` per item and `hold_capacity_kg` per module (hashed only when present), log version | **L** | flag on for logs up to v29, byte-identical | **High**: hydrogen (2.5 kg) and ammo (5 kg) pack 5 to 10 times denser than ore (30 kg). Mitigation: capacity is 25 kg times the old count, so ore-heavy loads barely change; run the three bands (Relaxed 44-50, Normal 48-53, Hard 51-56 on seeds 20261121 / 20261120 / 777) before and after; expect hydrogen logistics to ease |
| P2 | **Acceleration from thrust and real mass** (F = ma; engines in kN, module masses in content, speed limit kept as a flight-assist governor) | `LegacyMassFlight` flag, content `mass_kg` on every module kind and `thrust_kn` on engines (hashed when present), log version | **M** | on for old logs | Calibrate so the 172 t reference ship with one engine keeps 5 m/s2: starter times do not move; heavier ships accelerate slower (a new constraint). Bands move only if the bot's late ships are heavy: re-measure |
| P3 | **Drag-free Newtonian flight** (no governor: velocity persists, a flight computer is optional) | `LegacyFlightAssist`, new controls and HUD | **L** | on for old logs | Large: every flight system and the bot's pathing; **not recommended** before P2 is lived with |
| P4 | **Fuel for the engines by mass** (propellant, Tsiolkovsky delta-v) | engines burn hydrogen per N s; `LegacyFreeThrust` | **L** | on | Very high: a new logistics loop, bots must refuel; **not recommended** |
| P5 | **Collision damage from kinetic energy** (1/2 m v2 with real masses) | `LegacyCollisionDamage` | **M** | on | Medium: today's damage is a function of speed only |
| P6 | **Recoil from projectile mass and speed** (the `RecoilReferenceMass` rule becomes real) | folded into P2 | **S** after P2 | with P2 | Low |
| P7 | **Mining yield by rock volume and ore fraction** (a rock holds kg of ore from its volume x density) | derived from existing `mass` and `density`; no flag if the numbers are kept | **S** | n/a | None if the formula reproduces `AmountOf` |
| P8 | Gravity wells | **ignored**, decision: no gravity | | | |
| P9 | Strategic scale (a world 1,000 times larger, a drive in km/s) | world regeneration, `WorldRules` scale, new log version | **L** | on | Rewrites the galaxy; **not recommended** |
| P10 | Ballistic projectiles at real speeds (sub-tick stepping, hit-scan) | `LegacyFeel` style flag | **L** | on | Weapons feel rewrite |

### 4.3 Flags, log versions and ADR numbers
Verified on 2026-11-28 against `origin/main` (after #129): log version **28** (machine footprints, ADR 0050) and ADR **0049** (save slots) are merged; **29** (large module spans) is reserved by the multicell work and ADR **0051** with it. The display step takes **no log version** and ADR **0052**. Step 2 would take the next free log version after the reserved ones (**30 at the time of writing**) and the next free flag bit of the third flags byte (bit 5 after `LegacyLargeModules` at bit 4), and an ADR per physics change. Each step re-runs `git fetch` and renumbers on rebase.

## 5. Mass table of items (content, client-side)

A **lot** is the amount one item stands for. Lots were chosen so that **every recipe conserves or loses mass** (output mass at most input mass; the test of step 1 checks it) and so that the typical lot is about 25 kg (the *reference lot* that turns an item count into an approximate mass). Density is the bulk density of the loose material (crushed ore, stacked plates), used for the volume of a stack (`volume = mass / density`).

| Item | kg per item | Density kg/m3 | Volume per item | Reasoning |
| --- | --- | --- | --- | --- |
| iron ore | 30 | 2,500 | 12 L | crushed hematite |
| iron ingot | 20 | 7,850 | 2.5 L | 30 kg of ore gives 20 kg of metal |
| iron plate | 36 | 7,850 | 4.6 L | 2 ingots (40 kg) minus offcuts |
| carbon | 12 | 1,000 | 12 L | graphite and coal |
| ammo | 5 | 7,000 | 0.7 L | 1 ingot (20 kg) makes 4 rounds |
| ice | 25 | 920 | 27 L | water ice |
| titanium ore | 32 | 2,800 | 11 L | ilmenite |
| titanium ingot | 18 | 4,500 | 4 L | |
| titanium plate | 32 | 4,500 | 7 L | 2 ingots (36 kg) |
| nickel ore | 30 | 2,500 | 12 L | |
| nickel ingot | 20 | 8,900 | 2.2 L | |
| nickel plate | 36 | 8,900 | 4 L | 2 ingots (40 kg) |
| quartz | 26 | 1,600 | 16 L | crushed |
| silicon | 20 | 2,330 | 8.6 L | 2 quartz (52 kg) lose their oxygen |
| circuit | 6 | 1,500 | 4 L | boards, from 1 silicon (20) + 1 nickel plate (36) |
| titanium alloy | 30 | 4,500 | 6.7 L | 1 titanium plate (32) + 1 carbon (12) |
| hydrogen | 2.5 | 71 | 35 L | liquid H2; one item burns 30 s at 10 MW |
| fuel cell | 18 | 1,200 | 15 L | 2 hydrogen (5 kg) + 1 nickel plate (36), worth 6 hydrogen; **energy not closed** (a cell holds 3 times the energy of the hydrogen that goes in; fiction, flagged) |
| biomass | 20 | 800 | 25 L | 2 ice (50) + 1 carbon (12) |
| carbon fibre | 8 | 1,600 | 5 L | 3 carbon (36) + 1 nickel ingot (20) make 2 |
| composite plate | 24 | 1,800 | 13 L | 2 fibre (16) + 1 titanium plate (32) |
| coolant | 30 | 1,000 | 30 L | 3 ice (75) + 1 silicon (20) make 2 |
| resonant crystal | 4 | 3,000 | 1.3 L | rare loot |
| focus lens | 5 | 3,000 | 1.7 L | 1 crystal (4) + 2 quartz (52) |
| engineering, biology, physics science | 2 | 1,000 | 2 L | sample and data packs |

Consequences shown in step 1: the starter hold (60 items) is about **1.5 t** of mixed cargo, a cargo module (400 items) about **10 t**, a full ore rock of 60 items (the reference rock) about **1.8 t**; hand mining at 2 items/s is about **3.6 t/min** of ore; a depot cap of 24 items is about 0.7 t. **Machine throughput is the one place the real numbers look big**: a smelter that makes one ingot per second moves 20 kg/s of metal (72 t/h); a machine cell is therefore an *industrial block*, not a bench (decision, flagged in the Loc text as "industrial unit" only where it helps; the panel shows t/h). The alternative, 2 to 5 kg lots, would make the starter hold a few hundred kilograms, which is worse.

### Energy closure
Reactor Mk1: 10 MW, 1 hydrogen per 30 s = 300 MJ per item = 120 MJ/kg x 2.5 kg: **closed**. Reactor Mk2: 25 MW for the same fuel rate, 750 MJ per item (**a better reactor, not better chemistry**; the Mk2 text says so). Machines: a smelter draws 1 MW for one second per ingot (1 MJ for 20 kg, 0.05 MJ/kg) where real smelting needs about 1.4 MJ/kg: **not closed by a factor of about 30** (machine power is a game-balance number). The panel shows kW and MW as numbers, not as a claim of thermodynamic truth; step 2 does not touch it.

## 6. Mass table of modules

`ship_handling.json` already holds one relative mass per module kind (core 3.0, hull 0.8, ...) for the turn rate. **Unit: 1.0 = 25 t**, so a hull module (256 m3 shell) is 20 t (78 kg/m3 of volume, a light pressure hull) and the starter ship (the 7-module reference ship of `ship_handling.json`: cockpit, hull, cargo, reactor, engine, laser and gun) is **172.5 t***measured*. No new numbers: `Module mass t = ship_handling mass x 25`.

| Module | Relative | Mass | | Module | Relative | Mass |
| --- | --- | --- | --- | --- | --- | --- |
| core | 3.0 | 75 t | | shipyard | 2.0 | 50 t |
| cockpit | 1.0 | 25 t | | reactor Mk2 | 1.6 | 40 t |
| engine | 1.2 | 30 t | | workshop, defence | 1.5 | 37.5 t |
| reactor | 1.3 | 32.5 t | | drone bay | 1.0 | 25 t |
| cargo (empty) | 1.4 | 35 t | | shield, dock, hull, gun Mk2, laser Mk2 | 0.8 | 20 t |
| mining laser, gun | 0.6 | 15 t | | sensor long, gun heavy 1.2 | | 15 t, 30 t |
| sensor | 0.4 | 10 t | | sensor short, comms | 0.3, 0.5 | 7.5 t, 12.5 t |

The *loaded* ship mass in step 1 is **structure + cargo** (items x lot kg): the stats panel shows both. Thrust in kN is **not** shown in step 1 (the simulation does not know a force); the panel shows top speed and acceleration, and step 2 (P2) adds kN.

## 7. Display rules (step 1)

- **Systems**: metric SI only. **No imperial toggle now**: it is cheap in the formatters (one enum) but needs a settings row, translation of every unit word and a test matrix; the owner's request is metric. Revisit after step 1 is played.
- **Auto-scaling**: length m (below 1 km), km (below 1,000 km), Mm; mass g (below 1 kg), kg, t (from 1,000 kg), kt (from 1,000 t), Mt; volume L (below 1 m3), m3; speed m/s, km/s from 1,000 m/s (km/h only in tooltips); acceleration m/s2 with g in brackets; power W, kW, MW, GW; energy kJ, MJ, GJ (kWh in tooltips); mass flow kg/min below 1 t/min, then t/h.
- **Significant digits**: two below 100 (6.3 m/s, 12 m, 1.4 t, 4.8 MW), whole numbers from 100 (130 m, 250 kg); rounding that crosses a unit threshold rescales (999.7 m is 1.0 km). The mantissa never reaches 1,000, so **no digit grouping** is ever needed.
- **Locale**: Spanish decimal comma and English point (`Loc.Culture`: es-ES and en-US); the unit symbols are the same in both (m, km, kg, t, kW, MW, m/s, L); Spanish writes a space between number and unit as English does. Spanish "t" is *tonelada*; the tooltip names it.
- **Negative, NaN, infinity**: shown as 0 (a speed or a distance is never negative on screen).
- **Where**: HUD flight and target lines, nav list and markers, cockpit displays (flight, systems, weapons), scan and hover toasts, inventory and tooltips (mass of a stack, mass and volume of a bag and hold), build menu (module mass, power in kW, ranges in m), machine panels (power in kW, throughput in kg/min or t/h), blueprint editor stats (mass, acceleration, dimensions in m), ship computer Info and Systems tabs, docking hints, hazard line ("rock at 11 m").

## 8. Order, steps and effort

| Step | Content | Effort | Gate |
| --- | --- | --- | --- |
| 0 | This document and the open questions | S | owner reads it |
| 1 | **Display layer**: `Metric` formatters, `physical_units.json`, wiring, Loc es/en, tests, playtest page, ADR 0052 | **M** | none: no sim change |
| 2a | **P7** (rock volume and ore fraction) and **P6/P2** (mass-based acceleration with a calibrated starter ship), one flag, log version 30 | M | owner approves this spec |
| 2b | **P1** (hold capacity in kg and m3) with a bot re-measure | L | after 2a is played |
| 2c | **P5** (kinetic collision damage) | M | optional |
| later | P3, P4, P9, P10 | L each | only on request |

## 9. Decisions for the owner

1. **1 cell = 1 m** and **one linear scale for the galaxy** (stars 1.8 km apart): accept, or choose a strategic scale x 1,000 (P9, L).
2. **Item lots** (section 5): about 25 kg per item, hydrogen 2.5 kg, ammo 5 kg. Different lots change P1 more than anything.
3. **Power unit 100 kW** (reactor 10 MW): accept, or 1 unit = 10 kW (reactor 1 MW).
4. **Walk speed 4 m/s** is brisk; keep, or lower to 2.5 m/s (a `BodyMotion` constant, client-side, changes the feel).
5. **Imperial toggle**: not now.
6. **Step 2 order and scope**: which of P1 to P10, and whether P1 may change balance.

## 10. Risks and what could not be verified
- Enemy speeds and the unit of `speed` in `enemies` were not traced to the movement code; they are not displayed in step 1.
- The machine energy is not closed (section 5, "Energy closure"); the text must not call it physical.
- No measurement of the bot bands was needed for step 1 (no sim change); P1 and P2 each need a full re-measure on the three seeds and the three presets.
- Other agents are changing machine footprints (log v28) and module spans (v29); step 2 must re-fetch before it takes a number.

## 11. Step 1 as built (2026-11-28)
- **Code**: `Starfoundry.Ui.Metric` (the formatters and `Quantity`), `Starfoundry.Ui.Physical` and `PhysicalTable` (the conversions and the client-side table), `content/physical_units.json`, read by `ClientContent` beside `ship_handling.json` (also in the release pack check and the test output). [ADR 0052](../../adr/0052-physical-units-are-a-display-layer-one-cell-is-one-metre.md).
- **Wired**: HUD (flight speed and gauge tip, target distance and closing speed, deposit lines with the mass left, hazard line, markers, hold tooltip with cargo mass, power and fuel lines with the hydrogen mass), cockpit displays (nav distance with unit, rings, power pair, hold mass), star map list and gate markers, inventory tooltips (mass, volume, stack mass, hydrogen energy), world stack tooltip, build menu (module mass, kW and MW), machine panel (throughput in kg/min or t/h), machine inspector (power draw), blueprint editor stats (mass, length, top speed, acceleration, power balance, nominal cargo mass), ship designer line, docking guide, laser and drone hints, module purposes and tips. About 65 Loc strings changed in both languages; the grid square is now a *tile* (es *casilla*).
- **Decided while building**: the acceleration row shows m/s2 only (the g figure is in the speed gauge tooltip); the hold of a design is shown as items and nominal mass ("400 (about 10 t)"); the stats panel gained a *Length* row; a deposit shows the items left and their mass ("1950 (58 t)"); the nav screen draws the number and the unit apart ("1.2" and "km").
- **Not done**: an imperial toggle (decision 9.5), the enemy speeds (unit unverified), kN of thrust (step 2), a volume readout of the hold (items have a volume in the table and the tooltip, the hold total does not use it), ore of a rock in tonnes on the rock tooltip.
- **Verified**: `dotnet test` (fast tier and full), `--blueprint-check` in es and en with eight `units:` checks, the text-fit audit in es and en at four sizes. **Not verified**: the look of the new units in a hand-played session (no screenshots were taken), the audio, other monitors.
