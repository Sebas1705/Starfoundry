# Blueprint editor: ships and stations edited in a 3D plan, automation only in stations

Status: proposed (2026-11-26); **phase 2 (the ship editor shell) is built and checked headless, awaiting visual QA** ([prototype](../../prototypes/blueprint-editor.md)); phases 1 and 3 to 5 are not. Owner request: *edit ships and stations in a 3D plan where I can see how it will look and what it will cost before applying; a ship has no on-foot building and no automation (those go only in the station); on a ship you build things for the ship only.* Owner decisions taken in chat: (1) the ship keeps a **workbench**, clearly less efficient than automation, and **with an interface**; (2) in the station **everything** (modules, machines and belts) is edited in the 3D plan. Numbers below are *proposals* unless marked *measured*. ADR numbers and log versions are reserved: **ADR 0039, command log version 20, command tags 63 to 70** (check the ledger; ADR 0037 and 0038 are taken by the assets and world-items work in flight).

## 1. What exists today (measured)
- **Ships** have a designer at a station Shipyard (ADR 0032): a module grid editor with a rotating 3D preview, server verdict and price, applied atomically by `DesignerApply` (tag 46). It sits in the ship computer's Designer tab. Hardpoints are ship-only; the Shipyard is station-only (`ModuleKindIds.AllowedOn`).
- **Stations** are built in first person: the module tool (`N`) places modules in the world, and machines, belts and turrets are placed on the floor of buildable modules (ADR 0031, floor-only building, Fabrication bay). The same first-person floor building works on the starter ship (it has a workbench, and the bot and the first story levels build a bay and a factory on the ship).
- The workbench is `PlayerInfo.ShipRecipe` / `ShipCrafting` (ADR 0031): `F` cycles three recipes (ore to ingot, 2 ingots to plate, ingot to 4 ammo), 2 s each; it has a hover card but no panel.

## 2. New rules
1. **Ships hold ship modules only.** No machines, belts, turrets, walls or Fabrication bays on a ship; the on-foot floor building and the build tool are unavailable aboard a ship. A ship's interior is walkable (cockpit, corridors, cargo, reactor, engine room) but never an automation site.
2. **Stations hold modules plus automation.** Machines, belts, turrets and walls live in station bays. Hardpoints, engines and cockpits stay out of stations (ADR 0029).
3. **The ship workbench** stays as manual crafting and is deliberately inefficient (section 5); it gets a real interface.
4. **All structure edits go through the blueprint editor** (section 3): placing, moving, removing and wiring is a draft that is applied as one atomic command. In-world placement of station modules, machines and belts disappears as the way to build; on foot you inspect, demolish a single thing as a repair/undo shortcut if the draft rules allow, and repair.
5. **Legacy**: worlds from logs older than version 20 keep today's rules (a rules flag, set automatically, like `LegacyBuilding`), so old saves and replays are unchanged.

## 3. The blueprint editor (one editor for ships and stations)
**Where it opens**: for a ship, at a Shipyard (docked, as now: `F` at the shipyard console, or the ship computer's Designer tab when allowed); for a station, at the **station core console** (`F`) or from the ship computer's Designer tab when you are inside the station. The editor is a full-screen mode: first-person view pauses input to the world; the simulation keeps running unless the player opens the pause menu.

**The 3D plan** (the view the owner asked for):
- A free orbit/pan/zoom camera around the structure with an optional **top-down plan** and **section** (deck-by-deck) view, a grid, layer slider, and a **walkthrough preview** (drop the camera inside a module at eye height).
- **Ghosts**: added parts green, removed red, moved yellow, invalid parts red hatch; the real structure stays visible underneath so you see the difference before applying.
- Tools: module palette (the same catalogue as the build menu, with icons and costs), select/move/rotate/delete, copy/paste of a module group, mirror, undo and redo, snap to the module grid.
- **Station interior**: selecting a Fabrication bay (or the core) opens its **floor plan** (top-down) where machines, belts, splitters, turrets and walls are placed with the same ghost rules; belts drag in plan, show flow direction and the throughput readout; recipes and machine settings are set in a side panel (that panel is the machine interface).
- **Cost and stats panel** (always visible): total price of the draft by item with icons (red when the hold or station stock cannot pay, with what is missing), refunds for removed parts (as `DesignerApply` already does: half), net change, build time if any, **power balance**, mass and turn rate for ships (ADR T inertia), thrust, cargo, mining yield, defence rating, and for stations the factory throughput and consumption.
- **Validation list** with icons: errors block Apply (disconnected walkable path, no cockpit/engine/reactor on a ship, overlap, port blocked, machine not on a floor, belt gaps, power deficit as a warning), warnings do not. Clicking an entry frames the part in the 3D view.
- **Apply** sends one command; **Revert** discards the draft; **Save as blueprint** stores the draft in a slot (ties into the designs spec in `2026-11-24-pilot-feedback-round-design.md`, section E1: slots, presets, variants).

**Sim side** (new, ADR 0039):
- `ApplyBlueprint(structure, modules, machines, belts, turrets, walls, settings)` generalises `DesignerApply`: atomic, validated by the same rules the client previews (`SimWorld.CheckBlueprint` returns the same verdict and price, so the preview and the apply cannot disagree; a test pins this), refunds half for removed parts, pays from the hold then the station stock, refuses anything not allowed on that structure kind.
- Hash and validation only add state when a blueprint feature is present (golden-checksum idiom); the legacy gate is `version < 20`.
- The old in-world placement commands stay in the sim for legacy logs and for the bot, but are refused in new worlds for stations and ships where the new rule forbids them (`Reject.UseBlueprint`).

## 4. Interfaces (the second half of "they also need an interface")
- **Machine panel** (stations): click or `F` on a machine in the world, or select it in the plan: recipe list (icons, inputs, outputs, time), inputs and outputs slots with the inventory drag-and-drop, status (running, waiting for input, output full, no power), throughput per minute, enable/disable, and a link to its belt neighbours. Reuses the inventory panel and `UiTheme`.
- **Workbench panel** (ship): the same panel type with one recipe at a time, a small **queue** (up to 5), progress, input and output slots, and a clear "manual, slow" label.

## 5. The workbench is clearly worse than automation (proposal)
| | Workbench (ship) | Smelter and assembler (station) |
| --- | --- | --- |
| Speed | one craft per 8 s | 1 s (smelter), 1 to 2 s (assembler) *(measured values to confirm: smelt_iron is 1 s since the rebalance)* |
| Parallelism | one recipe at a time, no queue overlap | many machines in parallel, belts feed them |
| Inputs | taken from the hold by hand, 1 craft at a time | continuous from belts |
| Recipes | ingot, plate, ammo only | the whole tree |
| Yield | 100 percent, but only while the player is aboard and the workbench is not powered down | any |
Result: enough to bootstrap (found the first station) and to make a few plates, but a factory is an order of magnitude faster. The first station founding cost is reviewed so it is reachable in a few minutes with the workbench plus hand mining.

## 6. Early game and content impact (large)
- The starter ship has **no bay and no factory**. The early loop becomes: mine by hand, craft ingots and plates at the workbench, found the first station, then build the factory in the station's bay through the blueprint editor.
- **Stories and bot**: levels 01 to 03 (pre-built bay factories on the ship), the campaign guided steps, goals such as `fab_bay`, and the StructureBot (which builds a bay and a factory on the ship at 2:12 today) must be rewritten around the station-first loop; the bot applies blueprints instead of placing things. Balance targets stay Relaxed 44 to 50, Normal 48 to 53, Hard 51 to 56; expect a long tuning round (the first station should be founded within about 6 to 8 game minutes).
- **Hold overflow, inventory, shipyard, docking**: unchanged; the shipyard continues to be the ship editor's door.

## 7. Phases
1. **Rules and workbench** (ADR 0039 part 1): ship modules only, station-only automation, the legacy flag, the workbench rate and panel, `Reject.UseBlueprint`, content, the first story levels and the campaign steps, bot rewrite, balance.
2. **Editor shell for ships**: build the full-screen 3D plan on top of the existing designer (orbit/top/section views, ghosts, cost and stats panel, validation list, undo and redo), still applying with `DesignerApply`.
3. **Station modules in the editor**: station module editing through `ApplyBlueprint`; remove the in-world module tool for stations.
4. **Station interior**: bay floor plans for machines, belts and turrets in the editor, the machine panel, belts with flow readout, `ApplyBlueprint` carrying machines and belts.
5. **Polish and removal**: demolish and repair shortcuts, blueprints save and load, walkthrough preview, performance of large stations, docs, tests (determinism, replay of old logs, preview equals apply), hand testing with the owner.
Phases 2 to 5 can ship independently after phase 1; phase 1 changes the feel of the whole early game and is the one the owner should play before the rest is built.

## 8. Acceptance
- A test that the preview verdict and price equal the sim's for random drafts (ships and stations), that apply is atomic (a rejected draft changes nothing), and that old logs replay byte for byte.
- A test that a ship refuses machines, belts, turrets, walls and bays, and a station refuses cockpits, engines and hardpoints, with es and en reject texts.
- `--blueprint-shot` screenshots at 1280x720 and 1920x1080 (es and en) of: a ship in 3D with ghosts and the cost panel, a station top-down, a bay floor plan with belts, and the validation list; `--ui-audit` clean for the editor.
- The bot wins all presets on three seeds inside the bands above.
- Every machine and the workbench have a working panel.

## 9. Risks and open questions
- Phase 1 reshapes the early game and every scenario; budget a balance round and hand play before building the editor.
- A belt editor in a top-down plan with good ergonomics is the hardest UI piece (drag, bends, splitters, undo); start with grid-snapped paths and a clear flow arrow, and polish after play-testing.
- Defaults chosen here, for the owner to change: the workbench is 8 s per craft with a 5-item queue; the editor opens at the station core console; removing a placed part by hand on foot stays allowed as a repair shortcut (not a build tool).
- Interaction with the designs spec (saved designs, variants, antenna types) is deliberate: the blueprint slots and variants arrive in this editor, not in a second one.
