# Reference study: Main Sequence

Studied 2026-11-19. Source quality: marketing copy, a press release and news posts, plus Steam reviews. **Nothing here is verified by playing the game.** Use it to understand the genre expectations, not to copy.

## What the reference is
- First-person space factory builder, Early Access since 2026-11-12 (Steam, Epic). Solo developer Tumbling Dumpling Studio, publisher Mythwright. About 40 hours of content claimed, 1 to 2 years of Early Access planned. 24.99 USD.
- Reception: Very Positive (about 80% positive from 300+ reviews). Praised: single-player stability, breadth. Criticised: UI polish, cramped factory floor space, performance after expanding to a second asteroid belt.
- Sources: [Steam page](https://store.steampowered.com/app/2487190/Main_Sequence/), [press release](https://www.gamespress.com/Build-an-Intergalactic-Empire-in-MAIN-SEQUENCE-A-Space-Sim-Factory-Bui), [Simulation Daily](https://simulationdaily.com/news/space-factory-builder-main-sequence/), [dlcompare](https://www.dlcompare.com/gaming-news/main-sequence-enters-early-access-the-space-factory-builder-keys-from-11-70-91295).

## Stated mechanics (reference)
- Start with one mining ship in an asteroid belt; scan, mine, refine aboard the ship.
- Build a station as a permanent base; place machines, connect with conveyor belts, plan production lines.
- Research with produced items unlocks ships, station modules, items and upgrades; reach farther systems and richer resources.
- Ships and stations are designed module by module (cargo, reactor, weapons, engines, power) with decoration options; save and share designs; creative mode.
- Automated supply routes between stations; a fleet keeps everything provisioned.
- Procedural star systems with their own logistic challenges, wrecks, black holes, neutron stars.
- An ancient alien defence network treats industry as a threat; waves; defend with the fleet or strike first; endgame battleship.
- Trading with NPCs. Online co-op: each player captains their own ship; roles can be split.
- Planned: factions, more modules, production lines, places, characters, quality of life.

## Not stated anywhere we could read
Recipes, resource list, research tree, module stats, weapon and enemy types, system count, travel model, max co-op players, modding. Our specs must invent these.

## Genre lessons we take
1. The factory loop is the hook; combat is a pressure valve, not the main course.
2. Floor space and belt clutter are a recurring complaint: our builder needs generous, grid-flexible space and strong blueprint tools.
3. UI polish decides reviews for a solo-dev factory game: budget time for it from the start.
4. Performance with scale is the main technical risk: design the simulation for it from day one (see ADR 0002).
5. Co-op is expected and is far cheaper if designed in from the start.

## What we deliberately do differently
See [00-vision.md](../00-vision.md): an original setting and threat, expandable build volumes (stations grow outward, so floor space is never the limit), a deterministic simulation core, and blueprints as a first-class feature.
