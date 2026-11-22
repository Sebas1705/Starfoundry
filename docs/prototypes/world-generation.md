# Prototype: a wider galaxy with obstacles, boost and collision damage

Status: built (2026-11-22), awaiting a human play-test. Decision: [ADR 0028](../adr/0028-world-generation-obstacles-and-collisions.md) (proposed). Builds on the [galaxy](galaxy-exploration.md), [structure defence](defence.md) and the [structure bot](structure-bot.md).

## The question
Flying was free: the home field was two seconds across and nothing in space could hurt a ship outside a wave. Can the world be bigger, varied and dangerous, with flying that matters, without breaking determinism, old logs, the winnable campaign or the scripted player's pace?

## What it adds
| Area | Before | After |
| --- | --- | --- |
| System spacing, radius | 800, 150 cells | 1800, 320 cells |
| Home deposits from the anchor | 6 to 12 cells | 36 to 60 cells |
| Other deposits from the anchor | 10 to 60 cells | 50 to 240 cells, richer the farther (star, jumps from home, distance from the anchor) |
| Systems | an archetype only | an archetype and a star type (yellow dwarf, red dwarf, orange star, blue giant, binary, pulsar) |
| Space around | empty | about 50 obstacles per system: belts, clusters, scattered rocks, nebulae, wrecks, mines, crystals, debris |
| Ship speed | 6 cells a second | 6, or 24 with boost (Left Shift) |
| Collisions | none | damage by closing speed, push-out of rocks |
| Scanner, drone range | 80, 60 cells | 300, 130 cells (wide galaxy) |

### Obstacle kinds (all content, `world.obstacles`)
| Kind | Behaviour | Numbers | Per system (average of 40 seeds) |
| --- | --- | --- | --- |
| `asteroid_small` | solid | radius 1.5 to 3.5 cells, 6 damage at base speed | 29 |
| `asteroid_big` | solid | radius 4.5 to 9 cells, 14 damage | 7.4 |
| `floating_mine` | mine | trigger 16 cells, fuse 1 s, blast 18 cells, 90 damage at the centre | 3.4 |
| `unstable_crystal` | mine | trigger 9 cells, fuse 0.4 s, blast 11 cells, 45 damage | 3.5 |
| `ion_cloud` | cloud | radius 25 to 50 cells, reactors at 40 percent | 0.5 |
| `gravity_cloud` | cloud | radius 25 to 55 cells, engines at 45 percent | 0.6 |
| `derelict_wreck` | wreck | salvage in 2 s within 12 cells at 20 units a tick or less: 6 to 14 plates, 10 to 24 ingots, 3 to 8 carbon, 8 to 20 ammo or 4 to 12 ice | 2.2 |
| `drifting_debris` | drifter | radius 1.5 to 3 cells, 12 units a tick along a line of 60 to 160 cells, 10 damage | 4 |

Star types scale the veins (90 to 160 percent) and the density of rocks (50 to 130 percent) and hazards (80 to 220 percent); blue giants need depth 1 and pulsars depth 2. Home is always a yellow dwarf with rocks and wrecks but no mines or debris.

### Guarantees that keep the campaign winnable (tested over 200 seeds)
- Home always holds 12000 iron, 1500 carbon and 1500 to 2000 ice, 36 to 60 cells out; every system is on one connected lane graph; titanium, nickel, quartz and carbon exist outside home.
- Solid rocks keep 70 cells from the anchor (the arrival point) and 60 from every deposit, mines and debris 80; two rocks leave 10 cells between their surfaces; clouds never cover an anchor. The ship is 8 by 4 by 40 cells, so a gap always exists and above the rocks is always free.
- Deposits are not solid.

## Collision damage and balance
With a turned ship ([ADR 0030](../adr/0030-ship-attitude-in-the-simulation.md)) the module boxes are tested where the attitude puts them; the rules below are unchanged.

A ship at base speed closes at 48 units a tick. Damage is `kind damage * (v^2 - 10^2) / (48^2 - 10^2)`, at least 1, at most 16 times the kind's damage, once per 60 ticks per structure.

| Impact | Closing speed | Damage to the module that hits (cockpit 150 hp, engine 200 hp) |
| --- | --- | --- |
| Scrape or slow bump | 10 units a tick (1.25 cells a second) or less | none (a bump event) |
| Small rock at base speed | 48 | 6 |
| Big rock at base speed | 48 | 14 |
| Small rock at boost | 192 | 96 (the 16x cap) |
| Big rock at boost | 192 | 224: destroys any module (the last cockpit means the ship is lost) |
| Debris against a parked structure | 12 | 1 |
| Floating mine | blast, linear falloff | 90 at the centre, about 70 next door; a ship that leaves within the fuse is safe |

Pressing on a rock at base speed costs 3 to 7 hit points a second; shields take their percent off, a `Hull` module placed in front is a cheap bumper, repair bays and passive regeneration heal. Nothing here changes the loss rule: a ship dies only when its last cockpit is destroyed.

## Measured
All runs are the structure bot (`--structures`, 6 s per action), 150 simulated minutes, before (main at 577dd82) and after. Game time of the win:

| Seed, difficulty | Before | After |
| --- | --- | --- |
| 20261121 Normal | 45:21 | 47:00 |
| 20261121 Hard | 46:25 | 48:13 |
| 20261121 Relaxed | 43:19 | 46:24 |
| 20261120 Normal | 44:42 | 45:07 |
| 20261120 Hard | 50:47 | 47:56 |
| 20261120 Relaxed | 41:30 | 42:39 |
| 777 Normal | 44:16 | 44:12 |
| 777 Hard | 45:35 | 51:16 |
| 777 Relaxed | 41:50 | 41:37 |
| 20261121 Brutal (loses before and after) | lost at 41:50, 10 waves | lost at 36:28, 7 waves |
| 20261121 Normal at 30 s per action | 77:09 | 80:37 |

- The bot flies 151 s of game time on Normal 20261121 (it boosts on legs over 30 cells and hops over rocks and mines it would cross; it never salvages). The win moves by 0 to 3 minutes on Normal because home trips are longer, the base is farther from the iron and the ice, and every delay moves the signature-driven wave clock. Hard 777 is the outlier (+5:40): the first expedition leaves 3 minutes earlier than before and a ship away from home does not feed the alert meter, so waves 11 and 12 come later.
- The bot's hand-mining spot, station and ice trips work unchanged; modules lost 0 on Normal (2 on Hard 20261120).
- Running 150 minutes takes about 7 s instead of 3.5 s (a collision pass per tick).
- Tests: 1227 (about 45 new): `WorldGenerationTests`, `CollisionTests`, `ObstacleTextsTests`, plus the version, rules-flag and legacy tests.
- Screenshots (headless `--obstacle-shot`) of the belt, a rock beside the ship, a mine plain and armed, an ion and a gravity cloud from inside and outside, a wreck, boost and a rammed rock with the red flash were looked at; see the client notes below.

## Client
`game/ObstacleModels.cs` builds procedural models in the style of the deposits (cached per kind and seed): lumpy rocks, a spiked mine whose light blinks faster when armed (and a blast sphere while the fuse burns), a violet crystal cluster, cyan ion and purple-orange gravity nebulae, a torn wreck with a beacon that goes dark when salvaged, tumbling plates. `ObstacleLayer` shows the current system only and moves drifters each frame. Left Shift boosts (a key row only when the content has boost). The hazard line appears only when something is near (rock or debris within 30 cells, a mine in range with the seconds to the blast, a cloud with its effect). Impacts give a red flash and a short shake scaled by damage and a toast ("Collision: -96 to Cockpit"), explosions and salvage have toasts, and four procedural sounds were added (rock thud, mine beep, salvage chime, boost whoosh). All texts are in `Loc` in Spanish and English.

## Known limitations and not done
- The mining laser does not break small rocks, rocks give no ore, and there is no manual module repair command (modules heal through repair bays and slow regeneration; the `Repair` command is for machines).
- Real keyboard input (Shift), the sounds, the explosion effect and the star type in the star-map line were not verified by a human; the scene shots were.
- Stations are only hurt by mines and debris, and enemies ignore obstacles.
- The wave appears 70 cells from the base in the wide galaxy (from the anchor in the legacy one).
- Legacy worlds (worlds without the structure rules, logs older than version 10) keep the compact galaxy.

## Run it
```powershell
dotnet test
dotnet build game -c Debug
dotnet run --project tools/Starfoundry.Balance -- --structures --minutes 150 --seed 20261121 --difficulty normal
```
