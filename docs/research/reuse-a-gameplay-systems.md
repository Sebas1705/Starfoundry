# Reuse study A: gameplay systems and open-source games or libraries

Researched 2026-11-24. Scope: which existing open-source games and libraries could replace or shorten code we have written (or are about to write) in the simulation, structures, galaxy, factory, flight, threat and inventory layers. Companion studies cover other areas.

Method: repository metadata and license files read through the GitHub API (`gh api`); READMEs read for the main candidates; Starfoundry's own docs and code read first ([vision](../00-vision.md), [architecture spec](../specs/09-technical-architecture.md), ADRs 0002, 0016, 0018, 0028). Downloaded code was never run and nothing was cloned. See "What I could not verify" at the end.

## Bottom line

1. **Almost nothing can be dropped in.** Our simulation is a deterministic, integer-only library with no dependencies (the `Starfoundry.Sim` project has no package references). Every serious candidate is either floating point, tied to its own engine or ECS, or copyleft (GPL/AGPL), so it can be a design reference but not copied code.
2. **The open-source field around our exact combination is thin.** No open-source Godot or C# game combines a first-person modular ship, a belt factory and a deterministic command-log simulation. The closest code lives in Space Station 14 (MIT code, but welded to its own engine) and Mindustry (GPL, Java).
3. **The real reuse opportunities are ahead of us, not behind us:** co-op networking and rollback (LiteNetLib, Klotho), offline balance tooling (FactorioLab), and the Godot built-ins we may be re-implementing in the game layer. Behind us, the code is written, tested (24k lines of tests) and bound by golden checksums ([ADR 0028](../adr/0028-world-generation-obstacles-and-collisions.md) says old logs must replay as recorded), so swapping a core part for a library costs more than it saves.
4. **Do not start a rewrite on a library.** Replacing our fixed-point quaternion or collision with a third-party stack would move every golden checksum.

## Summary table

Effort saved is my estimate in developer-days, assuming a solo developer; "0" means nothing worth saving. Verdicts: ADOPT (dependency), ADAPT (port with attribution, license allows), LEARN (design reference only), IGNORE.

| Our subsystem | Best candidate | License | Verdict | Effort saved |
|---|---|---|---|---|
| Deterministic fixed-point math (quaternions, bounds) | [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) | MIT | LEARN now; ADOPT only for brand-new math (never swap existing) | 0 now, 3-5 d for future trig/geometry |
| Command log, replay, saves | [Klotho](https://github.com/xpTURN/Klotho) (replay, snapshots); OpenRA (design) | Apache-2.0; GPL-3.0 | LEARN (we already have it) | 0 |
| Co-op networking (future, spec 07) | [LiteNetLib](https://github.com/RevenantX/LiteNetLib) transport; Klotho for rollback | MIT; Apache-2.0 | ADOPT LiteNetLib when co-op starts; evaluate Klotho parts | 15-25 d |
| ECS / entity storage | [Arch](https://github.com/genaray/Arch), [Friflo](https://github.com/friflo/Friflo.Engine.ECS) | Apache-2.0; MIT | IGNORE (our SoA sim exists and is fast) | 0 |
| Power network (reactors, batteries, loads) | SS14 `Pow3r` solver; Mindustry power graph | MIT (SS14 code); GPL-3.0 | LEARN (SS14 ADAPT only for a specific algorithm, with attribution) | 1-2 d |
| Docking, shuttle grids | [Space Station 14](https://github.com/space-wizards/space-station-14) `DockingSystem` | MIT code | LEARN | 1-2 d |
| Modular ship/station structures, designer | SS14 grids; Star Ruler 2 designer | MIT code | LEARN | 0 |
| Personal/ship inventory (in progress) | [GLoot](https://github.com/peter-kish/gloot) UI and data model; SS14 `Storage` | MIT; MIT | LEARN; ADAPT UI ideas only | 2-4 d |
| Galaxy, systems, lanes, obstacle fields | Pioneer, Naev, Endless Sky (all GPL) | GPL-3.0 | LEARN | 0-1 d |
| Lane graph geometry (optional) | [DelaunatorSharp](https://github.com/nol1fe/delaunator-sharp) | MIT | IGNORE unless lane quality becomes a complaint | 0-1 d |
| Oriented collision, fixed-point quaternions | Jolt, Bepu, Godot physics | MIT; Apache-2.0 | IGNORE for the sim (float); fine for client-only effects | 0 |
| 6-DOF flight with inertia and flight assist | [godot-simplified-flightsim](https://github.com/fbcosentino/godot-simplified-flightsim) | MIT, GDScript | LEARN | 0 |
| Factory layer: belts, splitters, machines | Mindustry, shapez (GPL); OpenTTD (GPL-2) | GPL | LEARN | 0 |
| Recipes and ratio checking | [FactorioLab](https://github.com/factoriolab/factoriolab) | MIT, TypeScript | ADOPT as an offline tool (not a dependency) after a data adapter | 2-4 d of balance work |
| Research tree, science fields | Unciv (MPL-2.0) | MPL-2.0 | LEARN | 0 |
| Waves, threat director | Mindustry `Waves`/`SpawnGroup`; Valve L4D director talk | GPL-3.0; n/a | LEARN | 0 |
| Difficulty rules and presets | none found | - | nothing reusable | - |
| Scripted balance bot, scenarios, goals | none found | - | nothing reusable | - |
| Data-driven JSON content with content hash | none found as a library | - | nothing reusable | - |

Total realistic avoided work if every ADOPT happens: about 20-30 developer-days, mostly the future co-op transport. That is small next to the 60k+ lines already written, and it is the honest answer to "can we avoid programming this 20 times".

## Candidates in detail

Ordered by relevance. License facts come from the repository license files or metadata, quoted by name only. A GPL, AGPL or CC-BY-SA source cannot be copied into this MIT repository (see the rule in [AGENTS.md](../../AGENTS.md): no copying of names, lore, art, text or exact numbers either).

### Space Station 14 (SS14)
- URL: <https://github.com/space-wizards/space-station-14>; engine <https://github.com/space-wizards/RobustToolbox>
- License: content repository code MIT (`LICENSE.TXT`, README). Assets mostly CC-BY-SA 3.0, some CC-BY-NC-SA, so never take art or sound. The engine's `legal.md` says code contributed before 2019-03-13 is GPL-3, after that MIT, with one contributor relicensed; mixed history means engine files need individual checking.
- Language/engine: C#, its own engine (RobustToolbox) with an ECS (`EntitySystem`, components, YAML prototypes). Very active (last push 2026-11-24, about 3.9k stars). Mature, huge.
- Overlap: power (`Content.Server/Power`, including a `Pow3r` solver with a battery ramp solver), shuttles and docking (`Content.Server/Shuttles/Systems/DockingSystem.cs`), `Storage`, `Inventory`, `Construction`, `Atmos`, ship grids with physics.
- What it would save: nothing as code. The MIT license allows porting a single algorithm with attribution, but every file depends on RobustToolbox types, uses floating point and a physics engine, and is not deterministic across machines.
- Integration effort: L for any real port; S to read as a design reference.
- Risks: determinism (floats), coupling to the engine, attribution burden, asset licenses.
- Verdict: **LEARN**. Worth reading for how it models power supply, demand and battery charging fairly (compare with our [ADR 0019](../adr/0019-power-and-fuel.md) solver), docking ports and grid storage for the inventory work. ADAPT a single algorithm only if our own version shows a bug, and then with an attribution comment.

### Mindustry
- URL: <https://github.com/Anuken/Mindustry>. License: GPL-3.0. Java. Very active (last push 2026-11-18, about 30k stars).
- Overlap: conveyors, power graph, schematics (blueprints), wave spawning (`Waves.java`, `SpawnGroup.java`: begin, end, spacing, max, unit count scaling per wave, shield scaling), pathfinding.
- Verdict: **LEARN** (GPL, Java). The `SpawnGroup` idea (data rows with begin/end/spacing and linear scaling) mirrors what our wave ramp ([ADR 0010](../adr/0010-waves-can-start-later-and-the-ramp-is-measured-per-turret.md)) already does. Do not copy its numbers.

### shapez (shapez.io)
- URL: <https://github.com/tobspr-games/shapez.io>. License: GPL-3.0. JavaScript. Last push 2026-09-07.
- Overlap: belts, splitters, blueprints, goals. Verdict: **LEARN**, low value; its belts are 2D and our sim already handles 10,000 items at about 0.06 ms per tick.

### OpenRA
- URL: <https://github.com/OpenRA/OpenRA>. License: GPL-3.0. C#, very active, about 17.8k stars.
- Overlap: deterministic lockstep, orders as the only input, replays, sync hashes, data-driven rules in text files. This is the closest C# prior art to our command log and replay ([ADR 0003](../adr/0003-commands-as-the-only-way-to-change-the-world.md), [ADR 0016](../adr/0016-saves-are-the-command-log-replayed.md)).
- Verdict: **LEARN** only; GPL forbids copying. The lockstep and sync-hash details are from my background knowledge, not verified in this session (a wiki page I opened did not describe them).

### Klotho
- URL: <https://github.com/xpTURN/Klotho>. License: Apache-2.0 (permissive, compatible with MIT). C#, supports Godot 4.4+ (.NET) and Unity, addon folder `addons/klotho/`. Last push 2026-10-31, about 88 stars, 113 commits, no published releases or packages.
- What it is: deterministic framework with 32.32 fixed point (`FP64`), Xorshift128+ RNG, its own ECS, physics and navigation, lockstep, rollback and server-driven modes, replay with seek, hash comparison, LiteNetLib transport.
- Overlap: networking and replay for the future co-op ([spec 07](../specs/07-multiplayer.md)); also a second implementation of everything our sim does (fixed point, RNG, replay).
- Risks: young, one maintainer; it expects to own the simulation loop and ECS, while our sim has its own command layer and checksums; adopting it whole means rewriting the sim. Adopting only the transport and hash-compare ideas is cheap.
- Integration effort: M for selected parts, L for the full framework.
- Verdict: **LEARN now**; revisit as a parts donor (ADAPT with Apache attribution) when co-op work starts. Most of what we would want is already in our command log.

### LiteNetLib
- URL: <https://github.com/RevenantX/LiteNetLib>. License: MIT. C#, active (push 2026-11-24, about 3.6k stars). Reliable and ordered UDP.
- Overlap: transport behind the interface that spec 09 already reserves.
- Verdict: **ADOPT** when co-op starts (effort S-M, saves roughly 10-20 days against writing a reliable UDP layer). Alternative: Godot's built-in high-level multiplayer (ENet), which needs no dependency. Not yet decided; that is an architecture choice for an ADR.

### FixedMathSharp
- URL: <https://github.com/mrdav30/FixedMathSharp>. License: MIT. C#, NuGet package, engine-agnostic, active (push 2026-11-23, 97 stars).
- What it is: Q32.32 `Fixed64`, vectors, matrices, `FixedQuaternion`, bounds (box, sphere), deterministic random streams, trigonometry.
- Overlap: our `AttitudeRules` (quaternions at scale 2^14 in `int`), obstacle and oriented collision math, `Units`.
- Why not swap: ours is integer-scaled and tested to the checksum; replacing it would move golden checksums and break old logs and saves. It would also add a package to a library that has none.
- Verdict: **LEARN** (compare trig and sqrt tables and rounding rules with ours). **ADOPT only for new, separate features** (for example orbital or lane geometry) if we need more math than we have. Not benchmarked, not tested by us.
- Alternatives seen and rejected: [FixedMath.Net](https://github.com/asik/FixedMath.Net) (archived 2018), [FixedPoint-Sharp](https://github.com/RomanZhu/FixedPoint-Sharp) (last push 2021).

### Physics: Jolt, Godot built-in, Bepu, Jitter2
- Jolt: <https://github.com/jrouwe/JoltPhysics> (MIT, C++, very active). Release notes mention a cross-platform deterministic build option, but Godot's integration does not give us determinism across our own state, and our collision lives inside the integer sim. Use Godot's built-in Jolt (or the [godot-jolt addon](https://github.com/godot-jolt/godot-jolt), MIT) only for client-side effects such as debris or camera, never for state.
- [Bepu v2](https://github.com/bepu/bepuphysics2) (Apache-2.0, C#): its own Q&A says determinism holds on one machine and not across computers, and that fixed point "would be a pretty enormous undertaking". That rules it out for the sim.
- [Jitter2](https://github.com/notgiven688/jitterphysics2) (MIT, C#): float, not examined further.
- Verdict: **IGNORE** for the simulation.

### ECS libraries: Arch, Friflo, DefaultEcs, Entitas
- [Arch](https://github.com/genaray/Arch) (Apache-2.0, active), [Friflo.Engine.ECS](https://github.com/friflo/Friflo.Engine.ECS) (MIT), [DefaultEcs](https://github.com/Doraku/DefaultEcs) (MIT-0, last push 2024), [Entitas](https://github.com/sschmid/Entitas) (MIT, last push 2023).
- Verdict: **IGNORE**. Our `BeltNetwork`/`SimWorld` partial classes are already a data-oriented design with stable integer IDs and golden tests; an ECS would be a rewrite with no new feature.

### Pioneer, Naev, Endless Sky, Vega Strike, Oolite
- Pioneer: <https://github.com/pioneerspacesim/pioneer>, GPL-3 (README badge and `licenses/`), C++, active. Procedural systems, ship fitting, flight model.
- Naev: <https://github.com/naev/naev>, GPL-3 (`LICENSE`: GPLv3 for source, Lua for missions), C, active.
- Endless Sky: <https://github.com/endless-sky/endless-sky>, GPL-3.0, C++, active. Ship and outfit data in text files (capacity, mass, thrust, energy), which is a good model for data-driven modules.
- Vega Strike: <https://github.com/vegastrike/Vega-Strike-Engine-Source>, license field not asserted by GitHub (not read).
- Verdict: all **LEARN**. They are 2D or float C++/C, none is first-person or factory, and GPL excludes copying. Galaxy and obstacle generators here are small (our `Galaxy.cs` is 267 lines, `ObstacleGenerator.cs` 268), so little is lost by not reusing.

### Star Ruler 2
- URL: <https://github.com/BlindMindStudios/StarRuler2-Source>. README: "source code is licensed as MIT, art assets are licensed as CC-BY-NC 2.0". C++ plus AngelScript, last push 2023-11-18.
- Overlap: ship design from parts, empire logistics. Not examined in code. Verdict: **LEARN** only (C++, 4X, stale, art is non-commercial).

### Unciv
- URL: <https://github.com/yairm210/Unciv>. License: MPL-2.0 (file-level copyleft). Kotlin, active.
- Overlap: JSON tech tree and data-driven effects ("uniques") with mod support. Verdict: **LEARN**; our research tree and [ADR 0013](../adr/0013-defence-upgrades-are-research-bonuses.md) already are data.

### OpenTTD
- URL: <https://github.com/OpenTTD/OpenTTD>. License: GPL-2.0 (`COPYING.md`). C++. Cargo flow ideas only. Verdict: **LEARN**, low.

### FactorioLab
- URL: <https://github.com/factoriolab/factoriolab>. License: MIT. TypeScript/Angular, active (push 2026-11-24, about 880 stars). Ratio calculator for Factorio-like games, with data files per game.
- Use: a web tool to inspect production ratios of our recipes and to compare against the output of the scripted bot ([balance pass](../prototypes/balance-pass.md)). Needs a converter from our content JSON to its dataset format.
- Verdict: **ADOPT as an offline tool** (not linked into the game), effort M for the adapter. Unverified: whether its data model fits our one-cell machines and multi-output recipes.

### Inventory and AI add-ons for Godot
- [GLoot](https://github.com/peter-kish/gloot): MIT, GDScript, universal inventory for Godot, last push 2026-07-14, about 1k stars. [wyvernbox](https://github.com/don-tnowe/godot-wyvernbox-inventory): MIT, GDScript, action-RPG style. [inventory-system](https://github.com/expressobits/inventory-system): MIT, C++.
- Overlap: our personal and ship inventory is in progress, and its state must live in the sim (commands, determinism, saves). These add-ons keep state in Godot nodes, so they can inform the UI (grid, drag and drop, tooltips) but cannot hold state.
- Verdict: **LEARN**; maybe ADAPT UI scenes with attribution. [LimboAI](https://github.com/limbonaut/limboai) and [Beehave](https://github.com/bitbrain/beehave) (both MIT) are behaviour-tree add-ons: **IGNORE** unless we add client-side AI that is not part of the sim.

### Godot space and flight projects
- [godot-simplified-flightsim](https://github.com/fbcosentino/godot-simplified-flightsim): MIT, GDScript, last push 2025-08-24, 312 stars. Generic aircraft and spacecraft flight, floating point, not 6-DOF Newtonian with assist as far as the README suggests (I did not read the code). **LEARN**.
- [Exosphere](https://github.com/sebastianvelace/Exosphere): MIT per search metadata, Godot 4.6 + C#, double-precision orbital launch simulator (4 stars, young). Not our genre. **IGNORE**.
- [Anomaly Station](https://github.com/Noardewyn/AnomalyStation) and Space Station Theta: AGPL-3.0, Godot + C# remakes of a station game (tiny). **LEARN only**, and AGPL means no code reuse.
- Searches for Godot 6-DOF controllers, grid-building add-ons, tech-tree add-ons and galaxy generators found no mature, permissively licensed C# option (see limits below).

### Procedural galaxy and obstacle generators
- Seen: [Galaxia-Runtime](https://github.com/simeonradivoev/Galaxia-Runtime) (BSD-3, Unity particles, 2020), [UniverseGenerator](https://github.com/m4bwav/UniverseGenerator) (MIT, C#, 1 star, pushed 2026-11-23, not read), [galaxy-gen](https://github.com/wisepythagoras/galaxy-gen) (GPL-3). All are float visual generators, not integer-seeded gameplay graphs with lanes and obstacle fields.
- Godot already ships FastNoiseLite (the [upstream](https://github.com/Auburn/FastNoiseLite) is MIT), useful for visuals; the sim must keep its own integer generator.
- Verdict: **IGNORE** for generation; **LEARN** from Pioneer and Endless Sky for layout rules.

### Wave and threat directors
- No open-source reusable library found. References: Valve's AI-systems talk on Left 4 Dead's director ([PDF](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)), Game Developer articles ([intensity](https://gamedeveloper.com/design/using-intensity-to-drive-player-engagement)), and a threat-budget tutorial ([gamineai](https://gamineai.com/blog/how-to-build-a-reusable-enemy-spawn-director-in-unity-6-and-godot-4-wave-curves-budgets-cooldowns)). I read only the search summaries, not the full pieces.
- Verdict: **LEARN** (concept: threat cost per enemy, cap on live threat, pacing curve). Our signature-driven design ([ADR 0008](../adr/0008-remnant-threat-as-signature-driven-waves.md)) is different and already measured by the scripted bot.

## Subsystems with nothing reusable (our unique parts)

These are the parts nobody has published in a form we can use. They are also where the project's value is, so the cost is justified:

- **Deterministic integer simulation of a factory plus ships plus stations**, with commands as the only input, replay as save format and golden checksums (ADRs 0002, 0003, 0016). Closest prior art is OpenRA (GPL, different genre).
- **Modular structures on a grid that rotate and dock** in fixed-point attitude (ADRs 0018, 0029, 0030), with hull shells, power and drones. SS14 has grids and docking, but with floats and its own physics.
- **Ship designer and shipyard** on top of hardpoints and weapons (ADR 0032).
- **Obstacle fields (belts, clusters, mines, wrecks, ion and gravity clouds) with oriented collision in integers** (ADR 0028).
- **Difficulty rules and presets, goals, scenarios and the scripted bot used to balance pacing** (ADRs 0009, 0023, 0025).
- **Content as JSON with a content hash** that feeds saves and replays. Only generic JSON schema tools exist.
- **Signature-driven threat waves** tied to the factory footprint (ADR 0008).

## Recommended next steps (all small)

1. When co-op starts, write an ADR comparing LiteNetLib, Godot's ENet and Klotho parts. Do not pre-commit.
2. Read SS14's `Pow3r` and docking code once, then record in the power and docking prototypes what we would change (design notes only, no code).
3. Try FactorioLab on a data export of our recipes as a balance aid (half a day to see if the data fits before writing an adapter).
4. Audit the **game layer** (25k lines of C#) separately for hand-written things that Godot already provides (Jolt, noise, navigation, GridMap, audio buses, localization). That is probably where the largest avoidable cost sits and it is outside this study.
5. Keep the sim dependency-free. Any numeric library goes through an ADR because it can move the checksums.

## What I could not verify

- I did not read SS14, Mindustry, Pioneer or Star Ruler 2 source beyond file names, READMEs and license files. Claims about what they contain come from file listings and READMEs, not from reading the algorithms.
- OpenRA's lockstep, replay and sync-hash design is from background knowledge; the one page I opened did not describe it. Check before relying on it.
- Klotho and FixedMathSharp were assessed from READMEs and repository metadata only. I did not build, benchmark or test them, and I did not check their quality of rounding, saturation or trig accuracy.
- Maintenance dates, star counts and licenses come from the GitHub API on 2026-11-24 and will drift. For Vega Strike the license field was "NOASSERTION" and I did not read the license.
- The searches for Godot 6-DOF flight add-ons, grid-building add-ons, tech-tree add-ons and C# galaxy generators used GitHub topic search plus one web search; empty results mean "I did not find", not "does not exist". The Godot Asset Library was not browsed.
- Effort-saved figures are my estimates, not measurements.
- The game layer (25k lines) and UI helpers were not audited for duplicate engine functionality.

## Sources

- Starfoundry: [vision](../00-vision.md), [architecture](../specs/09-technical-architecture.md), [ADR index](../adr/README.md).
- [Space Station 14](https://github.com/space-wizards/space-station-14) ([LICENSE.TXT](https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT), README), [RobustToolbox legal.md](https://github.com/space-wizards/RobustToolbox/blob/master/legal.md)
- [Mindustry](https://github.com/Anuken/Mindustry), [SpawnGroup.java](https://github.com/Anuken/Mindustry/blob/master/core/src/mindustry/game/SpawnGroup.java)
- [shapez.io](https://github.com/tobspr-games/shapez.io), [OpenRA](https://github.com/OpenRA/OpenRA), [Pioneer](https://github.com/pioneerspacesim/pioneer), [Naev](https://github.com/naev/naev), [Endless Sky](https://github.com/endless-sky/endless-sky), [OpenTTD](https://github.com/OpenTTD/OpenTTD), [Star Ruler 2 source](https://github.com/BlindMindStudios/StarRuler2-Source), [Unciv](https://github.com/yairm210/Unciv)
- [Klotho](https://github.com/xpTURN/Klotho), [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp), [LiteNetLib](https://github.com/RevenantX/LiteNetLib), [Bepu Q&A on determinism](https://github.com/bepu/bepuphysics2/blob/master/Documentation/QuestionsAndAnswers.md), [Jolt release notes](https://github.com/jrouwe/JoltPhysics/blob/master/Docs/ReleaseNotes.md), [godot-jolt](https://github.com/godot-jolt/godot-jolt)
- [FactorioLab](https://github.com/factoriolab/factoriolab), [GLoot](https://github.com/peter-kish/gloot), [godot-simplified-flightsim](https://github.com/fbcosentino/godot-simplified-flightsim)
