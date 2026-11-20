# Prototype P3: the playable loop

Status: done (2026-11-19). Builds on [belts](belts.md), [machines](machines.md) and the [command layer](../specs/13-command-layer.md). Decision: [ADR 0005](../adr/0005-campaign-economy-hold-costs-and-research-gates.md) (proposed).

## The loop
1. **Mine by hand** (`M`): 2 ore per second into a 60-item hold.
2. **Refine on the ship** (`F`): the micro-refinery turns ore into ingots, one every 4 seconds.
3. **Build** (`1`-`9`, click or drag): belts, drills, smelters, assemblers, research labs and depots. Everything costs items from the hold; removing refunds.
4. **Automate**: a drill pulls ore from a deposit, a belt carries it to a smelter, then on to a **depot** that delivers ingots straight into the hold.
5. **Make plates** with an assembler and feed them to a **research lab** (`T` chooses the research).
6. **Unlock**: finished research opens new belt tiers and recipes (Mk2 belts, carbon drilling, Mk3 belts).

## What is new
| Area | Addition |
| --- | --- |
| Simulation | Players with a hold, hand mining, deposits, ship refinery, drills, depots, labs, research graph, build costs and refunds, content locks, sandbox and campaign modes |
| Content | `content/core.json`: machine roles and costs, belt costs, research nodes, a ship-only machine and a depot |
| Commands | `StartMining`, `StopMining`, `SetShipRecipe`, `SetResearch`, `LinkDeposit`, `UnlinkDeposit`; costs and locks enforced inside the existing build commands; log header carries the game mode (format version 3) |
| Godot | `Play.tscn`: first playable scene with HUD, build palette, drag-to-place belts, auto-linking of neighbours, removal, camera, speed and pause; `--demo` runs a scripted bot |

`Main.tscn` is still the benchmark scene (`godot --path game res://Main.tscn -- --bench`).

## The demo bot
`--demo --speed=60` plays the loop with the same commands as a player: mine and refine, build an ingot line ending in a depot, build a plate line, swap its depot for a research lab, research Logistics. Two of its steps go through the exact mouse code paths (belt drag, machine placement from the palette selection).

Result on this machine: Logistics researched after 2.9 simulated minutes (80 ore mined by hand, 102 ore drilled, 52 items delivered by depots, 10 plates used), Mk2 belts unlocked, invariants valid at the end.

## Tests
137 in total. New: loop rules (mining rate, full hold, deposit depletion, independent players, ship refinery, refund of aborted crafts), costs and refunds per player, locks and sandbox-only rules, research flow through a real automated chain, labs jamming on unneeded items, depots and a full hold, content validation for roles, costs, belts and research, and a random campaign storm on five seeds (3,000 ticks, invariants every tick, replay from the same starting world through the binary format).

## Known limitations
- All numbers are placeholders; no balance pass yet.
- The ship now flies and has ranges ([world and ship](world-and-ship.md)), but there is no collision, inertia or fuel, and the deposits are a fixed starter field.
- The HUD is now bilingual (es/en), see [polish and controls](polish-controls.md).
- Mouse picking and the HUD were verified through the bot and screenshots, not by a human at the keyboard.
- Hold capacity can trap a player who lets ore fill it; the interface now warns when it is full.
- Machine inputs from several belts are still not served fairly, machines are one cell, and there is no power or heat.

## Run it
```powershell
dotnet test
dotnet build game -c Debug
```
Then open `game/` in Godot 4.7.2 (Mono) and press F5, or run the scripted loop from the command line with `-- --demo --speed=60 "--shot=path.png"` after the project path.
