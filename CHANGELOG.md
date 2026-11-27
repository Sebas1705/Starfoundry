# Changelog

All notable changes to Starfoundry are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the version numbers follow [Semantic Versioning](https://semver.org/) (the number lives in the `VERSION` file; see [docs/release.md](docs/release.md)).

How this file is kept: every pull request that a player can notice adds one line under `## [Unreleased]`, in the section that matches its conventional-commit type (`feat` is Added, `fix` is Fixed, a behaviour change is Changed, `perf` is Changed). Cutting a release renames `Unreleased` to the version and date and opens a new empty `Unreleased`. The published release notes are the matching section of this file.

## [Unreleased]

First public build: no version has been released yet (`VERSION` is 0.1.0). The list below summarises the work merged so far, from the conventional commits of `main`.

### Added

- Windows release pipeline: a one-command local build (`tools/release/build-windows.ps1`), a portable zip, an Inno Setup installer, SHA-256 checksums and a manual or tag-triggered GitHub Actions release workflow that only drafts a release ([ADR 0048](docs/adr/0048-windows-release-pipeline.md)).
- Application icon, product version in the main menu, the Credits page and the window title.
- Clustered rock fields with voids, size classes and giant rocks, drawn with batched meshes ([ADR 0046](docs/adr/0046-clustered-rock-fields-with-voids-size-classes-and-giants.md)).
- Realistic, varied item models, item piles on the ground, a belt preview and more detail on turrets and walls.
- Lighting pass: star-coloured shadowed key light, rim light, glare, interior lamps and a graphics quality setting.
- Hull curves pass: rounded ship shoulders, swept wings, chamfered station roofs and round frames.
- Visual pass on the interface: editor panels, toasts, inventory and star map; exterior hardware for modules, machines and belts.
- Blueprint editor for ships and stations, with saved designs, a walkthrough and foot tools ([ADR 0043](docs/adr/0043-stations-are-edited-as-blueprints-with-applyblueprint.md), [ADR 0044](docs/adr/0044-the-station-interior-is-drawn-on-a-floor-plan.md), [ADR 0045](docs/adr/0045-saved-blueprints-live-in-user-data-and-the-walkthrough-is-client-only.md)).
- Dock modules as the only docking ports, antenna variants, heavy cannon and flared walls ([ADR 0041](docs/adr/0041-ship-and-station-dock-through-a-dedicated-dock-module.md), [ADR 0042](docs/adr/0042-antenna-variants-heavy-cannon-and-flared-walls.md)).
- Infinite rock field generated on demand ([ADR 0040](docs/adr/0040-infinite-rock-field-generated-on-demand.md)); ships hold modules only, automation belongs to stations ([ADR 0039](docs/adr/0039-ships-hold-no-automation-stations-do-and-the-workbench-is-slow.md)).
- Dropping items and picking them up by hand ([ADR 0038](docs/adr/0038-dropping-items-and-picking-them-up-by-hand.md)); personal inventory and reactor fuel slots ([ADR 0035](docs/adr/0035-personal-inventory-container-moves-and-reactor-fuel-slots.md)).
- Key rebinding page, bundled fonts, an external-assets policy and a Credits page ([ADR 0037](docs/adr/0037-external-assets-policy.md)).
- Controls redesign around one ship computer (`Tab`), the arsenal bar and weapons as hardpoint modules with the shipyard designer ([ADR 0032](docs/adr/0032-weapons-as-hardpoint-modules-and-the-shipyard.md)).
- Flight inertia, solid deposits and engine research ([ADR 0033](docs/adr/0033-flight-inertia-solid-deposits-and-hit-volumes.md)); free fire with recoil and resource density ([ADR 0036](docs/adr/0036-free-fire-recoil-hold-overflow-and-resource-density.md)).
- Scenarios and goals, seven campaign levels, new materials and goal types ([ADR 0025](docs/adr/0025-scenarios-and-goals.md), [ADR 0034](docs/adr/0034-optional-material-chains-and-read-only-goal-types.md)).
- Ship attitude, docking alignment, the galaxy with obstacles, world rules and difficulty presets ([ADR 0030](docs/adr/0030-ship-attitude-in-the-simulation.md), [ADR 0028](docs/adr/0028-world-generation-obstacles-and-collisions.md), [ADR 0023](docs/adr/0023-world-rules-and-difficulty.md)).
- Test tiers (fast and slow CI jobs), a determinism analyzer, an agent play harness and movie capture.

### Changed

- Rebalanced pacing (Normal takes about 49 minutes; Hard needs 13 waves at 120 percent enemy scaling).
- Construction happens on the floor of fabrication bays; the starter ship has a workbench instead of a factory ([ADR 0031](docs/adr/0031-construction-rules-fabrication-bays-and-floor-building.md)).
- Performance work on rock rendering and the simulation tick.

### Fixed

- The walking body now respects the drawn hull shell (floor and ceiling chamfers, nose, tail, door frames, station walls).
- Workbench interaction priority, the dock assist cancel, the sandbox cost label and a launch-flag bug found by the headless playability run.
- Warnings about solid rocks, impact speed display and the computer tabs staying in place.
