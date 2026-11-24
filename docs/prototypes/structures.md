# Prototype: modular structures

Status: in progress (2026-11-21). Decision: [ADR 0018](../adr/0018-modular-structures.md). Design: [structures design spec](../superpowers/specs/2026-11-21-structures-design.md). Plan: [implementation plan](../superpowers/plans/2026-11-21-structures.md).

## The question
What changes if the game starts inside a modular ship whose factory keeps working while it flies, and a station is the same kind of thing without engines?

## What is being built
| Area | Change |
| --- | --- |
| Simulation | A structure is a set of typed modules on a local grid with a world origin. Ships and stations are structures. Machines and belts carry a structure id and local cells; structure 0 is the legacy world |
| Commands | New tags for placing machines, segments and modules in a structure, removing modules and founding a station; a `structures` bit in the log header flag. Old logs and saves read as off |
| Module kinds | Core, cockpit, engine, reactor, cargo, workshop, defence and hull, each with one purpose; costs and allowed roles come from content |
| Starter ship | The campaign opens in a ship with cockpit, engine, reactor, cargo container and a workshop with an ore port, smelter, assembler and depot already working |
| Game | One node per structure, aiming and placing in local space, a procedural model per module kind, HUD with structure name, modules and flight state |

## The rules
- A structure is made of modules; every machine and belt sits on a cell inside a module of its structure.
- Machines and belts go only in modules that allow them (core and workshop); turrets and walls go in defence modules; hull is structure only.
- A ship flies only with a cockpit and at least one engine; more engines mean more speed.
- The hold holds what its cargo modules hold; the starter container holds 60.
- Moving a ship moves its whole factory, which keeps running.
- A station is founded next to the ship for an item cost and cannot overlap another structure. New modules must touch an existing module of the same structure.
- The sandbox, the tests and the scripted balance player keep the old free-build rules.

## How to run it
- The campaign starts in the starter ship.
- `--structures=false` starts a campaign with the old rules, to compare.

## Measured
Measured by `StructurePacingTests` (scripted player, sim commands only, 30 ticks per second, time counted from the first tick of hand mining at the home iron deposit):

| Event | Ticks | Time |
| --- | --- | --- |
| First ore in the hold | 14 | 0.5 s |
| First ingot in the hold | 255 | 8 s |
| First plate in the hold | 525 | 18 s |
| Station affordable (16 ingots + 12 plates) | 3165 | 106 s |
| Station founded next to the ship | 3166 | 106 s |
| First wave | none in 12 minutes | the starter factory signature (8) equals the threshold, so the alert never rises |

Before the fix (hand mining filled the whole hold, core cost 36 ingots): the hold sat at ore 49 of 60, the depot could not deliver, and the station was never affordable. The depot also caps each item at 24 in the hold, so a 36-ingot cost could never be reached at all.

Rules that came out of it:
- Hand mining stops while the hold is at or above 75 percent (`PlayerRules.MiningHoldLimitPercent`), only with structure rules, so 25 percent stays free for factory output.
- The core costs 16 ingots and 12 plates, so both the smelter and the assembler output are spent. Other module costs are unchanged; no single-item cost may exceed the depot cap of 24.
- Over ten minutes without building, ingots rise to the depot cap (24) with no stall longer than 254 ticks (8.5 s), and plates follow.

Not measured: real hand play after this change, flight time to a deposit other than the home one, and later stages (more modules, a station factory).

## Limits
- Walking: `E` stands you up from the pilot seat (and sits you down within about 2.6 cells of it); `WASD` walk (`Shift` runs, 4 and 7 cells per second, eye 1.7 cells above the module floor) in the local space of your ship, clamped to the union of its module boxes cut down to the drawn shell of each module (the floor and ceiling chamfers of a ship hull, its nose, tail and bulkheads, the door frames at every joint and airlock, a station's thick walls; one source of truth, `HullProfile`, shared with the model builder; modules with a roof under the head, the shield, the mining lasers and the guns, are not walkable) (adjacent modules of the same level connect; no jump, no gravity, machines are not obstacles, one level only). The body is client-only state, the sim is unchanged; flight and the autopilot are refused while standing. Walking inside stations works only while docked: the station's modules join the walkable volumes through the airlock ([docking](docking.md)). `--walk-shot=path` saves the workshop, cargo and seat views. Repair moved to `Backspace`.
- **Walking collision is sampled and solid furniture counts.** The body is a 0.6 square sampled on a 5 x 5 grid at feet and head (not at its four corners): a door frame or a wall thinner than the body can no longer be straddled, which was how the body slipped through the frames beside a doorway. Steps are cut into pieces of at most 0.1 cells. The console and the pilot seat of a ship cockpit and the containment vessel of a ship reactor are solid (`HullProfile.Fixture`); an aisle runs past them. Brute-force tests (`BodyShellBruteForceTests`) walk every ship and station kind next to every other, docked ship and station pairs and the starter ship with its pods, with steps up to a second long, and check the stand-up spawn of the seat against every neighbour around the cockpit; the check samples the whole body point by point against the shell.
- **One section for the whole ship.** Every body kind of a ship (hull, cargo, workshop, both reactors, cockpit, engine, defence, drone bay, dock) has the same roof (`HullProfile.Roof`, 3.88) and so the same octagon (same chamfers, same width) at every joint; the ship reactor used to be 3.0 high and stepped against the engine. Only the nose and the tail taper, each inside its own module, and the pods (hardpoints, shield, sensor) stay low on purpose. A pod next to a module (`HullProfile.PodFront`, `PodRear`, from `HullProfile.MaskOf`) has no doorway: the joint is closed with a bulkhead, or with a flat glass windscreen on a cockpit. The reactor's glow shows through a lit hatch in its roof.
- Power, materials, defence of structures and detailed visuals are later projects. Reactors and defence modules exist as kinds with minimal behaviour.
- The balance bot does not play structures, so earlier balance numbers do not describe this mode.
- Docking exists since [ADR 0029](../adr/0029-docking-ship-and-station-are-different.md) ([prototype](docking.md)); still one ship per player and no ship-to-ship transfer.
- Thrust in the command log: a save is the command log replayed, and every change of the ship's thrust is a logged `SetThrust`. Steering with the mouse changes the thrust vector every frame, so the game now sends it through `ThrustThrottle` (`src/Starfoundry.Ui`): components quantised to steps of 64/1024 (typically under 2 degrees of heading error, 5.5 at worst, for directions almost along an axis where rounding up would make the vector too long and it is truncated instead), at most ten changes a second, and a stop is sent at once. The final wanted value is always delivered because the game offers it every frame. The simulation is unchanged. Measured with `Godot --path game --headless -- --load-bench` (a scripted pilot for 30 minutes of game time at 60 frames a second, the view turning all the time, W held, now and then a strafe, a straight stretch and a stop; one routine of 60 s repeated; the starter ship, no threats, JIT warmed up by a first pass, three runs agree):

  | Sending rule | Thrust commands in the log | Save size | Load (replay 54 000 ticks) |
  | --- | --- | --- | --- |
  | Parked (no thrust, the floor) | 0 | 52 bytes | 54 to 64 ms |
  | Old (every change, at most 30 a second) | 25 549 | 638 777 bytes | 98 to 105 ms |
  | New (64/1024 steps, at most 10 a second) | 8 609 | 215 277 bytes | 80 to 82 ms |

  So the log and the save are about a third of their size. The load time of this nearly idle world is dominated by replaying the ticks (and by the sim moving the ship), so the saving there is small, about 20 ms; the commands matter more for long games with big bases, which were not measured here. Not measured: real mouse steering by hand, and the feel of the quantised heading in flight.
- The build menu and research panel are documented in the [play-test guide](../playtest.md); the `ResearchView` and `BuildCatalog` models are covered by tests, the drawing by screenshots only (`--ui-shot`).

## Construction rules (ADR 0031)
Status: built (2026-11-22). Decision: [ADR 0031](../adr/0031-construction-rules-fabrication-bays-and-floor-building.md).

What changed for a player:
- **The starter ship has no factory.** Its modules are cockpit, hull, cargo, reactor and engine; no machine, no belt, nothing buildable. It has a **workbench** (the ship's own crafting table, key `F`) and the hold. `F` cycles the workbench through ingots (1 ore, 2 s), plates (2 ingots, 2 s) and ammo (1 ingot for 4 rounds, 2 s) and then switches it off; it works one thing at a time, from the hold, while you play.
- **A fabrication bay is where you build.** The old workshop module is now the *Fabrication bay* (24 ingots and 8 plates). Add one to the ship with the module tool (`N`) beside the hull, or found a station (`I`): its core floor is buildable too. Machines and belts stand on the **floor** of a bay (the 8 by 8 cells at the bottom of the module) and nowhere else; a defence module takes turrets, walls and belts on its floor. Belts may run from a bay across the seam into the next bay or a defence module; they cannot go through a hull, a cargo module or any other kind, and they cannot climb.
- **Refusals say why**: a cell above the floor is refused with "You can only build on the floor of a fabrication bay"; a module that is not a bay with "That module does not allow this: machines and belts go in a fabrication bay (or a station core), turrets and walls in a defence module".
- **Build tool.** One cursor and no layer: the ghost snaps to the floor cell of the bay under the crosshair (the faint cell lines show only on a bay floor); `Z` and `X` change layer only for the module tool. A world that carries the legacy flag (an old log) plays as before.
- **Demolishing belts.** Right click on a belt removes the whole line and refunds its cells, as it does for a machine. The belt used to be found only through the build plane, so a belt on another layer (or one the ray crossed above the plane) could not be aimed at; the client now picks belts with a ray against the segment's box, like machines, at any height. The card of an aimed belt says "Right click: demolish".
- **Levels.** Level 1 asks for 60 ore and 10 plates (the workbench) and a station. Levels 2 and 3 keep a pre-built bay and factory on their ships and station; the siege station has its defence module beside the bay and the ammo belts run across the seam into it.

Checks: `ConstructionRulesTests`, the golden checksum tests on legacy worlds (the older structure tests run against `tests/Starfoundry.Sim.Tests/legacy/core_pre0031.json`, the content before this change, which carries `floor_building: false`), the structure bot (see [structure bot](structure-bot.md#construction-rules-adr-0031)) and the headless screenshots `--hud-shot`, `--structures-shot`, `--ui-shot` and `--walk-shot` (which now also saves `path.bay.png`, a bay floor with the belt tool in hand, `path.belt.png`, the card of an aimed belt, and `path.wall.png`).

### The workbench in the ship (2026-11-23)
The workbench of ADR 0031 has a model and a crosshair card: see [the ship workbench model](materials.md#the-ship-workbench-model-2026-11-23). It is client-only, in the first hull module of the ship (a cargo module when there is no hull), and changes nothing in the simulation, the logs or the checksums.

## Shell variety, closed joints and solid furniture (2026-11-24)
Status: built. Owner hand-test feedback on the glazed cockpit (#77) turned into six items; each was reproduced first with the audits below, then fixed. All of it is client-side (`src/Starfoundry.Ui`, `game/`): nothing here touches the simulation, the logs or the checksums.

### The audits (`Gallery.tscn --focus=<layout> --audit=holes|coplanar|walk|all [--step=1.0]`)
`--focus` is `ship-starter` (the real starter), `ship-nose` (a nose cockpit with a gun on top and a flank cockpit), `ship-row` (every body kind end to end, pods on top and in front) or `ship-big` (a wide ship with wings and three engines). The run builds the module models, collects every triangle of every mesh and prints `AUDIT` lines (the engine-free checks live in `src/Starfoundry.Ui/MeshAudit.cs`, unit-tested by `MeshAuditTests` and `ShellVarietyTests`):
- **holes**: from walkable points of every module (a few centimetres clear of the shell, five heights) rays go out in 66 directions. A ray that meets no surface the viewer would see is a hole: `seeSky` is a ray seen from inside (a back face culled, a missing liner), `seenFromOutside` an opening in the skin. `--probe=ox,oy,oz,dx,dy,dz` lists every triangle a line crosses, to find out why.
- **coplanar**: pairs of faces facing the same way, within 1.5 mm of one plane, overlapping, and visible from somewhere (`MeshAudit.Scene.CanBeSeen`; a face hidden inside a solid cannot flicker). Godot's reversed-Z depth resolves millimetres, so exactly coplanar overlaps are what flickers.
- **walk**: every position the walking body may take (the real `BodyMotion.Contains`) is tested for triangles cutting through a body box (0.4 wide, from 30 cm above the floor to the head) and for geometry within 12 cm of the eye (the camera would clip it). It is what showed the furniture the body used to walk through.

Results on the current build (holes at step 1.0, and 0.8 on the nose and row layouts): 0 holes on the four layouts. Coplanar: nothing above 5 cm2 left on the starter; what remains are slivers of 0.001 to 0.05 cm2 where two ring beams meet at a corner or a floor decal crosses a seam. Walk: 0 on the starter and on the nose layout; a few edge cases on the wide ships (the coaming of a stairwell, a window sill 5 cm deep at a wall the body can touch on an upper level, the first centimetres of a nozzle housing).

### 3. Closed joints (no hole under the front window, no light leaks)
The holes were all at the joints, never in the middle of a module:
- A hull ended 5 cm short of the module boundary at every open end, so two modules left a 10 cm slit round the whole section; the skin now runs out to the boundary where the side is open (`Octagon`: a side that touches a module has no wall, and its liner keeps its place along that axis, `LinerPoint`).
- A module with something above squared its top corners and stopped 12 cm under the deck above; one beside or below squared the lower or side corners. Two modules along Z could therefore meet with different outlines. `HullProfile.MaskOf` now also records, for the module in front and behind, its own neighbour bits and its skin (bits 16 to 23 and 24 to 29), and the model draws a **joint plate** (`JointPlate`) where its outline is bigger than the neighbour's: the ring of quads from its outline in to the neighbour's inner outline, from both sides. A module above starts at the height where the one below ends (`Octagon` with `Down`).
- Pods (guns, lasers, sensors, shields) are closed at both ends (`HullShell.Pod`): a pod used to leave its low section open against the cockpit's full-height windscreen, which is the hole seen under the front window of the starter. A nose or tail only tapers when nothing touches the module at its side or below (`HullProfile.Tapers`); otherwise it ends blunt (a flat windscreen, a flat end), because a tapering side would pull away from its neighbour.
- The deck is the floor. The white liner used to cover the deck 5 cm above it (the deck markings were never seen and the feet of the body sank into the floor); the liner now starts at the top of the deck (`DeckTop`), there is no liner floor and the deck reaches under the chamfer feet. Only the nose keeps a liner floor, because the tapered deck plate does not reach the tip.
- The canopy glass runs out to an open joint like the hull; the liner end caps sit exactly in the end plane.

### 1 and 6. Collision
- **The front of every cockpit.** The shell numbers for the nose, the windscreen cap and the pod joints were already sound against the liner. `ShellVarietyTests.TheFrontOfEveryCockpitHoldsAgainstEveryApproach` walks six cockpit layouts from every free position and from the seat, at 24 headings, at walking and running speed, in frames from 1/60 s up to one second, and `FromTheDoorwayBehindARunAtTheFrontStopsInsideTheCockpit` runs at the front from the hull behind in frames up to two seconds. What could be passed through was drawn geometry the body was allowed to enter (furniture, see below), the open nose of a cockpit with a neighbour beside it, and the joints that let daylight through.
- **Solid furniture of every kind** (`HullProfile.SolidsOf`, `SkinSolids`, in the same source as the model): cargo racks and crates, the engine's machinery, nozzle housing and coolant tanks (the engine room is machinery from wall to wall: it is entered but not walked), the reactor vessel, pillars, pipes and console, the drone bay's door wall, cradles and mast, the gun deck's mounts, ammo racks and parapets (with a gate in the middle), the core column, the lockers of hulls and workshops on a closed wall. Lockers, cargo doors, hangar walls and workshop skirts are no longer built on a side that is open to the next module.
- **Machines and the workbench** (`BodySolid`, `BodyMotion.StepHeight`): every machine fills its cell and the workbench its footprint for the walking body (`Play.BuildBodySolids`, the docked station's machines too); a solid whose top is within 0.3 cells of the feet (belts, plates, coamings) is walked over. A body standing where a machine is placed is moved to the nearest free spot.
- `--walk-shot=path --walk-bevel` still walks the body into every bevel and the nose; `--audit=walk` is the systematic version.

### 2. The starter cockpit
- **The pilot section** (`HullProfile.SkinWide`): lower chamfer 0.4 instead of 1.0, upper 0.8, so the floor is 7.1 wide instead of 5.9 and the side wall 0.48 to 3.08 high instead of 1.08 to 2.88; the glazing runs from 12 cm over the lower chamfer to 10 cm under the upper one, one band from the front to the back. The nose is shorter (`NoseEnd` 2.3 instead of 3.4), so the aisle beside the console is open further. The seated eye stays at (4, 2.7, 6.6).
- Cleaner view: the navigation table and its lit chart are gone, the console loses its switch row and lamps, the canopy keeps its spine and one ring instead of four rings, and the deck markings (hidden under the liner floor before) show.

### 5. Wall variety (skins)
`HullProfile.SkinOf(kind, mx, my, mz)` picks a section family per ship module, deterministically from its kind and place, so the model and the walking body agree: `SkinPlain` (the chamfered octagon), `SkinArched` (lower chamfer 0.55, upper 1.7: a rounded ceiling and taller side walls; 30 percent of corridor modules), `SkinBoxy` (chamfers 0.3 and 0.45: square walls and ceiling with bulkhead frames, two pairs of 0.46 deep pillars on the closed walls and a ceiling beam at each station, solid for the body; 30 percent) and `SkinWide` (the cockpit). Hull, cargo, workshop, both reactors and the drone bay take one; the engine, the dock, the defence deck and the pods stay plain. The skin is in the module's mask (and the skins of the neighbours are in the masks of their Z neighbours), so a joint between two skins is closed by the same plate as any other mismatch, the viewports are cut to the wall of the skin, and the liner, the collision section and the model all come from `HullProfile.Octagon`. Not built (listed in the request, left for later): stepped and ribbed walls with depth along the module, alcoves and window niches, flared sections, recessed service bays; keyframed rings along Z in `SectionAt` would take them without changing the joint rules.

### Checks
`ShellVarietyTests` (cockpit front brute force, skins, solids, pods, machines, the audit helpers), the updated `HullProfileTests` and `BodyShellBruteForceTests`, and the full `dotnet test`. Not checked by tests (they need the engine): the three Godot audits above and the screenshots (`--focus=ship-nose`, `--flight-shot`, `--walk-shot --walk-bevel`).

## Polish round 8 (2026-11-24): ladders, more wall skins, closed stations, floating parts
Status: built. Client only (`src/Starfoundry.Ui`, `game/`); nothing touches the simulation, the logs or the checksums.

### Ladders between decks (`Stairwell`)
- A module with a module right below it has a **stairwell** when both are of a kind with room for it (`Stairwell.Capable`: hull, cargo, fabrication bay, and a station's core): a square opening in the upper deck at the front (`Stairwell.X0..Z1` = x 3.2 to 4.8, z 0.6 to 2.0, the same for every kind and clear of every kind's furniture), a yellow coaming and a ladder down to the lower deck. The mask bit `HullProfile.StairDown` (bit 30) carries it, so the model, the body and the audits share it. The floor rule of ADR 0031 is unchanged: building stays on the floor of a bay; the opening is a hole in the floor, and a machine standing on the landing refuses the climb.
- **E** at a ladder (within 0.6 cells of the shaft) climbs: up from the lower deck onto the deck behind the opening, down from the upper deck to the floor under the ladder. When both ways exist (a middle deck) the view decides: looking up goes up. The crosshair prompt says "E: climb up/down"; a blocked landing says why. The eye glides between decks. It works in ships and in a docked station (the station's boxes join the walkable volumes).
- `Stairwell.Links/Try/Reachable` are engine-free. `StairwellTests` cover the mask rule, the shaft and landing being free in every capable kind, round trips on every capable pair (ship and station), refusals, and a **flood fill** over a three-level ship and a station core stack that reaches every deck (walking by `BodyMotion.Step`, climbing by `Stairwell.Try`).
- `--walk-shot=path --walk-stairs` stacks a hull and a cargo module, climbs up and down with the real E path and saves `path.stairs.below/top/down.png`, then walks a row of hull modules for the skins (`path.skin.<n>.<skin>.<l|r>.png`).

### More wall skins
`HullProfile.SkinOf` now picks among seven: plain, arched, boxy, **ribbed** (four 0.42 deep ribs and a lit ring on each closed wall, boxy section), **stepped** (a two-step plinth along the wall, 0.5 deep, 0.55 and 1.05 high, boxy section) and **niched** (a window niche and a recessed service bay with a steel hatch and a hazard strip, arched section). All are in `HullProfile.SkinSolids` (solid for the body, from the same numbers the model draws), skip a side that is a doorway, and replace the wall lockers on their wall. The skin number is 3 bits (bit 31 of the mask is the high bit); a neighbour's skin is stored as its section family (`SectionClassOf`), so the existing joint plates close every pair. `WallSkinTests` cover occurrence, mask round trips, solids on closed sides only, clearance of the stairwell and the workbench, brute-force walks and the joints between all skin pairs. **Not done:** flared sections (they need keyframed sections along Z) and the same skins on station walls.

### Closed station stacks and one station height
- A core, a station reactor and a shipyard were 3.0 and 3.2 high; every station kind except the pods is now `HullProfile.Roof` (3.88) like a ship. The walls of a station module with another on top now reach the floor above: a 12 cm slit ran round every stacked station (the holes audit, now run on stations, found 84 rays through it; now 0).
- Station furniture is solid for the body: the wall lockers (`Fixture.StationLockers`), the core's four braces and the turret barrels at rest.

### Skylights, wings, floating parts
- A gun deck on top of another module has two glazed **skylight panes** in its floor (`HullProfile.SkylightPanes`, either side of the gun mounts), so the module under it (the starter cockpit) sees sky through the glass roof of the deck, not plating. Collision and the shell tests are untouched (a pod is not walkable).
- **Wings** (the stub fins of hull, cargo and reactor modules) stand 0.55 above the deck (`HullProfile.WingY`), below every window sill (the lowest is 1.3), with the root on the hull surface at that height; the strobes follow. `Gallery --audit=glass` (`MeshAudit.AttachmentsThroughGlass`) reports a wing, antenna or rigging part cutting through glazing: 0 on the starter, nose, row, big and skins layouts. The designer shows "Warning: n attachment(s) block the pilot view" when a pod stands on, in front of or beside a cockpit (`DesignWarnings.ViewBlockers`; the glazed gun deck is exempt). Wings are outside the hull, so there is no body collision to match. `--focus=ship-row` shows wings on every module kind.
- **Floating parts audit** (`Gallery --audit=floating`, `MeshAudit.FloatingComponents`): every connected piece of a module mesh must touch (within 3 cm) the skin, the deck or a part that does. First run: 121 floating pieces on the ship row. Fixed: ceiling and wall light strips (on pods, beside doorways, on sloping walls, over a missing roof), reactor screens with a doorway behind, vents in doorways, the cargo hatch over a missing roof, stacked crates with a gap, drone cradles and drones, the pilot seat (pedestal, pedals, armrests), turret heads, workshop lamps, the hangar gantry, floor chevrons over the stairwell. The reactor's rings, pipes and pillars were already attached. Now 14 on the ship row, all within 4 to 9 cm (small lamps, a locker tag) or by design (the shield's spinning halo).

### Audit numbers (after the rebase onto #84; `--step=0.8`)
| Layout | holes | coplanar pairs | walk (parts) | floating | glass |
| --- | --- | --- | --- | --- | --- |
| ship-starter | 0 | 29 (area 1.70) | 0 | 5 | 0 |
| ship-nose | 0 | 38 | 0 | 10 | 0 |
| ship-row | 0 (the pinhole at 26.5, 1.0, 0.25 is gone) | 62 | 1 (drone bay wall) | 14 | 0 |
| ship-big | 0 | 63 | a few | 29 | 0 |
| ship-tower (three decks) | 0 | 48 | a few | 9 | 0 |
| ship-skins (12 rows, every skin pair) | 0 | 48 | 2 (hangar) | 134 | 0 |
| station-demo | 0 (was 84) | 60 | 8 (door frame slabs, barrels) | 22 | 0 |

The coplanar pairs are not zero. They are slivers where hull parts meet at the module boundary (joint plates against end caps, window frames against the wall skin); nothing above 0.4 cell2 is left on the starter. The floor decals were split by height (decals along X are 1.75 mm taller than along Z), and the reactor pipes were lowered under the 0.3 step height. The remaining pairs are listed by the audit and were not fixed. The text-fit audit (`--ui-audit`) fails 8 times: the info card overlaps the galactic frame gizmo (an overlap from the cockpit HUD work, not touched here).

### Smaller items
- Speed streaks are longer, wider and brighter (`Ambience.StreakExposure` 0.2 s, `StreakMinLength` 0.6, `StreakHalfWidth` 0.035, `StreakPeak` 1.0; all tunable constants).
- Cockpit displays dim with low power (`CockpitMath.PowerDim`: 1.0 at full power, 0.45 with none satisfied, 0.2 with no power at all) and the speed number is bigger (76 px on the centre screen). The boost chip and the port and gate rows of the nav list already existed; choosing a port or a gate from the list is still not possible.
