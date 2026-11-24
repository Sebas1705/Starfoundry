# UI icons and the shared look

Status: implemented prototype. Everything is drawn by code: no external art, no copied assets, no network.

## Icon system
- **Glyphs** (`src/Starfoundry.Ui/IconGlyphs.cs`): one SVG fragment per glyph on a 24 by 24 grid, stroke 1.75, round caps and joins, an optional 30 percent fill for the main mass. White: the game tints them.
- **Registry** (`IconCatalog`): an icon id is `kind:id` (`item:core:iron_ore`, `machine:core:smelter`, `module:drone_bay`, `belt:mk2`, `research:core:logistics`, `field:physics`, `enemy:*`, `obstacle:*`, `star:*`, `arch:*`, `tool:*`, `status:*`). Items use a shape for the form (ore, ingot, plate...) and a material tint, so colour is never the only cue. Research nodes take their field's colour. An unregistered id (new content, a mod, a module added by another branch) gets the generic glyph of its kind and one `ICON` log line; nothing breaks.
- **Names** (`IconCatalog.Name`): game data reuses the existing `Loc` names; tools and status icons have `icon.*` strings (es and en, `LocIcons.cs`). Every icon shows its name as a tooltip on hover.
- **Rasterising** (`game/Icons.cs`): `Icons.Get(id, size)` loads the SVG with `Image.LoadSvgFromString` at size times the window scale (2x up to 2560 wide, 3x above), cached per glyph and pixel size, so icons are crisp at 720p and 1080p and above.
- **Controls** (`game/IconControls.cs`, `game/HudParts.cs`): `IconRect`, `IconCount` (icon plus number, red when short), `GaugeBar`, `GaugeRow` (icon, gauge, value), `IconLabel`, `HoldStrip`, `ToolReadout`, `StepStrip`.

## Where icons are used
- Ship card: header icon, hull, power and integrity as gauge rows, hydrogen with the time left. Flight card: speed. Hold: a gauge plus one icon and count per item. Tool in hand: its icon and the cost as icons.
- Key panel: every row with an action icon next to its key caps. Markers: an icon chip (deposit item, enemy type, gate, station, port, ice, drones); the full label only for the selected, the important and the near ones, the others show icon and distance.
- Steps panel: a strip of step icons (done, current, pending). Wave banner: alarm or enemy icon.
- Build menu: tabs with icons, a grid of icon tiles (key, padlock or red cross badge, name that fits, price row), a detail pane for the focused or hovered tile with the cost as icons, power and the reason it is locked or short. Research: a tree with a band per field, round nodes (icon in the field colour, progress ring, padlock or tick), lines to requirements (bright when done) and a detail pane.

## Shared theme
`game/UiTheme.cs`: colour tokens, type scale (11, 12, 14, 16, 22), spacing, `Card` (HUD glass), `Dock` (menu panels), `Tile`, focus ring and a `Theme` (`UiTheme.Create`) set on the HUD root, the menus, the new game screen, the end screen and the story dialog. `UiKit` delegates to it. Pages taller than the window (Settings on a small screen) scroll.

## Checking it
- `Gallery.tscn --focus=icons --shot=out.png [--lang=es|en] [--page=1..3]` (add `--resolution 1280x1500` to see it all): every icon at 32, 16 and 24 pixels with its name, plus unregistered ids to show the fallback.
- `--ui-shot` also saves `path.research.field.png`; `--layout-shot`, `--hud-shot`, `--keys-shot`, `--menu-shot` are unchanged and show the new look.
- Text fit: `tools/ui-audit.ps1` (see Text fitting below). Tests: `IconCatalogTests` (every item, machine, research node, enemy, archetype, star, obstacle, module kind, belt tier, field, build tab, key panel row and guided step has an icon; every icon has a glyph and an es and en name; fallbacks), `ResearchTreeLayoutTests`.

## Not done
The detail pane shows the big icon, not a rotating 3D preview. Toasts have no category icons yet. The tree's basic band is tall and scrolls.

## Text fitting
Long Spanish names must never leave their tile, so fitting is shared (`game/UiFit.cs`), not per string:
- `UiFit.Fit` picks the largest font size from a maximum down to a minimum at which a text wraps to at most N lines of a given width (a word wider than the box counts as not fitting, so words are never broken). Only below the minimum does the text end with an ellipsis, and then the full text is in the tooltip.
- `FitLabel` is the label that uses it (max lines, max and min font, width); its minimum height is the height of the lines it needs, so a tile is sized by its content. Used for the build tile names (2 lines, 12 down to 10), the detail pane (name 1 line, role 2, description 3, status 2) and the story level cards of the New game screen (description and goals 2 lines each; the card grows).
- `UiFit.ShrinkButton` does the same for one-line buttons (the build menu tabs shrink their text, then clip it with the full text as tooltip).
- `ScrollWithHint` wraps a scroll area with a fade and a chevron at the edge that has more content (build tiles, story levels); the scroll bars have a visible rail and grabber in the theme. `CostStrip` is the price row of a tile: icon and number pairs that fit, then "+n" (red when the hold cannot pay).
- The build menu tile is a fixed grid cell computed from its content: quick key at the top left, padlock or red cross badge at the top right, icon, name (2 lines), price row. Locked tiles keep a readable name (muted blue, not near-black) and no price; a white ring marks keyboard focus (gold means the tool in hand). Columns follow the dock width (at least 92 px per tile).

## The text-fit audit
`tools/ui-audit.ps1` (Windows; `-Sizes 1280x720` for one size, `-Shots` to keep a picture of each screen) runs Godot windowed, at low priority, for es and en:
- `--ui-audit [--scenario=03_under_siege] [--audit-sizes=800x600,1024x600,1280x720,1920x1080] [--audit-shots=dir]` in the game: for every size it opens the HUD with toasts, the info cards, help, the key panel contexts (flying, aiming, walking, map, tool in hand), the build menu on every tab, the research panel on several nodes, the ship designer, the pause menu with Settings and Controls, both end screens and the story briefing, and walks the control tree (`game/UiAudit.cs`).
- `--ui-audit-menu` does the same for the main menu, Settings, Controls and the three New game tabs.
- It reports (`UIAUDIT ... FAIL`): a text wider than its label or button (`OVERFLOW`, `CLIPPED` without a tooltip), lines hidden by a line limit (`LINES_HIDDEN`), a word broken in the middle, a label taller than its box (`TALL`), a control leaving its parent (`LEAVES_PARENT`) or the viewport (`OFFSCREEN`), text panels overlapping each other (`OVERLAP`; the help page is a modal overlay and exempt), a HUD card within 4 px of the screen edge (`MARGIN`) and marker labels outside the view or under a docked panel (`MARKER_OFFSCREEN`). A cut text that has its full text as a tooltip is only a note (`ELLIPSIS`). Controls scrolled out of a scroll container are skipped.
- A stress pass reopens every build tab with a long suffix on every name; those cut names are counted as notes, not failures, but nothing may overflow.
- The run quits with exit code 1 on any failure; the script prints every failing line. It is clean for es and en at 800x600, 1024x600, 1280x720 and 1920x1080.
- Limits: native tooltips are separate windows and are not walked; the window scale at 800x600 is 0.625 of the 1280x720 layout, so a 12 px font is about 7.5 px on screen there.
