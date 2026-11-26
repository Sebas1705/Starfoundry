// Playability verification driver: drives the REAL game client through godot-mcp-runtime (attach mode), one phase per run.
// Usage: node playthrough.mjs <mcpEntry.js> <godotConsoleExe> <playCopyGameDir> <outDir> <phase> [gameArgs...]
// Phases are listed at the bottom. Never point it at the real checkout (attach mode edits project.godot).
// Game args after the phase go after `--agent`, e.g. `--scenario=06_salvage_run` (skips the menu).
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { mkdirSync, openSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [entry, godot, project, outDir, phase, ...gameArgs] = process.argv.slice(2);
if (!entry || !godot || !project || !outDir || !phase) { console.error('usage: node playthrough.mjs <entry.js> <godot.exe> <game dir> <outDir> <phase> [gameArgs]'); process.exit(2); }
mkdirSync(outDir, { recursive: true });

const srv = spawn('node', [entry], { windowsHide: true, env: { ...process.env, GODOT_PATH: godot, GODOT_MCP_DISABLE_ELICITATION: 'true' }, stdio: ['pipe', 'pipe', 'inherit'] });
const waiting = new Map(); let nextId = 1;
createInterface({ input: srv.stdout }).on('line', l => { try { const m = JSON.parse(l); waiting.get(m.id)?.(m); } catch { /* not a response */ } });
const rpc = (method, params) => { const id = nextId++; const p = new Promise(r => waiting.set(id, r)); srv.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n'); return p; };
async function tool(name, args = {}) {
  const m = await rpc('tools/call', { name, arguments: args });
  if (m.error) throw new Error(`${name}: ${JSON.stringify(m.error)}`);
  const txt = (m.result.content || []).map(c => c.text ?? '').join('');
  let data; try { data = JSON.parse(txt); } catch { data = txt; }
  if (m.result.isError) throw new Error(`${name}: ${txt.slice(0, 300)}`);
  return data;
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
const input = (...actions) => tool('simulate_input', { actions });
const log = (...a) => console.log(...a);
const T0 = Date.now();
const secs = () => ((Date.now() - T0) / 1000).toFixed(1);

// ---- helpers ------------------------------------------------------------------------------------------------
const key = (k, hold_ms) => input(hold_ms ? { type: 'key', key: k, hold_ms } : { type: 'key', key: k });
const keyDown = k => input({ type: 'key', key: k, pressed: true });
const keyUp = k => input({ type: 'key', key: k, pressed: false });
const click = name => input({ type: 'click_element', element: name });
const ui = async () => (await tool('get_ui_elements', {})).elements ?? [];
const names = async re => (await ui()).map(e => e.name ?? '').filter(n => re.test(n));
const script = async body => {
  const r = await tool('run_script', { script: 'extends RefCounted\nfunc execute(scene_tree: SceneTree) -> Variant:\n' + body.split('\n').map(l => '\t' + l).join('\n') + '\n' });
  return r;
};
// Visible label / button texts (what a human can read), filtered by regex source.
const texts = async (re = '.') => {
  const r = await script(`var out := []\nvar rx := RegEx.create_from_string("${re.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}")\nfor n in scene_tree.root.find_children("*", "", true, false):\n\tif n is Control and n.is_visible_in_tree():\n\t\tvar t := ""\n\t\tif n is Label or n is Button or n is RichTextLabel: t = n.text\n\t\tif t != "" and rx.search(t): out.append(t.replace("\\n", " / "))\nreturn out`);
  return Array.isArray(r) ? r : (r?.result ?? r);
};
const shot = async n => { const r = await tool('take_screenshot', { responseMode: 'path_only' }); const p = r.path ?? r.screenshotPath ?? JSON.stringify(r).slice(0, 200); log(`shot ${n}: ${p}`); return p; };
const logTail = (re, n = 5) => readFileSync(join(outDir, 'game.log'), 'utf8').split(/\r?\n/).filter(l => re.test(l)).slice(-n);
const errors = () => logTail(/ERROR|SCRIPT ERROR|Unhandled exception/i, 20);
const check = (id, pass, evidence) => { log(`CHECK ${id}: ${pass ? 'PASS' : 'FAIL'} | ${evidence}`); return pass; };


// ---- in-engine input and state --------------------------------------------------------------------------------
const FIND = `var play = null\nfor n in scene_tree.root.find_children("*", "Node3D", true, false):\n\tif n.has_method("AgentState"):\n\t\tplay = n\n\t\tbreak\n`;
const unwrap = r => (r && typeof r === 'object' && 'result' in r) ? r.result : r;
const state = async () => { const r = unwrap(await script(FIND + 'return play.call("AgentState") if play else ""')); try { return JSON.parse(r); } catch { return { player: false, raw: String(r).slice(0, 200) }; } };
const mouseMove = (dx, dy) => script(`var e := InputEventMouseMotion.new()\ne.relative = Vector2(${dx}, ${dy})\ne.position = Vector2(640, 360)\nInput.parse_input_event(e)\nreturn 1`);
const mouseButton = (down, button = 1) => script(`var e := InputEventMouseButton.new()\ne.button_index = ${button}\ne.pressed = ${down}\ne.position = Vector2(640, 360)\nInput.parse_input_event(e)\nreturn 1`);
const holdFire = async ms => { await mouseButton(true); await sleep(ms); await mouseButton(false); };
// Closed-loop aim: turns the ship until the crosshair (camera forward) is within tolDeg of the deposit; the mouse is 0.0025 rad per pixel.
async function aimAt(depositId, tolDeg = 2) {
  let err = 999;
  for (let i = 0; i < 6 && err > tolDeg; i++) { const r = unwrap(await script(FIND + `return play.call("AgentFace", ${depositId})`)); err = Number(r); await sleep(150); }
  const s = await state(); const d = s.deposits.find(x => x.id === depositId);
  return { ok: err <= tolDeg || (d && Math.hypot(Math.atan2(d.cx, -d.cz), Math.atan2(d.cy, -d.cz)) < 0.04), errDeg: err, hover: s.hover };
}

// Mouse-aim runs set ship inertia off in the copy's own settings.cfg (the harness turns with a closed loop; the default light inertia
// makes that loop oscillate). The setting is a normal player option.
function presetSettings(inertia) {
  const m = readFileSync(join(project, 'project.godot'), 'utf8').match(/custom_user_dir_name="Godot\/app_userdata\/([^"]+)"/);
  if (!m) return;
  const dir = join(process.env.APPDATA, 'Godot', 'app_userdata', m[1]); mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, 'settings.cfg'), `[settings]
ship_inertia="${inertia}"
`);
}
async function startGame(extra = []) {
  if (process.env.SF_INERTIA) presetSettings(process.env.SF_INERTIA);
  await rpc('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'sf-playthrough', version: '1' } });
  srv.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const att = tool('run_project', { projectPath: project, attach: true });
  await sleep(500);
  const logFd = openSync(join(outDir, 'game.log'), 'w');
  game = spawn(godot, ['--headless', '--path', project, '--', '--agent', '--ui-lang=en', ...extra, ...gameArgs], { windowsHide: true, stdio: ['ignore', logFd, logFd] });
  await att;
  await sleep(2500);
  log('WINDOWS:', winCheck());
}
async function newCampaign(seed = '42') {
  const els = await ui();
  const ng = els.find(e => /^(new game|nueva partida)$/i.test(e.text ?? ''));
  if (!ng) throw new Error('New game button not found');
  await click(ng.name ?? ng.path); await sleep(500);
  await click('TabCampaign'); await input({ type: 'click_element', element: 'SeedField' }, { type: 'text', text: seed });
  await click('StartButton'); await sleep(6000);
}
let game;
// Every Godot / node process we own must have no window (MainWindowHandle = 0).
import { execFileSync } from 'node:child_process';
const winCheck = () => { try { return String(execFileSync('powershell', ['-NoProfile', '-Command', "Get-Process | Where-Object { $_.ProcessName -like 'Godot*' } | ForEach-Object { $_.ProcessName + ':MainWindowHandle=' + $_.MainWindowHandle }"], { windowsHide: true })).trim().split(String.fromCharCode(10)).join(' ; '); } catch (e) { return 'check failed ' + e.message; } };

// ---- phases -------------------------------------------------------------------------------------------------
const phases = {
  async menus() {
    await startGame();
    const byText = async re => (await ui()).find(e => re.test(e.text ?? ''));
    const open = async re => { const b = await byText(re); if (!b) throw new Error('no button ' + re); await click(b.name ?? b.path); await sleep(600); };
    await open(/^credits$/i); const cr = await texts('.');
    check('1.credits-open', cr.length > 4, `${cr.length} texts: ${cr.slice(0, 3).join(' | ').slice(0, 120)}`);
    await key('Escape'); await sleep(500);
    check('1.credits-close', !!(await byText(/^new game$/i)), 'main menu buttons visible again');
    await open(/^controls$/i); const kp = await texts('.');
    check('1.keys-open', kp.some(t => /rebind|reset/i.test(t)), kp.slice(0, 6).join(' | ').slice(0, 160));
    await key('Escape'); await sleep(500);
    check('1.keys-close', !!(await byText(/^new game$/i)), 'main menu again');
    await open(/^new game$/i);
    log('newgame texts:', JSON.stringify(await texts('.')).slice(0, 900));
    log('newgame handles:', (await ui()).map(e => e.name).filter(n => n && !n.startsWith('@')).join(', '));
    await click('TabStory'); await sleep(400);
    log('story texts:', JSON.stringify(await texts('.')).slice(0, 900));
    log('story handles:', (await ui()).map(e => e.name).join(', ').slice(0, 600));
    log('errors:', JSON.stringify(errors()));
  },

  async campaign() {
    await startGame();
    await newCampaign();
    let s = await state();
    check('2.starts-seated', s.player && s.seated === true, `seated=${s.seated} tick=${s.tick} scenario=${s.scenario} mouseLook=${s.mouseLook} hold=${JSON.stringify(s.hold)}`);
    // fly: W held, speed sampled while held (key down, wait, read, key up) so the ramp is visible
    const p0 = s.pos; const series = [];
    await keyDown('W'); for (let i = 0; i < 6; i++) { await sleep(800); series.push(+(await state()).speedCps.toFixed(2)); }
    await keyDown('Shift'); for (let i = 0; i < 3; i++) { await sleep(800); series.push(+(await state()).speedCps.toFixed(2)); }
    await keyUp('Shift'); await keyUp('W');
    const s2 = await state(); const moved = Math.hypot(...s2.pos.map((v, i) => v - p0[i]));
    const progressive = series[0] < series[5] && series[5] > 0;
    check('2.fly-W-Shift-progressive', moved > 0 && progressive, `cells/s every 0.8 s while W held (6 samples) then +Shift (3): ${series}; moved=${moved.toFixed(0)} raw units`);
    await key('Q', 800); await key('E', 800); await key('Space', 800); await key('C', 800);
    const s3 = await state(); check('2.roll-updown-no-crash', s3.player && errors().length === 0, `tick=${s3.tick} errors=${errors().length}`);
    await keyDown('S'); await sleep(2500); await keyUp('S');
    // scan
    const before = (await state()).deposits.length; await key('K'); await sleep(2500); s = await state();
    check('2.scan-K', s.deposits.length >= before, `revealed deposits before=${before} after=${s.deposits.length}: ${s.deposits.slice(0, 6).map(d => `${d.item}#${d.id}@${d.dist.toFixed(0)}`).join(' ')}`);
    // select and mine iron: fly to the nearest iron deposit, aim the crosshair at it, hold fire with the laser slot (key 1)
    const iron = s.deposits.filter(d => d.item === 'core:iron_ore').sort((x, y) => x.dist - y.dist)[0];
    await key('1'); await sleep(300); s = await state();
    log('slot after key 1:', s.arsenalSelected, 'target', JSON.stringify(iron));
    let inRange = false; const trace = [];
    for (let i = 0; i < 40 && !inRange; i++) {
      const ai = await aimAt(iron.id, 3); s = await state(); const d = s.deposits.find(x => x.id === iron.id);
      trace.push(`${d.dist.toFixed(0)}${d.inRange ? 'R' : ''}${ai.ok ? '' : '?'}`);
      if (d.inRange) { inRange = true; break; }
      await keyDown('W'); await sleep(d.dist > 40 ? 2500 : 900); await keyUp('W'); await sleep(300);
    }
    log('approach trace (dist, R = in range):', trace.join(' '));
    await keyDown('S'); await sleep(1500); await keyUp('S'); // brake
    for (let i = 0; i < 12; i++) { s = await state(); if (s.deposits.find(x => x.id === iron.id)?.inRange) break; await aimAt(iron.id, 1); await keyDown('W'); await sleep(350); await keyUp('W'); await sleep(500); }
    const aimed = await aimAt(iron.id, 1.5); s = await state();
    check('2.iron-selected', s.hover.kind === 'Deposit' && s.hover.id === iron.id, `aim=${JSON.stringify(aimed)} hover=${JSON.stringify(s.hover)} inRange=${s.deposits.find(x => x.id === iron.id)?.inRange}`);
    const hold0 = s.hold['core:iron_ore'] ?? 0;
    await mouseButton(true); await sleep(3000); const mid = await state(); await sleep(5000); const mid2 = await state(); await mouseButton(false); await sleep(500); s = await state();
    check('2.laser-mines', (s.hold['core:iron_ore'] ?? 0) > hold0, `iron ore hold ${hold0} -> ${mid.hold['core:iron_ore'] ?? 0} (3 s) -> ${mid2.hold['core:iron_ore'] ?? 0} (8 s) -> ${s.hold['core:iron_ore'] ?? 0} after release; laserFiring while held=${mid.laserFiring} miningDeposit=${mid.miningDeposit}`);
    globalThis.__s = s;
    log('errors:', JSON.stringify(errors()));
  },
  async calib() {
    await startGame(); await newCampaign();
    const dirOf = async () => { const d = (await state()).deposits.sort((a, b) => a.dist - b.dist)[0]; const v = [d.cx, d.cy, d.cz]; const n = Math.hypot(...v); return v.map(x => x / n); };
    const ang = (a, b) => Math.acos(Math.max(-1, Math.min(1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2]))) * 57.3;
    for (const [dx, dy] of [[100, 0], [0, 100], [-100, 0], [20, 0], [300, 0]]) {
      const a = await dirOf(); await mouseMove(dx, dy); await sleep(1500); const b = await dirOf(); await sleep(800); const c = await dirOf();
      log(`move(${dx},${dy}) turned ${ang(a, b).toFixed(1)} deg after 1.5 s, ${ang(a, c).toFixed(1)} after 2.3 s; expected ${(Math.hypot(dx, dy) * 0.0025 * 57.3).toFixed(1)}`);
    }
  },
  async aimtest() {
    await startGame(); await newCampaign();
    let s = await state(); const t = s.deposits.sort((a, b) => a.dist - b.dist)[0];
    for (let i = 0; i < 16; i++) {
      s = await state(); const d = s.deposits.find(x => x.id === t.id);
      const yaw = Math.atan2(d.cx, -d.cz) * 57.3, pitch = Math.atan2(d.cy, -d.cz) * 57.3;
      log(i, `yaw=${yaw.toFixed(1)} pitch=${pitch.toFixed(1)} cz=${d.cz.toFixed(1)} hover=${s.hover.kind}`);
      await mouseMove((yaw / 57.3 / 0.0025 * 0.2).toFixed(1), (-pitch / 57.3 / 0.0025 * 0.2).toFixed(1)); await sleep(500);
    }
  },
  async explore() {
    await startGame();
    log('menu:', (await ui()).filter(e => e.text).map(e => `${e.name}=${e.text}`).join(' | '));
    await newCampaign();
    log('props:', JSON.stringify(await script(`var n = scene_tree.current_scene
var o := []
for p in n.get_property_list():
	if p.name.begins_with("_") or p.name[0] == p.name[0].to_lower(): o.append(p.name)
return [n.name, o.size(), o.slice(0, 120)]`)).slice(0, 2500));
    log('errors:', JSON.stringify(errors()));
  },
};

try {
  if (!phases[phase]) throw new Error('unknown phase ' + phase + ' (' + Object.keys(phases).join(', ') + ')');
  await phases[phase]();
  log(`phase ${phase} done in ${secs()} s`);
} catch (e) { log('PHASE FAILED:', e.message); process.exitCode = 1; }
finally { try { await tool('stop_project', {}); } catch { /* ignore */ } try { game?.kill(); } catch { /* already gone */ } srv.kill(); }
