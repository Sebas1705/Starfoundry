using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The panel of a station crafting machine (ADR 0039): F, or a click, on the machine opens it. It shows the machine (icon, name, hit points), its status (running, waiting
/// for input, output full, no power, off), the recipe list with a button to use each, the throughput per minute, the input and output buffers as slots, and the buttons to
/// switch it off and on (the recipe it had comes back), repair it and remove it. The buffers are display only: moving items in and out is done in the inventory panel
/// (the aimed machine opens there). The logic is in <see cref="MachineView"/>.
/// </summary>
public partial class MachinePanel : PanelContainer
{
    private const float Width = 400f;
    private const int Slot = 44;

    private readonly Func<BeltNetwork> _net;
    private readonly ContentDb _content;
    private readonly Action<Command> _send;
    private readonly Func<int, int> _satisfaction;
    private readonly Func<int, bool> _needsPower;
    private readonly Func<int, List<int>> _recipesFor;
    private readonly Action<int> _remove;
    private readonly Action<int> _afterRecipe;

    private int _machine = -1, _builtType = -1;
    private readonly Dictionary<int, int> _remembered = new();

    private IconRect _icon = null!;
    private Label _title = null!, _hp = null!, _status = null!, _rate = null!, _recipesHead = null!, _inLabel = null!, _outLabel = null!, _hint = null!;
    private GaugeBar _bar = null!;
    private Button _close = null!, _power = null!, _repair = null!, _removeBtn = null!;
    private VBoxContainer _recipeBox = null!;
    private HBoxContainer _inRow = null!, _outRow = null!;
    private readonly List<RecipeRow> _rows = new();
    private readonly List<InventorySlot> _inSlots = new(), _outSlots = new();

    public event Action? CloseRequested;

    private sealed class RecipeRow
    {
        public int Recipe;
        public Label Name = null!, Time = null!;
        public Button Use = null!;
    }

    public MachinePanel(Func<BeltNetwork> net, ContentDb content, Action<Command> send, Func<int, int> satisfaction, Func<int, bool> needsPower,
        Func<int, List<int>> recipesFor, Action<int> remove, Action<int> afterRecipe)
    {
        _net = net;
        _content = content;
        _send = send;
        _satisfaction = satisfaction;
        _needsPower = needsPower;
        _recipesFor = recipesFor;
        _remove = remove;
        _afterRecipe = afterRecipe;
        Name = "MachinePanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiTheme.Dock());
        SetMeta(UiAudit.ModalMeta, true);
        Build();
    }

    /// <summary>The machine shown, or -1.</summary>
    public int Machine => _machine;

    private static Label Text(int size, Color? colour = null, bool wrap = false)
    {
        var l = UiKit.Text(size, colour ?? UiTheme.Text);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(10, 0); l.SizeFlagsHorizontal = SizeFlags.ExpandFill; }
        return l;
    }

    private static void SetText(Label l, string text)
    {
        if (l.Text != text) l.Text = text;
    }

    private void Build()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", UiTheme.Gap);
        AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        _icon = new IconRect("tool:build", 28, null, tooltip: false);
        head.AddChild(_icon);
        _title = Text(UiTheme.Heading + 2, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.CustomMinimumSize = new Vector2(10, 0);
        head.AddChild(_title);
        _hp = Text(UiTheme.Small, UiTheme.Muted);
        head.AddChild(_hp);
        _close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        _close.Icon = Icons.Get("tool:close", 16);
        _close.ExpandIcon = false;
        _close.FocusMode = FocusModeEnum.None;
        _close.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
        head.AddChild(_close);
        root.AddChild(head);
        root.AddChild(UiTheme.Rule());

        _status = Text(UiTheme.Body, UiTheme.Text, wrap: true);
        root.AddChild(_status);
        _bar = new GaugeBar(120f, 8f);
        root.AddChild(_bar);
        _rate = Text(UiTheme.Small, UiTheme.Muted, wrap: true);
        root.AddChild(_rate);
        root.AddChild(UiTheme.Rule());

        _recipesHead = Text(UiTheme.Body, UiTheme.Gold);
        root.AddChild(_recipesHead);
        _recipeBox = new VBoxContainer();
        _recipeBox.AddThemeConstantOverride("separation", 4);
        root.AddChild(_recipeBox);
        root.AddChild(UiTheme.Rule());

        var inBox = new HBoxContainer();
        inBox.AddThemeConstantOverride("separation", 6);
        _inLabel = Text(UiTheme.Small, UiTheme.Muted);
        inBox.AddChild(_inLabel);
        _inRow = new HBoxContainer();
        _inRow.AddThemeConstantOverride("separation", 6);
        inBox.AddChild(_inRow);
        root.AddChild(inBox);
        var outBox = new HBoxContainer();
        outBox.AddThemeConstantOverride("separation", 6);
        _outLabel = Text(UiTheme.Small, UiTheme.Muted);
        outBox.AddChild(_outLabel);
        _outRow = new HBoxContainer();
        _outRow.AddThemeConstantOverride("separation", 6);
        outBox.AddChild(_outRow);
        root.AddChild(outBox);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _power = UiKit.Button("", TogglePower, 13);
        _repair = UiKit.Button("", () => _send(new Repair(_machine)), 13);
        _removeBtn = UiKit.Button("", () => _remove(_machine), 13);
        foreach (var b in new[] { _power, _repair, _removeBtn }) { b.FocusMode = FocusModeEnum.None; b.ClipText = true; b.CustomMinimumSize = new Vector2(10, 0); b.SizeFlagsHorizontal = SizeFlags.ExpandFill; buttons.AddChild(b); }
        root.AddChild(buttons);
        _hint = Text(UiTheme.Small, UiTheme.Dim);
        root.AddChild(_hint);
    }

    private void TogglePower()
    {
        var net = _net();
        if (_machine < 0 || !net.IsMachineAlive(_machine)) return;
        var m = net.GetMachineInfo(_machine);
        if (m.Recipe >= 0)
        {
            _remembered[_machine] = MachineView.Remember(m.Recipe, _remembered.GetValueOrDefault(_machine, -1));
            _send(new SetRecipe(_machine, -1));
        }
        else
        {
            int back = MachineView.RestoreRecipe(_remembered.GetValueOrDefault(_machine, -1), _recipesFor(m.Type));
            if (back >= 0) { _send(new SetRecipe(_machine, back)); _afterRecipe(_machine); }
        }
    }

    private void UseRecipe(int recipe)
    {
        if (_machine < 0) return;
        _remembered[_machine] = recipe;
        _send(new SetRecipe(_machine, recipe));
        _afterRecipe(_machine);
    }

    /// <summary>Shows the panel for a machine (or a different one).</summary>
    public void Open(int machine)
    {
        bool was = Visible && _machine == machine;
        _machine = machine;
        Visible = true;
        Refresh();
        if (!was) UiFx.Present(this);
    }

    public void Close()
    {
        Visible = false;
        _machine = -1;
    }

    /// <summary>Puts the panel at the right, with its bottom edge at <paramref name="bottom"/> (above the key panel).</summary>
    public void Layout(Vector2 size, float bottom)
    {
        float w = Math.Min(Width, size.X - 24f);
        CustomMinimumSize = new Vector2(w, 0);
        ResetSize();
        var min = GetCombinedMinimumSize();
        float h = Math.Min(min.Y, Math.Max(240f, bottom - 12f));
        Size = new Vector2(w, h);
        Position = new Vector2(size.X - w - 12f, Math.Max(8f, bottom - h));
    }

    private void RebuildRecipes(int type, List<int> options)
    {
        foreach (var c in _recipeBox.GetChildren()) c.QueueFree();
        _rows.Clear();
        foreach (int index in options)
        {
            var r = _content.Recipes[index];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new IconRect(Icons.ItemId(_content.Items[r.Outputs[0].Item].Id), 22, null, tooltip: false));
            var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            mid.AddThemeConstantOverride("separation", 1);
            var name = Text(UiTheme.Body, UiTheme.Text);
            name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            name.CustomMinimumSize = new Vector2(10, 0);
            mid.AddChild(name);
            var io = new HBoxContainer();
            io.AddThemeConstantOverride("separation", 4);
            foreach (var s in r.Inputs) { var c = new IconCount(Icons.ItemId(_content.Items[s.Item].Id), 16, UiTheme.Caption + 1); c.Set(Icons.ItemId(_content.Items[s.Item].Id), s.Count); io.AddChild(c); }
            var arrow = Text(UiTheme.Small, UiTheme.Dim);
            arrow.Text = ">";
            io.AddChild(arrow);
            foreach (var s in r.Outputs) { var c = new IconCount(Icons.ItemId(_content.Items[s.Item].Id), 16, UiTheme.Caption + 1); c.Set(Icons.ItemId(_content.Items[s.Item].Id), s.Count); io.AddChild(c); }
            mid.AddChild(io);
            row.AddChild(mid);
            var time = Text(UiTheme.Small, UiTheme.Muted);
            time.HorizontalAlignment = HorizontalAlignment.Right;
            time.CustomMinimumSize = new Vector2(38, 0);
            row.AddChild(time);
            var use = UiKit.Button("", () => UseRecipe(index), 13, 58);
            use.FocusMode = FocusModeEnum.None;
            row.AddChild(use);
            _recipeBox.AddChild(row);
            _rows.Add(new RecipeRow { Recipe = index, Name = name, Time = time, Use = use });
        }
        _builtType = type;
    }

    private void SizeSlots(List<InventorySlot> slots, HBoxContainer row, int count)
    {
        while (slots.Count < count) { var s = new InventorySlot(Slot); slots.Add(s); row.AddChild(s); }
        for (int i = 0; i < slots.Count; i++) slots[i].Visible = i < count;
    }

    /// <summary>Re-reads the machine: status, throughput, recipes, buffers and buttons.</summary>
    public void Refresh()
    {
        if (!Visible) return;
        var net = _net();
        if (_machine < 0 || !net.IsMachineAlive(_machine)) { Close(); return; }
        var m = net.GetMachineInfo(_machine);
        var def = _content.Machines[m.Type];
        var options = _recipesFor(m.Type);
        if (m.Type != _builtType || _rows.Count != options.Count) RebuildRecipes(m.Type, options);

        _icon.Set(IconCatalog.ForMachine(def.Id));
        SetText(_title, Loc.Name("machine", def.Id).ToUpperInvariantFirst());
        int maxHp = net.MaxHp(m.Type);
        SetText(_hp, Loc.T("tip.hp", m.Hp, maxHp));
        _close.TooltipText = Loc.T("bench.panel.close");

        bool needsPower = _needsPower(m.Type);
        int sat = _satisfaction(m.Structure);
        var run = MachineView.Classify(m.Recipe >= 0, needsPower, sat, m.Crafting, m.OutputItems);
        SetText(_status, Loc.T("machine.panel.status", Loc.T(MachineView.StatusKey(run))));
        _status.AddThemeColorOverride("font_color", run switch
        {
            MachineRun.Running => UiTheme.Good,
            MachineRun.NoPower => UiTheme.Bad,
            MachineRun.NoRecipe => UiTheme.Dim,
            _ => UiTheme.Warn,
        });

        var recipe = m.Recipe >= 0 ? _content.Recipes[m.Recipe] : null;
        float progress = recipe is null ? 0f : MachineView.Progress(m.Crafting, m.Remaining, recipe.TimeTicks);
        _bar.Set(progress, run == MachineRun.Running ? UiTheme.Good : UiTheme.Warn);
        if (recipe is null) SetText(_rate, "");
        else
        {
            int outCount = recipe.Outputs.Sum(o => o.Count);
            double nominal = MachineView.NominalPerMinute(outCount, recipe.TimeTicks);
            double now = MachineView.CurrentPerMinute(run, outCount, recipe.TimeTicks, sat);
            SetText(_rate, run == MachineRun.Running && sat < PowerRules.FullSatisfaction
                ? Loc.T("machine.panel.perMinuteNow", MachineView.PerMinuteText(now), MachineView.PerMinuteText(nominal))
                : Loc.T("machine.panel.perMinute", MachineView.PerMinuteText(run == MachineRun.Running ? now : nominal)));
        }

        SetText(_recipesHead, options.Count == 0 ? Loc.T("machine.panel.noRecipes") : Loc.T("machine.panel.recipes"));
        foreach (var row in _rows)
        {
            var r = _content.Recipes[row.Recipe];
            SetText(row.Name, Loc.Name("recipe", r.Id).ToUpperInvariantFirst());
            SetText(row.Time, Loc.T("bench.panel.seconds", (r.TimeTicks / (float)Units.TicksPerSecond).ToString("0.#", Loc.Culture)));
            bool current = row.Recipe == m.Recipe;
            row.Use.Text = current ? Loc.T("machine.panel.current") : Loc.T("machine.panel.pick");
            row.Use.Disabled = current;
        }

        // The buffers of the recipe in hand (or of the one that comes back when switched on).
        int shown = m.Recipe >= 0 ? m.Recipe : _remembered.GetValueOrDefault(_machine, -1);
        var buffer = shown >= 0 ? _content.Recipes[shown] : null;
        SetText(_inLabel, Loc.T("machine.panel.input"));
        SetText(_outLabel, Loc.T("machine.panel.output"));
        int ins = buffer?.Inputs.Length ?? 0, outs = buffer?.Outputs.Length ?? 0;
        SizeSlots(_inSlots, _inRow, ins);
        SizeSlots(_outSlots, _outRow, outs);
        for (int i = 0; i < ins; i++)
        {
            var s = buffer!.Inputs[i];
            var item = _content.Items[s.Item];
            int have = net.MachineInCount(_machine, s.Item);
            _inSlots[i].Set(s.Item, have, net.MachineInputCapacity(_machine, s.Item), item.Id, $"{Loc.Name("item", item.Id)}: {have}/{net.MachineInputCapacity(_machine, s.Item)}", false);
        }
        for (int i = 0; i < outs; i++)
        {
            var s = buffer!.Outputs[i];
            var item = _content.Items[s.Item];
            int have = net.MachineOutCount(_machine, s.Item);
            _outSlots[i].Set(s.Item, have, 0, item.Id, $"{Loc.Name("item", item.Id)}: {have}", false);
        }

        bool off = m.Recipe < 0;
        _power.Text = off ? Loc.T("machine.panel.enable") : Loc.T("machine.panel.disable");
        _power.Disabled = off && options.Count == 0;
        bool damaged = m.Hp < maxHp;
        _repair.Visible = damaged;
        if (damaged) _repair.Text = _repair.TooltipText = Loc.T("insp.repair", string.Join(", ", net.RepairCost(_machine).Select(s => $"{Loc.Name("item", _content.Items[s.Item].Id)} x{s.Count}")));
        _removeBtn.Text = _removeBtn.TooltipText = Loc.T("insp.remove", string.Join(", ", def.Cost.Select(s => $"{Loc.Name("item", _content.Items[s.Item].Id)} x{s.Count}")));
        SetText(_hint, Loc.T("bench.panel.hint"));
    }
}
