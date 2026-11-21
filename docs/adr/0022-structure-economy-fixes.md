# 0022. Structure economy fixes: the structure campaign must be completable

- Status: proposed
- Date: 2026-11-21
- Area: simulation, content, gameplay

## Context
A code audit of the structure campaign (`structures: true`, `StructureRules`, the way the game runs) found five blockers:
1. **Ore ports locked behind research.** Titanium and nickel research cost 40 ore each, a lab is fed only by belts, and the only unloaders that put that ore on a belt were unlocked by those same researches. The port bypass in `IsMachineLocked` applied only under `StationRules`, which the game does not use with structures.
2. **Research costs no belt can deliver.** Quartz (advanced plating, drone automation) had no drill recipe and no port; carbon (carbon drilling, jump drive, advanced logistics) had no port and drills cannot stand in modules.
3. **Hydrogen soft lock.** With zero hydrogen and no burning reactor satisfaction was 0: the cracker and the engines stopped for good, and nothing could make hydrogen.
4. **Defence unreachable.** The defence module cost titanium plates (the quartz chain), turrets only fit defence modules, so the Defend step could not be done early. The reactor needed nickel plates, the same chain.
5. **Stranding.** `RemoveModuleIn` let the player delete the ship's last cockpit, engine, reactor or cargo module.

## Decision
- **Ports are free in structure worlds.** `IsMachineLocked` treats every Unloader-role machine (all ore, ice, quartz and carbon ports and the cargo port) as unlocked when `StructureRules` is on, as it already did for `StationRules`. The legacy campaign keeps its gates.
- **Two new ports.** `core:quartz_unloader` and `core:carbon_unloader` are clones of `core:ore_unloader` (12 iron ingot, hp 80, signature 1, power 2, interval 30, reserve 0) carrying `core:quartz` and `core:carbon`. They are appended to the machine list, so existing indices do not move. Strings `machine.core:quartz_unloader` and `machine.core:carbon_unloader` in es and en.
- **Deliverability is a test, not a hope.** `StructureEconomyTests` check that every research cost item can reach a lab (a hand-minable raw item with a port, or the output of a recipe of a machine placeable in a workshop or core module whose inputs are deliverable too), that every raw item in a deposit has a port, and that the whole research graph is completable as a fixed point (prerequisites done, costs deliverable with the recipes unlocked so far). A scripted campaign test pays for advanced plating with hand-mined quartz through the real loop hold, port, belt, lab.
- **Emergency power.** `structures.reactor.emergency_power` (default 15, 0 to 100000, reactor only). A reactor with no fuel running yields it; a burning one yields `power_output`. Satisfaction is unchanged otherwise. A dead ship crawls: the cracker (12) runs and the engines move slowly, so hand-mined ice always recovers the ship. See the update paragraph of [ADR 0019](0019-power-and-fuel.md). The fuel warning of the HUD (`FuelWatch`) now also fires "out" when the fuel is gone, not only at satisfaction 0, and `learn.lowPower` talks about the emergency trickle.
- **Module costs.** Defence: 16 iron plate + 16 iron ingot (no titanium; was 12 plate, 16 ingot, 4 titanium plate). Reactor: 16 iron plate + 8 carbon (no nickel plate; was 16 plate, 8 carbon, 6 nickel plate). Engine keeps its 6 titanium plate (only needed for a second engine, the starter ship has one) and the drone bay is unchanged. The starter ship template is still free.
- **Last essential module.** New reject `LastEssentialModule` (appended at the end of the enum; strings `toast.reject.lastEssentialModule`, mapped in `RejectTexts`) when `RemoveModuleIn` would remove the last cockpit, engine, reactor or cargo module of the player's ship structure. Stations are unaffected, apart from the existing rule for the core.
- **Compatibility.** The content hash mixes `emergency_power` only when it is not 15, so a content file without the key (and with the old costs and two extra machines removed) hashes as before: the golden checksums of the defence tests still hold on that stripped content. Replays and saves made on the previous content do not carry over (content hash changes).

## Consequences
- Benefits: every research can be paid in a structure world; a ship that runs out of hydrogen can always recover; the Defend step is reachable from iron alone; the ship cannot be stripped into an unplayable state.
- Costs: a fuel-less ship is no longer a hard failure, so the pressure to keep hydrogen is softer (production 15 against a starter demand of 28 gives satisfaction 535); the game layer needs a model entry for the two new ports (`game/Models.cs` maps machine ids to models and the palette lists the ports).
- How it is checked: `StructureEconomyTests`, `PowerTests` (emergency output, `Zero_fuel_ship_is_not_dead`), `HydrogenTests` (recovery with only ice in the hold), `DroneTests` and `StructureDefenceTests` (full blackout on content with `emergency_power` 0), `MaterialsTests` (module costs).
- Not decided here: balancing the emergency value against the pacing of the first minutes, a dedicated palette key for the new ports.

## Alternatives considered
- **Quartz and carbon drills, or drill support in modules.** Rejected: drills need a placement model outside modules and a deposit link; a port from the hold reuses the loop the player already learned.
- **Free research for ore-costed nodes.** Rejected: it would hide a content bug instead of fixing the chain, and the tests would not notice the next one.
- **Starting every ship with spare hydrogen.** Rejected: it only delays the lock; a rule that cannot lock is better.
