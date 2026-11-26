// Playability verification driver: drives the REAL game client through godot-mcp-runtime (attach mode), one phase per run.
// Usage: node playthrough.mjs <mcpEntry.js> <godotConsoleExe> <playCopyGameDir> <outDir> <phase> [gameArgs...]
// Phases are listed at the bottom. Never point it at the real checkout (attach mode edits project.godot).
// Game args after the phase go after `--agent`, e.g. `--scenario=06_salvage_run` (skips the menu).
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { mkdirSync, openSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

const [entry, godot, project, outDir, phase, ...gameArgs] = process.argv.slice(2);
if (!entry || !godot || !project || !outDir || !phase) { console.error('usage: node playthrough.mjs <entry.js> <godot.exe> <game dir> <outDir> <phase> [gameArgs]'); process.exit(2); }
mkdirSync(outDir, { recursive: true });

const srv = spawn('node', [entry], { env: { ...process.env, GODOT_PATH: godot, GODOT_MCP_DISABLE_ELICITATION: 'true' }, stdio: ['pipe', 'pipe', 'inherit'] });
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

async function startGame(extra = []) {
  await rpc('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'sf-playthrough', version: '1' } });
  srv.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const att = tool('run_project', { projectPath: project, attach: true });
  await sleep(500);
  const logFd = openSync(join(outDir, 'game.log'), 'w');
  game = spawn(godot, ['--headless', '--path', project, '--', '--agent', '--ui-lang=en', ...extra, ...gameArgs], { stdio: ['ignore', logFd, logFd] });
  await att;
  await sleep(2500);
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
