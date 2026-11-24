# Reuse study B: the Godot ecosystem (UI and game-framework pieces)

Researched 2026-11-24 for the question "what already exists that we can reuse instead of building it again?". Scope: Godot 4.x addons, templates and C# libraries for the UI and framework layer. Simulation, art and game-design references are covered by other studies. This is a research note, not a decision: anything that closes off an alternative still needs an ADR.

## Method and honesty notes
- Facts about repositories (license, last push, stars, archived) come from the GitHub API on 2026-11-24 (via the `gh` CLI or the REST API). "License" is the SPDX id GitHub detected from the LICENSE file; I read the license text only for DebugDraw3D, where GitHub reported no id (it is MIT). I did not audit dependencies' licenses or bundled art beyond what is stated.
- Feature, Godot-version and C# statements come from the README or docs pages cited, read through a page summariser. Where a page was silent I write **unverified**; I did not install or run any addon (downloaded code is untrusted, nothing was cloned or built).
- "Works on 4.7" means the README or release notes say so, or says a floor (4.4+, 4.5+) that 4.7 satisfies. A floor is not proof: Godot 4.7 has [documented breaking changes](https://godotengine.org/releases/4.7/) and I did not test any addon against it.
- The Godot Asset Library is being replaced by a new Asset Store in 4.7 ([80.lv](https://80.lv/articles/godot-4-7-has-been-released)); several listings I saw point to `store.godotengine.org`. Prefer the GitHub repo as the source of truth.
- Everything here is secondary reading. No claim about how an addon "feels" in play.

## The finding that shapes every verdict
Our architecture splits a Godot-free simulation (`src/Starfoundry.Sim`, never references Godot, ADR 0002) and a Godot-free UI-logic library (`src/Starfoundry.Ui`: `Loc*.cs`, `KeyHints`, `HudMode`, `Markers`, `SaveSlots`, `ResearchTreeLayout`, ...), tested with xUnit, from a thin Godot layer (`game/`, about 9k lines for the pieces surveyed). The save file is a deterministic command log.

Almost every addon found is GDScript and keeps its state inside nodes and resources. That conflicts with our design in two ways: (1) the state (inventory, quests, dialogue progress) would live outside the deterministic core and the command log, and (2) every call from C# crosses the GDScript boundary through untyped `Call`/signals or a hand-maintained wrapper. So "adopt an addon that owns state" is mostly a bad fit; "adopt a presentation or tooling piece" and "learn the design" are the realistic wins. The libraries that fit best are C#-native and Godot-free (Chickensoft `LogicBlocks`, `Serialization`, `SaveFileBuilder`) and the C# test framework.

**Honest headline:** very little of what we built by hand has a mature C#-friendly replacement. The genuine reuse opportunities are small: in-engine testing (gdUnit4Net), CI setup (setup-godot), an optional dev console and debug drawing, a credits screen pattern, and possibly a dialogue runtime if the story grows branches. The big hand-built pieces (menus, HUD, build menu, research tree, markers, theme, text fitting, localisation tables, flight and walking) are not worth swapping out.

## Summary table

Effort saved is an estimate of work avoided or code deleted, not a measurement. Integration effort: S under a day, M a few days, L a week or more.

| Our piece | Best candidate | Verdict | Effort saved | Integration |
|---|---|---|---|---|
| Main menu, new-game, pause, end screens, options scaffolding | [Maaack Godot Game Template](https://github.com/Maaack/Godot-Game-Template) (MIT, GDScript) | LEARN (credits scene and scene-loader pattern only) | Low: our screens are themed, es/en, audited. Would add a credits scene, which we lack | M to port, S to copy the idea |
| Settings store, key rebinding (`SettingsStore`, `Play.Keys`, `KeyPanel`) | [G.U.I.D.E](https://github.com/godotneers/G.U.I.D.E) (MIT, GDScript, unofficial C# wrapper) | LEARN | Low: we have rebinding and key hints; G.U.I.D.E ships no remap dialog either | L (changes the whole input layer) |
| Gamepad glyphs / input prompts (not built) | [Controller Icons](https://github.com/rsubtil/controller_icons) (MIT, CC0 art, C# has engine-bug warning) | ADAPT only if gamepad play is planned | Medium for gamepad prompts, zero today | S to M |
| Inventory (in progress) | [GLoot](https://github.com/peter-kish/gloot) (MIT, GDScript, no C#) | LEARN (grid, constraints, prototype trees); do not adopt | Low: state must live in the sim | L |
| Tooltips | Engine built-ins (`_MakeCustomTooltip`, BBCode `[hint]`); [Nested Tooltips](https://godotengine.org/asset-library/asset/4260) (MIT, C#, 4.4) | IGNORE (keep ours), LEARN nested tooltips | Negligible | S |
| UI theme, text fitting (`UiTheme`, `UiFit`, `UiAudit`) | Nothing comparable found | KEEP ours | none | none |
| Icons (`IconGlyphs`, 24x24 SVG by code) | [game-icons.net](https://game-icons.net/about.html) (CC BY 3.0, attribution) | IGNORE | none; we draw our own on purpose | none |
| Localisation (`Loc*.cs`, 1.5k lines) | Godot gettext/PO workflow ([docs](https://docs.godotengine.org/en/4.6/tutorials/i18n/localization_using_gettext.html)) | LEARN; do not migrate now | Would delete table code but loses Godot-free testability; no missing-key tool exists | L |
| Story dialog (`StoryDialog`, 89 lines) | [Dialogue Manager](https://github.com/nathanhoad/godot_dialogue_manager) (MIT, official C# wrapper) | IGNORE now; ADAPT if story gets branching | Low today, medium later | M |
| HUD modes and cockpit pages state machines (`HudMode`, 69 lines) | [LogicBlocks](https://github.com/chickensoft-games/LogicBlocks) (MIT, C#, Godot-free) | LEARN; try on one new feature | Low: the hand-rolled machines are tiny | M |
| Wiring the `Play.*.cs` partial classes | [AutoInject](https://github.com/chickensoft-games/AutoInject) (MIT, C#) | LEARN, do not adopt | Low, invasive | L |
| First-person body, flight, custom collision (`Play.Body`, `Play.Flight*`) | [Simplified Flight Simulation](https://github.com/fbcosentino/godot-simplified-flightsim) (MIT), [FPS Player Controller C#](https://github.com/PolarBears-studio/player-controller) (MIT) | IGNORE | none; both are stale and not 6DOF with our inertia and oriented collision | n/a |
| HUD cards, gauges, key panel, markers and edge arrows, compass | Nothing established for Godot 4 (only paid itch.io packs) | KEEP ours | none | none |
| Build menu with tiles | Nothing found | KEEP ours | none | none |
| Research tree view (`ResearchTreeView`, `ResearchTreeLayout`) | [tree-maps-addon](https://github.com/ToxicStarfall/tree-maps-addon) (MIT, GDScript, 32 stars) | IGNORE | none | n/a |
| Quest and goal panel (`Play.Goals`, `Objectives`) | [LiteQuest](https://forum.godotengine.org/t/tool-litequest-system-modular-quest-framework-for-godot-4-7-net-8-c/144612) (MIT v1.0, C#), [quest-system](https://github.com/shomykohai/quest-system) (MIT, GDScript) | IGNORE | none; goals are read-only views of sim state (ADR 0034) | n/a |
| Save slots and command-log save (`SaveSlots`, `ScenarioStore`, `PlayLog`) | [SaveFileBuilder](https://github.com/chickensoft-games/SaveFileBuilder) + [Serialization](https://github.com/chickensoft-games/Serialization) (MIT, C#) | LEARN; optional ADAPT for the file layer (gzip stream) | Low | S to M |
| Camera / screen effects | [Phantom Camera](https://github.com/ramokz/phantom-camera) (MIT, GDScript, manual C# wrapper) | IGNORE now | n/a: our camera is the player's head | M |
| Dev console / debug (flags, `Play.Dump`) | [LimboConsole](https://github.com/limbonaut/limbo_console) (MIT, GDScript, community C# wrapper); [DebugDraw3D](https://github.com/DmitriySalnikov/godot_debug_draw_3d) (MIT, native) | ADAPT for dev builds only | Medium convenience, no code deleted | S to M |
| Scene-level / UI tests (none in engine today) | [gdUnit4Net](https://github.com/godot-gdunit-labs/gdUnit4Net) (MIT, C#, lists 4.7) | ADOPT (client tier), keep xUnit for sim | Adds coverage we cannot get otherwise | M |
| CI Godot setup, export | [setup-godot](https://github.com/chickensoft-games/setup-godot) (MIT), [godot-ci](https://github.com/abarichello/godot-ci) (MIT), [godot-export](https://github.com/firebelley/godot-export) (MIT) | ADOPT setup-godot (or GodotEnv) when CI builds the game; LEARN godot-ci | Low-medium, replaces hand-rolled installs | S |
| Procedural audio (`AudioManager`) | Nothing relevant | KEEP ours | none | none |
| Screenshot and movie flags | Engine built-in movie writer | KEEP ours | none | none |
| Modding hooks (`ContentLocator`) | [Godot Mod Loader](https://github.com/GodotModding/godot-mod-loader) (CC0, GDScript) | IGNORE (LEARN if modding becomes a goal) | none | n/a |

## Candidates in detail

Fields: URL, license, Godot support, C# support, activity (GitHub API, 2026-11-24), maturity, overlap, what it would let us delete, effort, risks, verdict.

### Complete game templates
**Maaack's Godot Game Template** ([repo](https://github.com/Maaack/Godot-Game-Template), [README](https://raw.githubusercontent.com/Maaack/Godot-Game-Template/main/README.md))
- License MIT. Godot: README targets 4.7 and says "4.4+ compatible". C#: README does not mention C#; code is GDScript (**C# usage unverified**; calling it from C# would be through signals and `Call`). Last push 2026-11-24, 1,756 stars, not archived. The most mature template, with addon form (`addons/`) and setup wizard.
- Provides main menu, options menus, pause menu, credits, scene loader with loading screen, music controller, UI sound controller, auto-save, example level manager.
- Overlap: `MainMenu`, `MenuScreen`, `EndScreen`, `SettingsStore`, pause. We already have all of these, themed with `UiTheme`, localised and covered by `--ui-audit`. Scenes would need re-theming and string-table integration. A credits scene is the one thing we do not have (no credits code found in `game/` or `src/Starfoundry.Ui`).
- Would delete: nothing worth deleting. Effort S to copy the credits idea, M to import and re-skin the whole thing.
- Risks: duplicated autoloads (music, UI sound) next to our `AudioManager`; GDScript boundary; template-managed project layout. Verdict **LEARN** (credits and loading-screen pattern).

**CSharpGodotTools/Template** ([repo](https://github.com/CSharpGodotTools/Template))
- MIT, C#, last push 2026-06-28, 185 stars. Aims at C# games with ENet multiplayer and visual debugging. Menu coverage described as main menu, options, credits; pause menu not mentioned in what I read. Godot floor stated as .NET SDK 8.0.400; **Godot version support unverified**. Small and opinionated (multiplayer). Verdict **LEARN** only.

**Chickensoft GameDemo / GodotGame template** ([GameDemo](https://github.com/chickensoft-games/GameDemo), MIT, 641 stars, pushed 2026-11-23; `Chickensoft.GodotGame` on [NuGet](https://www.nuget.org/packages/Chickensoft.GodotGame))
- A third-person C# demo showing their architecture (LogicBlocks, AutoInject, save files, tests). A reference, not a drop-in. Verdict **LEARN**: read it before deciding on items 11 in the retrospective.

**brettchalupa/godot_skeleton** ([repo](https://github.com/brettchalupa/godot_skeleton)): public domain per search snippet, language not stated. Only seen in a search result; **everything else unverified**. IGNORE.

### Input, settings, remapping
**G.U.I.D.E** ([repo](https://github.com/godotneers/G.U.I.D.E), [remapping docs](https://godotneers.github.io/G.U.I.D.E/usage/remapping-input))
- MIT, pushed 2026-11-02, 436 stars. GDScript; C# only through the unofficial [G.U.I.D.E-CSharp wrapper](https://phlegmlee.github.io/G.U.I.D.E-CSharp/usage.html) (needs Godot 4.2+; its maintenance **unverified**). Godot version for the core addon not stated on the README.
- Gives input contexts, modifiers, triggers (tap, hold, combo), runtime remapping with collision handling. The docs say there is no ready-made remap dialog, so we would still build the UI. README warns it "hasn't seen a lot of use yet".
- Overlap: our rebinding and key panel. We would swap the input layer for little benefit unless we want contexts and gamepad. Effort L. Verdict **LEARN** (contexts and triggers are a good model if we add gamepad).

**Controller Icons** ([repo](https://github.com/rsubtil/controller_icons))
- MIT code. Bundled art is CC0 (Xelu, plus a few CC0 extras) per the README; the logo uses the Godot logo (CC BY 4.0). Godot 4.1.2+. C#: README warns of "engine bugs affecting the addon's usage" with C# and points to a fork. Pushed 2026-11-17, 454 stars.
- Overlap: `KeyHints` prompts, if we add gamepad. Today nothing, so no saving yet. Verdict **ADAPT** later; the CC0 glyph art may be reusable without the addon (check the actual files before shipping).

### Inventory
**GLoot** ([repo](https://github.com/peter-kish/gloot))
- MIT. Godot 4.4+. README is GDScript only, no C# statement. Pushed 2026-07-14, 996 stars. v3 is not backward compatible with 2.x.
- Features: item prototypes in JSON prototrees with inheritance, grid constraint with rotation, weight and count constraints, equipment slots, UI controls (inventory, grid, capacity, slot), dictionary serialisation.
- Fit: our inventory belongs in the deterministic sim and the command log; GLoot keeps state in nodes. From C# every operation is an untyped call. **Do not adopt.** Worth reading for constraint design and the grid-with-rotation UI, if we ever want a Tetris-style hold. Verdict **LEARN**.

### Tooltips, theme, icons
- **Engine built-ins cover our tooltip needs:** `_MakeCustomTooltip` and the BBCode `[hint]` tag work from C# with no addon ([RichTextLabel docs](https://docs.godotengine.org/en/4.2/classes/class_richtextlabel.html)).
- **Nested Tooltips** ([asset 4260](https://godotengine.org/asset-library/asset/4260)): MIT, C#, Godot 4.4, written for a university course; maturity low, repo activity **unverified**. LEARN only.
- **Tooltips Pro** ([asset 4026](https://godotengine.org/asset-library/asset/4026)): proprietary. IGNORE.
- **Themes and kits:** I found no maintained Godot 4 UI kit that fits a code-built, token-driven theme like `UiTheme`. Kenney's art is CC0 per its usual terms, but I did not find a page confirming the license of a specific UI pack ([OpenGameArt listing](https://opengameart.org/content/game-icons) is for an icon set). **Unverified.** Not needed: AGENTS.md prefers our own art.
- **game-icons.net** is [CC BY 3.0 with per-author attribution](https://game-icons.net/about.html): usable, but our icons are code-drawn on purpose (`docs/prototypes/ui-icons.md`) so IGNORE.

### Localisation
- Godot's own workflow ([gettext docs](https://docs.godotengine.org/en/4.6/tutorials/i18n/localization_using_gettext.html)): PO/POT with editor POT generation, `msgfmt --check`, fuzzy entries ignored at runtime, "better with version control than CSV".
- I found **no third-party tool for missing-key checks** in Godot 4 (search turned up nothing). With PO this is a standard gettext job (`msgmerge`, `msgfmt`); with our C# tables it is a unit test (we probably have one; not verified here).
- Moving `Loc*.cs` to PO would delete about 1.5k lines of table code but would move translation into the engine, so strings could no longer be tested in the Godot-free library and the UI audit would need the engine. It would gain translator-friendly tooling (Poedit, Weblate). Effort L. Verdict **LEARN, not now**. Revisit if a third language or outside translators appear.
- [Dialogue Manager](https://github.com/nathanhoad/godot_dialogue_manager/blob/main/docs/Translations.md) also feeds Godot's `tr` with CSV or PO; consistent with the above.

### Dialogue and story
**Dialogue Manager** ([repo](https://github.com/nathanhoad/godot_dialogue_manager), [C# docs](https://github.com/nathanhoad/godot_dialogue_manager/blob/main/docs/CSharp.md))
- MIT. Godot 4.6+ for v4 (earlier versions for 4.5 and below). Official C# wrapper (`DialogueManagerRuntime`): load a `.dialogue`, show a balloon or fetch lines; mutations as `async` C# methods. Limits: only `[Export]` properties visible, no static classes, built-in C# node scripts must be attached by hand. Pushed 2026-11-20, 4,003 stars, 0 open issues (well maintained).
- Overlap: `StoryDialog` (89 lines) and any future branching conversation. Today a custom 89-line panel is fine. Verdict **IGNORE now, ADAPT if the story needs branching, conditions or voice-over**. Effort M.

**Dialogic 2** ([repo](https://github.com/dialogic-godot/dialogic))
- MIT, Godot 4.5+, GDScript, README has no C# statement (**unverified**). Visual-novel scope, 188 open issues, its README says some parts are alpha/beta. Too heavy for us. IGNORE.

**Yarn Spinner for Godot** ([repo](https://github.com/YarnSpinnerTool/YarnSpinner-Godot)): MIT, C#, pushed 2026-10-22, 218 stars. C#-native alternative to Dialogue Manager; I did not read the docs or the Godot version floor (**unverified**). Candidate if we want a C# runtime; compare with Dialogue Manager if the need arises.

### State machines and dependency injection
**Chickensoft LogicBlocks** ([repo](https://github.com/chickensoft-games/LogicBlocks))
- MIT, pushed 2026-11-16, 319 stars. Hierarchical, serialisable state machines; state-per-record-class; bindings; PlantUML diagram generation; built for AOT. **Does not depend on Godot** (its README does not mention Godot), so it could live in `Starfoundry.Ui` and be unit tested with xUnit. .NET target not stated on the README page (**unverified**; repo uses net8-compatible tooling as far as I know, check before adopting).
- Overlap: `HudMode` (69 lines), cockpit page logic, future inventory UI states. Our machines are tiny and tested; gain is a shared pattern and diagrams. Verdict **LEARN**, trial on one new feature as the retrospective already suggests.

**Chickensoft AutoInject** ([repo](https://github.com/chickensoft-games/AutoInject), [README](https://raw.githubusercontent.com/chickensoft-games/AutoInject/main/README.md))
- MIT, pushed 2026-11-16, 244 stars. Reflection-free node-tree DI with `[Node]` binding, `[Dependency]`, `Provide()`. Needs Introspection source generators and a `_Notification` override on each node; Godot version not stated. Invasive across `Play.*.cs`. Verdict **LEARN, do not adopt**. Nothing is saved until the node graph is big enough to hurt.

**godot-statecharts** ([repo](https://github.com/derkork/godot-statecharts)): MIT, 1,632 stars, pushed 2026-06-26; "supports both GDScript and C#"; README does not name 4.7 (**unverified**). Node-based visual states with guards; fits animation-like logic, not our data-first modes. IGNORE.

### First-person controllers and flight
- **FPS Player Controller [C#]** ([repo](https://github.com/PolarBears-studio/player-controller)): MIT, 4.4+, last push 2025-07-21, 35 stars. Basic walking.
- **Simplified Flight Simulation** ([repo](https://github.com/fbcosentino/godot-simplified-flightsim)): MIT, GDScript, last push 2025-08-24, 312 stars; engines, fuel tanks, thrusters, instruments. Not described as 6DOF; asset-library entry lists Godot 4.1.
- Search found no maintained 6DOF newtonian ship addon for Godot 4 ([search](https://github.com/fbcosentino/godot-simplified-flightsim) is the best hit; others were paid arcade controllers or editor space-mouse plugins).
- Our controller has inertia, attitude in the sim (ADR 0030), oriented collision and chamfer-aware walking. A generic addon would be a downgrade. Verdict **IGNORE**. Nothing exists for 6DOF with a deterministic sim; this stays ours.

### HUD, markers, compass, minimap
Only paid itch.io packs surfaced (compass, minimap, position indicators, tutorial framework), none established or MIT. Edge-arrow clamping is a standard DIY recipe in forum threads. Our `MarkerLayer` plus `Markers.cs` logic is tested and themed. Nothing to adopt. Verdict **KEEP ours**.

### Research and tech tree UI
- [tree-maps-addon](https://github.com/ToxicStarfall/tree-maps-addon): MIT, GDScript, pushed 2026-07-08, 32 stars. Written for a large tech tree; whether it uses GraphEdit I could not confirm.
- Others seen only as listings: Skill Editor (MIT, 4.2), Worldmap Builder (4.0, 0.5), Custom Graph Editor (MIT, 4.5+).
- Our tree is a 52-line tested layout plus a 191-line view with field bands, progress rings and locks. Nothing found does better. Verdict **IGNORE**.

### Quests and goals
- [LiteQuest](https://forum.godotengine.org/t/tool-litequest-system-modular-quest-framework-for-godot-4-7-net-8-c/144612): C#, Godot 4.7+, MIT for v1.0 (paid extended edition), launched Sept 2026, 1 star, early stage.
- [shomykohai/quest-system](https://github.com/shomykohai/quest-system) (MIT, GDScript, 4.4+, pushed 2026-08-05, 478 stars), [Chevifier/QuestManager](https://github.com/Chevifier/QuestManager) (MIT, last push 2024-08, stale), [CodingQuests dialogue-quest-system](https://github.com/CodingQuests/godot-dialogue-quest-system) (MIT, 4.7+, 0 stars).
- Our goals are read-only derived views of sim state with goal types in the sim (ADR 0034), so the state cannot live in an addon. Verdict **IGNORE**.

### Save systems
- **Chickensoft SaveFileBuilder** ([repo](https://github.com/chickensoft-games/SaveFileBuilder)): MIT, C#, pushed 2026-11-16, 30 stars. `ISaveable<T>` composition, gzip JSON stream, async load and save; Godot integration is optional. Pairs with **Serialization** ([repo](https://github.com/chickensoft-games/Serialization), MIT, 74 stars, AOT-friendly).
- Others: [Saveable (C#)](https://github.com/MrRobinOfficial/Godot-Saveable) (MIT, last push 2024-05, stale), SaveFlow Lite (MIT, 4.6, GDScript and C# APIs; **repo not examined**), several GDScript-only slot addons.
- Overlap: `SaveSlots` (49 lines), `ScenarioStore` (92), `ProgressRecord`. A command-log save is already tiny and deterministic. Possible small win: the gzip stream and atomic write layer. Verdict **LEARN**, ADAPT the file layer only if saves grow big. I did not check whether SaveFileBuilder writes atomically.

### Camera and screen effects
**Phantom Camera** ([repo](https://github.com/ramokz/phantom-camera), [C# wrapper page](https://phantom-camera.dev/c-sharp-wrapper))
- MIT, Godot 4.4+ per README, pushed 2026-11-22, 3,674 stars. The C# wrapper is official but maintained by hand, so some properties may be missing from it.
- 3D follow, look-at, framed and third-person modes, tweened transitions between cameras.
- Our camera is the player's eye and cockpit; there is nothing to replace. Possible later use for docking or cutscene transitions. **Contribution policy note:** the project prohibits AI or LLM use in contributions, so any fix we want upstreamed would have to be hand-made. Verdict **IGNORE now**.

### In-game console and debug tools
- **LimboConsole** ([repo](https://github.com/limbonaut/limbo_console)): MIT, GDScript, Godot 4.3 badge, pushed 2026-11-06, 469 stars. Commands registered from GDScript; C# only via a community wrapper on NuGet (not examined). Gives autocomplete, history, aliases, `.lcs` scripts, autoexec. We have flags (`--shot`, `--bench`, `--dump`) but no live console. Verdict **ADAPT** for dev builds if we want cheat and inspection commands; effort S to M, deleting nothing.
- **DebugDraw3D** ([repo](https://github.com/DmitriySalnikov/godot_debug_draw_3d)): MIT (LICENSE read; GitHub lists no SPDX id), C++ GDExtension, pushed 2026-04-04, 1,087 stars. C# API, Godot 4.7 support, exported-binary behaviour and platform list are **unverified** (the README did not load). Would help visualise hit volumes, belts and collision shapes. Verdict **ADAPT** (dev-only). Native binaries must be excluded or handled in export presets.
- [Calinou/godot-debug-menu](https://github.com/Calinou/godot-debug-menu): the repository URL returned 404 through the API, so unverified; Godot's built-in profiler, monitors and visual profiler plus our `tools/Starfoundry.Bench` cover performance. Verdict **IGNORE**.

### Testing
**gdUnit4Net** ([repo](https://github.com/godot-gdunit-labs/gdUnit4Net), moved from `MikeSchulze/gdUnit4Net`)
- MIT, pushed 2026-11-23, 199 stars, 27 open issues. README: master lists Godot 4.7.0 to 4.7.2 with .NET 8 and .NET 10; older lines list 4.5, 4.4 and 4.3. VSTest-compatible (`dotnet test --filter`), parameterised tests, scene runner (mouse, keyboard, signals), Godot log capture, Roslyn analyzers. Tests run without the Godot runtime unless marked `[RequireGodotRuntime]`.
- Overlap: nothing. We have no in-engine tests; the UI audit and scripted shots cover scenes today. Fits retrospective item 10. Risks: a [discussion](https://github.com/godot-gdunit-labs/gdUnit4Net/discussions/350) reports input issues in headless Linux CI (try on the Windows runner first); coexistence with xUnit in the same solution is **unverified**. Verdict **ADOPT** for a client test tier, effort M.
- **Chickensoft GoDotTest** ([repo](https://github.com/chickensoft-games/GoDotTest)): MIT, 159 stars, pushed 2026-11-11. Alternative runner; did not read it. Prefer gdUnit4Net (broader tooling).

### Build, export and CI automation
- [chickensoft-games/setup-godot](https://github.com/chickensoft-games/setup-godot): MIT, pushed 2026-11-10; a GitHub Action to install a given Godot (Mono) version. **GodotEnv** ([repo](https://github.com/chickensoft-games/GodotEnv)): MIT, 588 stars, CLI for Godot versions and addons.
- [godot-ci](https://github.com/abarichello/godot-ci): MIT, Docker images and workflow templates, pushed 2026-10-20, 1,159 stars. [godot-export](https://github.com/firebelley/godot-export): MIT, pushed 2026-05-28, 655 stars.
- Our CI (`docs/ci.md`) builds on `ubuntu-latest` or a self-hosted PC with `actions/setup-dotnet`; export is `tools/package.ps1`. If CI starts building and exporting the game (retrospective), **ADOPT** `setup-godot` for the install step rather than scripting it; use godot-ci as reference for export presets. Whether these ship 4.7.2 Mono builds I did not verify. Effort S.

### Mods and utilities
- **Godot Mod Loader** ([repo](https://github.com/GodotModding/godot-mod-loader)): CC0, GDScript, pushed 2026-08-21, 703 stars. Mod-by-script-override model; our content is data (`content/`, `ContentLocator`). IGNORE unless modding becomes a goal.
- **Chickensoft GameTools** ([repo](https://github.com/chickensoft-games/GameTools)): MIT, resource loading, feature tags, high-DPI helpers; **Log** ([repo](https://github.com/chickensoft-games/Log)). Minor helpers. LEARN.

## Pieces not worth replacing (already good)
- **UI theme, text-fitting helper and UI audit** (`UiTheme`, `UiFit`, `UiAudit`, `tools/ui-audit.ps1`): no equivalent found, and they encode our design tokens.
- **Localisation tables with Godot-free tests** (`Loc*.cs`): moving to PO trades testability for tooling. Keep for now.
- **HUD cards, gauges, key hints, markers, build menu, research tree, cockpit SubViewport screens, icon system**: all UI specifics of this game; no addon matches the design.
- **First-person and flight code with oriented collision**: no addon models inertia, ship attitude from a sim and chamfered walking.
- **Goals, inventory, save, time and research state**: must remain in the deterministic sim; addons keep state in nodes.
- **Procedural audio**, **screenshot/movie flags**, **text fitting**: nothing relevant exists.

## Where nothing exists
- A maintained Godot 4 **6DOF newtonian ship controller** driven by an external deterministic sim.
- A **missing-key check** and es/en parity tool for Godot translations (the gettext tools cover PO only; ours is a unit test).
- A **tech-tree view** with progress rings, field bands and locks as an actively maintained, C#-friendly addon.
- **HUD marker, compass or edge-arrow addons** that are free, maintained and Godot 4 native.
- An **inventory UI + logic** that is C# native and Godot-free (GLoot is GDScript and node-bound).

## Recommended order if we act on this
1. **gdUnit4Net** client tier (M): the only item that adds coverage we cannot get another way.
2. **setup-godot** in CI, when the CI job builds or exports the game (S).
3. **LimboConsole and DebugDraw3D** behind a dev flag (S to M), only if debugging pain justifies it.
4. **Credits scene**, copying Maaack's pattern, not the code (S).
5. **LogicBlocks** trial on the next new state machine, not a migration (M).
6. Revisit Dialogue Manager if the story needs branches, and PO localisation if a third language arrives.

## Open questions I could not settle
- Does the Maaack template or G.U.I.D.E work cleanly from a Godot 4.7 C# project? No source said so either way.
- DebugDraw3D C# API and 4.7 build availability; LimboConsole C# wrapper maintenance.
- LogicBlocks and Serialization .NET target and Godot 4.7 behaviour (both read through README only).
- Kenney UI pack license text (only its general CC0 policy was seen).
- Whether gdUnit4Net runs in the same solution as xUnit without clashing runners.

## Sources
- Repo metadata: GitHub API for the repositories linked above, 2026-11-24.
- [Godot 4.7 release page](https://godotengine.org/releases/4.7/), [80.lv on 4.7](https://80.lv/articles/godot-4-7-has-been-released), [Phoronix on 4.7](https://phoronix.com/news/godot-4.7-released).
- READMEs and docs: [Maaack template](https://raw.githubusercontent.com/Maaack/Godot-Game-Template/main/README.md), [GLoot](https://raw.githubusercontent.com/peter-kish/gloot/master/README.md), [Dialogue Manager](https://raw.githubusercontent.com/nathanhoad/godot_dialogue_manager/main/README.md), [Dialogic](https://raw.githubusercontent.com/dialogic-godot/dialogic/main/README.md), [godot-statecharts](https://raw.githubusercontent.com/derkork/godot-statecharts/main/README.md), [LogicBlocks](https://raw.githubusercontent.com/chickensoft-games/LogicBlocks/main/README.md), [AutoInject](https://raw.githubusercontent.com/chickensoft-games/AutoInject/main/README.md), [SaveFileBuilder](https://raw.githubusercontent.com/chickensoft-games/SaveFileBuilder/main/README.md), [gdUnit4Net](https://raw.githubusercontent.com/godot-gdunit-labs/gdUnit4Net/master/README.md), [Phantom Camera](https://raw.githubusercontent.com/ramokz/phantom-camera/main/README.md), [G.U.I.D.E](https://raw.githubusercontent.com/godotneers/G.U.I.D.E/main/README.md), [LimboConsole](https://raw.githubusercontent.com/limbonaut/limbo_console/master/README.md), [Controller Icons](https://raw.githubusercontent.com/rsubtil/controller_icons/master/README.md).
- Searches (results only, not repos): C# templates, 6DOF flight, off-screen markers, tech trees, quests, save systems, first-person controllers, tooltips, localisation; [Godot gettext docs](https://docs.godotengine.org/en/4.6/tutorials/i18n/localization_using_gettext.html), [G.U.I.D.E-CSharp](https://phlegmlee.github.io/G.U.I.D.E-CSharp), [Phantom Camera C# wrapper](https://phantom-camera.dev/c-sharp-wrapper), [game-icons.net license](https://game-icons.net/about.html).
- Internal: `docs/prototypes/ui-icons.md`, `docs/ci.md`, `docs/retrospective-2026-11-24.md` (items 10 and 11 already propose gdUnit4Net and Chickensoft).
