# 0023. World rules and difficulty presets live in the log header

- Status: proposed
- Date: 2026-11-22
- Area: simulation, gameplay
- Design: [modes, scenarios and resources](../superpowers/specs/2026-11-22-modes-and-scenarios-design.md), part A

## Context
Difficulty was one number (`goal_waves`, a UI setting that the simulation never saw). Everything that makes a game easier or harder (enemy strength, wave size and pace, how rich the deposits are, how much power a reactor gives, whether building costs anything) was fixed in `content/core.json`. Scenarios, a sandbox with player-chosen options and difficulty presets all need those knobs, and a replay or a save must reproduce them exactly, because a save is the command log replayed ([ADR 0016](0016-saves-are-the-command-log-replayed.md)).

## Decision
- **`WorldRules`**, an immutable class in the simulation, holds integer percentages and flags: `EnemyDamagePercent`, `EnemyHpPercent`, `WaveSizePercent`, `WaveIntervalPercent`, `FirstWaveGraceSeconds` (-1 = the content value), `DepositRichnessPercent`, `PowerOutputPercent`, `StartingItems`, `ThreatsEnabled`, `FreeBuild` and `GoalWaves`. The defaults are exactly today's behaviour. Percents are 25..300, richness 25..400, grace 0..3600 s, goal 1..30 waves, at most 64 starting stacks of 1..9999. `Validate` reports the first problem, `Clamped` forces a UI value into range, and the world constructors refuse invalid rules.
- **Presets** are named bundles: Relaxed, Normal (all defaults), Hard and Brutal. Their values are in [the difficulty prototype](../prototypes/difficulty.md). A preset is only a value of `WorldRules`; "Custom" is any other value.
- **Rules are part of the world's creation arguments**, like the seed and the three bools: `SimWorld.CreateGalaxy(..., WorldRules? rules = null)` and `new BeltNetwork(..., rules)`. The old `threats` bool stays; `ThreatsEnabled` in the rules is ANDed with it, so rules can only switch threats off.
- **Header version 7.** The log header gets flags bit 3 (value 8). When set, an int length and the `WorldRules.Serialize()` block follow the flags byte. The block is written only for non-default rules, so a default world's header differs from version 6 only in the version byte. Versions 5 and 6 still read, as default rules; version 6 with bit 3 is refused. `CommandCodec.ReadLog` has a new overload returning the rules, `SaveGame.Write/Peek/Read` carry them (`SaveInfo.Rules`), and `Replay` takes an optional `rules` when it builds the empty world.
- **The checksum mixes the serialised rules only when they are not the defaults**, so every existing golden checksum stays valid, and a replay that ignores the header rules diverges (a save then refuses to load).
- **Effects, all integer.** Hit points scale at spawn and damage at every hit (round half up, at least 1). Wave size scales each wave-table entry (round half up, at least one of a type that has any). The wave interval scales the alert meter's capacity and its after-wave value together, so the time to every wave scales as a whole; the meter shown in the HUD uses the scaled capacity. The first wave grace replaces the content value. Richness scales generated deposits when the galaxy is created. Power output scales a burning reactor (the emergency trickle is not scaled). Starting items are added to the starter hold in order as far as it has room; the units that did not fit are counted in `BeltNetwork.StartingItemsClamped`. Free build is one check in the cost helpers (`CanPayBuild` and `PayBuild`) used by every module, machine, belt and station placement and by repair, and `Refund` gives nothing back in a free world, so removing things cannot create items. Jumps keep their cost.
- **`GameOutcome`** reads the goal from the rules: `Evaluate(net, player)` uses the world's own rules, `Evaluate(net, player, WorldRules)` takes explicit ones, and the `GoalSettings` overload still works. `GoalSettings.From(WorldRules)` converts.

## Consequences
- Benefits: difficulty, scenarios and sandbox options are data, replays and saves reproduce them, and a game on defaults is bit-identical to before (the golden checksums did not change).
- Costs: a version 7 log cannot be read by a build that only knows version 6; the percentage scalings round, so tiny enemy stats (a damage of 4) move in coarse steps.
- How it is checked: `WorldRulesTests` (defaults, presets, validation, serialisation, header, save and replay, and each effect measured against the default), `GameOutcomeTests`, the unchanged golden checksum tests, and the structure bot with every preset (`tools/Starfoundry.Balance -- --structures --difficulty=...`).
- Not decided here: the New Game screen and custom-rule UI (part D of the design), scenario files and goals (part B), the balance of Hard and Brutal beyond what the bot shows.

## Alternatives considered
- **Rules in the content file.** Rejected: they would change the content hash and a difficulty could not be chosen per game.
- **Scaling enemy stats in the content at load time.** Rejected: the simulation would need one content per difficulty and saves would have to name it; header rules are smaller and explicit.
- **A float multiplier.** Rejected: the simulation is integer-only so that replays are exact across machines.
