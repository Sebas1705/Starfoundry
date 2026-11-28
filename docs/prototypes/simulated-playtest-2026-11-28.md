# Simulated playtest study (2026-11-28)

Status: study with measurements, 2026-11-28. Base: `main` at `1eb884b` (the study ran before #127 and #128 merged; the save screen and the release pipeline were not looked at). Question from the owner: could an agent simulate a game and study what to change? This page is the answer: three kinds of evidence, a findings table with severity, the fixes already made and a ranked list of the rest. Everything marked *measured* was counted in a run today; everything marked *proposal* is a suggestion; what no simulation can judge is listed in section 7.

## 1. Method
| Method | What it is | Cost of a run | Limits |
| --- | --- | --- | --- |
| A. Bot telemetry | `tools/Starfoundry.Balance --structures` plays a Normal campaign with real sim commands (`StructureBot`). New in this study: `--telemetry` (a per-minute table and the time budget by goal), `--no-turrets`, `--afk=SECONDS[/EVERY]`, plus the existing `--delay=` (game seconds the player takes per action; the balance bands use 6) and `--difficulty`, `--seed`. | 3 to 4 s of wall clock for 50 game minutes | The bot is one fixed plan: it never wanders, never misclicks, never reads a hint. It measures the game's economy and timing, not a human's confusion. |
| B. Real client | The real Godot client through `tools/agent/playthrough.mjs` on a throwaway copy (`new-play-copy.ps1`): headless runs for the text of every screen, and three short windowed sessions on the primary monitor (window at 100,100, one at a time, `%TEMP%\sf-window.lock`, each closed right after) for screenshots. New phases `tour`, `tour2` and `tour3`; `SF_WINDOWED=1`, `SF_RES=WxH` and `SF_LANG=es\|en` select the mode. | about 1 to 2 minutes per phase | The driver aims with harness shortcuts, so flight and mining feel are not tested. In the windowed run the scripted keys did not all land as in the headless run (the mining shot shows no mining), so the screenshots judge layout and copy only. |
| C. Analysis | Classification, impact and effort, below. | | |

Seeds: 20261121, 20261120 and 777 (the bot's seeds); presets Relaxed, Normal, Hard (Brutal was not run).

## 2. Timeline of the first 30 minutes (Normal, seed 20261121, 6 s per action)
*Measured.*

| Minute | What happens | Note |
| --- | --- | --- |
| 0:07 | hand mining starts | the iron deposit is 40 cells away at the start |
| 0:17 | first iron ingot (workbench, 8 s per craft) | |
| 1:05 | first iron plate | |
| 4:58 | **station founded** | the core costs 12 ingots and 8 plates = 28 smelts + 8 presses = 36 bench crafts of 8 s = 288 s; the bot waits 287 s in this goal |
| 9:11 | emergency power (a reactor has no hydrogen) | the hold starts with 20 hydrogen, about 10 minutes of ship reactor; it lasts until 19:30 |
| about 10:00 | first machine placed | |
| 14:11 | factory running (first depot delivery) | |
| 17:58 / 19:30 | hydrogen cracker built / first hydrogen from ice | ice is mined by hand |
| 20:51 / 21:40 | first defence module / first turret loaded | |
| 22:30 | **wave 1 warning** (30 s later the wave lands) | the first and only on-screen warning |
| 24:57 / 28:05 | laboratory / first research finished | |
| 33:52 | drones deliver ore | |
| 43:04 | first jump | |
| 48:11 | victory (10 waves) | band 48 to 53 |

"First dock" is not a campaign milestone for the bot (it works from beside the station); the level 06 run of the earlier verification docks in 63 s of wall clock.

Per minute (the same run, first 30 minutes). Hold % is the end of the minute; a hold at 75 % stops hand mining (that never happened here). Power short is seconds with a station machine on less than full power.

| Min | Goal at the end of the minute | Hand ore | Drone ore | Hold % | Min power | Power short s | Enemy s | Machines | Acting s | Waiting s | Flying s |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 2 found a station next to the ship | 41 | 0 | 14 | 100% | 0 | 0 | 0 | 0 | 52 | 7 |
| 2 | 2 found a station next to the ship | 0 | 0 | 13 | 100% | 0 | 0 | 0 | 0 | 60 | 0 |
| 3 | 2 found a station next to the ship | 0 | 0 | 11 | 100% | 0 | 0 | 0 | 0 | 60 | 0 |
| 4 | 2 found a station next to the ship | 0 | 0 | 10 | 100% | 0 | 0 | 0 | 0 | 60 | 0 |
| 5 | 2 found a station next to the ship | 30 | 0 | 12 | 100% | 0 | 0 | 0 | 0 | 56 | 0 |
| 6 | 2b station: reactor module (a station with m | 15 | 0 | 15 | 100% | 0 | 0 | 0 | 0 | 48 | 8 |
| 7 | 2b station: reactor module (a station with m | 0 | 0 | 14 | 100% | 0 | 0 | 0 | 0 | 59 | 0 |
| 8 | 2b station: reactor module (a station with m | 0 | 0 | 13 | 100% | 0 | 0 | 0 | 0 | 60 | 0 |
| 9 | 1b station factory on the core floor: ingot | 0 | 0 | 8 | 100% | 0 | 0 | 0 | 6 | 54 | 0 |
| 10 | 1b station factory on the core floor: ingot | 0 | 0 | 5 | 100% | 0 | 0 | 1 | 6 | 54 | 0 |
| 11 | 1b station factory on the core floor: ingot | 30 | 0 | 12 | 100% | 0 | 0 | 1 | 0 | 59 | 0 |
| 12 | 1b station factory on the core floor: ingot | 0 | 0 | 9 | 100% | 0 | 0 | 2 | 6 | 54 | 0 |
| 13 | 1b station factory on the core floor: ingot | 0 | 0 | 6 | 100% | 0 | 0 | 3 | 12 | 51 | 0 |
| 14 | 1b station factory on the core floor: ingot | 35 | 0 | 5 | 54% | 7 | 0 | 4 | 18 | 43 | 0 |
| 15 | 2 more ingot line(s) on the core floor | 31 | 0 | 8 | 50% | 60 | 0 | 5 | 12 | 43 | 0 |
| 16 | 2 more ingot line(s) on the core floor | 30 | 0 | 7 | 38% | 59 | 0 | 6 | 18 | 41 | 0 |
| 17 | 2 more ingot line(s) on the core floor | 70 | 0 | 18 | 35% | 60 | 0 | 8 | 12 | 48 | 0 |
| 18 | 3 hydrogen: ice port + cracker wired into th | 35 | 0 | 8 | 22% | 60 | 0 | 12 | 36 | 27 | 0 |
| 19 | refuel: hand-mine ice (hydrogen low) | 73 | 0 | 21 | 22% | 60 | 0 | 13 | 18 | 28 | 10 |
| 20 | 5b station defence: defence module, battery, | 157 | 0 | 42 | 22% | 29 | 0 | 13 | 6 | 35 | 17 |
| 21 | 5b station defence: defence module, battery, | 129 | 0 | 54 | 100% | 0 | 0 | 13 | 6 | 29 | 27 |
| 22 | 5b station defence: defence module, battery, | 155 | 0 | 49 | 100% | 0 | 0 | 18 | 48 | 4 | 7 |
| 23 | 2 extra cargo module(s) (more hold) | 128 | 0 | 29 | 92% | 30 | 1 | 21 | 30 | 8 | 18 |
| 24 | 4 laboratory: fabrication bay, plate line an | 161 | 0 | 25 | 92% | 27 | 15 | 21 | 18 | 25 | 17 |
| 25 | 4 laboratory: fabrication bay, plate line an | 216 | 0 | 29 | 100% | 0 | 0 | 27 | 60 | 0 | 0 |
| 26 | more defence: station battery on row 6 | 233 | 0 | 33 | 100% | 0 | 0 | 31 | 48 | 9 | 5 |
| 27 | more defence: station battery on row 6 | 110 | 0 | 35 | 100% | 0 | 24 | 31 | 6 | 30 | 21 |
| 28 | research core:logistics | 300 | 0 | 46 | 100% | 0 | 0 | 31 | 0 | 58 | 0 |
| 29 | a second laboratory with its own plate line | 192 | 0 | 48 | 86% | 48 | 19 | 36 | 54 | 5 | 0 |
| 30 | research core:carbon_drilling | 90 | 0 | 43 | 79% | 59 | 4 | 37 | 24 | 35 | 0 |

Time budget over the whole run (*measured*): acting 552 s (19 %), **waiting for items, machines or research 2,089 s (73 %)**, flying 219 s (8 %); power short for 1,586 s (55 %); enemies alive for 491 s. The longest single waits: research of the jump drive 291 s, the first station 287 s, the factory build 274 s, the station reactor 206 s, turret calibration 178 s.

### The three difficulty presets, three seeds
*Measured*, the 6 s bot. Times are minutes:seconds. "Margin" is the wave 1 warning minus the moment the first turret held ammo (seconds; the wave lands 30 s after the warning).

| run | founded | emergency from | factory | hydrogen loop | defence mod | turret loaded | wave 1 warn | research | drones | jump | win |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| hard_20261120 | 4:57 | 2:30 | 14:10 | 19:34 | 20:50 | 21:37 | 21:30 | 28:31 | 38:17 | - | 55:31 |
| hard_20261121 | 4:58 | 9:11 | 14:11 | 19:34 | 20:50 | 21:39 | 21:31 | 28:09 | 36:02 | 49:50 | 51:36 |
| hard_777 | 4:55 | 7:30 | 14:09 | 19:31 | 20:47 | 21:30 | 21:28 | 28:08 | 36:00 | 49:49 | 51:47 |
| normal_20261120 | 4:57 | 2:30 | 14:10 | 19:39 | 21:07 | 21:55 | 22:33 | 28:32 | 34:51 | - | 49:13 |
| normal_20261121 | 4:58 | 9:11 | 14:11 | 19:30 | 20:51 | 21:40 | 22:30 | 28:05 | 33:52 | 43:04 | 48:11 |
| normal_777 | 4:55 | 7:30 | 14:09 | 19:31 | 20:47 | 21:30 | 22:28 | 28:02 | 33:53 | 42:55 | 48:41 |
| relaxed_20261120 | 4:57 | 2:30 | 14:10 | 19:39 | 21:07 | 21:55 | 24:29 | 28:31 | 35:03 | - | 46:37 |
| relaxed_20261121 | 4:58 | 9:11 | 14:11 | 19:29 | 20:51 | 21:40 | 24:30 | 28:09 | 33:33 | 43:32 | 46:15 |
| relaxed_777 | 4:55 | 7:30 | 14:09 | 19:31 | 20:47 | 21:30 | 24:28 | 28:00 | 33:28 | 43:03 | 45:46 |

| Preset | Win band | Wins measured | Power short (s of the run) | Enemies alive (s) | Margin at wave 1 (seed 20261121) |
| --- | --- | --- | --- | --- | --- |
| Relaxed | 44 to 50 | 45:46, 46:15, 46:37 | 1,373 to 1,398 | 342 to 381 | +170 s |
| Normal | 48 to 53 | 48:11, 48:41, 49:13 | 1,586 to 1,635 | 491 to 535 | +50 s |
| Hard | 51 to 56 | 51:36, 51:47, 55:31 | 1,840 to 2,097 | 670 to 755 | **-8 s (22 s before the wave lands)** |

All nine runs lost no module and no machine. The bands hold.

## 3. Dumb-player fuzz
*Measured* (`--delay`, `--no-turrets`, `--afk`). "Lost (ShipLost)" is the game's own end rule.

| Variant | Relaxed | Normal | Hard |
| --- | --- | --- | --- |
| 6 s per action (baseline) | won 46:15 | won 48:11 | won 51:36 |
| 8 s | | won 48:26 | won 52:06 |
| 10 s | | won 49:11 | **lost 41:56, 5 waves** |
| 12 s | | won 50:15 | lost 46:22, 6 waves |
| 13 s | | won 50:45 | lost 46:25 |
| 14 s | | **lost 47:14, 5 waves** | lost 46:27 |
| 15 s | won 52:30 | lost 51:43, 6 waves | lost 46:30 |
| 20 s | won 57:16 | | |
| 30 s | lost 74:51 | lost 55:08 | lost 25:42, 0 waves |
| 60 s | | lost 32:52, 0 waves | lost 31:18, 0 waves |
| never builds turrets | lost 49:36 (7 waves) | station destroyed at 35:26 (4 waves, 29 machines lost) | lost 33:33 (4 waves) |
| away 2 min every 10 | | not decided at 120:00 (station lost at 36:42, 5 waves) | lost 40:09 |
| away 5 min every 10 | | lost 57:27 | lost 28:22, 0 waves |

Seed 20261120 on Normal loses at 14 s too (51:30), seed 777 wins at 14 and 15 s (51:14, 51:33): the edge moves by a few seconds with the seed, but it is a cliff, not a slope: one second per action turns a win with no loss into a lost game.

What the loss looks like (trace of Normal at 15 s): the plan is only about a minute slower than the 6 s one (turret loaded at 23:38 against 21:40; wave 1 warning at 22:46). The wave lands on the unfinished defence, destroys the ship's engine at 23:44 and the ship's reactor at 23:59, the ship cannot fly any more and the scripted player never recovers; the game ends at 51:43 when later waves finish the cockpit. A human has the "needEngine" tip and the Structure tab to rebuild an engine (20 plates); whether that works in that situation was not checked by hand (section 7).

What the fuzz shows, in plain words:
1. **The first wave is the cliff.** It arrives about 8 minutes (Normal; 10 Relaxed; 5 Hard) after the station's noise passes the threshold, with 30 s of warning, whatever the player has built.
2. **The bot band assumes 6 s per action.** If a real first-time player needs 10 s, Hard is lost before wave 6 and Normal is lost from 14 s. Nobody has measured how long a human takes per action.
3. **No state with no way forward was found for a player who keeps the ship alive.** The hold can reach the mining limit (75 %) and then mining stops, but the bot's dump (also the player's "dump the spare ore" button on the Systems tab and the inventory's throw-away) frees it; a hold with capacity 0 (`hold at the mining limit (216/0)`) appeared in the 60 s runs, apparently after the cargo modules were lost: a consequence of the loss, not a lock of its own (not traced further).
4. **No turrets means a fair loss, with notice**: Normal and Hard lose the station at wave 4 to 5 (33 to 35 minutes), Relaxed lasts to wave 7.

## 4. What the real client showed (first-time-player reading)
Screens, in order. Texts came from the headless run (both languages, en and es; the Spanish run has no missing translation, only the names `Starfoundry`, `Normal`, `Brutal`, `Reactor`, `Drones`, `PgUp`, `PgDn`, which are the same word). Screenshots are in `docs/prototypes/visual/playtest/` (JPG, 1280 px wide).

| Screen | Evidence | Reading |
| --- | --- | --- |
| Main menu and new game | texts: Continue (No saved game), New game, Settings, Controls, Credits, Quit; Campaign tab with the four presets and seven numbers (waves to win, enemy damage, time before the first wave "standard", enemy hit points, richness, wave size, reactor output, wave interval) | Clear. "Time before the first wave: standard" does not say how long (8 minutes after the noise starts). |
| Campaign start | [720p-01-campaign-start](visual/playtest/720p-01-campaign-start.jpg): seated in the cockpit; the hint toast says "Follow the steps at the top right" | **There is no step panel at the top right in flight.** The steps live in Tab, Info; in flight only a faint "Next: ..." line shows above the arsenal bar for the first three steps. Fixed (copy). |
| Aiming at a deposit | [720p-02](visual/playtest/720p-02-aim-at-deposit-goal-line.jpg) | The line "Next: Mine 5 ore (Tab, Info: guide)" is dim grey on the dark deck, the hardest text on the screen to read; it is the only on-screen answer to "what now". |
| Workbench panel | [720p-03](visual/playtest/720p-03-workbench-panel-overlap.jpg) | "iron ore rock, 7 cells away" (orange, in the middle of the screen) is drawn on top of the Recipes block; marker labels also show through the panel. Hazard line fixed; the markers are listed. |
| Ship computer, Info | [720p-04](visual/playtest/720p-04-computer-info-goals.jpg) | The best explanation of the goals and the steps; goal counters start non-zero ("Jump to another system 1/2", "second mining laser 1/2", "engines 100/300") because the starter ship and the home system already count, which reads as progress that was never made. |
| Build menu | [720p-05](visual/playtest/720p-05-build-menu-structure.jpg) | Good: costs as icons, "Missing: iron plate x12" in red, locked entries with a lock. "Automation is built in a station" in amber explains the empty Logistics tab. |
| Map tab | [720p-06](visual/playtest/720p-06-computer-map-tip.jpg) | The "Every jump costs 5 carbon" tip stays on screen across the Research, Inventory, Build, Info, Systems and Settings tabs and the pause menu (it is a toast, about 15 s), covering the cockpit screens. |
| Docked | [720p-07](visual/playtest/720p-07-docked.jpg) | Docking text explains U, comma and period well. |
| Blueprint editor, floor plan | [720p-08](visual/playtest/720p-08-blueprint-editor.jpg), [720p-09](visual/playtest/720p-09-floor-plan.jpg), [1080p-11](visual/playtest/1080p-11-blueprint-editor.jpg), [1080p-12](visual/playtest/1080p-12-floor-plan.jpg) | Layout holds at both sizes, nothing clipped; cost, stats and validation panels clear. The floor plan is an empty grid with no sentence about what to do first. The station editor spoke of "the ship" in its status line, its legend and its apply toast. Fixed (copy). |
| Story level 1 briefing | [720p-10](visual/playtest/720p-10-story-briefing.jpg) | Pauses the game and needs Enter ("Enter" is on the button). It said "hold the mine key"; there is no such key. Fixed. |

Not looked at in the client (time box): the end screen, quick save and load in the window (headless it works, see `playability-verification.md`), arsenal switching in flight, the research screen with a lab, inventory drag with the mouse, the ship designer and saved designs beyond the editor's own buttons.

## 5. Findings
Severity: **blocker** (progress impossible), **confusing**, **polish**, **balance**, **performance**. Impact is for a first-time player.

| # | Sev | Finding | Evidence | Status |
| --- | --- | --- | --- | --- |
| 1 | balance | The first wave is a cliff: 14 s per action loses Normal, 10 s loses Hard, 30 s loses Relaxed; the warning is 30 s and the Hard bot is only 22 s ahead | section 3 table; margin column | proposal, 4.1 |
| 2 | balance | The first station costs 288 s of bench crafts, with nothing else to do in that time | 36 crafts x 8 s; waiting 287 s in that goal; hand ore 0 in minutes 2 to 4 | proposal, 4.2 |
| 3 | balance | The station runs on partial power for 55 % of the game (min 22 % at minutes 18 to 20, then about 68 % from minute 34 on) in every preset; emergency power for 10 minutes | power short 1,373 to 2,097 s; emergency 612 to 619 s | proposal, 4.3 |
| 4 | balance | 73 % of game time is waiting (research 100 to 300 s each, factory builds, bench) | time budget | proposal, 4.4 |
| 5 | confusing | The welcome toast pointed to a step panel at the top right that does not exist in flight | screenshot 01 | **fixed** |
| 6 | confusing | The workbench step does not say that a seated pilot must hold Alt to look back; the bench is behind the seat | `obj.refine.hint`; B2 of `playability-verification.md` still open | **fixed (copy)**, 4.5 for the rest |
| 7 | confusing | The Defend step comes after Research and Scan, but the first wave comes at minute 22 and research finishes at minute 28; the hint did not say when the wave comes | order in `StructuresOrder`; timeline | **fixed** |
| 8 | confusing | "Next: ..." line is dim grey on the dark deck and only shows for the first three steps | screenshot 02 | proposal, 4.5 |
| 9 | confusing | Mining at 75 % of the hold: the advice said "spend or move items", not that items can be thrown away | `hud.miningPaused`, `toast.holdFull` | **fixed (copy)** |
| 10 | confusing | Level 1 briefing named a "mine key" | screenshot 10 | **fixed** |
| 11 | confusing | The station editor and its apply toast said "ship" | screenshots 08, 09; toast text | **fixed (copy)** |
| 12 | polish | The hazard line ("iron ore rock, 7 cells away") drew over the workbench panel and every other modal panel | screenshot 03 | **fixed** |
| 13 | polish | Markers ("carbon 49 cells") draw on top of open panels and the cockpit screens | screenshots 03, 04, 06 | proposal |
| 14 | polish | The jump-carbon toast stays over unrelated tabs and the pause menu | texts of tour steps 11 to 15 | proposal |
| 15 | polish | "System: Home (home)" repeats the name; goal counters start above zero | Info texts | proposal |
| 16 | polish | The floor plan opens as an empty grid; the status line under "Selected machine" says only how belts flow | screenshot 09 | proposal |
| 17 | confusing | "Time before the first wave: standard" on the new game screen does not say how long | new game texts | proposal |
| 18 | confusing | A slow player who loses the ship's engine in wave 1 may be unable to continue; not checked by hand | fuzz trace | needs the owner |
| 19 | performance | None found: the sim runs 50 game minutes in 3 to 4 s; frame cost in the client was not measured here | | not measured |

No blocker was confirmed. Nothing was found in the multi-cell, save-slot or release-pipeline areas (they were not touched, and the save screen and installer came after this base).

## 6. Fixes made (small commits on this branch)
- `fix(ui)`: welcome toast no longer points at a missing panel; the workbench step says Alt; the Defend step moves before Research and its hint says when the first wave comes (test of the step order updated).
- `fix(ui)`: the hazard line hides behind every modal panel, not only the computer.
- `fix(ui)`: the station blueprint editor says "station" and "what is built" (toast, status line, legend; `bp.applied.station`).
- `fix(content)`: the First Light briefing names the left click, not a "mine key".
- `fix(ui)`: the hold-at-75 % messages say items can be thrown away (I).
- `chore`: bot telemetry and fuzz flags; the `tour` phases and the windowed capture with the lock.
- Not changed: any balance number, any sim rule, the bot bands (re-run after the changes: Relaxed 45:46 to 46:37, Normal 48:11 to 49:13, Hard 51:36 to 55:31 as before, since no sim file changed).

## 7. Proposed changes, ranked (not done)
Impact is for the first hour; effort S under half a day, M one to two days, L more (agent days, owner reviewing).

### 7.1 Make the first wave readable and fair (impact high, effort S to M)
*Proposal.* Three independent options, cheapest first:
1. **Show it**: a one-line HUD countdown once the noise passes the threshold ("First wave in about N min"), and the number in the new game screen ("first wave about 8 minutes after your machines start"). S. Needs a hand check that it reads well.
2. **Slow it for slow players**: the grace counter (`first_wave_grace_seconds` 480 for Normal) only counts while the signature is above the threshold. Option: do not start the first wave while no turret holds ammo, up to a cap (say 5 extra minutes). That is a sim rule (log version and a legacy flag), so ADR and bot re-measurement. M.
3. **Retune the presets**: the data say Relaxed forgives 20 s per action, Normal 13 s, Hard 8 s. If the owner measures real first-time cadence (say 10 to 15 s), Normal needs about +120 s of grace and Hard about +240 s to forgive it; measure with `--delay=` before choosing.

### 7.2 The first five minutes (impact medium, effort S, but not band-neutral)
*Measured.* The bench takes 8 s per craft (`BenchCraftMultiplier = 8`, `PlayerRules`). With 5 s the station is founded at 3:10 instead of 4:58, but the whole campaign ends 5 minutes sooner (Relaxed 41:10, Normal 43:09, Hard 46:33; all below their bands). With 4 s: 2:34, Normal 41:27. So a faster bench needs a compensating change later (more waves, longer grace) to keep the bands, or a cheaper core is the alternative (12+8 to, say, 8+6 cuts about 1:30 the same way). Either is a balance decision; do not change one number alone. Interim, no balance change: while the queue runs, the guided step could tell the player what to do meanwhile ("mine ore; the bench is working"), and the bench panel already shows the queue.

### 7.3 Power in the middle of the game (impact medium, effort M)
*Measured.* From minute 14 the station is below full power in every run; 22 % at the worst (the factory has 13 machines on 15 emergency power). The bot's own comment says it is a design choice ("83 percent power is the cheaper choice"). For a human it is a red gauge for half an hour. Options: a visible reason and a hint ("Reactor needs hydrogen: ice port + cracker", the `lowPower` tip exists but shows only with 0 hydrogen), or a second reactor earlier. Measure with the bot before changing.

### 7.4 Waiting (impact medium, effort M)
73 % of the time is waiting for research, the bench and machines. A human fills it with mining, flying and scanning, which the bot does not do (it mines only up to a stock). Whether that is pleasant or dull is a *feel* question (section 8); the numbers only say the player has little to build between minutes 28 and 43 (research, jump drive 291 s). A second research slot or shorter research is the usual answer; do not decide without a hand pass.

### 7.5 Smaller clarity changes (impact medium, effort S each)
- Make the "Next: ..." goal line readable (a panel behind the text; the contrast ratio was not measured) and keep it until the third step is done, with the Alt hint in the seated key panel ("Alt: look around").
- Hide the world markers while a modal panel is open.
- Let the jump-carbon tip end when the map tab is closed.
- Drop the repeated "Home (home)"; start goal counters at 0 or label them "(starter ship)".
- A sentence in the empty floor plan: "Pick a machine on the left, click a cell; belts: drag".
- The new game screen: spell out "first wave about N minutes after your machines start".

### 7.6 Things to check by hand (section 8) before more features
Whether the ship can be rebuilt after losing its engine in wave 1 (finding 18).

## 8. What a simulation cannot judge
These need the owner's hands, eyes and ears. Do not read the absence of a finding as "fine".
- **Flight and combat feel**: acceleration, inertia, aim, laser feedback, turret and impact effects (the driver aims with a shortcut).
- **Audio**: nobody has listened to it; no recorded sound was heard.
- **Fun and pacing perception**: the bot says 73 % waiting; only a person can say whether the waiting is relaxing or boring.
- **Real human cadence**: seconds per action, misclicks, reading speed, how often the hint is read at all. The study asks for one measurement: time a first-time player's first 15 minutes with a stopwatch and count the actions.
- **Mouse and window behaviour**: drag, hover, pointer feel, focus loss; the windowed run here sent scripted keys that did not all land.
- **Contrast and readability at real viewing distance**, other monitors, scaling above 100 % (the captures came from one primary monitor).
- **Performance on other hardware**: no frame time was taken.
- **The end screen, saves across versions, the ship designer** (see section 4).

## 9. How to repeat it
```
# A: telemetry (3 to 4 s each); tools/Starfoundry.Balance
dotnet build tools/Starfoundry.Balance -c Release -m:1 -p:UseSharedCompilation=false
tools/Starfoundry.Balance/bin/Release/net8.0/Starfoundry.Balance.exe --structures --minutes 100 --seed 20261121 --difficulty normal --telemetry
#   fuzz: --delay=15 | --no-turrets | --afk=120/600, any --difficulty relaxed|normal|hard

# B: client tour (headless texts); add SF_WINDOWED=1 for screenshots (one window, lock file, closed at the end)
powershell -File tools/agent/new-play-copy.ps1            # the trailing import step may hang after it prints the path: stop it
SF_INERTIA=off node tools/agent/playthrough.mjs <mcp entry> <godot console exe> <copy>/game <outDir> tour
#   tour2 needs `--scenario=sandbox_station` after the phase; tour3 is the story level 1 briefing
powershell -File tools/agent/remove-play-copy.ps1 -Path <copy>
```
`tools/agent/quiet.ps1` wraps any of these at Idle priority on the last two cores. Raw outputs of this study (the per-minute tables of all runs, the fuzz logs and the texts of every screen) were kept outside the repository.
