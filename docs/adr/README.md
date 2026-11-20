# Architecture decision records

One decision per file, `NNNN-slug.md`. An accepted ADR is never rewritten; add a new one and mark the old one superseded.

| # | Decision | Status | Date |
| --- | --- | --- | --- |
| [0001](0001-engine.md) | Use Godot 4 with C# for the game layer | accepted | 2026-11-19 |
| [0002](0002-deterministic-simulation-core.md) | Engine-independent deterministic simulation core | accepted | 2026-11-19 |
| [0003](0003-commands-as-the-only-way-to-change-the-world.md) | All world changes go through validated, logged commands | accepted | 2026-11-19 |
| [0004](0004-data-driven-content-and-one-cell-machines.md) | Data-driven content and one-cell buffered machines | proposed | 2026-11-19 |
| [0005](0005-campaign-economy-hold-costs-and-research-gates.md) | Campaign mode: build costs paid from the hold, research gates content | proposed | 2026-11-19 |
| [0006](0006-ship-movement-as-intent-commands-and-ranges.md) | Ship movement as intent commands, with mining and build ranges | proposed | 2026-11-20 |
| [0007](0007-galaxy-as-regions-of-one-space-with-deterministic-generation.md) | The galaxy is a set of regions of one shared space, generated deterministically | proposed | 2026-11-20 |
| [0008](0008-remnant-threat-as-signature-driven-waves.md) | The Remnant threat: signature fills an alert meter, the meter sends waves | proposed | 2026-11-20 |
| [0009](0009-pacing-targets-measured-by-a-scripted-player-and-a-per-item-depot-cap.md) | Pacing targets are measured by a scripted player; depots deliver at most 40 percent of the hold per item | proposed | 2026-11-20 |
| [0010](0010-waves-can-start-later-and-the-ramp-is-measured-per-turret.md) | Waves can start later, and the wave ramp is measured against the number of fed turrets | proposed | 2026-11-20 |
| [0011](0011-walls-draw-enemies-and-repairs-restore-hit-points.md) | Walls draw enemies, repairs restore hit points | proposed | 2026-11-20 |
| [0012](0012-repair-bays-heal-slowly-from-a-belt-fed-item.md) | Repair bays heal slowly from a belt-fed item | proposed | 2026-11-20 |
| [0013](0013-defence-upgrades-are-research-bonuses.md) | Defence upgrades are research bonuses | proposed | 2026-11-20 |
| [0014](0014-repeatable-research-with-rising-cost.md) | Repeatable research with rising cost | proposed | 2026-11-20 |
| [0015](0015-cargo-ports-unload-the-hold-onto-belts.md) | Cargo ports unload the hold onto belts | proposed | 2026-11-20 |
| [0016](0016-saves-are-the-command-log-replayed.md) | Saves are the command log, replayed | proposed | 2026-11-20 |

## Template
```markdown
# NNNN. Title as a decision

- Status: proposed | accepted | superseded by ADR NNNN
- Date: YYYY-MM-DD
- Area: engine | simulation | data | network | tooling

## Context
Facts only.

## Decision
Present tense.

## Consequences
Benefits, costs, how it is checked.

## Alternatives considered
```
