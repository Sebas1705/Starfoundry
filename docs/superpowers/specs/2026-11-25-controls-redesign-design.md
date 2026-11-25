# Controls redesign: few keys and one ship computer

Status: phases 1 to 3 implemented on `feature/controls-redesign` (2026-11-25): the `InputActions` registry, the new bindings and the ship computer; the rebinding screen (phase 4) is implemented on `feature/key-rebinding` (see [key rebinding](../../prototypes/key-rebinding.md)); the polish pass (phase 5) is not done. Owner feedback: "the keys and controls are too much of a mess." Decisions taken by the owner: **few keys plus one ship computer menu** (not radial wheels, not a mere reshuffle), and **Q/E roll, F interact**. Everything else is a *proposal* for the owner to react to in play.

## 1. The problem (measured)
The F1 help lists about 55 bindings: movement (W A S D, Space, C, Shift, Alt, Tab), roll on Z and X, sit on E, and a long tail of single letters (M V F K U R Y N I B T G J H P L O, brackets, plus and minus), ten function keys (F1, F3, Shift+F3, F4, F6, F7, F8, F9, F10), `1-9`, `0`, `Q`, `Ctrl+Delete` and `Backspace`. The same letter means different things by context (Z and X are roll or build layer; N is the module tool and its kind), several features were added with whatever key was free (inventory on F10, flight assist on F8), and the help is hand-written text in two languages that drifts from the code.

## 2. Principles
1. **A short fixed core** the player learns once (about 20 keys); everything else is reached by *context* (aim at it and press the interact key) or from **one menu**.
2. **One key, one meaning per context**, enforced by a test, not by care.
3. **One source of truth**: a central action registry feeds the key handler, the key panel, the F1 help and the rebinding screen, so none of them can disagree.
4. **No F-key sprawl**: function keys are reserved for system things (F1 help, F5 quick save, F9 quick load, F11 fullscreen).
5. **Rebindable**, with conflict detection, saved in settings.

## 3. The core keys (proposal)
| Key | Meaning |
| --- | --- |
| W A S D | move: fly along the ship's axes, or walk on foot |
| Space, C | up and down along the ship's axes (flying); jump is not used on foot |
| Shift | boost (flying), run (walking) |
| Q E | roll left and right (flying) |
| F | **interact**: sit and stand, climb a ladder, dock and undock, open the thing you aim at (machine, reactor, shipyard, console screen), pick up |
| Mouse move | turn the ship, or look |
| Left click | use the item in hand: fire the weapon group, place a build tool, pick |
| Right click | secondary: demolish with a build tool in hand, precision zoom otherwise (optional) |
| Mouse wheel and `1`-`9` | cycle and pick the hotbar: in flight the weapon groups (guns, laser) and tools; in build mode the build entries |
| V | next weapon group (the wheel does the same) |
| Z | flight assist on and off |
| K | scan |
| B | build menu (opens the ship computer on its Build tab) |
| I | inventory (opens the ship computer on its Inventory tab) |
| M | star map (opens the ship computer on its Map tab) |
| **Tab** | **the ship computer** (section 4) |
| H | cycle the HUD detail level (minimal, standard, detailed) |
| Esc | cancel, close, then the pause menu |
| F1 | the help (generated from the registry) |
| F5, F9 | quick save, quick load (as now F6 and F9: unify to the usual pair) |

Build-mode extras that only exist while a build tool is in hand (so they never collide with flight keys): `R` rotate belts, `Y` change recipe, `Q` and `E` change layer only for the module tool (and only where layers are allowed). The mouse is captured by default; the ship computer frees it.

## 4. The ship computer (Tab)
One full-height panel with tabs, opened with **Tab** (or with its direct keys `B`, `I`, `M`), switched with `Q` and `E` or by clicking, closed with `Tab` or `Esc`. While it is open the simulation keeps running (as the research panel does today) unless the pause menu is open.
- **Map**: the 3D star map (arrows or drag rotate, wheel zooms, click picks a system, a Jump button; today's `G` and `J`).
- **Research** (today's `T`).
- **Inventory** (today's F10 panel).
- **Build** (today's build menu).
- **Info** (today's `H` details, goals, steps, threat).
- **Designer** (today's shipyard designer, only when docked at a shipyard; today's F2).
- **Settings**: language, volumes, HUD mode and detail, ship inertia, flight assist default, key bindings, quality.
- **Systems** (new, small): drones assignment (today's F7 and the bracket keys), cockpit display pages, speed controls (today's plus and minus) and pause.
The tabs reuse the existing panel code; the work is the frame, the tab switching, the direct keys, and removing the old keys.

## 5. Where each old key goes
| Old | New |
| --- | --- |
| Z X (roll) | Q E |
| E (sit) | F |
| U (dock) | F (at a port) |
| F10 (inventory), F2 (designer), T (research), G and J (map and jump), H (details) | the ship computer tabs; direct keys I, M, B; H becomes the HUD detail cycle |
| F3 and Shift+F3 (HUD mode and detail) | Settings tab, H for the detail |
| F4 (focus a cockpit screen) | F at the console, or look at it (auto-frame) |
| F7 (assign drones) and `[` `]` | Systems tab, and F on a deposit |
| F8 (flight assist) | Z |
| F6 (quick save) | F5 |
| P, plus and minus (pause, speed), L (language), O (steps) | Esc menu, Systems tab, Settings tab, Info tab |
| M (mine), F (workbench) | the weapon in hand and click for mining (the laser group); the workbench opens with F when you aim at it |
| N, I (module tool, found a station) | Build tab entries (still `N` while in build mode) |
| Ctrl+Delete (dump), Backspace (repair) | Inventory bin button, and a Repair button on a machine's panel |

## 6. The registry (the engineering part)
- A single `InputActions` table (engine-free, in `Starfoundry.Ui`): action id, default keys, contexts (flying, walking, build tool, ship computer, docked, menu), display name and hint (es and en), icon id. The key handler, `KeyHints`, the key panel and the F1 help are all generated from it; the hand-written help text disappears.
- Rebinding: a Settings tab lists the actions per context, shows conflicts, and stores overrides in `settings.cfg`. A test fails if two default bindings collide in the same context or an action has no hint text in both languages.
- Compatibility: old settings keep their volume and similar values; stored key overrides (there are none today) start empty. The saved `hud_mode`, `hud_detail`, `ship_inertia` settings stay.

## 7. Acceptance
- A test that no two actions share a key in the same context, and that every action has es and en text and an icon.
- `--keys-shot` shows the key panel for every context with at most 8 rows in flight and 10 in build mode (today it is longer).
- A scripted run drives the main flows with the new keys only: sit (F), fly, dock (F), open the computer (Tab) and each tab, inventory (I), build (B), map (M), scan (K), fire and cycle (V, wheel, 1-4).
- `--ui-audit` clean at 720p and 1080p for the ship computer in es and en.
- F1 help is generated and fits on one screen at 720p.
- No old key (F3, F4, F7, F8, F10, U, G, J, T, O, P, L) does anything except where this spec lists it.

## 8. Order of work
1. The registry and its tests, with the current bindings ported 1:1 (no behaviour change), generating the key panel and help.
2. The new bindings and removals (section 5), mapping each old key to its new place.
3. The ship computer frame and tabs reusing existing panels.
4. The rebinding screen.
5. Polish pass with the owner's hands: roll rate on Q and E, the hotbar wheel, interact conflicts.

## 9. Risks and open questions
- `Q` and `E` are also build-mode layer keys; the spec limits that to the module tool only. If it still collides in practice the layer keys move to `,` and `.`.
- The mouse wheel doing both zoom (map) and hotbar needs a clear rule: in the ship computer the wheel zooms, otherwise it cycles the hotbar.
- A radial wheel can be added later on top of the registry; it was not chosen now.
- Gamepad: the registry makes a gamepad profile a data file; not in scope.
