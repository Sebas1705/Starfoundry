# First-person view and 3D ambience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play Starfoundry from the cockpit in first person only, with free-direction flight, crosshair aiming and a 3D space ambience.

**Architecture:** A new deterministic command `SetThrust` moves the ship along an integer direction vector (the old axis `SetMovement` stays for tests and bots). The Godot game replaces its orbit camera with a cockpit camera, aims everything with a crosshair ray, and adds a sky, sun and dust. The window maximises and the UI scales.

**Tech Stack:** C# 12 / .NET 8 (`src/Starfoundry.Sim`, xUnit tests), Godot 4.7.2 Mono (`game/`).

**Spec:** [docs/superpowers/specs/2026-11-21-first-person-design.md](../specs/2026-11-21-first-person-design.md)

## Global Constraints
- Everything committed is in English; UI strings in `Loc.cs` as Spanish and English.
- Commits use the user's own git identity, with NO `Co-Authored-By` or "Generated with" trailers; never `--no-verify`.
- The simulation stays deterministic and integer only: no floats in `Starfoundry.Sim`.
- `SetMovement` (-1/0/1 per axis) and every existing balance and sim test must keep giving the same results.
- Thrust components are in [-1024, 1024] with squared length at most 1024 squared; out-of-range is rejected (a thrown `ArgumentOutOfRangeException` from the network method, a rejected result from the command).
- Ship speed: `PlayerRules.MoveUnitsPerTick` (48 units a tick, 240 units a cell, 30 ticks a second).
- Build range 24 cells, mine range 6, drill range 16; everything is built on the Y=0 cell plane.
- Branch `feat/first-person`; open the PR and merge it directly once CI is green (the owner's standing permission for this repository).

## Review Focus
- Thrust of length 0, or a vector slightly over 1024 from float rounding in the game: zero stops the ship; the game must normalise before scaling so it is never rejected.
- A long run in one fixed diagonal direction: position error must stay under one unit per 1000 ticks (remainders carried).
- Jumping, respawning and the demo autopilot while a thrust is set: the thrust must not move a jumping or respawning ship, and must be cleared on a jump like the axis intent.
- Mouse captured when the window loses focus or a panel opens: the cursor must come back, and flight keys must stop sending thrust.
- A very wide or very small window: HUD panels must not overlap or leave the screen.

---

### Task 1: `SetThrust` in the simulation

**Files:**
- Modify: `src/Starfoundry.Sim/Commands.cs` (add the record next to `SetMovement`, line 89)
- Modify: `src/Starfoundry.Sim/CommandCodec.cs` (tag 27, write at ~138, read at ~204, the movement-command list at ~49)
- Modify: `src/Starfoundry.Sim/SimWorld.cs` (dispatch next to `case SetMovement`, line 390)
- Modify: `src/Starfoundry.Sim/BeltNetwork.Economy.cs` (player fields, `SetThrust`, `MoveShip`, clone at ~99 and ~274, checksum ~674, invariants ~703)
- Test: `tests/Starfoundry.Sim.Tests/ThrustTests.cs`

**Interfaces:**
- Consumes: `SetMovement`, `MoveShip`, `PlayerRules.MoveUnitsPerTick`.
- Produces: `public sealed record SetThrust(int Dx, int Dy, int Dz) : Command;` and `BeltNetwork.SetThrust(int player, int dx, int dy, int dz)`; `SetMovement` clears the thrust and `SetThrust` clears the axis intent; per-player remainders `ThrustRemX/Y/Z` (sub-unit, in units of 1/1024) included in clone, checksum and invariants; a jump or respawn clears the thrust like the intent.

- [ ] **Step 1: Write the failing tests** in `ThrustTests.cs`: `Straight_thrust_matches_axis_movement` (thrust (1024,0,0) for 100 ticks gives the same X as `SetMovement(1,0,0)`), `Diagonal_speed_is_constant` (thrust of a unit vector at 45 degrees moves 48 units a tick within one unit over 1000 ticks, so no axis-diagonal slowdown), `Remainders_do_not_drift` (thrust (1,0,0) for 1024 ticks moves exactly `48` units), `Out_of_range_thrust_is_rejected` (1025, and (800,800,0)), `Zero_thrust_stops`, `SetMovement_clears_thrust_and_back`, `Thrust_survives_replay_and_save_roundtrip` (same checksum after `SimWorld.Replay` and `SaveGame`), `Checksum_changes_with_thrust`, `Jump_clears_thrust`.
- [ ] **Step 2: Run** `dotnet test --filter ThrustTests`. Expected: FAIL (type `SetThrust` missing).
- [ ] **Step 3: Implement** the command, codec tag, dispatch and network method. `MoveShip` adds `(thrust * MoveUnitsPerTick + remainder) / 1024` per axis with the remainder kept as `thrust * 48 % 1024` (floor semantics for negatives must be symmetric: use truncation toward zero on the accumulated value and keep the signed remainder).
- [ ] **Step 4: Run** `dotnet test`. Expected: all tests pass, 371 old plus the new ones; the three `BalanceTests` pacing tests unchanged.
- [ ] **Step 5: Commit** `git commit -m "Add SetThrust: ship flight along any integer direction"`.

### Task 2: Cockpit camera and flight

**Files:**
- Modify: `game/Play.cs` (`PlaceCamera`, ship follow ~232, `_yaw/_pitch/_distance/_target`, ship visibility)
- Modify: `game/Play.Ui.cs` (`OnMouseButton`, `OnMouseMotion`, `OnKey`, `UpdateShipControl`/movement intent ~995, `RecenterCamera`, `ToggleMining` waypoint)
- Modify: `src/Starfoundry.Ui/Loc.cs` and `docs/playtest.md`, `docs/prototypes/polish-controls.md` for the new controls

**Interfaces:**
- Consumes: `SetThrust` and `Net.GetPlayerInfo(Player)` position (task 1).
- Produces: `private Vector3 CockpitForward()` (unit view direction from yaw and pitch), `private bool _mouseLook` (true while the cursor is captured), `private void SetMouseCapture(bool on)` and `private Camera3D _camera` placed at the ship position plus a small cockpit offset; the third-person fields `_distance` and orbit code are deleted.

- [ ] **Step 1: Implement** the camera at the ship with yaw/pitch from mouse motion while captured (pitch clamped to about ±85 degrees, sensitivity 0.0025 rad per pixel), field of view 80. Hide the ship model from its own view (visible only when not the camera owner, which is never for now).
- [ ] **Step 2: Implement** flight: `W/S` forward/back along `CockpitForward()`, `A/D` strafe along the camera right, `Space/C` along world up and down, summed, normalised (never above length 1), scaled by 1024 and sent through `Send(new SetThrust(...))` only when the integer vector changes. A `--demo` and bot scripts keep using `SetMovement`.
- [ ] **Step 3: Implement** the autopilot: `M` with a deposit chosen sets a waypoint (the existing `_waypoint`/`_pendingMine` logic) and steers by sending `SetThrust` toward it, stopping within the mine range; the left-click-to-fly on the ground is removed (the crosshair click now inspects or builds).
- [ ] **Step 4: Cursor capture:** captured when flying; `Tab` or any open panel (research choice, map `G`, recipes, help `F1`) and window focus loss release it and stop sending thrust (`SetThrust(0,0,0)`); `Esc` first leaves a build tool, then releases the cursor.
- [ ] **Step 5: Verify by hand** in the Godot window (computer use): fly with the mouse and WASD, check the ship goes where the view points, check `M` flies to the deposit and mines, check tabbing out releases the cursor. Expected: movement matches view direction; no thrust left running after tab-out.
- [ ] **Step 6: Commit** `git commit -m "Fly from a first-person cockpit camera"`.

### Task 3: Crosshair aiming and building

**Files:**
- Modify: `game/Play.Ui.cs` (`CellUnderMouse`, `UpdateHover`, `OnLeftRelease`, drag handling `_dragStart`, tooltip placement, HUD crosshair)
- Modify: `game/Play.cs` (build ghost and belt preview ~749)

**Interfaces:**
- Consumes: the camera and `CockpitForward()` from task 2.
- Produces: `private Vector3 AimOrigin()` and `private Vector3 AimDirection()` (the crosshair ray, the screen centre), and `CellUnderMouse()` renamed `CellUnderAim()` returning the cell where the ray meets the Y=0 plane, or null when looking up or beyond the 24 cell build range.

- [ ] **Step 1: Implement** the aim ray from the screen centre; hover selects machines, deposits and enemies by the existing sphere tests against this ray; a crosshair (small centre ring) added to the HUD, tinted red when the placement is invalid.
- [ ] **Step 2: Implement** building: left click places at the cell under the aim, belts press at one cell, move the crosshair, release to place the line (the existing preview line uses the aim); right click demolishes the aimed cell; `R` rotates; number keys select as before.
- [ ] **Step 3: Tooltips** follow the crosshair (anchored near the screen centre) instead of the mouse.
- [ ] **Step 4: Verify by hand:** build drill, furnace, belt and depot by aiming down at the ground; check the ghost turns red beyond 24 cells and over occupied cells. Expected: the first six guided steps can be completed with mouse and keyboard only.
- [ ] **Step 5: Commit** `git commit -m "Aim, build and inspect with a crosshair"`.

### Task 4: Space ambience

**Files:**
- Create: `game/Ambience.cs` (builds the environment, sky, sun and dust)
- Modify: `game/Play.cs` (call it from the scene setup; per-system hue on jump)

**Interfaces:**
- Consumes: the galaxy seed and current system from `Net.Galaxy`.
- Produces: `public static class Ambience { public static void Build(Node3D root, ulong systemSeed); public static void Retint(ulong systemSeed); }`.

- [ ] **Step 1: Implement** a `WorldEnvironment` with a procedural sky shader (stars, a nebula band and a sun disc), sky ambient light, soft glow, tonemapping; a `DirectionalLight3D` in the sun direction; a `GpuParticles3D` dust cloud following the ship; the base hue chosen from the system seed.
- [ ] **Step 2: Verify** the shader compiles in the game log (`preview_logs`/console shows no shader errors), the frame time stays above 100 fps with `--stress=1000`, and sky colours differ between two system seeds. Expected: no errors, performance within the earlier table.
- [ ] **Step 3: Commit** `git commit -m "Add a procedural space sky, sun light and drifting dust"`.

### Task 5: Window, scaling and docs

**Files:**
- Modify: `game/project.godot` (`window/size/mode=2`, `window/stretch/mode="canvas_items"`, `window/stretch/aspect="expand"`)
- Modify: `game/Play.Ui.cs` (F11 toggle with `DisplayServer.WindowSetMode`, layout checks)
- Modify: `docs/playtest.md`, `docs/prototypes/polish-controls.md`, README link text if it mentions the camera

**Interfaces:**
- Consumes: the finished controls from tasks 2 and 3.
- Produces: `F11` toggles fullscreen; the UI scales with resolution.

- [ ] **Step 1: Implement** the project settings and F11; check `LayoutUi` uses the visible rect.
- [ ] **Step 2: Verify** at 1280x720, 1920x1080 and 2560x1440 (launch with `--resolution`) and with the window resized: HUD panels are readable and never overlap or leave the screen, mouse aiming still hits the cell under the crosshair.
- [ ] **Step 3: Update docs** with the new controls (mouse look, WASD relative to view, Tab frees the cursor, F11) and a note that the top-down camera is gone.
- [ ] **Step 4: Full verification:** `dotnet test` (all pass), then play the first ten minutes by hand through step 6 or later and record times in `docs/playtest.md`.
- [ ] **Step 5: Commit, push, open the PR and merge when CI is green.**
