# Packaging a Windows build

How to turn the Godot project in `game/` into a standalone `Starfoundry.exe` that finds its content without the repository.

## How the game finds its content

`ContentLocator` (`src/Starfoundry.Ui`, tested by `ContentLocatorTests`) looks for `core.json` in this order and uses the first that exists:

1. `--content=path/to/core.json` (a user argument, after `--`). When given, it is the only candidate: a typo is reported, not hidden.
2. `res://content/core.json`: inside the project; this is what an export contains.
3. `<folder of the executable>/content/core.json`: a loose file next to the exe, handy for modding or testing.
4. `res://../content/core.json`: the repository layout (`game/` beside `content/`), used when you press F5 in the editor.

If none exists, or the file is invalid, the game shows an error screen (Spanish or English by the saved language) listing every place it looked, with a Quit button; the same list goes to the console. `tools/Starfoundry.Balance` uses the same resolver (`--content=` as well).

`--content=` does not count as a scripted flag, so the main menu still opens.

## Why `tools/package.ps1` copies the content

Godot can only export files that live inside the project folder, and `content/` is a sibling of `game/`. Duplicating the file in git would let the two copies drift, so the script copies `content/` to `game/content/` for the duration of the export and deletes it afterwards (`game/content/` is git-ignored).

## Build a release locally

Needs: .NET 8 SDK, Godot 4.7.2 Mono, and the matching **export templates** (one-time install).

Install the templates once, either way:

- In the editor: open `game/`, Project > Export..., Manage Export Templates, Download and Install.
- By hand: download `Godot_v4.7.2-stable_mono_export_templates.tpz` from the `4.7.2-stable` release of `godotengine/godot-builds` (or the Godot download archive), then Manage Export Templates > Install From File. The result must be `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\` (it holds `windows_release_x86_64.exe`).

Then, from the repository root:

```powershell
.\tools\package.ps1                 # release build to build\windows\Starfoundry.exe
.\tools\package.ps1 -Out D:\drop    # another folder
.\tools\package.ps1 -DebugBuild     # debug export
```

The script finds Godot at the winget location or in `$env:GODOT` (or `-Godot <console exe>`). If the templates are missing it prints the instructions above and exits with code 2 without touching anything: without templates no standalone exe can be produced. Smoke test the result:

```powershell
.\build\windows\Starfoundry.exe --quit-after 300
```

The preset (`game/export_presets.cfg`, tracked in git, no secrets in it) is "Windows Desktop": x86_64, the data pack embedded in the exe, product name "Starfoundry", `content/*.json` included, `*.uid` and `*.md` excluded, plus a console wrapper (`Starfoundry.console.exe`) for logs. There is no app icon yet: add one under `game/` and set `application/icon` in the preset. With the pack embedded a C# export still places the .NET assemblies in a `data_Starfoundry_windows_x86_64` folder beside the exe; ship the whole folder.

## Saves, settings and flags in an export

- `user://` is the same folder as when running from the editor, `%APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype` (the project sets `config/custom_user_dir_name` to keep that location although the game is now named just "Starfoundry"). Saves, `settings.cfg` and play logs therefore carry over.
- The window title is "Starfoundry" and the window mode is maximised (project setting `window/size/mode`).
- Development flags work the same after `--` (`Starfoundry.exe -- --demo`).

## What CI could do later

A workflow could download the export templates (about 1 GB, cache them by version), run `tools/package.ps1` on `windows-latest` and upload the folder as an artifact. This repository has no such workflow on purpose: fetching the templates reliably in CI was not verified here, and publishing a release is the owner's decision.
