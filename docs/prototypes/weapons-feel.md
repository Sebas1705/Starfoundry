# Prototype: weapons feel, hold overflow, regrowth and resource density

Status: built, awaiting a human play-test (2026-11-24). Decision: [ADR 0036](../adr/0036-free-fire-recoil-hold-overflow-and-resource-density.md). Spec: [pilot feedback round](../superpowers/specs/2026-11-24-pilot-feedback-round-design.md), part 1 sections W, S9 and M and part 2 section G. Builds on the [mining laser](mining-laser.md), [weapons and shipyard](weapons-shipyard.md) and [flight feel](flight-feel.md) prototypes. Code: `src/Starfoundry.Sim/BeltNetwork.Feel.cs` (projectiles, recoil, pickups, regrowth), `ResourceGenerator.cs` (extra deposits), `Commands.Feel.cs` (`SetAim`), `game/Play.Weapons.cs` and `game/Play.FeelShot.cs`. Tests: `FeelTests`.

## The question
Can the owner pick up a weapon, fire it whenever he likes, feel it, and mine without the hold getting in the way, with the galaxy giving him more to find, while the old logs and the scripted player stay exactly as they were?

## What changed
| Area | Change |
| --- | --- |
| Controls | `M` (and `V`) cycles the weapon in hand, the mining laser or the guns; the left click fires it. No stow or deploy step: the laser is in hand whenever it is the group in hand. The HUD (top centre) says "In hand: ..." and the key panel shows the group and what the click does. The Mine and Weapons buttons and the cockpit weapon card take a group in hand. |
| Aim | With the guns in hand a short bright barrel from the nearest gun, and a faint guide, point where the crosshair does (brighter while firing); the laser gets the same from its emitter. |
| Free fire | Hold the click: the game sends `SetAim` (the crosshair ray, converged on the first deposit, enemy or obstacle it meets, in the ship's frame) and arms the guns. They fire every interval with or without an enemy. Settings has an optional "Aim assist" (an enemy within five degrees of the crosshair is aimed at; off by default). |
| Projectiles | Simulation entities (cap 64, 150 units a tick, 40 ticks of life), tested against enemies, mines, debris, wrecks, rocks and deposits; the first thing on the path takes the hit. Mines go off, debris breaks up after about five volleys, the rest throws sparks. |
| Recoil | `recoil` per gun module (content): the main gun 0, the Mk2 3000, the laser none. The volley pushes the ship opposite to the shot, scaled by strength and mass; the camera kicks (small for the main guns). |
| Ammo | Optional: `ship_weapon.ammo_item` and `ammo_per_volley`; none in the shipped content. A dry hold clicks and says so. |
| Hold | The starter cargo module holds 400 (was 100). Past 75 percent hand mining takes four times as long per item; with a full hold the laser still breaks the rock and the surplus floats as pickups (one toast per rock), pulled in by a ship with room within about six cells, fading after three minutes. Drones still wait at 75 percent. |
| Yield | Mining laser Mk1 250 percent (was 100), Mk2 425 (was 170); drone bay 14 ticks an item (was 36). |
| Regrowth | An emptied deposit regrows 12 percent of its first size (at least 10 items) after twelve minutes, once no ship or station is inside its rock and eight cells. |
| Density | `ResourceDensityPercent`: Relaxed 150, Normal 140, Hard 120, Brutal 100 (default 140). Above 100, extra deposits are appended after the base ones: more veins of what the archetype holds (home ones 30 percent bigger) and small ore rocks beside belts and clusters, each 44 cells from the others. |
| Mining readout | The laser card of the cockpit weapon display shows items a second and what is left in the aimed or mined deposit, or says that the hold is full and slow. |

## Rules in numbers
- A volley's damage is what it always was: `ship_weapon.damage` (6) times the structure percent (150) times the research bonus, times the summed strength of the guns. A projectile hit costs the same as the old instant hit.
- Recoil impulse per volley, in thousandths of a unit per tick: `sum(recoil * strength / 100) * 8 / modules` along the unit vector opposite to the shot (the top speed is 48000). A Mk2 gun (3000 at 170 percent) on the 7-module starter gives about 5800, 12 percent of the top speed.
- Hand mining: 15 ticks an item at density 100, divided by the laser percent (250: 6 ticks, five items a second), times four past 75 percent of the hold.
- Extra deposits at density 140 on the main test seed: 47 base deposits and 33 extra (the veins are 40 percent more, the rest are small ore rocks); at 100 the galaxy is the old one.

## Measured
Scripted player ([structure bot](structure-bot.md), 6 s per action, `--structures --difficulty=<preset> --seed N`), minutes to the win, after the faster economy of this change and the wave clock moved back to where it was (first wave alert capacity 234000, was 216000):

| Preset (goal) | Seed 20261121 | Seed 20261120 | Seed 777 | Before (main, 20261121) |
| --- | --- | --- | --- | --- |
| Relaxed (8) | 45:16 | 45:34 | 44:28 | 48:34 |
| Normal (10) | 47:36 | 47:59 | 52:44 | 49:31 |
| Hard (13) | 51:51 | 53:57 | 57:16 | 52:36 |

The first runs with the new economy and the old wave clock won Normal at 45:49 on the main seed (first wave warning 17:28 against 19:04): a richer start builds the noisy machines sooner and the alert meter fills faster. The capacity of the meter is the lever (`threat.alert_max`); the rules and the research costs are untouched. Seed 777 is slower on every preset: the bot's approach to the home iron rock pins a wing against it for minutes on that seed (it now slides sideways after the second pin, which took Normal from 56:53 to 52:44), so its waves come later because the bot builds less in the meantime; Hard on that seed wins at 57:16, 1:16 over the band. The numbers are in [difficulty](difficulty.md#weapons-feel-economy-and-resource-density-2026-11-24).

## Screenshots looked at
- `--gun-shot`: the seat with a tracer leaving the gun along the crosshair and no enemy in the sky (a thin bright streak and the barrel guide; the rocks ahead sit behind the nose laser's dome, so the impact is easier to see from outside); `path.outside.png` shows three tracers in flight between the gun and the rocks. The impact sparks are small at 24 cells; see the caveats.
- `--overflow-shot`: the nose laser's beam on a small rock with the hold at 400/400, the rock's count falling, the toast "Hold full: the laser still breaks rocks and the surplus floats..." and "Hold full: the surplus floats beside the rock...", the red hold bar. The loose ore is a small amber ball beside the rock; a larger size was set after the shot.

## Not measured, not done
- A heavy cannon module (the recoil data allows it; a new module kind needs a model, a designer entry and a hull profile, which other changes were editing) and missile launchers.
- A flight assist switch for linear drift: the simulation brakes towards the commanded velocity, so a recoil kick is always corrected by the engines (more slowly than a thrust change; the Mk2 gun's kick lasts about a second).
- Hearing it: the impact and pickup sounds reuse existing effects (rock thud, module hit, mine pop).
- A human hand: the feel of the kick, the aim assist cone and the converge distance (60 cells when nothing is under the crosshair) are numbers in `Play.Weapons.cs` waiting for the owner's hands.
