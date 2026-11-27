# Saves: slots, checkpoints and the Save/Load screen (2026-11-28)

Status: built, a *proposal* for the player-facing rules ([ADR 0049](../adr/0049-save-slots-checkpoints-and-slot-files.md)); nobody has played with it by hand yet. It builds on [ADR 0016](../adr/0016-saves-are-the-command-log-replayed.md): a save is still the command log replayed, and the simulation is untouched. Everything below is client and file format.

## What a save point is
A *save point* is any file in the saves folder. They differ in who makes them and how long they live.

| Kind | Made by | Slot | Kept |
| --- | --- | --- | --- |
| Manual | the player, on the Save/Load screen, with a name | a new file per name | until the player deletes it; overwriting asks first and keeps a `.bak` |
| Quick | `F5` (or Quick save in the pause menu) | one slot, `quick`; `F9` loads it | replaced by the next quick save; never touches the others |
| Checkpoint | the game, at key moments | a new file each time | the newest **6 per game**, older ones rotate out |
| Victory | the game, when a game is won | one per game | until deleted |

Checkpoints are taken (the reason is stored and shown):
- **before a wave**: when the wave warning starts;
- **a station founded**; **a research finished**; **docking**;
- **before a jump**: the instant before the Jump command is sent (needs 8 s since the last write);
- **leaving to the main menu** from the pause menu or an end screen;
- **every 5 minutes** of real time when none of the above happened.

Events never write closer than 30 s (the most important of a burst waits for the gap, and is dropped after 90 s); loading a game or starting one only sets the baseline, so it never writes a checkpoint of what was just loaded; the paused game, the story dialog, the end screens and the Save/Load screen write nothing. **A lost world never writes a checkpoint**, so after "Ship lost" the end screen's button **Reload last checkpoint** loads the newest loadable save of the same game (a quick save counts, a victory does not), or, if that game has none, the newest loadable save. A game is one playthrough: its id travels through every save and load, so a second campaign never pushes the first one's checkpoints out.

## Files
`<user data>/saves/<id>.sfslot` (on Windows `%APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype\saves`), one per save. Ids are `quick`, `auto-<unix time>-<reason>`, `victory-<game id>` and a slug of the typed name for manual saves; only `[a-z0-9._-]`.

```
"SFSL" | format byte (1) | metadata text | screenshot (JPEG, 320 px wide, may be empty) | goals text | SaveGame bytes | SHA-256 of everything before it
```
Each block is a 4-byte length and its bytes. The metadata is `key=value` lines: `name kind reason game scenario preset mode seed ticks wave system play saved content log`. The `SaveGame` bytes are exactly what `SaveGame.Write` produced before, so `SaveGame.Read` and its checksum comparison are unchanged. Next to a slot there may be `<id>.sfslot.bak` (the contents before the last overwrite).

- **Atomic write**: temporary file, flush to disk, copy the old one to `.bak`, rename over the slot. A crash leaves the old or the new file, never half of one. Leftover `.tmp` files are removed at start.
- **Corruption**: any changed byte, a cut file, a bad length or a wrong magic is found by the checksum when the folder is listed; such a file is shown as "Archivo ilegible / Unreadable file" with the reason and is never loaded, rotated away or deleted by the game.
- **Other content**: the metadata holds the content hash the log was recorded with (the scenario's header hash for a scenario game) and the log version. A save whose hash differs from this build's, from a scenario this build does not have, or from a newer log format is shown with a specific explanation, its Load button disabled, and the file untouched. (Older log versions load: they replay with their legacy rules.)
- **Migration**: the first start wraps the old `quicksave.sfsave`, `autosave.sfsave`, `autosave.prev.sfsave`, `victory.sfsave` and any other `*.sfsave` (with their `.goals`) into slots (quick, checkpoints, victory, manual), reads each back to check it, and moves the originals to `saves/legacy/`. Nothing is deleted. An unreadable old file is moved there too and counted.
- **Settings**: `settings.cfg` remembers `last_save=<id>`; Continue loads it if it still exists and this build can load it, else the newest loadable save.
- **Scripted runs** (every flag except `--agent`, `--menu-shot`, `--ui-lang`, `--content` and `--continue`) use `user://saves-scripted`; the audits use `saves-audit` and `--saves-check` uses `saves-check`, all rebuilt or ignored, none touching the player's saves or `last_save`. `--saves-dir=path` forces a folder; `--no-saves` writes nothing.

## The screen
Main menu: **Continue** (the last used save, with its name and play time), **Load game** (the screen, load only). Pause menu (`Esc`): **Save / load game** (the screen with the save row), Quick save (`F5`), Quick load (`F9`). No new key: F2 to F4 and F6 to F8 are kept free on purpose, and `F5` and `F9` keep their meaning, now on the `quick` slot only.

- Rows: kind badge, name (or "Quick save", or "Checkpoint - before a wave"), the game and difficulty, seed, game time, wave and system, the date and the real play time. Newest first; **Newest / Oldest / Name** sorts; **All / My saves / Checkpoints** and **This game only** filters.
- Detail pane: the screenshot (the game as it was before the pause menu opened; none in saves made headless or migrated), every field, the reason a save cannot be loaded, the file name.
- Buttons: **Load** (pressing a row picks it, pressing it again, Enter or a double click, loads it), **Overwrite** (a manual or quick save; not a checkpoint, duplicate it first), **Rename** (`F2`), **Duplicate**, **Delete** (`Del`), **Close** (`Esc`). Loading in a game, overwriting and deleting ask first with **No** under the focus; `Esc` answers No.
- **Save as new**: a name field (a suggestion such as "Campaign - 12:34") and a button. A name that matches an existing save does not replace it: the screen says so and selects that save so Overwrite is the deliberate way.

## Checks
- Unit tests (`SaveFilesTests`, `SaveSlotsTests`): the container round trip, every flipped byte and cut length detected, atomic writes and `.bak`, stale temp files, damaged files listed and kept, ids that try to leave the folder, rename and duplicate with unique names, rotation per game that never touches manual or quick saves or other games, the migration of old files (originals kept, replay of the wrapped save gives the same checksum, idempotent), compatibility from the header, naming and slugs, the list filters and sorts, the Continue and end-screen targets, the checkpoint watcher (baseline, timer, events, gap, priority, jump), every text in both languages with the same placeholders, and the `last_save` setting.
- `Godot --headless --path game -- --saves-check [--ui-lang=es|en]` (85 PASS lines in each language, exit code 1 on a failure): real saves of the running scene (manual, quick, six checkpoints from eight), the checksum after each load, the screen's named nodes and flows (a typed name that exists does not overwrite; Overwrite and Delete ask; No changes nothing; rename, duplicate), a damaged file, other content and a newer format refused with the file untouched, the migration, and the layout of the screen for six saves at 800x600, 1024x600, 1280x720 and 1920x1080 in both languages (every named control on screen, no cut or overlapping text).
- `--ui-audit` (campaign and scenario) and `--ui-audit-menu`: the screen on made-up saves of every kind (a long name, checkpoints, a victory, a scenario game, other content, an unknown scenario, a newer format, a damaged file, a cut one) with the list, the filters, the empty list and the load, delete and rename dialogs, plus the pause menu with its extra entry and the main menu with Load game; run by `tools/ui-audit.ps1` for es and en.
- `--saves-shot=path` (with a window): pictures of the screen on the fixtures (`path.real.png` has a real screenshot).

## Numbers (Debug build, headless, the owner's PC; the container work is the same in Release)
- Writing a save (building the bytes, the pack with its checksum, the atomic write): about 3 ms for the small world of the check. The container itself: **0.5 ms to pack and 0.3 ms to check a 600 KB save** with a 12 KB screenshot (SHA-256 included), so a long campaign's file adds well under a millisecond.
- Listing 53 files: 9 ms cold, 7 ms when nothing changed (every file is opened once to verify it, then cached by size and time).
- Loading is unchanged: it is the replay, [1.35 s in Release for a 47-minute game](performance-baseline.md).
- A real screenshot of the cockpit (320 px wide JPEG, quality 0.78) is 16.6 KB, so a small slot file is about 17 KB (measured by `--saves-shot` in a window); a long campaign's file is its log (54.6 KB for the 47-minute bot game, [performance baseline](performance-baseline.md)) plus that.

## Not verified, to look at
- The screen has been seen in pictures at 1280x720 in Spanish only; nobody has used it with a mouse and a keyboard. Check: the row text at 1920x1080, whether six checkpoints are the right number, whether the 30 s gap is too long or too short, and whether the toast "Checkpoint: before a wave" is welcome or noise.
- The headless runs cannot draw, so screenshots of saves are verified only in the windowed `--saves-shot` run.
- A save made while a different galaxy's scene is open reopens the scene (as before); that path was not exercised again by the new check.
