# Prototype: flight feel (inertia, solid deposits, hit volumes, engine research)

Status: built (2026-11-24), checked by unit tests, the scripted player and headless screenshots. Nobody has flown it by hand yet. Decision: [ADR 0033](../adr/0033-flight-inertia-solid-deposits-and-hit-volumes.md). Code: `BeltNetwork.Structures.cs` (`MoveInertial`), `BeltNetwork.Obstacles.cs` (`CollideCore`, `SolidDeposits`), `WorldGenContent.cs`, `game/Play.FlightFeel.cs`, `game/FlightHud.cs`, `game/ModelMeasure.cs`. Tests: `FlightFeelTests`. Everything here is a *proposal* of ours (nothing taken from another game); the numbers are first guesses measured by tests, a tool and the scripted player.

## Question
Can the ship have weight (a speed that builds and fades), can a ship touch nothing it should not be able to pass and nothing it can pass, and can the view show speed, all in the deterministic integer simulation, with old logs replaying unchanged and Normal still winnable in about 50 minutes?

## What the owner found by hand, and what changed
| Found | Change |
| --- | --- |
| The ship flew through a mineral deposit | Deposits are solid: a revealed, not exhausted deposit is a rock like an asteroid (module boxes against a sphere, damage by closing speed above 10 units a tick, push-out, the velocity into the rock is lost). 6 damage at 48 units a tick. |
| Part of the ship was drawn inside a rock before the hit | The hit radius is the drawn size: `hit_percent` per kind (rocks 155, debris 110), `deposit_hit_percent` 150 (from `--measure-models`), plus `hull_pad_units` 120 (half a cell) for ships' modules only |
| Thrust was instant (0 to 5.7 cells a second at once) | The thrust sets a target velocity; the velocity follows it at `top / 36` a tick when speeding up and `top / 18` when slowing or turning round. One engine: 6 cells a second in 1.2 s, stop in 0.6 s. Boost (400 percent) raises both. |
| Nothing made the ship faster | `engine_speed` and `engine_accel` research bonuses: ion thrusters, thrust vectoring, plasma drive |
| The speed readout did not change visibly | Speed against top speed with a throttle gauge, prograde and retrograde rings, streaks that stretch, a field-of-view kick, an engine rumble, a longer flame and a rising engine sound |

## Rules (numbers in `content/core.json`, `world.collision` and `world.flight`)
| Rule | Value |
| --- | --- |
| Top speed | 48 units a tick (6 cells a second) times the engine percent (400 at most) times boost (400 percent) times cloud slow times (100 + research bonus) percent |
| Acceleration | `top / accel_ticks` (36) per tick, times (100 + `engine_accel`) percent; braking `top / brake_ticks` (18), also with the bonus |
| Over the top speed (boost ended, a cloud) | shed at the braking rate, or `speed / 36` a tick if that is more |
| Velocity | thousandths of a unit a tick per axis, in the player state; the position carries the sub-milli remainder; part of the checksum only when non-zero |
| Zeroed by | a docking, a jump charge, a lost ship, the respawn, a cockpit or engine lost; the world edge zeroes the clamped axis |
| Docking speed | the larger of the commanded and the real speed, against 12 units a tick |
| Rock hit radius | generated radius times `hit_percent` plus the hull padding (ships only, not docked) |
| Deposit rock | `1.3 cells * RockScale(initial)` (0.6 to 1.6) times `deposit_hit_percent` (150) plus the padding: 1.7 to 3.6 cells |
| Station next to a deposit | a core or module whose box reaches into the rock is refused (`OverlapsDeposit`); beside it is fine |
| Mining | the laser reaches 10 or 14 cells from its module's centre, so the ship can mine from beside the rock; drills (16 cells) and drones work from the deposit's centre as before |
| Legacy | `WorldRules.LegacyCollisions` (flags 64) and `LegacyFlight` (flags 128); logs older than version 15 read with both on |

## Engine research
| Node | Cost | Requires | Bonus |
| --- | --- | --- | --- |
| `core:ion_thrusters` (Basic) | 80 iron plates | logistics | top speed +25 percent |
| `core:thrust_vectoring` (Basic) | 120 iron plates, 20 carbon | ion thrusters, carbon processing | acceleration and braking +40 percent |
| `core:plasma_drive` (Engineering) | 35 engineering science, 200 iron plates | thrust vectoring, alloy hulls | top speed +35 percent, acceleration +40 percent |

All three together: 9.6 cells a second at one engine (38 at boost), 1.2 s to 0.6 s to reach the top. They are optional for the win. A Mk2 engine module was left out: it needs a model and a designer slot (see the ADR). The ship designer shows the layout's engines, top speed and time to reach it, with the research.

## The hit volumes come from the models (`--measure-models`)
`Godot ... -- --measure-models` instantiates every obstacle and deposit model (60 variants of each obstacle kind, 24 of each deposit look at three sizes) and prints how far its vertices reach from the centre in units of the radius the simulation knows:

| Model | Largest | 95 percent of the vertices | Mean | Hit size chosen |
| --- | --- | --- | --- | --- |
| Asteroid (small and big) | 1.82 | 1.53 | 1.17 | 155 percent |
| Drifting debris | 1.23 | 1.09 | 0.69 | 110 percent |
| Floating mine, unstable crystal | 1.20, 1.40 | 1.20, 1.01 | | not solid: they act by distance |
| Derelict wreck | 1.90 | 1.68 | 1.10 | not solid: salvaged by lingering |
| Deposit: iron, carbon, titanium, nickel | 1.6 to 1.85 | 1.37 to 1.50 | 1.1 to 1.2 | 150 percent |
| Deposit: ice, quartz (crystal tips) | 2.5 | 1.9 to 2.0 | 1.3 to 1.4 | the padding covers the tips |

(The generated radius of a rock was the mean of its model divided by 1.3; the model's bumps reach 1.5 times that, and every rock is also stretched by its seed.) The rock model of a deposit is scaled by `DepositRules.RockScalePermille`, the same function the collision uses.

Contact check (`--obstacle-shot=path --obs-scene=depositrock` and `rockmargin` print `MARGIN`): after the ship came to rest on the rock, the nearest vertex of the drawn rock is 1.2 cells (deposit) and 2.0 cells (big asteroid) from any module box, never inside it. The hit sphere is a compromise: it is larger than the drawn rock where the rock is low (the flat top of the rock, in this check), so the player sees a gap of a cell or two there instead of an overlap. Tune `hit_percent`, `deposit_hit_percent` and `hull_pad_units` in the content to taste.

## The view
- **Speed card** (top centre, under the galactic-frame gizmo): `speed / top speed` in cells a second, and the throttle gauge: the fill is the real speed as a share of the top speed now (light blue at boost), the white tick with arrow heads is the speed the thrust asks for. While accelerating the fill chases the tick; braking, the tick sits at the empty end. Hovering gives the numbers (demand, acceleration in cells a second squared).
- **Velocity markers**: a green ring with wings where the ship is going, an amber ring with a cross where it came from, projected from the world velocity (so independent of any up). Hidden below 0.6 cells a second, off screen or behind the camera.
- **Dust streaks** lie along the velocity and stretch from 0.35 to 11 cells long with speed, thin lines at boost; they start at 1.2 cells a second (4 before) and are full at 16.
- **Field of view**: plus 2.5 degrees at base speed, up to 9 at boost, and 1.5 more while boosting and pushing; eased in and out.
- **Rumble**: a camera shake of at most 0.016 cells from the push of the engines and the boost (none in the screenshot runs).
- **Engine flame**: the engines' flame scales with the demand, the acceleration and the boost, and still burns while the ship brakes.
- **Engine sound**: the pitch follows the real speed and rises a little with the push.
- The boost banner and the hazard banner hang below the (now taller) speed card.

## Scripted player (Normal seed 20261121, 6 s per action; the table with every preset is in [structure bot](structure-bot.md))
| | Before | After |
| --- | --- | --- |
| Normal | 48:45 (about 49) | 48:51 |
| Relaxed | about 46 | 47:17 |
| Hard | about 53 | 54:03 |

The bot flies with the ramp (it slows down near its target as before) and stops where its laser reaches a deposit instead of flying to the centre of the rock; its station goes beside a deposit when the first spot is inside one (it tries nine spots); its detours also avoid deposit rocks. The extra minute is the ramp and the detours.

## Tests
`FlightFeelTests` (28): the ramp is progressive and exact, braking is faster and stops exactly, turning round brakes first, a diagonal has the same top speed, boost raises target and acceleration, the axis intent ramps too, a jump and a loss zero the velocity, the docking speed is the real speed, determinism and replay from the log, legacy flight flies exactly like content without inertia, a version 14 log replays to the same checksum, the flags roundtrip and hash as default rules, deposits are solid (stop, damage, one impact) and not with the legacy flag, hidden and exhausted deposits are not solid, mining from beside the rock, founding and extending a station beside and inside a rock, the hit radius and the padding (also for a turned ship), engine research and the designer's numbers, the texts in both languages. The golden checksum tests (legacy and structure worlds on the stripped content) pass unchanged.

## Screenshots (`--obstacle-shot=path --obs-scene=...`)
`ramp`, `cruise`, `boostcruise`, `brake` (the cockpit with the speed card at 16, 70, 80 and 62 ticks), `depositrock` and `rockmargin` (the ship resting on a rock, seen from outside, with the `MARGIN` line). Add `--ui-lang=es|en` and `--window=WxH`.

## Not tested
- The feel by hand: whether 1.2 s to full speed and 0.6 s to stop suit mining runs and docking, whether the field-of-view kick and the rumble are pleasant or tiring (there is no setting to turn them off yet), whether the streaks are enough at 6 cells a second.
- The audio pitch (no sound in the headless runs).
- The assisted docking approach with inertia: the autopilot steers by direction and was not retuned for the ramp (the unit and scripted tests cover docking, not the autopilot in the game).
- Sweeping a station past a deposit: stations are never pushed, only refused at placement.
