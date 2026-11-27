# Multi-cell machines, large modules and the small dock

Status: **decided by the coordinator and the owner's request, built in three stages** (2026-11-27). Stage 1 (the small dock) is built and described by [ADR 0047](../../adr/0047-the-dock-is-a-small-collar-on-a-module-face.md); stages 2 and 3 are *proposals* here until their own ADRs. Numbers are *measured* from the code where it says so and *proposal* otherwise; nothing is copied from another game.

The owner asked for three things: (1) bigger modules, so bigger factories fit; (2) machines that take more than one cell, in irregular shapes (L, T, long, 2 by 2, 2 by 3, plus); (3) a docking piece that is not a whole module. This document fixes how, in order, with one pull request and one ADR per stage.

## 1. What exists today (measured)
- A **module** is one entry of `Structure.ModuleMap` `(mx, my, mz)` to `ModuleKind`, an 8 x 4 x 8 cell box ([ADR 0018](../../adr/0018-modular-structures.md)); its hit volume is that box ([ADR 0030](../../adr/0030-ship-attitude-in-the-simulation.md) and the box hit volumes), it joins its six neighbours, and a limit of 64 modules per ship and 256 per station applies (`DesignerRules`).
- A **machine** is one cell (`Machine.X/Y/Z`, [ADR 0004](../../adr/0004-data-driven-content-and-one-cell-machines.md)); a **belt segment** is a straight run of cells; machines and belts stand only on the floor layer (`StationGrid.FloorLayer`, [ADR 0031](../../adr/0031-construction-rules-fabrication-bays-and-floor-building.md)) of a module whose kind allows them; a station's interior is edited as a blueprint (`BlueprintInterior`: machines, belts, links; [ADR 0044](../../adr/0044-the-station-interior-is-drawn-on-a-floor-plan.md)) applied by `ApplyBlueprint` ([ADR 0043](../../adr/0043-stations-are-edited-as-blueprints-with-applyblueprint.md)).
- The **dock** was a full `dock` module on both sides ([ADR 0041](../../adr/0041-ship-and-station-dock-through-a-dedicated-dock-module.md)): 8 x 4 x 8 cells of hull for one doorway.
- Latest merged: log version 26 and ADR 0046 (`origin/main`, 2026-11-27). The [hull shapes spec](2026-11-27-hull-shapes-design.md) is a proposal that is **not built**.

## 2. How the three requests relate to the hull shapes proposal
They are different layers and do not duplicate each other:

| Layer | What changes | Where |
| --- | --- | --- |
| Hull shapes (existing proposal) | The **filled volume of one module** (chamfer, wedge, round). | `ModuleShapes` per module; step B of the hull work. |
| Large modules (stage 3, here) | **Several module cells grouped as one unit** with a joined floor. | A `Span` grouping over ordinary cells. |
| Multi-cell machines (stage 2, here) | **Several floor cells taken by one machine.** | A footprint mask on the machine type, a turn on the machine. |
| Small dock (stage 1, built) | **A port element on a module face**, no module of its own. | A collar set per structure. |

Reconciliation, decided: (a) the hull shapes' `Turn` byte stays a module concern and is not reused for machines (a machine turn is a different byte on a different record); (b) a module shape may not remove a floor cell that a footprint needs, which the hull spec already lists as *blocked cells*, so footprint placement reuses that mask when shapes arrive; (c) a collar needs a **full** lateral face, which the hull spec calls a face that stays `full`; the collar check (`DockRules.CanHostPiece`) will consult the shape's face table when shapes are built; (d) the numbers the hull spec reserved (ADR 0046, log 26) are now taken, so shapes become ADR 0050 or later and the next free log version at that time; the hull spec carries a note.

## 3. Stage 1: the small dock (built; ADR 0047, log version 27)
- **A collar is a small piece attached to a horizontal face of an ordinary module** and has no volume of its own: it is a `(module cell, face)` pair in `Structure.DockPieceSet`. Hosts: core, hull, cargo, reactors, shipyard (all faces) and a cockpit (its two flanks); not engines, hardpoints, antennas, shields (open shapes), nor open decks.
- **Effect on ADR 0041.** The dock module kind is retired in new worlds (`PlaceModule`, designs and blueprints refuse it with `KindNotAllowed`); the rule "a structure keeps at least one dock" stays, now counted in collars (`NoDockModule`, `LastDockModule`). Everything else of ADR 0029 and 0041 (range, speed, alignment, pose, stock, repair, walking across) reads ports through `DockPorts`, which now also lists the collars.
- **Geometry.** The ship's host module lands on the module cell beyond the station's host module, exactly as the two dock modules did, so the two hulls meet at the seam and the airlock doorway opens between them. The ship is one module shorter than in ADR 0041 and a station's first free module cell is next to its core again.
- **Walk-across.** Unchanged: the docking bits of the two host modules open the doorway and draw the airlock; the collar flange stands on the seam.
- **Cost.** A collar costs half the cost of the old dock module (rounded up), refunds half, and a collar that only moves is free; a founded station (two collars, on the X faces of its core) and every pre-built structure (a ship: both cockpit flanks) come with them at no cost, because a ship docks axis-aligned and only meets an opposite X face; a collar adds the station stock capacity a dock module did (`DockStockCapacity`). No content changes, so the content hash and the golden checksums do not move.
- **Migration.** `WorldRules.LegacyDockPieces` (third flags byte, bit 2, value 4) keeps ADR 0041 as it was: logs of versions 5 to 26 read with it on and replay byte for byte (existing structures keep their dock modules; collars are refused). Saves are command logs, so they replay in the same way. Checksum: collars are hashed only when a structure has any.
- **Blueprint and designer.** `DesignerApply` and `ApplyBlueprint` may carry the collar set (tags 67 to 69; the older tags keep the collars that still have a host). The editor has a *Dock collar* tool: each click on a module turns its collar to the next free face and then takes it off.

## 4. Stage 2: multi-cell, irregular machines (branch `feature/multicell-machines`, log version 28, ADR 0048)

### 4.1 Footprint model
- A machine type gets an optional content field `footprint`: an ASCII mask, one string per row along +Z and one character per cell along +X, `X` filled and `.` empty, at most 4 x 4, connected, at least one cell. `["X"]` is today's machine and is the default, so no existing content changes. Examples: 2 x 2 `["XX","XX"]`; long 1 x 3 `["XXX"]`; L `["X.","X.","XX"]`; T `["XXX",".X."]`; plus `[".X.","XXX",".X."]`; 2 x 3 `["XX","XX","XX"]`.
- A machine placed at the **anchor** (its first filled cell in row-major order, the cell recorded as `X, Y, Z`) with a **turn** `0..3` (quarter turns about the vertical axis) covers the rotated mask. `Machine` gains a `Turn` field and a derived `Cells` list (anchor-relative, sorted); `MachineAt(cell)` resolves any covered cell to the machine through one occupancy map kept next to the existing per-cell lookup. Anchor and turn are in the checksum only when the machine type is multi-cell or the turn is not zero (the golden-checksum idiom).
- **Floor and blocking.** Every covered cell must be a floor cell (`NotOnFloor` otherwise) of **one** module whose kind allows the machine's role, free of other machines and belts (`Overlap`); a footprint may not cross a module seam (the wall and doorway logic stays untouched). New reject `FootprintBlocked` when a mask cell would leave the module or hit a cell a hull shape removed.
- **Belts and ports.** The input and output rules of one-cell machines extend by adjacency: a belt whose end points at **any** covered cell feeds the machine; a belt whose first cell touches any covered cell and leaves away from the machine is fed by it. Links (`BlueprintLink`) keep indexing machines and belts, so they need no new field. The machine's input and output buffers are shared (one set per machine, not per cell).
- **Power and recipes.** One power draw per machine type from content (a bigger machine declares a bigger `power_use`), one recipe at a time as today. Multi-cell machines get their own recipe entries with batch yields, so the recipe list shows what each is for (see 4.5).
- **Demolish, refunds, repair, damage.** A click on any covered cell selects the machine; `RemoveMachine` refunds half the cost as today; hit points and turret rules are per machine (a multi-cell turret is not part of stage 2).
- **Commands and format.** `BlueprintMachine` gains `Turn`; the interior is written with a new tag **70** (`ApplyBlueprintExt`, a flags byte: interior, collars, machine turns, and bit 3 reserved for stage 3 spans) so the combinations of optional parts stop multiplying tags; tags 65, 66, 68 and 69 still decode as before. `PlaceMachineIn` (the in-world tool of worlds that have it) gets no turn. Log version 28; `WorldRules.LegacyFootprints` (third flags byte, bit 3, value 8) is on for logs of versions 5 to 27, refuses footprints and turns and keeps the one-cell lookups. Preview and apply use one function (`BlueprintCore`), so `CheckBlueprint` cannot disagree.
- **Validation list.** New entries `bp.val.footprint` (a machine leaves its floor), `bp.val.machineOverlap`; machines on a floor of a module the draft removes are orphans as today.
- **Ghosts and panel.** The floor plan draws the footprint cells shaded in the machine colour; the held machine turns with `R`; the machine panel shows its size in cells. The visual model of a multi-cell machine is built through a footprint parameter of the machine model builder (a box per mask cell joined by the machine's own housing), so models of one-cell machines are untouched; another effort edits item and machine visuals, so the touch is one new function and one call.
- **Bot.** The scripted player builds one-cell machines only; its times must not move. A second scripted variant that swaps its smelter for the large one is *not* part of the band check; it is a unit test of throughput.

### 4.2 First content (proposal, tuned with the balance tools)
| Machine | Footprint | Role | Recipe idea | Reads as |
| --- | --- | --- | --- | --- |
| Large smelter | 2 x 2 | crafter | `smelt_iron_x4`: the Mk1 recipe times 4 for 2.5 times the time, so 1.6 times the output per tick for 3 times the cost | fewer belts into one bigger furnace |
| Long assembler | 1 x 3 | crafter | `assemble_batch`: three plates per cycle in the time of two | a line a belt can run along |
| L fabricator | L of 3 cells | crafter | accepts two input belts on the two legs, one output at the corner | a layout choice, not a stat |
| Reactor core | plus of 5 cells | power | burns the fuel cell of the Mk1 reactor module for 4 times the power | a station centrepiece in a bay |

### 4.3 Tests (stage 2)
Determinism (same log, same checksum, clone, replay); legacy replay of every older log (golden-checksum idiom: the frozen `core_pre0031.json` fixture); preview equals apply for random floor plans with footprints and turns (300); every refusal; belts feeding and leaving at every covered cell; demolish and refund; the mask parser (connected, size, default); `--blueprint-check` extended with a large smelter placed, turned and demolished.

## 5. Stage 3: large modules and the first big factory (branch `feature/large-modules`, log version 29, ADR 0049)

### 5.1 Model: a span groups ordinary cells
- A **large module** is `n x n` ordinary module cells of **one kind** in one layer (`n` = 2 or 3) grouped by a **span** `(anchor, n)` in `Structure.Spans`. Every covered cell stays an entry of `ModuleMap`, so the hit volumes, connectivity, ports, collars, hull points and power are per cell exactly as today; the span adds only **identity** (these cells are one unit) and a **joined floor**: the floor plan of the span is one 16 x 16 (n = 2) or 24 x 24 (n = 3) cell area whose internal seams have no wall, so machines, belts and footprints may cross an internal seam (the one exception to the stage 2 rule), and a machine at the seam works as in a single bay.
- **Joined to the 8-cell grid.** Coordinates do not change: a cell is still `(x, y, z)` and its module `ModuleOfCell`; a span only marks which seams are internal. A span at `(mx, my, mz)` with `n = 2` covers `(mx..mx+1, my, mz..mz+1)`. Nothing else in the 8-cell grid, the floor layer, the codec or the stairs changes.
- **Which kinds can be large.** Content field `max_span` per module kind: `workshop` (fabrication bay) 3, `core`-free: the core is never large; `cargo` 3, `reactor` 2, `hull` 3, `defence` 2, `drone_bay` 2; ships: `cargo` 2 and `hull` 2 only (ships hold no automation, [ADR 0039](../../adr/0039-ships-hold-no-automation-stations-do-and-the-workbench-is-slow.md)); engines, hardpoints, antennas, shields, cockpits and the dock stay single.
- **Ports and collars.** Faces on the outer boundary of the span are ordinary faces: a collar may sit on any of them if the kind hosts one; internal faces are never ports (no outside).
- **Walkable volume and the body.** `HullProfile` treats a neighbour of the same span as an open seam (no wall, no door frame); the mask gets a `SpanOpen` set carried in a new parameter of `ModuleModels.Build` (the 32-bit mask is full). A span's roof, frame and trim are drawn once around its outline.
- **Hull model.** Station spans: the slab box of each cell with the internal seam walls omitted and one frame around the outline; a 3 x 3 gets a skylight grid. Ship spans: the existing round section lofts with joined sections. Both use the mesh audit with a `spans` layout.
- **Cost.** A span costs `n * n` times the single module's cost times 0.85 (rounded up per item): bigger is slightly cheaper per cell (*proposal*), refunds half; a span is built and removed as a unit.
- **Research.** One node `core:large_structures` (campaign only; sandbox and free build have everything) after the fabrication bay research, costing plates and circuits at the level of the second bay (*proposal*); no early-game change.
- **Commands.** The tag-70 flags byte bit 3 carries the span list `(anchor x, y, z, size)` after the modules; modules of a span are still listed one by one. `DesignerApply` for the ship carries spans through a tag 71 pair the same way. Log version 29; `WorldRules.LegacyLargeModules` (third flags byte, bit 4, value 16) refuses spans and gives old logs their single cells.
- **Validation.** All `n * n` cells present, same kind, kind allows that `n` (`max_span`), no overlap with another span, cells of a span cannot be moved or removed alone (the editor and the sim both refuse; remove the span), connected piece rule as today; errors `bp.val.span`, `bp.val.spanKind`.
- **Editor.** The palette lists *Large bay (2 x 2)*, *Large cargo (3 x 3)* and so on under the single kinds; placing one stamps `n * n` cells, ghosts show the whole block, delete and move act on the unit; the floor plan opens the whole joined floor at once.
- **Bot.** Unchanged: the scripted player never builds spans. Win times must hold (section 7).

### 5.2 First big-factory content
- Large fabrication bay `workshop` 3 x 3 (24 x 24 floor, *measured* as 9 bays of floor): the room for the four stage 2 machines and a row of one-cell ones. A preset station blueprint, *Foundry*, in the blueprint library: a large bay, a large cargo, two reactors, ore port, large smelters, a long assembler line and depots (used by the walkthrough and the headless check).
- A scenario (a later level) may start with a large bay unlocked; stage 3 adds none.

## 6. WorldRules flags, log versions and ADR numbers
| Stage | Flag (third flags byte) | Log version | ADR | Commands |
| --- | --- | --- | --- | --- |
| 1 | `LegacyDockPieces`, bit 2 (4) | 27 | 0047 | tags 67 to 69 (collars on designs and blueprints) |
| 2 | `LegacyFootprints`, bit 3 (8) | 28 | 0048 | tag 70 (`ApplyBlueprintExt`) |
| 3 | `LegacyLargeModules`, bit 4 (16) | 29 | 0049 | tag 70 bit 3, tag 71 (ship spans) |
Latest merged on `origin/main` when this was written: v26 and ADR 0046. The hull shapes proposal takes the next free numbers when it is built. Each stage re-checks `git fetch` and renumbers if another effort merged first (rebase keeps both sides).

## 7. Legacy replay, bot and bands
- **Replay.** Each flag is on for older logs, off in the presets and the new-game screen, hashed as default rules; the reader refuses the new commands in older versions; every older golden checksum test runs against the frozen fixture content, and the frozen content is not edited (the golden-checksum idiom); a test per stage records a log in the old rules, patches its version byte and replays it byte for byte.
- **Bot.** `--structures --minutes 150 --delay=6` on seeds 20261121 / 20261120 / 777. Stage 1 measured (mm:ss):

| Preset (band) | 20261121 | 20261120 | 777 |
| --- | --- | --- | --- |
| Relaxed (44-50) | 46:15 | 46:37 | 45:46 |
| Normal (48-53) | 48:11 | 49:13 | 48:41 |
| Hard (51-56) | 51:36 | 55:31 | 51:47 |

Stages 2 and 3 must leave these within the bands; they add no scripted behaviour, so a change means a bug.

## 8. Effort and risks
| Stage | Effort | Main risk | Mitigation |
| --- | --- | --- | --- |
| 1 small dock | M (built) | Old fixtures assume dock modules | They run with `LegacyDockPieces`; new tests cover collars |
| 2 footprints | L | Every machine lookup assumes one cell (belts, links, damage, panels, picking) | One occupancy map, `Cells` accessor, brute-force test of random layouts; keep `Machine.X/Y/Z` as the anchor |
| 3 spans | L | Joined floors touch the body shell, stairs and models | Spans only mark seams as open; the body already walks open seams between hull modules; mesh audit `spans` layout; start with stations |
| Docs | S | Numbers drift between efforts | Renumber on rebase |

Other risks: the shared 32-bit neighbour mask (spans pass a separate parameter); content hash changes when machines and modules are added (old saves and logs refuse by design as in earlier content ADRs; legacy tests use the frozen fixture); editor complexity (the collar tool and the span palette are separate small tools); the owner's other effort on item and machine visuals (stage 2 touches model files minimally).

## 9. Decisions
1. A collar on a module face, not a pod (built). 2. Footprint masks up to 4 x 4 inside one module, anchor plus turn (stage 2). 3. Spans grouping ordinary cells, 2 and 3, one kind per span (stage 3). 4. One extended command tag for the optional parts from stage 2 on. 5. Flags in the third flags byte, log versions 27, 28 and 29, ADRs 0047 to 0049. 6. Prices, throughputs and research costs are proposals and are tuned with the stat panel and the balance bot, not here.
