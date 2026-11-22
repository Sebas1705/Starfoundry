# Prototype: the ship's mining laser

Status: done, awaiting a human play-test (2026-11-22). Replaces the "press `M` and the ship mines by itself" toggle. Builds on [polish controls](polish-controls.md).

## Goal
Owner feedback: mining must be done with the ship's weapons, not by pressing a key and that is it. Mining is now an act of aiming and firing.

## What changed (proposal)
| Area | Change |
| --- | --- |
| Weapon slot | `M` takes the mining laser in hand or stows it (the Mine button does the same). It sits beside the cannon (`V`, auto-targeting enemies); the key panel shows "Mining laser" and, while it is out, a "Hold click" row that lights up while it fires. Taking a build tool puts it away. |
| Firing | With the laser out, aim the crosshair at a deposit within 6 cells (`PlayerRules.MineRangeCells`) and hold the left click. The game sends the existing `StartMining` command for the deposit under the crosshair and `StopMining` as soon as the button is released, the crosshair leaves the deposit, the deposit is out of range, the player stands up or a panel opens. Only changes are sent. |
| Simulation | Unchanged: mining progress only advances while the player holds a mining order and the deposit is in range, 2 ore per second scaled by density, paused at the hold limit. The aim lives in the client (as the player's view does for every other command), so the command log version and every golden checksum are untouched. |
| Out of range | Holding fire on a deposit beyond the range shows a toast with the range; there is no autopilot any more. |
| Visual | A pulsing beam (core and halo) from the ship's nose to the rock surface with a hot spot where it lands, drawn only while the simulation is mining. |
| Audio | Reuses the procedural `MineLoop` buzz (it plays while the sim mines) and the `MinePop` per ore item. |
| Removed | The fly-to-the-deposit autopilot of the old `M` toggle. |

## Not done (TBD)
- The yield and rate do not yet depend on the weapon: there is a single laser. A tiered laser (rate or range upgrades through research) would be a sim rule and needs an ADR.
- The laser does not draw power. The reactor model prices machines, not ship tools; adding a draw is a sim change (golden checksums of structure worlds move) and is left for a decision.
- The bots (`StructureBot`, `BalanceBot`) keep issuing `StartMining`/`StopMining` directly, which is exactly what the client sends, so their runs and timings are unchanged.

## Verified
`dotnet build game`, `dotnet test` and a headless capture (`--structures-shot` with a yaw towards the rock) showing the beam on the deposit. Not tested: real mouse holding in a play session and the laser at the 720p and 1080p layouts.
