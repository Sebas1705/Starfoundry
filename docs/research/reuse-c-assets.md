# Reuse research, area C: assets

Date: 2026-11-24. Scope: 3D models, textures and materials, sky and planets, icons, fonts, UI art, VFX and audio. Docs only; no game code was changed.

How this was done: licence terms were read from the official pages through a page-to-text fetch (quotes below are from that text, so wording may be abridged) or from web search summaries. Anything not read at source is marked **unverified**. Nothing was downloaded. Prices and pack sizes can change; re-check on the day of purchase.

## Summary table

| Our asset area | Best candidate | Verdict | Effort saved (rough) |
| --- | --- | --- | --- |
| UI font (none today, Godot default font) | Inter (UI) plus Exo 2 or Rajdhani (headings), JetBrains Mono (numbers); all SIL OFL | ADOPT | Small code, big polish: about 1 day, replaces "no font at all" |
| Sound effects (procedural `SoundBank`, never listened to) | Kenney Sci-fi Sounds (70 OGG) and Interface Sounds (100 OGG), CC0 | ADAPT (as raw material, layered with our synth) | 2 to 4 days of tuning by ear, better first impression |
| Music | Incompetech (CC BY 4.0) or a paid pack | LEARN now, ADOPT later | Large (composing from scratch is the most expensive thing on this list) |
| PBR textures (our `Textures.cs`: panels, rust, rock, ice, glass) | ambientCG and Poly Haven (CC0) | ADAPT | Medium; keeps hand-made ones where role colour matters |
| Icons (about 100 glyphs, 204 ids) | Keep ours; Lucide (ISC) and Material Symbols (Apache 2.0) as a gap-filler; Game-icons.net (CC BY 3.0) only if attribution is accepted | IGNORE (replace) / LEARN | Low: the set exists and is tested |
| Ship and station modules, machines | Kenney Space Kit and Space Station Kit (CC0) as kitbash and reference; Quaternius Ultimate Space Kit (CC0) | LEARN (and ADAPT single pieces) | Low to medium; style mismatch (low-poly toy) |
| Blender pipeline | Spaceship Generator (MIT) and own geometry-nodes scripts; Blender itself is GPL, output is ours | ADOPT as tool | Medium; speeds the Blender pilot |
| Sky, nebula, stars | Keep `Ambience.cs`; study wwwtyro space-3d (Unlicense) | LEARN | None needed; ours already works |
| Planets and atmosphere | Zylann atmosphere shader (licence file not read), remijean 3D planet generator (MIT) | ADAPT / LEARN | Medium if planets get a close look |
| VFX (exhaust, lasers, shields, explosions) | Few good free 3D options; GDQuest art is NC | LEARN, keep ours | Low |
| Free-licence tracking | New `ASSETS.md` plus CI check (below) | ADOPT | Prevents a licence accident |
| Paid kits (Synty, KitBash3D, Sonniss, music packs) | See paid section | Case by case | Highest for music and audio |

Headline: the biggest wins are not models. They are (1) a real UI font, (2) real recorded or CC0 SFX layered with our synth, (3) music, and (4) CC0 PBR texture detail. For ships, the game's colour-coded role language is ours; third-party kits are best used as reference and as occasional greeble, not as the base.

## Policy conflicts to resolve before adopting anything

1. `docs/prototypes/audio.md` says "The game has no audio assets and none may be added", and `ui-icons.md` says "no external art". Adopting any file needs an ADR that changes this rule and states the licence gate below.
2. AGENTS.md forbids copying names, lore, art, text or exact numbers from other games. CC0 and permissive kits are not "other games" art, but assets must not be rips of a game. Record the origin of every file (see tracking section).
3. The game ships as MIT with a public repo, so every committed asset must be redistributable in source form. That rules out "royalty-free but no redistribution" licences (most paid packs, Sonniss) for files committed to git; they would need a private store plus a build-time fetch, and then the repo is no longer fully buildable by others.

## 3D models

### Kenney Space Kit and Space Station Kit
- URLs: [Space Kit](https://kenney.nl/assets/space-kit), [Space Station Kit](https://kenney.nl/assets/space-station-kit), [Kenney support/licence](https://kenney.nl/support).
- Licence: CC0. Kenney: "Yes, all game assets on the asset pages are public domain licensed (CC0)", "Attribution is not required." Space Kit page: 150 files, v1.0 (2020); Space Station Kit: 90 files, tags space, station, sci-fi, interior.
- Format: search results say OBJ, FBX, DAE, STL and glTF (itch.io listing); not downloaded to confirm.
- Style: flat-colour, low-poly "toy" look with a shared palette. Clean and readable but not semi-realistic.
- Replaces: could stand in for belts, props, turrets, ship parts; does not match our role colours or hull profile system.
- Effort: M (re-colour to our palette, re-pivot to the 8x4x8 module grid).
- Risks: style mismatch next to our beveled procedural modules; every game using Kenney looks alike (the owner's "do not repeat" concern is about work, not look, but a distinctive look has value).
- Verdict: **LEARN**, ADAPT single props (crates, pipes, antennas) if a gap appears.

### Quaternius Ultimate Space Kit and related packs
- URLs: [Quaternius](https://quaternius.com/), [Ultimate Space Kit](https://quaternius.com/packs/ultimatespacekit.html).
- Licence: listed as CC0, "free to use in personal and commercial projects" (page does not reproduce the licence text). 92 models, animated, FBX, OBJ, Blend, glTF; includes planets, enemies, characters. The index also lists Ultimate Spaceships, Spaceships, Ultimate Modular Sci-Fi, Modular Sci-Fi Megakit (the page showed no licence there; **unverified per pack**, check each page).
- Style: low-poly stylised, same mismatch as Kenney, but a `.blend` is included, which makes re-colouring and kitbashing easy.
- Replaces: enemy variants (scout, striker, brute), props, a few planets.
- Effort: M. Risks: style, per-pack licence check. Verdict: **LEARN / ADAPT** a handful of pieces.

### Poly Haven models and Sketchfab CC0
- [Poly Haven licence](https://polyhaven.com/license): "Our assets are all licensed as CC0", "You can use our assets for any purpose", credit "although it is appreciated". Website content other than assets needs permission; scraping is prohibited; the API has separate terms (matters for the Blender MCP integration).
- Poly Haven models are mostly scanned props and nature, almost no sci-fi. Verdict: **IGNORE** for models; use for textures and HDRIs (below).
- Sketchfab: licence is per model (CC0, CC BY, CC BY-NC...). Not researched model by model; **unverified**. Rule: only CC0 and CC BY, record the author and URL in `ASSETS.md`, never CC BY-NC or "standard" Sketchfab licence. Verdict: **LEARN**; treat as reference.

### Paid options (models)
- Synty POLYGON sci-fi packs: sold under their own [One Time Purchase Licence](https://syntystore.com/pages/one-time-purchase-licence); the excerpt read did not mention Godot or redistribution of source files. The Unity Asset Store EULA applies to Unity Store purchases; whether Unity-bought assets may be used in Godot is **unverified** (a forum thread asks the same question, with no answer found). In any case, paid packs cannot be committed to a public MIT repo. Same low-poly look. Price not checked.
- KitBash3D, Blender Market and Superhive kits (e.g. Starforge Nexus, Sci-Fi Greeble Kit Vol. 1, PanelForge): royalty-free commercial licences, typically "use in your work, no redistribution of the raw files". Fine for a Blender source file that stays private and exports baked `.glb` details, but then the `.glb` in a public repo is a redistribution of a derivative; **needs reading each EULA** (not done). Prices not checked (typically tens of dollars). Verdict: **IGNORE for now**; revisit for greeble only if hand-modelling detail turns out slow.
- Epic Fab and Megascans: per [CG Channel](https://cgchannel.com/2026/11/epic-has-made-megascans-free-again-for-good-this-time), Megascans is free again permanently under the Fab Standard License, usable "in any game engine". Fab licence forbids redistributing assets standalone, so same public-repo problem as above. Verdict: **IGNORE** (CC0 alternatives exist).

## Textures and materials

### ambientCG and Poly Haven
- URLs: [ambientCG licence](https://docs.ambientcg.com/license/), [Poly Haven licence](https://polyhaven.com/license).
- Licence: CC0. ambientCG: "You can copy, modify, distribute and perform the assets, even for commercial purposes, all without asking permission", "You can include the raw files in your project, for example a video game." Credit optional (suggested text given).
- Format: PNG or JPG maps (colour, normal, roughness, metalness, AO, displacement) at 1K to 8K; Godot-ready as `StandardMaterial3D` inputs. Poly Haven also offers EXR HDRIs.
- Style: photoreal scans; metal plates, painted metal, concrete, rust, rock. Good as a *detail* layer under our role colours (multiply or overlay), not as hero colour.
- Replaces or improves: `Textures.cs` panels, rust, rock, ice (rock and ice especially, where noise looks flat). 1K textures only, to keep the repo small: 10 materials at 1K is roughly 20 to 60 MB uncompressed PNG; use VRAM compression on import and consider Git LFS (**size not measured**).
- Effort: S to M. Risks: realistic look next to flat-shaded hull colour; repo size. Verdict: **ADAPT** (adopt as normal/roughness detail, keep procedural colour logic).

### Procedural textures in Blender
- Blender Cycles/EEVEE can bake our own trim sheet with geometry/shader nodes at no licence cost. This is the planned pipeline in the retrospective section 5. Verdict: **ADOPT** as the way to author the module trim sheet.

## Skybox, nebula, stars, planets

- Ours (`Ambience.cs`, 516 lines, sky shader, two star layers, nebula) already works and is parameterised per star system. Replacing it with baked cubemaps would lose that variation. Verdict: **keep**.
- [wwwtyro/space-3d](https://github.com/wwwtyro/space-3d): licence The Unlicense (public domain), generates procedural space scenes in the browser with WebGL. Output and export not confirmed. Verdict: **LEARN** (a reference for nebula layering) and a possible way to bake a few cubemaps if we ever want hero skies.
- [remijean/godot-3d-planet-generator](https://github.com/remijean/godot-3d-planet-generator): MIT, Godot 4, three shaders (planet, clouds, atmosphere), seven examples (terrestrial, ice, lava, sand, gas, star). Effort S to M. Risk: style, perf at many planets. Verdict: **ADAPT** if planets get a close-up role.
- [Zylann/godot_atmosphere_shader](https://github.com/Zylann/godot_atmosphere_shader): README says Godot 4.3+, two versions (fake and light scattering), fog from inside. The page only linked `LICENSE.md`; an earlier search said MIT; **unverified, read LICENSE.md first**. Compatibility renderer shows no atmosphere. Verdict: **ADAPT** after checking the file.
- Other Godot shader gems (Deep Space Shader on Godot Shaders, Mahamaya Sky, KAtmospheres, tiffany352 Starlight): licences not confirmed in search; **unverified**. Godot Shaders site content is often CC0 per shader, but each page states its own.
- Poly Haven HDRIs: CC0, but they are Earth skies, not space. **IGNORE**.

## Icons

- Ours: SVG glyph registry in `src/Starfoundry.Ui/IconGlyphs.cs`, 24x24 grid, tinted, tested for coverage (`IconCatalogTests`). This is a finished, consistent set tied to game ids.
- [Game-icons.net](https://game-icons.net/about.html): CC BY 3.0, "Icons made by {author;}. Available on https://game-icons.net"; about 4,180 icons (Lorc 1,429, Delapouite 2,022, others). Attribution per author is mandatory and appears in a credits screen. Style is bold, filled, single colour, quite different from our outlined 1.75 stroke set. Verdict: **IGNORE** for the base set; **ADAPT** only for a few missing concepts, converting to our stroke style by hand.
- [Lucide](https://github.com/lucide-icons/lucide/blob/main/LICENSE): ISC ("Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted", notice must stay), plus MIT for the Feather-derived icons. 24x24 stroke 2 round-cap SVG: the closest match to our style. Verdict: **ADAPT** for generic UI actions (settings, close, chevrons, lock) if missing; ship the ISC notice in credits.
- [Phosphor](https://phosphoricons.com/): MIT claimed; the page text did not confirm the count or formats; **unverified**. Six weights. Same use as Lucide.
- [Material Symbols](https://developers.google.com/fonts/docs/material_symbols): Apache License 2.0, 2,500+ glyphs, SVG and PNG available. Needs NOTICE-style attribution. Style differs (rounded Google look). Verdict: **LEARN**/gap filler.
- [Kenney Game Icons](https://kenney.nl/assets/game-icons): CC0, 105 files, tags icon, gamepad, joystick, prompt, interface. Useful for **key prompts** (keyboard, mouse, gamepad glyphs). Format not confirmed. Verdict: **ADAPT** for the key panel if it needs controller glyphs.
- Net: do not re-draw 100 icons with a third-party set. Fill only real gaps, and keep one visual language.

## Fonts

The game currently uses Godot's built-in font (the theme sets only a size; no font file in `game/assets`). Spanish needs full accent coverage; all of the following support Latin Extended (**coverage of every needed glyph not tested**).

| Font | Role | Licence | Notes |
| --- | --- | --- | --- |
| [Inter](https://github.com/rsms/inter/blob/master/LICENSE.txt) | UI body, tooltips | SIL OFL 1.1, "Copyright (c) 2016 The Inter Project Authors", no Reserved Font Name listed in the fetched text | Excellent small-size legibility. Variable and static. |
| Exo 2 | headings, HUD titles | SIL OFL (reported by a font-roundup blog and Google Fonts; **page-level confirmation failed**, read the font's `OFL.txt` before commit) | Techy but readable; ready 100 to 900 weights |
| Rajdhani | HUD numerals, labels | SIL OFL (same caveat) | Squared, condensed; good for tight tiles |
| Orbitron | logo and big titles only | SIL OFL ([Font Squirrel listing](https://www.fontsquirrel.com/fonts/orbitron), copyright 2009 Matt McInerney) | Too wide for body text; also very common in sci-fi games |
| JetBrains Mono | numeric readouts, console | SIL OFL 1.1 (per a third-party attribution page; confirm in repo) | Tabular digits help gauges |

OFL key terms (from the Inter file): fonts may be "used, studied, modified and redistributed freely" and may not be sold by themselves; modified versions cannot use a Reserved Font Name. Embedding in a game and shipping the TTF are fine. Keep each font's `OFL.txt` next to it.

Effort S: add TTF under `game/assets/fonts`, set `Theme.DefaultFont` in `UiTheme.Create`, rerun `tools/ui-audit.ps1` (every text width changes, so the fit audit is the real cost). Verdict: **ADOPT** Inter plus one display face.

## UI art

- [Kenney UI Pack Sci-fi](https://kenney.nl/assets/ui-pack-sci-fi): CC0, 130 files, 2D buttons, panels, sliders (v2.0). It is a 9-slice sprite approach; our UI is code-drawn (`UiTheme.Card`, `Tile`) with a tested look. Verdict: **IGNORE** (LEARN from slider and panel shapes).
- No third-party UI art is needed; our theme already adapts to es and en.

## VFX

Particles are built in code (nine particle usages found in `game/*.cs`). What exists elsewhere:
- [GDQuest godot-4-vfx-assets](https://github.com/gdquest-demos/godot-4-vfx-assets): code (scripts, scenes, shaders) MIT; **art assets are CC-BY-NC-SA 4.0**, so non-commercial and share-alike. Do not take the art. Shaders only: verdict **LEARN** (and ADAPT code with MIT notice).
- VFX 2D Free (shield, beam, shockwave) is CC-BY-4.0 and 2D; not useful for a first-person 3D game. **IGNORE**.
- Godot store packs (trails, Reeboo Stylized VFX free sample is listed MIT, Juice Shaders Lite) were seen only in search listings; licences and 3D suitability **unverified**.
- Honest conclusion: engine trails, laser beams and shield bubbles in 3D are small shaders; writing them (as done) costs hours, adopting an uncertain pack costs as much in licence checking. Verdict: **keep ours**, read GDQuest shaders for techniques (soft particles, fresnel, depth-fade).

## Audio

### Sound effects
- [Kenney Sci-fi Sounds](https://kenney.nl/assets/sci-fi-sounds): CC0, 70 OGG files, tags sci-fi, space, engine (per search; engines, lasers, explosions). [Kenney Interface Sounds](https://opengameart.org/content/interface-sounds) (100 OGG, listed CC0, mirror at OpenGameArt; official Kenney page not fetched). [Kenney Digital Audio](https://kenney.nl/assets/digital-audio) 60 files.
- Replaces: `SoundBank.Render` recipes for UiClick, UiConfirm, lasers, explosions, thrusters. The audio doc admits nobody has listened to the synth output yet, so the cheapest test is A/B in `--audio-test`.
- Recommendation: do not delete the synth. Keep it for procedural, pitch-varying and looping beds (reactor, machines, engine tiers), which a recorded file cannot do. Use recorded one-shots for impacts, UI and explosions. Effort S to M. Risks: stock-sound recognisability; many games use the same Kenney clicks; mono 22 kHz synth versus 44.1 kHz stereo files (loudness balance needs the existing `SoundCatalog` gains). Verdict: **ADAPT**.
- Freesound: per-sound licence; filter CC0 (the filter exists per guides). CC BY needs author credit; CC BY-NC is forbidden. Verdict: **ADOPT** as source for sounds Kenney lacks, one file at a time recorded in `ASSETS.md`.
- OpenGameArt: per-item licence incl. CC0, CC-BY, OGA-BY, GPL, CC-BY-SA (the FAQ page returned 403; guidance from [search summary](https://opengameart.org/content/faq) says GPL and CC-BY-SA are awkward for closed games; for an MIT repo CC-BY-SA would force derivatives under SA). Accept CC0, CC-BY, OGA-BY only.
- [Sonniss GameAudioGDC](https://80.lv/articles/the-gameaudiogdc-bundle-by-sonniss): per reports, royalty-free, no attribution, commercial use OK, but you cannot republish sounds unchanged and not for AI training. The licence text was only seen on an unofficial mirror; **unverified**. Redistribution of raw files in a public repo is likely forbidden. Verdict: **IGNORE for committed assets**; consider for private bake.
- Generators: sfxr-family tools (jsfxr, Bfxr, ChipTone) produce original SFX with output free to use; tool licences not read here (**unverified**). Verdict: **LEARN** (our synth already does this).

### Music
- [Incompetech](https://incompetech.com/music/royalty-free/faq.html): CC BY 4.0, credit template "Title Kevin MacLeod (incompetech.com) Licensed under Creative Commons: By Attribution 4.0"; a paid no-attribution licence exists (price per track not confirmed). Style is broad and recognisably "stock". Ambient and sci-fi tracks exist but none were checked.
- OpenGameArt music: per-item licence, same rules as above.
- Our procedural music exists (`Ambience` volume bus controls it). It is a good ambient bed; hand-composed or paid music is the real gap. Verdict: **LEARN** now; **ADOPT** a CC BY or paid track set when an actual soundtrack is wanted. Paid libraries (Epidemic, Artlist) are subscription licences that do not allow committing files; unverified, not recommended.

## Blender tools for generating content

- [a1studmuffin/SpaceshipGenerator](https://github.com/a1studmuffin/SpaceshipGenerator): "Released under the MIT License", Blender 2.80+, 40 commits, global-space cube UVs, replaceable hull textures. Last commit date not stated; may need fixes on current Blender. Output is ours. Verdict: **LEARN/ADOPT** as a base for greeble placement, but its look is chunky greebled hulls, not our clean modules; mainly useful for enemy and wreck variety.
- Greeble and kitbash add-ons: [Kit Ops](https://superhivemarket.com/products/kit-ops-3-pro) (paid versions listed GPL; a free version exists, licence not read), [PanelForge](https://superhivemarket.com/products/hard-surface-addon-bundle) (add-on code GPL v3, its corridor models carry a separate royalty-free licence). Blender add-ons that use Blender's Python API must be GPL; a GPL tool does not make its output (meshes you create) GPL. This holds for tools whose licence does not state an output restriction; the **kit and model content shipped with an add-on is licensed separately and may restrict redistribution**, so use the tool, not its bundled parts. Verdict: **ADOPT as tools** only after reading each licence file; hard-ops style tools were not found in search.
- Our own deterministic scripts (`tools/blender/<module>.py`, see retrospective section 5) remain the primary route; they cost no licence risk and match the colour-role and pivot rules.
- [Blender MCP](docs/retrospective-2026-11-24.md) integrations (Poly Haven, Sketchfab, AI generation): Poly Haven API has separate terms; AI-generated models carry per-service terms and unclear copyright. Verdict: allow Poly Haven only; treat AI meshes as **IGNORE** unless the service licence is read.

## Licence-tracking proposal

### Files
- `ASSETS.md` at repo root, one row per third-party file or folder (only third-party; our own procedural output needs no row).
- `assets/third_party/<source>/` or beside the file, keep the original licence text (`LICENSE.txt`, `OFL.txt`).
- A machine-readable `assets.lock.json` is optional; start with a Markdown table that CI parses.

### Format of `ASSETS.md`

```markdown
| Path | Name | Author | Source URL | Licence (SPDX) | Licence URL | Retrieved | Attribution text | Modified | Used for |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| game/assets/fonts/Inter-Regular.ttf | Inter | The Inter Project Authors | https://github.com/rsms/inter | OFL-1.1 | https://github.com/rsms/inter/blob/master/LICENSE.txt | 2026-11-24 | none | no | UI body |
| game/assets/audio/sci_fi_laser_01.ogg | Laser 1 | Kenney | https://kenney.nl/assets/sci-fi-sounds | CC0-1.0 | https://creativecommons.org/publicdomain/zero/1.0/ | 2026-11-24 | none | trimmed | mining laser |
```

Rules in the file header: allowed SPDX ids `CC0-1.0`, `CC-BY-3.0`, `CC-BY-4.0`, `OGA-BY-3.0`, `MIT`, `ISC`, `Apache-2.0`, `OFL-1.1`, `Unlicense`; forbidden `CC-BY-NC*`, `CC-BY-SA*`, `GPL` for content, "all rights reserved", unknown. Attribution-required licences (CC BY, Apache, OFL notices, ISC, MIT) also need a line in an in-game Credits screen (generated from `ASSETS.md`).

### CI check (small script, run in the existing workflow)
1. List files under `game/assets/**` (and any `docs` images from third parties) that are not `.glb` generated by our `tools/blender` scripts; fail if a binary asset path is not covered by a row or a folder row in `ASSETS.md`.
2. Fail if a row has an SPDX id outside the allow-list, or empty source URL, author or retrieval date.
3. Fail if an attribution-required licence has no attribution text, or the licence file named in the row is missing.
4. Warn if a file hash in `assets.lock.json` changed without the row's `Modified` field being updated (optional).
5. Run on PRs touching `game/assets/**`, `ASSETS.md` or the credits generator. A PowerShell script like `tools/check-assets.ps1` fits the repo's other tooling.

Generated models (the existing `.glb` files) should be listed once as "own work, generated by `tools/blender/...`" so the check has no blind spots.

## Suggested order (cheap first)

1. Fonts and the credits plus `ASSETS.md` scaffold (S).
2. Kenney SFX A/B against the synth in `--audio-test`, adopt the winners (S to M).
3. CC0 PBR detail maps for rock and metal in the Blender trim sheet (M).
4. Music decision (owner call: licence budget).
5. Planet and atmosphere shaders only when planets matter in gameplay.

## What could not be verified

- Licence text pages for Quaternius per pack, Phosphor, Exo 2, Rajdhani, JetBrains Mono, Zylann shader, OpenGameArt FAQ (403), Sonniss official terms, Synty and KitBash3D EULA text.
- Pack contents beyond counts (nothing was downloaded), glTF quality, scale and pivots, whether Kenney Game Icons ship SVG.
- Prices of all paid products; Unity-bought assets in Godot.
- Repository size impact of textures and audio.
- This research assumes that licence summaries given by web search are correct; none of this is legal advice.
