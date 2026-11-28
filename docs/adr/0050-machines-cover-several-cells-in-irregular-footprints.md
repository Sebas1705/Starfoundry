# 0050. Machines cover several cells in irregular footprints, with a turn; log version 28

- Status: proposed
- Date: 2026-11-28
- Area: simulation, content, client
- Builds on: [ADR 0004](0004-data-driven-content-and-one-cell-machines.md) (one-cell machines), [ADR 0031](0031-construction-rules-fabrication-bays-and-floor-building.md) (the floor), [ADR 0043](0043-stations-are-edited-as-blueprints-with-applyblueprint.md) and [ADR 0044](0044-the-station-interior-is-drawn-on-a-floor-plan.md) (the floor plan), [ADR 0047](0047-the-dock-is-a-small-collar-on-a-module-face.md) (the numbers before this)
- Design: [multi-cell machines, large modules and the small dock](../superpowers/specs/2026-11-27-multicell-and-large-modules-design.md), stage 2 of 3. Log version 27 and ADR 0047 are the small dock, so this takes 28 and 0050 (0048 is the release pipeline and 0049 is left to the save slots).

## Context
Every machine since ADR 0004 is one cell. The owner wants some automators to take more than one cell in irregular shapes (L, T, long, 2 by 2, plus), so that bigger factories fit and a layout is a choice about shapes and belts, not only about rows.

## Decision
- **A footprint is a mask in the content.** A machine type may have `footprint`: one string per row along +Z, one character per cell along +X, `X` filled and `.` empty, at most 4 by 4, one connected piece. Only crafters, labs and depots may have one in this stage. `["X"]` is the default, so no existing machine changes. The cells are listed relative to the **anchor**, the first filled cell in row order (`MachineDef.Cells(turn)`, anchor first).
- **A turn** of 0 to 3 quarter turns about the vertical axis (`Machine.Turn`, `BlueprintMachine.Turn`) rotates the mask about the anchor, which stays where it is. A one-cell machine has turn 0 only.
- **Where it stands.** Every covered cell must be a floor cell of **one** module whose kind allows the machine's role (ADR 0031), with no other machine or belt on it: `FootprintBlocked` when a cell leaves the anchor's module, `Overlap` when a cell is taken, `NotOnFloor`, `OutsideStation` and `KindNotAllowed` as for one cell. The simulation keeps one entry per covered cell in its cell table (`SimWorld.ClaimMachineCells`), so overlap, demolish and destruction free or claim all of them.
- **Belts.** A belt whose last cell touches **any** covered cell feeds the machine, and a belt whose first cell touches any covered cell is fed by it (blueprint links are checked this way and read back this way); the machine keeps one input and output buffer and the usual limit of two output belts.
- **Price, power, recipes.** One price and one power draw per machine, from the content; a multi-cell machine has its own recipes (batches). Demolishing a machine refunds half its price once, whatever the cells. A machine of the same type turned is a different machine (its cells differ): the blueprint removes the old one and builds the new one.
- **Commands.** `BlueprintMachine` gains `Turn`. A blueprint with a turned machine is written with the new extended tag **70** (a flags byte: interior, collars; then the usual parts, and a turn byte per machine); every other blueprint keeps tags 65, 66, 68 and 69 byte for byte. The in-world placement of machines (`PlaceMachine`, `PlaceMachineIn`) refuses a multi-cell machine (`NotAllowedHere`): they are drawn on a station's floor plan. The log version is 28.
- **Compatibility.** `WorldRules.LegacyFootprints` (third flags byte, bit 3, value 8; off in every preset and in the new-game screen) refuses multi-cell machines and turns in blueprints (`NotAllowedHere`). Logs of versions 5 to 27 read with it on and replay as recorded; the reader refuses tag 70 in them; it hashes as default rules. A machine's turn is hashed only when it is not zero, so worlds without turns hash as before. The older tests run against the frozen content fixtures, which have no footprint.
- **Content (proposal numbers).** Research `core:heavy_industry` (400 plates, 40 circuits, 40 titanium plates; needs electronics and advanced logistics) unlocks four machines and their recipes:

  | Machine | Footprint | Cost | Power | Recipes |
  | --- | --- | --- | --- | --- |
  | Large smelter | 2 x 2 | 30 ingots, 20 plates | 25 | iron, titanium, nickel ore: 4 to 4 ingots in 2.5 times the time of one (1.6 times the rate); quartz: 8 to 4 silicon |
  | Long assembler | 1 x 3 | 30 ingots, 20 plates | 30 | plates 6 to 3 in 4 s (iron), circuits 3 + 3 to 3 in 14 s: about 1.3 to 1.5 times the rate |
  | L fabricator | 4 cells | 30 ingots, 40 plates, 10 circuits | 30 | alloy, carbon fibre and composite plate in batches of 2 to 4 |
  | Reactor core | plus of 5 | 60 plates, 40 ingots, 20 circuits, 10 alloy | 45 | fuel cells: 8 hydrogen + 3 nickel plates to 5 cells in 20 s, 20 percent less hydrogen per cell than the cracker and twice the rate |

- **Editor and client.** The floor plan draws a footprint as one joined shape; the machine under the pointer shows its footprint, turned as it will be placed, green or red with the reason; the rotate key (and a Turn button) turns the selected machine, or the next one placed; a click, a recipe change or an erase on any covered cell acts on the whole machine; undo restores it. The machine list and panel show the size ("2 x 2 cells"). Models are built from the footprint (`Models.Machine(id, cells)`: a plinth under every cell and a housing of the type's own, with its animated "Glow*" parts), so one-cell models are untouched; the player's body and the aim ray collide with every covered cell.

## Consequences
- Benefits: bigger factories in the same floor, layouts that are about shape, and no change to any existing machine, blueprint, log or checksum.
- Costs: every code path that read "the machine at a cell" now goes through the cell table or a footprint; the content hash changes (new machines and research), so saves of earlier builds refuse by design as in earlier content ADRs, while logs of earlier versions replay with the frozen fixtures.
- Checked by `MultiCellTests` (the mask parser and its limits, the shipped footprints and turns, placement and every refusal, belts and links at any covered cell, demolish and refunds, tag 70 and its version gate, the flag, replay, clone and the hashed turn, 300 random floor plans with preview equal to apply, the draft and the session's turn key, a large smelter in a running line); the older blueprint, interior and replay tests are unchanged; `--blueprint-check` places, turns, selects and erases a large smelter on the open floor plan.
- Balance: the scripted player builds one-cell machines only and no research of the basic tree depends on the new node, so its times do not move. `--structures --minutes 150 --delay=6`, mm:ss:

  | Preset (band) | seed 20261121 | seed 20261120 | seed 777 |
  | --- | --- | --- | --- |
  | Relaxed (44-50) | 46:15 | 46:37 | 45:46 |
  | Normal (48-53) | 48:11 | 49:13 | 48:41 |
  | Hard (51-56) | 51:36 | 55:31 | 51:47 |

- Not decided here: footprints for turrets, walls and repair bays, a footprint across a module seam (large modules, stage 3, join floors instead), a machine on a ship, and the visual polish of the four models.

## Alternatives considered
- **A. A machine as a group of one-cell machines.** Rejected: one buffer and one recipe would have to be shared across several records, and demolish, damage and power would need a group id everywhere.
- **B. Footprints across module seams.** Rejected for now: the wall and doorway logic of ADR 0031 and the models would have to cut the machine; large modules (stage 3) give wide floors where this is not needed.
- **C. The turn only in the editor, not in the command.** Rejected: the cells a machine covers are part of the simulation (overlap, links), so the turn is sent and hashed.
- **D. A new tag per combination of optional parts.** Rejected: tag 70 carries a flags byte so the next optional part (stage 3) does not need a new tag.
