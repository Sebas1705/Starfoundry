# Prototype: materials and veins (visuals)

Status: in progress (2026-11-21). Part 1 of the [playable roadmap](../superpowers/specs/2026-11-21-playable-roadmap-design.md).

## The question
Can a player tell what a deposit holds, and how rich it is, from the rock alone?

## What each deposit looks like
Every deposit is a procedural asteroid (`game/DepositModels.cs`, class `DepositView`). The rock is an irregular low-poly shape seeded by the deposit id, scaled by its initial amount (0.6x to 1.6x). Ore chunks stick out of it:

| Item | Chunks |
| --- | --- |
| Iron ore | Blunt rusty orange-brown lumps, faint warm glow |
| Carbon | Black glossy domes on a lighter grey rock, almost no glow |
| Ice | Tall pale-blue translucent spikes, cold glow |
| Titanium ore | Angular silver-white metallic slabs |
| Nickel ore | Stubby greenish-grey nuggets |
| Quartz | Clear violet hexagonal crystals, bright glow |

## The density rule
- Chunk count: 2 at density 25, about 9 at 100, about 22 at 200 (`DepositView.ChunkCount`).
- Chunk size grows with density, glow has three buckets: poor (below 60), normal (60 to 120), rich (above 120).
- As `Remaining` falls the chunks shrink and disappear in 5 percent steps; a depleted rock is bare and dark. The rock itself keeps its size.
- Labels, the hover tooltip and the HUD lines show the density as a word plus percent ("Poor vein (25 %)", "Normal vein", "Rich vein").
- Meshes are cached per (deposit seed, item, density, step) and chunk materials per (item, density bucket).

## Reviewing it
`godot --path game res://Gallery.tscn -- --focus=deposits --angle=0 --pitch=58 --fov=30 --frames=30 --shot=out.png` renders one row per item with density 25, 100, 200, 30 percent left and depleted.

## Carbon everywhere (second audit, 2026-11-21)
A jump costs 5 carbon from the hold, so every system archetype must contain a carbon deposit the player can mine by hand, or a ship that arrives short is stranded. Nickel fields and titanium veins now carry one small carbon deposit (200 to 400 items, density 80 to 120); the other archetypes already had one (see [ADR 0022](../adr/0022-structure-economy-fixes.md)). `JumpSafetyTests.Every_archetype_has_carbon_so_a_return_jump_is_possible` enforces it. The star map's Jump button shows the cost and turns red while the hold cannot pay it.

## Resource expansion (2026-11-22, part C of the modes design, [ADR 0024](../adr/0024-resource-expansion.md))
Four items sit above the old tree: `core:silicon` (smelter: 2 quartz, 5 s), `core:circuit` (assembler: 1 silicon + 1 nickel plate, 6 s), `core:titanium_alloy` (assembler: 1 titanium plate + 1 carbon, 5 s) and `core:fuel_cell` (hydrogen cracker: 2 hydrogen + 1 nickel plate, 8 s). They are not deposits, so they have no veins and no item models yet; they only travel on belts and sit in the hold.

### The chains
```
quartz --smelter--> silicon ---------------+
                                           +--assembler--> circuit
nickel ore -> nickel ingot -> nickel plate-+
titanium ore -> titanium ingot -> titanium plate --+
                                                   +--assembler--> titanium alloy
carbon ---------------------------------------------+
ice -> hydrogen --+
                  +--cracker--> fuel cell
nickel plate -----+
```

### Research and what it unlocks
```
advanced_plating -> silicon_processing (smelt_silicon)
                 -> electronics (make_circuit, make_alloy)
                      -> shielding      (Shield module)
                      -> sensors        (Sensor module)
                      -> advanced_power (Reactor Mk2 module, make_fuel_cell)
```

### Where each item is used
| Item | Made by | Used for |
| --- | --- | --- |
| Iron ore | hand mining, drills (legacy) | smelter (`smelt_iron`), ship micro-refinery |
| Iron ingot | smelter | iron plate, ammo, machine costs, core, hull, cargo, workshop and defence modules, belts mk1, repair bay and cargo port items |
| Iron plate | assembler | almost every research cost, belts mk2 and mk3, lab, turret, wall, repair bay and cracker costs, the cockpit, engine, reactor, core, defence, drone bay, shield, sensor and Mk2 reactor modules |
| Carbon | hand mining (a port carries it) | jump cost, engine and reactor modules, advanced logistics and jump drive research, titanium alloy |
| Ammo | assembler (`make_ammo`) | turrets |
| Ice | hand mining | hydrogen (cracker) |
| Hydrogen | cracker | reactor fuel (1 burn interval), fuel cells |
| Fuel cell | cracker (`make_fuel_cell`) | reactor fuel (6 burn intervals), preferred over hydrogen |
| Titanium ore | hand mining | titanium ingot, titanium smelting research |
| Titanium ingot | smelter | titanium plate, advanced plating research |
| Titanium plate | assembler | drone bay, titanium alloy, drone automation, electronics and shielding research |
| Nickel ore | hand mining | nickel ingot, nickel smelting research |
| Nickel ingot | smelter | nickel plate, advanced plating research |
| Nickel plate | assembler | drone bay, circuits, fuel cells, silicon processing, sensors and advanced power research |
| Quartz | hand mining | silicon, advanced plating, drone automation and silicon processing research |
| Silicon | smelter (`smelt_silicon`) | circuits, sensor module, electronics, shielding, sensors and advanced power research |
| Circuit | assembler (`make_circuit`) | shield, sensor and Mk2 reactor modules |
| Titanium alloy | assembler (`make_alloy`) | shield and Mk2 reactor modules |

### The new modules in the gallery
`godot --path game res://Gallery.tscn -- --focus=modules --at=89.5,1.5,4 --angle=30 --pitch=28 --dist=15 --frames=70 --shot=out.png` frames the shield (ship row; the sensor is at x 99 and the Mk2 reactor at x 108.5, the station row is at z 20). The shield is a low plated base with an emitter pylon, a spinning halo and a faint blue dome; the sensor is a dish on a mast with a turning translucent green sweep and status lights; the Mk2 reactor shows a cyan core above the roof inside two crossed gold rings. All three dim with the power of their structure.

Spare ore can be thrown away with `Ctrl+Delete` (`DumpItems`): the largest stack of carbon beyond the jump cost, quartz, titanium ore, nickel ore, or ice while hydrogen is at least 20.

## Home deposits (second balance pass, 2026-11-22)
Home iron holds 12000 ore (it was 4000) and home ice 1500 to 2000 (it was 600 to 800); carbon stays at 1500. The structure bot used all 4000 iron by minute 50 to 60 (hand mining plus drones), long before the 17 research nodes (about 3 500 plates, 7 000 ore) and the buildings were paid, and the ice by minute 65 to 76. A drone bay mines 50 ore a minute at density 100, so a deposit has to last a few hours of automation. Other systems keep their own deposits. The small cargo container holds 100 items (it was 60), hand mining stops at 75 and depots at 90 percent of the hold. The drone bay is made of 16 iron plates and 16 iron ingots, `core:drone_automation` costs 150 iron plates and 20 carbon and needs `core:logistics` and `core:carbon_drilling`.

## Procedural textures (2026-11-22)
Models used to be flat vertex colours on one shared material. They now carry detail textures, all generated in code (`game/Textures.cs`, fixed seeds, no assets, no network) and served by a cached library (`game/Materials.cs`).

- **Mapping.** (The module and machine plating no longer uses this: see "Framed panels" below.) Every library material is a `StandardMaterial3D` with object-space triplanar mapping, so the meshes need no UVs and the texture density is set in world units by one `tile` number (units per repeat, 1 unit = 1 cell): module plating 4, machine and unit plating 1, structural metal 2, rock 3 (divided by the node's scale, bucketed by 1.25x steps, so a big and a small asteroid show the same texel density), items 0.5 to 0.8.
- **Colour still reads first.** The vertex colour (and the MultiMesh instance colour) multiplies the albedo, so the per-module and per-resource colour coding is unchanged. The albedo textures stay near 0.9 brightness and only add detail.
- **Textures.** At most 512 px, mipmapped, anisotropic filtering, built on first use (all of them together take about 0.5 s). Per set: albedo, a packed map (G roughness, B metallic, both relative: the material multiplies them), a normal map from a height field, and an emission mask for lights and crystals.
- **Library.** `Hull` (panels, groove seams, worn bevels, rivets, hatches, louvres, scratches), `Machine` (the same at machine scale), `Structural` (brushed, bolted: trusses, rails, masts, frames; also ingots and plates), `Burnt` (nozzles, barrels, mine shells), `Glass` (cockpit), `Light` and its vent grill (emission masks), `Rock` (craters, pits, cracks), `Rust` (wrecks, debris), `Ore`, `Ice`, `Crystal` (faceted, emissive veins for unstable crystals), `Belt` (cleats and chevrons).
- **Instancing.** Belts, belt caps and items stay one MultiMesh per tier or shape with instance colours. The belt tile has two surfaces: the body (structural metal) and the running surface (belt texture, flow along local +X). Items use ore, crystal or metal textures by shape. The belt texture creeps along the flow (`Materials.BeltCreep` repeats a second, one uniform updated once per rendered frame for every belt); it does not know whether items are moving, so a blocked belt still looks like it runs.
- **Where.** `Models.Body()` (machines, drones, enemies, the player ship) is machine-scale plating, `Models.HullMat()` module-scale plating, `Models.Struct()`, `Models.Burnt()`, `Models.Glow()` (lit panel), `Models.GlowVent()` and the unchanged `Models.GlowPlain()` (flames, muzzle flashes) and `GlowAlpha` (fields, clouds) are the entry points. Nebula clouds are untouched.
- **Review.** `godot --path game res://Gallery.tscn -- --focus=materials --angle=0 --pitch=38 --frames=30 --shot=out.png` shows every material on a sphere and a box at world size with the game's tints (add `--at=x,y,z --dist=15` to zoom). It prints how long the textures took to build.
- **Cost.** `--stress=1000` (1000 machines, 717 in view with `--stress-close`) on an RTX 4080 SUPER: 5.3 to 5.4 ms average frame before and 5.3 to 5.4 ms after (two runs each, within the noise); `--stress=1000` alone 6.9 before, 6.8 after. Not measured on integrated GPUs: triplanar plus a normal map is about nine texture fetches a pixel.

## Module texture grid, glass and opaque shells (2026-11-22)
Hand test: seen from outside the ship the textures of neighbouring parts did not join, the pilot area looked poor and from inside the structures were see-through.

- **One grid for every module part.** The hull, deck, frame, rails, trusses and detail meshes of the modules (`ModuleModels`, `ModuleDetail`) use `Materials.ModuleHull()` (repeat 4) and `Materials.ModuleStruct()` (repeat 2), planar-mapped instead of triplanar. `Models.Mb` writes UVs and tangents in module-local units when its material is planar (`Materials.IsPlanar`). A module is 8 x 4 x 8 units and sits on multiples of that, and 2 and 4 divide 8, so the panel rows of two neighbouring modules, of the hull and its frame, and of the deck and the walls meet on cell borders. A face whose normal is within about 25 degrees of an axis is projected along that axis; a chamfer or a nose slope is mapped in its own plane (u along the axis it is most perpendicular to, v along the face), so panels keep their size instead of smearing as a triplanar blend does at 45 degrees. Machines, drones and the other props keep the triplanar library.
- **Glass.** `Materials.Glass()` is nearly clear: grey-blue tint at 0.26 alpha, roughness 0.06, a rim sheen and the streak texture as a faint reflection, two-sided, and its instances do not cast shadows. It replaces the saturated blue block.
- **Opaque both ways.** A ship hull (`Loft`) is built with an inner liner: every outer face is doubled by an inner face 0.1 inside it facing inwards (and the end bulkheads too), so a hull is solid seen from inside. Station walls and roofs are closed slabs and already were. The workshop, the gun deck and the drone bay had no shell at all; they have one now (the drone bay's rear face is the launch door, the gun deck keeps a glass roof). The deck of a ship stops where the lower chamfer of the hull begins and a cockpit nose has a tapered floor, so no plate sticks out under the hull.
- **Where the outside shows.** Only glass: the cockpit canopy (roof, upper flanks and nose tip of a ship; a dome on a station), the gun deck roof, viewports in the side walls of crew (`Hull`), `Workshop`, `Cargo` and `Cockpit` modules (a framed pane per exposed side, `Viewports()`), and the open ends of doorways and docking airlocks. Everything else is opaque. A side wall that touches another module has no wall at all (the neighbour continues the room), as before.
- **Cockpit.** The ship nose has a flat belly and a faceted glass canopy over the roof, the upper flanks and the tip, with dark mullions and a spine; the pilot seat is a swivel chair with a reclined back, headrest, armrests, a flight stick, a throttle and pedals; the console has a dark body, a tilted deck with three lit screens (navigation, systems, radar) under printed instruments, a switch row with status lamps and cheek panels; a pitot tube sticks out of the nose. The seated eye is unchanged (`SeatLocator`).
- **Cockpit glazing (round 8).** The side walls of a ship cockpit are one long glazed band (sill 1.2, thin 0.09 frames, a mullion about every two cells) from the windscreen to the rear joint; with a pod in front (the starter ship's mining laser) the front is a flat windscreen over the whole section with two thin mullions; the canopy beams are 0.09 thick. The console is a narrow, low desk (72 percent wide, 78 percent high) and the mining laser's barrel sits at the beam's muzzle height, so neither blocks the view. A gun module on top still gives the cockpit an opaque ceiling (the gun deck's floor); a skylight through it is TBD. The seated eye did not move: its position is also the pivot of the ship's attitude (`AttitudeRules.PivotX/Y/Z`), which belongs to the flight code.
- **Check (round 8).** `Gallery.tscn --focus=ship-starter|ship-row|ship-big` build ships from the real masks: the starter ship with its pods, two rows of every body kind end to end (with pods hanging on them) and a big demo ship; `--walk-shot=path --walk-bevel` walks every kind (the reactor and cockpit from the aisle).
- **Check.** `Gallery.tscn --focus=ship-demo|station-demo|modules`, `--structure-demo --demo-big [--demo-yaw --demo-pitch --demo-dist --demo-focus=ship]` for the outside, `--walk-shot`, `--flight-shot`, `--dock-shot` and `--structures-shot` for the inside.

## The ship workbench model (2026-11-23)
The starter ship's workbench (the micro-refinery that `F` drives, [ADR 0031](../adr/0031-construction-rules-fabrication-bays-and-floor-building.md)) had no body in the world. It is not a machine of the simulation (no cell, no `MachineInfo`: the recipe and the craft live in `PlayerInfo`), so the client draws it.

- **Model.** `Models.Workbench` (`game/WorkbenchModel.cs`) builds a compact bench in the local cells of a module: a plated cabinet with drawers and a yellow base band, a worktop, a control post with three recipe lenses (ingot amber, plate cyan, ammo red) and a status lamp on a pole (green working, amber waiting, red without power), a hopper over a small furnace, a stamping press, and a pegboard with tools on the wall. The casing uses `Materials.ModuleHull()` and the post, furnace and press `Materials.ModuleStruct()`, built in module coordinates so the plating lines fall on the module grid; the lamps use `Materials.Light`.
- **Where.** `WorkbenchSpot.Pick` (`src/Starfoundry.Ui`) chooses the module: the first hull module (lowest deck, then nearest the cockpit), else a cargo module (moved half a cell off the wall to clear the shelves); never a fabrication bay, whose floor is for building. It stands against the left wall at the cockpit end, or the right wall when the left has a doorway. `Play.Workbench.cs` hangs it under the ship's structure root and rebuilds it when the structure changes.
- **Animation.** `Models.AnimateBench` runs only while the bench crafts: the lens of the active recipe pulses, the furnace mouth glows while it smelts, the press head drops (faster for ammo), the status lamp is green. Set but waiting for materials: lens dim, lamp amber. Off: no lens, lamp amber, nothing moves.
- **Card.** Aiming at the bench with no tool in hand (within 9 cells) shows its name, recipe, state and progress bar (in the recipe colour), input with what the hold has, output and "F: change recipe" (es and en). `F` itself works from anywhere, as before.
- **Check.** `Gallery.tscn --focus=workbench [--bench=ingot|plate|ammo|off] [--state=work|idle] [--wall=right] [--module=Hull|Cargo|Workshop|Cockpit] --angle=75 --pitch=20 --dist=4 --fov=60 --at=1.7,0.9,1.5 --shot=out.png`, and `--walk-shot=path [--ui-lang=en]`, which now also saves `path.bench.png` (plates, with the card), `path.bench.ingot.png` and `path.bench.off.png`. Unit tests: `WorkbenchSpotTests`.
- **Limits.** The bench is not an obstacle (the body walks through it, like machines); the pegboard overlaps the shelves of a cargo module (it hangs behind them); the model is not drawn on stations.

## Framed panels: one pattern per face (2026-11-24)
Hand test: the panel textures did not frame. Panel lines started mid-panel at the edge of a wall or a roof, the cut positions were random, neighbouring faces and parts had different panel sizes and rotations, and the frame, rails and hull looked like separate layers pasted over each other. The cause was the mapping: planar UVs in absolute mesh units with a four-unit tile of uneven panels, so a face border fell wherever the tile happened to be.

- **A face gets its own frame** (`game/FaceUv.cs`). `Models.Mb` hands its triangle soup to `FaceUv.Compute` when the material is planar (`Materials.IsPlanar`). Triangles that share an edge and lie in one plane are one face (a wall with a window hole is one face, so is a deck with a hatch). u and v run along the face: world axes for a wall, floor or roof, the longest border edge and its perpendicular for a chamfer, a nose slope or a taper. The longer side is u. The origin is a corner of the face.
- **Whole panels.** The face is measured in panels (`Materials.PlanarTile`: 1 unit for module plating and beams, 0.3 for machines) and the UVs are scaled so a whole number of panels fits each side. The seam of the texture therefore lies exactly on every face border and a chamfer carries as many panels along its length as the wall beside it. The panel size varies by at most about a third between faces (an 8 x 4 wall has 8 x 4 panels, a 3-unit wall 3, a 2.6-unit wall 3); two neighbours always differ by less than that.
- **Slivers keep the world scale.** A face narrower than half a panel (a beam, a rail, the edge of a slab, a drum facet) is not stretched; it keeps the world scale and shows the middle of the texture.
- **The texture is one framed panel** (`Gen.FramedPanel`, `Gen.BeamSegment`). The tile border is a groove, then a bevelled frame band with rivets at the corners and every quarter, gussets in the four corners, a thin step and a sunken field with grain and a few scratches. Everything is symmetric about the tile border, so tiles join without a visible seam; two modules that touch show a double groove, which reads as a frame between them. Beams and rails use a segment texture (brushed along u, bevelled long edges, a joint groove and bolts), so a beam is a row of whole segments that end where it ends.
- **UVs count panels**, so a planar material has a UV scale of 1 and the mesh carries all the information. The tangent is the u axis, so the normal map follows the face.
- **Where.** Every module part (hull, deck, walls, frame, rails, trusses, details), the hardpoint housings, emitters, gantry and cradle (`Models.HullMat`, `Models.Struct` now use the module materials), and machines, drones, enemies and the player ship (`Models.Body`, 0.3-unit panels). Rocks, ore, ice, crystal and rust stay triplanar (they have no faces to frame), belts keep their cleat texture and the ingots and plates on them stay triplanar.
- **Colour coding is unchanged**: the vertex colour (and the MultiMesh instance colour) still multiplies the albedo, and instanced belts and items are untouched.
- **Cost.** The same texture sizes as before (a 512 px panel, 256 px beam, half of that with `--low-textures`), mipmapped. The UVs are computed once per cached mesh.
- **Geometry changes keep working.** `FaceUv` knows nothing about the models: a new face or a changed height is framed on the next build. Faces made of triangles that do not share an edge (a T-junction between two quads) are framed separately, each to its own borders.
- **Check.** `Gallery.tscn --focus=framing --angle=25 --pitch=38 --dist=42 --frames=40 --shot=out.png` shows a cube, a chamfered prism, a tapered block, a twelve-sided drum with a dome and a beam frame, then a three-module ship strip and a three-module station strip behind them. Every face should carry whole panels with a seam on each border; neighbouring modules should show a double groove where they meet. Also `--focus=ship-demo`, `--focus=station-demo` and `--walk-shot` for the inside.
- **Limits.** A face that is not a rectangle (a tapered nose, a triangle) has one aligned border; the slanted sides cut the panels. The colour stripes and accent plates sit on the hull as their own faces and carry their own panel grid. A faceted drum shows one narrow panel per facet.

## Materials and what they are for: audit and the optional chains (2026-11-24)
Status: built. Decision: [ADR 0034](../adr/0034-optional-material-chains-and-read-only-goal-types.md). Content: `content/core.json`. Tests: `MaterialsExpansionTests`, `StructureEconomyTests`, `IconCatalogTests`.

### Audit before the change
Seventeen items, three of them only ever spent as research or module costs and two that nothing but a few modules ever used. The late economy was a straight line (ore, ingot, plate, then research), and weapons had no reason to look at the rest of the tree.

| Item | Source | Uses before the change | Verdict |
| --- | --- | --- | --- |
| Iron ore, ingot, plate | home deposit, smelter, assembler | everything | the backbone, unchanged |
| Carbon | hand mining | jump cost, engine, reactor, mining laser, advanced logistics, alloy | fine |
| Ice, hydrogen, fuel cell | hand mining, cracker | reactor fuel | fine; fuel cells are the propellant variant |
| Titanium ore, ingot, plate | hand mining, smelter, assembler | drone bay, alloy, research | fine |
| Nickel ore, ingot, plate | hand mining, smelter, assembler | circuits, fuel cells, research | fine |
| Quartz | hand mining | silicon, research | fine, and now also the lens |
| Silicon, circuit, titanium alloy | smelter, assembler | shield, sensor, Mk2 reactor, Mk2 hardpoints | fine |
| Ammo | assembler | turrets | fine |
| Science items, biomass | bench, lab, greenhouse | research | fine |
| Wreck loot | derelict wrecks (one item per wreck: plates, ingots, carbon, ammo, ice) | nothing special: the same items the player makes anyway | weak: no reason to fly to a wreck |

Gaps: no hazardous source gave anything the factory could not make, the Mk2 hardpoints cost only circuits, alloy and plates (an upgrade that needed nothing new), and carbon had no second product.

### New items (appended after biomass, so no index moves)
| Item | Made by | Needs | Used for |
| --- | --- | --- | --- |
| Carbon fibre | assembler, `make_carbon_fiber`, 6 s: 3 carbon and 1 nickel ingot give 2 | research Composites | composite plate |
| Composite plate | assembler, `make_composite_plate`, 6 s: 2 carbon fibre and 1 titanium plate | research Composites | Mk2 gun |
| Coolant | hydrogen cracker, `make_coolant`, 8 s: 3 ice and 1 silicon give 2 | research Cryogenics | Mk2 laser (2) and Mk2 gun (3) |
| Resonant crystal | **only** derelict wrecks (two of the seven loot entries, 2 to 6 crystals) | a crystal port (`core:crystal_unloader`, free in structure worlds) | focus lens |
| Focus lens | assembler, `make_focus_lens`, 8 s: 1 resonant crystal and 2 quartz | research Resonance | Mk2 laser (1) |

Module costs (campaign): Mk2 mining laser 8 circuits, 8 titanium alloy, 1 focus lens, 2 coolant (it used to take 12 iron plates as well; a module is limited to four stacks); Mk2 gun 8 circuits, 8 titanium alloy, 1 composite plate, 3 coolant. Everything else is unchanged: no early module, machine or research node needs a new item.

### New research (basic field, so the science bot and the whole-tree tests are unaffected)
| Node | Needs | Cost | Gives |
| --- | --- | --- | --- |
| Composites | advanced plating, carbon processing | 200 iron plates, 60 carbon, 20 nickel plates | `make_carbon_fiber`, `make_composite_plate`; ship hull +20 percent |
| Cryogenics | electronics | 250 iron plates, 30 silicon, 20 titanium plates | `make_coolant` |
| Resonance | sensors | 250 iron plates, 30 silicon, 40 quartz | `make_focus_lens`, the crystal port; mining rate +20 percent |

```
carbon + nickel ingot --assembler--> carbon fibre --+
                                                    +--assembler--> composite plate --> Mk2 gun
titanium plate -------------------------------------+
ice + silicon --cracker--> coolant -----------------------------------> Mk2 laser, Mk2 gun
wreck (salvage) -> resonant crystal --crystal port--+
quartz -------------------------------------------+--assembler--> focus lens --> Mk2 laser
```

### Rules the content tests keep (`MaterialsExpansionTests`)
- Every item is found (deposit or wreck) or made, and has a use (a recipe input, a research cost, a machine, module or belt cost, the jump, ammo or reactor fuel).
- Every recipe of a placeable machine has inputs that can reach a belt (a raw item with a port, or the output of such a recipe). The resonant crystal counts as raw because wrecks give it and the crystal port carries it.
- Every new recipe is gated by exactly one research node and the research graph of the shipped content is completable.
- No research node, machine, early module or hardpoint Mk1 costs a new item; only the two Mk2 hardpoints do.
- Every item, machine, recipe and node has Spanish and English text; every item and machine has an icon.
- A resonant-crystal wreck exists within two jumps of home on seeds 20261121, 20261120 and 777.

### Sources and hazards
The task asked for a rare ore from unstable crystals and mine fields as well. Those obstacles explode and carry no loot in the simulation, so giving them salvage needs a new obstacle rule (a sim change with a version gate). This round keeps to content: the crystal comes from wrecks, which already exist, are placed away from mines by the generator and are worth a short detour. A "mine field salvage" rule is a candidate for a later ADR.

### Models and icons
Items use the existing shapes with new tints (carbon fibre and composite: ingot and plate in slate, coolant: an orb in cyan, crystal and lens: crystals in violet); the crystal port reuses the unloader model with a violet heap. Icons: `item:core:carbon_fiber` (ingot), `composite_plate` (plate), `coolant` (drop), `resonant_crystal` (crystal), `focus_lens` (gem), `machine:core:crystal_unloader` (port) and the three research nodes, all registered in `IconCatalog` with Spanish and English names. A bespoke model for each is not built yet.

### Pacing
None of this is on the path to the win: the scripted player never researches the three nodes or builds a Mk2 module. Bot times are in [difficulty.md](difficulty.md#materials-and-missions-round-2026-11-24).
