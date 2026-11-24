# 0034. Optional material chains with salvage-only crystals, and goal types that only read the world

- Status: proposed
- Date: 2026-11-24
- Area: data, simulation (read-only queries), client
- Design: [materials](../prototypes/materials.md), [scenarios](../prototypes/scenarios.md)

## Context
The economy had seventeen items and the Mk2 hardpoints of [ADR 0032](0032-weapons-as-hardpoint-modules-and-the-shipyard.md) cost only things the factory already made, so nothing sent the player to a wreck or asked for a new chain. The scenario goals of [ADR 0025](0025-scenarios-and-goals.md) could ask for items, research, modules built, stations, systems and waves, but not for what the newer systems do: fitting a second laser at the shipyard, docking and moving stock, salvage, engine speed, kills.

Constraints: the command log version belongs to other work (ADR 0033 is reserved on another branch), the bot must still win Normal in about 49 minutes, every cost must stay deliverable ([ADR 0022](0022-structure-economy-fixes.md)), and the scenario files must load against the legacy content snapshot of the tests.

## Decision
- Five items are appended after biomass: carbon fibre, composite plate, coolant, resonant crystal and focus lens, with four recipes (assembler, assembler, cracker, assembler), a crystal port and three basic-field research nodes (Composites, Cryogenics, Resonance) that gate them. No existing index moves.
- The resonant crystal is found only in derelict wrecks: the wreck loot list gains two crystal entries. A wreck still holds one item, picked with the same single random draw as before, so no obstacle moves and no other draw shifts.
- Only the Mk2 mining laser (focus lens, coolant) and the Mk2 gun (composite plate, coolant) cost the new items. The Mk1 hardpoints, the early modules, the machines and every research cost stay free of them.
- Six goal types are added to the scenario schema: `have_module`, `salvage`, `dock`, `stock`, `ship_speed` and `kill_enemies`. Each is evaluated by `ScenarioProgress` from public read-only queries of the network (`Structures()`, `StockCount`, `IsDocked`, `EnemiesKilled`, the new `WrecksSalvaged()`); none adds a counter, a command, a field of the state or a hash input. The command log version is unchanged.
- Goals show an icon in the panel (`IconCatalog.ForGoal`). The guided steps of structure worlds gain Dock, Salvage and Upgrade.
- Two levels are added (06 Salvage Run, 07 Hull Breach) and played by the bot; levels 01 to 05 and the campaign gain secondary goals.

## Consequences
- Benefits: a reason to salvage and to dock, a deeper late economy for weapons, goals that exercise the shipyard, docking, free flight and guns, and no log or checksum change (saves of earlier content were already invalidated by earlier content hashes; this one changes the content hash again, as every content change does).
- Costs: a goal that reads the current world can be satisfied by modules the player removed later (a done goal stays done, so it never regresses); `salvage` is a count of wrecks gone, not per pilot; `kill_enemies` counts every kill, turrets included.
- How it is checked: `GoalTypesTests`, `MaterialsExpansionTests`, `ScenarioContentTests` (texts, icons, reachability, the bot wins levels 01, 02, 03, 06 and 07), `IconCatalogTests`, `StructureObjectivesTests`.
- Not decided: salvage from mines and unstable crystals (needs an obstacle rule and a version gate), engine and hull tiers as new module kinds (needs the ship geometry work to land first), models for the new items.

## Alternatives considered
- Crystals as a deposit in a new archetype: rejected, a new archetype shifts the galaxy generation and with it every measured time.
- A new counter for docking and salvage events with a log version: rejected, the current state answers every question the goals ask.
- Mk2 costs in plates and ingots only: rejected, an upgrade that asks for nothing new gives the player no reason to leave the factory.
