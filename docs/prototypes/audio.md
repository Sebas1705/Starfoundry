# Prototype: procedural audio

Status: built (2026-11-21). **Nobody has listened to it yet.** Everything below was checked by tests, by the `--audio-test` level report and by running the game once with the dummy audio driver; none of it was heard. Treat every level, pitch and timing as a first guess to be tuned by ear.

Sound sources (ADR 0037, which supersedes the earlier "no audio assets" rule): every sound can be synthesised from code at startup, and one-shot effects can also be played from recorded OGG files (CC0, from Kenney, listed in [ASSETS.md](../../ASSETS.md)) through the table `game/assets/audio/sfx_map.json` (effect id to files, per-effect `gainDb` and `pitchJitter`; one variant is picked at random). The synthesiser stays for the loops (engine, reactor, machines, ambience, music) and as the fallback when a file is missing or the setting "Sounds: synthetic" is chosen (Settings; saved as `recorded_sounds`, default recorded). `--audio-ab` plays each one-shot synthesised and then recorded, printing `AB <id> synthetic|recorded`, for listening side by side (needs a window and speakers).

**Status of the recorded set: the table is empty.** Kenney's site refused the automated download (HTTP 403), so no recorded file is bundled yet and the setting changes nothing until files are added: drop the chosen OGG files under `game/assets/audio/kenney/<pack>/` with the pack's `License.txt`, add a row each to ASSETS.md, map them in `sfx_map.json` and run the tests. The mapping code was not exercised with a real OGG file, and nobody has heard either version.

## Where the code is
| Part | File |
| --- | --- |
| Synthesis primitives (oscillators, noise, one-pole filters, cyclic reverb, WAV encoding, level measures) | `src/Starfoundry.Ui/Audio/Synth.cs` |
| The recipe of every sound (`SoundBank.Render(Sfx)`) | `src/Starfoundry.Ui/Audio/SoundBank.cs` |
| Sound ids, buses, per-sound mix (`SoundCatalog`) | `src/Starfoundry.Ui/Audio/Sfx.cs` |
| Mixing rules, rate limiter, nearest-N set, silent-flags rule | `src/Starfoundry.Ui/Audio/AudioMix.cs` |
| Players, pools, buses, caching (`AudioManager`, a Node) | `game/AudioManager.cs` |
| What the game tells the manager (a partial class of `Play`) | `game/Play.Audio.cs` |
| `--audio-test` | `game/AudioTest.cs` |
| Tests | `tests/Starfoundry.Sim.Tests/AudioTests.cs` |

The engine-free parts live in `Starfoundry.Ui`, so the maths (length, peak, finite samples, determinism, loop seams, the mixing rules) is covered by xUnit tests.

## Buses and settings
Four buses: **Master**, **Sfx**, **Ambience** and **Ui** (the last three send to Master; a limiter at -1 dB sits on Master against clipping). The Settings page has a slider for each, stored in `settings.cfg`:

| Setting | Key | Default | Drives |
| --- | --- | --- | --- |
| Master volume | `master_volume` (0..1) | 80 % | Master |
| Effects volume | `sfx_volume` (0..100) | 80 | machines, mining, building, combat |
| Music and ambience volume | `ambience_volume` (0..100) | 60 | engine, reactor, room tone and the music (0 turns the music off) |
| Interface volume | `ui_volume` (0..100) | 80 | clicks, toasts, research jingle, fuel beep |

Moving a slider plays a short preview of its bus. The sliders are linear amplitude (`percent / 100`, 0 is silent).

## The sounds
All are mono, 22 050 Hz, 16 bit. One-shots fade in and out over a few milliseconds; every buffer is normalised to a peak of 0.85 (music 0.80) and the per-sound loudness balance is the gain in `SoundCatalog` (dB). "Limit" is the least time between two plays of the same sound.

| Sound | What it is | Length | Limit |
| --- | --- | --- | --- |
| `UiClick` | short high tick, sine 2.2 kHz sliding down | 0.06 s | 0.04 s |
| `UiConfirm` | two rising notes (660 then 990 Hz) | 0.26 s | 0.08 s |
| `UiError` | two low buzzy falling tones (220, 170 Hz) | 0.30 s | 0.2 s |
| `ToastPing` | soft bell, 1.32 kHz with a fifth above | 0.40 s | 0.15 s |
| `ToastThreat` | two falling harsher tones (880, 622 Hz); plays for threat toasts | 0.50 s | 0.5 s |
| `ResearchDone` | rising arpeggio C-E-G-C with echoes and a held chord | 1.5 s | 1 s |
| `EngineLoop` | loop: low-passed noise plus a 55 Hz saw-like tone and a tremolo | 2 s | loop |
| `ReactorLoop` | loop: 60 / 120 / 181 / 302 Hz hum with a slow tremolo | 2 s | loop |
| `RoomTone` | loop: very quiet band-passed noise and 48 Hz (cockpit room tone) | 4 s | loop |
| `FuelBeep` | double beep at 1 kHz | 0.42 s | 3 s |
| `MachSmelter` | loop: low rumble, 44 Hz, sparse crackles | 2 s | loop |
| `MachAssembler` | loop: tick-tick, pause, tick-tick | 1 s | loop |
| `MachCracker` | loop: high-passed noise hiss with a slow swell | 2 s | loop |
| `MachDepot` | loop: two soft clicks and a 70 Hz hum (depots and unloaders) | 1.2 s | loop |
| `MachHum` | loop: 100 Hz hum (labs, drills, other crafters) | 2 s | loop |
| `MineLoop` | loop: 220 Hz buzz with a 40 Hz tremolo and noise | 0.5 s | loop |
| `MinePop` | falling sine pop (item enters the hold) | 0.14 s | 0.1 s |
| `DroneWhoosh` | noise swell that brightens and darkens | 0.9 s | 0.5 s |
| `PlaceThunk` | low thump (120 to 48 Hz) with a noise burst | 0.30 s | 0.05 s |
| `DemolishCrunch` | stuttering noise bursts over a low thump | 0.45 s | 0.08 s |
| `ModuleSnap` | two metallic clicks and a low thunk | 0.24 s | 0.08 s |
| `WaveAlarm` | four alternating siren notes (466 / 349 Hz) | 1.8 s | 4 s |
| `EnemyShot` | falling saw-like zap (1.3 kHz down to 260 Hz) | 0.32 s | 0.06 s |
| `TurretShot` | noise crack, falling sine and a low thump | 0.26 s | 0.05 s |
| `ModuleHit` | metallic clang from four inharmonic partials plus noise | 0.50 s | 0.12 s |
| `Explosion` | noise whose brightness falls fast, over a 62 to 28 Hz boom | 1.8 s | 0.25 s |
| `HullRumble` | low-passed noise and 40 Hz, fast attack, slow decay | 1.1 s | 0.5 s |
| `MusicCalm` / `MusicTense` | 32 s generative pad loops, see below | 32 s | loop |

## How the game uses them
- **UI**: every `UiKit.Button` clicks; toasts ping (`ToastThreat` for the threat category, none for the logistics category, which is mostly refusals); a refused command plays `UiError`; scan, jump and choosing research play `UiConfirm`; finishing a research plays `ResearchDone`.
- **Ship ambience** (only while playing, non-positional): the engine loop's volume follows the thrust (smoothed) and the engine's speed, its pitch the speed (0.75 to 1.65). The reactor hum exists only with a reactor on the ship: its pitch and volume follow the power satisfaction and below 50 % it stutters (random drop-outs, nine slots a second, more often the lower the power). A double beep sounds every 3 s while the fuel left is under two minutes (the `PowerFormat.LowFuelSeconds` threshold of the HUD warning). The room tone always runs, very quiet.
- **Machines**: eight `AudioStreamPlayer3D` slots. Four times a second the game picks the nearest working machines within 40 cells (working = crafting, a lab with research, a drill on a deposit, a depot; turrets, walls and repairers are silent) and gives each a hum by kind; machines that need power and are below half satisfaction are muted. Hums fade in and out (about half a second), start at a random point of the loop, follow their machine every frame and attenuate with distance (unit size 4, max distance 40, with the engine's distance low-pass).
- **Mining**: the buzz loops while hand mining with room in the hold; a pop plays when the hold total rises while mining. The drone whoosh plays when drones are assigned to a deposit.
- **Building**: place thunk (machines and belts), module snap (modules and founding a station), demolish crunch.
- **Threat**: the alarm plays while a wave is warned and when one arrives (at most every 4 s). Turret and ship shots and enemy shots are positional at the shooter; a module hit is positional at the module, a destroyed module explodes there; a hit on the ship's own structure also plays the hull rumble.
- **Music**: two loops of the same length, started together, crossfaded with equal power by a tension value that moves 0.25 per second towards 1 while a wave is warned or alive and back to 0 when it is not. On the Ambience bus (60 % by default, and the loops sit at -14 dB), so it is quiet.
- **Pause**: the pause menu (and the end screens, which pause the game) pause every loop and hum; UI sounds still play.

## How it is synthesised
- No sample data. `SoundBank` builds `float[]` buffers from sines (with optional second and third harmonics and an exponential pitch glide), noise (one-pole low and high-pass), envelopes and tremolo. A tiny xorshift generator with a fixed seed per sound makes every run identical.
- **Loops are seamless by construction**: tones use frequencies that fit a whole number of cycles in the loop (`Synth.Snap`), tremolo rates are snapped the same way, filtered noise runs round the buffer several times so its state wraps, and events inside a loop (crackles, ticks) wrap their tails round the end. The tests check that the jump from the last sample to the first is no bigger than the largest step inside the buffer.
- **Music**: four chords of eight seconds (A minor, F, C, G), each of five notes, each note three slightly detuned sines. Each chord has a 16 s raised-cosine window that overlaps its neighbours by half, so they crossfade and sum to one. A slow tremolo swells every 8 s. A few quiet pentatonic bell notes are scattered at fixed random times, their tails wrapping round the loop. A cyclic reverb (four feedback combs and two all-passes, all wrapping) gives the long tail. The tense variant adds a cold minor second, fewer high notes and a low 55 Hz note that pulses like a heartbeat every 1.6 s (twenty a loop).
- **Playback**: the buffers are rendered once on a background thread at the first `AudioManager` (about 2.5 s of CPU in a debug build), converted to `AudioStreamWav` the first time they are asked for and cached per manager. A sound not yet rendered is simply skipped. One-shots use pools of 10 flat and 12 positional players, so playing a sound never creates a node; loops are persistent players driven with volume and pitch scale each frame; a rate limiter per sound id drops repeats.

## Silence and safety
- `AudioManager.Enabled` is false when any scripted flag is present (`--demo`, `--explore-demo`, `--threat-demo`, `--station-demo`, `--structure-demo`, `--bench`, `--save-test`, `--stress*`, `--shot*` and every `--*-shot` flag, including `--menu-shot`): no manager is created, no sound is rendered, and every hook is a no-op, so screenshots and benchmarks are unaffected. The rule is `AudioFlags.IsSilent`, covered by tests.
- With no audio device the engine uses its dummy driver; the manager does nothing special and throws nothing (it was run with `--audio-driver Dummy`).

## Checking levels without listening: `--audio-test`
```powershell
Godot --headless --path game -- --audio-test [--audio-out=C:\some\dir]
```
It renders each sound to `<name>.wav` (default `user://audio_test/`, in the Godot user folder), prints length, peak and RMS in dBFS and, for loops, whether the seam is smooth, then quits with exit code 1 if a sound clips (peak above 0.95) or a loop has a seam. Open the WAVs in any editor to look at them or, better, listen.

Measured (debug build, 2026-11-21): every one-shot and loop peaks at 0.85 (-1.4 dBFS), music at 0.80 (-1.9 dBFS); RMS from -7 dB (`MachHum`) to -19 dB (`ModuleHit`); all 9 loops seamless; 29 sounds rendered in about 2.6 s.

## Limits and known gaps
- **Not listened to.** Pitches, balance, the siren, the music and the "feel" of every sound are untested by ear. Expect to retune `SoundCatalog` gains first.
- The pad is a simple additive/detuned-sine design at 22 kHz mono: it will sound synthetic and thin compared with a sampled track. There is no stereo and no real reverb on the buses.
- No sound for belts, items on belts, scanning, jumping, the ship's own cannon aiming, walking footsteps, repair bays or enemy movement. Machine hums are chosen by machine kind and "working" state only; starved and blocked machines are just silent.
- The wave alarm repeats every 4 s for as long as the warning lasts; the module-hit sound can be heard from the whole system (max distance 220 cells).
- Drone whoosh plays on assignment, not for each launch (drones are cosmetic in the client).
- Hand-mining buzz does not check the range to the deposit, only that a deposit is assigned and the hold has room.
- At the end of a run with sound the engine prints "ObjectDB instances were leaked at exit" for a handful of audio stream objects (the mixer still holds them while the process quits); it is a shutdown warning, not a runtime error.
- The pause overlay inside the game scene was not exercised by a live session; the loops are paused from `_paused`.
