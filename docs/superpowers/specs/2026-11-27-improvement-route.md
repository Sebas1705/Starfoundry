# Improvement route: the next phases of Starfoundry (proposal)

Status: proposal, 2026-11-27. A roadmap for the owner to accept, reorder or cut. Facts about the repository are *measured* (counted or read today); every estimate, order and risk below is a *proposal*; what nobody has checked is marked *TBD*. It builds on the memory of past plans in [the retrospective of 2026-11-24](../../retrospective-2026-11-24.md), the prototype pages in `docs/prototypes/`, the studies of this round ([resource fields](2026-11-27-resource-fields-design.md), [lighting](../../prototypes/lighting-study.md)) and the open items listed in `docs/specs/12-open-questions.md`.

Effort scale, as in the retrospective, in days of one agent working with the owner reviewing: **S** under half a day, **M** one to two days, **L** several days to a week or more. Risk is the chance that the work goes wrong or has to be redone (low, medium, high), not how hard it is.

## 1. Where the game stands
Measured today (`origin/main` at #120, 121 commits):
- A deterministic integer simulation with a command log of 25 versions, 45 ADRs, about 1,520 test methods in a fast and a slow tier, a scripted structure bot that is the balance oracle (Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56 minutes), seven story levels, difficulty presets, sandbox rules, and a Godot 4.7 client with a procedural look (cockpit, ship and station modules, machines, belts, rocks, HUD, a 3D blueprint editor with floor plans, saved designs and a walkthrough, docking, defence, drones, research).
- An infinite rock field around a finite 12-system galaxy ([ADR 0040](../../adr/0040-infinite-rock-field-generated-on-demand.md)).
- CI with fast and slow test tiers and a benchmark gate. There is **no build artifact** per merge (retrospective recommendation 1 is not done), no release pipeline beyond `docs/packaging.md` run by hand, no crash reporting, no store page.
- `docs/specs/07-multiplayer.md` is a proposal only: no networking exists (*measured*: no network code in `src/` or `game/`; the command log design is the intended base).

### What has never been hand-tested (honest list)
The repository's own pages say so, and the rule of thumb since the retrospective is that a still screenshot or a headless run does not count as having seen or heard it.
1. **Audio**: nobody has listened to it ([audio.md](../../prototypes/audio.md)); the recorded sound table is empty (Kenney refused the automated download, `ASSETS.md`).
2. **Blueprint editor phases 3 to 5**: `--blueprint-check` has 176 headless checks, but `--blueprint-shot` "was reviewed and compiled but not run"; nobody has seen the plan on screen: pointer feel, icon sizes, the design list, the walkthrough, the floor plan at 960x540 and 1920x1080 ([blueprint-editor.md](../../prototypes/blueprint-editor.md)).
3. **The infinite rock field and the visual passes of #117 to #120** (rock silhouettes, interface pass, exterior hardware): checked in the gallery on one 1280 by 720 window, never on other monitors, frame cost not measured ([visual-pass-models.md](../../prototypes/visual-pass-models.md)).
4. **Flight feel, docking, walls and repairs, weapons feel, the inventory**: each page says the numbers are proposals to be tuned by hand; the owner flew an early build (`flight-feel.md` records what was found) but not the current one *as far as the documents show*.
5. **Balance by feel**: the bot bands are met, but nobody has measured a human, an hour of idle time, or the late game beyond the scripted goals.
6. **Headless is not the real thing**: the playability run of 2026-11-26 passed 19 checks headless, and could not test visuals, building through the menu with the mouse, or the fire effects ([playability-verification.md](../../prototypes/playability-verification.md)).
7. **Other screens and hardware**: only a desktop with an RTX 4080 SUPER and the primary monitor have been used for performance; no weak GPU, no Compatibility renderer, no 4K, no touch or gamepad.
8. **Save compatibility across versions in practice**: logs of older versions replay in tests; no real player save has been carried across a release.

### What the studies of this round found
- **Resource field** (study 1): the shipped field is a uniform speckle: 35 rocks in every scanner sphere, a median rock 1.2 cells in radius (a 5-pixel dot at the scanner's edge), no concentration at all. A cluster generator (D) gives 68% empty sectors, 80% of the ore in 3.7% of the sectors, a median rock of 3.2 cells and giants of 12,000 ore, at 4.5 microseconds per sector and under 750 rocks loaded. It needs log version 26 and ADR 0046.
- **Lighting** (study 2): no shadows in play (sun, fill and lamps all have shadows off), ambient 1.35 plus a 0.3 fill so everything reads evenly lit, the star type ignored by the lighting colour, no anti-aliasing on the main viewport. A shadowed key light costs about 0.5 ms in the stress scene (2.28 to 2.77 ms) on the development PC.
- **Floating origin**: documented in ADR 0040 and in the blueprint editor page as not done; the client converts to 32-bit floats at about 60 sites; the jitter reaches 0.06 cells at a million cells from home.

## 2. Principles for the order
1. **Hand-verify before adding.** Five modules of work (editor, visuals, field, flight, audio) stand on a sample of one. The next feature must wait for at least one hand pass.
2. **Do the world and the look together, because they share the renderer.** Instancing rocks, the floating origin, shadows and far impostors all touch the same client code; doing them as one milestone avoids rewriting twice.
3. **Keep the simulation the source of truth.** New game content (trade, factions, story) goes through data and commands; new log versions use the legacy flag idiom so old logs replay.
4. **Reserve numbers by merging.** ADR numbers, log versions and flag bits are taken when work starts but are only safe when merged (retrospective item 4; `docs/adr/RESERVED.md` does not exist yet).
5. **A decision per milestone belongs to the owner**; each table says which.

## 3. Milestone 1: A build the owner can trust (two weeks, mostly hand work and small fixes)
Goal: close the gap between "tests pass" and "the owner played it".

| # | Item | Effort | Risk | Depends on | Owner decision |
| --- | --- | --- | --- | --- | --- |
| 1.1 | **Windows build artifact on every merge** (export templates in CI, upload artifact; retrospective item 1) | M | medium (export templates in the runner, the content copy of `tools/package.ps1`) | none | Accept an artifact published from CI on every merge; public repo, no secrets |
| 1.2 | **Owner hand-test checklist per build** (`docs/handtest.md`, short, one page per area) and GitHub Issues for findings | S | low | 1.1 | Where findings live: issues or the retrospective style |
| 1.3 | **One hand pass** of flight, docking, mining, the editor (phases 3 to 5), the inventory and the new rocks, with the findings turned into a fix list | L (owner time, then M of fixes) | medium: unknown findings | 1.1 | Who plays, how long, which monitors and window sizes |
| 1.4 | **Audio listening pass**: record the first mix, tune levels by ear; import the CC0 recorded set by hand if the download is allowed (download needs the owner's say: filename, source, size) | M | low | none | Whether to keep procedural sound only, or add recorded one-shots |
| 1.5 | **Golden-image shots and flicker/movie checks** (retrospective items 5 and 7) for the cockpit, the modules row and the editor | M | medium: perceptual diff tolerance | 1.1 | None (tooling) |
| 1.6 | **A reserved-numbers ledger** (`docs/adr/RESERVED.md`: next ADR, log version, flag bits; the third flags byte bit 2 is the next free) | S | low | none | None |
| 1.7 | **Pin tool versions** (`global.json`, Godot version file checked by CI) | S | low | none | None |

Exit: the owner has played a full campaign hour on the merged build and the fix list is under ten items of "high" severity.
*Honest limit*: everything here except 1.1, 1.5, 1.6 and 1.7 needs the owner's eyes and ears; no agent can replace them.

## 4. Milestone 2: A universe worth exploring (three to five weeks)
Goal: the owner's request: large voids, dense fields, big rocks; and the look that makes them readable.

| # | Item | Effort | Risk | Depends on | Owner decision |
| --- | --- | --- | --- | --- | --- |
| 2.1 | **Cluster field generator** (ADR 0046, log version 26, flag in the third flags byte, content `field.clusters`, tests, balance run with and without the flag) per the [resource field design](2026-11-27-resource-fields-design.md) | M | medium: log bookkeeping, tuning, late-game ore scale unknown | 1.6 | Gap between fields (800/1,000/1,300), ore scale, giant rarity, radius scale 250%, vertical plane bias |
| 2.2 | **Instanced rocks** (shared variant meshes, MultiMesh per sector, per-instance depletion, two mesh tiers by distance) | M | medium: look parity with the current rock | none (benefits from 2.1) | None |
| 2.3 | **Far cluster impostors and map markers**, scanner readout of cluster, richness and dominant ore | M | medium: depth range beyond the 500-cell far plane | 2.1, 2.2 | Are fields visible before scanning (discovery design) |
| 2.4 | **Floating origin**: a `WorldOrigin` helper, the conversion of the ~60 float sites, a rebase threshold, a numeric test at 5,000,000 cells | L | high: touches 27 client files and cannot be proven without looking at it on screen | 1.1 for the artifact | World limit: keep 4,000,000 cells, or cap at 200,000 until this is done |
| 2.5 | **Lighting pass 1** (done 2026-11-27 with 2.6, client only: [lighting pass](../../prototypes/lighting-pass.md)) (shadowed key light, ambient lowered from 1.35 to about 0.6, star-driven colour, anti-aliasing), per the [lighting study](../../prototypes/lighting-study.md) | M | medium: it changes the look of everything; needs the owner's eyes | none | Look: keep the flat bright style or go to a shadowed one |
| 2.6 | **Lighting pass 2** (done 2026-11-27, see 2.5): rim light, interior model (lamps per module, emissive trims), tone mapping and bloom, sun and planet glare | M | medium | 2.5 | Interior mood (dim and lit by lamps, or bright) |
| 2.7 | **Performance check** of 2.1 to 2.5 on a weaker GPU and the Compatibility renderer (measure; set the draw-call budget) | M | medium | 2.2 | Minimum hardware to support |

Dependencies inside the milestone: 2.1 and 2.2 together, then 2.3; 2.4 can run in parallel in its own branch (it touches many files, so merge it early to avoid conflicts); 2.5 before 2.6.
Risk to watch: **balance of the late game** with much richer clusters (a richness 5 cluster holds about two home galaxies of veins). The bot bands do not cover it; the owner's hand pass (1.3) is the instrument.

## 5. Milestone 3: Depth of play (four to six weeks)
Goal: more to do once the world is big: trade, ship variety, logistics, exploration reasons.

| # | Item | Effort | Risk | Depends on | Owner decision |
| --- | --- | --- | --- | --- | --- |
| 3.1 | **Shipped ship presets** (miner, hauler, gunship, scout of blueprint spec E1) as content data, balanced with the bot | M | low | 1.3 | Which presets, and whether they are free, bought or unlocked by research |
| 3.2 | **Storage crate machine** (optional in the editor request) and **ground stacks that travel with the ship** | M each | medium: new rules touch inventory, drops and ship attitude (ADR 0035, 0038) | 1.3 | Whether storage is a machine, a module or both |
| 3.3 | **Wall skins on station walls** (ship modules have skins; stations do not, `structures.md` section 5) | S to M | low (visual, audited by `MeshAudit`) | none | None |
| 3.4 | **Trading and an economy with prices** (static NPC prices first, a simple market after; `12-open-questions.md` lists "static prices vs simple market") | L | high: a new system with balance, UI and log commands | 3.1 | Static or dynamic prices; sell what, to whom |
| 3.5 | **Ship-to-ship interaction** (hailing, trading, escorts; NPC ships that are not only enemies) | L | high | 3.4 | Scope: NPC only, or the first step of co-op |
| 3.6 | **Exploration reasons**: wrecks and anomalies in the rock field (the scheme supports other kinds; only rocks are generated now), a survey item, rare ore fields as destinations | L | medium | 2.1, 2.3 | The shape of discovery; whether a story drives it |
| 3.7 | **Story depth**: the seven levels are teaching and challenge levels; a campaign arc with beats that use the field | L | medium: content writing in es and en | 3.6 | Tone, length, whether there is a final goal beyond waves |
| 3.8 | **Enemy variety** and the wave ramp for a world with a larger map (threats beyond the home systems) | M | medium: balance | 1.3 | Whether the sentinels also live in the field |

## 6. Milestone 4: Presentation, access and language (three to four weeks, can overlap M3)

| # | Item | Effort | Risk | Depends on | Owner decision |
| --- | --- | --- | --- | --- | --- |
| 4.1 | **Recorded sounds** (the Kenney CC0 set or another permissive set; ADR 0037; the mapping code exists and the table is empty) | M | low (licence work and the download need the owner) | 1.4 | Download approval, music or no music |
| 4.2 | **Accessibility**: UI scale, colour-blind safe palettes for the item and threat colours, reduced motion (the flight FOV kick and rumble have no off switch), subtitles for audio cues, readable focus; a keyboard-only path audit (the key rebinding page exists) | M to L | medium | 1.3 | Target level (a documented list, or a standard such as WCAG-like targets) |
| 4.3 | **Localisation pipeline**: strings out of C# (`Loc*.cs` is about 1.5k lines) into PO or CSV with a missing-key check; a third language as a test | M to L | medium: touches every panel | none | Which languages after Spanish and English |
| 4.4 | **Performance**: a draw-call and node budget, mesh LOD, a frame-time gate in the CI benchmark for the client (the gate exists for the simulation only) | M | medium | 2.2, 2.7 | Minimum hardware |
| 4.5 | **UI in scenes and Theme resources for new panels** (retrospective item 9) and an in-engine test runner (item 10) | M | medium: tool churn | 1.1 | None |
| 4.6 | **Hero models in Blender** where the procedural look hits its limit (the pilot went well, 2026-11-24; one module so far) | L | medium: binary files in git, two-language contract | 1.1 | Binary policy (Git LFS or regenerate in CI) |

## 7. Milestone 5: Release readiness (four to eight weeks, after M2 and most of M4)

| # | Item | Effort | Risk | Depends on | Owner decision |
| --- | --- | --- | --- | --- | --- |
| 5.1 | **Save compatibility policy**: every log version is a promise; a test corpus of saved logs from each release; a visible "this save is from an older version" message; a decision on how long legacy flags are kept | M | medium | 1.1 | How many versions back a save must load |
| 5.2 | **A real installer and signed build**, crash reporting (opt-in), a feedback channel | M to L | medium: signing costs and accounts are the owner's | 1.1 | Store, signing certificate, telemetry policy |
| 5.3 | **Tutorial and first-hour flow** (the seven levels are the base): a hand-tested first hour with the guided steps | M | medium | 1.3, 4.2 | Whether the tutorial is a level or a layer on the HUD |
| 5.4 | **Co-op decision** (`07-multiplayer.md`: host-authoritative commands with checksums): a spike for two clients on one host before committing | L | high: the largest single risk of the project | 3.5 | Go or no-go on co-op for the first release |
| 5.5 | **Early Access checklist** of `docs/specs/11-roadmap.md` (content hours, demo build, store page, age ratings) | L | medium | all | Name, store account, art direction (still open in the roadmap) |

## 8. Cross-cutting threads
- **Tooling and testing**: the fast tier is the inner loop (about one minute locally, a few minutes in CI); keep the slow tier gating merges; add golden images (1.5), a client frame-time gate (4.4) and the artifact (1.1). A weekly "owner plays 30 minutes" is worth more than any new automated test.
- **Documentation**: specs and open questions stay current in the same change as the decision (`AGENTS.md`); the fields study adds items to `12-open-questions.md`.
- **Performance**: simulation cost is not a problem at the sizes tested (a parked ship among 1,500 rocks costs 0.02 ms of a 33.3 ms tick, measured in the fields study); rendering is, from the number of nodes (about 3.7 microseconds per node-built object per frame, `render-performance.md`) and from the lack of instancing for rocks and of an anti-aliasing option.
- **Save compatibility** is a cost that grows with every generator change: the legacy-flag idiom keeps old logs replaying but each field needs its test; ADR 0046 will add one more flag and the bits are running out in the second flags byte (the third byte has bit 2 free).
- **Process**: the retrospective's integrate script and auto-merge (items 2 and 3) are not recorded as done in `docs/`; re-check before M2 starts, because several agents will touch the client at once.

## 9. Suggested order and the first two weeks
1. Start with **1.1, 1.6, 1.7** (small, no opinion needed) and ask the owner for the **hand pass (1.3)** and the **look decision (2.5)** in the same message; these two answers decide M2's shape.
2. In parallel, **2.1 with 2.2** can start on a branch as soon as the owner picks the gap and the ore scale; the generator is a pure function and its tests do not need the screen.
3. **2.4** (floating origin) in its own branch, merged early; it is the riskiest edit by file count.
4. After M2, decide between M3 (depth) and M4 (access and language) by what the hand pass found; they overlap.

## 10. Decisions the owner must take (summary)
| # | Decision | Needed before |
| --- | --- | --- |
| 1 | Accept a CI-built Windows artifact on every merge | 1.1 |
| 2 | Who plays the hand pass, when, on which screens; where findings are recorded | 1.3 |
| 3 | Audio: procedural only, or approve downloading the CC0 recorded set (filename, source, size to be confirmed at the time) | 1.4, 4.1 |
| 4 | Field gap (800, 1,000, 1,300 cells), cluster ore scale, giant rarity, radius scale, vertical bias, discovery by map or by scan | 2.1, 2.3 |
| 5 | Look direction: flat and bright (today) or shadowed with a key light; interior mood | 2.5, 2.6 |
| 6 | World limit while the floating origin is missing (4,000,000 cells or 200,000) | 2.4 |
| 7 | Minimum hardware and renderer (Forward+ only, or Compatibility too) | 2.7, 4.4 |
| 8 | Ship presets, storage crates, trade model (static or market), ship-to-ship scope | 3.1 to 3.5 |
| 9 | Story scope and tone; second language after es and en | 3.7, 4.3 |
| 10 | Accessibility target | 4.2 |
| 11 | Binary asset policy for Blender models | 4.6 |
| 12 | Save promise (how many versions back) | 5.1 |
| 13 | Co-op: go or no-go; store, signing, telemetry, name | 5.4, 5.2, 5.5 |

## 11. Risks of the plan itself
- It rests on the owner's time for the hand pass; without it, M2 to M5 stack unverified work on unverified work (the retrospective's finding 1 again).
- The cluster field changes the feel of the whole world; the numbers in the field study are first guesses and the late-game economy has no oracle.
- Dates are not given on purpose: the estimates are effort, not calendar, and they assume parallel agents as before.
- Everything marked *TBD* in the two studies (engine limits of 4.7, runtime LOD generation, double-precision builds) is unverified.
