# Resource fields: large voids and dense clusters of rocks (study and proposal)

Status: study and proposal, 2026-11-27. Nothing here is implemented in the product. The numbers marked *measured* come from a throwaway program, [`tools/Starfoundry.FieldStudy`](../../../tools/Starfoundry.FieldStudy/Program.cs) (not part of the solution, not shipped), run on this PC with the shipped generator and the shipped `content/core.json`. Everything under "Proposal" is a *proposal*; what we have not measured is marked *TBD*.

Owner request: much more gap between resources, many more concentrations (clusters, belts, fields of asteroids), and bigger asteroids: "today there are very few asteroids and the sizes are very small".

## 1. Short answer
- The complaint is correct and the numbers show why. The shipped field ([ADR 0040](../../adr/0040-infinite-rock-field-generated-on-demand.md)) is a uniform scatter: about 35 rocks inside the 300-cell scanner sphere everywhere, a rock about every 79 cells, **no rock has 8 neighbours within 100 cells**, and the median rock is 1.2 cells in radius (2.4 cells wide, a third of one ship module, which is 8 cells). Nothing is a place; everything is a speckle.
- Proposal: replace the per-sector uniform draw by **cluster parents decided per super-sector** (a Poisson-cluster process), each with a centre, a radius, a richness, a dominant ore and a shape (sphere, flat belt, hollow shell). A sector's rocks are the thinned sum of the parents that reach it. Between clusters there is a void with only a few strays. Cluster rocks come in size classes with a **rare giant** class; the rock radius is scaled up by 2.5.
- Measured on the prototype (candidate D, super-sector 800 cells): 68% of sectors empty (today 17%), 80% of the ore in 3.7% of the sectors (today 38%), a typical cluster 26 rocks and about 4,000 ore, the top 5% of clusters 90 to 650 rocks and 30,000 to 150,000 ore, the largest single rock 12,000 ore (today 851), median rock radius 3.2 cells (today 1.2), the next concentration a median 350 to 430 cells away (today there is none), generation 4.5 microseconds per sector, at most 744 rocks loaded at once (cap 1500).
- It needs a new log version (26) and ADR 0046 with a `WorldRules` legacy flag, because the rocks of every field world change. The early game and the bots do not change (rocks stay 470 cells from every system, and the bots never mine rocks); this must be confirmed with the balance tool once the generator exists (*TBD*, see section 8).
- Rendering needs work before clusters of 200 or more rocks are comfortable: one shared mesh per rock variant drawn with MultiMesh per sector (today two nodes and two draw calls per rock, and one freshly built mesh per slot id), two mesh tiers by distance, a far glow impostor for distant clusters, and a floating origin before the field is explored past about 50,000 cells (section 6).

## 2. How the shipped field behaves (measured)
Method: the real `SectorField.Generate` at the Normal density (100) with the shipped numbers. "Far region" is 40 by 6 by 40 sectors (9,600 sectors, 77 billion cubic cells) centred 12,000 to 20,000 cells from home, past every system, where all six ores are allowed. "Around home" uses 300 random directions at each distance, one scan sphere (300 cells, the wide scanner) and the navigation display (400 cells) at each point. The home galaxy has 12 systems spread over about 7,400 by 7,400 cells and almost flat (y within 70 cells), with 48 veins holding 66,367 ore in total.

| Measure (current generator) | Value |
| --- | --- |
| Rocks per sector | mean 2.50, p50 3, max 5; 16.9% of sectors empty |
| Ore per rock | mean 119; largest 851 |
| Rock radius in cells | p10 0.9, p50 1.2, p90 2.3, max 2.6 |
| Nearest-neighbour distance (cells) | p10 42, p50 79, p90 120 |
| Rocks with 8 or more neighbours within 100 cells | 0% (mean neighbours 1.3) |
| Share of the ore in the richest sectors | 80% of the ore in 38% of the sectors |
| Top rocks | the top 1% of rocks hold 6% of the ore; 4,000 ore per scan sphere |
| Ore per billion cubic cells | 37,090 |
| Generation | 4.4 microseconds per sector (p99 6.5) |

| Around home, scan sphere of 300 cells | rocks mean (p10 / p50 / p90 / max) | scan ore mean | nearest rock p50 / p90 (cells) |
| --- | --- | --- | --- |
| At home start (0 cells) | 0 (the 470-cell exclusion) | 0 | 470 |
| 500 cells | 30.5 (23 / 30 / 39 / 48) | 3,951 | 71 / 118 |
| 1,500 cells | 34.0 (23 / 35 / 44 / 51) | 4,172 | 85 / 137 |
| 5,000 cells | 35.6 (26 / 36 / 43 / 56) | 4,264 | 83 / 118 |

Reading: at every distance you see the same 30 to 36 rocks; the spread is tiny (p10 23, p90 44). There is no reason to choose a direction, nothing to find, nothing to come back to. The streaming load is steady: 167 rocks loaded (p99 213, cap 1,500) and 67 sectors in the load sphere.

Visible size (1080p, vertical field of view 80 degrees, the cockpit camera; focal length about 644 pixels; pixel diameter = 2 r / d x 644; the camera far plane is 500 cells):

| Rock radius (cells) | at 50 cells | 100 | 200 | 300 | 500 |
| --- | --- | --- | --- | --- | --- |
| 1.2 (median now) | 31 px | 15 | 8 | 5 | 3 |
| 3.2 (proposed median) | 82 | 41 | 21 | 14 | 8 |
| 5.0 | 129 | 64 | 32 | 22 | 13 |
| 8.0 | 206 | 103 | 52 | 34 | 21 |
| 16 (proposed giant) | 412 | 206 | 103 | 69 | 41 |

A current median rock is a 5-pixel dot at the scanner's edge: "very small" is literally true. At 720p all sizes are two thirds of the table.

## 3. Candidates prototyped
All candidates are pure functions of the world seed and the sector (or super-sector) coordinates, with integers only (a SplitMix64 hash per cell, integer value noise, integer square roots), and each was checked for order independence (200 random sectors, two orders, fresh generator: same checksum for A to E). They share the rules of ADR 0040 that must not change: 200-cell sectors, rocks 470 cells from system anchors, 3 cells between rock surfaces, world limit, the item distance table.

- **A. Current.** Baseline, the shipped generator.
- **B. Noise-thinned.** The same rocks, but each candidate is kept with a probability from two octaves of 3D value noise (lattice 700 and 260 cells, threshold 56%). Voids and ridges, no cluster identity, no per-cluster ore, ore domain per 1,500-cell lattice cell.
- **C. Basic clusters.** Cluster parents in super-sectors of 800 cells (0, 1 or 2 per super-sector with 30/50/20%, a mean of 0.9 per 800 cubed, one per 1,213 million cubic cells in the measurement). Each has a radius (80 + 40 x richness cells, times 0.8 to 1.2), richness 1 to 5 (weights 35/30/20/10/5), a dominant ore (the shipped distance table at the centre) and a flat-top falloff. A sector draws up to 120 candidate points and keeps each with the probability `sum(lambda_i x falloff_i(p)) / M` (thinning of a Poisson process, so the density follows the clusters exactly), plus a background of 0.25 rocks per sector. Rock mass and density as today. Dominant ore for 75% of a cluster's rocks.
- **D. Full clusters (the recommendation).** C plus: three shapes (50% sphere, 30% flat disc or belt four times thinner on one axis and 1.5 times wider, 20% hollow shell), a core density that rises with richness (24, 32, 44, 56, 70 rocks per sector at the core), size classes (small 300 to 1,200 mass (about 65% of the rocks), medium 1,200 to 4,000 (28%), large 4,000 to 14,000 (5 to 9%); richer clusters hold more large rocks), **giants** (mass 14,000 to 120,000, drawn first so they claim their room, 0.8% of the rock rate of the cluster), **rock radius scaled to 250%**, and density tied to richness (50 + 25 x richness, plus or minus 20).
- **E. Full clusters, sparser.** D with super-sectors of 1,300 cells and radii 100 + 55 x richness: the same shapes in a lonelier universe.

### Results (far region unless stated)
| | A current | B noise | C basic | D full 800 | E full 1300 |
| --- | --- | --- | --- | --- | --- |
| Rocks per sector, mean / max | 2.50 / 5 | 0.50 / 12 | 1.40 / 70 | 1.38 / 66 | 0.68 / 62 |
| Empty sectors | 16.9% | 71.0% | 68.2% | 67.8% | 81.0% |
| 80% of the ore lies in | 38.1% of sectors | 9.8% | 6.1% | 3.7% | 1.9% |
| Ore per billion cubic cells | 37,090 | 8,931 | 21,375 | 27,559 | 13,350 |
| Mean ore per rock / largest | 119 / 851 | 143 / 839 | 122 / 843 | 160 / 12,471 | 157 / 12,346 |
| Top 1% of rocks hold | 6% | 5% | 6% | 23% | 22% |
| Radius cells p50 / p99 / max | 1.2 / 2.6 / 2.6 | same | same | 3.2 / 7.6 / 16.0 | 3.2 / 7.7 / 16.0 |
| Nearest neighbour p10 / p50 / p90 (cells) | 42 / 79 / 120 | 47 / 101 / over 200 | 17 / 36 / 183 | 21 / 37 / 179 | 21 / 38 / over 200 |
| Rocks in a concentration (8 or more within 100 cells) | 0% | 0% | 70% | 68% | 64% |
| Generation, microseconds per sector (p99) | 4.4 (6.5) | 16.9 (25.3) | 4.5 (40.4) | 4.5 (36.7) | 2.2 (23.2) |

Around home (scan sphere 300 cells, rocks mean / p50 / p90 / max; distance to the nearest concentration of 8 or more neighbours, median / p90, search radius 1,500):

| | 500 cells | 1,500 cells | 5,000 cells | nearest concentration at 500 / 1,500 / 5,000 |
| --- | --- | --- | --- | --- |
| A current | 30.5 / 30 / 39 / 48 | 34.0 / 35 / 44 / 51 | 35.6 / 36 / 43 / 56 | none within 1,500 for 92 to 100% of the points |
| B noise | 5.1 / 3 / 14 / 28 | 6.8 / 4 / 16 / 66 | 6.5 / 4 / 17 / 41 | 1,245 / 976 / 1,093 (about half none) |
| C basic | 5.2 / 3 / 13 / 41 | 26.7 / 6 / 57 / 610 | 19.5 / 5 / 56 / 286 | 443 / 359 / 357 |
| D full 800 | 4.7 / 3 / 10 / 25 | 25.4 / 6 / 49 / 582 | 19.7 / 5 / 52 / 268 | 432 / 392 / 352 |
| E full 1300 | 3.3 / 1 / 7 / 40 | 6.3 / 2 / 12 / 199 | 9.6 / 3 / 22 / 368 | 664 / 587 / 700 |

At the home start every candidate gives 0 rocks in the scan, as today (anchor exclusion). With D, a pilot who leaves the home zone finds a median of 192 cells to the nearest rock and 432 cells to the nearest concentration; at the start ship's 6 cells per second (24 with boost, *measured* from `ShipSpeedUnitsPerTick`) that is about 70 seconds or 18 seconds boosted, E about 110 or 28 seconds. The distance between concentrations is the owner's knob (section 8).

Streaming (1,500 random ship positions, load rule of ADR 0040: sectors within 360 cells, 67 sectors):

| | rocks loaded mean / p50 / p90 / p99 / max | inside the keep sphere (480) p99 / max | generation per sector |
| --- | --- | --- | --- |
| A current | 167 / 166 / 189 / 213 / 228 | 373 / 387 | 0.7 microseconds |
| C basic | 92 / 50 / 235 / 492 / 803 | 688 / 961 | 0.8 |
| D full 800 | 90 / 51 / 218 / 476 / 744 | 678 / 940 | 0.9 |
| E full 1300 | 49 / 15 / 120 / 495 / 1,434 | 796 / 1,942 | 0.3 |

D is bounded by construction: the cluster's total rock count follows its richness and radius (12 rocks for richness 1 up to 460 for richness 5), not a free density, and a sector never holds more than 66 in the measurement. E's richest clusters (radius up to 430 cells, 1,000 to 2,000 rocks) reach the cap of 1,500 (keep sphere max 1,942), so a larger `max_loaded` (2,500 proposed) and a per-sector cap are part of the proposal. A teleport or jump loading all 67 sectors costs well under a millisecond of generation.

The simulation's own cost per loaded rock is small *(measured on the shipped generator with content edited to 0, 5, 15, 40 and 60 rocks per sector, ship parked at 15,000 cells, mean of 600 ticks)*:

| Rocks loaded | 0 | 214 | 686 | 1,494 | 1,492 (60 per sector) |
| --- | --- | --- | --- | --- | --- |
| ms per tick | 0.010 | 0.011 | 0.013 | 0.021 | 0.048 (p99 0.571) |

The budget is 33.3 ms per tick, so even 1,500 rocks parked costs 0.2% of it. Collision and mining while moving through a dense field were not measured here (*TBD*; the collision loop is over deposits and a hit volume per rock, so it should scale the same way).

### Per-cluster ore, candidate D (183 clusters in 222 billion cubic cells, one cluster per 1,213 million, a mean spacing of about 1,066 cells)
| Richness | clusters | radius cells p50 | rocks p50 / max | ore p50 / p90 / max | giants per cluster | biggest rock ore p50 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 66 | 132 | 12 / 28 | 948 / 1,757 / 7,105 | 0.1 | 207 |
| 2 | 52 | 172 | 40 / 92 | 3,980 / 8,327 / 28,713 | 0.2 | 644 |
| 3 | 40 | 241 | 95 / 163 | 14,763 / 21,481 / 31,983 | 0.8 | 1,347 |
| 4 | 17 | 267 | 181 / 488 | 32,929 / 88,604 / 92,544 | 2.1 | 5,198 |
| 5 | 8 | 267 | 461 / 654 | 102,923 / 148,665 / 148,665 | 4.0 | 11,383 |
| all | 183 | | 38 / 654 | 3,980 / 30,270 / 148,665 | | |

For scale, the whole home galaxy's veins hold 66,367 ore, and one scan sphere of the shipped field holds about 4,000. So a median cluster is about one scan sphere of today's field (but in one place), and a richness 5 cluster is two to three home galaxies. Whether that is right depends on how fast a base can consume ore, which the balance tool can answer for the late game (*TBD*); `unit_amount`-style scaling (section 8) makes it one number.
The dominant ore follows the shipped distance table at the cluster centre: iron 80, carbon 34, ice 22, titanium 21, nickel 18, quartz 8 of the 183 clusters; far from home every ore is allowed, so titanium, nickel and quartz clusters exist (a field of quartz 5,000 cells from home is a destination).

## 4. Proposal (candidate D, to be tuned)
### 4.1 Structure
- **Super-sector**: a cube of `super_cells` (proposed 1,000; the study measured 800 and 1,300). Its parents are a pure function of the world seed and the three super-sector coordinates: 0, 1 or 2 parents (30/50/20%), each with centre (uniform inside the super-sector), richness, radius, shape, axis, dominant ore. The id of a parent is (super-sector, index), so every rock can name its cluster.
- **Sector rocks**: for each 200-cell sector, collect the parents of the neighbouring super-sectors whose bounding shape reaches the sector, sort them by id, draw `M = ceil(sum of core rates + background)` candidates (cap 120) and keep each with probability `lambda(p) / M`, where `lambda(p) = background + sum_i core_i x falloff_i(p)`. The falloff is flat-top: 1 out to 0.58 of the radius, then 0 at the edge for a sphere; a disc flattens one axis 4 times; a shell peaks at 0.7 of the radius. All in permille with integer square roots.
- **Giants first**: up to two giant candidates per sector, kept 30 cells from the faces so their reach (up to 26 cells at mass 120,000 with the 250% radius) never leaves the sector; they are placed first so small rocks avoid them. Regular rocks keep a 14-cell margin (their reach at mass 14,000 is about 13). So the rule of ADR 0040 "rocks never overlap across a sector face" holds with a per-class margin.
- **Size**: radius = shipped cube-root rule (`RadiusOfMass`) times `radius_percent` (250). Mass classes per the prototype. The amount is still `mass x density x unit_amount / 100000`, so a giant of mass 120,000 at density 175 holds about 12,000 ore (200 reference rocks). A rock of radius 16 cells is a landmark, not a pebble.
- **Ore by cluster, density by richness**: 75% of a cluster's rocks hold its dominant ore, the rest the distance-table draw. Density = 50 + 25 x richness plus or minus 20, clamped 40 to 180. Richer clusters therefore mine faster (ADR 0036 ties mining speed to density), a reason to go for the rich ones.
- **Voids**: background of 0.25 rocks per sector so a void is never perfectly empty (the sum of strays: about one rock per 4 sectors; 68% of all sectors are empty in the measurement).
- **Early game untouched**: the exclusion radius around anchors (470 cells) stays and applies to every candidate. Clusters may overlap the exclusion zone, leaving a half-cluster at the edge (the measured distance to the nearest concentration from the home start is a median 624 cells for D, so the first field is a 100-second trip).

### 4.2 Content (data-driven)
A new optional object `world.field.clusters` (the old numbers stay, so the legacy path keeps working): `super_cells`, `parents` (`none`, `one`, `two` weights), `radius_base_cells`, `radius_per_richness_cells`, `richness_weights`, `core_rocks_per_sector[5]`, `shape_weights`, `size_classes` (mass ranges and shares), `giant` (mass range, rate permille, margin cells), `radius_percent`, `density` (base, per richness, jitter), `dominant_ore_percent`, `background_permille`, `max_loaded`. Validation as for `FieldRules.Load`: margins must hold the biggest rock, keep above load, and so on.

### 4.3 Determinism, versions and compatibility
- Integer only, no floats, no `System.Random`; the order of draws inside a sector is fixed (giants, then regular candidates, each with a fixed number of draws per candidate whatever happens to its position), so a rejected rock never shifts the next one's look (the same idiom as `SectorField.Generate`). Parents are cached per super-sector by the world but cannot change the result.
- A rock's identity is still (sector key, index in the sector's draw), so the table of mined rocks for forgotten sectors, the pins and the slot reuse of ADR 0040 are unchanged.
- **Log version 26 and ADR 0046** (verified at the time of writing: `origin/main` is at #120, the latest log version is 25 and the latest ADR is 0045, no open pull request; re-verify with `git fetch` before taking the numbers). The change adds no command. A new rules flag `WorldRules.LegacyRockClusters` goes in the **third flags byte, bit 2** (the second flags byte is full; bit 1 of the third byte is `LegacyInterior`): off in every preset and in the new-game screen, on when reading logs of versions 21 to 25, so they replay with the uniform generator byte for byte. It hashes as default rules. The `clusters` object is part of the content hash only when present, so the golden checksums (content without it) do not move.
- A field world's hash changes as soon as the ship is farther than 470 cells from every anchor (as ADR 0040 already says), including the rocks loaded but never mined near systems. New golden values are needed for the field tests only.
- Tests to add: order independence of the new generator, every rock inside its sector with its reach, no overlap across faces, parents reproducible from super-sector coordinates, distribution tests (empty share, giant share, richness weights), the cap and the per-sector cap, clearance from anchors, density rule scaling, replay and clone checksums with and without the flag, old logs read with the flag on, content validation, a loose time budget per sector.

## 5. What this does to the economy and the bots
- The bots never leave the systems and never mine rocks, so their finish times (Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56 minutes) should not move: rocks start 470 cells from every anchor and the new generator does not touch veins. This is a prediction, not a measurement: the acceptance test is the balance tool with and without `LegacyRockClusters`, three presets, identical times (*TBD until implemented*).
- Ore per billion cubic cells of the prototype D is 27,559, 74% of the current 37,090: the average field is slightly poorer but 80% of it sits in 3.7% of the sectors. To keep the average equal multiply the core rates by 1.35. The late game is where the field matters, and it is not covered by the bot targets, so any ore-scale choice (section 8) is an owner decision.

## 6. Rendering side
What the client does today (read from `game/DepositModels.cs` and `game/Play.cs`, measured only where stated):
- A rock is a `DepositView` node with two `MeshInstance3D` children (rock and chunks): **two nodes and two draw calls per rock**, no culling beyond the engine's frustum culling, no LOD, no shadows (see the lighting study).
- The rock mesh is a procedural icosphere of 320 faces (1,280 faces for the huge class), plus chunk prisms; materials are shared per (item, density bucket). **Meshes are cached by a key that contains the deposit slot id** (`dep.rock.{seed}...` with `seed = d`), so each loaded rock builds its own mesh, and a slot reused for another rock builds a new one (up to 1,500 slots times the chunk steps). Build time per rock and the hitch when a sector of 30 rocks loads were not measured (*TBD*); one 3-to-30-rock sector is fine, 20+ sectors at once (a jump) will hitch.
- `UpdateDeposits` loops over all deposits every frame (sync, refresh, visible). Fine at 200, about 1,500 calls at the cap; not measured.
- Camera `Far` is 500 cells while the keep sphere is 480 and the scanner reaches 300 (nav 400).

For a cluster of 200 or more rocks (the prototype's richness 4 clusters have 181 rocks p50, richness 5 461):
1. **Share meshes and instance them.** Build K variants of the rock (proposal K = 8 per size class and ore look, i.e. about 50 meshes in total, built once at start or lazily) and the ore chunks into the same mesh, with the tint and the glow per instance (instance colour and custom data, as the belts already do with `MultiMesh` and `UseColors`, `game/BeltModels.cs`). One `MultiMeshInstance3D` per (loaded sector, variant group): a sector holds at most 66 rocks, so a whole cluster costs about 10 to 25 draw calls instead of 400 to 1,000. The depletion look (chunks shrinking in 5% steps) becomes per-instance custom data (a shader parameter) instead of a mesh swap.
2. **Two or three mesh tiers by distance with Godot's visibility ranges** (`GeometryInstance3D.VisibilityRangeBegin/End` with fade): tier 0 = today's 320-face rock with chunks up to about 150 cells, tier 1 = a level-1 icosphere (80 faces, no chunks, a baked glow colour) from 120 to 420 cells, optional tier 2 = an 8-face blob out to the far plane. Visibility ranges are per node, not per instance, so the MultiMeshes are grouped per sector (200 cells wide, distance bands of 150 or more are coarser than the sector), which is why the grouping above is by sector. Godot's automatic mesh LOD is for imported meshes (`ImporterMesh`); for procedural `ArrayMesh` we build the tiers ourselves (*TBD*: confirm in 4.7 whether runtime-built meshes can use `generate_lods`).
3. **Far impostors for clusters beyond the load sphere.** The parents are a pure function of the seed, so the client can compute the clusters in 2,000 cells in a millisecond without the simulation and draw each as a soft dust-and-glow billboard (tinted by the dominant ore, size from its radius) from 500 out to about 3,000 cells. The camera far plane (500) is too short for that: either raise `Far` for a dedicated far layer or draw impostors on a sky-distance shell like the star field (*TBD*, needs a depth-precision check). This is also what makes "a concentration over there" visible without a scan; whether it should be visible before being scanned is a game design choice (section 8).
4. **Draw-call budget** (a proposal; the measured baseline is `docs/prototypes/render-performance.md`: individually node-built objects cost about 3.7 microseconds each per frame after the fixes, 1,000 machines 3.7 ms at 720p on an RTX 4080 SUPER): keep rocks at most 150 draw calls and 150,000 triangles in view at tier 0 and 1 together, which the MultiMesh grouping meets with a full cap of 2,500 loaded rocks (about 66 sectors x 2 tiers). A fully loaded 1,500 rocks as individual nodes would be an estimated 5 ms of node cost (extrapolated from the machine numbers, *not measured*).
5. **Shadows and light**: rocks cast no shadows today and the lighting study proposes a shadowed key light; a MultiMesh of 66 rocks in the shadow pass is cheap (one draw call per mesh tier), and the shadow distance should be capped near 200 cells.

### Float precision far from the origin
World positions are 64-bit (the simulation: units of 1/240 cell, positions up to 4,000,000 cells, 960 million units, inside an int), but the client converts to 32-bit floats in cells: `new Vector3(dx, dy, dz)` for rocks, `ShipPosition`, enemies, projectiles, markers. A float has 24 bits of mantissa, so the spacing between representable positions is:

| Distance from the origin (cells) | float spacing (cells) | relative to a ship module (8 cells) |
| --- | --- | --- |
| 10,000 | 0.001 | invisible |
| 65,536 | 0.0039 | invisible |
| 100,000 | 0.0078 | a shimmer of the hull against the rocks, starting |
| 1,000,000 | 0.0625 | visible jitter and vertex swim at 1/16 of a cell |
| 4,000,000 (world limit) | 0.25 to 0.5 | unplayable |

The jitter is largest where rocks, the ship and the camera are all large floats that are subtracted on the GPU. The mesh vertices are fine (they are local to the node). Godot has a double-precision engine build (`precision=double`), but the Mono release editor and export templates we use are the single-precision ones; a custom build is a heavy commitment (*TBD*: we have not checked whether the 4.7 Mono templates can be built that way in CI).

**Floating origin proposal**: the client keeps an origin in cells as `long` (snapped to 1,000 cells) and a single helper `View(long x, long y, long z)` that subtracts the origin in 64-bit arithmetic before converting to float, so every node position stays below about 30,000 cells (spacing 0.002). When the ship is more than 10,000 cells from the origin the client rebases: it chooses a new origin at the ship's snapped position and shifts every root of the world by the exact difference (an integer number of cells, exact in float at these magnitudes). Roots: rock nodes (the MultiMeshes of the previous section hold rocks relative to their sector, which makes this almost free: only a `Position` per sector changes), obstacles, stations and structures, enemies, projectiles, markers, drones, the star and planet backdrop (already camera-relative by design in `Ambience.cs`, to be checked). The simulation, the command log and the physics are untouched: the origin is a pure view concept.
The cost is the conversion sites. `CellUnits` appears 92 times in 27 files in `game/` (Play.cs 12, Play.Weapons.cs 12, Play.ObstacleShot.cs 8, Play.Obstacles.cs 7, Play.Ground.cs 5 and so on) and `DepositPosition` is read in 33 places; the owner's count of about 60 float conversion sites is consistent. Effort: M to L (a day for the helper, three to four days to convert and test every site; a test that sets the origin to 5,000,000 and compares screenshot-free geometry numerically for the ship, a rock and a projectile). It is not needed for the first version of clusters (the home galaxy spans 3,700 cells from the origin, clusters in a 20,000-cell neighbourhood have a 0.002-cell spacing) but it is required before the world limit of 4,000,000 cells is something a player can reach, and cheap to start doing now: add the helper and route new code through it.
At boost speed (24 cells per second) reaching 100,000 cells takes 70 minutes in a straight line, so in practice the problem begins in long exploration sessions, and a soft rule can postpone it (*proposal*: a travel-time warning or a world limit of 200,000 cells in content until the origin shift exists; `world_limit_cells` is already content).

## 7. Implementation plan
| Step | Content | Effort | Risk |
| --- | --- | --- | --- |
| 1 | ADR 0046 (accepted when merged), `FieldClusterRules` content + validation, `ClusterField.Generate` next to `SectorField`, flag `LegacyRockClusters` (flags byte 3 bit 2), log version 26, tests listed above | M | log-version bookkeeping; mitigated by the flag |
| 2 | Raise `max_loaded` to 2,500, a per-sector cap (80), `radius_percent` in the model and the hit volume, `DepositRockScalePermille` already follows the sim radius | S | solid giants: a 16-cell rock is a wall for a ship with an 8-cell module; check the collision warning, the laser range and `MineRangeCells = 6` (mining distance from a rock's surface, TBD) |
| 3 | Balance run: three presets with and without the flag (expect identical times); a late-game ore-demand estimate to set `unit_amount` per cluster | S | none for the bands; the late game is unmeasured |
| 4 | Client: shared variant meshes, MultiMesh per sector, per-instance depletion, two tiers | M | looks; keep the old `DepositView` behind a flag for the gallery |
| 5 | Client: far impostors and cluster markers on the map and the scanner readout (cluster name, richness, dominant ore) | M | design decision on discovery |
| 6 | Floating origin helper and conversion | M to L | touches 27 files; a staged conversion behind a debug assertion that no node is farther than 50,000 cells |

## 8. Decisions for the owner
1. **How far apart are fields?** Super-sector 800 cells (a field every 430 cells median from a random point, 18 seconds boosted), 1,000 (proposal), or 1,300 (700 cells, 28 seconds). Ship speed is 6 cells per second (24 boosted) at the start ship, so gaps feel smaller than they read.
2. **How much ore?** The prototype's median cluster is about 4,000 ore and a richness 5 cluster up to 150,000. A single multiplier (`unit_amount`, or a cluster ore scale) sets it; it needs a late-game ore-demand number we do not have.
3. **How rare are giants?** 0.8% of rocks (about one per 1.5 clusters on average, 4 in a richness 5 cluster), 12,000 ore each. Fewer giants make each one an event.
4. **Radius scale 250%** (median rock 6.4 cells wide, a giant 32). Bigger rocks are bigger obstacles (solid, ADR 0033).
5. **A galactic plane?** The home galaxy is flat (y within 70 cells) but the field is a uniform 3D volume, so most clusters lie above or below where the player flies. Proposal: multiply the parent probability by a falloff in `|y|` (full inside 800 cells, half at 2,000), which keeps clusters where the players are. Not prototyped (*TBD*); it changes the measured numbers.
6. **Are clusters visible before scanning?** The pure parent function lets the map and a far impostor show them from 3,000 cells; a survey item or a discovery rule is the alternative. This is exploration design, see the improvement route.
7. **Log version 26 / ADR 0046** to be reserved for this change when work starts (the numbers are only reserved by merging).

## 9. Not measured, or only partly
- No real-engine measurement of the rendering side: draw calls, mesh build hitches, the cost of 1,500 `DepositView` nodes and the gain of MultiMesh are estimates from the existing render-performance numbers.
- The balance bands with the new generator (the generator exists only in the study tool).
- Collision and laser behaviour among 200 solid rocks, ship handling around a 16-cell giant, mining range for giants.
- A vertical plane bias, the gap sweep beyond 800 and 1,300, tuning of the radius and core-rate tables (every parameter above is a first guess).
- Behaviour with the density rule other than 100 (the study uses 100; the proposal scales the core rates like the existing rule).
- Generation time inside the simulation with the real call path (the study measures only the generator: 4.5 microseconds per sector, p99 37 for D).
- The study's concentration test (8 neighbours within 100 cells, 40 random points per distance searched in 1,500 cells) is a coarse definition chosen by us.

## 10. Reproducing the numbers
```
dotnet run -c Release --project tools/Starfoundry.FieldStudy -- content/core.json            # everything (about 10 s)
dotnet run -c Release --project tools/Starfoundry.FieldStudy -- content/core.json region     # or: galaxy, determinism, vantage, loaded, clusters, sim
```
Seed 20261126 (the same as `FieldTests`), integer generators, deterministic output apart from the timings. The three cluster generators live in the same file as `ClusterGen` with a `ClusterParams` record, so the tables above can be regenerated with other numbers.
