# 0052. Physical units are a display layer: one cell is one metre, items have a mass, power is in kilowatts

- Status: proposed
- Date: 2026-11-28
- Area: tooling

## Context
- The interface counted distances in "cells", speeds in "cells/s" or "units per tick", hold contents in bare item counts and power in bare numbers. The owner asked for real measures: metres, kilograms, kilowatts.
- The client already walks a 1.7 m eye, a 1.8 m body and 2.7 m doors in cells, so the game is drawn at one cell per metre (`BodyMotion`, `ModuleModels`).
- The full design, with the table of every quantity, the decisions and the physics proposals for step 2, is [the physical units design](../superpowers/specs/2026-11-28-physical-units-design.md).

## Decision
- **No simulation change, no command, no log version, no content hash change.** The next free log version and the reserved numbers stay as they are; this ADR only changes what the interface says.
- **1 cell = 1 m** (`PhysicalTable.CellMetres`, one), a simulation unit is 1/240 m, a tick is 1/30 s. A module is 8 m x 4 m x 8 m. One linear scale for everything, the galaxy included (systems are 1.8 km apart; we do not claim stellar distances).
- **Metric display** in one engine-free place, `Starfoundry.Ui.Metric`: SI auto-scaling (m, km, Mm; g, kg, t, kt, Mt; L, m3; W, kW, MW, GW; J, kJ, MJ, GJ; m/s, km/s; kg/min and t/h), two significant digits below 100 and whole numbers from 100, a rescale when rounding crosses a unit (999.7 m is 1.0 km), the decimal comma in Spanish and the point in English (`Loc.Culture`), no digit grouping (the mantissa stays below 1,000), negative and non-finite values show as zero. Metric only; an imperial option is not built.
- **A client-side table**, `content/physical_units.json` (read like `ship_handling.json` by `ClientContent`, never hashed, never read by the simulation): the mass in kg and the bulk density of one item of every kind (a *lot*, about 25 kg for solids, 2.5 kg for hydrogen), the power unit (100 kW), the module mass unit (25 t, applied to the relative masses of `ship_handling.json`), the reference lot and the rock numbers. Built-in defaults equal the file (a test checks it); a missing or invalid file is reported and the defaults are used.
- **Where it shows**: HUD (flight speed and gauge tip, target distance and closing speed, deposit lines with the mass left, hazard line, markers, hold tooltip with the cargo mass, power and fuel lines with the hydrogen mass), cockpit displays (distance with its unit, speed, power pair, hold mass), nav list and rings, the star map, inventory tooltips (mass, volume, energy of the fuel), the world stack tooltip, build menu (module mass, power in kW or MW), the machine panel (throughput in kg/min or t/h), the blueprint editor stats (mass, length, top speed, acceleration in m/s2 and g, power balance, nominal cargo mass), the ship designer line, docking and laser hints. The word "cell" is gone from distances and speeds; a grid square is a *tile* (es *casilla*).
- **Checks that keep it honest**: `PhysicalUnitsTests` (every item has an entry, no recipe creates mass, hydrogen gives about 120 MJ/kg in the reactor, every module kind has a mass, no text counts distances in cells, placeholders match), `MetricTests` (scaling, rounding, locales, the conversions of the simulation constants) and the `units:` checks of `--blueprint-check` in both languages.
- **Step 2** (real physics: hold capacity by mass, acceleration from mass and thrust, kinetic collision damage) is a proposal in the design, each with its own flag, log version and balance run. Nothing in it is decided by this ADR.

## Consequences
- Benefits: the numbers on screen mean something and survive a physics change (the formatters take SI values); old saves and logs are untouched; the bot bands do not move (no simulation change).
- Costs: the machine energy is not thermodynamically closed (a smelter draws 1 MW for 20 kg a second); the text must not call it physical. Speeds look slow for space (6 m/s per engine) because the world is a boulder field, not an orbit. An item still counts as one slot whatever its mass: the hold shows its mass but a mass-limited hold is step 2.
- Checked by the tests above, `--blueprint-check` (eight of its checks are about units) and the text-fit audit in both languages.

## Alternatives considered
- **A. A scale factor between cells and metres.** Rejected: the body and the doors are already in metres.
- **B. A km/s drive and a world 1,000 times larger.** Rejected for now: it regenerates the galaxy and changes the log; listed as P9 in the design.
- **C. Putting masses in `core.json`.** Rejected for the display step: it would change the content hash and every golden checksum; a client table does the job until physics needs the numbers in the simulation.
- **D. An imperial toggle now.** Rejected: low value for the cost of a settings row and a test matrix; the formatters leave room for it.
