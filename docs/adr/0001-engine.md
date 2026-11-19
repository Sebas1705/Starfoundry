# 0001. Use Godot 4 with C# for the game layer

- Status: proposed (needs owner approval)
- Date: 2026-11-19
- Area: engine

## Context
- Single developer, part-time, needs fast iteration and low licence risk.
- Needs 3D first-person, instancing for thousands of belt items, a custom editor for build tools, Steam integration, Windows and Linux builds.
- Owner already works in Kotlin, Go, TypeScript; C# is close to Kotlin in style and has strong tooling.
- The simulation core is engine-independent (ADR 0002), which lowers engine lock-in.

## Decision
Use Godot 4 (stable) with C# for the engine layer and a plain .NET class library for the simulation core.

## Consequences
- Benefits: MIT licence, no royalties or runtime fees, small editor, fast builds, scene tree suited to build tools, Steam via GodotSteam.
- Costs: smaller 3D feature set and ecosystem than Unity or Unreal; fewer ready-made assets; C# export to some platforms is less mature.
- Checked by: a one-week prototype of 10,000 belt items rendered via MultiMesh at 60 FPS before the decision becomes accepted.

## Alternatives considered
- **Unity:** mature, large asset store, but licence and fee history is a risk; heavier editor.
- **Unreal 5:** best visuals and networking, heavy for a solo developer and a factory game with many small entities.
- **Custom engine (Rust or C++):** maximum control, unaffordable scope.
- **Bevy:** attractive data-oriented ECS, but the editor and UI tooling are still immature for this scope.
