# Blueprint editor, phase 2: the ship editor shell

Status: built and checked headless (2026-11-26); **not yet seen on screen** (see "Needs visual QA"). Spec: [blueprint editor design](../superpowers/specs/2026-11-26-blueprint-editor-design.md), section 3 and phase 2. It sits on top of the ship designer of [ADR 0032](../adr/0032-weapons-as-hardpoint-modules-and-the-shipyard.md) and still applies with the same `DesignerApply` command, so the simulation, the command log and the rules are untouched.

## What it is
A full-screen mode for the ship's structure. It opens when the ship is docked at one of your shipyards:
- from the ship computer's **Designer** tab (`Tab`, then the tab), and
- with `F` at the shipyard console (the same path).

The **Classic designer** setting (Settings, `classic_designer` in the settings file; off by default) keeps the earlier grid panel as a fallback until the 3D plan has been reviewed on screen. The classic panel has a **3D plan** button that opens the new editor. Closing the editor returns to flight; the draft is discarded (Esc twice when it has changes).

## What is on screen
- **3D plan** (centre, full window): the real structure, always visible, with an orbit/pan/zoom camera, a top-down plan (bow at the top) and a deck-by-deck section (a slider and `PageUp`/`PageDown`; the decks above the cut are hidden and not clickable). A grid shows the deck being edited.
- **Ghosts** over the real structure: added parts green, parts that go red, parts that only move yellow (a trail joins the old and new cell), invalid parts red hatch. The ghosts are the real module models with a tint, so what you see is what the ship will look like.
- **Palette and tools** (left): the module catalogue with icons and the cost of each module (the same catalogue the designer offers); tools select, place, move, rotate, delete, mirror, copy, paste, upgrade, undo and redo, each with its key in the tooltip.
- **Cost panel** (right, always visible): per item with its icon the full price of the new parts, the refund of the removed ones (half, as `DesignerApply` does), and the net; a row turns red when the hold and the station stock together cannot pay it, with a "Missing" line. A moved part costs nothing. In a free-build world it says so.
- **Stats panel**: power balance, mass, turn rate (from `content/ship_handling.json`: a heavier ship turns slower), top speed, cargo, mining and gun strength, defence, shields and hull points, each with its change against the ship as it is (green better, red worse).
- **Validation list**: errors block Apply, warnings do not; each entry has an icon, a severity and es/en text, and **clicking it frames the part** in the 3D view and selects it. Local checks: a missing cockpit, engine, reactor or cargo module, parts cut off from the rest, the docked port removed or blocked, power deficit, view blocked by a new pod, no mining laser, a much slower turn, too many modules. Anything else the simulation refuses (overlap with the station, room in the hold, research, items) is added from its verdict, so the list never reads valid for a draft `DesignerApply` would refuse.
- **Footer**: the status line, a legend of the ghost colours, **Save**/**Load** (a slot hook, see below), **Revert** (undoable) and **Apply** (one `DesignerApply`; disabled until the draft is valid and different from the ship).

## Controls
Registered in `InputActions` under the new context "blueprint editor" (`InputContext.Blueprint`, help group "3D ship plan"), so the F1 help and the key rebinding page list them and the collision test covers them. Combinations with `Ctrl` are fixed pseudo keys (`@ctrlz`, ...) that the editor resolves itself.

| Action | Key |
| --- | --- |
| Orbit | right mouse drag, or `Q` / `E` |
| Pan | middle mouse drag, or the arrow keys with nothing selected |
| Zoom | mouse wheel |
| Use the tool | left click (the free cell next to a module face for Place, the module for the rest); `Shift`/`Ctrl` click adds to the selection |
| Tools | `V` select, `N` place, `G` move (click a module, then its new cell), `R` rotate selection (or the tool), `Delete` / `X` delete selection (or the tool), `M` mirror, `U` upgrade |
| Nudge the selection | arrow keys (one cell, ship axes) |
| View | `T` cycles orbit, top and section; `PageUp` / `PageDown` change the deck; `F` frames the selection or the whole ship |
| Edit | `Ctrl+Z` undo, `Ctrl+Y` redo, `Ctrl+C` copy, `Ctrl+V` paste, `Ctrl+A` select all, `Ctrl+S` save the blueprint |
| Apply / Revert | `Enter` / `Backspace` |
| Close | `Esc` (clears the move in hand, the selection and the tool first; twice when the draft has changes) |

The editor reads its keys before the focus system, so Enter and the arrows work while a button has the focus; `Tab` moves the focus between the panels' buttons.

## How it is built
- `src/Starfoundry.Ui` (engine-free, tested): `BlueprintDraft` (module set with kind, cell and a quarter-turn rotation, add, remove, move, rotate, upgrade, mirror across a plane, copy and paste of a group, snap, a bounded undo/redo history), `BlueprintDiff` (Added, Removed, Moved, Unchanged and Invalid sets for the ghosts; counts equal `DesignReport`), `BlueprintCost` (price, refund, net and missing by item; the net equals `CheckDesign`'s cost and refund), `ShipStats` and `StatRows`, `BlueprintValidation` (entries with severity, icon, es/en text and the cells to frame), `BlueprintCamera` (orbit, pan, zoom, top, section, projection, ray and picking, pure math), `BlueprintSession` (tool, selection, camera and the derived panels, recomputed from `SimWorld.CheckDesign` after every change), `BlueprintFormat` (the saved blueprint text) and `LocBlueprint`.
- `game/BlueprintEditor*.cs` (Godot): draws the session and forwards the mouse and keys; `game/Play.Blueprint.cs` opens, closes and applies it. The old `ShipDesigner` and `ShipDesignEditor` are reused (the catalogue, the adjacency helper, the flight maths) and unchanged in behaviour.
- Every part has a name for the agent harness: `BlueprintEditor`, `PlanView`, `PlanViewport`, `PlanCamera`, `TopBar`, `PalettePanel` (`Palette_<Kind>`, `Tool<Name>`), `SidePanel` with `CostPanel`, `StatsPanel`, `ValidationPanel` (`Validation_<n>`), `FootBar` (`ApplyButton`, `RevertButton`, `SaveButton`, `LoadButton`), `DeckSlider`, `View<Name>`, `CloseButton`. `play.call("BlueprintState")` returns one line with the tool, selection, ghost counts, cost, power, validation codes and node counts; `play.call("AgentOpenBlueprint")` opens it.

## Checks
- Unit tests: `tests/Starfoundry.Sim.Tests/BlueprintEditorTests.cs` (undo and redo invariants over random edit sequences, diff counts equal the simulation's for random drafts, cost by item equals `CheckDesign` for random drafts, missing items, stats, validation entries, camera, picking, saved format, the session and the registry).
- Headless self-check, prints one PASS or FAIL line per check (63 at the time of writing) and exits non-zero on a failure: `Godot --headless --path game -- --blueprint-check`. It docks the starter ship at a shipyard, opens the editor, asserts the named nodes and the panel layout at 960x540, 1152x648, 1280x720 and 1920x1080 in both languages (no overlap, inside the window, no truncated text), adds a module, compares diff, cost and verdict with the simulation, makes a cut-off module and a missing cockpit and checks the errors, undo and redo, mirror and the three views, applies the draft and checks the ship. Run agent builds through `tools/agent/quiet.ps1` and a play copy (`tools/agent/new-play-copy.ps1`).
- Screenshots (implemented, **not run yet**): `Godot --path game -- --blueprint-shot=path [--blueprint-size=WxH] [--ui-lang=es|en]` saves `path.orbit.png` (a new laser, the gun removed, a module cut off: green, red and hatch ghosts, the panels), `path.top.png`, `path.section.png` and `path.done.png`.

## Save as blueprint (a stub)
`Save` writes the draft to `user://blueprints/ship_slot1.txt` (`BlueprintFormat`, one line per module) and `Load` puts it back into the draft as one undoable step. Slots, names, presets and variants are for later; the format has a header version so it can grow.

## Known limits
- **Rotation is view only.** `DesignerApply` carries modules by kind and cell, so a part's rotation is not sent and does not make the draft dirty. The ghost turns about its centre so the future can use it.
- Mirror is across the ship's centre plane `x = 0` (a copy is skipped when its cell is taken or invalid); the plane is not yet draggable.
- A draft may be temporarily invalid (a part far from the rest); the designer's "must touch a module" rule became a validation error, which is what the red hatch shows.
- No walkthrough preview, no belts or machines (phases 3 to 5). The hold must still carry its load after the redesign (the simulation says so).
- The tool icons for move, mirror, copy, paste, undo and redo reuse existing glyphs (arm, tabs, depot, upload, rotate, cycle); they are placeholders until the icon set gets real ones.

## Needs visual QA (nobody has seen it)
Run `--blueprint-shot` with a window (and `--ui-audit` for the new panels) and look at:
1. The orbit direction: dragging right should turn the ship the way the pointer goes; vertical drag should tilt over the top; pan follows the pointer; the wheel zooms about the target. Flip the signs in `BlueprintEditor.Input.cs` if not.
2. The ghosts: that the tint overlay reads as green, red and yellow over every module model (some materials may hide it), that the red hatch box reads as hatched and is not too dense, the yellow trail and the moved-from tint, a replaced module (a red old and a green new on one cell), the invalid hatch over a cut-off module.
3. Depth: ghosts that overlap the real model do not z-fight; the grid and the selection and hover outlines are visible against the ship and the background; the hover cursor module of the Place tool.
4. The top view: bow at the top, the right side of the ship on the right; the section view hides the decks above the cut and the grid sits on the deck.
5. Layout at 1280x720 and 1920x1080 in es and en: the top bar (title, three view buttons, deck slider and label), the palette rows (icon, name, cost icons fit in the panel), the side panel's three sections (cost grid columns, stats rows, validation buttons wrapping), the foot bar. The headless check measures overlap and truncation, but not how it looks.
6. Cost panel: red rows and the "Missing" line when the stock cannot pay; the refund in green; a free-build world.
7. The new icons (tools and the tool glyph placeholders), the key caps in the tooltips and the F1 help group "3D ship plan", and the key rebinding page group.
8. Feel: click-to-place on the face you point at, the move tool, selection with `Shift`, keyboard-only use, the Esc ladder, the toasts, the editor over the pause menu and the F1 help.
9. The settings page check "Classic designer" and the classic panel's "3D plan" button.
10. Performance of a 128-module ship: the scene is rebuilt on every change (module models are cached by the model builder); look for a hitch while editing.

## What remains for phases 3 to 5
Station modules through `ApplyBlueprint` (phase 3), station interior floor plans with machines, belts and turrets and the machine panel (phase 4), then demolish and repair shortcuts, blueprint slots and variants, the walkthrough preview, performance of large stations, determinism and old-log replay tests and hand testing (phase 5). Phase 2 needs no ADR (no sim rule changed); the `ApplyBlueprint` generalisation will carry ADR 0039 or its successor.
