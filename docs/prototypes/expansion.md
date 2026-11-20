# Prototype P15: leaving the home ore behind

Status: done, awaiting a human play-test (2026-11-20). Follows from [fair outputs and the home ore](fair-outputs-and-ore.md), which found that a growing factory empties the 4,000 ore of the home deposit in about two hours.

## The question
How soon can a player expand to another deposit, what does it take, and what is left at home when they do?

## What it adds
| Area | Change |
| --- | --- |
| Guided steps | A twelfth step, **Jump to another system**, ticked off by the first visit to a system other than home. Its hint says the home deposit runs out in a couple of hours and names the way: research carbon drilling and the jump drive (`T`), scan (`K`), jump (`G`, `J`) to a system with iron. es and en |
| Balance simulator | `--expand`: after Logistics the lab researches carbon drilling and the jump drive; a second lab beside the carbon deposit gets carbon from a drill by belt; the ship hand-mines the fuel for a round trip (10 carbon), jumps to the nearest neighbouring system whose archetype makes iron, scans, flies to the closest deposit, builds an ingot line and a plate line there (each paid from the hold, one placement at a time) and jumps home. Milestones and the home iron left at each are in the report |

```powershell
dotnet run --project tools/Starfoundry.Balance -- --delay=15 --observe=40 --minutes=150 --turrets=1 --walls=4 --bays=2 --expand --extra-plates
```

## Results
Default seed, 15 s per action, one fed turret, four walls, two repair bays and a dedicated plate line:

| Milestone | Time | Home iron left |
| --- | --- | --- |
| Logistics researched | 10:57 | |
| Carbon drilling researched | 21:11 | |
| Carbon line built | 21:13 | 2,825 |
| Jump drive researched | 37:05 | 2,273 |
| Jump out, arrive, scan | 37:13, 37:16, 37:17 | 2,264 |
| Remote ingot and plate lines built | 38:57 | 2,164 |
| Back at the base | 39:02 | 2,156 |

The same run on other seeds: jump drive at 36:20 to 36:55, remote lines at 38:51 to 43:02, home again at 38:56 to 43:07. The first remote deposit had 2,499 ore.

- **The jump is plate-limited, not carbon-limited.** The two researches cost 135 plates (carbon drilling) and 225 plates with 75 carbon (jump drive); the carbon arrives in a couple of minutes, the plates take a quarter of an hour each. The round trip costs 10 carbon, hand-mined.
- **About 40 minutes in, a player can leave with more than half of the home ore still there** (54 percent), and the building at the new deposit takes under two minutes.
- **The remote lines delivered almost nothing in the first hour** (28 items): the hold already holds the most a depot will deliver of each item, so they only replace what is spent. They matter once the home ore is gone.

## Findings
- **Infrastructure outside the defence is the first thing lost.** In the same run the first machine fell during wave 12, against 17 and 19 without the expansion: the second lab and the carbon drill sit at the carbon deposit, outside the turrets' reach, and the loudest machine is what enemies attack. A player who expands has to defend the new buildings or accept losing them.
- **Staged building is needed.** The ingots of the whole carbon line (29) are more than the hold keeps from depots (24 of one item), so it cannot be placed as one batch; each piece has to be paid separately.
- The ship being away costs nothing in the scripted run (two minutes, the round trip is a few seconds); a long stay would leave the home base without repairs or rebuilding.

## Targets pinned by tests
For two seeds (20261120 and 1): the jump drive is researched 30 to 45 minutes in; the remote lines are built within one to five minutes of arriving; the ship is home again 35 to 50 minutes in, after the lines; at least 40 percent of the home ore is left when the remote lines are built; the guided step is ticked off. 337 tests.

## Known limitations
- Nobody has played it by hand. The scripted player knows where the carbon deposit and a neighbour with iron are; a person has to find them (scan, the star map).
- Only the first expansion is measured. Whether the remote lines keep the defence alive after the home ore ends, and what a second or third expansion costs, are not.
- The remote lines are undefended; waves for a system with machines in it come from its own signature, which the scripted run does not wait to see.
- Only plates for the first two researches limit the time to jump; with a faster plate line it would be earlier.
