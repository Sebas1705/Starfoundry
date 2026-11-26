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

## Save as blueprint: saved designs (phase 5, [ADR 0045](../adr/0045-saved-blueprints-live-in-user-data-and-the-walkthrough-is-client-only.md))
The phase 2 stub (one slot) became a list. **Save** (and `Ctrl+S`) replaces the design that was last saved or loaded, or opens the list when there is none; **Load** (and `Ctrl+O`) opens the list. The list (`DesignsPanel`) is modal and has a name field and a variant field:
- **Save** stores the draft under that name and variant (a station design carries its machines, belts and links; a ship design its modules with rotation). An existing name asks for a second press.
- Click a row to select it, then **Load** (the design becomes the draft, one undoable step per half; the cost, validation and the simulation's verdict still decide whether it can be applied), **Rename**, **Duplicate as variant** (same name, the label in the variant field), **Preset** (pins it to the top) or **Delete** (second press).
- Ship and station lists are separate; at most 64 designs per kind. Files are `user://blueprints/<structure>_<name>[__<variant>].bp`; a phase 2 `ship_slot1.txt` is imported once as "Slot 1 (imported)". Nothing is written to the save game or the command log.
- Format: `starfoundry-blueprint 2` (version 1 still reads). Engine-free code: `BlueprintLibrary`, `BlueprintDesignFormat`, `IBlueprintStore` in `src/Starfoundry.Ui/BlueprintLibrary.cs`; `UserBlueprintStore` and the panel in `game/BlueprintEditor.Designs.cs`.
- **Shipped presets (miner, hauler, gunship, scout of spec E1) are not done**: the editor's presets are the player's own pinned designs. They need balance and review; proposed as content data later. Hull skins, cockpit styles and other module variants of E1 are a separate content task.

## Walkthrough preview (phase 5)
Press `B` (or the walk button next to the views): the camera drops inside the selected module (else the cockpit of a ship, else the core of a station) at `BodyMotion.EyeHeight` above its floor, with a 72 degree field of view, level, looking to the bow. Right drag or `Q`/`E` look around, the arrow keys walk (1.2 cells a press, kept inside the modules of that deck; the decks above the eye are hidden), `B`, `T`, `Esc` or any other view leaves it. It is preview only: clicks do nothing, the grid, selection and cursor are hidden, the ghost tints stay. Engine-free camera maths in `BlueprintCamera` (`SetWalk`, `WalkBy`), tested.

## On foot (phase 5)
Only the repair and undo shortcuts remain on foot, with the commands that already exist (no new command): right click demolishes what the crosshair is on (belt, machine) and repair works as before. In a world with the floor plan the build menu closes machines and belts with the hint "Machines and belts are drawn on the station's floor plan (F at the core)" (`FootTools`); the module tool and the station founder stay.

## Performance of large stations (measured, 2026-11-26)
`BlueprintPolishTests.A_station_of_60_modules_and_400_parts_*` on a dev laptop (Debug build, loose budgets asserted): a station of 60 modules with 400 machines and belts: opening the session 1.4 ms, drawing the 400 parts click by click 398 ms (about 1 ms a part), one edit (diff, cost, stats, validation and the simulation's verdict recomputed) 1.2 ms, `CheckBlueprint` 0.6 ms, apply 14 ms, save plus load of the design 13 ms, one simulation tick with the whole factory standing 0.09 ms (machines idle, belts empty: this measures the loops, not a running factory). Budgets asserted: edit under 250 ms, check under 1 s, apply under 2 s, tick under 20 ms. The cost that is not measured is drawing the 3D scene: `RebuildScene` rebuilds every ghost and real node after each change (module models are cached), which needs a window to time; with 60 modules that is about 120 nodes.

## Known limits
- **Rotation is view only.** `DesignerApply` carries modules by kind and cell, so a part's rotation is not sent and does not make the draft dirty. The ghost turns about its centre so the future can use it.
- Mirror is across the ship's centre plane `x = 0` (a copy is skipped when its cell is taken or invalid); the plane is not yet draggable.
- A draft may be temporarily invalid (a part far from the rest); the designer's "must touch a module" rule became a validation error, which is what the red hatch shows.
- The hold must still carry its load after the redesign (the simulation says so).
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

## Phase 3: stations in the same editor ([ADR 0043](../adr/0043-stations-are-edited-as-blueprints-with-applyblueprint.md))
- Opens on a station with `F` while aiming at its **core** (the core console) and from the ship computer's Designer tab when the ship is docked at one of your stations and no shipyard is at hand. The title reads "3D station plan".
- The palette is the station catalogue (reactors, cargo, bays, defence, hull, shipyard, drone bay, shield, antennas, dock); no cockpit, engine, hardpoint or core. The stats panel shows power, hold, defence, shields and hull points.
- Validation adds "the core must stay" and "a dock module is missing" (station wording) and drops the ship-only checks; the simulation's own refusals (a module that holds a machine, the docked port, range, research, items) still come from its verdict, so the list never reads valid for a draft `ApplyBlueprint` refuses. Items are paid from the hold, then the station's stock.
- Apply sends one `ApplyBlueprint`. In new worlds the in-world module tool is refused on stations (`Reject.UseBlueprint`); legacy worlds and logs keep it.
- Headless: the second half of `--blueprint-check` opens the editor on a station, checks the palette, the layouts, the diff, cost and verdict against the simulation, the core and dock errors, and applies a module.

## Phase 4: the floor plan of a module ([ADR 0044](../adr/0044-the-station-interior-is-drawn-on-a-floor-plan.md))
- In a station, select a module that allows building (a bay, the core, a defence module) and press the **Floor plan** button (the build icon in the top bar), or double click it. The 3D view gives way to a top-down plan of that module's 8 by 8 floor; **Back to the 3D plan** (or `Esc`) returns.
- Tools (left): select, machine (pick one from the list: smelter, assembler, depot, unloaders, turret, wall and so on, with their costs), belt (drag along the grid; `Shift` bends Z first; a click makes a one-cell belt), erase (or right click), undo and redo, the belt tier, and **Clear the module**. The machine panel under the list shows the selected machine and a button that cycles its recipe.
- Ghosts: parts the draft adds are outlined green, parts it removes stay as red ghosts, the cell under the pointer is green when the active tool may act there and red with the reason as a tooltip (no module, not on the floor, the module takes no such thing, the cell is taken). Every belt cell has an arrow for the flow; a red dot marks a belt end that leads nowhere, an amber one a start that nothing feeds.
- The cost, stats and validation panels keep working: the interior is priced with the modules (new parts full price, removed ones half back), the machines' power draw makes the power balance and its deficit warning, belt gaps are warnings, parts left in a missing module or off the floor are errors. Apply sends one `ApplyBlueprint` with the interior (tag 66) only when the plan was touched.
- Headless: the third part of `--blueprint-check` opens the core's plan, checks the nodes and the layouts in both languages, draws a smelter, a belt and a depot, checks cost and verdict against the simulation, the gap warnings, the machine panel, applies and reloads.
- Not done: a storage crate machine, carrying ground stacks with a ship (both optional in the request), splitters as their own piece (a belt that feeds two belts is one), moving a machine for free, **and nobody has seen the plan on screen** (pointer feel, drawing, icon sizes, the arrow glyphs, double click, the 8 by 8 plan at 960x540 and 1920x1080).

## What remains
Phase 5 is built and checked headless (`--blueprint-check` is 176 checks, all PASS; unit tests for the library, the walkthrough camera, the large station and determinism). Left for the owner: **visual QA of everything above**, hand testing, shipped presets and the module variants of spec E1, and `--blueprint-shot` (it needs a window: the headless renderer draws nothing, so it was reviewed and compiled but **not run**). Its frames: `path.orbit.png`, `.top.png`, `.section.png`, `.done.png` (ship), then `.station.orbit.png`, `.station.top.png`, `.floor.png`, `.walk.png` and `.designs.png` (station, floor plan, walkthrough, design list). Phase 2 needs no ADR (no sim rule changed); `ApplyBlueprint` is ADR 0043; phase 5 is ADR 0045 (no sim change).

## Needs visual QA (phase 5 additions)
1. The design list: layout at 960x540 and 1920x1080 in es and en (the panel fit is checked headless, not how it looks), the row text, the toggle highlight of the selected row, focus and typing in the two fields while the editor's keys are silent.
2. The walkthrough: the eye height and field of view inside a module, whether the module models read from the inside (back faces, ceilings), the ghost tints over the interior, the direction of the right drag and the arrow walking, the hidden upper decks.
3. The walk button icon and the `B` and `Ctrl+O` caps in the F1 help and the key page.
4. In a floor-plan world, the build menu entries for machines and belts shown closed with the new hint.

## Far from the origin (not done)
The infinite rock field ([ADR 0040](../adr/0040-infinite-rock-field-generated-on-demand.md)) has no world limit and the client draws with `float` world coordinates (cells, converted from the simulation's integers at about 60 call sites in `Play*.cs`). Far from home, float precision shows as jitter (about 0.06 cells at a million cells). A floating origin means re-basing every one of those sites (or parenting the world under a root and re-centring it as the ship moves while keeping positions local), which cannot be done safely without looking at it on screen, so it is **documented, not changed**. Proposal: one `WorldOrigin` offset subtracted when converting simulation integers to render positions, moved in whole sectors when the ship passes a threshold, and a headless test of the conversion.
