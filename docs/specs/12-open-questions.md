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
- Docking (ADR 0029, a proposal; ADR 0041 makes the dock module the only port and gives every ship and station one): ports on a dock module (before ADR 0041: the core, the cockpit flanks and a dock module); range 12 cells and 12 units a tick; stock of 60 + 40 per dock + cargo. TBD: several ships per port rules, ship-to-ship docking, stations powering a docked ship's machines, depots and ports that use the station stock, a fee or an exchange with traders.
- Ship attitude (ADR 0030, a proposal): a quaternion command, about 15 degrees of docking alignment, the pilot seat as pivot. TBD: ship against station and ship against ship collision, docking with a yawed or upside-down port, a turn rate limit in the simulation, a pivot that survives losing the cockpit.
- Flight feel (ADR 0033, a proposal): 36 ticks to full speed and 18 to stop at one engine, a hit sphere per rock at 155 percent of its radius plus half a cell for the hull, deposits solid. TBD: whether the numbers feel right by hand, a Mk2 engine module, a setting to reduce the camera effects, a rock that shrinks when hit, the assisted docking approach tuned for the ramp.
- Personal inventory (ADR 0035, a proposal): a bag of 24 slots (stacks of 100, fuel cells 20), reach of 12 cells, one move command for the bag, hold, stock, reactor slot and machine buffers, auto-feed per reactor (default the hold). TBD: item weight, crates and dropping items in the world, picking up from the world on foot, turret ammo and drone slots as containers, the bag after a ship loss (kept in the first version), a second player's bag, whether stack sizes should scale with research.
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

## Pilot feedback round (2026-11-24)
See [the spec](../superpowers/specs/2026-11-24-pilot-feedback-round-design.md).
- What "antenna" meant in the ship-editing request: sensor antenna types, or ship designs and hull types in general (the spec covers both).
- Hold overflow while mining: surplus drifts as collectable pickups (proposed) versus jettisoned or voided.
- Mining pace: raise yield and retune enemy pressure so Normal stays near 49 minutes, versus a faster game.

## Infinite rock field (2026-11-26, ADR 0040)
- Systems on the sector grid (an infinite galaxy of named systems with lanes, depth and jump goals) versus the finite campaign galaxy plus an infinite rock field.
- Obstacle kinds, hazards and wrecks in the field (the scheme supports them; only resource asteroids are generated now).
- A floating origin for the renderer: float positions lose precision hundreds of thousands of cells from home.
- Whether a scanned rock should stay revealed after its sector is forgotten (today it must be scanned again).

## Clustered rock fields (2026-11-27, ADR 0046)
- Chosen for now, to tune with play: super-sectors of 800 cells, the study's core rates, 0.8 percent giants, radius 250 percent. The ore scale of a cluster against a late-game ore demand nobody has measured: one number, `unit_amount`.
- Clusters are visible from afar as a glow before any scan (a design choice); a survey item or a discovery rule is the alternative.
- Not done: a floating origin, a bias toward the galactic plane (clusters are a 3D volume, the home galaxy is flat), cluster markers on the map and the scanner readout beyond the tooltip and the scan toast.

## Resource fields, lighting and the route (2026-11-27, studies)
See the [resource fields study](../superpowers/specs/2026-11-27-resource-fields-design.md), the [lighting study](../prototypes/lighting-study.md) and the [improvement route](../superpowers/specs/2026-11-27-improvement-route.md). All proposals.
- Fields: the gap between clusters (800, 1,000 or 1,300 cells), the ore scale of a cluster, how rare giants are, the rock radius scale, a bias toward the galactic plane, and whether clusters are visible before a scan.
- Look: a shadowed key light, star-coloured sun, lower ambient and lamp-lit interiors are now in (a *proposal* look, [lighting pass](../prototypes/lighting-pass.md)); still open for the owner: is the darker mood right, the default level (Medium), a Compatibility renderer target and a client-only versus content-file star light table.
- Floating origin: keep the 4,000,000-cell world limit or cap it until the origin shift exists.
- Route: the hand-test pass, audio listening, minimum hardware, accessibility target, save promise and the co-op go or no-go.

## Multi-cell machines, large modules and the small dock (2026-11-27)
See [the design](../superpowers/specs/2026-11-27-multicell-and-large-modules-design.md) and [ADR 0047](../adr/0047-the-dock-is-a-small-collar-on-a-module-face.md) (stage 1, built).
- Whether a collar may also sit on a roof or a floor (vertical docking), and whether a ship that docks from its nose needs a second collar variant.
- Stage 2 and 3 are proposals: the machine footprint size limit (4 x 4), the price and throughput of the large machines, the span discount (0.85), which module kinds may be large, and the research node that gates them.

## Hull shapes (2026-11-27)
See [the spec](../superpowers/specs/2026-11-27-hull-shapes-design.md) (proposal, not built; step A of the work is the drawn-only [visual pass 3](../prototypes/visual-pass-models.md#visual-pass-3-hull-curves-ships-and-stations)).
- Whether corner, wedge, round and ring modules are a shape and a turn on a module (recommended), new module kinds or half cells.
- Which shapes come first, whether a shape changes price, hit points and mass, and whether it is gated by research.
- Whether the sim learns the module turn that the editor's rotate tool only keeps on the client today.
- Scale: a deposit rock is smaller than one module (8 cells) and a ship is 40 cells long and 5 : 1; a bigger rock radius, a lower module height or a wider starter footprint are sim-level choices listed in the visual pass.
