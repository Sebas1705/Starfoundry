# 0049. Save slots: one checked file per save, automatic checkpoints and a Save/Load screen

- Status: proposed
- Date: 2026-11-28
- Area: client, tooling, gameplay
- Builds on: [ADR 0016](0016-saves-are-the-command-log-replayed.md) (a save is the command log, replayed). Nothing in it is replaced: the replay, the checksum comparison and the `SaveGame` bytes are exactly as before. What changes is where a save lives and how many there can be.
- Design notes and measurements: [saves](../prototypes/saves.md)

## Context
- The game had two slots, `quicksave` and `autosave` (plus a backup of each), picked by Continue, F9 and the pause menu as "the newest of the two". A player could not keep a base before a risky jump, name a save, see what a save was without loading it, go back past the last autosave, or manage the files. A damaged file was only discovered at load time.
- The owner asked for several saves, save points (checkpoints) and robust storage.
- The simulation does not need to know any of this: everything a save needs is the command log, and the log format (version 27 at the time of writing) is untouched.

## Decision
- **Slot files.** `user://saves/<id>.sfslot`, one per save. The file is `SFSL`, a format byte, four length-prefixed blocks (metadata text, a small JPEG screenshot, the goals text of a scenario game, the `SaveGame` bytes) and a SHA-256 of everything before it. A file that is cut short, edited, copied badly or from a newer format is recognised as such before anything is replayed. The id is the file name (`[a-z0-9._-]`, at most 64 characters, checked against path tricks).
- **Metadata** is `key=value` text (unknown keys ignored): name, kind, reason, game id, scenario, difficulty preset, mode, seed, game ticks, wave, system name, real play time, save time, the content hash and the command log version of the payload. The screen lists saves from it without replaying them, and judges whether this build can load a save from its content hash (the scenario's header hash for a scenario game) and log version alone.
- **What a save point is.** A *save point* is any slot file; there are four kinds.
  - *Manual*: named by the player on the Save/Load screen. Never rotated away, never overwritten without a confirmation.
  - *Quick*: one slot, `quick`. F5 writes it and F9 loads it (the registry keys are unchanged). It is its own slot: it never replaces a manual save or a checkpoint.
  - *Checkpoint*: written without being asked, at a moment worth coming back to. The moments are a wave about to arrive (its warning starts), a station founded, a research finished, docking, the instant before a jump, leaving to the main menu, and every five minutes when none of those happened. Events never write closer than 30 seconds (the most important event of a burst waits for the gap; the jump checkpoint needs 8 seconds); loading or starting a game only sets the baseline, so it never writes a checkpoint of what was just loaded. Each checkpoint records its reason, and a game keeps its newest **six**: older ones rotate out (a game is the playthrough id carried through loads, so a second campaign never pushes the first one's checkpoints out). Manual, quick and victory saves and files that cannot be read are never rotated away.
  - *Victory*: written when a game is won (one per game).
  - A lost world never writes a checkpoint, so the end screen's **Reload last checkpoint** (the newest loadable save of the same game, a quick save included) always has something to go back to.
- **Writing.** The bytes go to `<id>.sfslot.tmp`, are flushed to disk, the previous file (if any) is copied to `<id>.sfslot.bak`, and the temporary file is renamed over the slot. A crash leaves either the old file or the new one; leftover `.tmp` files are removed at start. Overwriting always keeps one backup.
- **Loading.** The slot is verified, then compared with this build: other content, an unknown scenario or a newer log format refuse the load with a specific reason and **the file is never touched or deleted**; the list shows the reason, the load button is disabled, and Delete (with a confirmation) is the player's own decision. A save of another galaxy reopens the scene on it, as before. After a replay the checksum is compared, as in ADR 0016.
- **Save/Load screen.** From the main menu (Continue, Load game) and the pause menu (Save / load game). Rows show the name, kind, game and difficulty, seed, game time, wave, system, play time and date; filters (all, mine, checkpoints, this game only), sort by date or name, a detail pane with the screenshot, and Load, Overwrite, Rename, Duplicate and Delete. Loading from inside a game, overwriting and deleting ask first, with No under the focus; typing the name of an existing save does not replace it (it says so and offers Overwrite); checkpoints cannot be overwritten by hand (duplicate one to keep it). The screenshot is the game as it was before the pause menu or the screen opened.
- **Continue** loads the slot used last (`last_save` in `settings.cfg`) if it still exists and this build can load it, else the newest loadable one.
- **Migration.** On the first start, the old `*.sfsave` files (and their `.goals`) are wrapped into slots (quick, autosaves as checkpoints, victory, any other as a manual save) and the originals are moved to `saves/legacy/`, never deleted. The wrap is checked by reading the new file back. With no old file nothing happens.
- **Scripted runs** (shots, audits, benchmarks, `--saves-check`) write to a throwaway folder (`user://saves-scripted`, `-audit`, `-check`) and never change `last_save`; `--saves-dir=path` forces a folder. Agent play runs on its own copy with its own user data, as before.
- **No simulation change.** `CommandCodec.CurrentLogVersion` is a read-only accessor so the screen can compare a save's version with the build's; no log version, no command, no rule.

## Consequences
- Benefits: several named saves, a way back before every wave, jump and station, corruption found when listing, an overwrite or delete is never one click, an incompatible save is explained and kept, and the file format can grow (new blocks, new metadata keys) without touching the replay.
- Costs: a slot file is a little larger than the old file (metadata, a screenshot of about 10 to 20 KB and 32 bytes of checksum); listing reads and verifies every file once per change (cached by size and time afterwards); six checkpoints per game are kept on disk.
- Not decided here: cloud saves, a checkpoint before the first wave of a new system, a "pin" that exempts a checkpoint from rotation (duplicating it does that today), snapshots to shorten long loads (ADR 0016 still holds: a 47-minute game loads in 1.35 s in Release).

## Alternatives considered
- **A manifest file listing the saves:** one more thing that can disagree with the folder. The folder is the index; the metadata lives inside each file.
- **Checksum in the replay only:** the replay already compares the world checksum, but only after seconds of replay; a file checksum finds a bad file when the list is drawn and for saves that cannot replay on this build anyway.
- **Rolling checkpoints by count over all games:** a second campaign would push the first one's checkpoints out; they rotate per game.
- **A save key on the keyboard (F6):** keys F2 to F4 and F6 to F8 are deliberately free of bindings (`InputActionsTests`); the screen is under Esc, and F5 and F9 keep their meaning.
