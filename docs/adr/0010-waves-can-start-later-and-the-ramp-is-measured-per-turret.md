# 0010. Waves can start later, and the wave ramp is measured against the number of fed turrets

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, tooling

## Context
- After the [first balance pass](0009-pacing-targets-measured-by-a-scripted-player-and-a-per-item-depot-cap.md) the first wave is safe, but one fed turret and the ship weapon held every wave for the first hour and still lost nothing at wave 7: the threat did not ask the player to grow a defence.
- Wave size was `base + per_wave * waveNumber` per enemy type, so a new enemy type could only exist from wave 0, and the global cap (24) truncated the last wave lines first once reached.
- More turrets only help if their ammo keeps up: one drill and smelter make about 2 shots a second, the same as one turret firing all the time.

## Decision
- A wave line may carry `from_wave` (default 0): from that wave on it holds `base + per_wave * (wave - from_wave)` enemies, before it none. The value is part of the content hash. The wave formula is unchanged for every line without it.
- A third enemy, the brute (300 hit points, slow, 30 damage), arrives in steps: one from wave 4, a second from wave 8, a third from wave 12 (`from_wave` 3, 7, 11, count constant). Steps, not growth, keep the early waves readable and make the later ones a visible jump.
- Turret damage 8 to 12 and the enemy cap 24 to 60, so the brute lines are not cut off when waves get large. (Raising the turret ammo buffer to 60 made no difference to the results and was kept at 60 as cheap depth.)
- The ramp target is a property of defence size, measured by the scripted player of ADR 0009 with `n` fed turrets, each with its own ammo line:
  - one fed turret holds at least the first seven waves and is overrun between the eighth and the fourteenth;
  - three fed turrets hold at least two waves longer than one, on every seed tried.
- `BalanceTests` pin both targets.

## Consequences
- Benefits: waves now ask for a bigger defence during the first one to two hours; a content author can introduce an enemy at any wave; the cap no longer hides the heavy enemies.
- Costs: the scripted player keeps its ship inside the turrets' reach and never repairs, so a human who plays the ship well will hold longer and one who ignores ammo will hold less; the targets are bands, not a promise.
- Results are noisy per seed (the wave direction comes from the seed): two turrets were no better than one on the default seed and four held the longest. The targets compare one against three and use ranges for that reason.
- Not decided here: targeting rules, difficulty settings.

## Alternatives considered
- **Per-wave hit point growth:** smooth, but it would also have changed the early waves and needs another field on every enemy.
- **Constantly growing brute counts:** the first try. Each wave then added 300 hit points, far more than a turret adds, so extra turrets bought almost nothing.
- **Tuning by turret damage alone:** the ratio to enemy hit points is what matters and it moves the whole curve, not its slope.
