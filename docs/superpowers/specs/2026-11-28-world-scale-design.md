# World scale: realistic distances, compressed travel time (design)

Status: **decided; stage 0 (this design), stage 1 (floating origin) and a first slice of stage 2 (the distances, the cruise drive and nav in km, see the ADR section "Stage 2 slice") are built; the remaining stage 2 items are deferred** (2026-11-28). Everything marked *measured* was read from the code or the content today; *proposal* is our choice; *TBD* is unknown. Nothing here is copied from another game.

The owner's request, delegated to us: "set the equivalence of the cells as you see fit, but make the **distances** more realistic". The [physical units design](2026-11-28-physical-units-design.md) fixed **1 cell = 1 m** for everything the player touches (ships, modules, doors, bodies, tools) and showed that the *spacing* is the part that is off: systems 1.8 km apart, rock fields in 200 m sectors and 800 m super-sectors, rocks 3 to 24 m, scan and nav ranges in hundreds of metres. That document rejected option P9 (the whole world 1,000 times larger) for its cost. This one chooses a different way to the same goal.

## 1. Decisions in one table

| # | Decision | Why |
| --- | --- | --- |
| 1 | **1 cell = 1 m stays, and so does everything built at local scale**: ships, modules, stations, machines, belts, docking, mining and weapon ranges, hand reach, the 320 m system working area with its veins and hazards. | The economy near the systems, the bots and every balance number depend on it. Changing it would rewrite the game (P9). |
| 2 | **Only spacing and the size of rocks change**, and travel time stays at minutes by adding a **cruise drive**: a drive mode with spool-up, a speed readout and an automatic approach governor. Distance over speed is still time, with honest speeds (km/s and Mm/s). | The units design's objection to "a drive in km/s" was a world that is 1,000 times too small for it. A world that really is that large, with spacing the drive crosses in minutes, has no such contradiction. |
| 3 | **Four distance regimes**, each with its own unit in the display: *local* (m), *field* (km), *system* (Mm, and Gm), *strategic* (ly). Section 2 has the numbers. | One number does not read well from 3 m to 9 light-years; the regimes match what the player does in each. |
| 4 | **Rocks**: radius from tens of metres to a few kilometres (small 33 to 52 m, medium 86 to 129 m, large up to about 310 m, giants 0.8 to 1.6 km). **Ore per rock is unchanged**: a rock holds the same amount of ore as before, as a vein on a much larger body (the mass shown by the units layer is the body, the ore is a share of it). | Realistic size, the same economy. Making the ore follow the volume (x 15,000 or more) would end the game's scarcity. |
| 5 | **Clusters** (ADR 0046) keep their shape and their rock counts and are scaled: sector 10 km, cluster radius 6 to 14 km, super-sector 100 km (a cluster every 54 km median), loaded out to 40 km. **The field lives around each system anchor out to 600 km** and nowhere else. | Tens to hundreds of kilometres between clusters, as asked; a bounded field keeps streaming and balance as they are. |
| 6 | **Systems are bubbles in one shared space**, 8 Gm apart in the frame, each flyable out to **1.5 Gm (1.5 million km)** from its anchor. Beyond the bubble the drive stops ("heliopause"). **Between systems you do not fly: you jump**, and the jump is the strategic hop, shown in **light years** (a display value derived from the frame distance, 1 grid step = 3 ly, so neighbours are about 3 ly apart, the real nearest star is 4.2 ly). | A shared i64 frame cannot hold light-year distances at 1/240 m resolution (9.2e18 units is 3.8e16 m, 4 ly). The bubble is the largest region that the integer maths allows with squares in metres (section 5). |
| 7 | **Bodies inside a system**: 0 to 3 planets and moons per system, generated from the system seed, at 0.25 to 1.2 Gm from the anchor, radius 1.5 to 40 Mm. They are nav targets and **danger zones**, not mining targets (what they offer is *TBD*, not in stage 2). The sun stays the sky disc (no body, no collision); only its distance in the nav list is a display value (0.1 to 0.3 AU, *proposal*). | "In-system body distances in millions of km" is met by the planets; the sky stays cheap and camera-relative. |
| 8 | **Cruise drive** (new ability `cruise`, unlocked by research, two levels): **field cruise up to 3 km/s**, **system cruise up to 10 Mm/s** (3.3 percent of the speed of light, a fiction we state). Spool-up doubles the speed every second from 200 m/s, brake halves it every 0.5 s. | Minutes for every trip of section 3. |
| 9 | **Safety, no hull collision at cruise**: a **time-to-contact governor** limits the cruise speed so that the time to reach any solid surface (rock, structure, planet, obstacle) along the velocity is at least **5 s in the field and 8 s in a system**, with a 100 m margin; below 120 m/s of allowed speed the drive drops out to normal flight. The danger zone is shown on the HUD and the nav list. | A governor derived from the same distances as the world: the approach to a planet from 1 Gm is an exponential braking of about 2 minutes ending in manoeuvring speed. |
| 10 | **Scan and nav ranges scale; docking, mining and weapon ranges do not.** Scan 80 m becomes 10 km (wide 300 m: 40 km), nav base 400 m becomes 150 km. | The scanner finds a cluster, the laser works at the rock. |
| 11 | **Behind a flag**: `WorldRules.LegacyWorldScale` (third flags byte, bit 5, value 32) and **log version 30**; **ADR 0053**. All older logs read with it on and replay byte for byte. | The standing pattern of ADR 0023. |
| 12 | **The client uses a floating origin** (stage 1, no simulation change) because the view math is float32. | Section 6. |

## 2. The regimes and every number

*Measured* = today's value. New values are a *proposal*, all in a new `world.scale` content block (metres) so tuning is data, validated like the rest.

| Quantity | Today | New | Factor | Regime |
| --- | --- | --- | --- | --- |
| Ship, module, station, belts, machines | 40 m, 8 x 8 x 4 m | unchanged | 1 | local |
| Docking range, mining range, weapon and hand reach | 12, 10 to 14, 6 | unchanged | 1 | local |
| System working area (anchor, veins, hazards) | 320 m radius | unchanged | 1 | local |
| Rock radius, small / medium / large | 2.2 to 7.8 m (median 3.2) | **15 / 25 / 40 times** the base law: 33 to 52, 86 to 129, 206 to 313 m | 15 to 40 | field |
| Giant rock radius | up to 16 m | **100 times**: 0.8 to 1.6 km | 100 | field |
| Sector, load radius, keep radius | 200, 360, 480 m | **10, 40, 60 km** | 50 to 125 | field |
| Field exclusion around an anchor | 470 m | 25 km | 53 | field |
| Cluster radius | 120 to 280 m | **6 to 14 km** (4,000 + 2,000 x richness) | 50 | field |
| Super-sector (cluster spacing) | 800 m | **100 km** | 125 | field |
| Rock margins in the sector (regular, giant) | 14, 30 m | 700 m, 3,000 m | 50 to 100 | field |
| Ore threshold distances (titanium, nickel, quartz) | 2.5, 3.5, 5 km | 125, 175, 250 km | 50 | field |
| Field limit around an anchor | 4,000 km from the origin | 600 km | | field |
| Scan, wide scan | 80, 300 m | 10, 40 km | 125, 133 | field |
| Nav base, long scanner, comms antenna | 400, 520, 700 m | 150, 195, 262 km | 375 | field |
| Cluster far glow | 2,800 m | 400 km | 143 | field |
| Planet and moon distance, radius | none | 0.25 to 1.2 Gm, 1.5 to 40 Mm | new | system |
| System bubble radius | 320 m | 1.5 Gm | | system |
| System spacing in the frame | 1,800 m | 8 Gm | | system |
| Distance between systems (display) | 1.8 km | about 3 ly (1.5 to 4.5) | | strategic |
| Thrusters, boost | 6 to 24, up to 96 m/s | unchanged | 1 | local |
| Field cruise | none | up to 3 km/s | new | field |
| System cruise | none | up to 10 Mm/s | new | system |
| Jump charge | 3 s | unchanged | 1 | strategic |

**Rock counts per cluster and rocks per sector do not change** (24 to 70 core rocks per sector, up to 120 candidates, at most 80 rocks); only the geometry is scaled, so the cost of generating a sector stays at the 15 microseconds measured for ADR 0046 and a cluster still holds 13 to 200 or more rocks. The density per cubic kilometre falls by the cube of the scale, which is the point.

## 3. Travel times (the check that "realistic" is still "playable")

At the proposal's numbers, straight flights, including spool-up and the approach braking of the governor:

| Trip | Distance | Time |
| --- | --- | --- |
| Leave the station until the governor allows cruise | 0.7 to 2 km | 7 to 20 s of boost, then 4 s of spool-up |
| Home station to the nearest cluster | about 25 to 60 km | 20 to 30 s at 3 km/s |
| Cluster to cluster (median, longest neighbour) | 54, 100 km | 25, 40 s |
| Across the whole field (the longest trip for ore) | 600 km | 3.5 min |
| Station to a planet, nearest and farthest | 0.25, 1.2 Gm | 2 min, 3.5 min (spool 16 s, cruise at 10 Mm/s, exponential approach of 2 min) |
| Planet to planet across the bubble | 2.4 Gm | 6 min |
| System to system | about 3 ly | the jump: 3 s charge, then the arrival at the anchor |
| Cross a 1 km giant at boost | 2 km | 21 s |

The 66 percent of empty sectors of ADR 0046 and the cluster spacing mean that an ore run of the new world takes about as many minutes as an ore run of the old one did at boost (the study counted 18 s median at 24 m/s boosted for 430 m). Titanium at 125 km is 42 s at field cruise.

## 4. What the cruise drive is (rules)

*Proposal; the numbers are content, the rules are simulation and therefore need the flag and the log version.*

- **Ability and levels.** The research ability `cruise` has two levels: 1 unlocks field cruise (3 km/s), 2 system cruise (10 Mm/s). Sandbox worlds have both. The key is a toggle (default `C`), a new command **SetCruise** (log tag 73) with a level request, so replays carry it.
- **Spool-up.** While engaged, the target speed is `200 m/s x 2^(seconds engaged)` up to the level cap; the ship accelerates to it by the existing flight-assist governor (ADR 0033), whose limits are lifted in cruise by the same factor. Disengaging brakes by halving every 0.5 s to the thruster speed.
- **Governor.** Every tick the simulation computes the smallest **time to contact** along the velocity against every solid within `speed x tau + radius` (loaded rocks, structures and their hit volumes, obstacles, planets): spheres and boxes the simulation already owns. The allowed speed is `distance to the surface minus 100 m, divided by tau` (5 s field, 8 s system). The target speed is clamped to it; below 120 m/s the drive drops out. With the load radius at 40 km and a field cap of 3 km/s (15 km at tau 5 s) nothing the governor needs is unloaded.
- **Not in cruise**: docking, mining, building, firing, jumping, walking. Engaging cancels a mining order; a jump charge or a dock refuses cruise.
- **Hull and rocks**: no hull collision is possible at cruise speed by construction. Collision damage keeps its rule at thruster speeds. A one-tick step at 3 km/s is 100 m, so a rock of 30 m radius could be passed through without the governor, which is why the governor is the rule and not a feature.
- **Edge of a bubble**: the outward velocity is cancelled at 1.5 Gm and the HUD says "Heliopause: use the jump drive".
- **The bots never use it**: they stay in their systems' working areas (320 m) and jump between systems. Nothing in their path changes.

## 5. Integer range (the sim is integer, ADR 0002)

*Measured:* positions of the ship, enemies, pickups, projectiles are `int` in units of 1/240 m (`BeltNetwork.Player.X/Y/Z`, `Feel.cs` projectiles and pickups), deposits and system anchors are `int` cells (`Deposit.X/Y/Z`, `StarSystem`), structure origins are already `long`; `SimWorld.MaxCoord = 10,000` is the *station grid* and is untouched.

| Value | Size | Verdict |
| --- | --- | --- |
| `int` units reach | 2.1e9 units = 8.9 Mm | too small for 1.5 Gm: **positions must become `long`** |
| System bubble 1.5 Gm | 3.6e11 units | `long` |
| Frame extent (grid +-4 x 8 Gm + jitter) | 3.2e10 m = 7.7e12 units | `long`; exact as a `double` too (< 2^53 = 9e15) |
| Distance squared inside a bubble, **in metres** | up to 3 x (1.5e9)^2 = 6.75e18 | fits `long` (9.22e18) with 27 percent of room; **squares are taken in metres, not in units** (units would need 3.9e23) |
| Distance squared between systems | up to 1e21 | `Int128`, only in the jump list and the strategic display |
| Cruise speed 10 Mm/s | 8e7 units per tick, 8e10 in the milli-units of `Vel*` | velocities become `long` |
| Sector coordinates, cluster hash | 3.2e6 per axis | `int` is enough; the hash inputs stay `int` |
| `IntMath.ISqrt(long)` | up to 9.2e18 | exists; add a test at the limit |
| Unit-to-view conversion in the client | | `long` subtraction first, then `float` (section 6) |

A generator does not need the whole range: the field is a pure function of the sector coordinates and the seed, so the bubble radius only decides which sectors are asked for. **Effort L for the `long` refactor** (about 40 members and every place that multiplies them), which we recommend as a separate step with **no flag**: it changes no value in any existing world, and every log must still replay byte for byte (the golden checksums prove it).

## 6. Float precision in the client: the floating origin is required

*Measured:* the client converts to float32 at `new Vector3(x, y, z) / CellUnits` (43 sites), `ShipPosition` (33 call sites, 7 in `Play.cs`), `DepositPosition` (34, across 17 files), `CellUnits` appears 93 times in 29 files, and `GlobalPosition`/`GlobalTransform` 56 times in 22 files. The spacing of a float32 near a distance `d` is `d x 2^-23` (the study of 2026-11-27: 0.06 cells at 1,000,000 cells):

| Distance from the origin | float spacing | at the new scale |
| --- | --- | --- |
| 65 km | 0.004 m | invisible |
| 600 km (the field's edge) | 0.06 m | visible shimmer of rocks against the hull |
| 1.5 Gm (the bubble's edge) | 128 m | unplayable |

So the world **must** be viewed relative to a moving origin. Design (built in stage 1, with no simulation change):

- **`ViewOrigin`** (engine-free, in `Starfoundry.Ui`): a `long` origin in cells, snapped to a multiple of 1,000. `ToView(long units)` subtracts in 64-bit and only then converts to float, so a view value is exact. `NeedsRebase(shipView)` is true when the ship is more than 20,000 cells from the origin; `RebaseTo(shipCells)` returns the exact integer delta.
- **One helper at every site.** The 43 `new Vector3(..) / CellUnits` sites, `ShipPosition`, `DepositPosition`, the obstacle, structure, enemy, projectile, pickup, marker, drone and effect sites go through `View` (`View.Cells(x, y, z)` for units, `View.FromCells` for cell values, and `View.ToUnits(Vector3)` back to the simulation for the aim, the tractor and the build tool).
- **A rebase** runs between frames: every long-lived node placed in world space is in the group `world_root_space` (obstacle nodes, structure roots, rock sector batches, cluster glow, deposit views, the ship) and is shifted by the exact integer delta (exact in float at these magnitudes); every cached world `Vector3` field is listed and shifted too (section in the stage 1 notes). Camera-relative things (the sky, dust, markers projected on screen) are untouched.
- **The simulation, the command log and the physics do not know the origin.** A debug flag (`--view-origin x,y,z` in cells) starts the view with a given origin and teleports a sandbox ship there, to measure the jitter of a flat hull line before and after at 0, 100 km, 10,000 km and 1e9 m.
- Godot's double-precision build is **not** used (*measured*: the Mono release editor and the export templates are single-precision; a custom build is a CI commitment we do not take on).

## 7. The generator at the new scale (checks)

- **Streaming**: load 40 km over a 10 km sector is 4 sectors of radius, 268 sectors in the sphere, about 90 of them non-empty (66 percent are empty, ADR 0046), so about 380 rocks loaded on average (up to about 2,000 inside a rich cluster) against 4,000 allowed; the keep radius of 60 km holds about 900 sectors. Moving at 3 km/s crosses a new shell of about 15 sectors per second: 0.2 ms per second of generation. Loading the first sphere costs about 4 ms.
- **Rendering**: the field's far plane is 500 m today; rocks of 30 m to 1.6 km seen from kilometres need a far plane of 100 km or more. Godot 4 uses a reversed-Z depth buffer, so near 0.05 and far 100,000 is precise enough (*TBD, measure*); rocks get two more mesh tiers by distance and the cluster glow stays the impostor beyond them. **Effort M.**
- **Giants**: a 1.6 km radius needs the sector margin of 3 km, so the giant's centre is confined to the middle 4 km of a 10 km sector. Validated in content like the other margins.
- **Mining**: the laser and the hand add the hit radius of the rock (ADR 0046), so a 1 km giant is mined from its surface at the same 10 m as a boulder; the ship stops at the hit surface (ADR 0033).
- **Collisions with big rocks**: unchanged rules; the governor keeps cruise away from them.
- **Hashing**: unchanged inputs; no hash change for the legacy path, and the new path uses the same functions on scaled sector coordinates.

## 8. Balance, bots and replays

- **The bots do not change.** They never leave the 320 m working area of their systems, never mine rocks and never cruise. `CurrentSystem` uses the bubble radius in the new scale (larger than the working area), which is true wherever the bots are. The bands stay (Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56 minutes on seeds 20261121, 20261120 and 777); stage 2 measures them with and without the flag and expects the same minute, as ADR 0046 did.
- **Legacy**: `LegacyWorldScale` on reproduces today's content values, today's generator and the 320 m system radius; logs of versions 5 to 29 read with it on. A test records a flight, rewrites the header to each of 26 to 29 and compares the checksum (the idiom of ADR 0046); the golden checksums of the existing replay tests must not change.
- **Content hash**: the `world.scale` block joins the hash only when present, so legacy content hashes do not move.
- **The new-game screen** gains no option: new worlds are scaled, old worlds replay.

## 9. Effort and risk per piece

| Piece | Effort | Risk |
| --- | --- | --- |
| Floating origin: helper, 43 + 33 + 34 sites, group shift, cached fields, debug flag, measurements (**stage 1**) | **M** to **L** | a missed cached `Vector3` jumps after a rebase; mitigated by a debug assertion that no node is farther than 50,000 cells and a forced rebase test |
| `long` positions and velocities in the simulation, `Int128` for strategic distances, metre-squares | **L** | silent overflow; mitigated by limit tests and the golden checksums (no flag) |
| `world.scale` content, validation, generator scaling (rocks, clusters, margins, exclusion, field limit around anchors, scan/nav) | **M** | content ranges and the giant margins; the field study tool (`Starfoundry.FieldStudy`) re-measures |
| Cruise drive: ability, `SetCruise` (tag 73), spool, time-to-contact governor, edge of the bubble | **L** | the governor must be exact and cheap; property test: no random flight at cruise ever overlaps a solid |
| System bodies (planets, moons), nav, danger zones | **M** | purely additive and generated after the existing draws with its own stream, so existing generation does not move |
| Strategic distance display (ly), jump list, 3D star map and nav display scale (logarithmic zoom) | **M** | the map code is not tested engine-free |
| Units display: Gm, AU, ly, km/s, Mm/s, percent of c (the `Units` module of ADR 0052) | **S** | conflicts with the open display PR; we add branches, not rewrites |
| Client rendering: far plane, rock tiers, impostors, cruise effects (speed streaks, a field of view widening), HUD readout and danger zone | **M** to **L** | looks; measured with the `--field-perf` harness |
| Bot, tests, bands report, ADR 0053, docs, es + en texts | **M** | none for the bands (bots stay local) |

**Total: about 2 to 3 weeks of work in three pull requests** (floating origin; `long` refactor with no flag; the scale, the drive and the display). Stage 2 of this task is the last two together unless the pull request becomes unreviewable, in which case it is split at the `long` refactor.

## 10. Not decided here (owner)

The units design's pending decisions (item masses, the power unit, the walking speed) are untouched. New: whether planets should ever offer something (ice, gas, a base), whether the jump should need a gate structure (we propose none: the jump drive is the gate), and whether 3.3 percent of the speed of light for system cruise is acceptable fiction (the alternative is 3 Mm/s and a 3 minute trip longer).
