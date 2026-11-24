# Reuse research, area D: C# / .NET engineering libraries and dev tooling

Status: research, nothing adopted. Written 2026-11-24 for the owner's concern: "we built everything from zero; I do not want to program the same thing 20 times."

How to read this document:

- **Measured** facts about our repo come from reading `src/Starfoundry.Sim`, `src/Starfoundry.Ui`, `tools/`, `tests/`, `docs/ci.md` and the ADR index in this worktree.
- **Library facts** (licence, last push, stars, archived flag) were read on 2026-11-24 from the GitHub REST API (`api.github.com/repos/...`) and from README or LICENSE pages. The fetch tool summarises pages with a small model, so treat details such as target frameworks as "reported by the page", not as verified by building the package. Licence texts were read in full for FixedMath.Net, MessagePack-CSharp, protobuf-net and WaveFunctionCollapse; for the rest I rely on the SPDX field GitHub reports plus the README's own statement.
- **Nothing was downloaded or run.** No package was restored and no determinism claim was tested. Anything marked *unverified* needs a spike before a decision.
- Companion document: [retrospective-2026-11-24.md](../retrospective-2026-11-24.md) already proposes CI test builds, test tiers, golden-image screenshots and GdUnit4Net. This document does not repeat those proposals; it adds the library-level evidence.

## 0. What we actually have (so the comparison is fair)

| Piece | Where | Size / notes |
|---|---|---|
| Simulation, integer only, no Godot reference, no NuGet dependency | `src/Starfoundry.Sim` (net8.0, `TreatWarningsAsErrors`, `Deterministic`) | about 11.6k lines |
| Fixed-point unit quaternion and rotation matrix (scale 2^14) | `Attitude.cs` (122 lines) | integer arithmetic, canonical sign |
| Command log binary codec, versions 5 to 15, strict bounded reads, `WorldRules.Legacy*` flags so old logs replay as recorded | `CommandCodec.cs` (497 lines), ADR 0016 | the legacy flags are game semantics, not just wire format |
| Own JSON content loader and validation | `ContentDb.cs` (1260 lines), `ScenarioCatalog.cs`, on `System.Text.Json` | semantic checks (references, ranges) dominate |
| Deterministic RNG | `Galaxy.cs`, SplitMix64 | a few lines; ADR 0007 |
| Integer geometry, hit volumes, collision | Sim and `BodyMotion.cs` in Ui | oriented boxes, spheres, capsules, ray casts |
| Mesh audit (holes, coplanar faces, floating parts) | `Starfoundry.Ui/MeshAudit.cs` (309 lines) | domain-specific rules |
| Scripted-player bots for balance | `tools/Starfoundry.Balance` | `StructureBot` is the balance oracle |
| Benchmark with a p99 tick budget | `tools/Starfoundry.Bench` | CI gate at 30 UPS |
| Tests | `tests/Starfoundry.Sim.Tests`, xUnit 2.9.2, about 1100 `[Fact]`/`[Theory]` markers | brute-force and model-based tests; the sim tests also reference Ui and Balance |
| CI | `.github/workflows/ci.yml`, `docs/ci.md` | `dotnet test`, Godot build, bench; PC runner with hosted fallback |

One honest observation from reading the sim: it is *almost* float-free. The only floating point left is `Math.Sqrt` seeding an integer square root that then corrects itself (`ISqrt` in `BeltNetwork.Docking.cs`, safe because the correction loops fix any off-by-one) and `Math.Floor(a / (double)b)` for floor division in `BeltNetwork.Station.cs`, which is exact for these ranges but is a floor-division helper that could be integer-only. These are the two lines a determinism analyzer would flag first.

## 1. Summary table

Verdicts: ADOPT = use as is; ADAPT = use with changes or in a limited role; LEARN = read it, copy ideas, do not take the dependency; IGNORE = not worth it for us. Effort: S under a day, M a few days, L a week or more.

| Our piece | Best candidate | Verdict | Effort | What it would save (honest) |
|---|---|---|---|---|
| Fixed-point math, quaternion, matrix, integer geometry | [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) (MIT, Q32.32) | **LEARN** | L to adopt | Nothing we need: our scale-2^14 integers are smaller, and switching changes every checksum and breaks log compatibility |
| Deterministic physics / collision | [Jitter2](https://github.com/notgiven688/jitterphysics2), [BepuPhysics v2](https://github.com/bepu/bepuphysics2) | **IGNORE** for the sim, LEARN for client collision ideas | L | Float-based; no cross-platform bit-exactness guarantee found for Bepu |
| Deterministic ECS / lockstep / rollback | [Klotho](https://github.com/xpTURN/Klotho) (Apache-2.0) | **LEARN** | L | Reference for rollback ring buffer, analyzer, determinism probes if co-op ever lands |
| General ECS | [Arch](https://github.com/genaray/Arch), [Friflo ECS](https://github.com/friflo/Friflo.Engine.ECS), [DefaultEcs](https://github.com/Doraku/DefaultEcs) | **IGNORE** | L | Our state is not ECS-shaped; rewrite for no gain |
| `CommandCodec` (binary log) | [MemoryPack](https://github.com/Cysharp/MemoryPack), [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp), [Bebop](https://github.com/betwixt-labs/bebop) | **LEARN** (keep hand-written) | M | Libraries do not remove the legacy-flag logic, which is most of the complexity; a tag table is the only merge hotspot |
| `ContentDb` structural checks | [JsonSchema.Net](https://github.com/json-everything/json-everything) (MIT) | **ADAPT** | S to M | Structure/type checks and editor autocompletion; semantic checks stay |
| Mesh audit (holes) | [geometry3Sharp](https://github.com/gradientspace/geometry3Sharp) (BSL-1.0) | **ADAPT**, test project only | M | Possibly the "closed mesh / boundary edge" part; coplanar and floating-part rules stay ours |
| Hand-written brute-force tests | [CsCheck](https://github.com/AnthonyLloyd/CsCheck) (Apache-2.0) or [FsCheck](https://github.com/FsCheck/FsCheck) (BSD-3) | **ADAPT** | S to M | Shrinking and reproducible seeds for the random-walk and codec round-trip tests |
| Test quality measurement | [Stryker.NET](https://github.com/stryker-mutator/stryker-net) (Apache-2.0) | **ADOPT** as a nightly tool on `Starfoundry.Sim` only | S | No code deleted; shows which of the 1100 tests are decorative |
| Golden saves, content dumps | [Verify](https://github.com/VerifyTests/Verify) (MIT) | **ADAPT** | S to M | Hand-rolled "expected text" files; needs xUnit v3 (see risks) |
| Bench tool | [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) (MIT) | **ADAPT** alongside, not instead | S | Our p99 gate is the right shape for CI; BDN is for investigating a regression |
| Screenshot regression | [odiff](https://github.com/dmtrKovalenko/odiff) (MIT), [pixelmatch](https://github.com/mapbox/pixelmatch) (ISC), Verify.ImageMagick | **ADAPT** (odiff CLI or Verify.ImageMagick) | S to M | The compare step the retrospective said it had to write |
| Determinism guard (ban float, DateTime, Random) | `Microsoft.CodeAnalysis.BannedApiAnalyzers` | **ADOPT** (check its new home first) | S | Replaces the "never use System.Random" comment in `Galaxy.cs` with a build error |
| Godot export / CI | [godot-ci](https://github.com/abarichello/godot-ci) (MIT), [godot-export](https://github.com/firebelley/godot-export) (MIT), [GodotEnv](https://github.com/chickensoft-games/GodotEnv) (MIT) | **ADAPT** | S to M | The export-template and Godot-version plumbing for the retrospective's test-build job |
| Client unit tests inside Godot | [gdUnit4Net](https://github.com/godot-gdunit-labs/gdUnit4Net), [GoDotTest](https://github.com/chickensoft-games/GoDotTest) | **ADAPT** (already proposed in the retrospective) | M | Scene-level logic only |
| Bots, tech tree, crafting graph | no suitable library found | **IGNORE** | none | Our `StructureBot` and research view are the right size |
| World generation | [FastNoiseLite](https://github.com/Auburn/FastNoiseLite), [Delaunator](https://github.com/nol1fe/delaunator-sharp), [WFC](https://github.com/mxgmn/WaveFunctionCollapse) | **IGNORE** for the sim (floats), LEARN | none | Our SplitMix64 and integer placers are the deterministic choice |
| Pathfinding | [Trailblazer](https://github.com/mrdav30/Trailblazer) | **LEARN** | L | Only if ships or drones ever need grid pathing in the sim |

Short answer to the owner's worry: **the big hand-written pieces (fixed-point attitude, command log, hit volumes, replay and checksum) are where our code is better than anything off the shelf, because determinism and log compatibility are the product's spine.** The libraries that would clearly save time are the *testing and tooling* ones: Stryker, CsCheck or FsCheck, Verify, a banned-API analyzer, an image-diff tool and the Godot CI images. Those add checks; they delete little code.

## 2. Candidate notes

### 2.1 Fixed-point and deterministic math

**FixedMathSharp** — [github.com/mrdav30/FixedMathSharp](https://github.com/mrdav30/FixedMathSharp), [NuGet](https://www.nuget.org/packages/FixedMathSharp).
- Licence: MIT (GitHub SPDX). Created 2024-10, last push 2026-11-23, 97 stars, 475 commits, not archived. Single main maintainer.
- Reported shape: `Fixed64` Q32.32, `Vector3d`, `FixedQuaternion`, matrices, bounds, rays, segments, triangles, seeded RNG streams, JSON and MemoryPack support, separate `.Lean` package without MemoryPack. Packages target .NET Standard 2.1 and .NET 10, so net8.0 should consume the netstandard2.1 build (*unverified*).
- Determinism: described as "deterministic by design", integer-backed paths. The page I could read does **not** say how trig functions are computed or give cross-platform test results. That is exactly the claim that matters, so it would need a source read and a cross-OS hash test.
- Overlap: `Attitude.cs`, parts of `Units.cs`, integer geometry helpers. Would let us delete perhaps a few hundred lines of math.
- Why not: our world is integers in sub-units (240 per cell, 30 ticks per second) and a 2^14 quaternion. Q32.32 is a different number system: every checksum, every saved log (versions 5 to 15, ADR 0016) and every golden test would change, and the codec's legacy flags exist precisely so old logs replay as recorded. A young library with API-breaking migration guides is also a risk for a format we promise to keep.
- Verdict: LEARN. Read its quaternion normalisation and trig tables against ours; do not adopt.

**FixedMath.Net** — [github.com/asik/FixedMath.Net](https://github.com/asik/FixedMath.Net). Q31.32. LICENSE read: Apache-2.0 for the project plus MIT terms for the bundled libfixmath and log2fix code (GitHub reports the SPDX as "other"). Archived, last push 2018-04, 649 stars. Forks exist ([ForNeVeR](https://github.com/ForNeVeR/FixedMath.NET), last push 2023). Verdict: IGNORE (superseded and unmaintained).

**FixPointCS** — I could not locate the repository or its licence through search. *Not verified; no verdict.*

**Unity deterministic physics ports** — a forum thread reports a fixed-point port of Bepu v2 was considered hopeless because Bepu v2 is SIMD-optimised ([Bepu forum](https://forum.bepuentertainment.com/viewtopic.php?p=14049), community statement, not official). Verdict: IGNORE.

**.NET floating point** — I found no authoritative statement that `Math.Sin`/`Math.Cos` are bit-identical across x86, x64 and ARM64; cross-architecture differences for trig are well documented in other runtimes ([Go issue](https://golang.org/issue/18354), [Blender sinf on M1](https://projects.blender.org/blender/blender/issues/107829)). `Math.Sqrt` is IEEE-correctly-rounded in principle but I found no .NET source confirming it. This supports the current ADR 0002 rule and the tiny `ISqrt` correction loops: keep trig out of the sim.

### 2.2 Physics, collision and geometry

**BepuPhysics v2** — [github.com/bepu/bepuphysics2](https://github.com/bepu/bepuphysics2). Apache-2.0, last push 2026-10-11, 2984 stars, active. Targets .NET 8 per the documentation. Single-machine determinism exists via a `Deterministic` setting (found via search result, *unverified in the docs I fetched*); I found **no official cross-platform bit-exactness guarantee**. Float-based and SIMD-heavy. Verdict: IGNORE for the sim; LEARN for shape and sweep APIs if the client ever needs richer collision than our oriented-box tests.

**Jitter2** — [github.com/notgiven688/jitterphysics2](https://github.com/notgiven688/jitterphysics2). MIT, last push 2026-11-22, 454 stars, "dependency-free". README lists an "optional cross-platform deterministic solver" with single or double precision chosen at compile time, plus EPA-aided MPR for convex shapes and continuous collision. It says nothing about what the determinism guarantee covers, and the demo needs the .NET 10 SDK (library target not stated). Verdict: IGNORE for state-affecting code (floats, and we would have to hash a float world); worth a LEARN read if cross-platform float determinism is ever reconsidered. Our hit volumes are integer and exact, which is strictly stronger.

**geometry3Sharp** — [github.com/gradientspace/geometry3Sharp](https://github.com/gradientspace/geometry3Sharp). Boost Software License 1.0 (permissive, no attribution in binaries), last push 2026-01-09, 1907 stars. Mesh type (`DMesh3`) with validity checks, boundary-loop and hole tools, AABB trees, ray and distance queries. Float/double. Target framework not checked (*unverified*; it is an older netstandard-era library). Overlap: the "holes" part of `MeshAudit`; our coplanar-face and floating-part rules are domain rules it does not have. Verdict: ADAPT, in the test project only (never in the sim), as a cross-check for `MeshAudit` and as a ready-made hole finder. Effort M including conversion from our meshes.

**MIConvexHull** — [github.com/DesignEngrLab/MIConvexHull](https://github.com/DesignEngrLab/MIConvexHull). MIT, active (push 2026-10-22). Convex hulls in N dimensions. We do not currently compute hulls in the sim. IGNORE unless we generate collision hulls from Blender models (the retrospective's pilot); then it is a LEARN/ADAPT candidate for a build tool.

**Clipper2** — [github.com/AngusJohnson/Clipper2](https://github.com/AngusJohnson/Clipper2). BSL-1.0; GitHub reported the repository as archived on 2026-11-24 despite a recent push, reason not checked. 2D polygon clipping and triangulation; no current need. IGNORE.

**GJK/EPA libraries and BVH libraries** — not researched beyond Jitter2's MPR/EPA mention. Our collision primitives are exact integer tests; a BVH would only matter if profiling shows obstacle queries dominate (the Bench tool says tick time is the budget). *No findings.*

### 2.3 ECS, lockstep and rollback

**Klotho** — [github.com/xpTURN/Klotho](https://github.com/xpTURN/Klotho). Apache-2.0, 88 stars, 113 commits, no published releases or packages seen. Pure-C# deterministic core with 32.32 fixed-point math, Xorshift128+ RNG, trig by LUT/CORDIC, a sparse-set ECS with a frame heap and a ring buffer for rollback, P2P lockstep and server modes, NavMesh, a Godot (.NET, net8.0, Godot 4.4+) adapter, and, importantly for us, a **build-time analyzer that flags float and non-deterministic APIs** plus determinism verification runners. The page gives no cross-platform test results. It is the closest thing to "Quantum, open source" that I found ([Photon Quantum](https://doc.photonengine.com/quantum) is commercial). Verdict: LEARN. Not adoptable (our state model and log format already exist), but its analyzer and determinism-probe ideas are the reference for the guard in section 3.

**Arch** — [github.com/genaray/Arch](https://github.com/genaray/Arch). Apache-2.0, pushed 2026-11-24, 1882 stars. **Friflo.Engine.ECS** — [github.com/friflo/Friflo.Engine.ECS](https://github.com/friflo/Friflo.Engine.ECS). MIT, pushed 2026-10-31, 819 stars. (Do not confuse it with the Friflo Fliox repo, which is LGPL-3.0.) **DefaultEcs** — [github.com/Doraku/DefaultEcs](https://github.com/Doraku/DefaultEcs). MIT-0, last push 2024-03, so effectively dormant. None gives deterministic iteration order or state hashing as a feature I could confirm. Our simulation is a belt/machine/structure graph in dictionaries with stable integer IDs; moving it into an ECS would be a rewrite that touches every file for a performance problem we do not have (the bench gate passes). Verdict: IGNORE.

**Others seen, not evaluated:** [massive](https://github.com/nilpunch/massive) (Unity package), DeltaNetcode and SetNet.Lockstep on NuGet (maturity unverified), [LiteNetLib](https://github.com/RevenantX/LiteNetLib) as a transport if co-op arrives (not fetched; repo name not confirmed).

### 2.4 Binary serialisation vs `CommandCodec`

- **MemoryPack** — [github.com/Cysharp/MemoryPack](https://github.com/Cysharp/MemoryPack), MIT, push 2026-11-11, 4782 stars. Source-generated; types must be `partial`; default mode lets you add but not remove or reorder members, a `VersionTolerant` mode needs explicit orders; it can write serialisation info to a file you diff in CI. **Its own docs say it is for trusted data and recommend MessagePack for untrusted input.** Targets netstandard2.1 and net7 or newer.
- **MessagePack-CSharp** — [github.com/MessagePack-CSharp/MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp), MIT (LICENSE read; also bundles BSD-2 lz4net and Apache-2.0 BufferWriter), push 2026-11-20, 6811 stars. Schema evolution by integer keys; safe-ish for untrusted input.
- **protobuf-net** — [github.com/protobuf-net/protobuf-net](https://github.com/protobuf-net/protobuf-net), Apache-2.0 (Licence.txt read), push 2026-11-20, 4975 stars. **Bebop** — [github.com/betwixt-labs/bebop](https://github.com/betwixt-labs/bebop), Apache-2.0, last push 2026-02-17, schema-first (`.bop`), multi-language.
- Why we keep the hand-written codec: (1) saves are the command log replayed (ADR 0016), and the format must stay byte-readable for versions 5 to 15; (2) the hard part is the *semantic* legacy handling (`WorldRules.LegacyGalaxy`, `LegacyLaser`, `LegacyHardpoints`, `LegacyCollisions`, `LegacyFlight`, `LegacyBuilding`: "the reader refuses this command in older logs"), which no serialiser expresses; (3) it is 497 lines with bounded, strict reads that our tests exercise; (4) a serialiser's wire format is not under our control across its own versions, which is the opposite of what a replay format needs.
- Where a library helps: if a network protocol or a client-only settings format appears, MessagePack (untrusted-input posture) or Bebop (if a non-C# peer appears) is better than hand-rolling a second codec. The merge-conflict hotspot the retrospective names (the tag list in `CommandCodec`) is cheaper to fix with a one-file tag registry and a golden-bytes test than with a new serialiser.
- Verdict: LEARN; keep the codec.

### 2.5 JSON content and schema validation

**JsonSchema.Net** (json-everything) — [github.com/json-everything/json-everything](https://github.com/json-everything/json-everything). MIT, pushed 2026-11-21, 1285 stars. Drafts 6 and higher, built for netstandard2.0 and net8/9/10, on System.Text.Json, schema generation from .NET types in a separate package. **NJsonSchema** — [github.com/RicoSuter/NJsonSchema](https://github.com/RicoSuter/NJsonSchema), MIT, push 2026-11-11, 1586 stars; Newtonsoft-based generator and validator.
- Overlap: the structural half of `ContentDb` (required fields, types, ranges, enum names). The semantic half (does recipe X reference an existing item, are research prerequisites acyclic, is the economy reachable) is game logic and stays ours.
- Practical use: write `content/schema/core.schema.json`, validate in a test (not inside the sim at runtime, to keep the sim dependency-free), and point VS Code or Rider at the schema for autocompletion and inline errors. That helps the "content from agents" workflow: errors appear before a build. I did not measure how much of the 1260 lines is structural; guess a third.
- Verdict: ADAPT. Effort S to M. Corvus.JsonSchema (suggested by the page for code generation) was not evaluated.

### 2.6 Procedural generation

- **FastNoiseLite** — [github.com/Auburn/FastNoiseLite](https://github.com/Auburn/FastNoiseLite), MIT, push 2026-10-17, 3562 stars. Float-based, so not for anything that affects state. Useful for visuals (nebula, ore texture). Verdict: IGNORE for the sim.
- **Delaunator (C#)** — [github.com/nol1fe/delaunator-sharp](https://github.com/nol1fe/delaunator-sharp), MIT per NuGet, last push 2025-02; float. **CDT.NET** — [github.com/MichaCo/CDT.NET](https://github.com/MichaCo/CDT.NET), MPL-2.0. Only relevant if a lane graph over a float layout is built outside the sim, then frozen as integer data. Not needed today (ADR 0007 generates regions by integer rules).
- **WaveFunctionCollapse** — [github.com/mxgmn/WaveFunctionCollapse](https://github.com/mxgmn/WaveFunctionCollapse). LICENSE read: MIT, but "provided image samples and tiles are not part of the software". Reference implementation, not a library. Verdict: LEARN if structure templates ever need constraint-based layout.
- Poisson-disk sampling: nothing evaluated; our `ObstacleGenerator` already does integer placement with a placer. *No findings.*

### 2.7 Pathfinding, AI, tech tree, crafting

- **Trailblazer** — [github.com/mrdav30/Trailblazer](https://github.com/mrdav30/Trailblazer). MIT, netstandard2.1 and net8.0, deterministic A* and flow fields with fixed-point costs and canonical ordering, built over the same author's GridForge and FixedMathSharp. v2, 6 stars, 0 forks, no releases seen. Interesting as a model of "deterministic A* with finite work budgets"; unproven in production. Verdict: LEARN.
- Behaviour-tree and utility-AI libraries: I did not find or evaluate a deterministic, dependency-light one. Our balance bot is a scripted player that issues commands and measures outcomes; that is closer to a test oracle than game AI, and a generic AI library would not give us the balance measurements. Verdict: IGNORE.
- Tech tree, recipes and dependency graphs: no library found worth taking. [QuikGraph](https://github.com/KeRNeLith/QuikGraph) (graph algorithms such as topological sort) is the usual .NET answer; I could not confirm the URL or licence from the one fetch that returned 404, so it is *unverified*. Our `ResearchTreeLayout` and content validation already do the needed topological work.

### 2.8 Testing

- **CsCheck** — [github.com/AnthonyLloyd/CsCheck](https://github.com/AnthonyLloyd/CsCheck). Apache-2.0, pushed 2026-11-24, 282 stars. Random-testing library for C# with shrinking and a replay seed; no F# runtime dependency. **FsCheck** — [github.com/FsCheck/FsCheck](https://github.com/FsCheck/FsCheck). BSD-3-Clause, pushed 2026-10-21, 1259 stars. Either could replace the hand-written random loops in `BodyShellBruteForceTests` (random walks with big and small steps), `OrientedCollisionTests` and codec round trips, and would report a minimal failing case instead of a long seed. Keep the exhaustive enumerations (every module kind next to every other) as plain loops; properties and exhaustion do different jobs. Verdict: ADAPT; I would try CsCheck first (smaller, faster, C#-native), unverified against xUnit 2.9 specifics.
- **Stryker.NET** — [github.com/stryker-mutator/stryker-net](https://github.com/stryker-mutator/stryker-net). Apache-2.0, pushed 2026-11-24, 2146 stars. Mutation testing for .NET. Run it on `Starfoundry.Sim` only, nightly, so the 3 to 5 minute local test run is not multiplied. I could not read its option list from the page fetched; the `--since` and mutate-filter options I remember should be checked in its docs before relying on them. It would tell us which of the roughly 1100 tests do not constrain behaviour, directly addressing the retrospective's worry that tests pass while hand-testing still finds bugs. Effort S for a first run; reading results is the real cost.
- **Verify** — [github.com/VerifyTests/Verify](https://github.com/VerifyTests/Verify). MIT, pushed 2026-11-23, 3476 stars. Snapshot ("golden file") testing with diffs; extensions for images (Verify.ImageMagick, Verify.ImageHash, ImageSharp, SSIM variants). The page lists **xUnit v3** support; we use xUnit **2.9.2**, so adopting Verify implies migrating the test project to xUnit v3 (or checking whether an older Verify.Xunit still works; *unverified*). Good fit: save-game bytes, content dumps, world checksum after a fixed scripted run ("golden state" tests ADR 0002 promises).
- **BenchmarkDotNet** — [github.com/dotnet/BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet). MIT, 11535 stars, pushed 2026-11-21. Statistically sound micro-benchmarks. Our `tools/Starfoundry.Bench` is a whole-tick p99 gate, which BDN does not replace (BDN is poor at "p99 of 3000 consecutive ticks of one growing world"). Use BDN as an investigation tool for hot spots. Verdict: ADAPT, optional.
- **Property tests of determinism specifically**: none of these provides cross-OS bit-exactness checks. The thing we need is a CI matrix job (Windows, Linux, macOS if available) that runs the same scripted game and compares the final world checksum to a committed value. That is a few lines of our own, and it is the only real proof of the ADR 0002 claim. (The ADR says this is "checked by golden-state tests in CI"; the CI file shows tests run on one OS at a time, hosted Linux or the owner's PC, so a committed golden checksum already catches Linux-vs-Windows drift only if both runners run the same test; worth confirming.)

### 2.9 Visual regression and image diff

- **odiff** — [github.com/dmtrKovalenko/odiff](https://github.com/dmtrKovalenko/odiff). MIT, pushed 2026-10-13, 3305 stars. SIMD-first image comparison with a CLI and a Node API, anti-aliasing tolerance. **pixelmatch** — [github.com/mapbox/pixelmatch](https://github.com/mapbox/pixelmatch). ISC, pushed 2026-11-24, 7031 stars, JavaScript (would add Node to CI). **ImageMagick `compare`** is available on any runner with metrics such as RMSE and PSNR and writes a diff image ([docs](https://usage.imagemagick.org/compare/)); SSIM appears in some bindings, not confirmed for Magick.NET. [dssim](https://docs.rs/crate/dssim/latest) is a Rust multi-scale SSIM tool.
- Recommendation: odiff CLI or ImageMagick `compare` called from the `--*-shot` scripts, with a per-shot tolerance and the diff image uploaded as a CI artifact on failure. The still-shot limits in the retrospective (flicker, streaks) remain: image diff does not catch temporal artefacts.
- Verdict: ADAPT, effort S to M. Pick the tool by what the owner's PC runner already has installed (not checked).

### 2.10 Godot CI, Godot C# specifics

- **godot-ci** — [github.com/abarichello/godot-ci](https://github.com/abarichello/godot-ci). MIT, pushed 2026-10-20, 1159 stars. Docker image with Godot and export templates; Mono variant by the `mono-` tag prefix. The README I read shows 3.2.1 examples only; **whether a 4.7 Mono tag exists was not verified**.
- **godot-export** — [github.com/firebelley/godot-export](https://github.com/firebelley/godot-export). MIT, pushed 2026-05-28, 655 stars. GitHub Action that exports and can create releases; Godot 4.7 and C# support not verified.
- **GodotEnv** — [github.com/chickensoft-games/GodotEnv](https://github.com/chickensoft-games/GodotEnv). MIT, pushed 2026-11-22, 588 stars. Installs and switches Godot versions from the command line on Windows, macOS and Linux. Fits the retrospective's "pin tool versions" item.
- **Chickensoft stack** (GoDotTest, AutoInject, LogicBlocks): [GoDotTest](https://github.com/chickensoft-games/GoDotTest) MIT, push 2026-11-11, 159 stars; a C# test runner with coverage and debugging. **gdUnit4Net** — [github.com/godot-gdunit-labs/gdUnit4Net](https://github.com/godot-gdunit-labs/gdUnit4Net), MIT, push 2026-11-23, 199 stars (the retrospective already proposes it). Either runs inside Godot; neither replaces xUnit for the sim.
- **Hot reload**: Godot 4.7 plus the [Hot Reload Live](https://forum.godotengine.org/t/tool-hot-reload-live-runtime-c-hot-reload-for-godot-4-7/144535) community tool patches methods of the running game; the author says adding or removing fields is skipped with a warning. Name-your-own-price on itch; licence not stated; one author. Blog posts report Godot's built-in reload sometimes keeps running old logic ([example](https://gamineai.com/help/godot-4-5-csharp-hot-reload-not-working-dotnet-sdk-editor-integration-fix); anecdotal). Verdict: LEARN; try it for a UI-iteration session only after reading its source, in an isolated environment.
- **Source generators**: the official `Godot.SourceGenerators` package exists on NuGet (4.7.2 listed); third-party [GodotSharp.SourceGenerators](https://github.com/Cat-Lips/GodotSharp.SourceGenerators) adds attributes for nodes and properties. Pitfall reported: IDEs keep stale generated code after `.csproj` edits. These help the `game/` project (about 25k lines of node code). The `.cs.uid` files showing as untracked in `git status` are Godot 4.4+ generated metadata; whether to commit or ignore them is a repo decision, not a library one (*not researched*).
- **Keeping the sim Godot-free**: already enforced by project structure (`Starfoundry.Sim.csproj` has no package or project references). A test that parses the csproj for `Godot` is a cheaper guard than an architecture-test library; not researched further.

### 2.11 Analyzers for determinism

- **BannedApiAnalyzers** (`Microsoft.CodeAnalysis.BannedApiAnalyzers`, rule RS0030): a `BannedSymbols.txt` lists symbols by documentation-comment ID (`T:System.Random`, `P:System.DateTime.Now`, ...) with an optional message; violations can be made build errors via `WarningsAsErrors`. Sources: [help page copy](https://github.com/reduckted/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md), [dev.to walkthrough](https://dev.to/karenpayneoregon/microsoftcodeanalysisbannedapianalyzers-1g6k). **Caveat: the `dotnet/roslyn-analyzers` repository was archived on 2026-11-12; the page does not say where the analyzer lives now.** The NuGet package should still restore; check the new home and the package's latest version before committing (*unverified*). It bans API symbols, not arithmetic operators on `double`; to ban the `float`/`double` types themselves you would ban `T:System.Single` and `T:System.Double` and allow-list the two helper lines noted in section 0 (*untested*; banning a type's use in declarations may or may not catch literals and operators).
- Meziantou.Analyzer ([repo](https://github.com/meziantou/Meziantou.Analyzer), MIT, active) includes many correctness rules; not a determinism tool by itself.
- Klotho ships a determinism analyzer of its own (section 2.3); read its source for what it flags.
- Verdict: ADOPT BannedApiAnalyzers for `System.Random`, `DateTime.Now/UtcNow`, `Environment.TickCount`, `Math.Sin/Cos/...`, `Dictionary` enumeration where order matters (the last is not expressible as a symbol ban), scoped to `Starfoundry.Sim` only. Effort S.

### 2.12 Dev-loop speed, release tooling

Mostly overlaps the retrospective (test tiers, integrate script, pinned versions). Additions:
- `dotnet watch test --project tests/...` re-runs tests on save (standard .NET SDK feature; not re-verified here). Combined with xUnit traits (`[Trait("tier","fast")]`) and `--filter`, it gives the sub-minute inner loop.
- Sharding in CI: split by `--filter` in a job matrix; xUnit v2 parallelises classes within a process by default. Measure first (retrospective item 3 already asks for it).
- Build caching: `actions/setup-dotnet` can cache NuGet restore given a lockfile (general knowledge, not re-verified). The sim has no packages; only test and tool projects restore anything, so the saving is small.
- Reproducible builds: [dotnet/reproducible-builds](https://github.com/dotnet/reproducible-builds) (MIT, Microsoft guidance). We already set `<Deterministic>true</Deterministic>`; the guide adds source-link and `ContinuousIntegrationBuild`. Useful only if we ship signed or verifiable artefacts.
- Release and version tooling (MinVer, Nerdbank.GitVersioning, release-please): not researched in this pass.
- Source-generated localisation (ResX generators and similar): not researched; our `Loc*.cs` string tables are plain C# and tested by `*TextsTests`.

## 3. Where hand-written code is the right call

1. **Integer simulation, `Attitude`, hit volumes.** Cross-platform bit-exactness is guaranteed by construction (integers, no libm). Every off-the-shelf physics or math library I found is float-based, or fixed-point with a different scale, and none documents cross-platform trig exactness in a way I could verify. Replacing ours would trade a proven property for a claim.
2. **`CommandCodec` and the legacy flags.** The cost is game semantics, not byte layout; no serialiser helps with it, and an external wire format would couple our replay compatibility to someone else's releases.
3. **SplitMix64 and integer placers.** A few lines each; any dependency is bigger than the code.
4. **Checksum hashing, clone, replay.** Not a library category I found for general use; Klotho is the only comparable design and is a framework, not a part.
5. **`MeshAudit` domain rules** (coplanar faces, floating parts) and **StructureBot** (balance oracle). They encode our rules and have found real bugs (retrospective section 2).
6. **Brute-force enumeration tests** (every module next to every other). A property-testing library complements these; it does not replace exhaustion.

## 4. Where an off-the-shelf tool would clearly save time

| Gap | Tool | Why |
|---|---|---|
| "Never use float, Random, DateTime in the sim" is a comment | BannedApiAnalyzers | One file and a build error instead of review vigilance |
| Do 1100 tests actually fail when the code is wrong? | Stryker.NET (nightly, sim only) | Measures the thing the retrospective could not |
| Random-walk tests report a seed, not a minimal case | CsCheck or FsCheck | Shrinking and replay |
| Content errors discovered at load time | JsonSchema.Net plus a schema file | Editor feedback before the build |
| Screenshot compare step missing | odiff or ImageMagick `compare` | Retrospective item 7 says we must write it; a CLI is enough |
| Godot version drift between PC, CI and agents | GodotEnv and `godot-ci` image | Retrospective item 14 |
| Hand-made golden expected files | Verify | Needs an xUnit v3 migration; weigh that cost first |

## 5. Risks and what I could not verify

- **No spike was run.** Determinism claims of FixedMathSharp, Jitter2 and Klotho are only what their READMEs say; I read no source and ran no cross-OS test.
- **Target frameworks** were taken from README or NuGet text through a summarising fetch. For geometry3Sharp, Jitter2's library target, Verify with xUnit v2 and several others I did not confirm that a net8.0 project can reference them.
- **FixPointCS** could not be found; **QuikGraph**'s URL was wrong in my fetch; **LiteNetLib** was not confirmed; the Clipper2 "archived" flag and the roslyn-analyzers archive date are as GitHub's API reported them, reasons unknown.
- **Not researched at all:** utility-AI and behaviour-tree libraries beyond a generic dismissal, Poisson-disk libraries, GJK/BVH libraries beyond Jitter2, release-version tools, source-generated localisation, incremental test selection tools. Treat these rows as open.
- Star counts and push dates are a snapshot of 2026-11-24 and say little about quality. Most candidates are single-maintainer projects.
- Licences are all permissive for a public MIT repo (MIT, Apache-2.0, BSD-3, BSL-1.0, ISC, MIT-0). The only copyleft items seen were Friflo Fliox (LGPL-3.0, unrelated to the ECS repo) and CDT.NET (MPL-2.0); neither is recommended.

## 6. Suggested order (for the owner to accept or change)

1. S: BannedApiAnalyzers on `Starfoundry.Sim` (confirm package source first); fix the two `double` helper lines or allow-list them.
2. S: a Stryker.NET nightly workflow on the sim project, report only.
3. S: JSON schema file for `content/*.json` plus a validating test; keep `ContentDb` semantic checks.
4. S to M: image-diff step (odiff or ImageMagick `compare`) in the existing shot flow, with diff artefacts on failure.
5. M: try CsCheck on one brute-force test class as a pilot; keep it only if the failure output is better.
6. Later, only if co-op or a network protocol becomes real: read Klotho's rollback and analyzer code, and evaluate MessagePack-CSharp for the network messages (not for the saved command log).
7. Not recommended: replacing the math layer, the physics, the codec or the content loader.

## Sources

- Fixed-point: [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp), [NuGet](https://www.nuget.org/packages/FixedMathSharp), [asik/FixedMath.Net](https://github.com/asik/FixedMath.Net), [ForNeVeR/FixedMath.NET](https://github.com/ForNeVeR/FixedMath.NET), [Bepu fixed-point port thread](https://forum.bepuentertainment.com/viewtopic.php?p=14049), [Trailblazer](https://github.com/mrdav30/Trailblazer)
- Physics and geometry: [BepuPhysics v2](https://github.com/bepu/bepuphysics2), [Jitter2](https://github.com/notgiven688/jitterphysics2), [geometry3Sharp](https://github.com/gradientspace/geometry3Sharp), [MIConvexHull](https://github.com/DesignEngrLab/MIConvexHull), [Clipper2](https://github.com/AngusJohnson/Clipper2)
- ECS and lockstep: [Klotho](https://github.com/xpTURN/Klotho), [Arch](https://github.com/genaray/Arch), [Friflo.Engine.ECS](https://github.com/friflo/Friflo.Engine.ECS), [DefaultEcs](https://github.com/Doraku/DefaultEcs), [massive](https://github.com/nilpunch/massive), [Photon Quantum](https://doc.photonengine.com/quantum)
- Serialisation: [MemoryPack](https://github.com/Cysharp/MemoryPack), [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp), [LICENSE](https://raw.githubusercontent.com/MessagePack-CSharp/MessagePack-CSharp/master/LICENSE), [protobuf-net](https://github.com/protobuf-net/protobuf-net), [Bebop](https://github.com/betwixt-labs/bebop)
- Content and generation: [json-everything](https://github.com/json-everything/json-everything), [NJsonSchema](https://github.com/RicoSuter/NJsonSchema), [FastNoiseLite](https://github.com/Auburn/FastNoiseLite), [delaunator-sharp](https://github.com/nol1fe/delaunator-sharp), [CDT.NET](https://github.com/MichaCo/CDT.NET), [WaveFunctionCollapse](https://github.com/mxgmn/WaveFunctionCollapse)
- Testing and tooling: [CsCheck](https://github.com/AnthonyLloyd/CsCheck), [FsCheck](https://github.com/FsCheck/FsCheck), [Stryker.NET](https://github.com/stryker-mutator/stryker-net), [Verify](https://github.com/VerifyTests/Verify), [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet), [odiff](https://github.com/dmtrKovalenko/odiff), [pixelmatch](https://github.com/mapbox/pixelmatch), [ImageMagick compare](https://usage.imagemagick.org/compare/), [dssim](https://docs.rs/crate/dssim/latest)
- Godot: [godot-ci](https://github.com/abarichello/godot-ci), [godot-export](https://github.com/firebelley/godot-export), [GodotEnv](https://github.com/chickensoft-games/GodotEnv), [GoDotTest](https://github.com/chickensoft-games/GoDotTest), [gdUnit4Net](https://github.com/godot-gdunit-labs/gdUnit4Net), [Hot Reload Live](https://forum.godotengine.org/t/tool-hot-reload-live-runtime-c-hot-reload-for-godot-4-7/144535), [GodotSharp.SourceGenerators](https://github.com/Cat-Lips/GodotSharp.SourceGenerators)
- Analyzers and build: [BannedApiAnalyzers help](https://github.com/reduckted/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md), [dotnet/roslyn-analyzers](https://github.com/dotnet/roslyn-analyzers), [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer), [dotnet/reproducible-builds](https://github.com/dotnet/reproducible-builds)
- Floating-point evidence: [Go issue 18354](https://golang.org/issue/18354), [Blender sinf M1](https://projects.blender.org/blender/blender/issues/107829)
