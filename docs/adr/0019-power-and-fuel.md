# 0019. Power and fuel: reactors burn hydrogen from the owner's hold

- Status: proposed
- Date: 2026-11-21
- Area: simulation, gameplay

## Context
- The reactor module of [ADR 0018](0018-modular-structures.md) is a marker with no effect. The [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md) (part 2) wants energy and fuel: no hydrogen means no power, so the ship drifts and the factory stops.
- The simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md)). Worlds without the structure rules, and structure 0, must keep their behaviour and checksum.
- The new materials already include ice, mined at the home system.

## Decision
- **Fuel.** The item `core:hydrogen` is made from ice by the machine `core:hydrogen_cracker` (a crafter, recipe `core:crack_ice`: 2 ice into 1 hydrogen in 4 s). Both are available from the start and fit the workshop and core modules. A new `core:ice_unloader` (cloned from the ore port, carrying ice, no research gate) takes ice from the hold onto a belt, so the loop hold, ice port, cracker, depot, hold works.
- **Reactors.** Under the structure rules, for structures other than 0 only, each reactor module produces `power_output` (content, 100) while it burns. It burns one hydrogen taken from the owner's hold every `burn_interval_ticks` (content, 900 ticks, 30 s). When it cannot take one it stops producing at the end of the current burn. Burn state is kept per reactor module.
- **Demand and satisfaction.** Every machine definition has `power_use` (content, default 0). A structure's demand is the sum for its alive machines. Satisfaction in per mille is `min(1000, production * 1000 / demand)`; 1000 when production covers the demand; and 0 when a reactor exists but nothing burns (even with no demand, so a bare ship without fuel cannot fly). A structure with no reactor and no demand is fully powered, which keeps hand-built test ships and legacy flows unchanged.
- **Effect.** At the start of every tick, before anything works, the power step burns fuel, computes the structure values and gives every machine a work credit (`PowerCredit`): credit grows by the satisfaction each tick and the machine works on a tick when it reaches 1000, keeping the remainder. At 1000 this is identical to no power model. Crafters advance and start crafts only on credited ticks; labs, depots, unloaders and repair bays work only on credited ticks; turrets fire only at satisfaction 500 or more. Engine speed is multiplied by the satisfaction (the sub-unit remainder now has a denominator of 100 x 1000); with 0 the ship does not accelerate although a thrust command is accepted.
- **Accessors.** `BeltNetwork.PowerStatus(structure)` and `ShipPower(player)` return `PowerStatus(Production, Demand, SatisfactionPermille, BurnRemainingTicks, HydrogenInHold, Burning)`.
- **Starter ship.** The structure template has a `hold` section: the starter ship begins with 20 hydrogen. Its demand is 28 (ore port 2, smelter 10, assembler 15, depot 1), below the 100 a reactor makes, so it runs at full power for 18000 ticks (10 minutes) before the fuel is gone.
- **State and checks.** Burn timers, the last production, demand and satisfaction per structure, and the machine credits are cloned and hashed only under the structure rules, and validated. Content hash mixes the new fields only when set, so content without power hashes as before.

## Consequences
- Benefits: a real reason to mine ice and build a cracker; stations need a reactor of their own to run machines; the model is a few integers per structure and machine; legacy worlds hash exactly as before (a golden checksum in the tests).
- Costs: a station without a reactor module has no power, so its machines stand still until the player adds one; ships built by hand in tests need a reactor and hydrogen if they carry machines. The starter ship's hold is 20 items smaller for ore and plates (hand mining stops at 75 percent of the hold).
- Pacing: measured numbers are in [docs/prototypes/power.md](../prototypes/power.md).
- How it is checked: tests for fuel burn timing, satisfaction scaling of crafting and engines, zero power, the starter ship's fuel time, the cracker, refuelling from hand-mined ice, turrets, unchanged legacy checksums and replay, save and clone round trips.
- Not decided here: reactors of different sizes, batteries, a fuel tank per structure, power for belts.

## Alternatives considered
- **A. Fuel from the owner's hold (chosen).** Nothing new to store per structure; the same hold that the depot fills and the ice port empties; stations and the ship share the pool of their owner.
- **B. A hydrogen tank per structure.** Rejected for now: it needs a new storage module, new commands to fill it and a way to move fuel between structures. It can be added later without changing the burn rules.
- **C. Satisfaction as a plain speed multiplier (no work credit).** Rejected: crafting times are integer ticks, so a multiplier would need fractional time; the credit keeps every machine on whole ticks and stays deterministic.

## Update (2026-11-21, see ADR 0022)
A reactor module with no hydrogen and nothing burning now yields an emergency output `structures.reactor.emergency_power` (default 15) instead of nothing. Production is `power_output` while a reactor burns and the emergency value otherwise, so a ship with no hydrogen is slow but not dead: the cracker (12) can still run, and with the ice port and a depot (15 in all) the loop hold, port, cracker, depot, hold brings hydrogen back. Satisfaction is still `min(1000, production * 1000 / demand)`. `PowerStatus.Burning` now means a reactor is really burning. The soft lock described under "Consequences" (zero satisfaction with no fuel) is gone; content with `emergency_power` 0 keeps the old blackout.
