using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The player interface of <see cref="Play"/>: a build bar, action buttons, hover tooltips, an inspector for
/// machines, a guided-steps panel, mouse camera and click-to-fly, all in Spanish and English.
/// </summary>
public partial class Play
{
    private enum ToolMode { Pointer, Build }

    private enum HoverKind { None, Ground, Machine, Segment, Deposit, Enemy }

    private readonly record struct Hover(HoverKind Kind, int Id, string Text);

    // ---- state -------------------------------------------------------------------------------------

    private ToolMode _tool = ToolMode.Pointer;
    private Hover _hover = new(HoverKind.None, -1, "");
    private int _selMachine = -1;
    private Vector3? _waypoint;
    private bool _pendingMine;
    private float _pitch = 0.95f;
    private bool _helpOpen;
    private bool _objectivesOpen = true;
    private bool _holdWasFull;
    private bool _rmbDown, _rmbDragged, _mmbDown;
    private Vector2 _rmbStart;
    private ObjectiveTracker _objectives = null!;

    // ---- nodes -------------------------------------------------------------------------------------

    private CanvasLayer _layer = null!;
    private Control _uiRoot = null!;
    private PanelContainer _statusPanel = null!, _toolbar = null!, _actions = null!, _objectivesPanel = null!, _inspector = null!, _helpPanel = null!;
    private Label _statusLabel = null!, _holdItems = null!, _modeHint = null!, _toastText = null!, _objectivesText = null!, _tooltipLabel = null!;
    private Label _inspectorTitle = null!, _inspectorBody = null!, _helpText = null!;
    private ProgressBar _holdBar = null!;
    private readonly List<Button> _buildButtons = new();
    private Button _btnMine = null!, _btnRefine = null!, _btnScan = null!, _btnMap = null!, _btnWeapons = null!, _btnResearch = null!;
    private Button _btnRepair = null!;
    private Button _btnJump = null!, _btnLang = null!, _btnHelp = null!, _btnObjectives = null!;
    private Button _inspPrev = null!, _inspNext = null!, _inspRemove = null!, _inspRepair = null!;

    private static string L(string key, params object[] args) => Loc.T(key, args);

    private string ItemName(int item) => Loc.Name("item", _content.Items[item].Id);

    private string MachineName(int type) => Loc.Name("machine", _content.Machines[type].Id);

    private string Stacks(ItemStack[] stacks) =>
        stacks.Length == 0 ? L("ui.free") : string.Join(", ", stacks.Select(s => $"{s.Count} {ItemName(s.Item)}"));

    // ---- building the interface ---------------------------------------------------------------------

    private static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, float margin = 8f)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(margin);
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(1); }
        return s;
    }

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.86f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.7f);
    private static readonly Color Accent = new(0.35f, 0.65f, 1f);

    private PanelContainer MakePanel(Control content)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Box(PanelBg, PanelEdge));
        p.AddChild(content);
        p.MouseFilter = Control.MouseFilterEnum.Stop;
        _uiRoot.AddChild(p);
        return p;
    }

    private Label MakeText(int size = 14, Color? color = null)
    {
        var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeColorOverride("font_color", color ?? Colors.White);
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    private Button MakeButton(string text, Action onPressed, bool toggle = false, int minWidth = 0)
    {
        var b = new Button { Text = text, ToggleMode = toggle, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(minWidth, 0) };
        b.AddThemeFontSizeOverride("font_size", 13);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.1f, 0.13f, 0.2f, 0.95f), PanelEdge, 5, 6f));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.16f, 0.22f, 0.34f, 0.97f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.15f, 0.34f, 0.58f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(0.18f, 0.4f, 0.66f, 0.98f), Accent, 5, 6f));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.06f, 0.07f, 0.1f, 0.7f), new Color(0.2f, 0.22f, 0.28f), 5, 6f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.52f, 0.58f));
        b.Pressed += onPressed;
        return b;
    }

    private void BuildUi()
    {
        _layer = new CanvasLayer();
        AddChild(_layer);
        _uiRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_uiRoot);

        _objectives = new ObjectiveTracker(_content);
        Loc.Lang = Loc.FromLocale(OS.GetLocale());

        // Status: hold bar and the lines that matter right now.
        var status = new VBoxContainer();
        var holdRow = new HBoxContainer();
        _holdBar = new ProgressBar { MinValue = 0, MaxValue = PlayerRules.HoldCapacity, ShowPercentage = false, CustomMinimumSize = new Vector2(170, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        _holdItems = MakeText(14);
        holdRow.AddChild(_holdBar);
        holdRow.AddChild(_holdItems);
        status.AddChild(holdRow);
        _statusLabel = MakeText(14);
        status.AddChild(_statusLabel);
        _statusPanel = MakePanel(status);
        _statusPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

        // Action buttons.
        var actions = new VBoxContainer();
        _btnMine = MakeButton("", ToggleMining, toggle: true, minWidth: 170);
        _btnRefine = MakeButton("", ToggleRefinery, toggle: true, minWidth: 170);
        _btnScan = MakeButton("", () => Send(new Scan()), minWidth: 170);
        _btnMap = MakeButton("", () => _mapOpen = !_mapOpen, toggle: true, minWidth: 170);
        _btnWeapons = MakeButton("", ToggleWeapons, toggle: true, minWidth: 170);
        _btnResearch = MakeButton("", CycleResearch, minWidth: 170);
        _btnRepair = MakeButton("", RepairAll, minWidth: 170);
        foreach (var b in new[] { _btnMine, _btnRefine, _btnScan, _btnMap, _btnWeapons, _btnRepair, _btnResearch }) actions.AddChild(b);
        _actions = MakePanel(actions);

        // The build bar.
        var bar = new HBoxContainer();
        for (int i = 0; i < _palette.Count && i < 12; i++)
        {
            int index = i;
            var b = MakeButton("", () => SelectBuild(index), toggle: true, minWidth: 104);
            b.CustomMinimumSize = new Vector2(104, 62);
            _buildButtons.Add(b);
            bar.AddChild(b);
        }
        _toolbar = MakePanel(bar);

        _modeHint = MakeText(14, new Color(0.85f, 0.92f, 1f));
        _modeHint.AddThemeColorOverride("font_outline_color", Colors.Black);
        _modeHint.AddThemeConstantOverride("outline_size", 5);
        _uiRoot.AddChild(_modeHint);

        _toastText = MakeText(15);
        _toastText.AddThemeColorOverride("font_outline_color", Colors.Black);
        _toastText.AddThemeConstantOverride("outline_size", 5);
        _uiRoot.AddChild(_toastText);

        // Guided steps.
        _objectivesText = MakeText(14);
        var obj = new VBoxContainer();
        var objButtons = new HBoxContainer();
        _btnObjectives = MakeButton("", () => _objectivesOpen = !_objectivesOpen);
        _btnHelp = MakeButton("", () => _helpOpen = !_helpOpen);
        _btnLang = MakeButton("", ToggleLanguage);
        objButtons.AddChild(_btnObjectives);
        objButtons.AddChild(_btnHelp);
        objButtons.AddChild(_btnLang);
        obj.AddChild(objButtons);
        obj.AddChild(_objectivesText);
        _objectivesPanel = MakePanel(obj);

        // The star map and its list are the existing nodes; the jump button sits under them.
        _map = new MapPanel { Visible = false };
        _uiRoot.AddChild(_map);
        _mapList = MakeText(13);
        _mapList.AddThemeColorOverride("font_outline_color", Colors.Black);
        _mapList.AddThemeConstantOverride("outline_size", 4);
        _mapList.Visible = false;
        _uiRoot.AddChild(_mapList);
        _btnJump = MakeButton("", SendJump, minWidth: 150);
        _btnJump.Visible = false;
        _uiRoot.AddChild(_btnJump);

        // Inspector for a selected machine.
        var insp = new VBoxContainer();
        _inspectorTitle = MakeText(16, new Color(1f, 0.9f, 0.5f));
        _inspectorBody = MakeText(13);
        var inspButtons = new HBoxContainer();
        _inspPrev = MakeButton("<", () => ChangeSelectedRecipe(-1), minWidth: 34);
        _inspNext = MakeButton(">", () => ChangeSelectedRecipe(1), minWidth: 34);
        _inspRemove = MakeButton("", () => { if (_selMachine >= 0) Send(new RemoveMachine(_selMachine)); _selMachine = -1; }, minWidth: 100);
        inspButtons.AddChild(_inspPrev);
        inspButtons.AddChild(_inspNext);
        _inspRepair = MakeButton("", () => { if (_selMachine >= 0) Send(new Repair(_selMachine)); }, minWidth: 100);
        inspButtons.AddChild(_inspRepair);
        inspButtons.AddChild(_inspRemove);
        insp.AddChild(_inspectorTitle);
        insp.AddChild(_inspectorBody);
        insp.AddChild(inspButtons);
        _inspector = MakePanel(insp);
        _inspector.Visible = false;

        // Help overlay.
        _helpText = MakeText(15);
        _helpPanel = MakePanel(_helpText);
        _helpPanel.Visible = false;

        // World tooltip that follows the mouse.
        _tooltipLabel = MakeText(13);
        _tooltipLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _tooltipLabel.AddThemeConstantOverride("outline_size", 5);
        _uiRoot.AddChild(_tooltipLabel);
    }

    // ---- per-frame refresh --------------------------------------------------------------------------

    private void UpdateUi(float dt)
    {
        _objectives.Update(Net, Player);

        var size = GetViewport().GetVisibleRect().Size;
        UpdateHold();
        UpdateStatus();
        UpdateButtons();
        UpdateObjectivesPanel();
        UpdateInspector();
        UpdateHelp();
        UpdateToasts();
        UpdateModeHint();
        LayoutUi(size);
    }

    private void UpdateHold()
    {
        int total = Net.HoldTotal(Player), cap = PlayerRules.HoldCapacity;
        _holdBar.Value = total;
        bool full = total >= cap;
        _holdBar.AddThemeStyleboxOverride("fill", Box(full ? new Color(0.9f, 0.25f, 0.2f) : new Color(0.3f, 0.7f, 0.4f), null, 3, 0f));
        _holdBar.AddThemeStyleboxOverride("background", Box(new Color(0.12f, 0.14f, 0.2f), null, 3, 0f));

        var items = new StringBuilder();
        items.Append(L("hud.hold", total, cap));
        foreach (var item in _content.Items)
        {
            int n = Net.HoldCount(Player, item.Index);
            if (n > 0) items.Append($"   {ItemName(item.Index)} {n}");
        }
        _holdItems.Text = items.ToString();
        _holdItems.AddThemeColorOverride("font_color", full ? new Color(1f, 0.55f, 0.45f) : Colors.White);

        if (full && !_holdWasFull) Toast(L("toast.holdFull"));
        _holdWasFull = full;
    }

    private void UpdateStatus()
    {
        var sb = new StringBuilder();
        var p = Net.GetPlayerInfo(Player);

        int dep = _selectedDeposit;
        string depLine;
        if (Net.MiningDeposit(Player) >= 0)
        {
            int d = Net.MiningDeposit(Player);
            bool inRange = Net.InMineRange(Player, d);
            depLine = inRange ? L("hud.mining", ItemName(Net.DepositItem(d)), Net.DepositRemaining(d))
                              : L("hud.miningOut", ItemName(Net.DepositItem(d)));
            if (inRange && p.Hull >= 0 && Net.HoldTotal(Player) >= PlayerRules.HoldCapacity) depLine = L("hud.miningFull");
        }
        else if (Net.IsDeposit(dep) && Net.IsDepositRevealed(dep))
        {
            var pos = Net.DepositPosition(dep);
            float dist = (new Vector3(pos.X, pos.Y, pos.Z) - ShipPosition(0f)).Length();
            depLine = L("hud.depositSelected", ItemName(Net.DepositItem(dep)), Net.DepositRemaining(dep), dist.ToString("F0", Loc.Culture));
        }
        else depLine = L("hud.noDeposit");
        sb.Append(depLine).Append('\n');

        if (Net.Galaxy is { } galaxy)
        {
            int cur = Net.CurrentSystem(Player);
            string where = cur >= 0 ? $"{galaxy.Systems[cur].Name} ({Loc.Name("arch", _content.Archetypes[galaxy.Systems[cur].Archetype].Id)})" : L("hud.deepSpace");
            string jump = Net.JumpRemaining(Player) > 0
                ? L("hud.jumping", galaxy.Systems[Net.JumpTarget(Player)].Name, (Net.JumpRemaining(Player) / (float)Units.TicksPerSecond).ToString("F1", Loc.Culture))
                : Net.HasAbility(Ability.Jump) ? L("hud.jumpReady") : L("hud.jumpLocked");
            sb.Append(L("hud.system", where, Enumerable.Range(0, galaxy.Systems.Count).Count(Net.IsSystemVisited), galaxy.Systems.Count, jump)).Append('\n');
            ThreatHud(sb, cur);
            OreWarning(sb);
        }

        if (Net.CurrentResearch >= 0)
        {
            var node = _content.Research[Net.CurrentResearch];
            sb.Append(L("hud.research", ResearchName(node, next: true),
                string.Join(", ", Net.ResearchCost(node.Index).Select(c => $"{Net.ResearchProgress(node.Index, c.Item)}/{c.Count} {ItemName(c.Item)}")))).Append('\n');
        }
        else sb.Append(Net.ResearchedCount == 0 ? L("hud.researchNone") : L("hud.researchDone",
            string.Join(", ", _content.Research.Where(r => Net.IsResearched(r.Index)).Select(r => ResearchName(r, next: false))))).Append('\n');

        int bDmg = Net.BonusPercent(BonusKind.TurretDamage), bRate = Net.BonusPercent(BonusKind.TurretRate),
            bHp = Net.BonusPercent(BonusKind.MachineHp), bHeal = Net.BonusPercent(BonusKind.RepairHeal),
            bHull = Net.BonusPercent(BonusKind.ShipHull), bGun = Net.BonusPercent(BonusKind.ShipDamage);
        if (bDmg + bRate + bHp + bHeal + bHull + bGun > 0) sb.Append(L("hud.defense", bDmg, bRate, bHp, bHeal, bHull, bGun)).Append('\n');

        if (_demo) sb.Append(L("hud.speed", _demoSpeed));
        else if (_speed != 1 || _paused) sb.Append(_paused ? L("hud.paused") : L("hud.speed", _speed));
        _statusLabel.Text = sb.ToString().TrimEnd('\n');
    }

    private void ThreatHud(StringBuilder sb, int currentSystem)
    {
        if (!Net.ThreatActive) return;
        var rules = _content.Threat!;
        int sys = currentSystem >= 0 ? currentSystem : 0;
        var th = Net.GetSystemThreat(sys);
        string state = th.WarningRemaining >= 0 ? L("hud.waveIncoming", th.Wave + 1, (th.WarningRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture))
            : th.Enemies > 0 ? L("hud.enemies", th.Enemies)
            : L("hud.alert", 100 * th.Alert / Math.Max(1, th.AlertMax));
        var p = Net.GetPlayerInfo(Player);
        string ship = p.RespawnRemaining > 0 ? L("hud.rebuilding", (p.RespawnRemaining / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture))
            : L("hud.hull", p.Hull, Net.ShipMaxHull);
        sb.Append(L("hud.threat", th.Signature, rules.SignatureThreshold, state, ship)).Append('\n');
    }

    private readonly HashSet<(int System, int Item)> _lowOreWarned = new();

    /// <summary>A status line, and a toast the first time, when an ore of the current system is running low.</summary>
    private void OreWarning(StringBuilder sb)
    {
        int system = Net.CurrentSystem(Player);
        foreach (var item in _content.Items)
        {
            int pct = OreRunway.PercentLeft(Net, Player, item.Index);
            if (!OreRunway.IsLow(pct)) continue;
            string name = Loc.Name("item", item.Id);
            sb.Append(L("hud.oreLow", name, pct)).Append('\n');
            if (_lowOreWarned.Add((system, item.Index))) Toast(L("toast.oreLow", name, pct));
        }
    }

    /// <summary>A research name, with the level for repeatable nodes (the level being researched when next is true, the level reached otherwise).</summary>
    private string ResearchName(ResearchDef r, bool next)
    {
        string name = Loc.Name("research", r.Id);
        if (r.MaxLevels <= 1) return name;
        return L("hud.level", name, next ? Net.ResearchLevel(r.Index) + 1 : Net.ResearchLevel(r.Index), r.MaxLevels);
    }

    /// <summary>Damaged machines the ship can reach, worst first.</summary>
    private List<int> DamagedInRange()
    {
        var list = new List<int>();
        for (int id = 0; id < Net.MachineIdLimit; id++)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            if (m.Hp >= Net.MaxHp(m.Type)) continue;
            if (!_world.InBuildRange(Player, m.X, m.Y, m.Z)) continue;
            list.Add(id);
        }
        return list.OrderBy(id => Net.GetMachineInfo(id).Hp / (float)Net.MaxHp(Net.GetMachineInfo(id).Type)).ToList();
    }

    /// <summary>Repairs every damaged machine in reach, worst first; the hold pays and what it cannot pay for stays damaged.</summary>
    private void RepairAll()
    {
        var targets = DamagedInRange();
        if (targets.Count == 0) { Toast(L("toast.nothingToRepair")); return; }
        foreach (int id in targets) Send(new Repair(id));
    }

    /// <summary>The key that picks build item i: 1 to 9, then 0, then Q.</summary>
    private static string KeyLabel(int i) => i < 9 ? (i + 1).ToString() : i == 9 ? "0" : i == 10 ? "Q" : "B";

    private void UpdateButtons()
    {
        // Build bar.
        for (int i = 0; i < _buildButtons.Count; i++)
        {
            var item = _palette[i];
            bool locked = item.IsBelt ? Net.IsBeltLocked(item.Tier) : Net.IsMachineLocked(item.Machine);
            var cost = item.IsBelt ? _content.Belts[(int)item.Tier]?.CostPerCell ?? Array.Empty<ItemStack>() : _content.Machines[item.Machine].Cost;
            bool affordable = Net.CanPay(Player, cost);
            string name = item.IsBelt ? L("ui.belt", (int)item.Tier + 1) : MachineName(item.Machine);
            string costText = cost.Length == 0 ? L("ui.free") : string.Join(" ", cost.Select(s => $"{s.Count} {ItemName(s.Item)}"));
            var b = _buildButtons[i];
            b.Text = $"{KeyLabel(i)}  {name}\n{(locked ? L("ui.locked") : costText + (item.IsBelt ? L("ui.perCell") : ""))}";
            b.Disabled = locked;
            b.SetPressedNoSignal(_tool == ToolMode.Build && _selected == i);
            b.Modulate = !locked && !affordable ? new Color(1f, 0.75f, 0.7f) : Colors.White;
            b.TooltipText = BuildTooltip(item, cost, locked, affordable);
        }

        // Actions.
        var player = Net.GetPlayerInfo(Player);
        bool mining = Net.MiningDeposit(Player) >= 0 || _pendingMine;
        _btnMine.Text = mining ? L("btn.mineStop") : L("btn.mine");
        _btnMine.SetPressedNoSignal(mining);
        _btnMine.TooltipText = L("tip.mine");

        int shipRecipe = _content.RecipeIndex("core:ship_smelt");
        bool refining = player.ShipRecipe == shipRecipe;
        _btnRefine.Text = refining ? L("btn.refineStop") : L("btn.refine");
        _btnRefine.SetPressedNoSignal(refining);
        _btnRefine.TooltipText = L("tip.refine");

        int cooldown = Net.ScanCooldown(Player);
        _btnScan.Text = cooldown > 0 ? L("btn.scanWait", (cooldown / (float)Units.TicksPerSecond).ToString("F0", Loc.Culture)) : L("btn.scan");
        _btnScan.Disabled = cooldown > 0;
        _btnScan.TooltipText = L("tip.scan");

        _btnMap.Text = L("btn.map");
        _btnMap.SetPressedNoSignal(_mapOpen);
        _btnMap.Visible = Net.Galaxy is not null;
        _btnMap.TooltipText = L("tip.map");

        _btnWeapons.Visible = Net.ThreatActive;
        _btnWeapons.Text = player.Armed ? L("btn.weaponsOff") : L("btn.weapons");
        _btnWeapons.SetPressedNoSignal(player.Armed);
        _btnWeapons.TooltipText = L("tip.weapons");

        var damagedNow = DamagedInRange();
        _btnRepair.Visible = Net.ThreatActive;
        _btnRepair.Text = L("btn.repair", damagedNow.Count);
        _btnRepair.Disabled = damagedNow.Count == 0;
        _btnRepair.TooltipText = damagedNow.Count == 0 ? L("tip.repairNone") : L("tip.repair", Stacks(damagedNow.SelectMany(id => Net.RepairCost(id)).GroupBy(s => s.Item).Select(g => new ItemStack(g.Key, g.Sum(s => s.Count))).ToArray()));

        _btnResearch.Text = Net.CurrentResearch >= 0 ? L("btn.researchNext") : L("btn.research");
        _btnResearch.TooltipText = L("tip.research");

        _btnJump.Visible = _mapOpen && Net.Galaxy is not null;
        _btnJump.Text = L("btn.jump");
        _btnJump.Disabled = _selectedSystem < 0 || Net.JumpRemaining(Player) > 0;
        _btnJump.TooltipText = L("tip.jump");

        _btnObjectives.Text = _objectivesOpen ? L("btn.hideSteps") : L("btn.showSteps");
        _btnHelp.Text = L("btn.help");
        _btnLang.Text = Loc.Lang == Language.Es ? "ES | en" : "es | EN";
    }

    private string BuildTooltip(BuildItem item, ItemStack[] cost, bool locked, bool affordable)
    {
        var sb = new StringBuilder();
        sb.Append(item.IsBelt ? L("ui.belt", (int)item.Tier + 1) : MachineName(item.Machine)).Append('\n');
        sb.Append(L("tip.cost", cost.Length == 0 ? L("ui.free") : Stacks(cost) + (item.IsBelt ? L("ui.perCell") : ""))).Append('\n');
        if (!item.IsBelt)
        {
            var def = _content.Machines[item.Machine];
            sb.Append(L("tip.role." + def.Role.ToString().ToLowerInvariant())).Append('\n');
            if (def.Signature > 0) sb.Append(L("tip.signature", def.Signature)).Append('\n');
        }
        else sb.Append(L("tip.beltSpeed", BeltTiers.ItemsPerSecond(item.Tier).ToString("F0", Loc.Culture))).Append('\n');
        if (locked) sb.Append(L("tip.locked"));
        else if (!affordable) sb.Append(L("tip.needItems"));
        return sb.ToString().TrimEnd('\n');
    }

    private void UpdateModeHint()
    {
        if (_demo) { _modeHint.Text = ""; return; }
        if (_tool == ToolMode.Build)
        {
            var item = _palette[_selected];
            string name = item.IsBelt ? L("ui.belt", (int)item.Tier + 1) : MachineName(item.Machine);
            _modeHint.Text = L(item.IsBelt ? "hint.buildBelt" : "hint.buildMachine", name);
        }
        else _modeHint.Text = L("hint.pointer");
    }

    private void UpdateObjectivesPanel()
    {
        _objectivesPanel.Visible = true;
        var states = _objectives.States;
        var current = _objectives.Current;
        var sb = new StringBuilder();
        sb.Append(L("obj.title", _objectives.DoneCount, states.Count)).Append('\n');
        if (_objectivesOpen)
        {
            if (current is null) sb.Append(L("obj.allDone"));
            else
            {
                // The step in progress with its hint, then a short look at what comes next.
                var cur = states.First(s => s.Objective == current);
                sb.Append("> ").Append(L(cur.TitleKey)).Append('\n').Append("   ").Append(L(cur.HintKey)).Append('\n');
                foreach (var s in states.Where(s => !s.Done && s.Objective != current).Take(3))
                    sb.Append("  ○ ").Append(L(s.TitleKey)).Append('\n');
            }
            _objectivesText.Text = sb.ToString().TrimEnd('\n');
            _objectivesText.Visible = true;
        }
        else _objectivesText.Visible = false;
    }

    private void UpdateHelp()
    {
        _helpPanel.Visible = _helpOpen;
        if (_helpOpen) _helpText.Text = L("help.text");
    }

    private void UpdateToasts()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        _toasts.RemoveAll(t => t.Until < now);
        _toastText.Text = string.Join("\n", _toasts.TakeLast(5).Select(t => t.Text));
    }

    private void LayoutUi(Vector2 size)
    {
        // The status text wraps so it never runs under the steps panel at the top right.
        _objectivesText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _objectivesText.CustomMinimumSize = new Vector2(Math.Clamp(size.X * 0.3f, 240, 420), 0);
        _objectivesPanel.ResetSize();
        var objMin = _objectivesPanel.GetCombinedMinimumSize();
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _statusLabel.CustomMinimumSize = new Vector2(Math.Clamp(size.X - objMin.X - 60, 260, 760), 0);
        _statusPanel.ResetSize();
        _statusPanel.Position = new Vector2(12, 10);

        _actions.Position = new Vector2(12, _statusPanel.Position.Y + _statusPanel.Size.Y + 10);

        // The build bar shrinks to fit narrow windows instead of running off the screen.
        var barSize = _toolbar.GetCombinedMinimumSize();
        float barScale = Math.Min(1f, (size.X - 24f) / Math.Max(1f, barSize.X));
        _toolbar.Scale = new Vector2(barScale, barScale);
        _toolbar.Position = new Vector2((size.X - barSize.X * barScale) / 2f, size.Y - barSize.Y * barScale - 10);
        barSize = new Vector2(barSize.X * barScale, barSize.Y * barScale);

        _modeHint.Position = new Vector2(Math.Max(12, (size.X - _modeHint.Size.X) / 2f), _toolbar.Position.Y - 26);
        _toastText.Position = new Vector2(12, _toolbar.Position.Y - 12 - _toastText.Size.Y - 28);

        var objSize = _objectivesPanel.GetCombinedMinimumSize();
        _objectivesPanel.Position = new Vector2(size.X - objSize.X - 12, 10);
        float y = _objectivesPanel.Position.Y + objSize.Y + 10;
        if (_mapOpen && Net.Galaxy is not null)
        {
            _map.Position = new Vector2(size.X - _map.Size.X - 12, y);
            _mapList.Position = new Vector2(size.X - 380, _map.Position.Y + _map.Size.Y + 6);
            _btnJump.Position = new Vector2(size.X - 162, _mapList.Position.Y + _mapList.Size.Y + 6);
        }
        _map.Visible = _mapOpen && Net.Galaxy is not null;
        _mapList.Visible = _map.Visible;

        var inspSize = _inspector.GetCombinedMinimumSize();
        _inspector.Position = new Vector2(size.X - inspSize.X - 12, size.Y - inspSize.Y - barSize.Y - 24);

        var helpSize = _helpPanel.GetCombinedMinimumSize();
        _helpPanel.Position = new Vector2((size.X - helpSize.X) / 2f, (size.Y - helpSize.Y) / 2f - 40);

        // The tooltip follows the mouse and stays on screen.
        var mouse = GetViewport().GetMousePosition();
        _tooltipLabel.Visible = _tooltipLabel.Text.Length > 0 && !_demo;
        _tooltipLabel.Position = new Vector2(Math.Min(mouse.X + 16, size.X - _tooltipLabel.Size.X - 8), Math.Min(mouse.Y + 18, size.Y - _tooltipLabel.Size.Y - 8));
    }

    // ---- selecting tools ------------------------------------------------------------------------------

    private void SelectBuild(int index)
    {
        if (index < 0 || index >= _palette.Count) return;
        if (_tool == ToolMode.Build && _selected == index) { _tool = ToolMode.Pointer; return; } // pressing it again puts it away
        _selected = index;
        _tool = ToolMode.Build;
        _selMachine = -1;
        _waypoint = null;
    }

    private void ToggleLanguage()
    {
        Loc.Lang = Loc.Lang == Language.Es ? Language.En : Language.Es;
        Toast(L("toast.language"));
    }

    private void RecenterCamera()
    {
        _yaw = 0.2f;
        _pitch = 0.95f;
        _distance = 15f;
        _target = ShipPosition(0f);
    }

    // ---- mouse and keyboard ------------------------------------------------------------------------------

    private bool OverUi() => GetViewport().GuiGetHoveredControl() is { } c && c != _uiRoot;

    public override void _UnhandledInput(InputEvent e)
    {
        if (_demo) return;

        switch (e)
        {
            case InputEventMouseButton mb:
                OnMouseButton(mb);
                break;
            case InputEventMouseMotion mm:
                OnMouseMotion(mm);
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                OnKey(k);
                break;
        }
    }

    private void OnMouseButton(InputEventMouseButton mb)
    {
        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp when mb.Pressed: _distance = Mathf.Max(5f, _distance * 0.9f); break;
            case MouseButton.WheelDown when mb.Pressed: _distance = Mathf.Min(90f, _distance * 1.1f); break;
            case MouseButton.Middle: _mmbDown = mb.Pressed; break;
            case MouseButton.Right:
                if (mb.Pressed) { _rmbDown = true; _rmbDragged = false; _rmbStart = mb.Position; }
                else
                {
                    // A click demolishes what is under the mouse; a drag was only turning the camera.
                    if (_rmbDown && !_rmbDragged && CellUnderMouse() is { } cell) RemoveAt(cell);
                    _rmbDown = false;
                }
                break;
            case MouseButton.Left:
                if (mb.Pressed) _dragStart = _tool == ToolMode.Build ? CellUnderMouse() : null;
                else OnLeftRelease();
                break;
        }
    }

    private void OnMouseMotion(InputEventMouseMotion mm)
    {
        if (_rmbDown && (mm.Position - _rmbStart).Length() > 6f) _rmbDragged = true;
        if (_mmbDown || (_rmbDown && _rmbDragged))
        {
            _yaw -= mm.Relative.X * 0.005f;
            _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.005f, 0.35f, 1.5f);
        }
    }

    private void OnLeftRelease()
    {
        if (_tool == ToolMode.Build)
        {
            if (_dragStart is { } start && CellUnderMouse() is { } end)
                PlaceSelectedAt(end, _palette[_selected].IsBelt ? start : null);
            _dragStart = null;
            return;
        }

        // Pointer: inspect machines, pick deposits, fly to the ground.
        switch (_hover.Kind)
        {
            case HoverKind.Machine:
                _selMachine = _hover.Id;
                break;
            case HoverKind.Deposit:
                _selectedDeposit = _hover.Id;
                _selMachine = -1;
                Toast(L("toast.depositSelected", ItemName(Net.DepositItem(_hover.Id))));
                break;
            case HoverKind.Ground:
                _selMachine = -1;
                if (CellUnderMouse() is { } c)
                {
                    var ship = ShipPosition(0f);
                    _waypoint = new Vector3(c.X + 0.5f, ship.Y, c.Z + 0.5f);
                    _pendingMine = false;
                }
                break;
            default:
                _selMachine = -1;
                break;
        }
    }

    private void OnKey(InputEventKey k)
    {
        int digit = (int)(k.Keycode - Key.Key1);
        if (k.Keycode == Key.Key0) digit = 9;
        if (k.Keycode == Key.Q) digit = 10;
        if (k.Keycode == Key.B) digit = 11;
        if (digit >= 0 && digit < Math.Min(12, _palette.Count)) { SelectBuild(digit); return; }

        switch (k.Keycode)
        {
            case Key.Escape:
                if (_helpOpen) _helpOpen = false;
                else if (_selMachine >= 0) _selMachine = -1;
                else if (_tool == ToolMode.Build) _tool = ToolMode.Pointer;
                else if (_mapOpen) _mapOpen = false;
                else _waypoint = null;
                break;
            case Key.F1: _helpOpen = !_helpOpen; break;
            case Key.L: ToggleLanguage(); break;
            case Key.O: _objectivesOpen = !_objectivesOpen; break;
            case Key.Home: RecenterCamera(); break;
            case Key.Tab: CycleRecipe(); break;
            case Key.R: _dir = _dir switch { Dir.PosX => Dir.PosZ, Dir.PosZ => Dir.NegX, Dir.NegX => Dir.NegZ, _ => Dir.PosX }; Toast(L("toast.beltFacing", _dir.ToString())); break;
            case Key.Z: _cursorY--; break;
            case Key.X: _cursorY++; break;
            case Key.M: ToggleMining(); break;
            case Key.E: RepairAll(); break;
            case Key.Bracketleft: CycleDeposit(-1); break;
            case Key.Bracketright: CycleDeposit(1); break;
            case Key.K: Send(new Scan()); break;
            case Key.V: ToggleWeapons(); break;
            case Key.G: _mapOpen = !_mapOpen; break;
            case Key.J: SendJump(); break;
            case Key.Up: CycleSystem(-1); break;
            case Key.Down: CycleSystem(1); break;
            case Key.F: ToggleRefinery(); break;
            case Key.T: CycleResearch(); break;
            case Key.P: _paused = !_paused; break;
            case Key.Equal: case Key.KpAdd: _speed = Math.Min(10, _speed + 1); break;
            case Key.Minus: case Key.KpSubtract: _speed = Math.Max(1, _speed - 1); break;
        }
    }

    // ---- ship control ------------------------------------------------------------------------------------

    /// <summary>Keys fly the ship relative to the camera (snapped to the nearest world axis); a click flies to a point.</summary>
    private void UpdateShipControl()
    {
        if (_demo) return;

        int forward = (Input.IsKeyPressed(Key.W) ? 1 : 0) - (Input.IsKeyPressed(Key.S) ? 1 : 0);
        int right = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        int up = (Input.IsKeyPressed(Key.Space) ? 1 : 0) - (Input.IsKeyPressed(Key.C) ? 1 : 0);

        (int X, int Y, int Z) intent;
        if (forward != 0 || right != 0 || up != 0)
        {
            _waypoint = null;
            _pendingMine = false;
            float snapped = MathF.Round(_yaw / (MathF.PI / 2f)) * (MathF.PI / 2f);
            int fx = (int)MathF.Round(-MathF.Sin(snapped)), fz = (int)MathF.Round(-MathF.Cos(snapped));
            int rx = (int)MathF.Round(MathF.Cos(snapped)), rz = (int)MathF.Round(-MathF.Sin(snapped));
            intent = (Math.Clamp(fx * forward + rx * right, -1, 1), up, Math.Clamp(fz * forward + rz * right, -1, 1));
        }
        else if (_waypoint is { } wp)
        {
            var p = ShipPosition(0f);
            static int Axis(float diff) => MathF.Abs(diff) > 0.45f ? Math.Sign(diff) : 0;
            intent = (Axis(wp.X - p.X), Axis(wp.Y - p.Y), Axis(wp.Z - p.Z));
            if (intent == (0, 0, 0)) _waypoint = null; // arrived
        }
        else intent = (0, 0, 0);

        if (_pendingMine && Net.IsDeposit(_selectedDeposit) && Net.InMineRange(Player, _selectedDeposit))
        {
            _pendingMine = false;
            _waypoint = null;
            intent = (0, 0, 0);
            Send(new StartMining(_selectedDeposit));
        }

        if (intent != _lastIntent)
        {
            _lastIntent = intent;
            Send(new SetMovement(intent.X, intent.Y, intent.Z));
        }
    }

    /// <summary>The M button: fly to the selected deposit if needed, then mine it.</summary>
    private void ToggleMining()
    {
        if (Net.MiningDeposit(Player) >= 0 || _pendingMine)
        {
            _pendingMine = false;
            Send(new StopMining());
            return;
        }
        if (!Net.IsDeposit(_selectedDeposit) || !Net.IsDepositRevealed(_selectedDeposit))
        {
            var nearest = RevealedDeposits().FirstOrDefault(-1);
            if (nearest < 0) { Toast(L("toast.noDeposits")); return; }
            _selectedDeposit = nearest;
        }

        if (Net.InMineRange(Player, _selectedDeposit)) { Send(new StartMining(_selectedDeposit)); return; }

        // Out of range: fly to a point a few cells from the deposit on the ship's side, mine on arrival.
        var pos = Net.DepositPosition(_selectedDeposit);
        var dep = new Vector3(pos.X, pos.Y, pos.Z);
        var ship = ShipPosition(0f);
        var dir = (ship - dep);
        dir = dir.LengthSquared() < 0.01f ? Vector3.Right : dir.Normalized();
        _waypoint = dep + dir * (PlayerRules.MineRangeCells - 2);
        _pendingMine = true;
        Toast(L("toast.flyingToMine", ItemName(Net.DepositItem(_selectedDeposit))));
    }

    // ---- hover and inspection ----------------------------------------------------------------------------

    private static bool RayHitsSphere(Vector3 origin, Vector3 dir, Vector3 centre, float radius, out float t)
    {
        var oc = centre - origin;
        t = oc.Dot(dir);
        if (t < 0) return false;
        float d2 = oc.LengthSquared() - t * t;
        return d2 <= radius * radius;
    }

    private void UpdateHover()
    {
        _tooltipLabel.Text = "";
        _hover = new Hover(HoverKind.None, -1, "");
        if (_demo || OverUi()) return;

        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);

        // Enemies first (small and dangerous), then deposits.
        float best = float.MaxValue;
        foreach (var en in Net.Enemies())
        {
            var c = new Vector3(en.X, en.Y, en.Z) / CellUnits;
            if (RayHitsSphere(origin, dir, c, 0.7f, out float t) && t < best)
            {
                best = t;
                _hover = new Hover(HoverKind.Enemy, en.Id, L("tip.enemy", Loc.Name("enemy", _content.Enemies[en.Type].Id), en.Hp));
            }
        }
        for (int d = 0; d < Net.DepositCount; d++)
        {
            if (!Net.IsDepositRevealed(d) || Net.DepositRemaining(d) <= 0) continue;
            var pos = Net.DepositPosition(d);
            float scale = Mathf.Max(0.15f, 0.35f + 0.65f * Net.DepositRemaining(d) / Net.DepositInitial(d));
            if (RayHitsSphere(origin, dir, new Vector3(pos.X, pos.Y, pos.Z), 1.5f * scale, out float t) && t < best)
            {
                best = t;
                _hover = new Hover(HoverKind.Deposit, d, L("tip.deposit", ItemName(Net.DepositItem(d)), Net.DepositRemaining(d), Net.DepositInitial(d)));
            }
        }

        if (_hover.Kind == HoverKind.None && CellUnderMouse() is { } cell)
        {
            _cursor = cell;
            var (kind, id) = _world.EntityAt(cell.X, cell.Y, cell.Z);
            if (kind == SimWorld.EntityKind.Machine) _hover = new Hover(HoverKind.Machine, id, MachineSummary(id));
            else if (kind == SimWorld.EntityKind.Segment)
            {
                var s = Net.GetSegmentInfo(id);
                _hover = new Hover(HoverKind.Segment, id, L("tip.belt", (int)s.Tier + 1, s.LengthCells, Net.CountOn(id)));
            }
            else _hover = new Hover(HoverKind.Ground, -1, "");
        }

        if (_tool == ToolMode.Build) return; // the ghost explains placement instead
        _tooltipLabel.Text = _hover.Text;
    }

    private string MachineStatus(MachineInfo m)
    {
        var def = _content.Machines[m.Type];
        switch (def.Role)
        {
            case MachineRole.Drill:
                if (m.Deposit < 0) return L("status.drillUnlinked");
                if (Net.DepositRemaining(m.Deposit) <= 0) return L("status.drillEmpty");
                return m.Crafting ? L("status.working") : m.OutputItems > 0 ? L("status.outputBlocked") : L("status.drillIdle");
            case MachineRole.Lab:
                return Net.CurrentResearch < 0 ? L("status.labNone") : L("status.labWorking", Loc.Name("research", _content.Research[Net.CurrentResearch].Id));
            case MachineRole.Depot:
                if (Net.HoldTotal(Player) >= PlayerRules.HoldCapacity) return L("status.depotFull");
                foreach (var item in _content.Items)
                    if (Net.HoldCount(Player, item.Index) >= PlayerRules.DepotItemCap)
                        return L("status.depotCap", Loc.Name("item", item.Id), PlayerRules.DepotItemCap);
                return L("status.depotOk");
            case MachineRole.Wall:
                return L("status.wall");
            case MachineRole.Unloader:
            {
                var un = def.Unloader!;
                if (Net.MachineOutputCount(m.Id) == 0) return L("status.unloaderNoBelt");
                int held = Net.HoldCount(Player, un.Item);
                return held > un.Reserve ? L("status.unloaderReady", held, un.Reserve) : L("status.unloaderEmpty", un.Reserve);
            }
            case MachineRole.Repairer:
            {
                var rep = def.Repairer!;
                return m.InputItems > 0 ? L("status.repairerReady", rep.RangeCells, m.InputItems, rep.ItemCap) : L("status.repairerEmpty");
            }
            case MachineRole.Turret:
                var t = def.Turret!;
                return m.InputItems > 0 ? L("status.turretReady", m.InputItems, t.AmmoCap) : L("status.turretEmpty");
            default:
                if (m.Recipe < 0) return L("status.noRecipe");
                if (m.Crafting) return L("status.working");
                if (m.OutputItems > 0) return L("status.outputBlocked");
                return L("status.waitingInputs");
        }
    }

    private string MachineSummary(int id)
    {
        var m = Net.GetMachineInfo(id);
        var def = _content.Machines[m.Type];
        string recipe = m.Recipe >= 0 ? " - " + Loc.Name("recipe", _content.Recipes[m.Recipe].Id) : "";
        return $"{MachineName(m.Type)}{recipe}\n{MachineStatus(m)}   {L("tip.hp", m.Hp, Net.MaxHp(m.Type))}";
    }

    private void UpdateInspector()
    {
        if (_selMachine >= 0 && !Net.IsMachineAlive(_selMachine)) _selMachine = -1;
        _inspector.Visible = _selMachine >= 0 && !_demo;
        if (_selMachine < 0) return;

        var m = Net.GetMachineInfo(_selMachine);
        var def = _content.Machines[m.Type];
        _inspectorTitle.Text = MachineName(m.Type);

        var sb = new StringBuilder();
        sb.Append(MachineStatus(m)).Append('\n');
        sb.Append(L("tip.hp", m.Hp, Net.MaxHp(m.Type))).Append('\n');
        if (m.Recipe >= 0)
        {
            var r = _content.Recipes[m.Recipe];
            sb.Append(L("insp.recipe", Loc.Name("recipe", r.Id))).Append('\n');
            sb.Append(L("insp.takes", r.Inputs.Length == 0 ? "-" : Stacks(r.Inputs))).Append('\n');
            sb.Append(L("insp.makes", Stacks(r.Outputs), (r.TimeTicks / (float)Units.TicksPerSecond).ToString("F1", Loc.Culture))).Append('\n');
        }
        if (def.Signature > 0) sb.Append(L("tip.signature", def.Signature)).Append('\n');
        _inspectorBody.Text = sb.ToString().TrimEnd('\n');

        var options = RecipesFor(m.Type);
        _inspPrev.Visible = _inspNext.Visible = options.Count > 1;
        _inspRemove.Text = L("insp.remove", Stacks(def.Cost));
        bool damaged = m.Hp < Net.MaxHp(m.Type);
        _inspRepair.Visible = damaged;
        if (damaged) _inspRepair.Text = L("insp.repair", Stacks(Net.RepairCost(_selMachine)));
    }

    private List<int> RecipesFor(int machineType) =>
        _content.Recipes.Where(r => r.Machine == machineType && !Net.IsRecipeLocked(r.Index)).Select(r => r.Index).ToList();

    private void ChangeSelectedRecipe(int step)
    {
        if (_selMachine < 0 || !Net.IsMachineAlive(_selMachine)) return;
        var m = Net.GetMachineInfo(_selMachine);
        var options = RecipesFor(m.Type);
        if (options.Count < 2) return;
        int i = options.IndexOf(m.Recipe);
        int next = options[((i < 0 ? 0 : i + step) % options.Count + options.Count) % options.Count];
        Send(new SetRecipe(_selMachine, next));
        // A drill follows its recipe to a deposit of the new ore, when one is within reach.
        if (_content.Machines[m.Type].Role == MachineRole.Drill) RelinkDrill(_selMachine, next);
    }

    private void RelinkDrill(int machine, int recipe)
    {
        var mi = Net.GetMachineInfo(machine);
        var item = _content.Recipes[recipe].Outputs[0].Item;
        int best = -1;
        for (int d = 0; d < Net.DepositCount; d++)
        {
            if (Net.DepositItem(d) != item || !Net.IsDepositRevealed(d) || Net.DepositRemaining(d) <= 0) continue;
            var dp = Net.DepositPosition(d);
            long dx = dp.X - mi.X, dy = dp.Y - mi.Y, dz = dp.Z - mi.Z;
            if (dx * dx + dy * dy + dz * dz > (long)ExplorationRules.DrillRangeCells * ExplorationRules.DrillRangeCells) continue;
            if (best < 0 || Net.DepositRemaining(d) > Net.DepositRemaining(best)) best = d;
        }
        if (best >= 0) Send(new LinkDeposit(machine, best));
        else Toast(L("toast.noDepositForDrill"));
    }

    // ---- build ghost ----------------------------------------------------------------------------------------

    /// <summary>Why the selected item cannot be placed in this cell right now, or null if it can.</summary>
    private string? PlacementProblem((int X, int Y, int Z) cell)
    {
        var item = _palette[_selected];
        bool locked = item.IsBelt ? Net.IsBeltLocked(item.Tier) : Net.IsMachineLocked(item.Machine);
        if (locked) return L("why.locked");
        if (!_world.InBuildRange(Player, cell.X, cell.Y, cell.Z)) return L("why.outOfRange");
        if (_world.EntityAt(cell.X, cell.Y, cell.Z).Kind != SimWorld.EntityKind.None) return L("why.overlap");
        var cost = item.IsBelt ? _content.Belts[(int)item.Tier]?.CostPerCell ?? Array.Empty<ItemStack>() : _content.Machines[item.Machine].Cost;
        if (!Net.CanPay(Player, cost)) return L("why.notEnough");
        return null;
    }

    private void UpdateGhost()
    {
        bool build = _tool == ToolMode.Build && !_demo && _hover.Kind != HoverKind.None && !OverUi();
        _ghost.Visible = build;
        if (!build) { _dragGhost.Visible = false; return; }

        var c = _cursor;
        _ghost.Position = CellCenter(c.X, c.Y, c.Z);
        string? problem = PlacementProblem(c);
        ((StandardMaterial3D)_ghost.MaterialOverride).AlbedoColor = problem is null ? new Color(0.4f, 1f, 0.5f, 0.4f) : new Color(1f, 0.3f, 0.25f, 0.4f);
        if (problem is not null) _tooltipLabel.Text = problem;
        else if (_dragStart is null && _palette[_selected].IsBelt)
        {
            var cost = _content.Belts[(int)_palette[_selected].Tier]?.CostPerCell ?? Array.Empty<ItemStack>();
            _tooltipLabel.Text = L("tip.cost", Stacks(cost) + L("ui.perCell"));
        }
        UpdateDragGhost();
    }
}
