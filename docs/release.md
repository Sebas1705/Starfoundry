# Releasing Starfoundry for Windows

How a checkout becomes `Starfoundry.exe`, a portable zip and an installer, locally and in CI, and how the owner cuts a release. The decisions behind it are in [ADR 0048](adr/0048-windows-release-pipeline.md); how the exported game finds its content is in [packaging.md](packaging.md).

## Build everything locally

Needs: Windows 10 or 11, the .NET 8 SDK, Godot 4.7.2 Mono (the winget one is found automatically, or set `GODOT_PATH`), the Godot export templates and Inno Setup 6. Windows PowerShell 5.1 is enough.

```powershell
.\tools\release\build-windows.ps1                 # exe, zip, installer; prints what is missing and exits 2 if a tool is absent
.\tools\release\build-windows.ps1 -NoInstaller    # only the exe folder and the zip
.\tools\release\build-windows.ps1 -TestInstaller  # also install, smoke-test, upgrade and uninstall the installer in a temp folder
```

Nothing is downloaded unless you ask. Switches that do, each printing what and from where:

| Switch | What it downloads or installs |
| --- | --- |
| `-InstallTemplates` | `Godot_v4.7.2-stable_mono_export_templates.tpz` (1,202,598,411 bytes, about 1.2 GB) from the official `godotengine/godot-builds` release `4.7.2-stable`. The size is checked against the GitHub API and the SHA-512 against the release's `SHA512-SUMS.txt`; only the Windows x86_64 files (about 220 MB) are unpacked into `%APPDATA%\Godot\export_templates\4.7.2.stable.mono`. |
| `-InstallGodot` | `Godot_v4.7.2-stable_mono_win64.zip` (about 117 MB), same release, same checks, into `%LOCALAPPDATA%\Starfoundry\release-tools` (or `$env:SF_TOOLS_DIR`). Only when no Godot is found. |
| `-InstallInnoSetup` | `winget install --id JRSoftware.InnoSetup -e --scope user` (the installer comes from jrsoftware.org through the winget community repository), or `choco install innosetup -y` without winget. |

Or do it by hand: the templates from the editor (Project, Export, Manage Export Templates, Download and Install) and Inno Setup from <https://jrsoftware.org/isdl.php>. The Godot version is read from `game/Starfoundry.Game.csproj` (`Godot.NET.Sdk/4.7.2`), so there is one place to change it.

What the script does, in order: reads `VERSION`; builds the solution (`dotnet build -c Release -m:1`, no shared compiler server, `DOTNET_PROCESSOR_COUNT=2` unless set); copies `content/` into `game/content` and a throw-away `game/Starfoundry.Game.sln` (both removed afterwards, also on failure); exports the "Windows Desktop" preset headless; stamps the icon and the version information into both exes; adds `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` (generated from `ASSETS.md`), the font licences and a `README.txt`; lists the pack and fails if content, fonts or credits are missing or if dev-only files are in; runs the headless smoke test; zips; compiles the installer; writes the checksums; runs `dotnet build-server shutdown`. No game window opens at any point.

### Output layout

Everything is under `artifacts/`, which is git-ignored.

```
artifacts/
  windows/
    Starfoundry/                              the game folder, as the zip and the installer carry it
      Starfoundry.exe                         windowed game
      Starfoundry.console.exe                 the same game with a console (logs, bug reports, smoke test)
      Starfoundry.pck                         content, scenes, fonts, credits data
      data_Starfoundry.Game_windows_x86_64/   C# assemblies and the .NET runtime files
      LICENSE.txt  THIRD-PARTY-NOTICES.txt  README.txt  licenses/
    Starfoundry-<version>-windows-x64.zip     portable build, one top-level folder
    SHA256SUMS.txt                            checksum of the zip
  installer/
    Starfoundry-<version>-setup.exe           the installer
    SHA256SUMS.txt                            checksum of the installer
```

Verify a download with `Get-FileHash -Algorithm SHA256 <file>` and compare with the sums file (`<hash> *<name>` per line, readable by `sha256sum -c`).

### Measured on a development PC (version 0.1.0, release export)

| Item | Size |
| --- | --- |
| `Starfoundry.exe` (Godot Mono release template, pack not embedded) | 104.3 MB |
| `Starfoundry.pck` (content, scenes, fonts, models, credits) | 1.4 MB |
| `data_Starfoundry.Game_windows_x86_64/` (C# assemblies, .NET runtime files) | 79.6 MB |
| Game folder in total | 185.3 MB |
| Portable zip | 72.0 MB |
| Installer | 52.6 MB |

Wall-clock time of one full run (build, export, stamp, smoke, zip, installer, installer test), warm caches, no downloads: 1 minute 17 seconds (export 10 s, zip 8 s, installer compile 33 s, installer test 17 s). The smoke test took 4 s: main menu 1.1 s, self-check passed, 176 `--blueprint-check` lines passed and none failed.

## The installer

`tools/release/installer.iss`, built with Inno Setup 6.

- **Per user by default**: no administrator rights, installs to `%LOCALAPPDATA%\Programs\Starfoundry`; the wizard offers an all-users install (administrator) through its "install for" dialog.
- **Shortcuts**: a Start Menu entry (plus a console entry and a link to the licences and credits), an optional Desktop shortcut (unchecked by default), and an Apps & features entry that uninstalls.
- **Upgrade in place**: the `AppId` never changes, so running a newer installer over an installed copy keeps the same folder, closes a running game, deletes the old `data_*` assemblies first and installs the new files. Downgrades are allowed (Inno's default).
- **Saves and settings are never touched.** They live in `%APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype`, outside the install folder; the uninstaller does not list or remove that folder either (delete it by hand for a full wipe).
- **Languages**: Spanish and English wizards, chosen from the Windows display language like the game (`LanguageDetectionMethod=uilanguage`), the licence page shows the MIT licence.
- **Silent install and uninstall** (documented and tested by `test-installer.ps1`):

```powershell
# install for the current user into a chosen folder, no windows, no prompts
.\Starfoundry-0.1.0-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="C:\Games\Starfoundry" /LOG="$env:TEMP\sf-install.log"
# add the Desktop shortcut:   /TASKS="desktopicon"
# uninstall (returns at once; the folder disappears a few seconds later)
& "C:\Games\Starfoundry\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

`tools/release/test-installer.ps1 -Setup <exe>` does all of that in a temp folder: install, check the files, the Apps & features entry and the Start Menu entry, run `smoke-test.ps1` against the installed copy (with `APPDATA` redirected to a temp folder, so real saves are never touched), install again over it (a planted stale file must vanish), uninstall, and check that the folder, the registry entry and the shortcut are gone. It refuses to run when Starfoundry is already installed for the user. It does not test the optional Desktop shortcut (that would write to the real Desktop).

## The smoke test

`tools/release/smoke-test.ps1 -Dir <folder>` runs `Starfoundry.console.exe` headless with a throw-away `APPDATA`:

1. `--headless --quit-after 60`: the main menu scene loads and quits cleanly, with no script errors.
2. `-- --self-check`: prints `SELFCHECK` lines and exits 0 only if `content/core.json` resolves, the scenarios load, `credits.tsv` lists the bundled packs, the three fonts and their licence texts and a model are inside the pack, and the version matches `VERSION`.
3. `-- --blueprint-check`: starts the game scene from the packaged content and runs its PASS/FAIL checks (about 170 of them): a short play phase.

Limitation, said plainly: the scripted `--agent` playthrough in [tools/agent](../tools/agent/) drives a project folder through the Godot MCP bridge and cannot attach to an exported build, so step 3 stands in for it. Nothing here opens the real window, renders a frame with a GPU (headless uses the dummy renderer) or listens to audio; those stay manual checks in [playtest.md](playtest.md). The Credits page itself is a scene built from `credits.tsv`; the smoke test checks the data and the packaged files, not the pixels (the page was looked at in the editor build).

## CI: `.github/workflows/release.yml`

Triggered by `workflow_dispatch` (Actions tab, "Release", Run workflow) and by pushing a tag `vX.Y.Z`. It is independent of `ci.yml`, whose `build-and-test` gate is unchanged, and runs only on GitHub-hosted `windows-latest` (never the self-hosted PC).

1. Reads `VERSION` and, for a tag, fails unless the tag equals `v` plus the version.
2. Restores NuGet (cached), restores the portable Godot and the export templates from caches keyed by the Godot version, runs the fast test tier.
3. Runs `build-windows.ps1 -InstallGodot -InstallTemplates -InstallInnoSetup -TestInstaller -Sign`: everything above, including the silent install and uninstall test.
4. Uploads `Starfoundry-<v>-windows-x64.zip`, `Starfoundry-<v>-setup.exe` and `SHA256SUMS.txt` as the workflow artifact `starfoundry-<v>-windows` (30 days).
5. Only for tags: a second job creates a **draft** GitHub Release for the tag with the same files, the matching `CHANGELOG.md` section as notes, and `--prerelease` when the version has a pre-release part (`1.0.0-rc.1`). The workflow never publishes, never creates tags and never edits an existing release.

The workflow was reviewed but could not be run before it was merged: the first real check is a `workflow_dispatch` run from `main`. Expect to adjust something on that first run (a cache path, the Inno Setup install on the runner image).

## Versioning and the changelog

- **The version is the `VERSION` file** (`MAJOR.MINOR.PATCH`, optionally `-rc.1`). One edit changes the assembly version, the main menu, the Credits page, the window title, the exe properties, the installer, the file names and the workflow check. Releases are tagged `vMAJOR.MINOR.PATCH`.
- **The bump follows the conventional commits since the last tag**: any `feat` raises the minor, only `fix`/`perf`/`docs` raise the patch, a breaking change (`feat!:` or a `BREAKING CHANGE:` footer, for example a save or log format that old builds cannot read) raises the major, or the minor while the version is below 1.0. `git log v0.1.0..HEAD --format=%s` lists them.
- **`CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/)**: each pull request that a player can notice adds a line under `## [Unreleased]` (`feat` to Added, `fix` to Fixed, behaviour changes and `perf` to Changed). Releasing renames that section to `## [x.y.z] - date`. The draft release notes are that section.

## Cut a release, step by step (owner)

Agents prepare; only the owner tags and publishes.

1. Make sure `main` is green (`build-and-test`) and everything for the release is merged.
2. Decide the version from the commits since the last tag (see above). In a pull request: set `VERSION`, rename `## [Unreleased]` in `CHANGELOG.md` to `## [x.y.z] - YYYY-MM-DD` (add a fresh empty `Unreleased`), merge it.
3. Optional dry run: Actions, "Release", Run workflow on `main`. Download the artifact, install it on a clean Windows user or VM, play a few minutes.
4. Tag the merge commit and push the tag: `git tag -a vX.Y.Z -m "Starfoundry X.Y.Z"` then `git push origin vX.Y.Z`. The workflow builds and drafts the release.
5. Open the draft on GitHub Releases, check the files (names, sizes, `SHA256SUMS.txt`) and the notes, then press **Publish release**. Until then nothing is public.
6. If the build is wrong: delete the draft and the tag (`git push --delete origin vX.Y.Z`), fix, bump the patch or retag, run again.

No secret belongs in a release asset: the game embeds none, and the build reads no secrets unless you configure signing.

## Code signing (optional, off by default)

`tools/release/sign.ps1` signs `Starfoundry.exe`, `Starfoundry.console.exe` and the setup exe when, and only when, signing secrets exist; otherwise it prints that the artifacts are unsigned and the build continues. No certificate, key or password is in the repository; the names below are referenced only. Locally they can come from Doppler (`doppler run -p <project> -c <config> -- .\tools\release\build-windows.ps1 -Sign`), in CI from GitHub Actions secrets of the same names.

| Option | Secrets (names) | Notes |
| --- | --- | --- |
| Azure Trusted Signing | `SF_SIGN_AZURE_ENDPOINT`, `SF_SIGN_AZURE_ACCOUNT`, `SF_SIGN_AZURE_PROFILE`, `SF_SIGN_DLIB` (path of `Azure.CodeSigning.Dlib.dll`), `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | Key held in Azure, short-lived certificates, no `.pfx` to guard. Roughly 10 USD a month at the time of writing; identity validation is required and availability depends on country and organisation type, so check current terms first. The workflow does not install the dlib: add a step that fetches the `Microsoft.Trusted.Signing.Client` package and points `SF_SIGN_DLIB` at it when you enable this. |
| A code-signing certificate (.pfx) | `SF_SIGN_PFX_BASE64`, `SF_SIGN_PFX_PASSWORD`, optional `SF_SIGN_TIMESTAMP_URL` | Certificate authorities charge from roughly 100 to 400 USD a year for OV certificates (EV more). Issuers now require the private key on a hardware token or a cloud HSM, so a plain exportable `.pfx` is no longer what most CAs sell; check before buying. |

SignTool comes with the Windows SDK (present on `windows-latest`); `SF_SIGNTOOL` can point at it. The uninstaller embedded in the setup exe stays unsigned.

**SmartScreen, honestly.** An unsigned installer downloaded from the internet gets Windows SmartScreen's "Windows protected your PC" warning (More info, Run anyway), and some browsers add their own warning. Signing removes the "unknown publisher" part and is the precondition for building reputation, but a new certificate does not clear SmartScreen by itself: reputation grows with the number of downloads over time, and nobody can promise when the warning disappears. Until the project is signed, say so on the download page and publish the SHA-256 sums. The portable zip carries the same unsigned exes and gets the same warning.

## Not covered yet (future options)

- Steam (Steamworks SDK, depots, `steamcmd` in CI), itch.io (`butler push`) and other stores.
- Linux and macOS builds (the preset, templates and installers are Windows only; Godot can export both but nothing is set up or tested).
- Auto-update: the installer upgrades in place when the user runs a newer one; there is no update check or delta patching.
- ARM64 Windows, a 32-bit build, MSIX or WinGet submission.
- Bundling Godot's full third-party licence text (the notices link to it) and showing the engine licence in-game.
- A GPU smoke test (a window, a rendered frame, audio) on a runner with a display, and the packaged `--agent` playthrough.
