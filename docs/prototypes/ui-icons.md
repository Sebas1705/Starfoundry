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
- Build menu: tabs with icons, a grid of icon tiles (key, padlock or red cross badge), a detail pane for the focused or hovered tile with the cost as icons, power and the reason it is locked or short. Research: a tree with a band per field, round nodes (icon in the field colour, progress ring, padlock or tick), lines to requirements (bright when done) and a detail pane.

## Shared theme
`game/UiTheme.cs`: colour tokens, type scale (11, 12, 14, 16, 22), spacing, `Card` (HUD glass), `Dock` (menu panels), `Tile`, focus ring and a `Theme` (`UiTheme.Create`) set on the HUD root, the menus, the new game screen, the end screen and the story dialog. `UiKit` delegates to it. Pages taller than the window (Settings on a small screen) scroll.

## Checking it
- `Gallery.tscn --focus=icons --shot=out.png [--lang=es|en] [--page=1..3]` (add `--resolution 1280x1500` to see it all): every icon at 32, 16 and 24 pixels with its name, plus unregistered ids to show the fallback.
- `--ui-shot` also saves `path.research.field.png`; `--layout-shot`, `--hud-shot`, `--keys-shot`, `--menu-shot` are unchanged and show the new look.
- Tests: `IconCatalogTests` (every item, machine, research node, enemy, archetype, star, obstacle, module kind, belt tier, field, build tab, key panel row and guided step has an icon; every icon has a glyph and an es and en name; fallbacks), `ResearchTreeLayoutTests`.

## Not done
The detail pane shows the big icon, not a rotating 3D preview. Toasts have no category icons yet. The tree's basic band is tall and scrolls.
