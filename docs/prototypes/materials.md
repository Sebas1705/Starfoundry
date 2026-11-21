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

Spare ore can be thrown away with `Ctrl+Delete` (`DumpItems`): the largest stack of carbon beyond the jump cost, quartz, titanium ore, nickel ore, or ice while hydrogen is at least 20.
