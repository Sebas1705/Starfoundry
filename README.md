# Starfoundry (working title)

A first-person space factory-builder: mine asteroids, refine, automate production lines, design modular ships and stations, and defend an expanding logistics empire against a dormant alien sentinel network.

> **Status: specification phase plus early prototypes.** This repository holds the design specs, research notes and decision records needed to build an original game in the genre of *Main Sequence*, *Factorio*, *Satisfactory* and *X4*, and prototypes in Godot: [belts](docs/prototypes/belts.md), [data-driven machines](docs/prototypes/machines.md) a [playable loop](docs/prototypes/playable-loop.md) (mine, refine, build, automate, research) a [ship that flies with mining and build ranges](docs/prototypes/world-and-ship.md) a [generated galaxy to scan and jump through](docs/prototypes/galaxy-exploration.md) and the [Remnant threat with turrets and combat](docs/prototypes/threat-and-combat.md), all driven by a command layer.

This is an **original design**. Genre-level mechanics (mine, refine, belt, automate, research, modular ships) are common ground; names, lore, art, numbers and systems here are our own. See [docs/research/reference-main-sequence.md](docs/research/reference-main-sequence.md) for what we studied and why we do not copy it.

## Where to start

| Read | For |
| --- | --- |
| [docs/playtest.md](docs/playtest.md) | Run the game, what to try, and how the play log compares with the scripted player |
| [docs/00-vision.md](docs/00-vision.md) | Pillars, audience, scope, non-goals |
| [docs/specs/01-core-loop.md](docs/specs/01-core-loop.md) | The moment-to-moment and long-term loop |
| [docs/specs/02-factory-logistics.md](docs/specs/02-factory-logistics.md) | Machines, belts, power, throughput rules |
| [docs/specs/03-ship-station-builder.md](docs/specs/03-ship-station-builder.md) | Modular ships and stations |
| [docs/specs/04-world-exploration.md](docs/specs/04-world-exploration.md) | Procedural systems, anomalies, wrecks |
| [docs/specs/05-research-progression.md](docs/specs/05-research-progression.md) | Tech tree and tiers |
| [docs/specs/06-threats-combat.md](docs/specs/06-threats-combat.md) | The Remnant, waves, fleet defence |
| [docs/specs/07-multiplayer.md](docs/specs/07-multiplayer.md) | Co-op model |
| [docs/specs/08-ui-ux.md](docs/specs/08-ui-ux.md) | Controls, HUD, blueprint tools |
| [docs/specs/09-technical-architecture.md](docs/specs/09-technical-architecture.md) | Engine, simulation core, saves, networking |
| [docs/specs/10-data-model.md](docs/specs/10-data-model.md) | Data-driven content schemas |
| [docs/specs/11-roadmap.md](docs/specs/11-roadmap.md) | Prototype, vertical slice, Early Access |
| [docs/specs/12-open-questions.md](docs/specs/12-open-questions.md) | What is still undecided |
| [docs/specs/13-command-layer.md](docs/specs/13-command-layer.md) | Commands, validation, log, replay (implemented for belts) |
| [docs/adr/](docs/adr/README.md) | Architecture decision records |

## Conventions

Everything committed is in English. See [AGENTS.md](AGENTS.md).

## License

[MIT](LICENSE)
