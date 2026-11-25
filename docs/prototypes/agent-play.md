# Agent play-testing and movie capture

Status: tooling, 2026-11-25. Lets an AI agent start Starfoundry, begin a campaign, fly, press keys, click UI by name, take screenshots and record movies, on a **throwaway copy** of the game, without touching the owner's checkout, saves or keyboard focus. Background and the candidate comparison: [godot-mcp-eval](godot-mcp-eval.md).

## Pieces
| Piece | What it does |
| --- | --- |
| `--agent` game flag | The window never needs the OS focus: flight and key reading work while another window has the keyboard (`Launch.Agent`, read by `UpdateCapture` in `game/Play.Ui.cs`). The main menu stays up (like `--ui-lang` and `--content`, the flag does not make a run scripted). Nothing else changes; without it the game behaves as before. |
| `tools/agent/new-play-copy.ps1 [-Path dir]` | Copies `game/`, `src/` and `content/` into a new directory (default `%TEMP%\sf-play\<id>`), writes the marker `.sf-play-copy`, sets `custom_user_dir_name` in the copy to `Starfoundry Agent Copy <id>` (the owner's saves and settings stay untouched), runs `dotnet build game -c Debug -m:1` and the first asset import, and prints `<copy>\game` as the last line. |
| `tools/agent/remove-play-copy.ps1 -Path dir` | Deletes a copy and its user-data folder. Refuses any path without the marker, or inside the repository. |
| `tools/agent/install-godot-mcp.ps1` | Installs `godot-mcp-runtime` **4.0.1** into `%LOCALAPPDATA%\sf-tools\godot-mcp-runtime` with `--ignore-scripts`, checks the integrity `sha512-8jzL6Vnp5nQ5MJMceUrWfPUd+nu1y60yEixCz3TlurBcsHAlCV2NHWahd2+RXK+ntMnG55/BPZXm0kOmXSGUBg==` against the lock file and prints the `.mcp.json` snippet. Registers nothing. |
| `tools/agent/smoke-client.mjs` | A dependency-free JSON-RPC client over stdio: attach, new campaign, fly, Tab, screenshots, state. A template for an agent loop. |
| `tools/agent/record-movie.ps1` | Records a scripted run with Godot Movie Maker and converts it to mp4. |
| `tools/agent/flicker-check.ps1` | Scores a clip for flicker. |

## Play with the MCP (attach mode)
1. `powershell -ExecutionPolicy Bypass -File tools/agent/install-godot-mcp.ps1` once; put its snippet in the `.mcp.json` (or `claude mcp add`) of the agent's project. It runs `node <installed dist/index.js>`, never `npx -y`. `GODOT_MCP_DISABLE_ELICITATION=true` is in it because `run_script` asks for a confirmation the client may not show; drop it if the client shows prompts.
2. `tools/agent/new-play-copy.ps1` prints the copy's `game` folder.
3. `run_project {projectPath: <copy>\game, attach: true}` and, while it waits, start the game yourself: `Godot_v4.7.2-stable_mono_win64_console.exe --path <copy>\game --windowed --resolution 1280x720 -- --agent --ui-lang=en`, stdout to a log file (attach mode captures none). Flags that skip the menu (`--scenario=`, `--seed=`, any `--*-shot`) go after `--agent`; `--window=WxH` is such a flag, so use `--resolution` when you want the menu.
4. Drive it: `get_ui_elements`, `simulate_input` (`click_element`, `key` with `hold_ms`, `text`, `wait`), `take_screenshot` (`responseMode: path_only` or `preview` to save tokens), `run_script` for state, then `stop_project` and `remove-play-copy.ps1`.

The whole flow as one command: `node tools/agent/smoke-client.mjs <entry.js> <godot console exe> <copy>\game <outDir>`.

A campaign starts seated in the pilot seat (`F` stands up, `F` sits down); `W` held flies; `Tab` opens the ship computer; `1` to `9` pick the arsenal slot; `I` opens the inventory; see the [controls](../playtest.md).

## Named handles
`click_element` takes a node name or path; auto-named nodes such as `@Button@51` shift between runs, so name what an agent must click. Stable today: `StartButton`, `SeedField`, `TabCampaign` (new game screen); `ShipComputerBar`, `Tab<Page>` (`TabMap`, `TabResearch`, `TabInventory`, `TabBuild`, `TabInfo`, `TabDesigner`, `TabSettings`, `TabSystems`) and `CloseComputer` (ship computer); `Slot_<ContainerKind>_<id>_<slot>` for every inventory slot; `ArsenalBar` (its slots are drawn, not nodes: pick one with the number keys). To add a handle set `Name = "..."` where the node is created (a short stable CamelCase noun, no per-run suffix) and list it here. Menu buttons with the default name can be found by their text in `get_ui_elements`, as the smoke client does for "New game".

## Movies and the flicker check
`powershell -ExecutionPolicy Bypass -File tools/agent/record-movie.ps1 -Args "--structure-demo --demo-orbit=30" -Seconds 8 -Out C:\clips\orbit.mp4` makes a fresh copy (`-Project <copy>\game` reuses one), runs Godot with `--write-movie out.avi --fixed-fps 30 --quit-after <frames>` (movie time: frames are exact whatever the machine does; about half of real time on the RTX 4080 Super), converts with the ffmpeg found under the WinGet packages and removes the copy. `--demo-orbit=<deg/s>` (with `--structure-demo`) turns the camera around the demo ship (`--demo-big` for the large one). The flag-driven shots (`--flight-shot`, `--streak-shot`, `--cockpit-shot`) also run but quit by themselves, so `-Seconds` must cover the whole run. Keep clips outside the repository.

`flicker-check.ps1 -Video clip.mp4` prints `frames=.. meanDiff=.. spike=.. jitter=..` from the luma difference of every consecutive frame pair: `meanDiff` is how much changes per frame, `spike` the 99th percentile over the median of that series (1.0 is perfectly steady) and `jitter` the frame-to-frame wobble of it. Compare the same scene before and after a change; shimmer, z-fighting and popping raise `spike` and `jitter`.

Baseline (2026-11-25, `main` at `818ddff`, 1280x720, 30 fps, 8 s, `--structure-demo --demo-orbit=30`, the starter ship in the fixed overview): `frames=238 meanDiff=6.941 spike=1.46 jitter=0.019`. Two checks of one clip give identical numbers. Treat a rise of `spike` above 1.6 or of `jitter` above 0.03 on the same scene as worth a look (a heuristic from one clip, not a measured threshold).

## Security notes
- The server does not sandbox anything: `run_script` runs GDScript as the owner, the editing tools write any file inside the project path, headless tools run every autoload of the project. **Never point it at the real checkout**: attach mode edits `project.godot` and creates `.mcp/` and `.gitignore` in the project folder while the owner may have Godot open.
- It talks only to localhost (a TCP bridge in the game with a per-session token) and spawns only the Godot binary; the package has two runtime dependencies. The install is pinned (4.0.1), runs no install scripts and is checked against the recorded integrity; upgrade by changing the version and the hash together after reading the diff.
- The copy has its own user-data folder, so saves, settings and play logs of the owner are never touched. The scripts delete only directories that carry their marker.
