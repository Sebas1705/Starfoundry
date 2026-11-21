# Play-test guide

Everything in this repository so far was checked by tests and scripted players. Nobody has played the game by hand. This page is for the first person who does: how to run it, what to try, and how to turn the session into numbers that can be put next to the scripted player's.

## Run it
You need the .NET 8 SDK and Godot 4.7.2 (Mono).

```powershell
dotnet build game -c Debug
```

Open the `game/` folder in Godot and press F5. The game opens on the main menu. It uses Spanish or English by system language the first time; `L` (or the Settings page) switches. `F1` shows every control.

### Menus and settings
- **Main menu**: Continue (enabled when a quick save or autosave exists; it loads the newest one and shows its play time), New game, Settings, Controls (the same text as `F1`) and Quit. Arrows and Enter work as well as the mouse; `Esc` goes back from a page.
- **Pause menu**: `Esc` with nothing to cancel (no build tool, panel or selection) frees the mouse and opens it; the simulation is paused while it is open. Resume, Save and Load (the quick save, same as `F6`/`F9`), Settings, Controls, Main menu (autosaves first) and Quit. `Esc` again resumes.
- **Settings**: language, mouse sensitivity (0.5x to 2x), field of view (70 to 100), master volume, effects, music-and-ambience and interface volume (four buses, see [audio](prototypes/audio.md); moving a slider plays a preview), invert mouse Y and fullscreen. Changes apply at once and are saved to `settings.cfg` in the Godot user folder:

  ```powershell
  explorer "$env:APPDATA\Godot\app_userdata\Starfoundry Belt Prototype"
  ```

  Delete that file to go back to the defaults. Saves are in `saves\` beside it.
- **Difficulty: goal waves** (3 to 30, default 10) is in the same Settings page, stored as `goal_waves`.
- **Galaxy seed**: every new game picks a random seed (1 to 999 999 999); the pause menu shows it ("Galaxy seed: N"), the end screen lists it, the play log starts with `-- galaxy seed N` and the save header stores it, so Continue and load rebuild the same galaxy (loading a save of another galaxy while playing reopens the scene on it). `--seed=N` fixes it (and, like any flag, skips the menu). The scripted runs (every other flag: demos, screenshots, benchmarks) keep the fixed seed 20261120 so their numbers stay comparable.
- **Research panel** (`T`, `Esc` or `T` closes): all nodes grouped as researching, available, locked and done; the selected one shows its description, progress bar, cost lines (what the lab already has out of what it needs, and what your hold has), requirements, what it unlocks (machines, recipes, belts, abilities, upgrades) and a Start button. Arrows choose, `Enter` starts, the mouse works too. A hint at the bottom says how to deliver the items (port, belt, lab). The pointer is free while it is open; the game keeps running.
- **Build menu** (`B`, `Esc` or `B` closes): tabs Logistics (belts, depot, ports), Production (smelter, assembler, cracker, lab), Defence (turret, wall, repair bay) and Structure (module kinds with their cost, power and purpose, and found a station). Each entry shows its quick key, cost (green when your hold can pay, red with what is missing), power use or output, a one-line description and, when locked, which research unlocks it. Arrows move, `Enter` or a click chooses the tool and returns you to flight with it in hand, `PgUp`/`PgDn` change tab. The old palette keys `B`, `U` and `F2`-`F5` are gone: the ore, titanium, nickel, ice, quartz and carbon ports (and the cracker) are reached through the menu only. `B` now always toggles the menu, with or without a tool selected.
- **Ship thrust in the log**: steering sends thrust in steps of 64/1024 and at most ten times a second (a stop is immediate), which keeps saves small; see [structures](prototypes/structures.md#limits). `--load-bench` measures it.
- **UI screenshots**: `--ui-shot=path [--ui-lang=es|en]` saves `path.research.png`, `path.research.locked.png`, `path.build.png` (Production tab), `path.build.logistics.png`, `path.build.structure.png`, `path.build.defence.png` and `path.bar.png`, then quits.
- Any command-line flag (`--demo`, `--shot=`, `--hud-shot=`, `--structures-shot=`, `--stress=`, `--save-test` and the rest) skips the menu and starts the game directly, as does running `Play.tscn` from the editor. `--menu-shot=path` saves the main menu (`path`), the Settings page (`path.settings.png`), the Controls page (`path.controls.png`) and the pause overlay over the game (`path.pause.png`), then quits.

## The first ten minutes
1. Follow the steps in the panel at the top right. Each one ticks off by itself.
   The game is played in first person: the mouse looks (the cursor is captured), `W`/`S` fly along the view, `A`/`D` strafe, `Space`/`C` go up and down. `Tab` frees the cursor for the buttons (click in the game to look again); `Esc` leaves a build tool, then opens the pause menu. The top-down camera is gone. `E` stands you up from the pilot seat; standing, `W`/`A`/`S`/`D` walk inside the ship (`Shift` runs, no jump, `Space`/`C` unused), flight and the autopilot (`M`) are off, and `E` near the seat sits you down again. Repair moved from `E` to `Backspace`. Walking is client-only and limited to your own ship (see [structures](prototypes/structures.md)). Headless check: `--walk-shot=path`.
2. Fly to the brown rock (look at it and press `W`, or press `M` to fly there on autopilot), mine, press `F` for the ship refinery.
3. Open the build menu (`B`) and build a smelter and a depot (Production and Logistics tabs; the quick keys `4` and `7` pick them directly) and join them with belts (`1`): look down at the ground and the crosshair picks the cell (it turns red when you cannot place there); click to place, for belts press, move the crosshair and release. Right click demolishes, `R` rotates, `Y` changes the recipe of the item you are placing. Click a machine with no tool to see its status; `Tab` then lets you use its buttons.
4. Make plates with an assembler (`5`), build a lab (`6`), press `T` for the research panel and start Logistics: it lists the cost, what the lab already has, what is required and what it unlocks.
5. When the warning says a wave is coming, build a turret (`8`, or the Defence tab of the `B` menu) fed with ammo by belt, and switch your weapon on (`V`).

## The goal and the end
The last line of the steps panel is the goal, pinned even when the list is folded: "Goal: survive 10 waves with a station (N/10)". A wave counts as survived once every enemy of it is dead; the goal also needs a station of yours standing, so found one (`I`) before the last wave.

- **Victory** ("Station secured"): the goal is met. The game pauses and shows the play time, modules built, stations founded and lost, waves survived, what was mined by hand and by drones, and what was lost. It writes an extra `victory` save. Buttons: Keep playing (endless, the screen never comes back), Main menu, Quit.
- **Defeat** ("Ship lost"): the ship's last cockpit fell (there is no respawn in the structures campaign). The play log gets an `Outcome` line. Your autosave and quick save are untouched (a lost world is never autosaved), so Load last save takes you back; or New game, or Main menu (which does not autosave).
- Screenshots of both overlays: `--end-shot=path` saves `path.victory.png` (made-up numbers) and `path.defeat.png` (a real loss).

While a structure is under attack: modules darken and redden with their damage and, below half, throw sparks and smoke with a flickering light; a hit flashes the module and, if it is your ship's, a red vignette pulses; the status block shows the ship's integrity bar; a wave countdown and then the enemy count are shown large at the top; a module you aim at (or one hit in the last seconds) shows its hit points; losing a module or a station raises a toast and an explosion. `--defence-shot=path` renders these (`path`, `path.cockpit.png`, `path.destroyed.png`, `path.station.png`).

## What to try after that
- Walls (`9`) in front of the base and the repair button (`Backspace`) during a wave; a repair bay (`0`) fed ingots by belt.
- The defence upgrades in the research panel (`T`), the ports of the Logistics tab of the build menu (`B`; ore, titanium, nickel, ice, quartz and carbon ports are menu-only) and the jump to another system (`G`, `J`) when the status panel says the home ore is running low.
- Play at your normal pace and do not read the docs: where you get stuck is the finding.

## The play log
Every game that is not a demo writes its milestones to a text file as they happen. The path is printed in the console when the game starts, and the folder is:

```powershell
explorer "$env:APPDATA\Godot\app_userdata\Starfoundry Belt Prototype\playlogs"
```

A line has the time in the game (minutes:seconds of simulated time), a kind and a text:

```
2:35  Step       Depot
10:56 Researched core:logistics
12:28 Wave       wave 1 arrived with 2 enemies alive
```

The kinds are `Step` (a guided step), `Researched`, `Wave`, `Loss`, `Jump`, `Tip` (a hint the game showed you) and a last line with how long the hold was completely full. The step names are the ones in the table below.

## What the scripted player did
A scripted player that never hesitates, with a pause of 15 seconds after every action, standing in for a first-time player (default seed, one fed turret). Compare your times with these; the targets in the tests (see the [balance pass](prototypes/balance-pass.md)) are bands around them.

| Step | Scripted player |
| --- | --- |
| Fly, Mine, Refine | 0:01, 0:04, 0:14 |
| Drill | 0:43 |
| Smelter | 1:51 |
| Depot | 2:35 |
| Assembler | 3:55 |
| Lab | 5:15 |
| Scan | 5:48 |
| Defend (turret armed) | 7:40 |
| Research (Logistics) | 10:56 |
| First wave | 12:28 |

If your times are far from these, that is the interesting result: write down what slowed you down or what you did not understand.

## What to write down
- Where you got stuck or confused, and what you tried.
- Anything the interface did not tell you (a refusal without a reason, a button you did not find).
- Whether the camera, the autopilot and the drag-to-build felt right.
- Whether the waves felt fair, too slow or too fast, and when you first lost something.
- Whether you ever ran out of ore or ended up with a full hold.
- Send the play log with the notes. If you closed the game, the autosave or quick save can bring you back.

## Known limits
- The numbers in the log are game time, not wall-clock time, and follow the speed you set with `+` and `-`.
- A paused game does not advance the log.

## Hints
After the twelve guided steps the game teaches the rest with hints that appear when the situation happens, once each and at least 25 seconds apart, as long messages at the bottom: turrets without ammo during a wave, damaged machines (repair with `Backspace`), the first loss without walls, an idle lab, the defence upgrades once Logistics is done and the cargo port once the jump drive is researched and the home ore is low. Each hint shown is a `Tip` line in the play log. If a hint came too early, too late or not at all, that is worth noting.

## The HUD
The first-person HUD keeps the view free and shows only what you need:
- **Status (top left):** hull and alert, plus the step in progress when the steps panel is folded. `H` (or the Details button) opens the full block (selected deposit, system, threat noise, research, upgrades).
- **Flight readout (top centre):** a heading strip (N is the world's -Z axis, E is +X) and the speed in cells per second.
- **Markers:** the selected deposit, the nearest enemy and your base (the nearest cluster of machines, else the system anchor) get a name and a distance. In view they sit on the target; off to a side or behind, an arrow on the screen edge points toward it. They disappear when you are too close.
- **Target panel:** aim the crosshair at a machine (no tool selected) for a compact panel with name, role, status, recipe and a hit-point bar. Click it to pin it in the inspector.
- **Notifications:** at most three at a time, repeats grouped as "x3", coloured by category (green production, amber logistics, red threat, white info).
- **Bottom strip:** the hold bar with item counts, the selected tool and its cost (green when affordable, red when short, grey when locked), and the quick bar: one row with the first twelve favourites (keys `1`-`9`, `0`, `Q`, plus the module tool `N`) and a `B: all` button that opens the build menu. Every other entry is in the menu.

Layout check: `Godot --path game --resolution 1280x720 -- --hud-shot=out.png [--hud-details]` saves the HUD of the played game after a second and quits. The projection, compass, toast and health-band logic live in `src/Starfoundry.Ui` (`Markers`, `Compass`, `ToastQueue`, `HudFormat`) and are covered by `HudTests`.

## Saving
`F6` saves, `F9` loads, and a played game saves itself to an autosave every five minutes of real time. The files are in the `saves` folder next to `playlogs` (`%APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype\saves`). A save is the log of what you did and is replayed when loaded, which takes a second or two for an hour-long base ([ADR 0016](adr/0016-saves-are-the-command-log-replayed.md)). If the game says a save cannot be loaded, keep the file and tell me: it means the game changed since it was made. After a load the guided steps are recomputed and a hint may show again. The play log notes the load.

## Sound
The game has procedural audio (synthesised at startup, no asset files; see [audio](prototypes/audio.md)) and **nobody has listened to it yet**. Play with headphones for a while and write down: sounds that are too loud, too quiet, grating or confusing; whether you can tell the engine, the reactor and a working machine apart; whether the wave alarm helps or annoys; whether the calm music is pleasant at 60 % and the tense variant fades in during a wave; whether anything clicks or pops at a loop point. The gains are in `SoundCatalog` (`src/Starfoundry.Ui/Audio/Sfx.cs`). `Godot --headless --path game -- --audio-test` writes every sound to a WAV file and prints its levels; scripted runs (`--demo`, `--*-shot`, `--stress`, `--bench`) are always silent.
