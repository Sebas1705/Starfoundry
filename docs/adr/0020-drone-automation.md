# 0020. Drone automation: bays abstract mining ships without moving entities

- Status: proposed
- Date: 2026-11-21
- Area: simulation, gameplay

## Context
- The [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md) (part 3) wants the base to grow without the player hand-mining all the time, with "mining ships" the player can see working.
- The simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md)). Structures ([ADR 0018](0018-modular-structures.md)) move, and power ([ADR 0019](0019-power-and-fuel.md)) already gives every structure a satisfaction value.
- Hand mining stops at 75 percent of the hold, so the hold is already the throttle of mining income.

## Decision
- **Module kind.** `ModuleKind.DroneBay` (appended to the enum; the content id is `drone_bay`). Content entry `structures.drone_bay`: cost 12 iron plate, 8 titanium plate and 8 nickel plate; hp 150; `allows` nothing; `drone_range_cells` 60; `drone_interval_ticks` 90 (3 s per item at density 100); `power_use` 20. In campaign mode placing one needs the research `core:drone_automation` (ability `drone_bay`; requires `core:logistics` and `core:advanced_plating`; costs 150 iron plate, 20 titanium plate and 20 quartz); a locked placement is rejected with `Reject.Locked`. The sandbox needs no research.
- **Assignment.** The command `AssignDrones(Structure, Deposit)` (tag 35; `-1` clears) sets one deposit per structure. It must be a revealed deposit within `drone_range_cells` of the world centre of the structure's cell (0, 0, 0), and the structure needs a bay (`Reject.NoDroneBay`, appended to the enum). Only the owner may assign, and only under the structure rules.
- **Work.** Every tick, after hand mining, each bay of an assigned structure adds the structure's power satisfaction (per mille) to its own progress. At `interval * 100 / density` ticks' worth (`DepositRules.Scale`) it takes one item from the deposit into the owner's hold. Bays work independently, so two bays double the rate. At satisfaction 1000 this is exactly one item per interval; with no power the progress does not move. A bay waits at full progress while the owner's hold is at the mining limit (hold total below 75 percent of the capacity, the same rule as hand mining) and delivers as soon as there is room.
- **Power.** While a deposit is assigned each bay adds `power_use` to the structure's demand, so a bay draws fuel like a machine.
- **Range and depletion.** Range is re-checked every tick: a structure that flies away, or a deposit that runs out, drops the assignment (back to -1). Removing the last bay also clears it.
- **State.** The assignment and the per-bay progress live in the structure (cloned, hashed and validated only under the structure rules; a structure with no drone work hashes as before). Drone-mined items are counted in `BeltNetwork.MinedByDrones`, which joins the deposit accounting check and is hashed only when non-zero.
- **Accessors.** `BeltNetwork.DroneBays(structure)`, `AssignedDeposit(structure)`, `DroneStatus(structure)` returning `DroneStatus(Bays, Working, Deposit, RatePerMinute, WaitingForHold, PowerOk)`.
- **Client.** The look (a bay launching small drones towards the deposit) is cosmetic and client only; the simulation has no drone entities.

## Consequences
- Benefits: a handful of integers per structure; deterministic replay, save and clone with no new entity type; automation is limited by hold space and by fuel, as the roadmap asks; legacy worlds hash exactly as before (golden checksum in the tests).
- Costs: drones cannot be shot down or delayed on a trip, so there is no travel time or risk to model later without changing the rules; the assigned deposit is one per structure, so mining several deposits needs several structures.
- Pacing: measured numbers are in [docs/prototypes/automation.md](../prototypes/automation.md).
- How it is checked: tests for the rate, density scaling, two bays, zero power, the hold limit, range loss, assignment validation, the research gate, no depletion below zero, save, clone and replay round trips, the codec, and an unchanged legacy checksum.
- Not decided here: bays of different sizes, upgrades, drones that carry ore to a cargo port instead of the hold.

## Alternatives considered
- **A. Abstract bays (chosen).** A rate per bay, no moving things.
- **B. Simulated drone entities** that fly to the deposit, mine and fly back. Rejected for now: positions, speeds, pathing, collisions with enemies and a trip state to save make the state large and the determinism checks heavy, for a result the player sees equally well with a cosmetic effect.
- **C. A drill-like machine linked to a deposit** (reusing the drill role). Rejected: drills deliver onto belts and need a station; automation here should feed the hold directly so a ship can use it.
