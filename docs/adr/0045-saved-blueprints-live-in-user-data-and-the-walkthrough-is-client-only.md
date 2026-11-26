# 0045. Saved blueprints live in user data, the walkthrough is client only, and on foot you repair and demolish

- Status: proposed
- Date: 2026-11-26
- Area: tooling

## Context
- Phase 5 of the [blueprint editor](../superpowers/specs/2026-11-26-blueprint-editor-design.md) asks for saved blueprints (slots, presets, variants: spec E1 of the [pilot feedback round](../superpowers/specs/2026-11-24-pilot-feedback-round-design.md)), a walkthrough preview, the demolish and repair shortcuts on foot, and the performance of large stations.
- [ADR 0043](0043-stations-are-edited-as-blueprints-with-applyblueprint.md) and [0044](0044-the-station-interior-is-drawn-on-a-floor-plan.md) already carry every structure edit in `ApplyBlueprint`; `RemoveMachine`, `RemoveSegment` and `Repair` were never gated.

## Decision
- **No simulation change, no command, no log version.** Saved designs, the walkthrough and the on-foot tool rules are client features. The command log, the save game and the world hash do not see them; the next free log version stays 26 and the next ADR 46.
- **Saved designs** are text files in the user data (`user://blueprints/*.bp`), one per design, written through a temporary file. Format version 2 (`starfoundry-blueprint 2`): `structure`, `name`, optional `variant` and `preset`, then `m=` module lines (cell, kind id, quarter turn), `machine=` (cell, machine id, recipe id or `-`), `belt=` (cell, direction, length, tier) and `link=` (kind and the cells of both ends, so a design does not depend on index order). Ids are content ids, so a design survives content reordering; an unknown id makes the whole file unreadable (strict, never half a design). Version 1 files (the phase 2 slot) still read as a ship design with no interior.
- A **design** is a name; a **variant** is the same name with a variant label (its own file); a **preset** is a design pinned to the top of the list. Saving under an existing name and variant replaces it only on a second press. Rename, duplicate as variant, pin and delete (second press) are in the list. At most 64 designs per structure kind. Ship and station designs are separate lists; loading one for the other kind is refused.
- **Loading** puts the design into the draft as undoable steps (modules, then interior) and the usual preview, cost and validation say whether it can be applied; the simulation verdict still decides, so a stale design (a module that is no longer unlocked, a part with no module) cannot bypass any rule.
- **Shipped presets are not included.** Spec E1 wants shipped presets (miner, hauler, gunship, scout); they need balance and visual review, and are proposed as data under `content/` for a later round. The editor's presets are the player's own pinned designs.
- **The walkthrough** is a camera mode of the plan: the eye at `BodyMotion.EyeHeight` above the floor of a module, a wider field of view, look and walk with the arrow keys, kept inside the modules of the deck. It is preview only (no editing from inside).
- **On foot**, in a world with the floor plan, the build tool closes the machine and belt entries with a hint pointing at the editor (`FootTools`); right click demolishes what the crosshair is on and repair works as before, both with the commands that already exist. There is no build tool for automation on foot.

## Consequences
- Benefits: nothing in the deterministic core changes; old logs and saves replay as before (a test replays an editor session twice and compares hash and log bytes); designs are plain text a player can share.
- Costs: designs are per machine, not in the save game; presets are only the player's; the file format is a second text format next to the save game's binary one.
- Checked by `BlueprintLibraryTests` (round trip of a station with machines, belts and links, load into a fresh draft with the same price, slots and variants, rename, duplicate, pin, delete, bad and foreign files, the limit, safe file names), `BlueprintPolishTests` (the walkthrough camera, a 60 module station with 400 parts in the model and the tick loop, determinism of an editor session), `FootToolsTests`, and the fourth part of `--blueprint-check` (the design list in both languages and four sizes, save, overwrite prompt, variant, load, quick save, rename, pin, delete, the walkthrough).

## Alternatives considered
- **A. Store designs in the save game or the command log.** Rejected: a design is a convenience, not world state; putting it in the log would need a version and a gate for no gameplay effect.
- **B. A `SaveDesign` command.** Rejected for the same reason.
- **C. One file with all designs.** Rejected: one corrupt design must not take the others with it, and a file per design is easy to copy by hand.
- **D. Index-based links in the file.** Rejected: cell-based links survive reordering of parts and are easy to read.
