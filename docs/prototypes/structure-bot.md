# Prototype: the structure bot

Status: built (2026-11-22), second balance pass the same day (see below). Checks that the structure campaign ([ADR 0018](../adr/0018-modular-structures.md) to [0022](../adr/0022-structure-economy-fixes.md)) can be played from the first tick to the win with sim commands only, and finds where it gets stuck. Code: `tools/Starfoundry.Balance/StructureBot*.cs`. Tests: `StructureBotTests`, `StructureRecoveryTests`.

## The question
Is the structure campaign completable (start in the starter ship, build a station, power it, defend it, research, explore, automate, survive the waves), and where does a player get stuck or wait absurdly long?

## How to run it
```
dotnet run --project tools/Starfoundry.Balance -- --structures --minutes 120 --seed 20261121 --trace
```
Options: `--delay=6` (game seconds after every placement, standing in for aiming and thinking), `--continue` (keep playing after the win, to see how far research and exploration get), `--no-threats` (no waves, to study the economy alone). Exit code 0 when the game was won. A run of 100 simulated minutes takes about four seconds.

## What the bot does
Sim commands only (`PlaceModuleIn`, `PlaceMachineIn`, `PlaceSegmentIn`, `Connect*`, `FoundStation`, `StartMining`, `SetThrust`, `SetWeapons`, `SetResearch`, `SetRecipe`, `Scan`, `Jump`, `AssignDrones`, `DumpItems`, `RemoveMachine`); never `GiveItems`, never `GrantResearch` (a test scans its source for both). The plan is a list of named goals in order; each waits for the items it needs (guarded by "can I afford it / is it unlocked") while a once-a-second maintenance step keeps hand mining, the fuel stock and the record going.

| Goal | What it builds |
| --- | --- |
| 1 fly and mine | Fly to the home iron deposit with `SetThrust`, hand-mine it; pause at 40 ore so the hold is not clogged |
| 2 station | `FoundStation` beside the cockpit on its -z side; later a workshop module and a reactor module (the reactor needs 8 hand-mined carbon) and an ingot line |
| 3 hydrogen | Two extra cargo modules, then ice port, cracker and a belt into the starter depot; hand-mine ice until hydrogen is made; refuel again whenever hydrogen is under 12 |
| 5 defence | Defence module on the ship (above the starter workshop) with an ammo battery (ingot port, assembler making ammo, two belts, two turrets) and two walls; weapons on. The same on the station, plus a repair bay each; a second battery on both from wave 2 |
| 4 laboratory | A workshop module with two smelter rows into one plate assembler into a lab; a second lab line after the first research |
| research | Logistics, carbon processing, drone automation (then the bays and, after the first expedition, two extra laboratories with plate lines and two reactors on the station), carbon for and then the jump drive, then (after one expedition for all the metal ore) titanium smelting, nickel smelting, the plate-only nodes, and advanced plating last; each research gets the port and belt feeds its ore costs need (raw port, port + smelter, port + smelter + assembler), taken down afterwards |
| expeditions | Jump along lanes the star map shows, scan every new system, hand-mine what is missing (every system has carbon, so the 5 carbon of each jump are mined on the way), jump home |
| drones | Two drone bay modules on the station (assigned to the home iron) and one on the ship (assigned to the ice), `AssignDrones`; a bay is taken off its deposit while ore or ice piles up in the hold (`AssignDrones` with -1) and put back when the stock runs down |

Habits a player has too: the bot dumps what the depots keep delivering when the hold sits at the mining limit, switches off the ingot lines and the starter assembler when their stock is plenty (so the ore goes to the labs), carries 45 to 60 hydrogen on a trip, packs light for it and goes only when no fight is on.

## Second balance pass (2026-11-22): before and after
Changes under test: hold 100 and depot limit 90 percent, home ice 1500 to 2000, home iron 12000 (the bot had used all 4000 by minute 50 to 60), the win needs a jump, `drone_automation` behind logistics and carbon processing (150 plates, 20 carbon), the drone bay of 16 plates and 16 ingots, interval 36 ticks (50 ore a minute at density 100). The bot builds the bays as soon as the research is done (two on the station mining the home iron, one on the ship mining the ice; it takes a bay off its deposit when 90 ore or 40 ice pile up in the hold and puts it back when the stock runs down, as a player would), and after the first expedition a reactor pair and two more laboratories with their own plate lines on the station. It brings the ore of all three metal nodes home in one expedition (100 titanium, 100 nickel, 32 quartz) and researches the plate-only nodes before advanced plating.

| Measure (seed 20261121, 6 s per action) | Before | After |
| --- | --- | --- |
| Research plates per minute | 30 to 40 | about 50 with two laboratories (before minute 45), about 90 with four |
| Drone automation researched | never (not reached in 110 min) | 32:44 |
| First drone bay, first ore delivered by drones | never | 32:45, 32:52 |
| Jump drive researched | 36:53 (33:00 without waves) | 41:37 (waves draw plates to defence; 36:13 without waves) |
| Wave 10 survived | 42:57 | 42:14 |
| **Victory** (waves, station and a jump) | 42:57 | 43:54 |
| Titanium, nickel smelting researched | 53:09, 61:08 (50:32, 63:42 without waves) | 57:43, 60:41 (50:51, 53:51 without waves) |
| Advanced plating | never (starved after the iron ran out) | 98:26 (93:17 without waves) |
| Whole non-repeatable tree (17 nodes, 3 500 plates) | never (not by 130 min) | 93:56 for the last plate-only node, 98:26 with advanced plating (no waves: 89:53 and 93:17) |
| Emergency power (a reactor with no hydrogen) | from about minute 65 to 76 on | 182 s in 130 minutes, nothing sustained |
| Hold at the mining limit blocking mining | from minute 8 | rarely: the bot dumps surplus at the limit, as before |

Other seeds (victory, whole tree without waves): seed 1 42:18, seed 2 43:17, seed 3 44:01, seed 20261120 42:54; pace 0 s per action 43:45, 3 s 43:45, 15 s 48:11, 30 s 77:09. The win moved from "the wave clock" to the jump drive, and still sits at wave 10 plus about one to two minutes. The tree is done at about minute 90 to 98, not the aimed 70: the first 40 minutes are the same (plates at 30 to 50 a minute, one lab line per 30 plates a minute is the assembler's limit) and the expedition and the station build cost about 15 minutes in which the laboratories stand still. A player who researches faster than this bot would still need roughly 3 500 plates, 7 000 ore.

Not changed in this pass (see "needs decision"): the wave table. At the win the bot has 8 turrets and had lost no module and no machine on any of the five seeds.

## Timeline before the second balance pass (seed 20261121, 6 s per action, for reference)
Game minutes, from `--structures --continue --minutes 110`. The win is first reached at 42:57; the run goes on to see how far research gets.

| Minute | Milestone |
| --- | --- |
| 0:01 | Hand mining the home iron deposit |
| 0:10 | First iron ingot in the hold |
| 0:19 | First iron plate in the hold |
| 1:48 | Station founded |
| 5:21 | Hydrogen cracker built |
| 6:05 | Hydrogen loop works: first hydrogen made from ice |
| 8:59 | First defence module built (ship) |
| 10:01 | First turret built |
| 10:15 | First turret loaded with ammo |
| 12:38 | Station powered with machines running (workshop, reactor, ingot line) |
| 15:39 | Wave 1 warning |
| 16:25 | Wave 1 survived |
| 16:26 | Laboratory built |
| 16:50 | First repair bay built |
| 20:03 | First research finished: logistics |
| 25:54 | Carbon processing researched |
| 36:53 | Jump drive researched |
| 41:51 | First jump (to system 1); the bot scans and visits ten systems in six minutes |
| 42:57 | Wave 10 survived: **victory** (ship alive, station standing) |
| 47:31 | Back home with titanium ore, nickel ore and quartz |
| 53:09 | Titanium smelting researched |
| 61:08 | Nickel smelting researched |
| 85:39 | Defeat (ship lost to wave 22, after the win) |

Waves survived (minute): 1 16:25, 2 20:00, 3 23:20, 4 26:26, 5 29:09, 6 31:51, 7 34:34, 8 37:16, 9 40:02, 10 42:57. After the first warning a wave comes every 2.5 to 3 minutes, whatever the player does. Not reached in 110 minutes: advanced plating (started at about 67:00 and starved, see below), drone automation, the first drone bay.

Other seeds and paces (victory minute, 6 s per action unless noted): seed 1 41:04, seed 2 43:24, seed 3 46:12, seed 20261120 42:02; 0 s per action 42:47, 3 s 42:56, 15 s 44:29, 30 s 54:10. The bot lost no module and no machine before the win on any of those runs. The wave clock decides the win time, not the player.

## Where the bot got stuck, or something was impossible or absurdly slow
In the order found. "Did" says what the bot or the repository did about it.

1. **Moving to a deposit with an out-of-range thrust.** A normalised thrust vector can have a squared length a little above 1024 squared and is rejected, so the ship did not move. Bot only: it scales to 1000.
2. **The hold of the starter ship is a trap (hold 100 and a 90 percent depot limit since the second balance pass).** Hand mining stops at 75 percent of the hold, the starter hold is 60 with 20 hydrogen in it, and the depots stock up to 40 percent of the capacity of each item they deliver (24 ingots, 24 plates, then 72 of each with 180 slots). Two stocked items and the hydrogen put the hold past the limit: mining stops, every ore port starves, the lab and the lines stand still while the hold is full of goods nobody uses. Seen as: carbon mining stuck at 45/60, then research at 6 percent of its speed for 30 minutes. Did: the bot builds two cargo modules (20 ingots each) at minute 2, and dumps surplus ingots, plates and hydrogen (`DumpItems`) when the hold sits at the limit, switches off the lines whose stock is high. A player has to do the same.
3. **A wave found the ship away at the ice with the defence half built.** The bot's own order (ice trip started after the first warning) lost the ship at minute 25 in an early version, and an earlier one lost the engine. Did: the defence (module, battery, walls) comes before the station reactor and the lab, and the ice trip is allowed any time (the ice deposit is 9 cells from home, inside the turrets' reach).
4. **The only engine can be shot off and could not be rebuilt (fixed).** The engine cost 6 titanium plates; a ship with no engine cannot fly to the titanium, so it stayed stranded for good (and, with the ship at the ice, it could not return to the iron: research stopped for 40 minutes). Did: the engine now costs 20 iron plates and 4 carbon (`StructureRecoveryTests`, [ADR 0022](../adr/0022-structure-economy-fixes.md) update).
5. **Ore, not time, limits everything.** Hand mining gives about 2 ore a second (iron density 100 percent). Four smelters on ore ports already ask for more, and ports seem to be served in the order the machines were built, so the starter lines and the ingot lines eat the ore while the lab line starves (observed: 90 plates took 3.5 to 7 minutes, 150 plates 4 to 16 minutes). Did: the bot switches off the starter assembler and the ingot lines when their stock is plenty (`SetRecipe` with -1), so about 30 to 40 plates a minute reach the lab. All research that costs plates (14 non-repeatable nodes: about 2 600 plates) needs about 85 minutes of that.
6. **Fuel is finite.** Every reactor burns two hydrogen a minute (four ice) wherever the ship is, whether the machines need it or not. Home ice is 600 to 800 and the hand-mined trip is 60 ice; with a ship reactor, a second one and a station reactor the home ice is gone at about minute 65 to 76 (seen at 75:50). The jump drive is researched at 37 minutes, so an ice expedition is possible, but the hold limit holds only 55 to 75 ice on the way home (the rest of the hold is hydrogen, ingots, carbon, ore), which is about 30 hydrogen, five minutes of fuel for a trip of seven. The base slides into emergency power (15 per reactor): the smelters crawl, no ingots, so even the 12-ingot ports of the next research cannot be bought ("could not afford core:nickel_ore_unloader within 40 minutes (iron_ingot 4/12)") and the drone chain that would mine ice never completes. Fixed in the second balance pass (ice 1500 to 2000, a drone bay on the ice); see "needs decision".
7. **Research stalls three carbon short.** The bot itself spent 8 carbon on a reactor after fetching exactly the jump drive's 75; the lab then waited for ever. A player notices; the bot now watches progress and mines the missing item (`TopUp`). Not a game problem, but the research panel shows no hint that the hold lacks an item.
8. **Station out of build range.** A station beside the ship on +x is out of build range (24 cells from the cockpit) for its second module. Did: it sits on the cockpit's -z side, where every module slot of a core and its neighbours is within about 21 cells. A ship that wanders off needs to come back before it can add modules.
9. **The first expedition can only start after the jump drive (minute 37) and costs six minutes.** The star map only shows archetypes after a visit, so the bot visits ten of the eleven other systems before it has seen titanium, nickel and quartz. It works (jump 7 s each, 5 carbon each, scan in range of everything, carbon in every system), but while it is away the base has only the station's two turrets: it held wave 10, which arrived while the ship was away, and the hydrogen in the hold has to cover every reactor of both structures (the ship takes its reactors with it).
10. **Waves do not wait for the research.** The win (10 waves) comes at minute 43 with nothing researched beyond the jump drive; the research that matters for the waves (turret calibration and the plating line) is not needed. After the win the waves keep coming and wave 22 at minute 85 destroys a base that has no research and an empty fuel hold. So the campaign is completable by defence alone; the research and automation path is optional and in this bot's hands slower than the waves. Since the second balance pass the win also needs a completed jump, so the jump drive research (and the earlier automation the bot does first) is on the path.
11. **Layout mistakes of the bot itself** (an overlapping battery rebuilt twice, a belt that ended beside nothing) were found by the placement rejects (`Overlap`, `OutsideStation`); the rejects are listed in the run output and the bot treats them as "stuck" so none go unnoticed.

## What was fixed (each in its own commit, with a test)
| Fix | Test |
| --- | --- |
| The engine module costs 20 iron plates and 4 carbon instead of 16 plates, 4 carbon and 6 titanium plates (a content change; `MaterialsTests`, `StructureEconomyTests` and the golden content of `StructureDefenceTests` follow) | `StructureRecoveryTests`: every module of the starter ship is rebuildable from items found at home; a ship whose engine is destroyed places a new one beside the cockpit and flies again |

## Needs decision
Decided and done in the second balance pass: the hold trap (hold 100, depots stop at 90 percent), the fuel horizon (ice 1500 to 2000), the win needing a jump, drones and automation earlier and stronger. Still open:
1. **The hold trap is smaller, not gone.** With 100 slots the 75 percent limit leaves 75 for stock, ore and hydrogen and the bot still has to dump surplus ingots and ore at the limit (about ten dumps in 130 minutes). A HUD hint "hold full of goods" or a larger cap per item would help a human.
2. **Fuel is sustainable but not free.** Home ice (1500 to 2000) is gone at about minute 120 with five reactors and a drone bay on it; after that the ice has to come from other systems (the bot can carry only 60 to 90 ice home). Options: a cheaper burn, a reactor that idles with no demand, a second ice deposit near home.
3. **Waves 0 to 10 are close to trivial (re-checked).** The bot wins with 8 turrets (two batteries of two on the ship and on the station), two walls per structure and a repair bay each, and lost no module and no machine before the win on five seeds; the win is bound to the wave clock plus the jump (minute 42 to 44), the research path only has to deliver the jump drive. If the research and automation path should matter for the waves, the first wave clock, the wave count or the early waves (0 to 6) have to change; the wave table was deliberately left alone in this pass.
4. **Research is still slow.** 17 nodes need about 3 500 plates and 7 000 ore; the bot finishes the plate-only nodes at minute 90 to 94 and advanced plating at 93 to 98 (the aim was about 70). One laboratory line makes at most 30 plates a minute (one assembler) and the expedition for the new metals costs the labs about ten minutes of standing still. Options: a faster assembler recipe, a lab that takes ingots directly, a cheaper tree (about 2 600 plates was the old estimate), or research that continues while the ship is away.

## Not tested
- A second ice or iron source for the time after minute 120 (the home deposits run out); drones on carbon; more than one deposit per structure (kept at one on purpose).
- A human's real hesitation, aiming, and the HUD; only the commands.
- Other defence layouts: the bot uses one (turrets in a defence module on top of a workshop, walls inside it).

## Construction rules (ADR 0031)
The bot was rewritten for the floor rule and the workbench start ([ADR 0031](../adr/0031-construction-rules-fabrication-bays-and-floor-building.md)). It still uses sim commands only.

What it does differently:
- **First minutes.** It hand-mines, runs the workbench (`SetShipRecipe`: a small policy picks ingots or plates), adds a fabrication bay beside the hull (module (1,0,1)) and builds the starter factory on its floor (ore port, smelter, assembler, depot; the same planar layout as the old pre-built one). It then builds the ice loop (own depot on another row), founds the station and adds cargo.
- **Layouts are planar.** Every machine and belt is on the floor layer. The ammo battery (port, assembler, two belts, two turrets) lies on the bay floor with the defence module beside it (module x + 1), the belts crossing the seam; walls stand on the defence floor. The plate line is two smelter rows into one assembler, whose output belt climbs one row into the laboratory; the lab bay has one free face for one raw feed (carbon), which is all the default plan needs. The station gets a bay at (1,0,0) with its defence module at (2,0,0).
- **Not played after ADR 0031 (played again since the rebalance of 2026-11-23, see the last section):** the science tier (`--science`, `StructureBot.Science.cs`), the ingot and plate feeds and advanced plating (three feeds into one lab) and the extra station laboratories of the second balance pass. The tests of those chains ran against the content before the rule for a while.

| Measure (seed 20261121, 6 s per action) | Before ADR 0031 | After |
| --- | --- | --- |
| Hand mining, first ingot | 0:01, 0:10 | 0:05, 0:08 |
| Fabrication bay built | (built in) | 2:12 |
| Starter factory delivering | (built in) | 5:13 |
| Hydrogen cracker, first hydrogen | 5:21, 6:05 | 6:19, 7:17 |
| Station founded | 1:48 | 8:44 |
| First turret loaded | 10:15 | 16:45 |
| Station powered with machines | 12:38 | 23:18 |
| Wave 1 warning | 15:26 | 24:36 |
| Laboratory, first research | 16:26, 20:03 | 26:41, 31:03 |
| Drone automation | 32:44 | 50:30 |
| Jump drive researched | 41:37 | 56:59 |
| **Victory** Normal | 43:54 | 61:00 |
| Victory Relaxed, Hard | not measured | 59:43, 66:58 |

The win moved about 17 minutes later on Normal. The scripted player now starts with nothing built, so its machines (and the noise that fills the alert meter, ADR 0008) come later and so do the waves: the first warning is at 24:36 instead of 15:26. The workbench was made faster than its first draft (1 s recipes, 2 s with the ship multiplier) because the first draft left the factory to minute 10 and the reactor ran dry. Level 2's start ingots went from 30 to 60 because its ice loop now has a depot of its own.

## Rebalance after the construction rework (2026-11-23)
Goal set by the owner: bring Normal back to about 50 minutes (it was 66:08 after ADR 0031 and ADR 0032), Relaxed about 45 and Hard about 55, keep Brutal unwinnable but close, keep the feeling of starting from nothing, and play the whole research tree again.

### Where the time was lost (Normal, seed 20261121, 6 s per action, content before the change)
| Finding | Evidence |
| --- | --- |
| **Ingots were the bottleneck for the first 25 minutes.** The only factory is one ore port, one smelter (2 s per ingot, 30 a minute) feeding both the depot and the plate assembler, and everything that is built before the first laboratory costs ingots (station core 36, defence module 16, turrets, walls, repair bays, bays 24 each, reactors, cargo). | The hold held 1 to 15 ingots from minute 12 to 22; the ship defence goal alone took 10 minutes (12:00 to 22:12) with plates piling up to 57. |
| **The laboratory came at 26:29.** Defence, station and cargo had to be paid first. | Wave 1 warning 24:31, first research 31:03. |
| **The waves came later too.** The alert meter fills with the noise of the machines (signature above 8), so a slower build-up moves the first warning (15:26 before the rework, 24:31 after) and with it wave 10 (55:12). | Wave 10 survived at 55:12, the jump drive only at 63:37, so the jump research was the last thing the win waited for. |
| **Research ran at about 20 plates a minute** (ore ports are served in the order the machines were built, so the starter lines ate the ore before the lab line did). | logistics (90 plates) took 4 minutes, carbon drilling started 7 minutes after it ended (the second lab was built in between), drone automation at 50:30. |
| Bot only: the stock policy switched the starter smelter off at 70 ingots even when plates (made from its ingots) were short, so builds that need plates from the hold could wait for ever once the full tree started building. | Found while playing the new late plan (`StationLabs` stuck for 20 minutes with 241 ore and 75 ingots in the hold). |

### What was changed
Content, one number: **`core:smelt_iron` takes 1 s instead of 2 s** (a smelter makes 60 ingots a minute, an iron ore port plus smelter plus depot is a real source of ingots). Everything else is as it was: the workbench (2 s per ingot, 2 s per plate on the ship), the bay price (24 ingots and 8 plates), the starter factory, the research costs and times, the deposits, the wave table and the grace period.

Why this and not the other levers tried (same bot, Normal, three seeds):
| Variant | Normal (20261121, 20261120, 777) | Verdict |
| --- | --- | --- |
| Before | 66:08, 57:54, 56:34 (Relaxed 59:46 to 54:17, Hard 64:44 to 61:31, one Hard loss) | the starting point |
| Smelting 1 s and plates 1 s | 48:42, 54:04, 52:28 | the plate assembler is not the bottleneck; it also doubles every lab line, which spreads the seeds (one at 54) |
| **Smelting 1 s** | 49:04, 48:58, 50:42 (then 48:45, 48:59, 48:53 with the bot changes below) | chosen: one number, the ingot source is what the first 25 minutes lack |
| Smelting 1 s and home iron density 150 | 50:02, 52:27, 50:25 (jump drive at minute 37) | more ore buys nothing: the win is now bound by the wave clock, not by research |

With the 1 s smelter the alert meter fills sooner (first warning 18:59 instead of 24:31), the laboratory stands at 19:04 and the jump drive at about 43 to 46. The win moved from "the research" back to "the wave clock plus the jump" as before ADR 0031. No cost, no wave number and no research cost was lowered: a human still has to build the bay, the factory, a power line, the defence and the laboratory in the same order, only the ingots arrive twice as fast.

Bot, so that it plays the whole tree again:
- **The first jump.** The win needs one jump. The expedition for the new metals takes the ship (and the noise of its machines) away from home, which holds the waves back (Relaxed on seed 777 lost seven minutes that way). The bot now makes one hop to a neighbouring system and back as soon as the drive is researched, researches plate-only nodes until the win, and goes on the expedition afterwards.
- **Two feeds on a lab.** A laboratory has four faces on the floor plane: the plate line comes in from one, a belt along row 1 ends beside it, a belt along row 2 ends beside it, the fourth face is the next bay. The bot builds two feeds at once (raw, ingot or plate feed: port, smelter, assembler) and, for a research with a third feed (advanced plating: titanium ingots, nickel ingots, quartz), builds the third when the lab has all it needs of the first and takes that feed down.
- **Extra station labs.** Two reactors and two more fabrication bays beside the station's defence module, each with the plate line of the ship's lab (`StationLabs`). They are built after the expedition.
- **The science tier** (`--science`, `StructureBot.Science.cs`) is back: the electronics branch (silicon, nickel and titanium plates), then a wing per field on the station. Engineering and physics use two fabrication bays side by side (the circuit or physics science belt crosses the seam into the laboratory), biology uses one. All planar, belts only along rows and columns.
- **Stock policy.** The starter smelter stays on while plates are short.

### Measured (seed 20261121 unless noted, 6 s per action)
| Measure | Before (after ADR 0031/0032) | After |
| --- | --- | --- |
| Fabrication bay, factory delivering | 2:12, 5:13 | 2:12, 5:13 (the workbench is as slow as before) |
| Station founded, first turret loaded | 8:44, 16:45 | 8:20, 12:16 |
| Station powered with machines | 23:18 | 15:28 |
| Wave 1 warning | 24:31 | 18:59 |
| Laboratory, first research | 26:29, 31:03 | 19:04, 30:43 (the defence is built while it researches) |
| Drone automation | 52:38 | 39:44 |
| Jump drive researched | 63:37 | 46:13 |
| **Victory** Normal | 66:08 | 48:45 |
| No waves, whole basic tree (17 nodes) | not played (advanced plating skipped, 15 nodes in about 2 hours) | 94:34 (advanced plating 93:06, drone automation 34:40) |
| Science tier on rich deposits (`--science --richness=400`) | not played | complete at 377:23 (345 on the stacked layout) |

The table with every preset and seed is in [difficulty](difficulty.md#rebalance-after-the-construction-rework-2026-11-23). On other seeds (Normal): seed 1 46:38, seed 2 47:36, seed 20261120 48:59; pace 0 s 48:10, 3 s 48:35, 15 s 59:22, 30 s 88:16.

### After flight inertia, solid deposits and hit volumes (ADR 0033, 2026-11-24)
Same seed (20261121), 6 s per action, the content with inertia (36 ticks to full speed, 18 to stop), solid deposit rocks and padded hit volumes: Normal 48:51, Relaxed 47:17, Hard 54:03 (all won; the baseline above and in [difficulty](difficulty.md) was 48:45, about 46 and about 53). The bot flies to a deposit until its laser reaches it (it cannot reach the rock's centre any more), detours around deposit rocks like asteroids, and looks for a spot for its station that is not inside a deposit's rock. See [flight feel](flight-feel.md).

Caveats: the base still falls to wave 17 or 18 after the win (about minute 66 to 76 with the compressed economy; the bot then no longer researches). With the new order (expedition after the win) titanium smelting is researched at 65:19 and nickel smelting at 69:42 on that seed, close to that limit. The science mode runs without waves.

Re-measured after rebasing onto the glazed cockpit and sampled-body collision (#77): Relaxed came out at 55:35, slower than Normal. The cause was the bot, not a rule. With solid deposit rocks the station has to stand clear of the rock (the footprint check used a 4 cell margin and a 40 by 20 by 40 box), so the ship's mining spot is about 17 cells from the build site and every placement became a round trip (47 trips against 18 before, about 9 s each, with the hand mining stopped meanwhile). Fixes, all in the bot (no rule weakened): the station spot is the clear one nearest to the ship, the footprint margin is 1 cell (the sim checks the real overlap), a leg that starts beside a rock and moves away from it no longer detours over the rock (the detour test is in 3D), and a free flight that makes no headway for 10 s (a wing pressed against a rock that the cockpit point would clear; seed 777 Normal lost 3 minutes that way) climbs out and goes on. Result, 6 s per action, all won:

| Seed | Relaxed | Normal | Hard |
|---|---|---|---|
| 20261121 | 48:34 | 49:31 | 52:36 |
| 20261120 | 48:04 | 49:20 | 55:18 |
| 777 | 46:15 | 48:42 | 53:26 |

## Weapons feel round (2026-11-24)
[ADR 0036](../adr/0036-free-fire-recoil-hold-overflow-and-resource-density.md). The bot sends no aim, so its guns fire at the enemy the simulation picks as before, and recoil never applies to it. The economy it plays is two to three times faster (laser 250 percent, drone bay 14 ticks, starter hold 400), so the wave clock was moved back (`threat.alert_max` 234000; see [difficulty](difficulty.md#weapons-feel-economy-and-resource-density-2026-11-24)). Two changes to the bot: `--density=N` plays with any resource density (the presets carry 150, 140, 120 and 100; `--richness` now keeps the density), and a flight leg that is pinned twice at the same place slides sideways (alternating sides) after climbing out, so the descent to the mining spot comes in elsewhere. On seed 777 the home iron rock still pins a wing for a while; a better approach (a spot chosen with the ship's width in mind) is the real fix.

### Pose for build trips (2026-11-25)
The pin on seed 777 came from `EnsureRange`: it flew the ship's origin to a point level with the cell to build, 8 cells short, ignoring that the ship's boxes (8 cells wide, 16 long) would then sit inside the home iron rock. It now searches the nearest pose (within 12 cells sideways, 4 below to 12 above) where no module box comes within the hull padding plus 40 units of a deposit rock and the cell is still in build range, and flies there; with a clear default point nothing changes. Result, 6 s per action, all won:

| Seed | Relaxed | Normal | Hard |
|---|---|---|---|
| 20261121 | 45:24 | 47:35 | 51:13 |
| 20261120 | 45:33 | 47:44 | 54:16 |
| 777 | 44:27 | 47:19 | 51:54 |

## Station-first opening (ADR 0039, 2026-11-26)
A ship holds no machines, belts, turrets, walls or bays, so the plan changed: `MineIron` queues crafts at the workbench (`QueueBench`, at most five waiting, one craft of 8 s at a time; `BenchPolicy` counts what is queued and keeps plates for the core, then the reactor), `FoundStation` (12 ingots, 8 plates), 8 carbon for the station reactor, the starter factory (ore port, smelter, depot, then the plate assembler) on the core floor, two more ingot lines, the ice loop on the core floor, the station defence (bay, defence module, ammo batteries on rows 4, 6, 2 and 0 of the bay: two on Normal and below, four on Hard) and a repair bay on the core floor, a second station reactor, then the laboratory bays beside the core, and the research as before. The ship only mines, carries cargo modules and drone bays and keeps its gun armed. Two bugs found on the way: hand mining never paused again after a carbon trip, and the workbench sim started the recipe that had just finished instead of the next queued one. Results are in [difficulty](difficulty.md#station-first-opening-adr-0039-2026-11-26).
