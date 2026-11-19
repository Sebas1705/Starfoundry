# Vision

Working title: **Starfoundry**. Status: proposal.

## Pitch
You are the last licensed tug pilot of a dead mining guild, drifting in a rich asteroid belt with one ship. Mine, refine, build a station, automate it, and grow a logistics empire across procedurally generated star systems, while the dormant sentinels of an ancient network wake up in response to your heat and power signature.

## Pillars
1. **The factory is the game.** Satisfaction comes from designing, debugging and scaling production lines. Combat and exploration feed the factory; they never replace it.
2. **Everything is a module.** Ships, stations and machines are assembled from parts with real trade-offs (mass, power, heat, volume), not picked from a menu.
3. **Scale without friction.** Blueprints, bulk placement and clear flow visualisation make a 500-machine base as pleasant as a 5-machine one.
4. **Pressure, not punishment.** Threats escalate with your industrial footprint, give warning, and can be handled by design (defence) or by logistics (evacuation, shielding). No permadeath, no lost saves.
5. **Together or alone.** Same game solo or in co-op; every player captains their own ship.

## Audience
Players of Factorio, Satisfactory, Dyson Sphere Program, X4 and Space Engineers who want a first-person, ship-centred automation game. Comfortable with systems depth; expect polish and good tooling.

## Scope
- **MVP target (Early Access):** one star-system generator, three resource tiers, about 30 hours of content, solo plus 2 to 4 player co-op, Windows first.
- **Platform:** PC (Windows, then Linux). Controller support is a later goal.
- **Team:** assumed single developer with part-time hours, so every spec favours reuse and data-driven content over bespoke code.

## Non-goals (for now)
- Real-time Newtonian flight simulation across huge distances. Travel between bodies is assisted.
- Player-versus-player.
- Competing with AAA visuals. Readable, stylised art is a deliberate choice.
- Mod tooling in the first release (but content is data-driven so mods are possible later, see [10-data-model](specs/10-data-model.md)).
- Monetisation beyond a one-time purchase.

## Differentiators (proposal)
- Expandable build volumes: stations grow outward in 3D; there is no hard floor-plan limit that creates the cramped-factory complaint seen in the reference.
- Blueprints and ship templates are first-class from the prototype onward.
- A deterministic simulation core that can run headless, which gives automated tests, replays, and cheap co-op sync.
- An original setting: the **Remnant** (see [06-threats-combat](specs/06-threats-combat.md)).

## Success criteria
- A new player places a working mine-refine-assemble line within 20 minutes.
- A 1,000-machine save holds at least 60 FPS on the recommended spec and 30 UPS simulation (see [09-technical-architecture](specs/09-technical-architecture.md)).
- Playtesters ask for more content, not for fixes to the basics.
