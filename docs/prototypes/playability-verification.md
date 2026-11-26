# Playability verification (headless client run)

Date: 2026-11-26. Base: `origin/main` at `8f00c11`, branch `docs/playability-verification`. Method: the real game client (Godot 4.7.2 Mono) driven through the pinned `godot-mcp-runtime` in attach mode on a throwaway copy, **headless** (`--headless`: no window, no GPU), Idle priority, last two logical cores, `windowsHide`, hidden `Start-Process`. Both Godot processes reported `MainWindowHandle=0` at every launch. No screenshot, no OS-level input. Tools: `tools/agent/quiet.ps1`, `tools/agent/playthrough.mjs` (phases `menus`, `story01`, `campaign`, `level06`, `buildprobe`). Run it: `SF_INERTIA=off node tools/agent/playthrough.mjs <mcp entry> <godot console exe> <copy>/game <outDir> <phase> [--scenario=...]` (through `quiet.ps1`). The driver sets `ship_inertia=off` in the copy's own settings because a closed-loop aim oscillates with the default light inertia; the aim itself uses a harness shortcut (below).

Harness additions in the game (read-only or shortcuts, all in `game/Play.Agent.cs`): `AgentState()` (JSON snapshot: tick, position, speed, hold, bag, seat, arsenal slot, laser, bench, deposits, wreck, station, outcome, goals), `AgentFace(deposit)` / `AgentFacePoint(...)` / `AgentFaceBench()` (turn the ship or head; synthetic mouse deltas were not reproducible headless), `AgentDrop(item, n)` (the method the slot menu's entries call; the menu is a popup window).

## Results

| # | Check | Result | Evidence |
|---|---|---|---|
| 1a | Menu to a new Campaign (Normal, seed) | PASS | `StartButton` after `TabCampaign` + `SeedField`: `scenario=campaign`, seated, tick 139 |
| 1b | Menu to a story level | PASS (after fix B1) | Story tab, card `Level_01_first_light`, `StartButton`: `scenario=01_first_light`, goals mine_iron, make_plates, found_station, quick_station, fab_bay; briefing pauses the game |
| 1c | Credits, Settings > Keys open and close | PASS | `menus` phase: credits text list, Controls page with rebind texts, Esc returns to the main menu |
| 1d | No ERROR lines in the Godot log | PASS | `errors: []` in every phase |
| 2a | Starts seated | PASS | `seated=true` |
| 2b | Fly W / Shift / Q E / Space C, speed rises progressively | PASS | cells/s sampled every 0.8 s while W held: 3.6, 6.0, 6, 6, 6, 6; with Shift 19.8, 24 (boost) |
| 2c | K scans | PASS (weak) | K pressed, no error; the five or six nearest deposits are already revealed at start, so the scan showed no new deposit |
| 2d | Iron deposit gets selected | PASS | crosshair on deposit 53, `hover=Deposit` (aim by shortcut, see above) |
| 2e | Laser mines with held fire, hold count rises | PASS | real left-button hold: iron ore 0 -> 15 (3 s) -> 40 (8 s); `laserFiring=true`; stops on release (the 75 percent hand-mining limit was not reached) |
| 2f | Workbench (F) crafts a plate | PASS (with a caveat, B2) | seated, `Alt` held to look, F: ship_smelt ore 40->36, ingots 0->3; F again: ship_press plates 0->1 |
| 2g | V / 1-4 switch weapons | PASS | slot 0 (laser) and 1 (gun); the starter ship has two slots; `armed=true` while firing slot 2 |
| 2h | Holding fire without an enemy gives a projectile/impact event | NOT TESTABLE headless (visual) | `armed=true` is all the state shows; no log line exists for shots |
| 3a | Tab opens; every tab builds (Map, Research, Inventory, Build, Info, Systems, Settings; Designer) | PASS | each `Tab<Page>` handle exists and 30 to 63 visible texts after clicking it; `TabDesigner` exists (the docked-only gating was not exercised) |
| 3b | B / I / M open the right tab; Tab / Esc close | PASS | `computerOpen` true after each; Tab closes; Esc closes without opening the pause menu |
| 3c | Inventory drop and F pickup | PASS (via shortcut for the menu) | `AgentDrop(core:iron_ore, 10)`: hold 37 -> 27, `stackInReach=true`; F: hold+bag 27 -> 37 (the pickup lands in the bag first) |
| 4 | F5, change state, F9 | PASS | saved tick 2112 hold 40; before F9 tick 2279 hold 57; loaded tick 2220 hold 40, position delta 0 |
| 5 | Sandbox: found a station, module, bay machine, belt, power, drill | NOT DONE | Sandbox starts (`scenario=sandbox`), B opens the Build menu (keyboard driven: arrows, Enter; tab buttons Logistics, Production, Defence, Structure; entries incl. "Found station"). Choosing an entry and clicking needs a face of the ship under the crosshair; not completed in the time box. The Cockpit entry showed "Missing: iron plate x12" in a Free build sandbox (see B3) |
| 6a | Level 06 Salvage Run end to end | PASS | outcome `Won`, end screen texts "Victory" / "Next level", `progress.cfg` written (`[06_salvage_run] completed=true best_seconds=62.2 best_stars=3`). Script: Shift+W with the ship turned to the wreck every 0.5 s (7 s), brake, wait for the strip (2 s), W toward the station until the dock guide shows (4 s), F every 2.5 s until docked (48 s), `,` (unload). 63.5 s wall clock, tick 1866 |
| 6b | Level 01 first steps | PARTLY | opens from the menu; mining and the workbench work (2e, 2f); founding the station was not attempted (same Build-menu gap as 5) |
| 7 | Soft-lock hunt | see below | |
| 8 | Fast tier `dotnet test --filter "Tier!=Slow"` | PASS | 1653 passed, 0 failed, 1 m 12 s (before the `LaunchFlags` tests; those 7 pass on their own) |
| - | `tools/ui-audit.ps1` | DEFERRED | needs a window |

Totals: 19 PASS (counting each row once, 2c weak), 1 PARTLY, 1 NOT DONE, 1 NOT TESTABLE (2h), 1 DEFERRED.

## What CI and the bot already prove
The simulation side: `Bot_wins_level_NN` for levels 1 to 3, 6 and 7 (the bot wins level 6 in 16 s with real commands), `ScenarioContentTests` (goals deliverable, texts in both languages) and the reachability checks for levels 4 and 5, the save and load round trip tests, the inventory and drop tests (ADR 0038). This run adds what they cannot: the client path (menu, input, UI nodes, seat, focus, computer, quick save and load, end screen, progress file).

## Bugs found
- **B1 (high, fixed here, with test):** `--agent` and `--ui-lang=` counted as "scripted run" flags in `ChooseScenario` and `ScriptedSeed`, so a game started from the menu while they were present ran the legacy world (fixed seed, no scenario, no goals, no end screen) instead of the chosen mode or level. It only affects harnesses and `--ui-lang` users, but it made the documented `smoke-client.mjs` flow play the wrong game. Fix: `LaunchFlags.IsScripted`, test `LaunchFlagsTests`.
- **B2 (medium, UX):** the workbench is reachable only seated and with `Alt` (look) held, because the bench is behind the seat and the mouse steers the ship. `InteractNow` returns Sit/Climb first when walking, so F at the bench never works on foot. Level 01 needs plates, so a new player must discover Alt. Check that a hint or the briefing says so.
- **B3 (low, unverified):** in a Free build sandbox the Build menu shows "Missing: iron plate x12" for a Cockpit module. Either the label ignores free mode or free build is not applied to this entry; confirm in a window.
- **B4 (low, text):** the laser hint on the HUD says "M stows it" and `docs/playtest.md` step 2 says "press M to take out the mining laser"; M is the Map now (laser slot is key 1 / V).
- **B5 (low):** docking took 48 s in the run because the script pressed F every 2.5 s; a second F while the dock assist runs cancels it (`DockCancel`). A human who taps F twice may see the same stall. Consider a visible "assist on" toast (not checked).

## Soft-lock hunt (code, scenarios, traces; not exhaustively executed)
1. No laser fitted: levels 01 to 05 start with the starter ship that has a laser; a ship designed or damaged without one cannot mine by hand. The Designer has no rule that forces a laser to stay (not verified in code); the workbench still turns ore, but with no ore and no deposit income the run is stuck. Risk: medium in level 04/05 where players redesign the ship.
2. Hold at the mining limit: hand mining stops at 75 percent of the hold. Level 01 needs 60 ore plus ingots and plates for the bay (24 ingots + 8 plates + more), which fits 400 slots, but a hold full of other ore blocks mining with only the "dump spare ore" button (Systems tab) or the drop menu as a way out. The toast exists; the hint on where to find the dump is worth checking.
3. Interact priority: `StackInReach` outranks everything, so a dropped stack in reach hides sit, dock and bench actions until picked up; dropping near the seat and then wanting to dock could confuse. Pick-up goes to the bag first (24 slots); a full bag then the hold: not tested.
4. Level 06: the quick-run secondary is missed if the dock assist is cancelled (B5). The primary goals are always reachable (won in 63 s with a clumsy script).
5. Levels 04 and 05 are validated only by reachability analysis, never played by a bot or a human; titanium needs nickel or titanium systems with no carbon (the jump costs 5 carbon from the hold: the in-game tip warns, but a player who jumps with no carbon cannot return).
6. The story briefing pauses the game and needs Enter; a player who does not find that sees a frozen world. (Harness: Enter works.)

## Not testable headless (run later when the owner is away)
All visual checks: the actual look of every page, the laser beam and projectile effects (2h), HUD layout in es and en at several window sizes, cockpit displays, the end screen layout, fonts, `tools/ui-audit.ps1`, real mouse feel and turn inertia (this run used inertia off and shortcut aiming), audio. Sandbox building (5) and the level 01 station founding (6b) need either a real mouse or a longer scripted Build-menu session.

## Verdict
**A human can complete the game: partly (likely yes, not proven).** The campaign and story level start, the controls in the checklist work in the client, save and load restore the state, and level 06 was won end to end with real key and mouse-button input, with the progress record written. Not shown: building and founding a station by hand (needed by levels 01 to 05 and the campaign goals), levels 01 to 05 and 07 to the end, and combat. Fix B1 matters for anyone starting a game with `--agent`.
