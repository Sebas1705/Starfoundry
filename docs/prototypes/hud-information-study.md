# HUD information study

Status: study written before any HUD change (part 2 of the pilot feedback round, sections H and R of `docs/superpowers/specs/2026-11-24-pilot-feedback-round-design.md`). Measured numbers come from `--hud-sweep` (below); the recommendation is a *proposal*, not a fact about what players prefer: nobody has played the new levels by hand.

The question: the pilot asked for more information on screen (numeric speed, fuel time, power, targets, income), while earlier feedback asked for a clean view. The answer cannot be "add everything". This study lists what is on screen per context, what decision each element supports, what it costs in screen area, what is missing, and fixes a ceiling.

## How the screen share was measured

`Godot --path game -- --hud-sweep=dir --window=1280x720 [--hud-levels=min,std,det]` puts the played game (structures campaign, English, seated in the starter ship) in six contexts, saves a picture with the HUD and one with the whole HUD canvas layer hidden (two consecutive frames, the world does not step in between), and counts the pixels that changed by more than a small threshold (sum of the three channel differences above 24 of 765). That is the share of the screen the overlay visibly alters, so a dark glass card over a dark background counts a bit less than its rectangle. It prints `HUDCOVER` lines (total share, share inside the centre 30 by 30 percent box) and `HUDELEM` lines (the rectangle of every card that is showing) and writes `coverage.csv`. At 1280x720 and 1920x1080 the shares are the same to within a point, because the canvas stretches with the window (base 1280x720).

Limits: the shot starts with the welcome toasts and the opening hint still on screen (the first minute of a game), so toasts are over-represented compared with minute ten; "combat" is a wave forced with `ScheduleWave` and stepped 20 seconds, not a played fight; "docked" is the assisted approach to a scripted station. The camera is the seat, looking aft through the ship, which is the real start of the game.

## What is on screen today, per context

Refresh: every card is refreshed each frame by `UpdateUi` (about 60 Hz) and its text is set whether or not it changed; the cockpit displays are redrawn 15 times a second. "Area" is the share of the 1280x720 screen taken by the card's rectangle (`HUDELEM`).

| Context | Element | Decision it supports | Area | Missing or weak |
|---|---|---|---|---|
| Flying (seated) | Ship card: hull (threat worlds), flight readiness, power gauge, hydrogen count with time left, integrity | Can I fly, will I run out of fuel, am I being hurt | 5.3 % | No brownout risk, no shield, no damaged-module count |
| | Flight readout: galactic frame gizmo, speed and top speed, throttle gauge | How fast, where is the velocity | 2.7 % | Velocity vector only as the gizmo and prograde/retrograde markers |
| | Hold strip: gauge, used/capacity, one icon and count per item | Do I have room, what do I carry | 1.1 % | No forecast, no income per minute |
| | World markers: selected deposit, two nearest deposits, nearest enemy, port, gate (diamond, label, edge arrows with degrees) | Where to go, what is coming | 0 to 5 % of the centre | Only the nearest enemy gets an arrow; no closing speed |
| | Key panel (up to 22 rows) | What can I press now | 12 % | Mostly rows the player already knows after the first hour |
| | Toasts (up to 3) and the one-line hint | Something happened, what to do next | 1.6 % (opening minute) | Long messages are paragraphs |
| Mining | Same as flying; the hold gauge and the deposit marker carry the decision | Is the deposit worth it, will the hold fill | 21 % | Yield, minutes to full hold, deposit remaining are only in `H` |
| Walking (standing) | Key panel (walking rows), toasts, markers for enemies only; the ship card appears only during a threat | Where can I go, what can I do here | 7.7 % | Nothing about the ship while away from the seat |
| Building | Tool in hand (icon, name, cost icons), the one-line hint, the placement card at the crosshair, a short key panel | Can I afford it, where does it go | 5.6 % | Power balance is not shown while placing power-hungry machines |
| Docked | Flying card plus the dock rows (undock, unload, load, stock n/total) in the key panel | What is in the station, can I leave | 30 % | Stock is a number in a row; the largest share of all contexts |
| Combat | Flying card, wave banner, alert group on top of the key panel, toasts | Where is it, how hurt am I, is my weapon on | 27 % | No target card (distance, closing, hull), no weapon state beside the crosshair, the toast stack reaches into the centre box |
| Any (`H`) | Full status block, steps and goals, research, threat forecast | The long view | opens at the top | Not glanceable by design |
| Any | Crosshair, alert banner (wave) | Always | under 1 % | |

Measured coverage of the current HUD (baseline, `std` is the only level that exists yet):

| Context | Whole screen | Centre 30 % box | Biggest cards |
|---|---|---|---|
| Flying | 24.9 % | 3.2 % (markers) | key panel 11.9, ship card 5.3, flight 2.7 |
| Mining | 21.4 % | 5.0 % (markers, laser beam labels) | same |
| Walking | 7.7 % | 0.0 % | key panel 5.5, toasts 4.1 |
| Building | 5.6 % | 0.0 % | hint 2.4, key panel 2.2, toasts 4.1 |
| Combat | 27.0 % | 10.7 % (toasts 13.7 % of the screen, reaching the box) | key panel 14.0, ship card 5.3 |
| Docked | 30.2 % | 8.4 % (markers, toasts) | key panel 16.0, toasts 8.5 |

Findings:
1. The **key panel is the largest single element** in every flying context (12 to 16 % of the screen). It is static knowledge; it earns its place for a new player and costs a lot to a veteran.
2. **Information cards are small** (ship card 5 %, flight 2.7 %, hold 1.1 %): there is room to add information, not by new cards, but by rows inside the existing ones.
3. **Toasts break the centre rule in combat** (a three-line stack of long messages starts above 60 % of the screen height). Everything else that is a card stays out of the centre box.
4. **Nothing is refreshed with care**: every card sets its text, colour overrides and gauge each frame. The cost is small per card but adds up; most of it can be skipped when the value did not change (see "Reactivity").
5. The only data a pilot asked for that the HUD does not have at all: target distance and closing speed, hold forecast and income per minute, brownout risk, damaged-module count, shield state, objective progress outside `H`, weapon cooldown.

## Candidate additions

Data source is what the simulation already exposes to the client; nothing here needs a change in `Starfoundry.Sim` except where marked.

| Candidate | Decision | Data | Cost | Verdict |
|---|---|---|---|---|
| Numeric speed and velocity vector | Am I too fast to dock or to turn | `_speedCps`, `_velCps` (client) | exists (2.7 %) | Keep. Minimal keeps the number and drops the gizmo |
| Fuel time left | Do I need ice now | `PowerFormat.TimeLeftTicks` | exists, one row | Keep at every level while seated; amber under two minutes (exists) |
| Power balance, brownout risk | Will the machines stall | `PowerStatus` production, demand, satisfaction | one row | Standard shows production/demand (exists); **Detailed adds the risk row** ("demand above supply", "burning down") |
| Per-module health summary | Is a module about to fall | `ModuleHp`, `ModuleMaxHp` | one row | **Detailed**: "n damaged" next to integrity; the cockpit systems page keeps the cell row |
| Target info (distance, closing speed, hull) | Fight or flee, shoot now | enemy positions and `Hp` (client differences over time) | one 2-line card | **Standard in combat and mining** (deposit: remaining, distance), left column under the ship card, never at the crosshair |
| Threat bearing arrows | Which way to turn | markers (already arrows) | none | Standard: nearest enemy (as today); **Detailed: the three nearest** |
| Resource income per minute | Is the mining paying | hold deltas (client tracker) | one chip | **Detailed** in the hold strip |
| Hold fill with forecast | When to turn back | hold deltas | one chip, only while changing | **Standard**: "full in 2:10" only while the hold grows |
| Objective progress | What is next | `ObjectiveTracker` | one line | **Detailed** only (`H` has the long view) |
| Weapon ammo and cooldown | Can I fire now | ship guns have no ammo; cooldown is not exposed by the sim (`Player.WeaponCooldown` is internal) | | **TBD**: Standard shows the weapon group and armed state; a cooldown bar needs a read-only getter in the sim (a follow-up, not done here) |
| Shield state | Am I covered | `ShieldAbsorbPercent` | one row, only with a shield module | **Standard in combat** |

## Ceiling and recommendation

Proposal: **at most 7 groups at once, every group glanceable in under a second**. A group is a card, a strip or a set of markers; crosshair, the alert banner and toasts are always on and not counted. Within a group the detail level adds or removes rows, it never adds cards. A tested rule (`HudDetails`) makes the ceiling a property of the code, not a habit.

| Context | Minimal | Standard (default) | Detailed |
|---|---|---|---|
| Flying | vitals, flight, keys | vitals, flight, hold, markers, keys, target | + objective (7) |
| Mining | vitals, hold, keys | vitals, hold, markers, keys, target (deposit) | + flight, objective (7) |
| Walking | keys | keys, markers | + hold, vitals, objective (5) |
| Building | tool, keys | tool, hold, keys | + vitals (power balance), objective (5) |
| Docked | keys | vitals, hold, keys | + objective (4) |
| Combat | vitals, markers, keys | vitals, flight, markers, keys, target, weapons | + objective (7) |

What moves where:
- **To the cockpit displays** (already there): the flight page carries speed, thrust, hull, power, fuel, hold and the velocity dot; the systems page carries the module cells; the weapons page carries the group and state; the nav page lists targets with degrees. With the HUD mode at *Cockpit only*, the outside cards are off and the displays are the source.
- **To `H`**: research, the full threat forecast, goals, upgrades, the long objective text. Not glanceable, not on the default screen.
- **Minimal** is the screen for people who want the view: the key panel collapses to the rows that matter now (alerts, the seat, docking, the tool in hand, help and the HUD key), the ship card to its vital rows, no hold strip while flying.
- **Coverage targets** (proposal): flying at most 20 % of the screen in Standard (25 % in Detailed), 8 % in Minimal; nothing but markers, the aim card and the crosshair inside the centre 30 by 30 percent box; toasts stay below it.

The key panel is left as it is at Standard and Detailed (it is the teaching surface); the cost shows up in the table above and is the first thing to trim if a later play-test says the screen is still too busy.

## After the change

The numbers below are filled in by the same sweep after the three levels exist (see the end of this page).

## Reactivity findings (part R)

- Hover, press and disabled styles come from the shared theme and switch on the frame the mouse moves, but nothing eases: a button snaps between its looks.
- Panels pop: the build menu, research, designer, pause, help and map list set `Visible` and appear in one frame.
- Counters jump: the hold total, the speed and the gauges move in one step when the value changes.
- `IconCount`, `GaugeRow` and the HUD labels re-set text and colour overrides every frame even when nothing changed; a theme override call makes the control re-measure.
- Audio: the menu screens play a click on release (`Pressed`); the HUD buttons and the panels play nothing, and there is no hover tick.
