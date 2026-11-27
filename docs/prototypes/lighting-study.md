# Lighting and shadow study (2026-11-27)

Status: study. Facts about the code are *measured* by reading it (file and line given); numbers are *measured* where a table says so; everything under "Proposal" is a *proposal*. Godot facts that were not checked against the 4.7 documentation or a run are marked *TBD*.

## 1. How lighting works now

The game uses the Forward Plus renderer (`game/project.godot:21`, features `"4.7", "C#", "Forward Plus"`). No renderer, MSAA, scaling or quality setting is set anywhere in the project (grep for `Msaa`, `ScreenSpaceAa`, `Scaling3D`, `rendering/` finds only sub-viewports), so everything runs on engine defaults: no anti-aliasing in the main viewport, no SSAO, no SSIL, no SDFGI, no volumetric fog, no VoxelGI, no lightmaps, no reflection probes.

### 1.1 Flight and station play (the main scene)
`Ambience.Build` (`game/Ambience.cs:224`), called from `game/Play.cs:387` (and from the menu background, `MainMenu.cs:126`), builds all of it:

| Part | Setting | Where |
| --- | --- | --- |
| Background and ambient | A procedural sky shader (stars, nebula, two lit planets, far star, sun disc and streak). Ambient light and reflections come from the sky, energy 1.35, radiance size 128. | `Ambience.cs:229-233`, shader `:13-203` |
| Sun | One `DirectionalLight3D`, energy 1.1, **`ShadowEnabled = false`**, colour = seed-random sun colour lerped 40 percent to white. Direction = the sun disc direction (elevation 12 to 62 degrees, random azimuth). | `:258`, `:381-384` |
| Fill | A second directional light, energy 0.3, specular 0.2, cool blue `(0.62, 0.72, 1.0)`, pointing from roughly the opposite side. No shadows. | `:261`, `:385-389` |
| Tonemap | ACES, exposure 1.05, white 6.0. | `:234-236` |
| Glow | On, intensity 0.6, strength 1.0, bloom 0.08, HDR threshold 1.0, HDR scale 2.0, blend Screen. | `:237-243` |
| Fog | Exponential, density 0.0012 (to 0.0024 at speed), sky affect 0, light energy 0.8, colour = system tint. | `:245-249`, `:307` |
| Adjustment | Contrast 1.06, saturation 1.08. | `:250-252` |
| Motion | 260 additive streak quads, two particle dust layers (350 and 140 particles), unshaded. | `:408-536` |

Key facts:
- **There are no shadows at all in play.** Neither directional light casts them, and the lamps (below) have `ShadowEnabled = false`. Only the gallery tool turns sun shadows on (`Gallery.cs:416`), which is why `station-after.jpg` shows cast shadows and the in-game shots do not.
- **The star type of the simulation is not used by lighting.** The sim has star types (`content/core.json` `world.star_types`: yellow dwarf, red dwarf, orange, blue giant, binary, pulsar) and the map prints the star name (`Play.cs:900`), but `Retint` derives the sun colour, direction and the whole palette from a hash of the system seed (`Ambience.cs:317-390`: sun hue 0.08 to 0.18, saturation 0.15 to 0.45). A blue giant can therefore have a yellow-white sun. The sun is also a sky direction only: there is no sun in 3D space, no distance falloff by system.
- **The sun direction is in world space, the ship rotates.** The ship has attitude (ADR 0030), so the lit side of the ship changes with attitude, correctly. But because the light never moves with the system's star position (it is a direction constant per system), flying across a system does not change the lighting.
- Sky ambient at 1.35 is strong. The fill and the sky ambient together mean faces turned away from the sun are never darker than a mid blue-grey; contrast between lit and unlit sides is low by design (comment at `:260`).

### 1.2 Interior and module lamps
- Every placed module gets **one `OmniLight3D` named `ModuleLight`**, no shadows, specular 0.3, attenuation 1.3, range 7 to 9 cells, energy 0.8 to 2.4, colour by module kind (cockpit cool white-blue, workshop warm, defence red, reactor green, engine orange, shield blue, core amber), with **distance fade 38 + 22** (`ModuleDetail.cs:61-81`). `Models.SetAmber` dims these when power is low (`Models.cs:348`, `:410`).
- Each module also has emissive **glow strips** (`GlowStrip`, `Models.Glow(strip, 1.7f)`, `ModuleDetail.cs:93`+) along the side walls. The shared emissive material helper is `Materials.Light` (`Materials.cs:142-151`); glass has a faint emission and a rim term (`:120-135`).
- Other lights: damage lights on stations (`StationView.cs:407`, orange, range 7), explosion flashes (`Play.Defence.cs:323`, energy 5, range 3.5 times size). All without shadows.
- Editors and previews (`Main.cs:241`, `BlueprintEditor.Scene.cs:57`, `BuildPreview.cs:30`, `ShipDesigner.cs:449`, `Gallery.cs:401`) each build their own simple environment: flat colour background, flat colour ambient (0.4 to 0.95), one sun (1.1 to 1.6) and no shadows except the gallery. These are not the in-game look.

### 1.3 Materials
`Materials.cs` builds `StandardMaterial3D` with generated albedo, ORM (roughness green, metallic blue) and normal maps from procedural texture sets; hull, machine, rock, ore, ice, crystal (emissive texture) and belt materials are cached and shared. Rock roughness and metalness set how the sun reads; the only rim term is on glass. There is no per-material sun-colour response and no emissive bloom budget: emissive strengths are tuned by eye against glow threshold 1.0.

### 1.4 What the shadow story is today
Nothing casts or receives a dynamic shadow in flight. Lit-versus-unlit difference comes only from the directional term and the baked-in ORM/normal detail. A cargo bay, a corridor and open space are lit identically by sky ambient (the interior is open to the same ambient as space; there is no occlusion between the ambient and the inside of a hull).

## 2. What looks flat or wrong (from the shots in `docs/prototypes/visual/` and the cockpit pictures)

I looked at `rock-ship-after.jpg`, `rock-field-after.jpg`, `station-after.jpg`, `ship-after.jpg` and `blender-cockpit-closeup.png`.

1. **The ship is evenly lit** (`rock-ship-after.jpg`): the hull reads as one light blue-grey with almost no terminator, no darkened underside and no shadow of the two rocks that sit in front of and below it. The scene looks like a studio render, not a ship in the glare of a star. The rocks are uniformly dark blue-grey as well, so rock and ship have the same ambient fill. Cause: ambient 1.35 plus fill 0.3 plus no shadows.
2. **No contact or occlusion cues**: panels, struts and dish mounts on `station-after.jpg` float visually; the only dark bits are the cast shadows that exist because the gallery enables them. In play none exist, so the exterior hardware pass (#119) loses a lot of its depth.
3. **The cockpit interior is lit like the outside** (`rock-field-after.jpg`): evenly blue, the sun does not enter the windows as a patch, so day side and shadow side of the cockpit look the same. The closeup (`blender-cockpit-closeup.png`, a gallery render with shadows) shows what is lost: shafts of light on the floor and dark corners. It is the single biggest visual gap between the gallery and the play view.
4. **Light-coloured glow strips and window frames are clipped**: the bright bars across the closeup are near white with bloom; at 0.08 bloom and threshold 1.0 they do not read as emitters, only as bright geometry.
5. **Far scenery has no atmosphere**: fog density 0.0012 is almost invisible; distant rocks have the same brightness and saturation as near ones (`rock-field-after.jpg`), so depth comes only from size.
6. **Sun and planets are in the sky only**: the sky planets are lit by `sun_dir` (a good touch), but they have no glare on the camera or the cockpit glass and the sun disc does not bloom over the horizon of the ship.
7. **Star colour does not tell the system**: see 1.1; every system looks the same family of cool blue-white.

## 3. Ranked proposal

Cost ranks use *frames* on the owner's RTX 4080 SUPER at 720p where I measured (section 4); on a weaker card treat all numbers as lower bounds. Impact is a judgement from the shots.

| Rank | Change | Impact | Cost | Effort | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | **Real key light with shadows on the main sun**, and lower the sky ambient (1.35 to about 0.6) so the shadow side goes dark | very high | +0.5 ms measured (200-machine base, default cascades) | S | Needs a quality switch for weak GPUs |
| 2 | **Star-driven key colour and strength** (read the system's star type; derive the colour from the star colour table, strength from a per-type `light_energy`) | high | none | S | Content change, optional new field in the star type entry |
| 3 | **A cool rim light instead of the generic fill**: fill from behind the camera-relative shadow side, in the sky tint, no shadows | medium | none | S | None |
| 4 | **Interior lighting model**: darker ambient inside, lamps carry the room (section 3.3) | very high | small | M | Needs a "this fragment is inside a hull" signal (see 3.3) |
| 5 | **SSAO (or SSIL) on a quality switch** | medium | within noise at 720p on the 4080 (see 4) | S | Artefacts around thin hardware; TBD on weaker GPUs |
| 6 | **Bloom and exposure pass**: bloom stronger on emissives, a slightly lower white point | medium | none | S | Easy to overdo |
| 7 | **Sun glare and planet atmosphere on the camera** (lens-streak sprite when the sun is in view, fog tint by angle to the sun) | medium | tiny | S | None |
| 8 | **Rock shadows on the ship and eclipse** (section 3.7) | high feel, mostly free with rank 1 | included | S to M | Cascade range must cover rocks 300 cells away |
| 9 | **Volumetric fog and god rays** (section 3.6) | high only inside the hangar and nebula scenes | measured ~0 to +0.1 ms at 720p, but see caveats | M | Quality varies, flicker at low voxel counts, not available in Compatibility |
| 10 | **SDFGI / VoxelGI / lightmaps** | low for the cost | SDFGI +0.2 ms measured at low load but large VRAM and cascade build cost *TBD* | L | Not suited to a world of moving procedural geometry |

### 3.1 Key, rim and fill by star colour (ranks 2 and 3)
- **Today:** one hash-driven sun colour. **Proposal:** add `light_color` and `light_energy` to each entry of `world.star_types` in the content (a pure client concern, so it does not touch the sim or checksums; it must still pass `ContentDb` validation and the content hash if part of the content file, which would change the golden content hash, so put it in a separate client-only table if the hash must stay: a *decision for the owner*). Suggested values (*proposal*, to be tuned by eye): yellow dwarf (1.0, 0.95, 0.82) 1.15; red dwarf (1.0, 0.62, 0.42) 0.9; orange (1.0, 0.78, 0.55) 1.05; blue giant (0.65, 0.78, 1.0) 1.5; binary two lights of different colours at 0.7 each with the second one casting no shadow (cheap); pulsar a cold white-blue at 1.3 with a flicker the player can see on hull highlights.
- **Rim:** replace the generic fill with a fixed rim: a directional light behind the ship relative to the key (direction = key reflected through the ship's up axis), colour = the sky tint, energy 0.25, shadow off, specular on. It gives the hull a bright edge on the dark side without brightening flat faces.
- **Sky ambient:** keep it, but scale energy to about 0.5 to 0.7 and tint it with the star colour at low saturation. The nebula already gives the cool tint.

### 3.2 Shadow cascades (ranks 1 and 8)
Godot's directional shadow has up to 4 splits (`directional_shadow_mode`, `directional_shadow_max_distance`, split offsets), `directional_shadow/size` in project settings (default 4096) and soft-shadow filter quality (`rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality`). Exact 4.7 defaults are *TBD*; the values below are what I propose to try.

- **Mode:** 2 splits (`DirectionalShadowMode.Parallel2Splits`) with `DirectionalShadowMaxDistance` about 120 client units (*TBD*: the client scale of a cell was not checked here; the intent is ship and station plus the rocks within a few ship lengths). Cascade 1 sharp on the ship and station, cascade 2 for rocks near the ship. Beyond that, no shadow, which is fine in space where far shadows are not visible.
- **Resolution:** 2048 for the quality "medium", 4096 for "high". `ShadowBlur` about 1.5 to hide stair-stepping.
- **Bias:** `ShadowBias` 0.03 and `ShadowNormalBias` 1.5 as a starting point *TBD*; the thin chamfered module shells are the worst case for peter-panning.
- **Cost:** measured +0.49 ms on the stress scene (200 machines, 2.28 ms to 2.77 ms average; 439 to 361 fps) with default Godot cascades (4 splits, default distance). A 2-split setup should cost less; not measured separately.
- **Who casts:** machine and module meshes are many small `MeshInstance3D`s. Set `CastShadow = Off` on glow strips, particles, streaks and tiny detail meshes (screens, gauges, pipes) so the shadow pass draws the hull and big hardware only. The shadow pass doubles the draw call count of whatever casts; at the 5,000 machine scale of `render-performance.md` the extra pass would be the dominant cost, so the quality switch must also drop shadows when more than N machines are in view *TBD value*.
- **Receivers:** everything. Do not disable receiving on interior meshes; the inside of the hull shadowing itself is the point (3.3).

### 3.3 Interior versus exterior (rank 4)
Today the interior takes the same sun, fill and sky ambient as the outside. Three cheap tricks, in order:

1. **Shadow does most of the work.** With the sun shadowed (rank 1), the hull shell blocks the sun from the interior, so the inside drops to ambient only, with a bright patch only where the sun comes through a window. This alone recovers the look of `blender-cockpit-closeup.png`.
2. **Interior ambient pulled down.** Because ambient comes from the sky everywhere, inside a hull it is too bright. Options: (a) a reduced global ambient (0.5) and let lamps carry rooms (works because every module already has a lamp); (b) a per-room darkening applied as a `ReflectionProbe` or `VoxelGI` volume per module (costly, see 3.5); (c) a custom shader global `interior_darkness` driven by the camera being inside a hull module (the client already knows when the player is seated or walking inside). Proposal: (a) first, then (c) if rooms next to space look too dark from outside.
3. **Lamp budget and warm and cool contrast.** There is one omni per module, range 7 to 9 cells, no shadows. For a ship of 10 modules that is 10 lamps; a station of 50 modules is 50, but `DistanceFade` 38 + 22 removes far ones. Forward+ clusters lights, so counts of this order are fine; the hard limit is TBD for 4.7 (the project setting for max clustered lights per scene is believed to be in the hundreds, *TBD*). Proposal: keep one lamp per module, add a **second lamp only for the cockpit** (the seat view, a cool light over the screens plus a warm rim) and a **spot light in the corridor ceilings of longer ships** (budget: at most 16 shadowless lamps within 38 cells of the camera, enforced by the distance fade). Doors and airlocks get an emissive strip, no lamp.
4. **Emissive trims over lamps.** Trims (`GlowStrip`) already cost nothing. Raise emissive on floor guide lines and door frames (the cyan lines in `rock-field-after.jpg` already do this well) and use *colour by state* (red for pressure, amber for power low via `SetAmber`).
5. **Bays and hangars.** For the station docking bay, one large warm shadowless spotlight over the bay mouth plus emissive floor markings; volumetric fog (3.6) only there.

### 3.4 Planets, sun glare and eclipse (ranks 7 and 8)
- Planets are in the sky shader and lit by `sun_dir`, with a day/night terminator (`Ambience.cs:132-135`). They are infinitely far, so no parallax and no eclipse. Keep it.
- **Sun glare:** when the sun disc is within about 20 degrees of the view axis, add a screen-space overlay: a soft radial glare and 4 faint streaks, scaled by the dot product and faded by occlusion (a ray cast to the sun direction against rocks and the ship hull). It can be one `TextureRect` or a sprite with additive blending. Cost: one draw. Also raise the fog tint towards the sun colour in that cone.
- **Eclipse of the ship by a rock:** free with rank 1 if the cascade covers the rock-to-ship distance. A cheaper alternative for big rocks beyond the cascade (rock mass 8000, radius 624 units, 2 cells) is a **ray test from the ship to the sun**: if a large rock covers the sun direction (cone test against rock radius, in the client), lower the key light energy over about 0.5 second and tint it red at the edge. This also gives a clear "you are in a rock's shadow" and could later feed a solar-power rule, but that would be a sim change and is out of scope here.

### 3.5 GI choices: SDFGI, VoxelGI, lightmaps versus cheap tricks
- **Renderers:** Forward+ supports SDFGI, VoxelGI, SSAO, SSIL, volumetric fog and SSR; Mobile supports fewer; Compatibility supports none of those screen-space or probe-based GI features and uses a per-object light limit (*TBD exact numbers in 4.7*). The project is Forward+; a Compatibility build for low-end machines would need a separate look, which is a *decision for the owner*.
- **SDFGI** is built for large static scenes; this world is procedural, moving and unbounded, and cascade rebuilds when the camera moves are expensive. Measured +0.2 ms at low load with default settings (see 4) but the visual gain is little in space (there is almost nothing to bounce off), so rank 10.
- **VoxelGI** could light a station interior from emissive trims, but it must be baked in the editor and our stations are built at run time (blueprints, ADR 0043). Not suitable.
- **Lightmaps:** same problem; nothing is baked.
- **Cheap tricks that work with run-time content (ranked):** shadows (3.2), lowered ambient (3.3), shadowless lamps (3.3), emissive trims, SSAO on a quality switch, a vertex-colour or texture "baked" occlusion on module meshes (the Blender cockpit path already allows this), and per-module `ReflectionProbe`s only if the interior looks too bright and flat after the previous steps (reflection probes cost memory and are static unless updated, *TBD*).
- **SSAO/SSIL:** measured within noise at 720p on the 4080. Useful on the dark corners of corridors; keep radius small (about 0.6 unit) to avoid halos on thin hardware. Both are screen space, so they do nothing for things hidden behind the hull.

### 3.6 Volumetric fog and god rays
- Godot's volumetric fog is a froxel volume that scatters light from directional, omni and spot lights, so shadowed sun plus a window gives a light shaft. Cost depends on `volumetric_fog/volume_size` and `volume_depth` (project settings; defaults *TBD* for 4.7).
- Measured here (200 machines, density 0.01, default volume) +0.04 ms, within the noise. This does not say much: the scene has no shadowed sun, so the volume had nothing to scatter. I did not measure it with shadows and windows, and not on a weaker GPU.
- **Proposal:** use it only in two places, the inside of a station bay (small bounded volume, length limit of about 60 units) and nebula regions if a nebula ever becomes a place in the world, with density 0.01 to 0.03. Do not enable it globally: in open space it would wash out the stars. Fake the cheaper outdoor version with a sun glare overlay (3.4).

### 3.7 Tone mapping, exposure and bloom (rank 6)
- ACES with white 6.0 and contrast 1.06 is a reasonable base; ACES desaturates and shifts bright blues towards cyan-white, which probably explains the washed look of the glow strips. Try *Filmic* or *AgX* (`ToneMapper.Agx`, *TBD* whether available in 4.7's enum under that name) on a test scene and compare by eye with the same shots.
- Lower **sky ambient** before touching exposure; otherwise raising contrast just clips the lit side.
- **Bloom:** threshold 1.0 means only values above 1.0 after lighting bloom; most strips are at or under that. Either raise the emissive multiplier of true lamps (reactor, engine plume, door lights) above 2 or lower the HDR threshold to 0.8. Keep `GlowBloom` low (0.08 to 0.15) and use the intensity for emitters. Add `GlowHdrLuminanceCap` in case the sun disc (value 6) leaves a huge bloom *TBD*.
- **Auto-exposure** (`CameraAttributes` exposure): not recommended while the player moves between seat and outside; a slow auto-exposure could give a real "eyes adjust" feel from a dark cockpit to a lit rock, but is a risk for readability of HUD screens in the cockpit (screens are emissive).
- **Anti-aliasing:** nothing is enabled today (no MSAA, FXAA or TAA in the main viewport; sub-viewports use MSAA 2x). Thin hull edges and dish struts shimmer; enabling MSAA 2x or TAA would probably help more than any lighting tweak. TAA interacts with fast motion and the motion streaks, *TBD*.

### 3.8 Quality levels (to make all of this optional)
| Level | Shadows | SSAO | Glow | Notes |
| --- | --- | --- | --- | --- |
| Low | off, ambient 0.9 (today's look) | off | on | for old GPUs and Compatibility *TBD* |
| Medium | 1 cascade 2048, 80 units | off | on | default |
| High | 2 cascades 4096, 120 units | on, radius 0.6 | on | |
| Ultra | as High plus volumetric fog in bays, SSIL | on | on | |
The settings screen (`SettingsStore`) already has a `LowTextures` flag; a `Quality` setting would extend it.

## 4. Measurements

**Setup.** Play copy built from this checkout (`tools/agent/new-play-copy.ps1`), `Ambience.cs` patched in the copy only with environment variables to switch shadow, SSAO, SSIL, volumetric fog and SDFGI on; windowed at 100,100, 1280x720, primary monitor, `--stress=200 --stress-close` (200 machines in view, 300 frames after a 60 frame warm-up, vertical sync off). RTX 4080 SUPER, Windows 11, Godot 4.7.2 Mono. One run per setting (the copy and lock were removed afterwards).

| Setting | avg ms | fps | p95 ms | worst ms |
| --- | --- | --- | --- | --- |
| Baseline (today) | 2.28 | 439 | 2.78 | 3.31 |
| Sun shadows on (engine defaults, 4 splits) | 2.77 | 361 | 2.78 | 2.78 |
| SSAO on (defaults) | 2.24 | 446 | 2.78 | 2.78 |
| SSIL on (defaults) | 2.42 | 412 | 2.78 | 2.78 |
| Volumetric fog on (density 0.01) | 2.32 | 431 | 2.78 | 3.48 |
| SDFGI on (defaults) | 2.51 | 399 | 3.03 | 4.54 |

How to read it: the frame times are quantised near 2.78 ms (a frame-pacing floor on this machine), so differences below about 0.2 ms are noise and a single run per setting is not a benchmark. The clear result is **shadows cost about 0.5 ms (about 20 percent) in a scene of 200 machines**, which is a lot for a scene this light, because the 200 machines are many small casters. The others are indistinguishable from the baseline at this scene and resolution on a high-end card. The `process_ms` column of the tool read implausibly (hundreds of ms) in the patched runs, so I discarded it.

## 5. What was not tested
- No run on a weaker GPU, integrated graphics, 1080p or 1440p; costs scale with resolution for SSAO, SSIL and volumetric fog, so the "free" verdicts are for 720p on a 4080 only.
- No measurement of 2-split shadows, shadow resolution, bias, or distance variants; only the engine defaults.
- No comparison of tone mappers, no look test of any proposed values: all colours and energies in section 3 are untested starting points.
- No Compatibility renderer build and no check of the Godot 4.7 limits on lights per cluster, per object or volumetric volume sizes (marked *TBD*).
- Interior shadow behaviour (sun through windows, shadowed hull interiors) was reasoned from the gallery picture, not run in play. The cockpit has not been hand-tested with shadows on.
- Volumetric fog was measured without a shadowed sun, so its real cost with light shafts is unknown.
- No before-and-after captures were kept: the settings test used the performance tool, not screenshots, so this document uses the existing shots only.
- Nothing here has been seen by the owner on their screen; the "flat" assessment is mine from still images (the retrospective already warns that stills hide temporal artefacts such as shadow flicker and cascade popping).

## 6. Decisions for the owner
1. Shadows on by default for the main sun (about 0.5 ms in the stress scene), with a quality switch and an automatic cut when many machines are in view? (Recommendation: yes.)
2. Star colour from the sim's star type (a client-only table or a content field that changes the content hash)?
3. Lower the sky ambient and accept darker unlit faces (readability of far hardware versus drama)?
4. Is a Compatibility renderer target for older PCs wanted (separate look and no SSAO or volumetrics)?
5. Anti-aliasing: MSAA 2x, TAA or FXAA as default?
