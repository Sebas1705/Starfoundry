# Prototype P10: the wave ramp

Status: done, awaiting a human play-test (2026-11-20). Decision: [ADR 0010](../adr/0010-waves-can-start-later-and-the-ramp-is-measured-per-turret.md) (proposed). Builds on the [balance pass](balance-pass.md) and [threat and combat](threat-and-combat.md).

## The question
After the balance pass, one fed turret and the ship weapon held every wave for an hour. A defence only matters if the threat asks for more of it over time.

## What changed
| Area | Change |
| --- | --- |
| Content format | A wave line can have `from_wave`: it holds `base + per_wave * (wave - from_wave)` enemies from that wave on, none before. Part of the content hash |
| Enemies | New **brute**: 300 hit points, speed 10, damage 30 every 40 ticks, range 5. One arrives from wave 4, a second from wave 8, a third from wave 12 |
| Defence | Turret damage 8 to 12; turret ammo buffer 20 to 60 shots; enemy cap 24 to 60 |
| Game | The brute is shown as a scaled-up striker with its own name (es "bruto"); a model of its own is future work |
| Balance simulator | `--turrets=N` builds N fed turrets, each with its own drill, smelter and assembler line (extra turrets cost plates, so the lab goes back to a depot after Logistics); `--waves` prints one row per wave; the ship retreats to the base after the first turret, since at the deposit it would be out of the turrets' reach |

```powershell
dotnet run --project tools/Starfoundry.Balance -- --delay=15 --observe=240 --minutes=300 --turrets=3 --waves
```

## What the bot found along the way
- The first attempt (a brute count growing every wave) made extra turrets nearly useless: each wave added 300 hit points, much more than one more turret adds. Constant steps worked.
- Turrets sharing one ammo line did not scale: one drill and smelter make 2 shots a second, one turret's full rate. Each extra turret needs its own ammo supply, which is the logistics link the design wants.
- A bot that left its ship at the deposit lost it again and again, out of the turrets' range. A rule of the scripted player now, a lesson for the interface (a hint to keep the ship near the defence is worth adding).
- More machines raise the signature, so waves come faster: wave 1 is at about 12 minutes with one turret line, and waves arrive every 6 minutes with one turret, 5 with two, 4 with three.

## Results
First machine lost, as the wave number (1 is the first wave). Delay 15 s per action, every extra turret with its own ammo line, seeds 1 to 4 and the default seed.

| Fed turrets | Default seed | Seeds 1, 2, 3, 4 |
| --- | --- | --- |
| 1 | 9 | 12, 11, 10, 11 |
| 2 | 9 | 11, 8, 10, 12 |
| 3 | 13 | 15, 13, 16, 15 |
| 4 | 16 | not run |

With one turret the first loss comes about an hour in (wave 9 at the default seed was spawned at 63:38). Three turrets hold two to five waves longer than one on every seed, four hold the longest.

## Targets pinned by tests
One fed turret is overrun between the 8th and the 14th wave (seeds 20261120, 1, 2); three fed turrets hold at least two waves longer than one (same seeds). The earlier targets still hold: the first wave comes at least two minutes after the turret is armed and a defended base loses nothing in the first 25 minutes. 290 tests.

## Known limitations
- Two turrets were no better than one on the default seed and on seed 2: the direction of a wave decides which machines are in reach, and the effect is noisy. The targets compare one against three for that reason.
- Still a scripted player: it never repairs, never rebuilds a lost machine and never moves the ship in a fight. A human can hold longer (dodging, rebuilding) or shorter (no ammo planning).
- The brute has no model of its own and a high-hit-point target is the only reason to build more than a few turrets: no walls, repairs, shields or other tools yet.
- Seed 5 cannot place the extra ammo lines (a drill out of reach of the home deposit), so ramp tests use other seeds.
- Waves beyond about 25 and the carbon, exploration and jump parts of the economy remain unmeasured.
