# 04 World and exploration

Status: proposal. Reference states procedurally generated systems with unique logistic challenges, wrecks, new resources, black holes, neutron stars, NPC traders.

> **Implementation status:** a first version exists ([ship in the world](../prototypes/world-and-ship.md), [galaxy and exploration](../prototypes/galaxy-exploration.md)): a seeded galaxy of systems with archetypes and connected lanes, hidden deposits revealed by a scanner, and jumps along lanes gated by research. The systems share one coordinate space ([ADR 0007](../adr/0007-galaxy-as-regions-of-one-space-with-deterministic-generation.md)). Since [ADR 0028](../adr/0028-world-generation-obstacles-and-collisions.md) structure worlds also have a wider galaxy ([prototype](../prototypes/world-generation.md)): star types, belts, clusters, nebulae (ion and gravity), derelict wrecks, mines and unstable crystals, drifting debris, richer far veins, boost and collision damage. Solar power, black holes and neutron-star effects beyond the star type, NPCs and trade, and cross-system logistics are still design only. Since [ADR 0040](../adr/0040-infinite-rock-field-generated-on-demand.md) structure worlds also have an **infinite field of resource asteroids** around and beyond the systems, generated sector by sector from the seed (size times density decides the ore; an emptied rock is destroyed); the systems themselves stay finite. Since [ADR 0046](../adr/0046-clustered-rock-fields-with-voids-size-classes-and-giants.md) that field is clustered: large voids, dense clusters, belts and shells of rocks in size classes with rare giants, drawn by batches and visible from afar as a glow.

## Structure
Galaxy map of **systems**, each generated from a seed. Each system contains a star, bodies (asteroid belts, planetoids, gas giants, ice fields) and optional anomalies. Systems are connected by **lanes** discovered by scanning.

```mermaid
flowchart TD
  Galaxy --> System
  System --> Star
  System --> Belts[Asteroid belts]
  System --> Giants[Gas giants]
  System --> Anomaly[Anomalies]
  System --> Sites[Wrecks and sites]
```

## Generation
- Seed = galaxy seed + system index. Same seed gives the same system for every player (co-op safe).
- Each system picks an **archetype** that defines its logistics challenge: dense belt (short hauls), sparse belt (long hauls), gas-rich (fluid logistics), radiation storm (shielding), tight orbit (heat), rogue (no sun, power from fuel only).
- Star type sets solar power, temperature zones, and which resources exist (data-driven weights).

## Resource tiers
| Tier | Examples (illustrative) | Where |
| --- | --- | --- |
| 1 | iron ore, carbon, ice | Home belt |
| 2 | copper, silicon, hydrogen | Home system, outer belt |
| 3 | titanium, rare isotopes, exotic gas | Other systems |
| 4 | Remnant alloy, neutron-star matter | Anomalies and Remnant sites |

## Movement
- **In-system:** assisted flight. Free flight near stations and bodies; long legs use autopilot "burn" with a visible time. Ships have no fuel for basic thrust in the MVP (TBD).
- **Between systems:** jump drive (tier 3 research) along discovered lanes; jump cost in fuel and charge time.

## Scanning
Active scanner reveals deposits in a cone and range; passive scanner marks anomalies. Scan results are persistent map data. Richness is shown as a colour band, not exact numbers.

## Points of interest
- **Wrecks:** loot containers and a short puzzle (power the door, route a cable) that yields blueprints and rare parts.
- **Derelict stations:** claimable, with a ruined module graph the player can repair.
- **Anomalies:** black hole (gravity well hazard, energy source later), neutron star (extreme heat and rare matter), nebula (sensor noise).
- **Remnant sites:** guarded vaults and nodes of the sentinel network (see [06-threats-combat](06-threats-combat.md)).

## NPCs and trade
Small, scripted outposts offering trade and contracts. Early scope: buy and sell a fixed list of items at rates that shift with supply. Factions are post-MVP.

## Persistence
Visited systems are stored as seed plus a delta list (player-built entities, depleted deposits). The world is never regenerated from scratch.

## Open questions
Galaxy size, lane graph shape, finite vs regenerating asteroid belts. See [12-open-questions](12-open-questions.md).
