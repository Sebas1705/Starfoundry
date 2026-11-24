# Pilot feedback round: ship editing, nav screen, turning inertia, mining and weapons

Status: proposed (2026-11-24). Source: the owner's hand-test of main at #82. Everything here is a *proposal* unless marked *measured* (read from the current code or content). Builds on ADR 0027 (mining laser), 0029 (docking), 0030 (attitude), 0031 (construction), 0032 (weapons and shipyard), 0033 (flight feel) and the cockpit displays of #81.

Implementation note (2026-11-24, branch `feature/nav-screen-turn-inertia`): sections N and T are built; see [cockpit displays](../../prototypes/cockpit-displays.md#navigation-screen-and-the-larger-console-2026-11-24) and [flight feel](../../prototypes/flight-feel.md#turn-inertia-2026-11-24-client-only-replaces-the-40-ms-mouse-easing). The starter turn numbers are 90 deg/s and 160 deg/s2 (the proposal of 70 and 120 gave a half turn of 3.4 s, over the 3 s acceptance bound).

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

---

# Part 2 (same day): personal inventory, UI reactivity, HUD information, resource spawn, wings, floating parts

Status: proposed. Owner message after the first part. The **personal inventory is the main objective** of this part.

## I. Personal inventory (main objective)
Goal: a visual, RPG-style inventory so the player can carry items from the ship to the person and from the person into machines (the reactor first), instead of every transfer happening implicitly through the ship hold.

- **Containers** (proposal): the *person* (a grid of slots, default 24, stack size per item from content, e.g. 100 for ores and plates, 20 for fuel cells), the *ship hold* (existing, capacity from cargo modules), the *station stock* (existing from ADR 0029) and *machine slots* (the reactor fuel slot first, then drills, assemblers, turret ammo, the workbench input).
- **Visual inventory panel** (key proposal `C`, check the key panel for a free one; a cockpit button too): a slot grid with item icons (the icon set from #70), count badges, a hover tooltip (name, description, where it is used), and a second panel beside it showing the container you are interacting with (hold, station stock, or the machine). Left drag moves a stack, right drag moves half, shift click quick-moves to the other panel, a sort button, category filters (ore, parts, fuel, ammo), and a trash slot that asks for confirmation. Keyboard and gamepad focus ring like the build menu. All strings es and en.
- **Reactor refuel by hand**: aim at the reactor (or open it from the cockpit or engine room) and its panel shows the fuel slot, the burn time left and an **auto-feed** selector: *none*, *from hold* (today's behaviour, the default so old worlds do not change), *from my inventory*, *hold then inventory*. Dropping hydrogen or fuel cells from the inventory into the slot fills it; the sim burns from the slot first.
- **Sim**: the personal inventory is part of the player state (a list of item stacks within a slot budget). Commands, one new log version (16, together with the weapons and overflow work; ADR 0035):
  - `MoveItems(fromContainer, fromSlot, toContainer, toSlot, count)` for every move above. Validation refuses over-capacity, a wrong item for a slot and containers out of reach (the player must be aboard, docked, or within reach range).
  - `SetFeedSource(machine, source)` for the auto-feed mode.
  - The existing `TransferItems` (hold to station stock) stays and is expressed internally as a `MoveItems`.
  - The checksum mixes the inventory only when it is non-empty (golden-checksum idiom); a legacy flag keeps old logs identical.
- **Where items come from**: mined ore still lands in the hold (the ship mines). On foot the player can take items out of machine outputs, crates and the hold into the personal inventory, and put them from the inventory into machines and the station stock. Death or respawn keeps the inventory in the first version.
- **Acceptance**: unit tests for every container pair, capacity and stack rules, quick-move, split, over-capacity rejection, the reactor burning from its slot, the auto-feed modes, codec round trip, and legacy replay identical; `--inventory-shot` at 1280x720 and 1920x1080 in es and en with the reactor panel open; the UI audit (`--ui-audit`) clean for the new panels; the bot is unaffected (auto-feed defaults to the hold).

## R. UI reactivity
- Every interactive control gives immediate feedback (hover, press, focus, disabled) within one frame and a short transition (100 to 160 ms ease). Panels open and close with a quick fade and slide instead of popping, counters tween, and drag and drop follows the cursor without lag.
- Stop rebuilding whole panels every frame: diff-update labels and tiles, pool controls, and throttle heavy refreshes (the HUD cards refresh at most 15 Hz unless a value changes).
- A measurable budget: a panel opens in under 100 ms and no frame exceeds 20 ms during any panel interaction at 1080p. `--ui-audit` gets a timing pass that opens every panel and records the worst frame.
- An immediate audio click on press (not on release) and subtle hover ticks, both optional in settings.

## H. HUD information: more, with a study first
- **Study first** (deliverable `docs/prototypes/hud-information-study.md`, before changing anything): list every HUD element per context (flying, mining, walking, building, docked, combat), the decision it supports, its screen area and refresh rate, and what is missing. Candidate additions to evaluate: numeric speed and velocity vector, fuel time left, power balance and brownout risk, a per-module health summary, target info (distance, closing speed, hull), threat bearing arrows, resource income per minute, hold fill with a forecast, objective progress, weapon ammo and cooldown, shield state. Recommend a ceiling per context (proposal: at most 7 groups at once, glanceable in under 1 second) and what moves to the cockpit displays or `H`.
- **Then implement** three detail levels on top of the existing HUD modes (F3): *Minimal*, *Standard* (default, richer than today), *Detailed*. A setting and a cockpit button change it, and context still decides what appears.
- **Acceptance**: the study document with a table and a recommendation, the three levels covered by `--hud-shot`, the UI audit clean at 720p and 1080p, and no element covering the centre 30 percent of the screen.

## G. Resource spawn rate
- Raise how many resources the player finds: more deposits per system, richer near home, more ore-bearing rocks in belts and clusters, and slow regrowth of depleted fields (an emptied deposit can respawn small after a long delay). Add a `ResourceDensityPercent` rule (world rules header) so presets can scale it. Proposal: Relaxed 150, Normal 140, Hard 120, Brutal 100 (Normal 140 means 40 percent more than today).
- World generation is deterministic and seeded, and changing counts moves every later draw, so this goes behind a legacy flag (a new bit in the rules flags) and the same log version bump (16). Old logs and old saves regenerate exactly as before.
- Pair it with the mining output proposal of part 1 (M) and the starting hold; re-run the bot and keep Normal at 48 to 52 minutes by retuning enemy pressure if needed.
- **Acceptance**: world generation tests with the new rule (counts per system within bounds, the legacy flag reproduces the old world byte for byte), a deposit regrowth test, and the bot table in the difficulty doc.

## V. Wings against the windows
- In the ship builder the wings sit at the height of the glazing, so they collide visually with the windows. Fix both ways: (a) mount wings lower (root below the window sill; proposal 0.4 to 0.9 above the deck) and (b) the window cutter skips any wall segment where a wing, antenna or pod attaches, so glass never has a solid part through it. Wings never cover the pilot view cone.
- Add a check to the mesh audit (`MeshAudit`): attachments must not intersect the glazing bands. The designer shows a warning icon if a chosen attachment position would block a window or the pilot view.
- **Acceptance**: the audit reports zero attachment-through-glass cases on the starter, nose, ship-row and big ships with wings; a gallery focus shows wings on every module kind; the body collision of wings is solid and matches the model.

## F. Floating parts
- Some models still have loose or floating parts (the reactor is the example): rings, pipes, rails or pillars that touch nothing. Add a *floating parts* audit to `MeshAudit` (each connected component of a module's mesh must touch the hull or another component within a small tolerance), list the offenders per module kind and fix them (attach, extend or remove). Keep furniture solid for the body (as in #82) and re-run the connectivity and hole audits.
- **Acceptance**: the audit reports no floating component on any module kind in a row of all kinds, the reactor is fixed, and the audit helper has tests.

## Order of work for part 2
1. Study and quick fixes in parallel: the HUD study document, the wing and floating-parts audits and fixes, and the UI reactivity pass (client only).
2. Sim (ADR 0035, log version 16, tags 56 to 62 reserved): the personal inventory with `MoveItems` and `SetFeedSource`, together with part 1's free fire, recoil, hold overflow and the resource density rule.
3. The inventory UI and the reactor panel; the HUD detail levels from the study.
4. Mining and resource pace balance, and the bot table.
