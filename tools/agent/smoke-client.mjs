// Smoke test for an agent-driven game: raw JSON-RPC over stdio to godot-mcp-runtime (attach mode), no dependencies.
// Usage: node smoke-client.mjs <mcpEntry.js> <godotConsoleExe> <playCopyGameDir> [outDir]
// Never point it at the real checkout: attach mode edits project.godot and adds .mcp/ in the project folder.
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { mkdirSync, openSync } from 'node:fs';
import { join } from 'node:path';

const [entry, godot, project, outDir = '.'] = process.argv.slice(2);
if (!entry || !godot || !project) { console.error('usage: node smoke-client.mjs <entry.js> <godot.exe> <game dir> [outDir]'); process.exit(2); }
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
let game;
try {
  await rpc('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'sf-smoke', version: '1' } });
  srv.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const att = tool('run_project', { projectPath: project, attach: true }); // waits for the game, which starts next
  await sleep(500);
  const logFd = openSync(join(outDir, 'game.log'), 'w');
  game = spawn(godot, ['--path', project, '--windowed', '--resolution', '1280x720', '--', '--agent', '--ui-lang=en'], { stdio: ['ignore', logFd, logFd] });
  log('attached:', JSON.stringify(await att).slice(0, 160));

  const ui = async () => (await tool('get_ui_elements', {})).elements ?? [];
  let els = await ui();
  log('menu controls:', els.length, els.filter(e => e.text).slice(0, 8).map(e => e.text).join(' | '));
  const newGame = els.find(e => /^(new game|nueva partida)$/i.test(e.text ?? ''));
  if (!newGame) throw new Error('New game button not found');
  await input({ type: 'click_element', element: newGame.name ?? newGame.path });
  await sleep(500);
  await input({ type: 'click_element', element: 'TabCampaign' });
  await input({ type: 'click_element', element: 'SeedField' }, { type: 'text', text: '42' });
  await input({ type: 'click_element', element: 'StartButton' });
  await sleep(6000);
  log('campaign started; game stdout in', join(outDir, 'game.log'));

  await sleep(1500); // a campaign starts seated in the pilot seat
  const shot = async n => { const r = await tool('take_screenshot', { responseMode: 'path_only' }); log(`screenshot ${n}:`, r.path ?? r.screenshotPath ?? JSON.stringify(r).slice(0, 200)); };
  await shot('seated');
  await input({ type: 'key', key: 'W', hold_ms: 2000 });
  const speed = await tool('run_script', { script: 'extends RefCounted\nfunc execute(scene_tree: SceneTree) -> Variant:\n\tvar out := []\n\tfor n in scene_tree.root.find_children("*", "Label", true, false):\n\t\tif n.visible and ("cells/s" in n.text or "celdas/s" in n.text): out.append(n.text)\n\treturn {"speed": out, "fps": Engine.get_frames_per_second()}\n' }).catch(e => String(e));
  log('state after 2 s of W:', JSON.stringify(speed).slice(0, 300));
  await input({ type: 'key', key: 'F' }); await sleep(500); await input({ type: 'key', key: 'F' }); await sleep(500); // stand up, sit down again
  await input({ type: 'key', key: 'Tab' });
  await sleep(800);
  await shot('computer');
  els = await ui();
  log('computer handles:', els.map(e => e.name).filter(n => /^Tab|Close|Slot_/.test(n ?? '')).slice(0, 12).join(', '));
  await input({ type: 'key', key: 'Tab' });
  await tool('stop_project', {});
  log('SMOKE OK');
} catch (e) { log('SMOKE FAILED:', e.message); process.exitCode = 1; }
finally { try { game?.kill(); } catch { /* already gone */ } srv.kill(); }
