# Fix: machines with two outputs share fairly; what the home ore limits

Status: done (2026-11-20). A bug fix with a finding about the economy. Found while measuring the [repeatable upgrades](repeatable-upgrades.md).

## The bug
A machine can feed up to two output belts and is meant to alternate between them. The turn to go first rotated every tick, whether or not an item came out. A craft that ends on the same tick parity every time (every recipe in the shipped content lasts an even number of ticks) therefore always met the same turn and gave every item to the same belt. The other belt got nothing until the first one backed up.

It showed up as a lab that was always researching taking every plate of its line, while a second output to a depot stayed empty and the scripted player could not build its first wall.

## The fix
The turn rotates only when an item is pushed, to the belt after the one that took it, and a belt that cannot take an item is skipped. Three tests (`OutputShareTests`) cover it: a 30-tick extractor and a smelter each split their items evenly between two belts (the old code gave 116 to 0 and 96 to 0), and a backed-up belt is skipped so the other takes everything. All existing tests and balance targets still hold. The change alters the order of items on machines with two outputs, so old command logs do not replay bit for bit; nothing in the repository stores such a log.

## What the home ore limits
The scripted player never moves to another deposit, and the home iron deposit has 4,000 ore. Tracing it explained the drop that appeared when more upgrades were researched (first machine lost with one fed turret, four walls and two repair bays, seeds 20261120 and 1: four upgrades 26 and 25, six upgrades 20 and 20):

| Minutes | Ore left with four upgrades | Ore left with six upgrades |
| --- | --- | --- |
| 30 | 2,765 | 2,765 |
| 60 | 1,793 | 1,793 |
| 90 | 1,033 | 746 |
| 110 | 830 | 5 |
| 130 | 513 | 0 |
| 140 | 265 | 0 |

With four upgrades the lab finishes at about 75 minutes and then jams, which stops its plate line and saves ore; with six it keeps researching until about 100 minutes and the lines drain the deposit, after which nothing can be built or rebuilt. So the results of every run longer than about 110 minutes measure the ore as much as the defence.

## What it means for the game
- A growing factory exhausts the home deposit in about two hours of play. The game then has to push the player to scan and expand to other deposits and systems, which the scripted player does not do; that is the next thing worth measuring.
- Idle labs and depots at their cap save ore because the lines behind them back up; a continuously working line costs ore.
- The balance simulator now prints the iron left in its trace (`--trace`).

## Known limitations
- The extra deposits (other systems, carbon) are not part of any scripted run, so how fast a player can replace the home ore is unmeasured.
