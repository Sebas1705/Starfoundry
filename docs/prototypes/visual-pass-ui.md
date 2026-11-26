# Visual pass: interface

First time the recent interface work was looked at on screen. Screenshots came from short windows at 1280x720 (and 1920x1080 for the blueprint editor) on a play copy, with the existing flags: `--blueprint-shot`, `--layout-shot`, `--inventory-shot`, `--keys-shot`, `--cockpit-shot`, `--arsenal-shot`, `--defence-shot`, `--menu-shot`, `--newgame-shot`, `--rebind-shot` and `--ui-audit --audit-shots=dir`. No keyboard or mouse automation was used, so anything that needs input (drag direction, hover and focus states, click feel) was **not** checked. Spanish was the main language (it has the longest strings); English was checked by `--ui-audit` only, because `--blueprint-shot` ignored `--ui-lang=en` in this run.

Images are in [visual/](visual/), 640 to 960 px wide.

## Findings

| # | Where | Found | Status |
|---|-------|-------|--------|
| 1 | Blueprint editor, cost panel | The price, refund and net columns were 0 wide (a clipped label has no minimum width, so the grid gave all the width to the name): the panel listed item names with no numbers and no header. | Fixed (minimum widths). |
| 2 | Blueprint editor, palette | Rows were ragged (button width depended on its cost icons) and long names were cut ("Sensor de corto alcanc"). | Fixed: one full-width tile per module, name on top, costs under it. |
| 3 | Blueprint editor, floor plan | The plan was drawn over the live game: the HUD status card showed through the top bar and the key panel through the side panel. | Fixed: opaque backdrop. |
| 4 | Blueprint editor, floor machine list | Same ragged rows, and the long names were clipped with no ellipsis. | Fixed: one cost column width, ellipsis. |
| 5 | Blueprint editor, design list | "Duplicar como variante" read "Duplicar co" (buttons were clipped at 90 px). | Fixed: the action row wraps, buttons keep their text. |
| 6 | Blueprint editor, side panels | Panels were flush with the window edge (`--ui-audit` FAIL MARGIN) and the audit left the editor open over every later step. | Fixed: 6 px floating margin; `CloseAllPanels` closes the editor. `--ui-audit` is clean in es and en. |
| 7 | Blueprint editor, side panel | The stats list was squeezed behind a scroll while the cost panel was half empty. | Fixed: heights rebalanced (cost 3.2, stats 4.8, validation 3.4). |
| 8 | HUD toasts and the hint line | Text with only a black outline over a bright floor or wall (orange and green on white and cyan) was hard to read. | Fixed: a soft dark pill behind each toast and behind the hint line (`ToastRow`, `HintLabel`). The column is laid out in the same frame, which also stops a one-frame mismatch that made `--ui-audit` flaky. |
| 9 | Inventory, filter chips | "Combustible" and "Municion" were cut at 1280x720; the sort button lost its label. | Fixed: a name that does not fit leaves the icon alone (the tooltip has the name). |
| 10 | Star map list | Columns were padded with spaces in a proportional face, so they did not line up. | Fixed: monospaced at 10 px, narrower columns. The text is small. |
| 11 | HUD target card | "mineral de hierro: quedan 12000, 53 celdas" was cut with an ellipsis. | Fixed: shorter text ("12000 · 53 celdas") and a wider row. |
| 12 | Flight card, speed gauge | At rest the thrust tick with its arrow heads looked like an I-beam text cursor. | Fixed: a small plain tick inside the track at rest. |
| 13 | Blueprint editor, orbit and top views | Ghost tints read well (green new, red removed, yellow moved); the hatched red box for an invalid part is dense and busy over a small module. No z-fighting seen at these angles; bow at the top in the top view. | Left (taste: lighter hatch). |
| 14 | Blueprint editor, tool buttons | The second row (copy, paste, upgrade, undo, redo) reuses placeholder glyphs; with no labels they are hard to tell apart. | Left: needs real icons ([ui-icons](ui-icons.md)). |
| 15 | Blueprint editor, walkthrough | Reads well from the inside (floor, ceiling, door frame, lights). | OK. |
| 16 | Arsenal bar, seated | At 1280x720 it overlaps the bottom edge of the central cockpit display while it is shown. | Left (it fades after four calm seconds; moving it needs a decision about the display). |
| 17 | HUD info card (Detailed level) | "Integridad 100 %" wraps and leaves the "%" on its own line. | Left (minor; the Detailed level is the busy one by design). |
| 18 | Defence HUD, wave banner | In the 2560x1350 `--defence-shot` the alert icon appears to overlap the first letter of "OLEADA". Not reproduced at 1280x720. | Left, unverified. |
| 19 | New game screen | The campaign description is left aligned while the rest of the page is centred; a large gap under the seed row. | Left (cosmetic). |
| 20 | Ship computer tab frame | A click on a tab landing on a research node: another agent is on it (branch `fix/dock-collision-and-computer-tabs`). Not touched here. | Not mine. |
| 21 | Menus, pause, controls, end screens, build menu, research tree, cockpit displays | Looked at: layout, spacing and type are consistent, no overflow found. The cockpit display textures are readable and the alert states are clear. | OK. |

## Not verified
- Orbit, pan and zoom direction, click-to-place feel, hover, pressed and focus states, tooltips and the cursor in the editors (they need input).
- English screenshots of the editor and the 1920x1080 set for everything but the editor and the inventory.
- The machine panel and the workbench panel (no scripted screenshot exists for them).
- Performance of a 128-module ship in the editor.

## Checks
`dotnet test` 1957 passed; `--ui-audit` errors=0 in es and en (only frame-time budget notes appeared, when other programs loaded the machine); `--blueprint-check` 176 PASS.

## Before and after
- Editor, 3D plan: [before](visual/editor-orbit-before.png), [after](visual/editor-orbit-after.png), [after at 1920x1080](visual/editor-orbit-1080-after.png).
- Editor, floor plan: [before](visual/editor-floor-before.png), [after](visual/editor-floor-after.png).
- Editor, design list: [before](visual/editor-designs-before.png), [after](visual/editor-designs-after.png).
- HUD toasts and hint: [before](visual/hud-toasts-before.png), [after](visual/hud-toasts-after.png).
- HUD details and star map: [before](visual/hud-details-map-before.png), [after](visual/hud-details-map-after.png).
- Inventory: [before](visual/inventory-before.png), [after](visual/inventory-after.png).
