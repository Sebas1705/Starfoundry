# 0009. Pacing targets are measured by a scripted player; depots deliver at most 40 percent of the hold per item

- Status: proposed
- Date: 2026-11-20
- Area: simulation, data, tooling

## Context
- [Technical architecture](../specs/09-technical-architecture.md) asks for a balance simulator: headless runs that measure time to milestones, because content is tuned by simulation, not by hand.
- Every number so far was a placeholder. Run by a scripted player, the guided first steps took 3:50 with perfect play and the first Remnant wave arrived at about 4 minutes, before the player could have a lab or a turret. That breaks the pacing rule "threat waves never arrive before the player has a first weapon and a way to build a turret" ([core loop](../specs/01-core-loop.md)).
- At a beginner's pace the scripted player got stuck for the rest of the run: a depot delivering ingots filled the whole 60-item hold, plates could no longer enter, and the plates were needed to build the lab. A human would hit the same wall.

## Decision
- A headless scripted player (`tools/Starfoundry.Balance`) plays the guided steps with the same commands as a human and records when each step, each research and the first wave happen. A pause after every action (5, 15 or 30 seconds) stands in for a human; 0 is the floor that no player can beat.
- Pacing targets are checked as tests (`BalanceTests`) so a content change that breaks them fails the build:
  - every pace finishes the eleven guided steps without getting stuck (the hold is full for at most 8 percent of the run);
  - a perfect player cannot finish the first research before 9 minutes;
  - a first-time pace (15 s per action) finishes all steps in 9 to 20 minutes, with the first drill inside 5 minutes and the first automation inside 10;
  - the first wave arrives at least 2 minutes after the turret is armed, at every pace;
  - a base with one fed turret and the ship weapon loses no machine to the early waves;
  - the results do not depend on the galaxy seed.
- A depot stops delivering an item once the hold has `DepotItemCap` of it (40 percent of the capacity, 24 of 60), so one automated line can never take the whole hold. A full hold still stops every depot.
- Numbers changed to meet the targets (all in `content/core.json`): machine costs about 1.5 to 2 times higher, research costs 9 times (Logistics, carbon drilling) and 7.5 times (the later nodes), `alert_max` 12 times and `alert_after_wave` 12 times.

## Consequences
- Benefits: pacing is a measured property, not a feeling; the deadlock is gone and covered by a test; costs in tests are read from the content, so later balance changes do not break unrelated tests.
- Costs: the targets come from a scripted player that never hesitates and builds one plate line; they say nothing about how a human reads the hints. A human play-test must confirm or move them.
- A single machine cost can never exceed the per-item cap, or a depot-fed player could not afford it; the largest cost is 20 ingots against a cap of 24.
- Not decided here: hold upgrades, a way to discard items, wave difficulty beyond the first minutes (one fed turret survives five waves in 30 minutes), the economy beyond the first research.

## Alternatives considered
- **A bigger hold:** hides the deadlock instead of removing it.
- **Depots that stop when a player-chosen item is enough:** more control, but a new interface for a problem a simple cap solves.
- **Tuning by feel:** what the placeholders were; it produced a wave before the first turret.
