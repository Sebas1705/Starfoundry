# 0027. The mining laser is a simulation rule: a trigger, a power draw and research

- Status: proposed
- Date: 2026-11-22
- Area: simulation, content, gameplay

## Context
Mining is done by firing the ship's mining laser ([prototype](../prototypes/mining-laser.md)): the client sends `StartMining` while the click is held and `StopMining` when it is released. Three things were left open there: the laser has one fixed rate (2 ore a second, scaled by deposit density), it draws no power, and "firing" lives only in the client, so a replay only knows the start and the stop.

The simulation is deterministic and integer only ([ADR 0002](0002-deterministic-simulation-core.md)). Old command logs must replay as they were recorded, and the golden checksums of legacy worlds must not move.

## Decision
- **The trigger is state.** `Player.LaserReleased` and a new command `FireLaser(bool Firing)`. A mining order (`MiningDeposit`) can stand with the trigger released: nothing is mined, no power is drawn, and the partial item is kept. `StartMining` pulls the trigger (as it always meant), `StopMining`, a jump, a depleted deposit and the loss of the ship clear both the order and the trigger. `FireLaser` is rejected (`InvalidArgument`) without a mining order. The client now releases the trigger when the button is let go and keeps the order; a new deposit is a new `StartMining`.
- **Rate through research.** A new bonus kind `mining_rate` (percent, appended to `BonusKind`). Ticks per item are `MineTicksPerItem * 10000 / (density * (100 + bonus))`, at least 1. With no bonus this is exactly the old `MineTicksPerItem * 100 / density`, checked for every deposit. Two new nodes in the Basic field (plates and carbon only, so they are deliverable by belt like the rest): `core:laser_focusing` (60 plates, requires logistics, +25 %) and `core:beam_collimator` (100 plates and 20 carbon, requires laser focusing and carbon processing, +40 %). Together 2 ore a second becomes 3.3 (a 15-tick item takes 9). Density still scales the result. The ticks per item follow the bonus at once; progress already made is kept and cannot exceed the old limit, so a research that finishes while mining is safe.
- **Power draw.** While a laser would mine this tick (a mining order, trigger pulled, deposit holds ore, in range, hold below its mining limit), its ship's demand grows by `PlayerRules.LaserPowerUse` = 5 (a smelter asks for 10, a laboratory for 20). Only ships with a reactor module draw it, under the structure rules, so hand-built test ships and legacy flows are unchanged. The draw is shown in `PowerStatus.Demand`.
- **The laser is served first.** A brownout slows the machines but not the laser: the machines see the production left after the laser's share (`(production - laser) * 1000 / others`), and the laser advances on every tick as long as production covers its draw. Only a ship whose production is below the laser's draw slows it, with the same work-credit idiom as machines (`Player.LaserCredit`, advance on credited ticks, so progress stays whole ticks); with no production at all (a reactor with no fuel and no emergency output) it stops. Reason: the recovery loop of [ADR 0022](0022-structure-economy-fixes.md) (emergency power, hand-mined ice, cracker) needs hand mining at emergency output. Measured before this rule: with the laser in the shared pool, a base at 30 of 200 power mined at 15 percent and the scripted player starved of fuel for hours.
- **Compatibility.** Three layers:
  - The command log version is 9. `FireLaser` is tag 37 and cannot appear in logs of versions 5 to 8 (the reader refuses it).
  - `WorldRules.LegacyLaser` (a bit of the rules flags, no format version change, off in every preset and in the new-game screen): the laser draws no power. A log older than version 9 is read with it on, so it replays as it was recorded. It is mixed into the checksum only together with other non-default rules (a world whose only non-default rule is `LegacyLaser` hashes as default rules).
  - State is mixed only when set: `LaserReleased` (a marker), `LaserCredit` and `Structure.LaserDraw` when non-zero. Worlds that never fire a laser, or fire it without power consequences, hash as before (the golden checksum tests, on the stripped content without the two research nodes, are unchanged).
- **Not a difficulty rule.** The draw and the rates are fixed in the simulation like the other player rules; the world rules only carry the legacy switch.

## Consequences
- Benefits: the weapon has numbers a player can improve; mining takes part in the power budget; a replay or save reproduces exactly when the player fired; no old log or golden checksum moves.
- Costs: a starter ship near its power limit runs a little slower while mining (5 of 100 to 200 produced); the scripted player's win times moved by under a minute on five of six measured runs and the sixth got faster ([prototype](../prototypes/mining-laser.md)). `FireLaser` adds one command, one tag and two player fields to every consumer of the log.
- How it is checked: `MiningLaserTests` (trigger, codec, version 9 and older logs, legacy replay, draw, brownout, starved laser, rate and research, checksum idioms, strings), the golden checksum tests, `StructureBotTests` (the whole basic tree now includes the two nodes), `ScienceFieldsTests` (tree shape).
- Not decided here: laser range upgrades, a second laser module, drones using the same bonus, a toast or HUD line for a starved laser, balancing the draw for a human player.

## Alternatives considered
- **A. A shared brownout for the laser (rejected after measuring).** The laser takes the structure's satisfaction like a machine. It breaks the recovery loop above and moved the scripted player's win by up to four minutes; the chosen priority keeps both.
- **B. A rule flag that defaults to on for new worlds and a version gate in the reader (chosen as `LegacyLaser`, inverted).** A flag that defaults to off for new worlds would make `Normal` differ from `Default`, which a test and the log header idiom forbid; the inverted flag keeps `Default` as the new game.
- **C. Firing as an implicit consequence of Start/Stop only.** Rejected: nothing could keep a partial item across a short release, and the power draw would need the client's intent anyway.
- **D. Laser power use as content.** Rejected for now: it would change the content hash and need a golden strip of its own; a constant in `PlayerRules` is read in one place.
