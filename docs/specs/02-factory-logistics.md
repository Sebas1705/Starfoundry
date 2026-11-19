# 02 Factory and logistics

Status: proposal. Reference only establishes "machines + conveyor belts + production lines + automated supply routes". Everything else below is our design.

## Principles
- Deterministic, tick-based simulation (30 ticks per second target, see ADR 0002).
- Everything that moves items is an explicit entity with capacity and speed, so throughput is predictable and testable.
- Visualise flow: per-machine status (working, starved, blocked, unpowered, overheated) and per-belt saturation.

## Build volumes
A **build volume** is a bounded 3D grid (1 m cells) attached to an anchor. It can be expanded outward by adding **truss frames** (cost: parts and power). This replaces the fixed floor plan.
- Starter volume: 24 x 12 x 24 cells. Each expansion adds a frame of 12 cells on one face. Hard cap per volume: 192 cells per axis (TBD, perf-driven).
- Volumes dock to each other with **bridges** carrying belts, pipes and power.
- Volume gravity: on (artificial) or off. Off allows ceiling and wall placement and encourages compact 3D layouts.

## Item transport
| Carrier | Items/s (proposal) | Notes |
| --- | --- | --- |
| Belt Mk1 | 4 | Tier 1, one lane |
| Belt Mk2 | 10 | Tier 2, two lanes |
| Belt Mk3 | 24 | Tier 3 |
| Pipe (fluid) | 60 units/s | Tier 2, pressure model optional (TBD) |
| Pneumatic tube | 8 | Short-range, crosses walls without bridges |
| Cargo drone | 2 per trip | Intra-volume hauling, tier 3 |

Items are discrete stacks of fixed size per type (for example 50 ingots). Belts carry individual items; sorters, splitters, mergers and overflow gates are separate parts.

## Machine categories
| Category | Examples | Notes |
| --- | --- | --- |
| Extraction | Drill rig, gas skimmer, ice cutter | Needs a deposit or asteroid anchor |
| Refining | Smelter, centrifuge, cracker | Ore to ingots, gas to fuel |
| Fabrication | Printer, assembler, forge, circuit fab | Recipes with 1 to 4 inputs |
| Energy | Reactor, solar array, battery | See power below |
| Support | Sorter, splitter, buffer chest, thermal radiator | |
| Ship services | Dock, refit bay, hangar | Ships trade and refit here |

A machine has: footprint (cells), power draw (kW), heat output, input and output ports, recipe slots, upgrade slots.

## Recipes
Defined as data (see [10-data-model](10-data-model.md)): inputs, outputs, time (seconds at speed 1.0), power, optional catalyst, heat.
Example (illustrative): `iron_plate = 2 iron_ingot -> 1 plate, 2.0 s, 60 kW`.

## Power
- Grid per build volume, bridges merge grids.
- Producers and consumers; if supply is short, all consumers slow proportionally (no brownouts that delete state).
- Batteries buffer; priority tags (critical, normal, low) decide load shedding.
- Reactors consume fuel items; solar output depends on distance to the star.

## Heat (tier 3 and up)
Machines emit heat; **radiator panels** reject it into space at a rate depending on exposed surface. Overheated machines throttle. This is the signature constraint that ties factory layout to ship-like design thinking, and it feeds the Remnant detection mechanic (see [06-threats-combat](06-threats-combat.md)).

## Ship logistics
- **Supply routes:** a route is an ordered list of docks with load and unload rules (item filter, amount, trigger: when full, when below N, on timer).
- Routes are assigned to ships by the player or by a **dispatcher** station module that auto-assigns idle haulers.
- Travel time between bodies is abstracted as segment time from distance and ship speed; no player input needed.
- Route failures (no cargo, dock full, threat on path) pause with a clear alert, never silently.

## Blueprints
Select a region, save it as a blueprint (machines, belts, pipes, wiring, settings). Place with ghosts; builders (player or construction drones) fulfil ghosts from storage. Blueprints are shareable files (see [10-data-model](10-data-model.md)).

## Throughput and testing
The simulation exposes a headless API so we can unit test belt saturation, splitter fairness and power distribution, and run regression benchmarks ("1,000 machines, 10,000 belt items").

## Open questions
Pressure-based fluids vs simple flow; item-on-belt individual simulation vs lane buckets for performance. See [12-open-questions](12-open-questions.md).
