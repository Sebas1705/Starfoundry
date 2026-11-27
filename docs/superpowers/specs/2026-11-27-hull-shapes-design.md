# Hull shapes: corner, wedge, round and sloped modules in the simulation

Status: **proposal for the owner to approve** (2026-11-27). Nothing in this document is built. It is step B of the hull curves work; step A (drawn-only rounded shoulders on ships, a chamfered roof edge on stations, round frames, swept wings; no sim change) is described in [visual pass 3](../../prototypes/visual-pass-models.md#visual-pass-3-hull-curves-ships-and-stations). Numbers are *measured* from the code unless marked *proposal*; the next free numbers are command log version **26**, [ADR](../../adr/README.md) **0046** and the **second bit** of the third world-rules flags byte (checked against `origin/main` on 2026-11-27: log version 25, ADR 0045, `flags3 & ~1`).

## 1. Why step A stops where it does
- A module is one entry of `Structure.ModuleMap` (`(mx, my, mz)` to `ModuleKind`), an 8 x 4 x 8 cell box. The sim's hit volume is that box (`BeltNetwork.ModuleBox`, ADR 0030), docking uses the four lateral faces as ports (`DockPort`, ADR 0041), the designer sends `DesignModule(Mx, My, Mz, Kind)` and nothing else (`DesignerApply`, tag 46; `ApplyBlueprint`, tag 65), and the editor's rotate tool is a client-only draft field (`BlueprintDraft.RotationOf`, never sent).
- So without changing the sim the picture can only get **smaller than the box** (a chamfered roof edge on a station, which is above the head of the walking body), or **gain thin attachments**. A true rounded corner removes material from a place a body can walk in (the corner cell of a station module, where machines and belts are placed on the floor grid), and a ship's hit volume would still be the full box. The owner asked for modules with rounded corners, diagonals and curves; this is the spec for them.

## 2. Goals and non-goals
Goals: modules whose *shape* is not a box (diagonal and rounded corners, wedges, slopes, ring and pipe sections) with matching hit volume, walkable volume, ports, cost and validation; the same editor, the same command log, old logs and saves replay byte for byte; the scripted player unchanged.
Non-goals: a free-form voxel hull, a finer grid (half cells), curved-wall physics, destructible hulls, new module *roles* (a wedge is a hull, a cargo wedge is cargo with less volume; no new gameplay).

## 3. The options considered
| | Idea | Verdict |
| --- | --- | --- |
| A | **A shape and a turn on a module** (the chosen one): the module stays one grid cell and one `ModuleKind`; a new `HullShape` (box, vertical chamfer, roof chamfer, rounded corner, wedge ramp, ring bend...) and a `Turn` (0 to 3 quarter turns) say what part of the box is filled. | Recommended: the grid, the ports, the structure graph and every kind's rules stay; only the filled volume changes. |
| B | New module kinds (`hull_wedge`, `hull_corner`, `cargo_round`...) | Rejected: the kind explodes (22 kinds today; each needs a model, a shell, content, research, a palette entry and per-kind rules), and shape is orthogonal to role. |
| C | Half cells (a 4 x 2 x 4 sub-grid) | Rejected for now: touches every coordinate in the sim, the codec, the floor plan (ADR 0044), the stairwells and the Blender shell; very large, and the shapes of A give the diagonals without it. Kept as a later option. |
| D | Only a visual shape, no sim | That is step A. |
| E | Curves as many small wedges (a ring of 8 wedge modules) | Possible with A: a ring section is four corner modules; no extra rule needed. |

## 4. The shape set (proposal)
All shapes are **integer, convex, and a subset of the box**, so every collision query stays an exact integer calculation (ADR 0002). A shape is defined once, as a list of half-spaces `a*x + b*y + c*z <= d` with small integer coefficients in cells (units are 1/240 cell, so every plane scales exactly), clipped to the box.

| Shape | Cut | Used for |
| --- | --- | --- |
| `Box` | nothing | every module today (the default; never written to the log) |
| `EdgeV` (vertical diagonal) | a 45 degree cut of the vertical corner edge, leg 2 cells | station and ship corners seen from above |
| `EdgeH` (roof or floor diagonal) | a 45 degree cut of one horizontal edge, leg 2 cells | the chamfered roof line, hull "shoulders" |
| `Corner` | `EdgeV` and `EdgeH` together (a corner of three faces) | a hull's bow and stern corners |
| `Round` | `EdgeV` approximated by a 3-plane arc (22.5, 45, 67.5 degrees), leg 3 cells | station towers, rounded corners |
| `Wedge` | a ramp: the roof falls 4 cells over 8 (a sloped hull piece) | noses, fairings, tapered tails |
| `Bend` | an inner quarter-ring (an arc of 3 planes) with the walkable path following it | curved station rings and corridors, pipe elbows |
| `Ring` | a straight pipe: a round bore (an octagonal tunnel) in the box | pipe and ring sections between modules |

Every shape except `Box` removes volume; it never adds any (the box is the sim's bounding volume, which keeps the broad phase, the build range test and the docking tests as they are). Each shape lists which of the six faces stay **full** (a door and a neighbour can attach there), which become **partial** (a neighbour can attach only if its own face is cut to match) and which become **none**. `Turn` rotates the shape about Y (four turns), and a mirrored shape is a different `Turn` of a symmetric one, so no mirror flag is needed.

## 5. Simulation
- **State.** `Structure` keeps `ModuleMap` as is and adds a sparse `ModuleShapes` map `(mx, my, mz)` to `(shape, turn)`, absent for `Box`. The checksum hashes it only when it is not empty (the golden-checksum idiom of ADR 0018), so worlds without shapes hash exactly as before.
- **Commands.** `DesignModule` gets two trailing optional bytes; the codec writes them only for log version 26 and later and only when a shape is not `Box` (a flag bit in the existing kind byte, so a box module costs the same bytes). `DesignerApply` and `ApplyBlueprint` carry them; `PlaceModuleIn` (the legacy single-module tool) cannot place a shape. This also gives the **rotation** the editor has never sent: `Turn` is a real field.
- **Rules flag and legacy.** `WorldRules.LegacyHullShapes` (third flags byte, bit 1, value 2; the reader's mask `flags3 & ~1` becomes `~3`). New worlds write log version 26 with the flag off; logs of versions 5 to 25 read with it on, so they replay as recorded, and the reader refuses a shaped module in them. In a legacy world a shaped `ApplyBlueprint` is refused (`NotAllowedHere`), exactly like `LegacyBlueprint` in ADR 0043. Saves: the save format stores the structure, so it grows by the same optional section and old saves load without it.
- **Hit volume.** `BoxToPoint` and `InsideBoxOf` (ADR 0030) become shape-aware: the nearest point of `box ∩ half-spaces` to a sphere centre. For a convex integer polyhedron that is a clamp to the box followed, for each violated plane, by a projection onto the plane and a second clamp; with planes of 45 degrees and axis legs in whole cells, all of it is exact in `long` (the squared distance `D2` stays exact, as the contract of `BoxVector` requires). Cost: at most 3 planes per module and per query, the same order as the box.
- **Ports and docking.** `DockPort` faces are the four lateral faces. A shaped dock module keeps its port face full (the dock module itself stays a `Box`, ADR 0041; `Turn` is refused on it). The port test of a neighbour is unchanged for full faces.
- **Hull points and mass.** `module_hp` and the mass use the filled volume share of the shape (an integer percent per shape, in content) so a wedge is lighter and weaker than a box; *proposal*, tuned with the stats panel.
- **Interior (stations).** The floor plan (ADR 0044) and `BlueprintInterior` place machines, belts and turrets on the 8 x 8 cells of a module floor. A shape leaves some of those cells (partly) inside the cut: each shape lists the **blocked cells** of its floor (a 4 x 4 corner for `Round`, for example) and `ApplyBlueprint` refuses an interior placement on a blocked cell (`Reject.CellBlocked`). Without this a machine would stand in a wall.

## 6. Walkable volume and the body
- `HullShell` (`HullProfile.ShellOf`) gets `Shape` and `Turn` next to its neighbour mask (the 32 bits of the mask are full, so these are new fields, not new bits). `HullProfile.Clear` adds, after the box checks, the shape's half-spaces in the module frame (turned), shrunk by the wall thickness (`LinerInset` on a ship, `StationWall` on a station), and `HullProfile.SectionAt` blends nothing new. Everything the body needs stays engine-free and is tested like today (`HullProfileTests`, `BodyShellBruteForceTests`).
- The body must not walk into a cut: the test is the one `HullShouldersTests` already does for step A, extended to the shapes (every visible hull point is outside the liner polygon, and every walkable sample is inside the filled volume).
- Stairwells (`Stairwell.Capable`) and doorways need a full floor and a full face: a shaped module is refused as a stair partner unless its shape keeps the stairwell cell.

## 7. Models
- `ModuleModels.Build(kind, ship, variant, neighbours)` gets the shape and turn (so the model cache key grows by two small numbers). The shapes reuse step A: a ship shape is an octagonal section whose chamfers are bigger, drawn with the same lofts and the rounded shoulders; a station shape is a slab shell with a diagonal wall and the cap of the roof bevel. The frame, the trim and the viewports follow the cut.
- The mesh audit must stay green on every shape in every neighbour combination: the audit layouts get a `shapes` layout (every shape, every turn, every neighbour), like `ship-skins` does for the skins.
- Triangle budget: a shape module costs about the same as a box (the wall count is the same, the cut replaces a corner); the frame of step A already uses round tubes.

## 8. The blueprint editor
- **Palette**: a *Shapes* section under the module list (icon of the cut, the same cost line). A shape is chosen with the module (a two-step pick: kind, then shape) or with the existing rotate tool (`R`), which now also cycles the shape of a selected module.
- **Ghosts**: the existing ghost overlay works unchanged because ghosts are the real model under an overlay (`BlueprintEditor.Part`). The rotation holder already exists, so a turned shape simply rotates.
- **Rotation**: `BlueprintDraft.RotationOf` becomes the `Turn` of the module and is applied; today it is never sent (section 1).
- **Station parts are drawn with ship hulls** in the editor today (`Build(kind, true, ...)`), which has to be fixed first, otherwise stations would show the wrong shape family.
- **Validation list**: new errors `shape not allowed on this kind` (a dock, a cockpit's windscreen section, a hardpoint stay boxes), `partial face meets a full face` (a neighbour cannot attach there), `blocked floor cell holds a machine`; a connectivity check that follows full and partial faces (the walkable path is a graph over module faces, as today).

## 9. Cost and validation
- A shape has the price of its module (*proposal*: the same, so a rounded corner is a cosmetic choice and not a tax) and a build price of zero extra; refund rules are those of ADR 0043. Research: none for the diagonal shapes, one node (`core:hull_forming`, *proposal*, by the plates sink) for `Round`, `Bend` and `Ring`, so the first hours are unchanged.
- Validation reuses `SimWorld.CheckBlueprint` (the preview and the apply cannot disagree): the same code checks shape allowance, face matching, floor cells and the connected-piece rule, and the test of 300 random drafts adds shapes.

## 10. Bot, balance and legacy replay
- The scripted player builds boxes only; it is untouched, its win times (Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56 minutes) must not move, and the test that pins them runs unchanged. A shape has the same price, the same effect and the same role as the box it replaces, so no balance number changes.
- Legacy: a world from a log of version 25 or older replays byte for byte (the flag, the optional bytes, the unchanged checksum); the codec test of the older versions grows a case with a shaped module in a version 26 log and one that must be refused in a 25 log.

## 11. Effort and phasing (S small, M medium, L large)
| Step | What | Effort |
| --- | --- | --- |
| B1 | Sim: shape and turn state, codec (v26), flag and legacy gate, validation, hash, `BoxToPoint` shape-aware hit volume, tests (round trip, replay, old logs, 300 random drafts, collision exactness). Only `EdgeV`, `EdgeH` and `Corner`. | M |
| B2 | `HullProfile` and the body: `Shape` in the shell, `Clear` with the half-spaces, floor cell masks, tests and the brute force. | L |
| B3 | Models: the shapes for the ship section and the station shell, mesh audit layout, walk audit. | L |
| B4 | Editor: palette, `Turn` applied, validation messages, the station-model fix, `--blueprint-check` additions. | M |
| B5 | `Round`, `Wedge`, `Bend`, `Ring` (more planes, the walkable arc), research node, presets that use the shapes. | L |
| Docs | ADR 0046, ADR for the log version if separate, the structures and stations specs, `12-open-questions`. | S |
Total: **L**. B1 alone is a safe first merge (no visible change until B3), and the early game is unchanged throughout.

## 12. Risks
- The body shell is the riskiest part: step A could not touch it, and a shape that cuts a floor cell changes where a machine can stand. Mitigation: the blocked-cell masks and the brute-force test over every shape.
- Mask and cache explosion in the model builder (the neighbour mask already carries skins and stair bits). Mitigation: shapes are part of the cache key; the audit layout covers them.
- Exactness of the hit volume for the rounded shapes (planes at 22.5 degrees are not integer): *proposal* to use only 45 and 90 degree planes in the sim and let the drawing add the small curvature, so the hit volume is always at least the drawn hull.
- Saves and the content hash: new content (a research node, a hull-point table) changes the content hash, so old saves refuse to load by design (as in every content change); say so in the ADR.
- Scope creep towards a voxel hull: the box stays the bounding volume and shapes only ever remove from it.

## 13. Decisions for the owner
1. Approve option A (a shape and a turn on a module) over new kinds, and over half cells.
2. The first shape set: `EdgeV`, `EdgeH` and `Corner` only (B1 to B4), then `Round`, `Wedge`, `Bend`, `Ring` (B5), or everything at once.
3. Whether a shape changes the price, the hit points and the mass (this spec: same price, volume-share hull points and mass).
4. Stations only first (where the owner sees the boxes most), or ships and stations together.
5. Whether the sim may be told a turn at all (the editor's rotate tool is client-only today), or turn is only part of the shape.
6. Research gating for the round shapes.
7. ADR number 0046 and log version 26 (the next free numbers on 2026-11-27).
