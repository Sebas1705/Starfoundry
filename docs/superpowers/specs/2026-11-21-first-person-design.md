# First-person view and 3D ambience: design

Status: proposed (2026-11-21). Approach B chosen by the owner.

## Intent
Main Sequence, the reference, is played from the cockpit in first person. Starfoundry is played from a top-down orbit camera, so it feels like a map with a ship floating in it. The owner wants first person only, with no top-down mode, and a world that feels like 3D space (sky, depth, light). The window must also adapt to the screen.

## Goals
- Camera inside the ship, mouse look, no third-person or top-down view.
- Flight in the direction you look or move (free 3D direction), assisted and not a dogfight.
- Build, belts, inspection and mining aimed with a crosshair.
- Space ambience: stars and nebula sky, a sun with directional light, drifting dust, asteroids lit with depth.
- Maximised window, UI that scales with resolution, F11 fullscreen.

## Non-goals
- Walking inside ships, interiors, ship editor, co-op (all later, none in this step).
- Changing economy, threat or content.
- A top-down tactical view (the owner decided against it).

## Anti-goals
- A sim change that breaks old logs, tests or the balance bot, or makes replays diverge.
- Building that needs awkward aiming for a 30-machine base.

## Constraints
- The simulation stays deterministic and integer only (ADR 0002); the ship moves only through commands.
- Today `SetMovement` takes -1, 0 or 1 per axis, so the ship flies in 26 fixed directions.
- Everything is placed on the Y=0 cell plane; the mouse ray hits that plane (`CellUnderMouse`).
- Build range 24 cells, mine range 6 cells, drill range 16.

## Approaches
- A. Camera only: WASD snapped to the nearest axis. No sim change but you fly where you do not look. Rejected.
- B. Real heading flight (chosen): a new command carries a scaled integer direction vector; the sim moves the ship along it with exact integer maths.
- C. A then B: an intermediate step of little value. Rejected.

## Design

### Simulation
- New command `SetThrust(int Dx, int Dy, int Dz)`, each in [-1024, 1024] with squared length at most 1024 squared (the game sends a unit vector scaled by 1024). Anything else is rejected, not clamped.
- `SetMovement` stays as is (tests, bots, the demo keep working) and clears the thrust; `SetThrust` clears the axis intent. A non-zero thrust moves the ship each tick by `thrust * MoveUnitsPerTick / 1024` per axis, with sub-unit remainders kept per axis so slow or diagonal movement loses nothing. Speed along any direction is the same as along one axis.
- The remainders and thrust are part of the checksum and `ValidateInvariants` (length bound).
- A new command tag in `CommandCodec`; old logs and saves still load, they simply contain no `SetThrust`.
- Tests: straight line equals the axis result, diagonal speed is constant, remainders do not drift over a long run, out-of-range thrust rejected, replay and save round trip with thrust, checksum changes with it.

### Game (Godot)
- Camera: a `Camera3D` at the ship's cockpit position, yaw and pitch from the mouse (captured while flying), field of view about 80 degrees. Ship model hidden from its own camera or reduced to a cockpit frame (builder's call).
- Flight: mouse look; W/S forward and back along the view, A/D strafe, Space/C up and down, relative to the camera. Each frame the game sends `SetThrust` only when the intent changes. The existing autopilot (click to fly) becomes the key that flies to the selected deposit (M) and the map flight; waypoints set thrust toward the target.
- Aiming: the crosshair ray replaces the mouse ray. It selects machines, deposits and enemies by raycast; for building it meets the Y=0 plane (look down to build) and the ghost snaps to that cell within the 24 cell range. Belts: press at one cell, move the crosshair, release to place the line.
- Cursor: captured by default; Tab or any panel (research, map, recipes, help) frees it. The UI panels stay as they are but the pointer is hidden when flying.
- HUD: minimal; crosshair, tool, hold bar, alert, hint line and guided steps stay, the build palette stays on the 1 to 9 keys. Hover tooltips follow the crosshair.
- Ambience: procedural sky (stars, a nebula band, a bright sun disc) via a `WorldEnvironment` sky shader, a directional light for the sun, ambient from the sky, soft glow, drifting dust particles near the ship, deposits and machines already lit; each system picks a sky hue from its seed. Reduce-motion respects dust.
- Window and scaling: `window/size/mode` maximised, `stretch/mode = canvas_items`, `aspect = expand`, F11 toggles fullscreen. The layout code reads the viewport size, so it adapts.
- The old orbit camera code is removed.

### Balance bot and demos
Bots keep using `SetMovement`; the sim outputs for the scripted runs must not change (the existing balance tests are the proof).

## What is left to the builder
- Exact mouse sensitivity, field of view, cockpit frame look, dust density, sky colours.
- Whether the rest of the ship model shows in the camera.
- The precise intent-to-thrust mapping for strafe and combined keys.

## Risks
- Aiming at a plane from the cockpit may feel poor for far placements; if it does, add a build-height offset or a snap-to-ship-level plane later.
- Motion sickness: keep a short smoothing, a steady horizon cue and a reduce-motion option.
- Mouse capture inside the Godot editor window needs testing.
