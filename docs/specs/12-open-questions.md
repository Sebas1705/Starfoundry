# 12 Open questions

Update this file in the same change as any decision. Move resolved items into an ADR.

## Needs owner decision
1. ~~Engine~~ Decided: Godot 4 with C#, see [ADR 0001](../adr/0001-engine.md).
2. **Name:** "Starfoundry" is a working title. A web search on 2026-11-19 found no game or studio with that exact name, but close names exist (Starfactory on itch.io, Obelisk Foundry, Foundry, StarWorks, Astrofactories). Still needs a real trademark search (USPTO, EUIPO) and a domain check before any store page or art.
3. **Art direction:** stylised low-poly (assumed) vs realistic.
4. **Scope:** solo-only first, or co-op in the vertical slice?
5. **Distribution:** Steam Early Access, Epic, itch.io demo; owner-only account steps.
6. **GitHub:** repo visibility (public vs private) and org.

## Design
- Tier count (four proposed) and gates.
- Belts: individual items vs lane buckets for performance.
- Fluids: pressure model or simple flow.
- Flight: fuel for basic thrust? Assisted autopilot detail.
- Docking (ADR 0029, a proposal): ports on the core, the cockpit flanks and a dock module; range 12 cells and 12 units a tick; stock of 60 + 40 per dock + cargo. TBD: several ships per port rules, ship-to-ship docking, stations powering a docked ship's machines, depots and ports that use the station stock, a fee or an exchange with traders.
- Inter-system travel: lanes only or open jump.
- Finite vs regenerating asteroids.
- Quiet play: can a player avoid waves entirely?
- Fleet AI depth (formations, orders).
- Trade: static NPC prices vs simple market.
- Factions: scope and timing (post-MVP assumed).

## Technical
- Fixed-point vs integer-only simulation in the chosen language (ADR 0002 sets determinism as the goal).
- Content format (JSON vs binary) and schema language.
- Networking transport (engine built-in vs Steam sockets).
- Host migration, dedicated server, cross-play.
- Maximum build volume size at target performance.

## Research gaps
Reference specifics we could not read: recipes, resource list, module stats, system count, co-op cap, modding. Verify by playing the reference or reading the official wiki ([unofficial wiki](https://mainsequenceguide.wiki/game/) not yet reviewed) before treating any comparison as fact.
