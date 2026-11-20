# Render performance with a big factory

Status: done (2026-11-20). Answers a limitation noted since the 3D models went in: machines, ships and enemies are individual nodes with their own animation, and nobody had measured what a large base costs.

## The tool
`--stress=N` builds a sandbox base of N machines of every kind (a grid, two cells apart) and 32 belts with items moving, puts the camera far enough back to see it all, switches vertical sync off so the frame time is the work and not the refresh rate, skips 60 frames while the scene builds, then times 300 frames and prints the average, 95th percentile and worst frame time. Run it from the project folder:

```powershell
godot --path game -- --stress=1000
```

## What it found
The first measurement was linear in the number of machines, about 13 microseconds a machine per frame, with nothing else on screen:

| Machines | Frame time before | Frames per second before |
| --- | --- | --- |
| 50 | 1.1 ms | 892 |
| 200 | 2.4 ms | 411 |
| 500 | 6.4 ms | 156 |
| 1,000 | 12.9 ms | 78 |

The cost was not drawing. Every frame, for every machine, the model code rewrote the material properties of every part (damage tint: five properties per part; glow strength and engine scale for every light) even when nothing had changed, which makes many calls into the engine, and it gave each machine its own copy of its materials the first time it did so. Most machines are undamaged and idle most of the time.

## The fix
- `SetDamage` does nothing when the damage has not changed (by more than one percent) since the materials were last set, so an undamaged machine keeps the shared materials and never copies them.
- `Animate` remembers the last glow factor of each light and writes nothing when it has not changed visibly (idle machines hold a constant glow); the name test for engine lights is made once, not every frame.

## After

| Machines | Frame time | Frames per second |
| --- | --- | --- |
| 200 | 1.1 ms | 911 |
| 500 | 2.3 ms | 433 |
| 1,000 | 3.7 ms | 273 |
| 2,000 | 7.1 ms | 142 |
| 5,000 | 20.2 ms | 50 |

A factory of a thousand machines is far above anything the scripted players build (about 30 in the biggest runs) and runs at 273 frames per second; at 5,000 the cost is drawing the models (each is a few meshes), and a level-of-detail or instancing scheme would be the next step if bases ever get that large.

The look did not change: the gallery render of the clean and damaged models is the same, the three demo bots give the same results, and the damage tint and the lights still follow the simulation.

## Limits
- Measured on one machine (a desktop with an NVIDIA GeForce RTX 4080 SUPER); a weaker graphics card will be draw-bound earlier.
- Only the renderer is measured here: the simulation's cost per tick is covered by the benchmark in `tools/Starfoundry.Bench`.
- The first table is a mostly idle base; the section below measures one with every machine working.

## Machines at work, and a camera that sees only part of the base
Two options of `--stress` answer the limits above: `--stress-active` marks every machine as working (the worst case for animation: spinning parts and pulsing lights change every frame) and `--stress-close` puts the camera at the distance a player really has, seeing a part of a big base.

All machines working cost a quarter to a half more than the idle base (1,000 machines 4.5 ms against 3.7 ms, 2,000 machines 10.3 ms against 7.1 ms), and a close camera saw about half of a 2,000-machine base while the rest was still being animated for nobody.

So machines outside the camera's view are no longer animated, damaged-tinted or aimed: the frustum planes are fetched once a frame and the machines are tested in managed code (a first version that asked the engine about each machine cost as much as it saved). The margin is a cell and a half, so a machine about to enter the view is already moving, and a machine that was out of view while it changed is brought up to date the frame it appears.

| Machines, all working | Close camera before | Close camera now | Whole base in view now |
| --- | --- | --- | --- |
| 1,000 | 3.9 ms | 3.5 ms | 4.5 ms |
| 2,000 | 7.6 ms (132 fps) | 5.8 ms (172 fps; 1,145 in view) | 10.4 ms (96 fps; all in view) |
| 5,000 | 23.8 ms (42 fps) | 9.3 ms (107 fps) | not measured |

With the whole base in view there is nothing to cull and the cost is unchanged. A base of a few thousand machines seen from close up now stays above 100 frames per second on this machine.
