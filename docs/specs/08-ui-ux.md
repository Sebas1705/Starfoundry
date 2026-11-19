# 08 UI and UX

Status: proposal. Reviews of the reference say UI polish is its weakest area, so this is a first-class spec.

## Perspective and movement
- First person on foot and in the cockpit. Optional third-person camera for ship exteriors in the editor only.
- On foot: walk, sprint, jetpack in zero-g, magnetic boots on hulls.
- Ship: assisted flight with mouse and keyboard; gamepad later.

## HUD
- Minimal by default: crosshair, tool, power/heat bars for the current ship or volume, alert meter for the Remnant.
- Contextual panels open on targeting a machine: status, recipe, throughput graph.
- Notifications grouped (production, logistics, threat); never more than three visible.

## Build mode
- **Ghost placement** with snap and rotate hints, colour-coded validity.
- **Bulk placement:** drag-line for belts, area-fill for floors and frames.
- **Blueprint mode:** select, name, save, place as ghosts.
- **Inspect overlay:** toggle layers (items, power, heat, pipes, route lines).
- **Flow arrows** and saturation tint on belts.

## Maps and planning
- System map with bodies, deposits, wrecks, routes, threat nodes.
- Route editor: drag from dock to dock, set load rules in a side panel.
- Station overview map for large volumes (top-down slices).

## Menus
- Research tree with search, filters, a "next best" suggestion, and the exact items missing.
- Inventory with auto-sort and quick-transfer.
- Quick-bar with radial menu on a key hold.

## Accessibility
- Full key rebinding, UI scale (75 to 200 percent), colour-blind safe palettes for item and status colours, subtitles for all voiced lines, reduce-motion option, hold-vs-toggle for sprint and zoom.

## Localisation
Strings externalised from the start; English and Spanish at launch (project convention), other languages added by community later. No text in textures.

## Feedback and audio
- Distinct audio per machine state (working, starved, blocked) with spatial attenuation.
- Rate-limited alarms to avoid fatigue.

## Tutorial
Contextual hints and a short optional scripted first-hour objective chain; no forced modal tutorials.

## Open questions
Controller layout for build mode. See [12-open-questions](12-open-questions.md).
