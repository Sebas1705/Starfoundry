# Free flight

Status: prototype, checked by unit tests and headless screenshots only. Nobody has flown it by hand.

In space there is no up. The old cockpit camera was a yaw and a pitch about a world "up" (pitch clamped to 85 degrees, no roll), with a compass strip and a flat range ring on the ground, so everything told you which way was straight. This change removes all of it while the player pilots.

## What changed

- **Attitude.** The ship has a full 6-DOF attitude, a unit quaternion in world space (`FlightOrientation` in `src/Starfoundry.Ui`, engine-free). The mouse pitches and yaws it about the ship's own axes, `Z`/`X` (or the left and right arrows) roll it, and there is no pitch limit, no horizon lock and no levelling. The quaternion is renormalised after every turn, so it cannot drift (tested over 50 000 random turns).
- **Thrust.** `W`/`S`, `A`/`D`, `Space`/`C` and boost follow the camera's own axes (the ship's axes while piloting): forward, right and toward the ship's roof, summed and sent as the usual integer `SetThrust` vector in world space. The simulation never sees the attitude.
- **The ship is turned in the client.** The ship structure's root node carries the attitude and is turned about the pilot seat, so the seat (and the eye) stay where the simulation puts the ship, and the factory, the item belts, the engines and the hull turn with it. Aiming and building work in each structure's own frame (the crosshair ray goes through the inverse of the structure's root transform), so you can inspect and build in a ship that is upside down in the world.
- **Walking stays upright in the ship.** The "head" yaw and pitch (clamped to 85 degrees, as before) is relative to the ship, so standing and walking use the ship's own floor whatever the attitude is. While piloting the head rests on the nose (it eases back at about 9 per second); standing up from the seat therefore leaves you upright with respect to the ship's floor.
- **Looking inside the ship while seated.** The mouse steers the ship only while piloting: seated, the mouse captured, not docked, no build tool in hand. With a build tool in hand or `Alt` held, the mouse turns the head instead, so you can look at the workshop and build without spinning the ship.
- **Docking.** A docking port is axis-aligned and the simulation does not know the attitude, so once the ship is docked it eases back onto the port's axes (about a second). You can dock at any attitude.
- **Removed.** The compass strip (`Compass`, `_flightHeading`), the artificial ground rings of the build and mine range (now drawn only in the scripted overview camera) and the world-"down" offset of the mining beam (it is the camera's down now).

## Keys

| Key | Action |
|---|---|
| Mouse | Pitch and yaw about the ship's axes (looks inside the ship with a build tool in hand or `Alt`) |
| `Z` / `X` or left / right arrows | Roll left / right |
| `Alt` (held) | Look around inside the ship instead of steering |

Why not `Q`/`E`: every letter was already taken. `Q` is the 11th quick-build entry and `E` the seat. `Z`/`X` change the build layer, but only with a build tool in hand, so they roll the ship the rest of the time. The key panel shows the roll and look rows while flying; mouse sensitivity and invert-Y still apply.

## Navigation instead of a compass

- **Markers in 3D** (`UpdateMarkers`, `Markers.Place`): the selected deposit, the two nearest other known deposits, the nearest enemy, stations and docking ports, and the gate toward the system chosen on the star map (the direction between the two systems in galactic coordinates, with its distance in map units). Each carries a distance. One that is off screen sits on the screen edge as an arrow that points the shortest way to turn (computed around the view axis, so it is the same whatever the roll) and shows the angle to turn in degrees (`NavFrame.TurnDegrees`).
- **Galactic-frame gizmo** (`NavGizmo`, `NavFrame.Project`), top centre above the speed: the three axes of the galaxy, X, Y and Z, the same fixed frame the star map and every system share. It never moves by itself and is not a horizon: it is how you read your attitude. An axis pointing away from you is drawn faint. It is labelled "Galactic frame".
- **3D star map** (`MapPanel`): the known systems at their real X, Y, Z with lanes, the current system (white ring), the chosen destination (orange ring, name and a dashed route), the ship's nose as a cyan arrow and a corner triad of the galactic axes. Drag to rotate, `Left`/`Right` rotate in steps, the wheel zooms, `Home` resets, a click on a dot chooses it, `Up`/`Down` still choose. The orbit pitch is limited to 83 degrees so the view does not flip over the orbit's own pole; that limit is about the map, not the ship.

## Simulation: nothing changes

The simulation only supports axis-aligned structures and integer thrust vectors, and needs neither to change for this: the attitude is never sent, never saved and never replayed. The command log and the saves are exactly what they were, so no log version and no ADR are needed. A loaded game starts level. Consequences of keeping the attitude out of the simulation:

- A tilted ship is only a picture. Collisions, ranges and docking use the axis-aligned structure the simulation knows, so a ship rolled 90 degrees still collides as its upright box. Fixing that would be a simulation change (an orientation in the structure, with a versioned command log as in [ADR 0026 to 0029](../adr/README.md)); flight does not need it and it was not done.
- The legacy world without a ship structure (scripted demos, `--structures=false`) turns the camera but has no structure to turn.

## Tests and screenshots

- `FreeFlightTests`: the axes of the identity, pitch past the vertical and over the top, yaw, roll, turns about the ship's own axes (order matters), a full roll, 50 000 random turns staying orthonormal, a broken quaternion, the dock alignment, markers and edge arrows mirrored when upside down, the angle to turn independent of roll, and the gizmo axes.
- `--flight-shot=path` (see the [play-test guide](../playtest.md)): level, upside down, tumbling, past the vertical, standing in the upside-down ship (the machine under the crosshair is still found) and the 3D map from two angles.

## Not tested

- Real mouse and keyboard feel: sensitivity, the roll rate (1.5 radians a second), the head returning to the nose, whether `Z`/`X` are comfortable, and whether steering the ship with the mouse feels right for mining and docking by hand.
- Legibility of a long upside-down flight, the usefulness of the gizmo, and the marker label clutter when several marks bunch together (the labels can overlap).
- The first frames after the mouse is captured: the head turns from the workshop to the nose.
- The autopilot and the assisted docking approach at odd attitudes (they use world-space thrust and should not care, but were only run level).
- The 3D map with a large galaxy (only four systems were known in the screenshots).
