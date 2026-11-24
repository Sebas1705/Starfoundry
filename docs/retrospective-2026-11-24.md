# Retrospective and development-speed plan (2026-11-24)

Status: proposal. Written after about two weeks of intense work (PRs #52 to #84, ADRs 0026 to 0036) with many parallel agents. Facts about the repo are *measured* (counted today); tool claims come from the linked pages and may be out of date; everything under "Recommendations" is a *proposal*.

## 1. The numbers
- C# lines: game 24.9k, simulation 11.6k, UI helpers 7.0k, tests 24.4k. About 1500 tests; CI takes 1.5 to 3.5 minutes, a full local run 3 to 5.5 minutes.
- 33 PRs merged since #52 (this stretch), 35 ADR files, six log-version bumps (v6 to v16 planned).
- Hand-made content: `ModuleModels*.cs`, `ModuleDetail.cs` and `MachineModels.cs` are about 2.8k lines of procedural meshes; `Loc*.cs` is about 1.5k lines of strings.

## 2. What worked (keep doing)
- **A deterministic integer sim with a command log.** Legacy flags and log versions let every feature land without breaking old replays; the golden-checksum idiom made refactors safe. Parallel branches could not corrupt each other's world.
- **ADRs and specs first.** The owner could review intent before code; later agents reused decisions instead of rediscovering them.
- **Worktrees plus parallel agents.** Independent topics (textures, flight, UI, content) finished in parallel, and integration by rebase was mostly mechanical.
- **Audits that find real bugs by machine.** `MeshAudit` (holes, coplanar faces), the walk and connectivity brute-force tests, `--ui-audit` and the bot tables caught issues no human had reported yet. The StructureBot is a good balance oracle: it exposed, for example, that solid rocks made Relaxed slower than Normal.
- **Single source of truth.** `HullProfile` feeding both the model and the body collision ended a class of "drawn but not solid" bugs.

## 3. What hurt (with causes)
1. **Hand-testing found, in minutes, problems the scripted checks missed**: a cockpit with almost no view, passable bevels, flickering textures, vertical speed streaks, a narrow nav screen, wings through windows, floating reactor parts. Cause: we verify with still screenshots from fixed cameras and the agents never fly or walk the game; temporal artefacts (flicker, streaks, easing) are invisible in a still.
2. **The owner tested a stale build several times.** Their checkout was not updated while they played, so some reports were for already-fixed code and others could not be reproduced (the build-tile overflow, the pass-through at the front).
3. **Integration was a human-in-the-loop bottleneck.** Every branch needed a rebase, a full test run, a push, a PR, a CI wait and a merge, driven by the coordinator. Conflicts concentrated in shared registries: `Loc.cs`, enum appends (`ModuleKind`, `Reject`), `CommandCodec`, the ADR index, `core.json`.
4. **Reserved numbers by chat.** ADR numbers, log versions and command tags were reserved in agent prompts; collisions still needed manual renumbering twice.
5. **Stopped agents cannot be resumed**, so three agents lost their working context when stopped; the recovery was reading half-finished worktrees. Duplicate hand-backs also flooded the thread.
6. **CI was blocked for about a day** by GitHub billing on the private repo; making the repo public removed the problem but the dependency on a hosted-minutes budget remains for forks and private work.
7. **Procedural art by code is slow to iterate and hard to judge.** Looks are tuned by editing C# and re-rendering; quality is limited by what meshes can be hand-built in code, and each fix (bevels, joints, framing) cost an agent round.
8. **The backlog lives in chat.** Feedback items are in messages and specs, not in an issue tracker, so nothing shows what is open, done or verified.

## 4. Recommendations (ordered by value for effort)
Effort: S under half a day, M one to two days, L several days.

| # | Proposal | Effort | Why |
| --- | --- | --- | --- |
| 1 | **A test build the owner can run on every merge**: a CI job that exports a Windows build from `main` (Godot export template in the job) and uploads it as an artifact (public repo: downloadable), plus the same for open PRs. | M | Ends stale-build confusion and my "pull and build your checkout" step; the owner tests the exact merge. |
| 2 | **One integrate script** (`tools/integrate.ps1 <branch>`: rebase on main, build, fast tests, push, open PR, wait for CI, merge when green) and **GitHub auto-merge** (`gh pr merge --auto --squash`) on PRs from agents, with `build-and-test` as the required check. | S | Removes the waiting loops and the coordinator bottleneck; the owner already allows merging when CI is green. Needs the owner to enable auto-merge in the repo settings. |
| 3 | **Test tiers**: fast (unit, codec, small sim) on every push; slow (bots, brute-force walks, 216-order connectivity) in a second job and nightly. Locally `dotnet test --filter`. | S | Cuts the inner loop from 3 to 5 minutes to well under a minute; slow tests still gate merges but run in parallel. Needs a measurement of which tests dominate. |
| 4 | **A numbers ledger** (`docs/adr/RESERVED.md`: next ADR, log version, tags, rules-flag bit, owner branch) updated in the same PR, and per-feature partial registries instead of appending to shared enums and `Loc.cs`. | S | Prevents the renumbering and the append conflicts seen twice. |
| 5 | **Record movies, not only stills**: Godot's Movie Maker mode (`--write-movie`) plus ffmpeg (already installed here) to produce a 10 to 20 second clip for each flight, walk and cockpit script, and a frame-difference check for flicker. | M | Temporal bugs (flicker, streaks, easing, camera kick) become reviewable. |
| 6 | **An owner hand-test checklist per build** (`docs/handtest.md`, short and generated per PR) and **GitHub Issues** for every feedback item with a label per area, closed by the PR that fixes it. | S | The backlog becomes visible; "verified by hand" is tracked separately from "tests pass". |
| 7 | **Visual regression with golden images** for a small set of stable shots (modules row, cockpit seat view, HUD at 720p) using a perceptual diff and a tolerance, on the owner's PC runner (has a GPU) or Mesa software GL on Linux. No turnkey Godot tool was found, so we write the capture-and-compare step. | M | Stops regressions such as the cockpit view or the nav screen from coming back silently. |
| 8 | **Authored 3D art in Blender, delivered as glTF** (see section 5). | L, start with a pilot | The biggest quality lever for ship and station models. |
| 9 | **UI in scenes and Theme resources for new panels** (static layout as `.tscn`, dynamic content in code). | M per panel | Editor preview, quicker iteration on the inventory and designer; code-built UI keeps working. |
| 10 | **In-engine tests for the client** with GdUnit4Net (C# support, CLI runner and JUnit output; v6 needs Godot 4.5 or newer, we use 4.7); keep xUnit for the sim. | M | Covers scene-level logic (panels, input) that today only scripted shots touch. A report says headless Linux runs can have input-handling issues, so try it on the Windows runner first. |
| 11 | **Chickensoft libraries** (state-machine and dependency-injection helpers for C# in Godot) for the cockpit pages, HUD modes and inventory states. | M | Replaces hand-rolled state code; evaluate on one new feature before adopting. |
| 12 | **Strings out of C#** into CSV or PO files (Godot translations) with a missing-key check. | M | `Loc*.cs` at about 1.5k lines is merge-conflict prone and hard to proofread in es and en. |
| 13 | **Split `ModuleModels.cs`** into one file per module kind with a shared kit (frames, windows, panels). | M | 2.8k lines of meshes in a few files is the main source of join and flicker fixes; smaller files make agent work parallel and reviews easier. |
| 14 | **Pin tool versions** (`global.json` for the .NET SDK, the Godot version in one `tools/versions.json`, checked by CI and the integrate script). | S | Prevents drift between the owner's machine, CI and agents. |

## 5. Blender pilot (proposal)
What exists: the `blender-mcp` family links an AI client to Blender through a socket. It has two parts, a Blender add-on that runs a socket server and a Python MCP server, and it can run Python inside Blender; some builds add Poly Haven, Sketchfab and AI model generation integrations. Setup needs Blender 3.0 or newer, Python 3.10 or newer and `uv`; the Claude Code command is `claude mcp add blender uvx blender-mcp`. Several forks share the name, so pick one and read its README.

State of this machine (measured today): Blender is not installed; `python` resolves to the Windows Store stub; `uv` is not installed; `ffmpeg`, `gh`, `node` and `doppler` are present.

Proposed workflow:
1. **Install** Blender (winget `BlenderFoundation.Blender`), Python 3.12 and `uv` (owner approval for installing software).
2. **Two ways to drive Blender**, used together: (a) the MCP for exploratory modelling and previews, interactive and fast to judge; (b) headless `blender --background --python tools/blender/<module>.py` scripts in the repo that generate a module deterministically from parameters and export `.glb`. (b) is reproducible, reviewable in a PR and runs in CI; (a) is how we find the design.
3. **Import to Godot** as glTF with the node-name suffixes Godot documents for collision: `-colonly`, `-convcolonly` and the like (prefer a few primitive shapes over triangle or convex meshes, which the docs say perform better). The docs disagree about suffix case sensitivity between versions, and one Houdini user reported suffixes not recognised after a glTF export, so test the export path first on one piece.
4. **Keep the logical shell in code.** `HullProfile` stays the source of the cross-section, joints and body collision (it is covered by brute-force tests). Blender models the *detail* on top: panels, pipes, console, seat, canopy, engines, furniture. Pieces use the module grid (8x4x8 cells), a fixed pivot, a trim sheet with framed UVs and vertex colours for the existing role colours, so the colour coding and instancing survive.
5. **Pilot scope**: one module (the starter cockpit) end to end: model, UVs, collision empties, import, replace the procedural mesh behind a flag, compare shots and a recorded movie, measure frame cost; decide go or no-go from that result.
6. **Risks**: the MCP can run arbitrary Python (save before use; run it on a scratch copy); downloaded assets need a licence check (CC0 sources such as Poly Haven are fine; Sketchfab and AI-generated assets need per-asset terms) and AGENTS.md forbids copying art from other games; binary `.blend` files bloat git (commit the generator scripts and the exported `.glb`, keep `.blend` working files outside the repo or in Git LFS).

## 6. Godot tooling for agents (proposal, evaluate before adopting)
- Third-party Godot MCP servers exist that run the project, capture screenshots and inject input (for example `Erodenn/godot-mcp-runtime`, Godot Forge, `Coding-Solo/godot-mcp`, and larger editor-control servers). They would let an agent *play* the game (walk, click a cockpit screen, fire the guns) and see the result, which is exactly the gap behind section 3 item 1.
- They are third-party code with the power to run and drive processes. Try one in the isolated agent environment, read its source, pin a version, and keep our own `--*-shot` flags as the reproducible baseline.

## 7. Suggested order
1. Week 1, process (all small): items 2, 3, 4, 6 and 14; then item 1 (test build artifact) and item 5 (movies).
2. Week 1 to 2, in parallel with the feature work already running: the Blender pilot (item 8) and one golden-image set (item 7).
3. Later, per feature: scenes and themes for new panels (9), Chickensoft on one state machine (11), then strings (12), model split (13) and GdUnit4Net (10) if they still pay off.

## 8. Decisions for the owner
- Allow installing Blender, Python 3.12 and `uv` on this PC, and adding the Blender MCP to this environment (pilot, can be removed).
- Enable GitHub auto-merge on the repository and mark `build-and-test` as required.
- Accept a Windows build artifact published from CI on every merge (the repo is public; the build contains no secrets).
- Approve evaluating one Godot MCP server in an isolated worktree.

Sources: [blender-mcp](https://github.com/ahujasid/blender-mcp), [Godot node-type suffixes for imported scenes](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/node_type_customization.html), [GdUnit4](https://github.com/MikeSchulze/gdUnit4), [GdUnit4 on Linux CI discussion](https://github.com/godot-gdunit-labs/gdUnit4Net/discussions/350), [godot-mcp-runtime](https://github.com/Erodenn/godot-mcp-runtime), [Coding-Solo godot-mcp](https://github.com/coding-solo/godot-mcp).
