# Architecture decision records

One decision per file, `NNNN-slug.md`. An accepted ADR is never rewritten; add a new one and mark the old one superseded.

| # | Decision | Status | Date |
| --- | --- | --- | --- |
| [0001](0001-engine.md) | Use Godot 4 with C# for the game layer | proposed | 2026-11-19 |
| [0002](0002-deterministic-simulation-core.md) | Engine-independent deterministic simulation core | proposed | 2026-11-19 |

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
