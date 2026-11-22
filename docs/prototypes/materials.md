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

- **Mapping.** Every library material is a `StandardMaterial3D` with object-space triplanar mapping, so the meshes need no UVs and the texture density is set in world units by one `tile` number (units per repeat, 1 unit = 1 cell): module plating 4, machine and unit plating 1, structural metal 2, rock 3 (divided by the node's scale, bucketed by 1.25x steps, so a big and a small asteroid show the same texel density), items 0.5 to 0.8.
- **Colour still reads first.** The vertex colour (and the MultiMesh instance colour) multiplies the albedo, so the per-module and per-resource colour coding is unchanged. The albedo textures stay near 0.9 brightness and only add detail.
- **Textures.** At most 512 px, mipmapped, anisotropic filtering, built on first use (all of them together take about 0.5 s). Per set: albedo, a packed map (G roughness, B metallic, both relative: the material multiplies them), a normal map from a height field, and an emission mask for lights and crystals.
- **Library.** `Hull` (panels, groove seams, worn bevels, rivets, hatches, louvres, scratches), `Machine` (the same at machine scale), `Structural` (brushed, bolted: trusses, rails, masts, frames; also ingots and plates), `Burnt` (nozzles, barrels, mine shells), `Glass` (cockpit), `Light` and its vent grill (emission masks), `Rock` (craters, pits, cracks), `Rust` (wrecks, debris), `Ore`, `Ice`, `Crystal` (faceted, emissive veins for unstable crystals), `Belt` (cleats and chevrons).
- **Instancing.** Belts, belt caps and items stay one MultiMesh per tier or shape with instance colours. The belt tile has two surfaces: the body (structural metal) and the running surface (belt texture, flow along local +X). Items use ore, crystal or metal textures by shape. The belt texture creeps along the flow (`Materials.BeltCreep` repeats a second, one uniform updated once per rendered frame for every belt); it does not know whether items are moving, so a blocked belt still looks like it runs.
- **Where.** `Models.Body()` (machines, drones, enemies, the player ship) is machine-scale plating, `Models.HullMat()` module-scale plating, `Models.Struct()`, `Models.Burnt()`, `Models.Glow()` (lit panel), `Models.GlowVent()` and the unchanged `Models.GlowPlain()` (flames, muzzle flashes) and `GlowAlpha` (fields, clouds) are the entry points. Nebula clouds are untouched.
- **Review.** `godot --path game res://Gallery.tscn -- --focus=materials --angle=0 --pitch=38 --frames=30 --shot=out.png` shows every material on a sphere and a box at world size with the game's tints (add `--at=x,y,z --dist=15` to zoom). It prints how long the textures took to build.
- **Cost.** `--stress=1000` (1000 machines, 717 in view with `--stress-close`) on an RTX 4080 SUPER: 5.3 to 5.4 ms average frame before and 5.3 to 5.4 ms after (two runs each, within the noise); `--stress=1000` alone 6.9 before, 6.8 after. Not measured on integrated GPUs: triplanar plus a normal map is about nine texture fetches a pixel.
