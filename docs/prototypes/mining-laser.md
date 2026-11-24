# Prototype: the ship's mining laser

Status: done, awaiting a human play-test (2026-11-22). Replaces the "press `M` and the ship mines by itself" toggle. Builds on [polish controls](polish-controls.md). The rules (trigger, power draw, research) are [ADR 0027](../adr/0027-mining-laser-rules.md).

Update (2026-11-24, [ADR 0036](../adr/0036-free-fire-recoil-hold-overflow-and-resource-density.md), [weapons feel](weapons-feel.md)): `M` no longer takes the laser out or stows it; it cycles the weapon in hand (the guns or the laser) and the left click fires it. The laser is 250 percent, hand mining slows to a quarter past 75 percent of the hold instead of stopping there, and a full hold overflows into ore pickups. The table below describes the first version.

## Goal
Owner feedback: mining must be done with the ship's weapons, not by pressing a key and that is it. Mining is now an act of aiming and firing, and firing is part of the simulation.

## What changed (proposal)
| Area | Change |
| --- | --- |
| Weapon slot | `M` takes the mining laser in hand or stows it (the Mine button does the same). It sits beside the cannon (`V`, auto-targeting enemies); the key panel shows "Mining laser" and, while it is out, a "Hold click" row that lights up while it fires. Taking a build tool puts it away. |
| Firing | With the laser out, aim the crosshair at a deposit within 6 cells (`PlayerRules.MineRangeCells`) and hold the left click. The first press on a deposit sends `StartMining`; letting go (or looking away, leaving the range) sends `FireLaser(false)`, which releases the trigger and keeps the order; pressing again on the same deposit sends `FireLaser(true)`. A new deposit, stowing the laser, standing up or opening a panel use `StartMining` / `StopMining`. Only changes are sent. |
| Simulation: trigger | `FireLaser` is a command and the trigger is player state, so a replay or save knows exactly when the player fired. A released laser mines nothing, draws no power and keeps its partial item. `StartMining` pulls the trigger, so every existing log, bot and test keeps its meaning. |
| Simulation: rate | 2 ore per second at density 100 (15 ticks per item), scaled by density, shortened by the research bonus `mining_rate`: laser focusing (+25 %, 60 plates) and beam collimator (+40 %, 100 plates and 20 carbon) take an item from 15 to 12 and then 9 ticks (3.3 ore per second). Paused at the hold limit as before. |
| Simulation: power | While it would mine, the laser adds 5 to its ship's power demand (ships with a reactor, structure worlds, not `LegacyLaser`). It is served before the machines: a brownout slows the machines, not the laser, so a starved base can still mine ice to restart its reactors; with no production at all the laser stops. |
| Out of range | Holding fire on a deposit beyond the range shows a toast with the range; there is no autopilot any more. |
| Visual | A pulsing beam (core and halo) from the ship's nose to the rock surface with a hot spot where it lands, drawn only while the laser fires (trigger pulled and in range). |
| Audio | Reuses the procedural `MineLoop` buzz (it plays while the laser fires) and the `MinePop` per ore item. |
| Removed | The fly-to-the-deposit autopilot of the old `M` toggle. |

## Compatibility
Command log version 9 (older logs read with `WorldRules.LegacyLaser`, so they replay without the power draw and cannot contain `FireLaser`); golden checksums unchanged (laser state is mixed only when set). Details in the ADR.

## Measured: the scripted player
`StructureBot` (`dotnet run --project tools/Starfoundry.Balance -- --structures --seed N --difficulty D`, 6 s per action), minutes to the win, before this change (main at bc35740) and after:

| Run | Before | After |
| --- | --- | --- |
| Normal, seed 20261121 | 45:01 | 45:21 |
| Normal, seed 20261120 | 43:50 | 44:42 |
| Normal, seed 777 | 47:59 | 44:16 |
| Relaxed, seed 20261121 | 43:14 | 43:19 |
| Hard, seed 20261121 | 46:26 | 46:25 |
| Brutal, seed 20261121 | lost at 41:49 | lost at 41:50 |

A bot in a world with `LegacyLaser` reproduces the "before" run exactly (45:01, same ore). A first version in which the laser shared the structure's brownout, and a draw of 10, moved the Normal wins by up to 4 minutes and sent the science-mode run into a refuel loop; serving the laser first fixed both (see the ADR). The bot plays the laser research last (after advanced plating, and after the science tier in science mode): researching it earlier delayed the jump drive by about 3 minutes, because the laboratory line, not the ore, limits the early game, so the upgrades do not pay back on the way to the win. Two bot robustness fixes came with it: the expedition throws spare ingots out again when the hold fills at the far end, and a refuel with a hold full of expedition cargo dumps half of the biggest raw stack.

## Not done (TBD)
- A toast or HUD line when the laser is starved (production below its 5), and a visual for the released trigger.
- Laser range upgrades, a second laser, drones sharing the bonus.
- Balance by hand: the draw (5) and the two bonuses are proposals measured only by the bot, which wins on the wave clock whatever its ore rate.

## Verified
`dotnet build game` and `dotnet test` (1180 tests, `MiningLaserTests` for each rule). The beam was verified by a headless capture in the first version of the laser; the client changes of this ADR (the trigger release instead of `StopMining`) were only compiled. Not tested: real mouse holding in a play session (the release and re-press on the same deposit are new), the beam going dark on a released trigger, and the laser at the 720p and 1080p layouts.
