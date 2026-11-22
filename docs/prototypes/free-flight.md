# Free flight

Status: prototype, checked by unit tests and headless screenshots only. Nobody has flown it by hand. Since ADR 0030 the simulation knows the attitude (collisions, ports and ranges follow it).

In space there is no up. The old cockpit camera was a yaw and a pitch about a world "up" (pitch clamped to 85 degrees, no roll), with a compass strip and a flat range ring on the ground, so everything told you which way was straight. This change removes all of it while the player pilots.

## What changed

- **Attitude.** The ship has a full 6-DOF attitude, a unit quaternion in world space (`FlightOrientation` in `src/Starfoundry.Ui`, engine-free). The mouse pitches and yaws it about the ship's own axes, `Z`/`X` (or the left and right arrows) roll it, and there is no pitch limit, no horizon lock and no levelling. The quaternion is renormalised after every turn, so it cannot drift (tested over 50 000 random turns).
- **Thrust.** `W`/`S`, `A`/`D`, `Space`/`C` and boost follow the camera's own axes (the ship's axes while piloting): forward, right and toward the ship's roof, summed and sent as the usual integer `SetThrust` vector in world space. The thrust is a world-space vector, so it does not depend on the attitude; the attitude itself is sent separately (see below).
- **The ship is turned in the client.** The ship structure's root node carries the attitude and is turned about the pilot seat, so the seat (and the eye) stay where the simulation puts the ship, and the factory, the item belts, the engines and the hull turn with it. Aiming and building work in each structure's own frame (the crosshair ray goes through the inverse of the structure's root transform), so you can inspect and build in a ship that is upside down in the world.
- **Walking stays upright in the ship.** The "head" yaw and pitch (clamped to 85 degrees, as before) is relative to the ship, so standing and walking use the ship's own floor whatever the attitude is. While piloting the head rests on the nose (it eases back at about 9 per second); standing up from the seat therefore leaves you upright with respect to the ship's floor.
- **Looking inside the ship while seated.** The mouse steers the ship only while piloting: seated, the mouse captured, not docked, no build tool in hand. With a build tool in hand or `Alt` held, the mouse turns the head instead, so you can look at the workshop and build without spinning the ship.
- **Docking.** A docking port is axis-aligned, so the simulation docks only within about 15 degrees of its axes (see below); once docked the ship settles onto the port's axes (about a second).
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

## Simulation: the attitude is state since ADR 0030

Decision: [ADR 0030](../adr/0030-ship-attitude-in-the-simulation.md). Until that change the attitude was a picture only; now the client quantises it and sends it, and the simulation turns the ship's boxes with it.

- **The command.** `SetAttitude(W, X, Y, Z)` (tag 42, log version 12): a fixed-point unit quaternion, 16384 is 1.0. The client (`SyncAttitude` in `Play.Flight.cs`, `ShipAttitude` and `AttitudeThrottle` in `Starfoundry.Ui`) sends a change of a quarter of a degree at most ten times a second and any smaller change after 0.3 s, only while no other command waits and never while docked. A loaded game adopts the attitude the replay gives.
- **The frame.** The ship turns about the pilot seat (the same point the client turns the picture about). The identity attitude gives exactly the old axis-aligned numbers.
- **Collisions.** Rocks, drifting debris, mines, clouds and wreck range test the module boxes where they are after the turn: a ship rolled 90 degrees along its nose axis stands vertical, and a rock above it is hit by the module that is on top, not by the one that was beside the cockpit. Damage, safe speed, cooldown, shields and push-out are as in [ADR 0028](../adr/0028-world-generation-obstacles-and-collisions.md).
- **Everything that reads a module position** uses the turned position: enemy targeting, shield and defence ranges, sensors, turrets, drills, drone bay range, docking ports, and the overlap tests when founding a station or docking. The player position (the origin), which the laser, the scanner, the ship weapon and the build range measure from, does not move when the ship turns, so a mining order survives a roll.
- **Docking** needs the ship within about 15 degrees of the port's axes (`Misaligned` otherwise; the dock row shows the ports as not ready and the assisted approach turns the ship onto them as it closes in). A docked ship is level: the simulation snaps its attitude to the identity and refuses a turn.
- **Legacy.** Logs of versions 5 to 11 have no `SetAttitude`, so they replay with a level ship exactly as recorded (no rules flag was needed); the checksum mixes the attitude only when it is not the identity.

Remaining differences between the picture and the simulation:

- While you turn, the simulation lags the picture by up to 0.1 s (the picture follows the mouse at once, the simulation the quantised value sent ten times a second); both agree as soon as you stop.
- There is no ship-station or ship-ship collision (none existed), so a rolled ship can still overlap a station it is not docked at.
- The legacy world without a ship structure (scripted demos, `--structures=false`) turns the camera but has no structure to turn.

## Tests and screenshots

- `FreeFlightTests`: the axes of the identity, pitch past the vertical and over the top, yaw, roll, turns about the ship's own axes (order matters), a full roll, 50 000 random turns staying orthonormal, a broken quaternion, the dock alignment, markers and edge arrows mirrored when upside down, the angle to turn independent of roll, and the gizmo axes.
- `--flight-shot=path` (see the [play-test guide](../playtest.md)): level, upside down, tumbling, past the vertical, standing in the upside-down ship (the machine under the crosshair is still found) and the 3D map from two angles.

## Not tested

- Real mouse and keyboard feel: sensitivity, the roll rate (1.5 radians a second), the head returning to the nose, whether `Z`/`X` are comfortable, and whether steering the ship with the mouse feels right for mining and docking by hand.
- Legibility of a long upside-down flight and the usefulness of the gizmo. Marker labels no longer overlap: the most important mark (an enemy, the chosen deposit, the gate, the station) takes its label first, the others stack below or above it or lose the label (the diamond stays), and the ones past 160 cells fade towards 40 percent at 700. This was checked in screenshots only, not by hand.
- The first frames after the mouse is captured: the head turns from the workshop to the nose.
- The autopilot and the assisted docking approach at odd attitudes (they use world-space thrust and should not care, but were only run level).
- The attitude lag against the picture (0.1 s) and the oriented collisions by hand: only the unit tests and the scripted flights have exercised them. The assisted approach turning onto the port was not run in the game.
- The 3D map with a large galaxy (only four systems were known in the screenshots).
