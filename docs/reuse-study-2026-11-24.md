# Reuse study: what already exists that we can use (2026-11-24)

Status: proposal. Question from the owner: *thousands of space games exist; we built a lot from zero; what can we reuse so we do not program the same thing twenty times?* Four research passes, one per area, each reading licence files and READMEs: [A gameplay systems and open-source games](research/reuse-a-gameplay-systems.md), [B Godot ecosystem and UI](research/reuse-b-godot-ecosystem.md), [C assets](research/reuse-c-assets.md), [D .NET libraries and tooling](research/reuse-d-dotnet-libs-and-tooling.md). Nothing was installed or run; verdicts rest on READMEs, licence files and metadata, and each area file lists what it could not verify. Effort numbers are estimates.

## 1. The honest answer
- **The core of Starfoundry has no off-the-shelf equivalent.** Deterministic integer simulation of a factory plus modular rotating ships and stations, with a command-log save, oriented integer collision, a ship designer, difficulty rules, scenarios and a scripted balance bot: no open-source game or library combines these. The nearest open-source relatives are GPL (Mindustry, shapez, OpenRA, Pioneer, Naev, Endless Sky, OpenTTD: *learn only*, their code cannot enter an MIT repo) or tied to their own engine and floating point (Space Station 14's code is MIT but welded to its engine and ECS).
- **Swapping our core for a library would hurt.** Physics libraries are floating point and none documents cross-platform bit-exactness; the only maintained fixed-point library found (FixedMathSharp, Q32.32) would change every golden checksum and break replay of command logs v5 to v15. Serialisers do not solve our real codec cost (the legacy-flag replay rules). ECS libraries solve a problem our structure-of-arrays sim does not have.
- **So much of the effort was inherent, not waste.** The parts a library could have saved are the smaller ones: presentation pieces, assets, tooling and tests. Rough savings if we had known at the start: 20 to 30 developer-days in gameplay libraries (mostly future co-op), plus a smaller amount in UI, assets and tooling. The biggest *avoidable* cost is probably elsewhere (section 4, item 1).

## 2. What to adopt or try (ordered by value for effort)
| # | Item | Verdict | Effort | What it gives us |
| --- | --- | --- | --- | --- |
| 1 | **Fonts**: Inter (SIL OFL 1.1, confirmed) for text, one display face (Exo 2, Rajdhani or Orbitron) for titles, JetBrains Mono for numbers. We use Godot's default font today. | ADOPT | S (then rerun `tools/ui-audit.ps1`, every text width changes) | The cheapest visible quality jump for the HUD and every menu. Read each font's `OFL.txt` before committing it. |
| 2 | **Test tooling**: `BannedApiAnalyzers` (verify its new home; the roslyn-analyzers repo was archived on 2026-11-12) to make `float`, `System.Random` and `DateTime` in the sim a build error; **CsCheck or FsCheck** for the random-walk and codec tests; **Stryker.NET nightly** on the sim; **JsonSchema.Net** for `content/*.json`; **odiff or ImageMagick `compare`** for screenshot golden images. | ADOPT | S to M each | Adds checks instead of deleting code. The analyzer would also flag the two float spots still in the sim (`Math.Sqrt` seeding `ISqrt` in `BeltNetwork.Docking.cs`; `Math.Floor(a / (double)b)` helpers in `BeltNetwork.Station.cs`). |
| 3 | **Godot CI plumbing**: `chickensoft-games/setup-godot` or `godot-ci` for installing Godot in CI (verify a 4.7 Mono tag exists), `GodotEnv` for version pinning. | ADOPT | S | Prerequisite for the Windows build artifact and visual regression. |
| 4 | **gdUnit4Net** (MIT, active; its README lists Godot 4.7) for in-engine C# client tests; xUnit stays for the sim. | TRIAL on the Windows runner | M | Scene-level tests we have none of today. Not confirmed that it coexists with xUnit in one solution. |
| 5 | **Sound effects**: Kenney Sci-fi Sounds and Interface Sounds (CC0) A/B against our synth in `--audio-test`; keep the synth for looping beds (engine, reactor, machines). Music is the real gap (Incompetech is CC BY 4.0, needs a credit line). | ADAPT | S to M | `docs/prototypes/audio.md` says nobody has listened to the synth yet; hear it before replacing it. |
| 6 | **Textures**: ambientCG and Poly Haven (both CC0) as detail layers under our role colours, 1K maximum, for rock, ice and metal. | ADAPT | M | Better rock and metal than hand-tuned noise; keep size and repo weight in check. |
| 7 | **Blender tooling**: our `tools/blender` generator scripts (the pilot now running) plus *the tool* of Spaceship Generator (MIT) for ideas; use GPL add-ons as tools only and never their bundled models. | ADOPT as tools | M | Fits the Blender plan in the retrospective. |
| 8 | **Chickensoft LogicBlocks** (Godot-free C# state machines) on one new state machine (cockpit pages, HUD detail levels or the inventory UI). | TRIAL | S | Replaces hand-rolled state code if it fits; AutoInject only if we want DI. |
| 9 | **LiteNetLib** (MIT) as the transport when co-op starts; **Klotho** (Apache-2.0: Q32.32, rollback ring buffer, Godot net8.0 adapter, a float-ban analyzer) as a parts donor to read first. | LATER (adopt LiteNetLib; learn Klotho) | L, saves 15 to 25 days | Co-op is in spec 07 but not planned; Klotho is young (88 stars, no releases). |
| 10 | **FactorioLab** (MIT, TypeScript) as an offline recipe-ratio and balance tool, once someone writes a data adapter from `content/core.json`. | TRIAL | M | A visual check of the economy besides the bot. |
| 11 | **Maaack's Game Template** pattern for a credits scene, options and scene loader; **Dialogue Manager** (MIT, official C# wrapper) only if the story ever needs branching (`StoryDialog` is 89 lines today). | LEARN / LATER | S | We have no credits scene; copy the pattern, not the code. |

## 3. What not to replace (decided, with reasons)
- The sim, `Attitude`, hit volumes, `CommandCodec`, legacy replay rules, the content hash (determinism and size matter; nothing found beats them).
- `UiTheme`, `UiFit`, `UiAudit`, HUD, markers, build menu, research tree (the only research-tree candidate has 32 stars), the first-person and flight code (no 6-DOF newtonian addon exists), procedural audio beds, the VFX in `Ambience.cs` (the closest MIT repo has CC-BY-NC-SA art), our icon set (Lucide or Material Symbols can fill small gaps; Game-icons.net is CC BY 3.0 and a different style).
- The string tables in `Loc*.cs`: Godot's gettext workflow would lose our Godot-free tests; there is no missing-key checker for Godot 4, so our unit test stays.
- ECS libraries (Arch, Friflo): our sim is already a fast structure of arrays.
- Kits whose style clashes with ours: Kenney and Quaternius 3D space kits are low-poly "toy" style; keep them as reference and for a few props.
- Paid libraries (Sonniss, Synty, KitBash3D, Fab and Megascans, Blender Market kits): their licences forbid or probably forbid committing raw files to a public repo.

## 4. Where the largest avoidable cost still hides
1. **The 25k-line game layer against what Godot already provides** (Jolt, noise, navigation, GridMap, audio buses, built-in localisation, UI containers and themes). Area A flagged this as probably the biggest saving and outside its scope. Proposal: one audit pass over `game/*.cs` listing every hand-written piece that Godot offers, with a keep or replace verdict (some replacements may not be worth it).
2. **Procedural art by code** (see the Blender pilot): the models, not the shell, are where authored art beats code.
3. **Process**: before starting a subsystem, a short prior-art note in its spec or ADR (section 6).

## 5. Conflicts and decisions to resolve before adopting files
- `docs/prototypes/audio.md` says *no audio assets and none may be added*, and `docs/prototypes/ui-icons.md` says *no external art*. Adopting any font, sound or texture needs an ADR that changes this and adds a licence gate.
- Licence tracking (area C proposal): an `ASSETS.md` table (file, source URL, licence, attribution text, date) and a CI script that fails if an asset has no row.
- The code licence rule: only MIT, Apache-2.0, BSD, ISC, BSL-1.0 and CC0 may enter; GPL, AGPL, LGPL, MPL and CC-BY-SA are learn-only (area D saw none of them in its shortlist except Friflo Fliox and CDT.NET, neither recommended).

## 6. Proposed process rule: prior-art check
Add to the spec and ADR template one short section, **Prior art**: what we searched, the three closest candidates with licence and verdict (adopt, adapt, learn, ignore), and why we build when we build. A subsystem starts only after that section exists. Estimated cost: 15 minutes per spec; it prevents the "twenty times" problem. This needs the owner's approval because it changes the contract in `AGENTS.md`.

## 7. Suggested order
1. This week, small: fonts (1) with the ADR in section 5; the analyzer and test tooling (2) and CI plumbing (3); the audio A/B.
2. Next: the game-layer audit (section 4.1); gdUnit4Net and LogicBlocks trials (4, 8).
3. Later: textures, FactorioLab, LiteNetLib and Klotho when co-op is scheduled.

## 8. Not verified (consolidated)
No candidate was installed or built, so Godot 4.7 and .NET 8 compatibility comes from READMEs. Several licence texts were read through GitHub's SPDX field and the README rather than the full file; the Kenney UI pack licence, the LogicBlocks .NET target, Quaternius per-pack terms and the Zylann atmosphere shader licence are unconfirmed; the Godot Asset Library was not browsed, so an empty search means "not found", not "does not exist". The SS14, Mindustry and Pioneer algorithms were not read, only file listings, READMEs and licences. Effort numbers are estimates.
