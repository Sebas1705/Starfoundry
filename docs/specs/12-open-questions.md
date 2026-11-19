# 12 Open questions

Update this file in the same change as any decision. Move resolved items into an ADR.

## Needs owner decision
1. **Engine:** Godot 4 (recommended) vs Unity vs Unreal. See [ADR 0001](../adr/0001-engine.md).
2. **Name:** "Starfoundry" is a working title. Needs a trademark and domain check.
3. **Art direction:** stylised low-poly (assumed) vs realistic.
4. **Scope:** solo-only first, or co-op in the vertical slice?
5. **Distribution:** Steam Early Access, Epic, itch.io demo; owner-only account steps.
6. **GitHub:** repo visibility (public vs private) and org.

## Design
- Tier count (four proposed) and gates.
- Belts: individual items vs lane buckets for performance.
- Fluids: pressure model or simple flow.
- Flight: fuel for basic thrust? Assisted autopilot detail.
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
