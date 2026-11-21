# Play-test guide

Everything in this repository so far was checked by tests and scripted players. Nobody has played the game by hand. This page is for the first person who does: how to run it, what to try, and how to turn the session into numbers that can be put next to the scripted player's.

## Run it
You need the .NET 8 SDK and Godot 4.7.2 (Mono).

```powershell
dotnet build game -c Debug
```

Open the `game/` folder in Godot and press F5. The game opens in Spanish or English by system language; `L` switches. `F1` shows every control.

## The first ten minutes
1. Follow the steps in the panel at the top right. Each one ticks off by itself.
   The game is played in first person: the mouse looks (the cursor is captured), `W`/`S` fly along the view, `A`/`D` strafe, `Space`/`C` go up and down. `Tab` frees the cursor for the buttons (click in the game to look again); `Esc` leaves a build tool, then frees the cursor. The top-down camera is gone. `E` stands you up from the pilot seat; standing, `W`/`A`/`S`/`D` walk inside the ship (`Shift` runs, no jump, `Space`/`C` unused), flight and the autopilot (`M`) are off, and `E` near the seat sits you down again. Repair moved from `E` to `Backspace`. Walking is client-only and limited to your own ship (see [structures](prototypes/structures.md)). Headless check: `--walk-shot=path`.
2. Fly to the brown rock (look at it and press `W`, or press `M` to fly there on autopilot), mine, press `F` for the ship refinery.
3. Build a drill (`4`), a smelter (`5`), a depot (`8`) and join them with belts (`1`): look down at the ground and the crosshair picks the cell (it turns red when you cannot place there); click to place, for belts press, move the crosshair and release. Right click demolishes, `R` rotates, `Y` changes the recipe of the item you are placing. Click a machine with no tool to see its status; `Tab` then lets you use its buttons.
4. Make plates with an assembler (`6`), build a lab (`7`), press `T` and pick Logistics.
5. When the warning says a wave is coming, build a turret (`9`) fed with ammo by belt, and switch your weapon on (`V`).

## What to try after that
- Walls (`0`) in front of the base and the repair button (`Backspace`) during a wave; a repair bay (`Q`) fed ingots by belt.
- The defence upgrades with `T`, the cargo port (`B`) and the jump to another system (`G`, `J`) when the status panel says the home ore is running low.
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
- **Bottom strip:** the hold bar with item counts, the selected tool and its cost (green when affordable, red when short, grey when locked), and a one-line build palette on keys `1`-`9`, `0`, `Q`, `B`.

Layout check: `Godot --path game --resolution 1280x720 -- --hud-shot=out.png [--hud-details]` saves the HUD of the played game after a second and quits. The projection, compass, toast and health-band logic live in `src/Starfoundry.Ui` (`Markers`, `Compass`, `ToastQueue`, `HudFormat`) and are covered by `HudTests`.

## Saving
`F6` saves, `F9` loads, and a played game saves itself to an autosave every five minutes of real time. The files are in the `saves` folder next to `playlogs` (`%APPDATA%\Godot\app_userdata\Starfoundry Belt Prototype\saves`). A save is the log of what you did and is replayed when loaded, which takes a second or two for an hour-long base ([ADR 0016](adr/0016-saves-are-the-command-log-replayed.md)). If the game says a save cannot be loaded, keep the file and tell me: it means the game changed since it was made. After a load the guided steps are recomputed and a hint may show again. The play log notes the load.
