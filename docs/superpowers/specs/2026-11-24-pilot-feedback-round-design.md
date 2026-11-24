# Pilot feedback round: ship editing, nav screen, turning inertia, mining and weapons

Status: proposed (2026-11-24). Source: the owner's hand-test of main at #82. Everything here is a *proposal* unless marked *measured* (read from the current code or content). Builds on ADR 0027 (mining laser), 0029 (docking), 0030 (attitude), 0031 (construction), 0032 (weapons and shipyard), 0033 (flight feel) and the cockpit displays of #81.

## Feedback in one list
1. Visibility from the pilot seat is now much better (confirmed, no work).
2. Ship editing should go much further: several designs, several types, and the "antenna" part.
3. The navigation screen is too small to read from the seat.
4. Turning with the mouse is instantaneous; a real ship lags when it turns, just as it ramps when it accelerates.
5. Mining output (mined ore and drone mining) is far too low.
6. Weapons: left click fires the weapon in hand; `M` switches between the proton guns and the mining laser. The process must be visible, not an automatic stow/deploy button. (Mining already behaves like this.)
7. Guns cannot be fired without an enemy; they must fire at any time.
8. Shots have no force: some weapons need recoil (the main ones need not).
9. The mining laser goes straight to its target and will not break a rock while the hold is full. The starting hold is too small.
10. Station founding is to be tested later (out of scope here).

## Open questions (the owner decides, defaults in bold)
- "antenna": the transcript reads "la parte de la antena". Reading A: the **antenna/sensor module** (the Sensor kind) needs several types. Reading B: it meant the ship ("nave"), so several ship designs and hull types. This spec covers both (E1 designs and types, E2 antenna types); say if only one was meant.
- Hold overflow: **rock breaks and the ore that does not fit drifts as pickups** (S9 option B) versus jettison or voiding it.
- Mining pace: **raise yield and deposit size, then retune enemy pressure so Normal stays near 49 minutes** versus accepting a faster game.

## Measured facts that shape the specs
- The starter ship holds **100** items from one cargo module (`hold_capacity: 100` on the cargo kind in `content/core.json`). Mining stops at **75 percent** of the hold (`PlayerRules.MiningHoldLimitPercent = 75`), so a stalled laser and a "full" hold at 74/100 are the same thing. This is the "can't break it even with the hold full" report.
- The seated eye and flight pivot sit at (4, 2.7, 6.6) (`AttitudeRules.PivotX/Y/Z`, tied by a test); moving the seat moves the turning centre.
- Guns fire only while armed and, in the sim, a volley needs a target in reach (`BeltNetwork.Threat.cs` fire loop). With no enemy nothing leaves the barrel.
- Turning is client-side: the quaternion is sent to the sim as `SetAttitude` (ADR 0030). The client eases the mouse at about 24 per second, roll at 7 per second (ADR 0033), so turn lag is about 40 ms. That is what reads as instant.

## E. Ship editing (designer)
### E1 Several designs and types
- **Saved designs**: the shipyard keeps named design slots per player (at least 8): save, load (preview, cost delta against the current ship), duplicate, rename, delete. Stored as a module list with positions and options, in the save file and the command log is untouched until `DesignerApply`.
- **Presets**: a few shipped blueprints (miner, hauler, gunship, scout) that the player can load and then edit. Each is just data in `content/` and must validate against the designer rules.
- **Module types**: more than one variant per kind, selectable in the designer: hull skin (the arched, boxy and wide variants from #82 become player-chosen as well as automatic), cockpit styles (wide pilot, nose, flank), engine tiers and layouts, cargo sizes (see S9), reactor sizes. A variant is a content entry with its own cost, stats and model id; the generated one stays the default so old designs still load.
- Designer UX: per-module panel (variant, colour trim), mirror editing, undo/redo, validation hints with icons (what is wrong and where), and the 3D preview showing the pilot eye view.
- Acceptance: a design saved and reloaded gives the same module list (test); every shipped preset passes `CheckDesign`; legacy saves load; es and en strings; `--designer-shot` covers the new panels.

### E2 Antenna (sensor) types
- Sensor kind gets variants: short-range wide scan, long-range narrow scan, and a comms antenna that extends the nav display (shows more systems, ports and gates on the map). Each has a model (mast and dish shapes, so they read from outside), power use and research gate.
- Antenna shape and number show on the hull; several antennas stack their effect with diminishing returns (proposal: second gives 50 percent).
- Acceptance: scanner range comes from the fitted antennas (content-driven), the nav display lists what the best antenna can see, the ship ends with at least one antenna model per variant, tests for stacking.

## N. Navigation screen
- Two layers of fix, both: (a) the starter cockpit gets a legible nav display now, (b) the console becomes modular so a later cockpit can have a bigger one.
- (a) Starter: make the centre screen larger on the console (about 1.6 times the current area) and tilt it towards the eye, raise the type sizes of the key values (target name, distance, speed) and keep scanlines subtle; the unfocused centre screen must be readable at 720p for the three numbers that matter. Do **not** move the seat (it is the flight pivot, ADR 0030). If more is needed, move the screen closer to the eye instead.
- Looking at a screen for about 0.4 s auto-frames it (gentle zoom, no click), leaves when the mouse moves away or when a flight input is pressed; `F4` stays for the full focus.
- (b) Cockpit variants carry their own `CockpitScreenLayout` (screen count, size, tilt) as content, so the designer's cockpit styles (E1) can offer a wide console with one big nav screen.
- The nav page itself: a larger 3D map with the selected route, gates, ports and deposits as icons, a target list with distance and a pick action (ports and gates included), zoom and rotate with the mouse while focused.
- Acceptance: in `--cockpit-shot` at 1280x720 the nav screen unfocused shows its title and three numbers legibly (checked by pixel height of the text, at least 14 px); focused map fills at least 60 percent of the width; no change to the seated eye.

## T. Turn inertia
- Replace the 40 ms easing with real rotation dynamics in the client controller (attitude stays quantised and throttled, ADR 0030 untouched): the mouse and roll keys set a *desired angular rate*, the ship's *angular velocity* approaches it with a maximum angular acceleration, and a flight-assist damping returns it to rest when there is no input.
- Two stats per ship, from content: `turn_rate` (max angular speed, degrees per second per axis) and `turn_accel` (degrees per second squared). Proposal: base 70 deg/s and 120 deg/s2 for the starter; heavier ships (more modules, more mass) turn slower; RCS or thruster modules and the engine research raise both. Mass is the module count weighted by a per-kind mass, content data.
- **Flight assist** toggle (default on): on = the ship damps rotation when you stop moving the mouse; off = it keeps rotating until you counter it (the owner's earlier "free space" wish). A key (proposal `V` is taken; use `G` or a cockpit button) and a cockpit display button.
- The HUD shows angular rate on the cockpit flight display (a small three-axis rate indicator), and the existing `smooth_look` setting becomes "Ship inertia: off / light / full" (off keeps the current 40 ms easing for accessibility).
- Acceptance: unit tests on the controller (rate approaches target monotonically, no overshoot beyond a bound, 50 000 random inputs keep the quaternion orthonormal); a scripted run shows a 180 degree turn takes between 1.5 and 3 s for the starter; the attitude command rate stays within the existing throttle; the bot is unaffected.

## M. Mining output
- *Measured*: mining stops at 75 percent of the hold and the starter hold is 100, so a trip yields at most 75 units. Deposit sizes and laser yield come from ADR 0027 and the world generator.
- Proposal, in order of cost:
  1. Raise the base laser yield and drone mining rate by 2x to 3x (content numbers), and deposit richness scales with distance from home so far fields are worth the trip.
  2. Make the first deposits bigger and add dense fields near home so early game is not starved by the small hold (see S9).
  3. Drone miners: more bays per drone module and faster cycles; a drone carries a bigger load; a mining drone tier from research.
  4. Show the rate: a mining readout on the weapon card (units per second, deposit left) so the player feels the output.
- Pace guard: re-run the StructureBot after the change; the target is Normal 48 to 52 minutes, Relaxed 44 to 50, Hard 51 to 56. If mining is much faster the bot will win early; compensate with enemy pressure (wave size) or research costs, not with a slower hold.
- Acceptance: a human-scale estimate (mine plus haul) and bot times in the difficulty doc; the content tests pass; every deposit remains mineable with the starter laser.

## W. Weapons: fire, switch, recoil
### W1 Controls
- Left click fires the weapon group in hand; `M` cycles **guns (proton), mining laser** (more groups later). No stow or deploy step: the group in hand is shown on the HUD and the barrel or emitter visibly aims and charges. The key panel row shows the group icon.
- Mining stays as it is now (it is already hold-to-fire with a visible beam); this spec keeps that behaviour and only changes the switching.

### W2 Free fire
- Guns fire whenever the player holds fire, with or without an enemy: a tracer leaves the nearest gun along the aim direction (camera ray in the cockpit), travels with range and speed, and hits whatever it meets first (enemies, rocks, mines, debris, wrecks, deposits). Missing the target is allowed; auto-aim stays as an optional assist only when an enemy is inside a cone.
- The sim handles it as a command (the existing `SetWeapons(true)` plus a new aim vector `SetAim`, quantised, log version 16, legacy-gated so old logs behave as before: fire needs a target). Projectiles are sim entities with deterministic integer motion and a lifetime; shots that hit obstacles do damage to destructible ones (mines, debris) and a spark effect to the rest.
- Ammo: unchanged cost per volley (from the hold). A dry hold gives a clear click and a toast.
- Acceptance: tests (shot hits the first thing on its path, no target needed, legacy replay unchanged, determinism over 5 000 ticks), a `--gun-shot` showing tracers with no enemy.

### W3 Recoil
- Per weapon module a `recoil` value (content): a deterministic velocity impulse applied to the ship opposite to the firing direction, scaled by the shot strength and the ship mass, limited by the engine damping. Proposal: main proton guns 0 to small (an effect only), heavy cannons and later missile launchers strong, mining laser none.
- Visible: camera kick scaled to the impulse, a muzzle flash, the ship drifts back and the flight display shows it; with flight assist off the recoil is not corrected.
- Acceptance: unit tests that a volley changes velocity by the expected impulse in world space with the ship's attitude, that identical logs give identical results, and that recoil 0 leaves existing bot behaviour unchanged.

## S. Mining with a full hold, starting hold
### S9 What to do when the hold is full
- Compare:
  - A. The laser stops (current behaviour at 75 percent): simple, frustrating.
  - **B. The rock still breaks and the surplus drifts as ore pickups** that the ship can collect (a short tractor range) or leave; pickups fade after a few minutes. Gives the visible, satisfying result the owner wants and wastes nothing by force.
  - C. The surplus is voided: simple but feels like a loss.
- Proposal B: the 75 percent limit stays only as a *soft* limit for hand mining speed (drops to a trickle past 75 percent so crafted items keep room) and is removed as a hard stop; ore past capacity becomes pickups. A toast "Hold full: surplus floating" shows once per rock. Drones keep waiting at a full hold (they already do), which keeps automation sane.
- Sim change: pickups are items on the ground with positions and a timer, hashed only when present (golden-checksum idiom), gated by a legacy flag and log version 16 (with W2). If this is too large for one change, a quick first step is only A': drop the 75 percent hard stop to 95 percent and raise capacity (below).

### Starting hold
- Raise the starter ship's cargo from 100 to **400** (content: one cargo module of the starter has more capacity or the starter gets a second cargo module). A cargo module tier ladder (Mk1 400, Mk2 800) comes with E1 variants. The hold research bonus stays.
- Acceptance: tests that mining past capacity creates pickups, pickups can be collected, none are created at a non-full hold, replay of old logs matches; the starter ship template and scenario ships validate; the designer cost math includes the new capacities.

## Order of work
1. Quick wins that need only content or the client: starting hold 400, nav screen enlargement and auto-frame, turn inertia controller, `M` and left click group switching and free-fire aim on the client side.
2. Sim work together (one ADR 0035, log version 16, tags 56 to 60 reserved): free-fire projectiles with `SetAim`, recoil, hold-overflow pickups.
3. Designer: saved designs and presets, then variants and antenna types.
4. Mining pace balance and bot re-measure (after 2 so the numbers are real).

## Out of scope
Station founding tests (the owner will do them), multiplayer, new enemy types, missile weapons (recoil data allows them later).

## Risks
- Free-fire projectiles add sim entities per tick; keep a cap (proposal 64 live) and cull by lifetime.
- Turn inertia can feel sluggish: the three levels (off, light, full) and the assist toggle are the escape hatch; tune with the owner's hands, not only tests.
- Raising mining output shifts the whole economy; the bot table is the guard.
- The antenna reading is ambiguous (see open questions).
