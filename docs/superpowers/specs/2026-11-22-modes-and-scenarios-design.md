# Game modes, difficulty, scenarios and more resources

Status: proposed (2026-11-22). The owner asked to check and enrich everything that makes a game richer: objectives, breadth of resources, difficulty, sandbox customisation, story levels. Decisions are delegated to the designer. Builds on the structures campaign (ADR 0018-0022) and `GameOutcome`.

## What exists today
- `GameMode` has only Campaign and Sandbox in the sim. The campaign is one fixed scenario (starter ship, 10 waves with a station and, after the balance pass, a jump). Difficulty is a single setting (`goal_waves`). The sandbox is a developer mode (free build) with no player-facing options.
- One fixed set of guided steps. Goals are hard-coded in `GameOutcome`.
- Resources: iron, carbon, ice, titanium, nickel, quartz and derived plates/ingots/ammo/hydrogen. Quartz and carbon have few uses.

## Goals
1. **World rules**: a data block of integer percentages and flags that scales the game and is stored in the command-log header (so replay and saves reproduce it): `enemy_damage_percent`, `enemy_hp_percent`, `wave_size_percent`, `wave_interval_percent`, `first_wave_grace_seconds`, `deposit_richness_percent` (scales deposit amounts at generation), `power_output_percent`, `starting_items` (stacks added to the starter hold), `threats_enabled`, `free_build` (module, machine and belt costs are zero), `goal_waves`. Difficulty presets: Relaxed, Normal, Hard, Brutal (named bundles of these) plus Custom.
2. **Scenarios** (data-driven, `content/scenarios/*.json`, loaded and validated like content): id, es/en name, description and briefing, rules (preset or explicit), start (starter ship or a custom module list, starting items, fixed seed or random), a list of goals and optional story beats. Goal types: `survive_waves(n)`, `research(node)`, `produce(item, count)`, `visit_systems(n)`, `found_stations(n)`, `build_module(kind, n)`, `mine(item, count)`, `defeat_waves_without_losing(module_kind)` (optional), each with an optional time limit; a scenario is won when all primary goals are done (secondary goals give stars). Story beats are texts shown when a goal completes or at start/end.
3. **Story levels** (5, each teaches one thing): 1 *First Light* (start in the ship: mine iron, make plates, found a station), 2 *Cold Fuel* (power: build the ice port and cracker, keep the reactor burning, found a powered station), 3 *Under Siege* (a pre-built station with a defence module and no turrets: survive 5 waves with a limited budget), 4 *Deep Vein* (jump to a titanium system, research titanium and plating, return), 5 *The Long Watch* (the full standard campaign goal on Hard). Campaign (Standard) stays as the open scenario.
4. **Sandbox customisation**: a Sandbox scenario template with player-chosen rules (free build on/off, threats on/off, richness, starting items from a list, starter ship or blank station, seed) saved as named presets in `user://sandbox_presets.cfg`.
5. **More resources and uses** (content + visuals + strings): `core:silicon` (smelted from quartz), `core:circuit` (assembler: silicon + nickel plate), `core:titanium_alloy` (titanium plate + carbon), `core:fuel_cell` (hydrogen + nickel plate, stores fuel compactly and refuels faster); new module kinds `Shield` (absorbs a percentage of module damage while powered; needs circuits), `Sensor` (reveals deposits and enemies at range and shows wave composition in advance; needs silicon), `Laboratory` is NOT new; new research nodes using them; a second reactor tier `reactor_mk2` module (output 250) needing circuits and alloy. Keep every cost deliverable (the content analysis tests must still pass).
6. **Progress and objectives UI**: a New Game screen (mode, scenario or level select with locks and stars, difficulty preset, custom rules, seed), a goals panel in the HUD (primary and secondary goals with progress), briefing and outcome texts, a progress record in `user://progress.cfg` (levels completed, stars, best time), and the end screen showing goals and stars.

## Non-goals
Co-op, a level editor UI beyond sandbox rules, procedural story generation, leaderboards, achievements platform integration.

## Constraints
Deterministic integer sim; legacy worlds and existing tests must keep passing; rules go in the log header with a version bump that still reads older logs as default rules; content hash changes are expected; every new text in es and en; headless screenshots for visuals; bot checks for balance (the structures bot must still win Normal and the story levels must be winnable by scripted play where feasible).

## Order
A. World rules + presets (sim). B. Scenario schema + goals engine + the five levels (Ui + content, uses rules). C. More resources (sim + content + visuals + strings). D. Game UI: New Game screen, goals panel, progress, end screens. E. Bot validation of levels and presets, docs, hand tests.
