# Roadmap to a fully playable single-player game

Status: proposed (2026-11-21). The owner delegated the decisions ("enrich everything you can until it is fully playable alone"). Builds on the modular structures of ADR 0018. Each part below has its own short plan and is built with subagents.

## Intent
After the structures step the player starts in a working starter ship and can found stations. To be a complete single-player game it still needs: a reason to move (new materials in asteroid veins), energy and fuel (reactors, ice, hydrogen), automation (so the base grows without the player hand-mining), danger for the structures (waves), the body of the player inside the ship (walking, pilot seat) and a game loop around it (start, goal, lose, save).

## Parts, in build order
1. **Materials and veins (sim + content + visuals).** New items: `core:ice`, `core:titanium_ore`, `core:titanium_ingot`, `core:nickel_ore`, `core:nickel_ingot`, `core:quartz`. A deposit gets a `Density` (percent, 25 to 200, integer). Hand mining speed and drill yield scale with density (`ticks per item = base * 100 / density`), and the deposit's look scales with it (rich: many bright ore chunks; poor: a bare rock with few visible chunks). Archetypes: the home system has iron, carbon and a little ice; nickel and ice-rich asteroids a jump away; titanium and quartz farther. Smelter recipes for titanium and nickel ingots; research nodes gate titanium and nickel smelting and the quartz lab recipes; new uses (titanium plate for engines and defence modules, nickel for reactor parts, quartz for sensors and the lab) so every material is spent on something.
2. **Power and fuel (sim + content + HUD).** `core:hydrogen` made from ice in a `core:hydrogen_cracker` crafter machine; a Reactor module produces power (`power_output` from content) while it burns hydrogen drawn from the owner's hold (one unit every N ticks); machines declare `power_use`; a structure's power satisfaction (per mille, integer) scales crafter progress and the engines' thrust; no hydrogen means no power: the ship drifts and the factory stops. The starter ship begins with a small stock of hydrogen.
3. **Automation (sim + content).** A `drone_bay` module kind (unlocked by research) mines deposits within range for the owner at a rate by density and power, delivering ore into the hold: "mining ships" without moving entities in the sim; visuals are a bay with launching drones (client only, cosmetic). Cargo modules and the hold limit the throughput.
4. **Defence and survival of structures (sim + game).** Waves target structures: enemies pick the nearest module of a structure, modules have hp (content) and are destroyed at 0 (machines inside are lost); `defence` modules host turrets and walls (rules already allow them); repair bays work across a structure; a station whose core is destroyed is lost; the ship at 0 hull respawns at the nearest station core (or the home anchor) with its hold emptied. Waves start counting once the player has a station or after a fixed time.
5. **Walking and the pilot seat (client only).** The player has a body inside their ship and stations: seated at the cockpit seat (E) they fly (current controls); standing they walk in first person in the structure's local space, clamped to the volume of the structure's modules and their floors, with the mouse look; building, inspecting and the hold work when standing; the camera and the aim ray use the walking position; E at the seat sits again. Stations are walkable too.
6. **Game loop (game).** A start screen (new game, continue, language, controls), a goal and ending (for example: build a jump-capable station network and defeat the final wave of a Remnant sentinel; the end screen shows the time and counts), a lose and restart path, settings (mouse sensitivity, volume placeholder), and an autosave indicator.

## Non-goals now
Co-op, trading with NPCs, ship design editor, controller support, Linux build, final art and audio.

## Constraints and rules for every part
- Deterministic integer sim; legacy worlds (structures off) and all existing tests keep passing; old logs read.
- New commands and header flags only where needed; content hash changes are expected.
- Every part: tests first, headless screenshots for visuals, a short hand check, docs (ADR or prototype note), PR with CI green.
- Balance is measured with sim-only pacing tests and re-checked by hand play; a bot that plays structures is a later follow-up.
