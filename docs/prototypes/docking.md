# Docking: the ship moves, the station is fixed

Decision: [ADR 0029](../adr/0029-docking-ship-and-station-are-different.md). Everything here is a *proposal* of ours (nothing taken from another game); numbers are first guesses measured only by tests and the scripted player.

## What changed in the rules
| Rule | Value |
| --- | --- |
| Station | Fixed. No player, no thrust, no `cockpit` or `engine` module (`KindNotAllowed`). A ship takes no `core`. |
| Ports | A free horizontal face of a station `core`, a ship `cockpit` (its two flanks) or a `dock` module (any kind of structure, four faces). A face is free when no module touches it and no ship is docked there. Since [ADR 0041](../adr/0041-ship-and-station-dock-through-a-dedicated-dock-module.md) only a dock module is a port, and since [ADR 0047](../adr/0047-the-dock-is-a-small-collar-on-a-module-face.md) a port is a docking collar, a small piece on a horizontal face of an ordinary module (no dock module in new worlds). |
| `dock` module | Optional content. 10 iron plates and 8 iron ingots, 200 hit points, no machines, +40 to the station stock. |
| Docking range | Centre of the ship's port within 12 cells of the station's port (`DockRules.RangeCells`). |
| Docking speed | Speed at most 12 units a tick, a quarter of the base speed (`DockRules.MaxSpeedUnitsPerTick`): the larger of the commanded speed (the thrust vector or the axis intent, the engines and the power) and, since [ADR 0033](../adr/0033-flight-inertia-solid-deposits-and-hit-volumes.md), the real speed of a ship with inertia, so a coasting ship is too fast. |
| Pose | The ship's port module takes the module cell beyond the station's port; the ship keeps its id, the pilot goes with it, thrust and intent are cleared. Refused (`Overlap`) if any module would intersect another structure. |
| Docked ship | No thrust and no jump (`Docked`), `CanFly` false, speed 0. Stopping is always allowed. |
| Undock | Keeps the pose, returns the thrust. Also by itself when the station or either port module is destroyed. A port module in use cannot be removed, and nothing can be built into the space of a docked ship. |
| Stock | Per station: 60 (core) + 40 per dock module + the capacity of its cargo modules. `TransferItems(item, count, toStation)`; zero or less moves all that fits. |
| Fuel | A station's reactors and the reactors of a docked ship burn the station stock first (fuel cell, else hydrogen), then the hold. |
| Repair | A docked ship regains module hit points 3 times as fast, under the station's power (full satisfaction at the station is enough). |
| Defence | A docked ship uses the better of its own and its station's shield absorption, and its turrets get the station's defence-module bonus (cap 50 percent). |
| Log | Version 11: `DockAt` (39), `Undock` (40), `TransferItems` (41). Older logs cannot hold them and replay unchanged. |

New rejects: `Docked`, `NotDocked`, `NoFreePort`, `TooFast`, `NoRoom`, `AlreadyDocked` (each has an English and a Spanish text).

## The client
| Part | What it does |
| --- | --- |
| Contextual HUD | Nothing about docking shows far from a port. Within 45 cells of a free port of one of your stations: a "Docking port" marker with its distance, and the key panel adds `F` ("Approach and dock", or "Dock" when in range and slow enough, or "Cancel the approach") plus a line "Port N cells away, speed P% of the limit" (red when too fast and close). Docked: `F` Undock, `,` Unload to the station, `.` Load from the station and a "Stock used/capacity" line; the status panel says "docked at a station". A tip explains it once when a free port first comes close. |
| Guide lights | A ring of pulsing lamps round the collar of every free port of a station module; they brighten while the ship is being guided to that port. |
| Airlock | When docked, the two modules at the port replace the wall with a doorway: a heavy hazard-striped frame with a lit lintel on the station, a slimmer collar on the ship, and the door leaves slid into the jambs. The dock module is a hull section with hazard lanes (a ship's carries collars). |
| Assisted approach | `F` from afar: the autopilot flies to a staging point on the port's axis when off axis, then down the axis at a speed that falls with the distance (never above the docking limit when close), and sends `DockAt` when the ports are 3.2 cells apart and slow. Flying by hand, standing up or `F` again cancels it. |
| Animated approach | The simulation snaps the ship to the pose; the view slides it in over 1.3 s (cubic ease-out) and plays the clamp sound at the end. Undocking pushes the ship 14 cells out along the port's axis with the autopilot. |
| Walking across | While docked the body keeps the ship's local frame and the walkable volumes are the ship's modules plus the station's. Crossing the airlock plays the airlock sound and a toast says "inside the station" or "back aboard". Undocking is refused while the body is in the station. |
| Audio | `DockClamp`, `DockRelease` and `Airlock`, all procedural (`SoundBank`), and the usual error sound for a refused command. |
| Engines | A docked ship has no thrust, so its flames are off; a station has no engine module to light. |
| Module menu | A cockpit or engine on a station, a core on a ship and a module into the space of a docked ship are red with their reason. |
| Help | The F1 controls text has a docking paragraph (es and en). |

## Measured
- Scripted player (`StructureBot`, `dotnet run --project tools/Starfoundry.Balance -- --structures --seed N --difficulty normal`, 6 s per action), minutes to the win. The bot never docks and no rule changes for an undocked ship, so the win times must not move; they are the same to the second as the figures recorded in the [mining laser prototype](mining-laser.md) on main (the commit before this change):

  | Run | Before | After |
  | --- | --- | --- |
  | Normal, seed 20261121 | 45:21 | 45:21 |
  | Normal, seed 20261120 | 44:42 | 44:42 |
  | Normal, seed 777 | 44:16 | 44:16 |

- Golden checksums of the legacy and structure worlds (stripped content, `StructureDefenceTests`) unchanged; `DockingTests` check the clone, the replay and the checksum of docked worlds.
- Headless captures with `--dock-shot=path`: the approach (marker, guide ring on the collar, key row with the guidance), the assisted approach docking by itself, the docked view from the cockpit (the lit airlock ahead), the docked ship from outside, and the walk from the cockpit into the station core and back ("Airlock crossed" and "Back aboard" toasts). A transfer of the hold's hydrogen into the stock was checked in the same run (stock 7 to 26).

## Not done (TBD)
- Several ships at one station by choice (the rules allow one per port), ship-to-ship docking.
- A station that powers a docked ship's machines; depots and ports that use the station stock (the hold is still global); a fee or a trader.
- Docking in the story levels: no goal type `dock_at_station` yet (a goal that the scripted player cannot meet would stop it winning), so the campaign flow "found a station, dock, unload, refuel" is playable but optional.
- Balance by hand: the range, the speed limit, the stock sizes, the repair speed-up and the dock module cost are proposals.
- Not tested: real mouse and keyboard flight into a port in a play session, the airlock at 720p and 1080p layouts, the sounds by ear, and a ship docked while a wave hits.
