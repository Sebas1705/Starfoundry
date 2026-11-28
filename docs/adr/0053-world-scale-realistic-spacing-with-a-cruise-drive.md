# 0053. World scale: realistic spacing with a cruise drive, a floating origin in the view, log version 30

- Status: accepted (stage 0, stage 1 and the stage 2 slice below; the rest of stage 2 is deferred)
- Date: 2026-11-28
- Area: simulation, content, client
- Builds on: [ADR 0007](0007-galaxy-as-regions-of-one-space-with-deterministic-generation.md) (regions of one space), [ADR 0033](0033-flight-inertia-solid-deposits-and-hit-volumes.md) (flight governor), [ADR 0040](0040-infinite-rock-field-generated-on-demand.md) and [ADR 0046](0046-clustered-rock-fields-with-voids-size-classes-and-giants.md) (the rock field), [ADR 0052](0052-physical-units-are-a-display-layer-one-cell-is-one-metre.md) (1 cell = 1 m)
- Design: [world scale](../superpowers/specs/2026-11-28-world-scale-design.md). Stage 0 (this decision) is documents only; stage 1 (floating origin, client only) and stage 2 (the scale, the drive) follow as separate pull requests.

## Context
ADR 0052 fixes 1 cell = 1 m for everything the player touches and shows that distances are the unrealistic part: systems 1.8 km apart, rock sectors of 200 m, rocks of 3 to 24 m, scanners of hundreds of metres. The units design rejected a world 1,000 times larger (P9) for its cost. The owner asked for more realistic distances and left the equivalence to us. The simulation holds positions in `int` units of 1/240 m (8.9 Mm), the client converts to float32 at about 110 sites, and the economy, the bots and the balance live in the 320 m working area of each system.

## Decision
- **The local scale does not change** (1 cell = 1 m, ships, modules, docking, mining and weapon ranges, the 320 m working area of a system with its veins and hazards).
- **Spacing and rock size change**, in a new content block `world.scale` (metres): rocks 33 m to 1.6 km (ore per rock unchanged), sectors 10 km, clusters 6 to 14 km across and 100 km apart, loaded out to 40 km, a field only within 600 km of a system anchor, scan 10 km and nav 150 km.
- **A system is a bubble of 1.5 Gm** (1.5 million km) in a shared frame, bubbles 8 Gm apart; between systems the ship jumps, and the jump is shown in light years (about 3 ly per grid step). 0 to 3 planets and moons per system, 0.25 to 1.2 Gm from the anchor, are nav targets and danger zones.
- **Cruise drive**: ability `cruise` (research, two levels), command `SetCruise` (log tag 73), field cruise to 3 km/s and system cruise to 10 Mm/s, spool-up doubling each second from 200 m/s, and a **time-to-contact governor** (5 s in the field, 8 s in a system, 100 m margin) that makes a hull collision at cruise impossible.
- **Integers**: positions and velocities become `long` in a refactor with no flag; squares are taken in metres inside a bubble (6.75e18 at most) and in `Int128` between systems.
- **Compatibility**: `WorldRules.LegacyWorldScale` (third flags byte, bit 5, value 32), log version **30**; logs of versions 5 to 29 read with it on and replay byte for byte; the content hash moves only when `world.scale` is present.
- **Client**: a floating origin (`ViewOrigin`, snapped to 1,000 cells, rebased beyond 20,000 cells) in the view only; no simulation change; built first so that nothing at the new scale is ever drawn from a large float.

## Consequences
- Travel stays at minutes: 25 to 40 s between clusters, 3.5 minutes across the field, 2 to 3.5 minutes to a planet, 3 s for a jump.
- The bots never leave the working area and never cruise: the bands (Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56 minutes) are expected not to move and are re-measured in stage 2.
- Cost: about 2 to 3 weeks in three pull requests; the `long` refactor and the governor are the risks, checked by limit tests, golden checksums and a property test of random cruise flights.
- Open: what planets offer, whether a jump needs a gate structure, 3.3 percent of the speed of light as fiction.

## Alternatives considered
- **P9, the whole world 1,000 times larger**: rewrites ships and economy; rejected by the units design.
- **Per-system local frames with `int` positions**: cannot hold millions of kilometres (8.9 Mm) and splits stations from the field.
- **Light-year distances in one frame**: 4 ly is the whole `long` range at 1/240 m.
- **Faster thrusters instead of a drive**: no safe way to cross rocks at km/s; the governor is the drive.
- **Double-precision Godot build**: not available in the Mono templates; the floating origin is cheaper and is needed by the double build as well.

## Stage 2 slice (built)
The owner approved a first slice of stage 2: the distances and the drive between systems and rock clusters. It is behind `WorldRules.LegacyWorldScale` (third flags byte, bit 5, value 32) and log version 30, and the content hash moves only when the `world.scale` block is present.
- **Content**: `world.scale` in `content/core.json`: systems 100 km apart, cluster super-sector 100 km, no background rocks, ore distance thresholds times 50 (125, 175 and 250 km), a rock field only within 600 km of a system anchor, nav range 150 km (37,500 percent of the 400 m base) and the `cruise` block (3 km/s, about 12 s spin-up, about 6 s braking, drop-out below 960 units per tick, zone margin of 2 sectors). The research `core:cruise_drive` (needs electronics and the jump drive) unlocks the ability.
- **Command**: `SetCruise(bool On)`, log tag 73, refused in logs older than version 30. Outside a world with `world.scale` or without flight inertia it is `NotAllowedHere`; `Locked` in the campaign without the research; `Docked`, `Busy` (a jump is charging), `NoCockpitOrEngine` as for the boost; `NotAllowedHere` inside a zone. Releasing is always accepted.
- **Governor** (position only, integer): the allowed speed is the lesser of the cruise speed and `ISqrt(2 * 0.7 * brake * distance to the edge of the nearest zone)`. A zone is a system (its 320 m radius) or a cluster (its reach plus 2 field sectors). Cruise drops out when the allowed speed is under the drop-out speed or the power is short. A speed above both the engines and the allowed speed is shed at the braking rate, so no flight reaches a zone fast.
- **Display**: nav and HUD in km and km/s (the units layer already scales); the nav list shows clusters in range (computed from the seed, not hashed); the local scan keeps its 80 and 300 m. The cruise key is `N` in flight.

Deviations from the sections above, to be revisited with the rest of stage 2:
- The governor is isotropic and position-only (the time-to-contact variant with the ship's velocity is deferred), a single level of research and a boolean command.
- Planets, structures and obstacles are not zones yet, and rock size, sector size and cluster size are unchanged (so a cluster at this spacing is the same few kilometres across, not 6 to 14 km).
- There is no system bubble, no light-year display, no system cruise and no `long` refactor: positions stay `int` units and the field is bounded to 600 km, which fits.
- Measured: a cluster 75 km away is reached in about 36 s at cruise (about 13 minutes at boost speed of 96 m/s).
- Bots: Relaxed, Normal and Hard on seeds 20261121, 20261120 and 777 are within 6 s of the legacy-scale runs (the bots do not cruise and never leave the working area); every run is inside its band.
