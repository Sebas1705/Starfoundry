# 0028. A wider galaxy with obstacles: seeded star types, rocks, mines, clouds, wrecks and debris, boost and collision damage; log version 10

- Status: proposed
- Date: 2026-11-22
- Area: simulation, content, gameplay

## Context
The galaxy of [ADR 0007](0007-galaxy-as-regions-of-one-space-with-deterministic-generation.md) is empty space around a few deposits: home deposits lie 6 to 12 cells from the anchor, other systems 12 to 60, systems 800 cells apart. A ship flies 6 cells a second, so the whole home field is two seconds across and nothing in space can hurt a ship that is not in a wave. The owner asked for better generation, bigger distances, collisions with asteroids and more obstacle kinds ([prototype](../prototypes/world-generation.md)).

The simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md)). Old command logs must replay as recorded, the golden checksums of the legacy worlds must not move, and the scripted player ([structure bot](../prototypes/structure-bot.md)) must still win Normal at about the same time.

## Decision
- **A `world` section of the content** (optional; without it the content generates the legacy galaxy and has no obstacles, boost or collisions). It holds the wide spacing and system radius, star types, obstacle kinds, keep-clear distances, the boost percent, the collision rules and the salvage rules. It is part of the content hash only when present. Archetypes get an optional `wide_distance_cells`.
- **The wide galaxy** (structure worlds only):
  - Systems 1800 cells apart (was 800) with a radius of 320 (was 150). Home deposits 36 to 60 cells from the anchor (was 6 to 12); other archetypes 50 to 240 (was 10 to 60). Drone bays reach 130 cells (was 60), the wide scanner 300 (was 80), and a wave appears 70 cells from the base, not from the anchor.
  - **Star types** (yellow dwarf, red dwarf, orange star, blue giant, binary, pulsar), picked by weight with a minimum depth (jumps from home) for the exotic ones. A type scales the veins (`richness_percent`) and the density of rocks and hazards. Home is always a yellow dwarf.
  - **Veins grow** with the star's richness, 15 percent per jump from home, and up to 40 percent from the inner to the outer edge of the system. The legacy amounts are the base.
  - **Obstacles** of eight kinds in five behaviours (below), about 50 per system: an asteroid belt (a ring, in 55 percent of systems), one to three clusters, scattered singles, up to two nebulae, one to three wrecks (one at home), and from depth 1 groups of mines or unstable crystals and three to seven drifting debris. Dense and sparse fields come from the star type.
  - **Guarantees.** Every system generates from its own random stream (so nothing else shifts). Solid rocks keep 70 cells clear of the anchor and 60 of every deposit and leave at least 10 cells between rock surfaces; mines and debris keep 80 cells from anchors and deposits; clouds never cover an anchor. Home has no mines and no debris. Home keeps its iron (12000), carbon (1500) and ice, and every system stays on a connected lane graph, so the arrival point, the hand mining spots and the jump goals are never boxed in. Deposits themselves are not solid (the mining laser works inside the rock).
- **Obstacle behaviours** (content kinds, es/en names in `Loc`):
  - *Solid* (`asteroid_small`, `asteroid_big`): pushes a ship out and hurts the module that hit it. A station never moves into a rock, so only ships collide.
  - *Drifter* (`drifting_debris`): moves along a line in a triangle wave of the tick, hurts any structure it crosses by relative speed, is never pushed.
  - *Mine* (`floating_mine`, `unstable_crystal`): a ship module within the trigger range arms the fuse; at zero it explodes, hurting every module within the blast radius (linear falloff, any structure) and arming the mines inside the blast. A mine that has exploded is gone.
  - *Cloud* (`ion_cloud`, `gravity_cloud`): while any module touches it the engines lose `slow_percent` and the reactors `drain_percent`. Stateless: computed from the positions of the tick.
  - *Wreck* (`derelict_wreck`): a ship that stays within 12 cells at 20 units a tick or less for 60 ticks salvages one item stack chosen at generation (a few plates, ingots, carbon, ammo or ice), limited by the room in the hold (it waits if the hold is full). Salvaged wrecks are gone.
- **Boost.** New command `SetBoost(bool)` (tag 38) and `Player.Boost`: the engines run at the content's `boost_percent` (400) of their speed (the 4x cap of engines is applied first). Free to use; its price is the collision damage. Rejected with `NotAllowedHere` without a `world` section, switched off by a jump and by the loss of the ship.
- **Collision damage.** Once a tick, after the ships moved, a ship's module boxes are tested against every solid, drifter and mine of the system. The deepest module is the one that hits:
  - A solid pushes the ship out along the contact normal by the depth (the ship cannot fly through it).
  - The closing speed is the relative speed along the normal in units per tick (a ship at base speed closes at 48). At or below `safe_speed_units` (10) it is a bump: an event, no damage. Above it `damage = kind.damage * (v^2 - safe^2) / (48^2 - safe^2)` at least 1 and at most 16 times the kind's damage, so a base-speed hit does the kind's damage (6 small, 14 big, 10 debris) and a boosted hit about 16 times that.
  - After a hit the structure takes no collision damage for `cooldown_ticks` (60), so pressing on a rock costs a hit every two seconds, not every tick.
  - Damage goes through the module hit points of [ADR 0021](0021-structure-defence.md): shields absorb their percent, `Hull` modules are bumpers (whichever module is nearest takes the hit), the existing repair bays and passive regeneration heal it, and a ship is lost only through the existing rule (its last cockpit destroyed). Events are `ModuleHitEvent`s (so toasts, bars and sounds work) plus `ObstacleEvent`s.
- **Compatibility.**
  - The command log version is 10. `SetBoost` cannot appear in logs of versions 5 to 9 (the reader refuses it).
  - `WorldRules.LegacyGalaxy` (a bit of the rules flags, off in every preset and in the new-game screen) generates the legacy galaxy. Logs older than version 10 are read with it on. It hashes only together with other non-default rules (as `LegacyLaser` does). Worlds without the structure rules (the pre-structure station worlds and their balance bots) always generate the legacy galaxy, so their pacing is untouched.
  - State is mixed only when set: obstacle state (exploded mines, salvaged wrecks, burning fuses, salvage progress), `Player.Boost` and a structure's impact cooldown. Obstacles themselves are a pure function of the seed and the content. The golden checksums hold on the stripped content (`Stripped()` in `StructureDefenceTests` now also removes the `world` section, the wide distances and the drone range).
  - Content or legacy-flag combinations that produce a legacy galaxy (`legacy: true`, content without `world`) are tested to give exactly the old deposits and systems.
- **Not decided here:** a manual module repair command (heals only through repair bays and regeneration, as before); the mining laser breaking small rocks; ore chunks from broken rocks; hazards for stations (only mines and debris reach them); obstacle-aware enemy movement.

## Consequences
- Benefits: flying has weight (a 40-cell ship passes between rocks, boosts across a system in seconds and pays for mistakes); every system differs by star, belts and hazards; far veins are worth the trip; the whole thing is data (kinds, numbers) and deterministic.
- Costs: a collision pass every tick for structures in a system with obstacles (about 50 obstacles by 10 modules: the scripted player runs 150 minutes in a few seconds); one command, one tag, a player field, an obstacle table and a structure field to every consumer of the log; the bot flies longer (about 1.5 to 3 minutes of game time on Normal, [prototype](../prototypes/world-generation.md)).
- How it is checked: `WorldGenerationTests` (determinism, legacy path, guarantees over 200 seeds, keep-clear and gaps, star depth, richer far veins, content validation), `CollisionTests` (boost, rams, push-out, cooldown, bump, boost cap, shields and hull bumpers, last cockpit, regeneration, mines, chain blasts, wrecks, clouds, debris, saves and replays), the golden checksum tests, the version and rules-flag tests, `StructureBotTests` and the bot's report.

## Alternatives considered
- **A. Make deposits solid.** Rejected: the ship box is 8 by 4 by 40 cells and the mining spot is 3 cells from the deposit centre, so every hand-mining position would be a collision.
- **B. Pass-through collisions (damage only).** Simpler, but a rock the ship can fly through is not an obstacle; the push-out costs one integer square root.
- **C. Boost that costs hydrogen or power.** Rejected for now: power demand is solved at the start of the tick for machines and the laser, and a second priority class would touch every power test. The collision damage is the price.
- **D. Scaling the legacy galaxy (all distances times four).** Rejected: it breaks old logs and the stripped goldens, and cannot hold star types or obstacles. The wide generator is a second path selected by the content and the legacy flag.
- **E. Put obstacles in the world rules.** Rejected: they are generation data, not difficulty; the rules only carry the legacy switch.
