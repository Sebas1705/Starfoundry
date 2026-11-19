# 0002. Engine-independent deterministic simulation core

- Status: accepted
- Date: 2026-11-19
- Area: simulation

## Context
- Factory games live or die on performance at scale, and reviewers of the reference game report slowdowns after expanding.
- Co-op needs consistent state across machines; sending full state is expensive.
- Automated tests of logistics (belts, power, routes) are far cheaper outside an engine.

## Decision
The factory, logistics, power, heat, route and threat-resolution logic lives in a separate library with no engine references. It uses a fixed timestep (30 ticks per second), stable integer entity IDs, a seeded random generator, and integer or fixed-point arithmetic for any value that affects state. The engine layer sends commands and renders snapshots.

## Consequences
- Benefits: headless tests and benchmarks, replays, checksum-based desync detection, command-based co-op, easy profiling.
- Costs: a boundary to maintain (commands, snapshots), no floating-point shortcuts in state, rendering must interpolate between ticks.
- Checked by: golden-state tests (same commands give the same checksum across runs and machines) in CI.
- Evidence (2026-11-19): the belt prototype ([docs/prototypes/belts.md](../prototypes/belts.md)) runs the whole simulation in a library with no Godot references, 18 tests (including determinism and conservation on every tick) and a headless benchmark at about 0.06 ms per tick for 10,000 items. Accepted by the owner on the same day.

## Alternatives considered
- **Simulation inside engine nodes:** fastest to prototype, but untestable headless and non-deterministic; rejected for the scale target.
- **Floating-point deterministic with care:** fragile across CPUs; rejected for gameplay state, still allowed for visuals.
