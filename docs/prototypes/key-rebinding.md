# Key rebinding

Phase 4 of the [controls redesign](../superpowers/specs/2026-11-25-controls-redesign-design.md). Every action of the `InputActions` registry can be given another key, with conflict detection, and the choice is saved in `settings.cfg`.

## Where
- Main menu and pause menu: *Controls*. Ship computer, Settings tab: *Keys and shortcuts*. Both open the same page (`game/KeyBindingsPage.cs`, hosted by `MenuScreen`).
- The page lists the actions in five groups (flying and walking, build tool, ship computer, docked, system) with the icon, name, the contexts it works in, the key cap and *Rebind*, *Clear*, *Reset*. A search box filters by name or key; *Reset all* restores every default. The list scrolls with a fade at the edge that has more rows.

## Rules (engine-free, `src/Starfoundry.Ui/KeyBindings.cs`, tests in `KeyBindingsTests`)
- **Slots.** An action with several keys (move, climb, roll, layer, tabs, map arrows) has one row per slot ("Move - forward"). Overrides are stored per action, one key per slot.
- **Conflict.** Two slots collide when both actions can be active together (`InputActions.ActiveSets`). So *run* (walking) and *boost* (flying) may share Shift, but a flying action cannot take a global key. Two slots of one action may not hold the same key either. The default table is tested to be collision free.
- **Swap.** When exactly one slot holds the key, *Swap* gives it the old key, if that leaves no collision (a swap inside one action, such as Q/E, works). With several holders there is only *Cancel*.
- **Reserved keys:** `Esc` (cancel, back, pause), `F1` (help) and `Tab` (the ship computer is read before the focus system can take it). They cannot be assigned to anything.
- **Fixed actions:** cancel, help, the computer key, every mouse action (`fire`, `place`, `demolish`, wheel) and the number rows (weapon pick, hotbar, tab pick: they select by position). Mouse buttons cannot be rebound because the registry has no rebindable mouse action; the page shows them as *Fixed*.
- **Optional actions** can be cleared (an unbound slot is written `None`, its cap is a dash): look, assist, scan, unload, load, next weapon, rotate, recipe, kind, the three computer shortcuts, map reset, HUD detail, quick save and load, fullscreen. Movement, interact, roll, tabs and the map keys need a key.
- **Capture.** `Esc` cancels; Ctrl chords and keys the game cannot read (Ctrl, Meta, function-less specials) are refused with a message; a reserved key shows an explanation and *Try another*. While a dialog is open the game ignores its own keys.

## Saving and applying
- `settings.cfg` gets one line per changed action: `bind.scan=J`, `bind.roll=E,Q`, `bind.assist=None`. Defaults write nothing, so there is nothing to migrate.
- Loading is tolerant: unknown actions, locked actions, unknown keys and extra keys are ignored (a missing slot keeps its default), ids are case insensitive, and if a hand-edited file leaves two actions on one key, the overridden actions involved go back to their defaults.
- `KeyBindingStore` (game) owns the live `KeyBindingEditor`, pushes every change to `InputActions` (key handler, key panel caps, F1 help) and saves through `SettingsStore`. *Reset to defaults* in Settings keeps the keys: they have their own reset.
- Held keys (move, climb, roll, run, boost, look) are read through `HeldKeys`, so a rebound key moves the ship too. A key still on its default is read by position (WASD stays WASD on any layout); a chosen key is read by the character it types.

## Checking it
- `dotnet test` (`KeyBindingsTests`, `InputActionsTests`).
- `godot --path game --resolution 1280x720 -- --rebind-shot=/abs/dir/rb` saves `rb.es.png`, `.en.png`, `.capture.png`, `.conflict.png`, `.reserved.png`, `.scrolled.png`. Use an absolute path.
- `--ui-audit-menu` (page *Controls* from the main menu) and `--ui-audit` (`pause.controls`, `computer.settings.keys`) in es and en are clean.

## Not covered
- Gamepads, several keys per slot, mouse buttons and chords (Ctrl+key) are not rebindable. Layout-aware rebinding of held keys on non-QWERTY keyboards was not tested on hardware.
