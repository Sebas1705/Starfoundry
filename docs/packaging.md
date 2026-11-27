# Packaging a Windows build

How the Godot project in `game/` becomes a standalone `Starfoundry.exe` that finds its content without the repository. Building, the installer, CI and releasing are in [release.md](release.md); this page covers how the exported game behaves.

## How the game finds its content

`ContentLocator` (`src/Starfoundry.Ui`, tested by `ContentLocatorTests`) looks for `core.json` in this order and uses the first that exists:

1. `--content=path/to/core.json` (a user argument, after `--`). When given, it is the only candidate: a typo is reported, not hidden.
2. `res://content/core.json`: inside the project; this is what an export contains.
3. `<folder of the executable>/content/core.json`: a loose file next to the exe, handy for modding or testing.
4. `res://../content/core.json`: the repository layout (`game/` beside `content/`), used when you press F5 in the editor.

If none exists, or the file is invalid, the game shows an error screen (Spanish or English by the saved language) listing every place it looked, with a Quit button; the same list goes to the console. `tools/Starfoundry.Balance` uses the same resolver (`--content=` as well).

`--content=` does not count as a scripted flag, so the main menu still opens.

## Why the build script copies the content

Godot can only export files that live inside the project folder, and `content/` is a sibling of `game/`. Duplicating the file in git would let the two copies drift, so `tools/release/build-windows.ps1` copies `content/` to `game/content/` for the duration of the export and deletes it afterwards (`game/content/` is git-ignored). For the same kind of reason it creates a throw-away `game/Starfoundry.Game.sln` (the exporter insists on one; a permanent one would make `dotnet build game` ambiguous).

## The export preset

`game/export_presets.cfg` is tracked in git (no secrets in it). The "Windows Desktop" preset is x86_64, release, with the data pack **beside** the exe (`Starfoundry.pck`, not embedded; [ADR 0048](adr/0048-windows-release-pipeline.md) says why). It exports every resource plus `content/*`, `assets/credits.tsv`, `assets/audio/*` and `assets/fonts/*` (with the licence texts), and excludes `*.uid`, `*.md`, the dev scenes `Gallery.tscn` and `Main.tscn` and the icon source. A C# export puts the .NET assemblies in a `data_Starfoundry.Game_windows_x86_64` folder beside the exe; ship the whole folder. The console wrapper `Starfoundry.console.exe` is exported too (logs, bug reports and the headless smoke test).

The icon and the version information are not set by Godot (that needs rcedit, a separate download): `tools/release/Set-ExeResources.ps1` stamps them after the export.

## Saves, settings and flags in an export

- `user://` is the same folder as when running from the editor, `%APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype` (the project sets `config/custom_user_dir_name` to keep that location although the game is now named just "Starfoundry"). Saves, `settings.cfg` and play logs therefore carry over. Neither the installer nor its uninstaller touches that folder.
- The window title is "Starfoundry <version>" (set by the main menu from the `VERSION` file) and the window mode is maximised (project setting `window/size/mode`).
- Development flags work the same after `--` (`Starfoundry.exe -- --demo`). `--self-check` prints what the packaged build can see (content, scenarios, credits, fonts) and quits with code 0 or 1; the release smoke test uses it.
