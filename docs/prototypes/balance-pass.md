# Prototype P9: the first balance pass

Status: done, awaiting a human play-test (2026-11-20). Decision: [ADR 0009](../adr/0009-pacing-targets-measured-by-a-scripted-player-and-a-per-item-depot-cap.md) (proposed). Builds on the [polish and controls](polish-controls.md) prototype.

## The balance simulator
`tools/Starfoundry.Balance` is a headless scripted player. It plays the eleven guided steps with the same commands a human sends (fly, mine, refine, three production lines, depot swapped for a lab, research, scan, a fed turret with weapons on) and records when each step, each research and the first wave happen. It is a lower bound for a human, so a pause after every action stands in for clicking and thinking.

```powershell
dotnet run --project tools/Starfoundry.Balance -- --delay=15 --observe=30
dotnet run --project tools/Starfoundry.Balance -- --delay=15 --trace
```

`--delay` is the pause in seconds (0 is the floor no player can beat), `--observe` keeps playing that many minutes after the steps to watch the waves, `--trace` prints the hold, machines and waves every 30 simulated seconds, `--seed` changes the galaxy.

## What it found (before any change)
| Finding | Evidence |
| --- | --- |
| The first stretch was far too fast | Perfect play finished all eleven steps in 3:50 |
| The first wave came before the player could defend | Wave at about 4:50 (warning 4:20), with no lab and no turret yet; 7 machines lost |
| The hold could trap the player | At a beginner pace a depot delivering ingots filled the 60-item hold; plates could not enter, so the lab could never be paid; the hold stayed full for 1,277 s of a 25-minute run and the run never finished |
| Raw ore piled up | Hand mining (2 per second) outruns the ship refinery (one ingot per 4 s); a bot that mined without pause filled the hold with ore |

## What changed
| Area | Change |
| --- | --- |
| Simulation | `PlayerRules.DepotItemCap` (24 of 60): a depot stops delivering an item when the hold has that many of it. A full hold still stops every depot |
| Machine costs | Drill 6 to 10 ingots, smelter 8 to 14, assembler 12 to 20, depot 5 to 8, lab 10 ingots and 4 plates to 16 and 8, turret 10 and 4 to 14 and 6 |
| Research costs | Logistics 10 to 90 plates, carbon drilling 15 to 135, advanced logistics 40 plates and 10 carbon to 300 and 75, jump drive 30 and 10 to 225 and 75 |
| Threat | `alert_max` 18,000 to 216,000 and `alert_after_wave` 4,000 to 48,000: the meter rises by the signature above the threshold each tick, so a two-line base now takes about 12 minutes to call the first wave (a bigger base, sooner) |
| Interface | Depots say when they stopped at the cap (`status.depotCap`); the lab hint no longer states a number |
| Tests | Costs in loop tests are read from the content; `BalanceTests` pins the targets below |

## Results
Same content, default galaxy seed, 30 minutes of observation after the steps. Times are minutes:seconds of simulated time.

| Pace (pause per action) | Mine 5 ore | First drill | First automation (depot) | Lab built | Logistics researched | All 11 steps | Turret armed | First wave | Hold full | Machines lost |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 s (floor) | 0:04 | 0:43 | 2:35 | 4:00 | 9:57 | 9:57 | 5:19 | 11:05 | 0 s | 0 |
| 5 s (experienced) | 0:04 | 0:43 | 2:34 | 4:12 | 10:02 | 10:02 | 5:19 | 11:13 | 0 s | 0 |
| 15 s (first time) | 0:04 | 0:43 | 2:35 | 5:15 | 10:56 | 10:56 | 7:40 | 12:28 | 16 s | 0 |
| 30 s (slow) | 0:04 | 0:43 | 2:53 | 8:04 | 13:03 | 13:03 | 12:44 | 15:20 | 50 s | 0 |

Five waves and 30 enemies were fought in the 30 observed minutes at every pace, with one fed turret and the ship weapon, and no machine was lost. The three other galaxy seeds tried give the same times within a second (checked at the 15 s pace).

The in-game bot (`--demo`) agrees: Logistics after 9.6 simulated minutes (it was 2.9).

## Targets pinned by tests
Every pace finishes without getting stuck (hold full at most 8 percent of the run, finished inside 30 minutes); a perfect player cannot finish Logistics before 9 minutes; a first-time pace takes 9 to 20 minutes with the first drill inside 5 minutes and the first automation inside 10; the first wave comes at least 2 minutes after the turret is armed at every pace; a defended base loses nothing to the early waves; the results do not depend on the seed.

## Known limitations
- A scripted player is not a human: it never misreads a hint, never builds a worse layout and builds one plate line. The pause only approximates hesitation. A human play-test is the real check; the targets and numbers are meant to move after it.
- The experienced and first-time paces differ by only a minute: from 15 seconds per action on, the economy is no longer the limit, the player is. The economy bites at the fast end (a perfect player waits 282 s for items).
- Waves are too easy once a turret is fed: one turret and the ship weapon handled five waves without a loss. The ramp (`per_wave` growth, enemy types) is untouched and is the next balance question.
- Only the first stretch was balanced: the later research nodes (advanced logistics, the jump drive), carbon, exploration and jump costs were scaled with the same factor but not measured.
- Hold size, a way to discard items and hold upgrades are not decided.
