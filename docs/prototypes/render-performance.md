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
- The animated machines (working drills, spinning labs) change every frame and still write materials every frame; the stress base has most of its machines idle, so a base with everything running costs more than these numbers.
