# 0051. Large modules are spans of ordinary cells with a joined floor; log version 29

- Status: proposed
- Date: 2026-11-29
- Area: simulation, content, client
- Builds on: [ADR 0018](0018-modular-structures.md) (modules), [ADR 0031](0031-construction-rules-fabrication-bays-and-floor-building.md) (the floor), [ADR 0043](0043-stations-are-edited-as-blueprints-with-applyblueprint.md) and [ADR 0044](0044-the-station-interior-is-drawn-on-a-floor-plan.md) (blueprints and the floor plan), [ADR 0050](0050-machines-cover-several-cells-in-irregular-footprints.md) (footprints)
- Design: [multi-cell machines, large modules and the small dock](../superpowers/specs/2026-11-27-multicell-and-large-modules-design.md), stage 3 of 3. Log version 28 and ADR 0050 are the footprints, 0049 the save slots, so this takes 29 and 0051.

## Context
Every module is one 8 x 4 x 8 box. The owner wants bigger modules so that bigger factories fit, and the multi-cell machines of ADR 0050 are held to the floor of one module (8 x 8 cells) because the wall between two modules would cut them.

## Decision
- **A large module is a span: a square of 2 x 2 or 3 x 3 ordinary module cells of one kind in one layer, grouped.** Every covered cell stays an entry of the module map, so hit volumes, connectivity, ports, power, hit points and the module limits work cell by cell as before. A structure keeps a sorted set of spans (`Structure.SpanSet`: lowest corner and side), cloned, hashed only when not empty and checked by `ValidateInvariants` (whole square, one kind, allowed size, no overlap).
- **The floor is joined.** Two cells of the same span share one floor (`BeltNetwork.SameFloor`): a machine footprint (ADR 0050) may cross the seam between them, so a 3 x 3 large bay is a 24 x 24 cell floor. The cell grid does not change: a cell is still `(x, y, z)` and its module `ModuleOfCell`. Footprints still may not cross the seam between two different modules.
- **Which kinds, how large (`ModuleKindIds.MaxSpan`).** Station: fabrication bay, cargo and hull up to 3; reactor, defence and drone bay up to 2. Ship: cargo and hull up to 2 (a ship holds no automation, ADR 0039). Cores, cockpits, engines, hardpoints, antennas, shields and docks stay single. Collars (ADR 0047) sit on the outer faces of a span like on any module; an inner face has a neighbour and is no port.
- **Price and research.** A module built as part of a new span costs 85 percent of its price (`ModuleKindIds.SpanPricePercent`, rounded up per item, a *proposal*); cells that already stood give no discount, and taking a large module down refunds half the full price, so building and removing never gains. In a campaign world the research `core:large_structures` (500 plates, 60 circuits, 60 titanium plates; needs heavy industry) unlocks the new ability `large_modules`; sandbox and free build have it.
- **Commands.** `DesignerApply` and `ApplyBlueprint` may carry the spans (`DesignSpan`, log tags **72** and **71**: a flags byte for the interior, the collars and the machine turns, then the parts); without them the large modules whose cells all still stand are kept, and one that lost a cell falls apart into ordinary modules (removing a cell with the module tool does the same). The modules of a span are still listed one by one. A bad span (not a full square, a mixed kind, a kind that may not be that large, overlapping, size not 2 or 3) is refused with `Reject.BadSpan`; a campaign world without the research refuses with `Locked`. Preview and apply are one function (`PlanSpans` inside `BlueprintCore` and `DesignCore`). Log version 29.
- **Compatibility.** `WorldRules.LegacyLargeModules` (third flags byte, bit 4, value 16; off in every preset and in the new-game screen) refuses spans (`NotAllowedHere`). Logs of versions 5 to 28 read with it on and replay as recorded; the reader refuses tags 71 and 72 in them. It hashes as default rules. The content hash changes with the new research node; older logs replay against their frozen fixtures.
- **Editor.** The palette lists *Large bay 2 x 2*, *3 x 3* and so on under the single kinds; a click places the whole square from that cell (the ghost shows the square, green or red); delete on any cell removes the whole large module; a large module is not moved (it is taken down and built again); undo, revert and the floor plan follow (the floor plan treats the joined floor as one). A frame is drawn round every span in the 3D plan and in the station.

## Consequences
- Benefits: a 24 x 24 floor for the multi-cell machines and long belts, no change to ports, collisions, power or any older command, and the module limit counts cells so balance is unchanged.
- Costs and limits: the cells of a span keep their own models, so the doorways between them stay and the frame girders are the only sign of one hall (a model with open seams needs a free mask bit and is left for a later visual pass); the body and ray collision are the per-cell boxes as before. No scenario or template uses spans yet: they are built in the editor.
- Checked by `LargeModuleTests` (the table, price and discount, every refusal, the research gate, the legacy flag, falling apart, footprints across the seam, ship spans, tags 71 and 72 with the version gate, replay and clone and the hashed span, 150 random blueprints with preview equal to apply, the draft and the session) and `--blueprint-check` (palette, click, frame, remove whole, undo).
- Balance: the scripted player builds no spans; `--structures --minutes 150 --delay=6`, mm:ss:

  | Preset (band) | seed 20261121 | seed 20261120 | seed 777 |
  | --- | --- | --- | --- |
  | Relaxed (44-50) | 46:15 | 46:37 | 45:46 |
  | Normal (48-53) | 48:11 | 49:13 | 48:41 |
  | Hard (51-56) | 51:36 | 55:31 | 51:47 |

- Not decided here: open seams in the models and the walk, a span of different kinds (a hall with a hull skin), spans in templates and scenarios, spans of cores, saved designs carrying spans (the design file keeps modules only, so a loaded design brings the cells and the editor asks for the span again).

## Alternatives considered
- **A. One big module kind with a bigger box.** Rejected: a new box size touches the grid, ports, the codec and every model.
- **B. A span as one entry with its own coordinates.** Rejected: collisions, connectivity, power, damage and ports would all learn a second kind of module.
- **C. Spans of any kind mix.** Rejected: one kind keeps the rules and the models simple.
