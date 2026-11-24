# Godot MCP servers for agent play-testing (evaluation, 2026-11-24)

Status: evaluation, nothing adopted in the repository. Goal: let an AI agent start Starfoundry, begin a campaign, walk or fly, click UI and cockpit screens, press keys, take screenshots and read logs, deterministically, from a script it can run, without touching the owner's checkout or save files. Facts below are *measured* (trial on 2026-11-24, Windows 11, Godot 4.7.2 Mono, Node 20+) or *read from source* (marked as such); popularity and dates come from the GitHub API on the same day.

## Summary
- **Recommendation: adopt `godot-mcp-runtime` 4.0.1 (pinned), in attach mode, against a throwaway copy of `game/`.** It did everything in the goal on the first real attempt. A custom `--agent-port` bridge is not needed now; see [What a thin own bridge would add](#what-a-thin-own-bridge-would-add-not-built).
- It needs **no change committed to the game**. In attach mode it temporarily adds an `McpBridge` autoload to the `project.godot` of the copy and removes it again on `stop_project`.
- Two things in our game must be handled: the ship only flies while the OS window has focus (`DisplayServer.WindowIsFocused()`), and our user data folder is shared with the owner's saves (fixed in the copy, see [Steps](#registering-it-for-claude-code-pinned)).

## Candidates

| Candidate | What it is | License | Last push (stars) | Tools | Transport | Runtime play? | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| [Erodenn/godot-mcp-runtime](https://github.com/Erodenn/godot-mcp-runtime) 4.0.1 | TypeScript MCP server, headless scene editing plus a runtime bridge injected as a transient autoload | MIT | 2026-11-13 (103) | 43: `run_project`, `take_screenshot`, `simulate_input`, `get_ui_elements`, `run_script`, `get_debug_output`, `stop_project`, profiler, scene and node editing | stdio; bridge is a localhost-only TCP port | **Yes**: screenshots, key, mouse, UI click by name, text, wait frames, live GDScript | **Chosen, trialled** |
| [yanhuifair/Godot-MCP](https://github.com/yanhuifair/Godot-MCP) | 386-tool server plus an editor plugin (`addons/godot-mcp`) and a runtime bridge autoload on `127.0.0.1:9877` | AGPL-3.0 | 2026-10-23 (52) | 386 incl. 11 runtime (`runtime_input` key only, `runtime_freeze`, `runtime_step`, `runtime_screenshot`, tree, call method) | stdio, SSE or Streamable HTTP (Express) | Partly: key input only (no mouse), needs the **editor running** and the plugin committed into the project | Second most capable, not trialled: needs the editor, commits an AGPL addon, 386 tools is a large surface and token cost |
| [ivanmurzak/Godot-MCP](https://github.com/ivanmurzak/Godot-MCP) 0.42.4 | C# editor addon with an MCP server inside the editor; cloud account link to ai-game.dev | Apache-2.0 | 2026-11-24 (384) | Editor tools in C# (scenes, scripts, play, console) | Editor plugin; has a device-auth cloud flow | Editor-mediated; no injected-input play loop found | Not suitable: editor-bound, cloud connection (violates "no data to external services") |
| [gregario/godot-forge](https://github.com/gregario/godot-forge) 0.1.2 | 8 tools: docs search, script analysis, test runner, `godot_screenshot` | MIT | 2026-04-26 (8) | `godot_screenshot` runs a generated `SceneTree` script that loads a scene, waits 10 frames and saves the viewport | stdio | **No**: one static screenshot of a scene, no input, no session, no user args | Skip for this need |
| [Coding-Solo/godot-mcp](https://github.com/Coding-Solo/godot-mcp) 0.1.1 | The original: launch editor, run project, capture output, simple scene ops | MIT | 2026-04-16 (6756) | 14: `run_project`, `get_debug_output`, `create_scene`, `add_node`, ... | stdio | **No**: no screenshots, no input | Skip (popular, but not a play tool) |

Notes on the first two, from source:
- **godot-mcp-runtime**: only network surface is a `127.0.0.1` TCP listener in the game (`mcp_bridge.gd`, 4-byte length-prefixed JSON frames, per-session token checked on every frame; the token is baked into the injected script in attach mode) and a listener on `127.0.0.1` in the server for the profiler/parent watch. No HTTP client, no telemetry, no outbound calls anywhere in `src/` or in the published `dist/`. Processes: it spawns only the Godot binary (`spawn(godotPath, args)` with argument arrays, no shell). The published npm `dist/scripts/mcp_bridge.gd` and `godot_operations.gd` are byte-identical to the git tag source (checked with `diff`). Runtime dependency: `@modelcontextprotocol/server` and `zod` only (4 packages in `node_modules`). Active (4.0.1 published 2026-11-11, extensive tests, a `SECURITY.md` that states the trust model).
- **yanhuifair/Godot-MCP**: README says the runtime bridge binds `127.0.0.1` only; Express and the SDK are dependencies. Input injection is `runtime_input` (keycode and press/release), so no mouse or UI clicks from the listing. The editor plugin must be installed into the project (`--enable-plugin`), which is a committed change.

## What the agent can touch (godot-mcp-runtime)
From `SECURITY.md` and source: it does **not** sandbox anything.
- `run_project` and `run_script` run code as the owner. `run_script` runs arbitrary GDScript with full SceneTree access (read and write files, use the network, start programs). It asks for confirmation once per project; the client we drive must either display that prompt or the server runs with `GODOT_MCP_DISABLE_ELICITATION=true`, which skips it with a warning.
- The editing tools (`create_scene`, `set_node_properties`, `set_project_setting`, `add_autoload`, ...) can write any file inside the project directory (writes outside it are refused). On the real repo that means source files; on a copy it is harmless.
- Headless tools load the project, so every autoload `_init` and `@tool` script of the project runs. `launch_editor` opens the editor.
- It adds `.mcp/godot-runtime/` (screenshots, owner registry) inside the project and creates or extends a `.gitignore` there with `.mcp/`. In our trial it created `game/.gitignore` (it did not exist). In the real repo that would show up as a change, which is another reason to use a copy.
- Leftovers after `stop_project` in the trial: `project.godot` back to its original text, no `[autoload]` entry, only the `.mcp/` folder and the `.gitignore`.

## Trial

Set-up (everything outside the repo, under the session scratchpad; nothing registered with Claude, no global install):
1. `npm install godot-mcp-runtime@4.0.1 --ignore-scripts` in an empty directory (integrity `sha512-8jzL6Vnp5nQ5MJMceUrWfPUd+nu1y60yEixCz3TlurBcsHAlCV2NHWahd2+RXK+ntMnG55/BPZXm0kOmXSGUBg==`; 4 packages).
2. A copy of the repository at `origin/main` (`git archive HEAD | tar -x`), with `config/custom_user_dir_name` in the copy's `project.godot` changed to `Godot/app_userdata/MCP Trial Copy` so saves and settings never touch the owner's folder. `dotnet build game -c Debug` in the copy (3 s at below-normal priority).
3. A small test client of my own (raw newline-delimited JSON-RPC over stdio, about 25 lines) spawns `node .../godot-mcp-runtime/dist/index.js` with `GODOT_PATH` and `GODOT_MCP_DISABLE_ELICITATION=true`, then a daemon keeps one server and one game alive and takes commands over a local socket (so a shell script can drive it step by step, the way an agent would).
4. Flow: call `run_project {projectPath, attach: true}` and, while it waits, start the game ourselves: `Godot_v4.7.2-stable_mono_win64_console.exe --path <copy>/game --windowed --resolution 1280x720`. The server writes the port and token into the injected autoload before Godot reads the project.

Results (single session, no retries needed apart from my own wrong parameter name on the first `simulate_input`, which the server rejected with a clear message):

| Step | Call | Result | Latency |
| --- | --- | --- | --- |
| Handshake | `initialize`, `tools/list` | server `godot-mcp` 4.0.1, 43 tools | 0.3 s |
| Start | `run_project` `attach: true` + Godot launched in parallel | `bridgePort` returned, log shows `McpBridge: Listening on TCP port N` | 4.3 s including game start |
| Read the menu | `get_ui_elements` | all visible Controls with type, text, rect, disabled; our named nodes (`StartButton`, `TabCampaign`, `SeedField`) are stable handles, auto-named ones (`@Button@51`) shift between runs | 2 to 8 ms |
| Click a disabled button | `simulate_input` `click_element` | `ok:false`, "UI element is disabled" (useful negative) | 5 ms |
| New game, campaign | `click_element` `@Button@51` (Nueva partida), `click_element` `SeedField`, `text` `42`, `click_element` `StartButton`, `wait ms 4000` | campaign scene loaded; each action reports `signals`, `focus` and a diff of appeared, disappeared and changed nodes | 4.1 s (4 s of it is my wait) |
| Screenshot | `take_screenshot` `path_only` | 2560x1351 PNG on disk plus pixel stats (`likelyBlank:false`); `preview` mode returns a 960x540 inline image | 0.6 to 1.1 s |
| Move the player | `key` `W` `hold_ms 2500` | **no effect while the window was unfocused**, then 5.8 of 6.0 cells/s after the window was brought to the front (see below) | 2.5 s |
| Send a key | `key` `M` | toast "Guardar laser" (stow laser) appeared; also `F3` ("HUD: CABINA") and `F4` (screens view) worked | 50 to 70 ms |
| State query | `get_ui_elements` and `run_script` | speed label read as text ("5,8 / 6,0 celdas/s"); a 6-line GDScript returned `{focused, mouse_mode, fps, win}` | 10 ms |
| Screenshots | three after keys | cockpit screens, HUD toasts and the key panel all visible; `likelyBlank:false` | about 1 s each |
| Log | game stdout | `MCP_ACTION_BOUNDARY n` markers per action; engine and game errors with C# backtraces (see below). In attach mode `get_debug_output` returns nothing, so **we capture stdout ourselves** | file |
| Stop | `stop_project` | bridge stopped, Godot left running (killed by my daemon); `project.godot` restored | fast |

Findings from the trial:
1. **Window focus gates flight.** `Play.Ui.cs` `UpdateCapture` sets the mouse look off when `DisplayServer.WindowIsFocused()` is false, and thrust only reads W/A/S/D while `_mouseLook` is on. A game window launched from a background process is unfocused, so `W` did nothing. A `run_script` with `DisplayServer.window_move_to_foreground()` plus a mouse click fixed it, but it steals the owner's keyboard focus for the session. Options: (a) run agent games on a machine or desktop the owner is not using; (b) add a game flag that skips the focus check (a one-line change, under `--agent`, listed below). Walking (`Play.Body.cs`) reads physical keys directly and was not tested.
2. **Injected keys set both `keycode` and `physical_keycode`**, so our `Input.IsPhysicalKeyPressed(Key.W)` polling works (confirmed by the speed change).
3. **Mouse positions are viewport pixels**, not window pixels; the bridge converts for the stretch mode. A click aimed at a cockpit screen in 3D is not a UI click: our game uses its own cursor in the screen-look view, so aiming needs `mouse_motion` and the game's own click handling. I did not get a clean cockpit-screen click in the time available; it is the main thing left to test (see Caveats).
4. **A real game warning surfaced in the log**: `ERROR: No vertices were added, surface can't be created` from `Ambience.DrawStreaks` (`Ambience.cs:455`, `ImmediateMesh.SurfaceEnd`), raised repeatedly while the ship was slow. Not caused by the bridge; worth a separate fix.
5. **Screen size**: the window was maximised (2560x1351) in spite of `--resolution` because `project.godot` sets `window/size/mode=2`. Use `--window=WxH` (our flag) or `--windowed` plus a mode override to make screenshots a fixed, small size.
6. **Not flaky** in this session: every call returned; no timeouts. One session is not a flakiness measurement.

### Not done
- Spawn mode (`run_project` without `attach`) was only read, not run: it launches Godot with `--path <project> [scene]` plus env vars and **cannot pass user args after `--`** (checked in `godot-runner.ts`: only `--path`, optional `--remote-debug` and the scene are added). So `-- --scenario=...`, `--content=...` and our `--*-shot` flags need attach mode, where we start Godot ourselves. The menu path needs no args (any flag skips the menu).
- Firing the weapon (hold left mouse), the cockpit-screen click, walking in the interior, and a long run were not tried.
- yanhuifair and ivanmurzak were read, not run (editor-bound, see table).

## Recommendation
**Adopt `godot-mcp-runtime` 4.0.1 pinned, attach mode, on a throwaway copy.** Reasons: only candidate that covers screenshots, keys, mouse, UI-by-name and live script in one stdio server; no addon or autoload committed (a transient one in a copy); MIT; no network beyond localhost; maintained; works with our Mono build as is (the server never builds C#, we run `dotnet build` first); deterministic building blocks (`wait frames`, named nodes).

Why not our own bridge now: the MCP already covers screenshots, input and a state query (`run_script` can read any node or autoload, including our C# nodes through the SceneTree). What it lacks is cheap, so do these instead:
1. A wrapper script `tools/agent-game.ps1` (not built) that copies `game/` and `src/` to a scratch folder, changes `custom_user_dir_name`, builds, starts Godot with logging to a file and prints the project path to register.
2. Name more UI nodes (like `StartButton`) so agents click handles that do not shift.
3. An `--agent` game flag (small) that skips the focus requirement and fixes the window size, so an unattended window can fly.

## Registering it for Claude Code (pinned)
Not done in this task (no Claude settings or MCP configuration were touched). Steps for the owner:

```powershell
# 1. Install the pinned package outside the repo (no scripts run, nothing global)
$d = "$HOME\.claude\mcp\godot-runtime"; New-Item -ItemType Directory -Force $d | Out-Null
npm install godot-mcp-runtime@4.0.1 --ignore-scripts --no-audit --no-fund --prefix $d
# expected integrity: sha512-8jzL6Vnp5nQ5MJMceUrWfPUd+nu1y60yEixCz3TlurBcsHAlCV2NHWahd2+RXK+ntMnG55/BPZXm0kOmXSGUBg==

# 2. Register it (user scope or local scope), running the file directly, no `npx -y` at start-up
claude mcp add godot-runtime -e GODOT_PATH="C:/Users/<you>/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe" -- node "$d\node_modules\godot-mcp-runtime\dist\index.js"
```
Set `GODOT_MCP_DISABLE_ELICITATION=true` only if the client cannot show the confirmation prompt. Upgrade by changing the version and re-checking the integrity; do not use `@latest`.

Agent recipe (per session):
1. Copy: `git archive HEAD | tar -x -C <scratch>/sf`, edit `game/project.godot` `custom_user_dir_name` to a unique value, `dotnet build game -c Debug` there.
2. `run_project {projectPath: <scratch>/sf/game, attach: true}` and, in parallel, start `Godot_..._console.exe --path <scratch>/sf/game --windowed --resolution 1280x720 [-- --content=... --ui-lang=en]` with stdout redirected to a log file.
3. Drive with `get_ui_elements`, `simulate_input`, `take_screenshot` (`responseMode: preview` to keep tokens low), `run_script` for state; read the log file for errors; `stop_project`, then kill the game and delete the scratch copy.

What it can touch: the scratch copy (any file inside the project path), the user folder named in the copy's `project.godot`, localhost TCP ports, and anything GDScript from `run_script` can reach as the owner. It must never be pointed at the main checkout: attach mode edits `project.godot` and `.gitignore` there while the owner may have Godot open.

## What a thin own bridge would add (not built)
Only worth building if the MCP proves too heavy or we need game-specific determinism. Design sketch:
- Flag `--agent-port=N` (user arg after `--`), handled in one `AgentBridge` autoload-free node created by `Main` and `MainMenu` when the flag is present (so no project.godot change at all). Listens on `127.0.0.1:N` only, with a random token printed to stdout or taken from `--agent-token=`; refuses non-loopback peers.
- Line-delimited JSON commands: `screenshot {path, size}`, `key {name, down|up|tap, hold_ms}`, `mouse {x, y, button, motion}`, `click {node}` (by `Name`), `wait {frames|ticks}`, `state {query}` (named queries backed by the sim: tick, position, speed, hull, hold, goals, rejects), `quit`.
- Uses `Input.ParseInputEvent` with both keycodes (as the MCP does), advances in fixed frames, and skips the focus gate under the flag.
- Advantages: state queries without GDScript, tick-exact waits tied to the simulation, no Node dependency. Costs: about 300 lines of C# plus tests, and a second tool to maintain; the MCP gives the same loop today.

## Caveats
- One short session on one machine (Windows, RTX 4080 Super): no flakiness data, no long-run or memory data. Latencies are wall-clock with the owner's game and other agents running.
- Cockpit-screen clicks, firing and interior walking are untested.
- The trial copy used `main` at `0f2af0b`; behaviour with `--content` or scenario flags is only inferred from source.
- Candidate facts for yanhuifair and ivanmurzak come from their READMEs and source structure, not from running them.
