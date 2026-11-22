# Prototype: world rules and difficulty presets

Status: built (2026-11-22). Decision: [ADR 0023](../adr/0023-world-rules-and-difficulty.md). Design: [modes, scenarios and resources](../superpowers/specs/2026-11-22-modes-and-scenarios-design.md), part A. Code: `src/Starfoundry.Sim/WorldRules.cs`. Tests: `WorldRulesTests`, `GameOutcomeTests`.

## The question
Can difficulty be a small bundle of integer numbers stored with the game, and do four named bundles give a playable range from relaxed to brutal?

## The rules
| Rule | Default | Range | Effect |
| --- | --- | --- | --- |
| `EnemyDamagePercent` | 100 | 25..300 | Damage of every enemy shot (round half up, at least 1) |
| `EnemyHpPercent` | 100 | 25..300 | Hit points of every enemy at spawn |
| `WaveSizePercent` | 100 | 25..300 | Enemies per wave-table entry (round half up, at least 1 of a type that has any) |
| `WaveIntervalPercent` | 100 | 25..300 | Time between waves: capacity of the alert meter and its after-wave value (higher is slower) |
| `FirstWaveGraceSeconds` | -1 | 0..3600 | Grace before the first wave of a system; -1 keeps the content value (480 s) |
| `DepositRichnessPercent` | 100 | 25..400 | Size of every generated deposit |
| `PowerOutputPercent` | 100 | 25..300 | Output of a burning reactor (the emergency trickle is not scaled) |
| `StartingItems` | none | up to 64 stacks | Added to the starter hold as far as it has room |
| `ThreatsEnabled` | true | | False: no waves (ANDed with the world's `threats` bool) |
| `FreeBuild` | false | | True: placing and repairing costs nothing, removing refunds nothing |
| `GoalWaves` | 10 | 1..30 | Waves to survive for the win (read by `GameOutcome`) |

## The presets
| Preset | Enemy damage | Enemy hp | Wave size | Wave interval | First wave grace | Richness | Power | Goal waves |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Relaxed | 70 | 80 | 70 | 130 | 600 s | 150 | 110 | 8 |
| Normal | 100 | 100 | 100 | 100 | content (480 s) | 100 | 100 | 10 |
| Hard | 130 | 130 | 130 | 80 | 300 s | 80 | 100 | 12 |
| Brutal | 170 | 170 | 170 | 65 | 120 s | 60 | 90 | 15 |

Each preset also has `ThreatsEnabled` true, `FreeBuild` false and no starting items.

## How to run it
```
dotnet run --project tools/Starfoundry.Balance -- --structures --difficulty=hard --seed 20261121 --minutes 150
```
`--difficulty=relaxed|normal|hard|brutal` plays the structure bot with that preset (default `normal`). The bot's goal is the preset's `GoalWaves`.

## Measured
The structure bot ([structure-bot.md](structure-bot.md)), action delay 6 s, structure world with threats, each preset on three seeds. "Win" is the minute the goal was reached; a loss shows the minute the ship fell and the waves cleared before.

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 |
| --- | --- | --- | --- |
| Relaxed (8) | Win 41:11 | Never won (see note) | Win 44:25 |
| Normal (10) | Win 42:57 | Win 42:02 | Win 41:58 |
| Hard (12) | Win 44:42 | Win 49:42 | Win 47:49 |
| Brutal (15) | Lost 43:17 (11 waves) | Lost 42:39 (11 waves) | Lost 42:29 (11 waves) |

First wave warning on the main seed: Relaxed 16:56, Normal 15:39, Hard 14:39, Brutal 13:49.

What it says:
- Relaxed and Normal win on the main seed; Hard wins on all three seeds, a few minutes later than Normal because it needs two more waves; Brutal is lost on every seed around minute 42 to 43 after 11 waves, so with the bot's current defence it is out of reach (a human with better layouts may do better; this is not tuned).
- Hard being almost as winnable as Normal means the bot's defence has slack at 130 percent; a harder Hard is possible without touching Brutal, but that is a balance decision left open.
- Relaxed on seed 20261120: the slower waves move the bot's exploration trip (it jumps away from home at about minute 38 once waves 1 to 7 are cleared) into the stretch before wave 8, and the bot runs out of carbon in the third system and cannot return, so the station falls later. That is a limitation of the scripted player (the expedition runs before the goal is reached), not of the rules; the bot still cleared 23 waves.

## Measured again after the harder waves (2026-11-22)
Same bot, delay and seeds, after the bulwark and the larger waves 3 to 10 ([defence.md](defence.md), [ADR 0026](../adr/0026-science-fields-and-research-expansion.md)). The bot needed no change.

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 |
| --- | --- | --- | --- |
| Relaxed (8) | Win 43:14 | Win 41:30 | Win 41:48 |
| Normal (10) | Win 45:01 (was 42:57) | Win 43:50 (was 42:02) | Win 47:59 (was 41:58) |
| Hard (12) | Win 46:26 (was 44:42) | Win 50:47 (was 49:42) | Win 48:58 (was 47:49) |
| Brutal (15) | Lost 41:49 (10 waves) | Lost 42:26 (10 waves) | Lost 44:27 (11 waves) |

- Normal wins on every seed, 1 to 6 minutes later than in the table above. Hard still wins on all three seeds, 1 to 2 minutes later. Brutal is still lost.
- Relaxed on seed 20261120 now wins (the note above had it stuck on an expedition); the waves of that run fall at different times now. A side effect of the new table, not a tuned result.
- Story levels: 01 (1:48), 02 (5:52) and 03 (17:53, 3 stars, five waves) are won by the bot as before; level 03 needed no change to its own wave modifiers. Level 05 (Hard, 12 waves) is not played by the bot; Hard wins above.

## Not measured
Custom rule combinations, `FreeBuild` and `StartingItems` with the bot, and play by a person. The New Game screen that picks a preset is part D of the design.
