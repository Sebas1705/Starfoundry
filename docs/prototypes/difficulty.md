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
| `ResourceDensityPercent` | 140 | 50..300 | How many deposits the generator makes: above 100, extra deposits are appended after the base ones (ADR 0036); 100 is the old galaxy. Ignored with the legacy flag |
| `StartingItems` | none | up to 64 stacks | Added to the starter hold as far as it has room |
| `ThreatsEnabled` | true | | False: no waves (ANDed with the world's `threats` bool) |
| `FreeBuild` | false | | True: placing and repairing costs nothing, removing refunds nothing |
| `GoalWaves` | 10 | 1..30 | Waves to survive for the win (read by `GameOutcome`) |

## The presets
| Preset | Enemy damage | Enemy hp | Wave size | Wave interval | First wave grace | Richness | Power | Goal waves | Density |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Relaxed | 70 | 80 | 70 | 130 | 600 s | 150 | 110 | 8 | 150 |
| Normal | 100 | 100 | 100 | 100 | content (480 s) | 100 | 100 | 10 | 140 |
| Hard | 120 | 120 | 120 | 80 | 300 s | 80 | 100 | 13 | 120 |
| Brutal | 170 | 170 | 170 | 65 | 120 s | 60 | 90 | 15 | 100 |

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

## Measured after docking, world generation and free flight (2026-11-22)
Same bot, delay (6 s) and seeds, on the build with docking ([docking](docking.md)), generated worlds and obstacles, and free flight. The bot needed no change and content was not touched.

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 |
| --- | --- | --- | --- |
| Relaxed (8) | Win 46:24 (was 43:14) | Win 42:39 (was 41:30) | Win 41:37 (was 41:48) |
| Normal (10) | Win 47:00 (was 45:01) | Win 45:07 (was 43:50) | Win 44:12 (was 47:59) |
| Hard (12) | Win 48:13 (was 46:26) | Win 47:56 (was 50:47) | Win 51:16 (was 48:58) |
| Brutal (15) | Lost 36:28 (7 waves; was 41:49, 10) | Lost 41:56 (10 waves; was 42:26, 10) | Lost 43:08 (10 waves; was 44:27, 11) |

- Relaxed, Normal and Hard win on every seed, within about 3 minutes either way of the previous table (Normal 44:12 to 47:00, a smaller spread than before). Nothing drifted far enough to investigate; first wave warnings are 16:21 (Relaxed), 15:13 (Normal), 14:26 (Hard) and 13:36 (Brutal) on the other seeds, 16:28, 15:20, 14:33 and 13:44 on the main one.
- Brutal is still lost on every seed. On the main seed it now falls earlier (wave 7, minute 36:28, 19 machines and 5 modules lost); the other two are within a minute and a wave of before. One seed is not a trend, but if Brutal is ever tuned, start from that run.
- Hard lost two modules on seed 20261120 and none elsewhere; Relaxed and Normal lost none.

## Not measured
Custom rule combinations, `FreeBuild` and `StartingItems` with the bot, and play by a person. The New Game screen that picks a preset is part D of the design.

## Rebalance after the construction rework (2026-11-23)
After ADR 0031 and ADR 0032 the bot won Normal at 66:08, Relaxed at 59:46 and Hard at 64:44 (seed 20261121). Iron smelting was changed from 2 s to 1 s, nothing else in the content or the presets ([structure-bot](structure-bot.md#rebalance-after-the-construction-rework-2026-11-23) has the analysis and the variants that were tried), and the bot got the changes listed there (first jump before the expedition, lab feeds, station labs, science wings). Same bot, delay 6 s.

Before (content as after ADR 0032, the bot of that time):

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 |
| --- | --- | --- | --- |
| Relaxed (8) | Win 59:46 | Win 55:09 | Win 54:17 |
| Normal (10) | Win 66:08 | Win 57:54 | Win 56:34 |
| Hard (12) | Win 64:44 | Win 61:31 | Lost 58:09 (ship lost) |
| Brutal (15) | Lost 46:53 (3 waves) | Lost 49:17 (8 waves) | Lost 48:31 (8 waves) |

After:

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 |
| --- | --- | --- | --- |
| Relaxed (8) | Win 46:18 | Win 46:57 | Win 46:28 |
| Normal (10) | Win 48:45 | Win 48:59 | Win 48:53 |
| Hard (12) | Win 50:40 | Win 51:36 | Win 50:36 |
| Brutal (15) | Lost 47:45 (10 waves) | Lost 48:11 (10 waves) | Lost 42:46 (9 waves) |

First wave warning on the main seed: Relaxed 20:12, Normal 18:59, Hard 17:50, Brutal 16:56. Jump drive researched: Relaxed 44:35, Normal 46:13, Hard 46:52 on the main seed (41:35 to 46:52 on all seeds).

- Every preset but Brutal wins on every seed; the win time is the wave clock (about three minutes a wave once the base is loud) with the jump drive research just ahead of it, as it was before ADR 0031. Normal sits at 48:45 to 48:59 (aim: about 50), Relaxed at 46:18 to 46:57 (aim: about 45), Hard at 50:36 to 51:36 (aim: about 55).
- The spread between the presets is only 2 to 3 minutes. It comes from the presets, not from the content: Hard has two more waves to survive, but its waves come 20 percent faster, which cancels most of it (the same was true before ADR 0031: 47:00 against 48:13). If Hard should land at about 55 the lever is the preset (13 goal waves puts it near 54), not the content; this was not changed because the presets are a rule table that the docs, the tests and the new-game screen quote.
- Brutal is still lost on every seed, at wave 9 or 10 of 15 (it was wave 3 to 8 before the rebalance, wave 10 to 11 in the first measurement of this document).
- Hard on seed 777 lost the ship before the rebalance (minute 58) and wins now.

## Hard raised to 13 waves (2026-11-23)

Owner decision: Hard sat only 2-3 minutes behind Normal. Hard now needs 13 waves and scales enemies by 120 percent (damage, hit points and wave size) instead of 130. With 13 waves and 130 percent the bot lost two of three seeds at wave 13 (ship lost at about minute 53); 120 percent keeps Hard winnable by the bot. StructureBot, 6 s per action:

| Seed | Hard (13 waves, 120%) |
| --- | --- |
| 20261121 | Win 52:21 |
| 20261120 | Win 54:18 |
| 777 | Win 53:06 |

Normal stays at about 49 minutes, so Hard is now 3-5 minutes longer and one wave harder.

## Materials and missions round (2026-11-24)
Five optional materials ([materials](materials.md#materials-and-what-they-are-for-audit-and-the-optional-chains-2026-11-24)), six read-only goal types and two new levels ([scenarios](scenarios.md)). Nothing the bot does touches the new items (it researches none of the three new nodes and builds no Mk2 module), and the wreck loot gained entries without moving any random draw. StructureBot, 6 s per action, seed 20261121, `--structures --difficulty=<preset> --minutes 150`:

| Preset (goal) | Win time |
| --- | --- |
| Relaxed (8) | 48:34 (wave 1 warning 20:23) |
| Normal (10) | 49:31 (19:04) |
| Hard (13) | 52:36 (17:49) |

These match main after the flight-feel merge (about 48.5, 49.5 and 52.6): the new content moved nothing. Level times with the new secondary goals (scenario bot): level 1 3:27 (1:51 without the optional bay, 3 stars), level 2 4:20 (3 stars), level 3 18:09 (3 stars), level 6 0:16, level 7 8:52 (3 stars). The `ship_speed` goals count engines fitted (100 percent each), so the engine research of ADR 0033, which raises top speed past the cap, does not change them.

## Weapons feel, economy and resource density (2026-11-24)
[ADR 0036](../adr/0036-free-fire-recoil-hold-overflow-and-resource-density.md), [weapons feel](weapons-feel.md). The starter hold holds 400, the mining laser is 250 percent (Mk2 425) and a drone bay takes 14 ticks an item, so a trip and a drone line yield two to three times what they did; the new `ResourceDensityPercent` rule adds deposits (Relaxed 150, Normal 140, Hard 120, Brutal 100), a full hold no longer stops the laser and emptied deposits regrow. The bot never sends an aim, so combat is as before; the economy is what moved.

Compensation: with only the economy changed the bot won Normal on the main seed at 45:49 (first wave warning at 17:28, it was 19:04): a richer start builds the noisy machines sooner and the alert meter fills faster. The first wave alert capacity (`threat.alert_max`) went from 216000 to 234000, which puts the wave clock back; no rule and no research cost was weakened. The bot also learned to slide sideways after being pinned twice against a rock on its way down to the mining spot (seed 777, Normal: 56:53 before, 52:44 after).

StructureBot, 6 s per action, `--structures --difficulty=<preset> --seed N`, minutes to the win (targets Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56):

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 |
| --- | --- | --- | --- |
| Relaxed (8) | Win 45:24 | Win 45:33 | Win 44:27 |
| Normal (10) | Win 47:35 | Win 47:44 | Win 47:19 |
| Hard (13) | Win 51:13 | Win 54:16 | Win 51:54 |

Before (main, same bot and seed 20261121): Relaxed 48:34, Normal 49:31, Hard 52:36.

- Relaxed is inside its band on every seed. Normal's two seeds land 0:01 and 0:24 under 48 and Hard's seed 777 1:16 over 56: the spread between seeds (up to 5 minutes) is wider than the band for Normal, so the capacity was set for the middle. Seed 777 is slow on every preset because the bot's approach to the home iron rock pins a wing for minutes there; its waves come later because the bot builds less meanwhile. A better approach for the bot is the real fix; it was not attempted beyond the sideways slide.
- Running with `--density=100` on seed 777 gives the same minute, so the extra deposits are not what moved the bot there. The `--density=N` flag of the Balance tool plays any density (the presets carry 150, 140, 120 and 100).
- Brutal was not re-measured.
## Personal inventory round (2026-11-24)
[ADR 0035](../adr/0035-personal-inventory-container-moves-and-reactor-fuel-slots.md) adds the personal bag, `MoveItems`, `SetFeedSource` and a reactor fuel slot. The bot never moves items by hand and the default feed source is the old rule, so nothing moved. StructureBot, 6 s per action, seed 20261121, `--structures --difficulty=<preset> --minutes 150`:

| Preset (goal) | Win time |
| --- | --- |
| Relaxed (8) | 45:16 |
| Normal (10) | 47:36 |
| Hard (13) | 51:51 |

### Known-issues round (2026-11-25)
The bot's build trips now choose a pose that keeps every module box clear of the deposit rocks (with the hull padding), so seed 777 no longer pins a wing against the home iron rock (Normal 52:44 became 47:19, Hard 57:16 became 51:54). The table above is the new measurement; the seed spread is under 1 minute for Relaxed and Normal and 3 minutes for Hard. Normal now sits 0:16 to 0:41 under its 48 to 53 target on all three seeds: the economy was balanced against the slow seed, so this is the honest number and the economy was not retuned. The test bands (Relaxed 40 to 52, Normal 45 to 55) still hold.
