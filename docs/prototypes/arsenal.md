# Prototype: the arsenal bar

Status: built, awaiting a human play-test (2026-11-25). Builds on [weapons feel](weapons-feel.md) (ADR 0036), the hardpoint modules (ADR 0032), the [HUD information study](hud-information-study.md) and the controls redesign (input action registry).

## The question
The weapon in hand was a group (the mining laser or the guns) shown as a line of text, "In hand: ...", at the top centre. Can mining and the guns read as one arsenal of numbered slots that the pilot sees, switches with the keys he already has, and trusts at a glance?

## What it is
- **The model** (`ArsenalModel`, `ArsenalVisibility` in `src/Starfoundry.Ui`, engine-free): one slot per weapon kind and mark fitted, built from the hardpoint modules of the ship (lasers first, then guns, Mk1 before Mk2, at most nine). A world without hardpoint modules has the implicit laser and gun as two Mk1 slots. A slot has the icon, the module count, a state (`Ready`, `Firing`, `Cooling`, `Blocked`, `LowPower`, `NoPower`, `Dry`), a one-line readout and a tooltip. Selection wraps; `Key1`..`Key9` map to slots; the marker eases to the slot in hand in about a tenth of a second.
- **The bar** (`game/ArsenalBar.cs`, `game/Play.Arsenal.cs`): at the bottom centre, over the hold strip and the key panel when the window is narrow (the hint and the toasts rise above it). The slot in hand is outlined in gold; the readout under it is the mining numbers (items per second, deposit left) for a laser or the volley state and ammo for a gun; the other slots show their state word. Hover names a slot (tooltip, es/en); with the mouse free (ship computer open) a click takes it in hand.
- **The cockpit weapons page** lists the same slots from the same model, one row each, and a click picks one.
- **Keys** (registry `weaponPick` 1-9, `weaponNext` V, `weaponWheel`): a slot takes its weapon group in hand. Nothing changes in the simulation or the command log: `PickWeaponGroup` is the same call as before.

## Rules
| Situation | Bar |
| --- | --- |
| Seated, calm, nothing used for four seconds | fades out |
| A switch, a shot, the laser firing, a wave or enemy near, the ship computer open | shown at once and kept while it lasts |
| HUD detail Minimal | only the slot in hand (icon, key, count) |
| HUD mode cockpit-only or minimal, a build tool in hand (the number keys pick build entries), standing, any other panel open | not drawn |
| Hardpoint ship with nothing fitted | one muted slot: "Fit weapons at a shipyard" (opens nothing) |

The Spanish and English names, states and hints are in `LocArsenal.cs`; the old top-centre label and its texts are gone, and the group-change toast only shows when the bar is not on screen.

## Decisions and limits
- Group semantics stay: all lasers act together, all guns act together. Two slots of one group (a Mk1 and a Mk2 laser) are both outlined; picking either takes the group in hand. Per-module selection would need a command in the log and is not built.
- `V` and the wheel step through slots, so with Mk1 and Mk2 lasers one step can stay in the same group; the number keys go straight to a group. Say if stepping should skip to the next group instead.
- The simulation exposes no gun cooldown, so `Cooling` is never fed (the model and ring support it); "firing" is the armed state. Dry (no ammo) is shown only when the content asks for ammo per volley. Low power uses the ship's power satisfaction, no power at zero.
- "Blocked" for a laser is the existing out-of-range state (aimed deposit too far).

## How to check
`Godot --path game --resolution 1280x720 -- --arsenal-shot=path --ui-lang=en` (also 1920x1080, es) saves the laser in hand (`path.laser.png`), the marker halfway (`.ease`), the gun (`.gun`), firing (`.firing`), Minimal detail (`.minimal`), the cockpit-only HUD (`.cockpit`, no bar) and the ship from outside (`.outside`). `--cockpit-shot` shows the weapons page with the slot rows. `--hud-sweep` includes the bar in its coverage measure. Tests: `ArsenalModelTests`, `InputActionsTests`.
