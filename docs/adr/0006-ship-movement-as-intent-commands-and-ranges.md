# 0006. Ship movement as intent commands, with mining and build ranges

- Status: proposed
- Date: 2026-11-20
- Area: simulation, gameplay

## Context
- Until now the player was an inventory without a place: mining and building worked from anywhere ([ADR 0005](0005-campaign-economy-hold-costs-and-research-gates.md)).
- Movement happens every frame but commands are logged and sent over the network ([ADR 0003](0003-commands-as-the-only-way-to-change-the-world.md)); one command per frame per player would flood logs and co-op traffic.
- The simulation must stay deterministic and integer-only ([ADR 0002](0002-deterministic-simulation-core.md)).

## Decision
- Each player has a ship with a position in integer **units** (240 per cell, the same unit as belt positions), starting at the origin.
- Movement is an **intent**: `SetMovement(dx, dy, dz)` with each axis -1, 0 or 1 in world axes. The ship then moves every tick until a new intent arrives, so a held key is one command on press and one on release. Speed is 48 units per tick (6 cells per second) per axis, reduced to 34 and 28 when combining two or three axes so the path speed stays about the same, using integers only. The ship cannot leave the building cube.
- **Mining range**: hand mining needs the ship within 6 cells of the deposit's centre, in every mode. Starting out of range is refused; leaving range pauses mining without cancelling it.
- **Build range** (campaign only): placing and removing belts and machines needs the ship within 24 cells of the target (both ends of a belt). Sandbox has no range limit. Auto-links and other commands that only connect existing things are not range-limited.
- Deposits have a position in cells. Ranges are compared as exact integer squared distances.
- The ship does not collide with structures and has no fuel or inertia yet.

## Consequences
- Benefits: a place for the player in the world without per-frame commands; the same movement works for several players; ranges give the factory a footprint to defend and a reason to fly; replays and checksums cover position.
- Costs: the intent model gives world-axis movement only (no analogue speed or smooth turning); the renderer extrapolates between ticks; range rules add rejection reasons the UI must explain.
- Not decided here: inertia and fuel, collisions, travel between systems, a separate ship for hauling, how deposits are placed in a generated world.

## Alternatives considered
- **Send the position every frame:** simplest, but floods logs and the network and ties the log to frame rate.
- **Click-to-move targets:** compact, but needs path rules in the simulation and does not feel like flying.
- **Floating-point positions rendered from the engine:** easy, but not deterministic and not replayable.
