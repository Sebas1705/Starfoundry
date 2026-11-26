using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>One container the right-hand panel can show: what to move into, its name and its icon.</summary>
public sealed record InventoryTab(ContainerRef Ref, string Label, string Icon, string Tip);

/// <summary>
/// The personal inventory (ADR 0035), opened with F10 or from the reactor or machine you aim at. Two panels side by side: your bag (24 slots, filter
/// chips, a sort button and a bin that asks first) and the container you are working with (the ship hold, a station's stock, a reactor's fuel slot with its
/// burn time and auto-feed selector, or a machine's input and output). Drag a stack with the left button (the whole stack) or the right one (the larger half)
/// from slot to slot; Shift+click or a double click moves it to the other panel; the keyboard picks up and drops with Enter and moves with the arrows.
/// Every change is a command (<see cref="MoveItems"/>, <see cref="SetFeedSource"/>, <see cref="SortInventory"/>); the panel only draws what the world says.
/// </summary>
public partial class InventoryPanel : Control
{
    private readonly Func<BeltNetwork> _net;
    private readonly Func<int> _player;
    private readonly ContentDb _content;
    private readonly Action<Command> _send;
    private readonly Action<string> _toast;

    private PanelContainer _window = null!;
    private VBoxContainer _root = null!;
    private HBoxContainer _body = null!;
    private VBoxContainer _left = null!, _right = null!;
    private Label _title = null!;
    private FitLabel _hint = null!, _legacy = null!;
    private HBoxContainer _confirm = null!;
    private Label _confirmText = null!;
    private DragGhost _ghost = null!;
    private Rect2 _area = new(0, 0, 1280, 720);
    private Vector2 _viewport = new(1280, 720);

    private readonly List<InventorySlot> _slots = new();
    private InventorySlot? _bin;
    private Label? _slotsLabel;
    private readonly Dictionary<ItemCategory, Button> _chips = new();
    private readonly List<Button> _feedButtons = new();
    private FitLabel? _feedTip, _burnLine;
    private GaugeBar? _gauge;
    private Label? _gaugeText;

    private ItemCategory _filter = ItemCategory.All;
    private ContainerRef _other = ContainerRef.Hold;
    private int _machine = -1;
    private string _signature = "";
    private int _tile = 48;
    private List<InventoryTab> _tabs = new();

    // drag, pick-up and the pending bin
    private InventorySlot? _pressed, _drag, _selected;
    private bool _dragHalf, _pressRight;
    private (ContainerRef From, int Slot, int Item, int Count)? _held, _binPending;

    public event Action? CloseRequested;

    /// <summary>The container beside the bag.</summary>
    public ContainerRef Other => _other;

    /// <summary>A stack of the bag or the hold was dragged out of the window and released over the world (ADR 0038): container, slot, count.</summary>
    public event Action<ContainerRef, int, int>? DropToWorldRequested;

    public bool Dragging => _drag is not null;

    public bool Holding => _held is not null || _binPending is not null || _drag is not null;

    /// <summary>The slots, for tests and the screenshot driver.</summary>
    public IReadOnlyList<InventorySlot> Slots => _slots;

    public InventoryPanel(Func<BeltNetwork> net, Func<int> player, ContentDb content, Action<Command> send, Action<string> toast)
    {
        _net = net;
        _player = player;
        _content = content;
        _send = send;
        _toast = toast;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _window = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _window.AddThemeStyleboxOverride("panel", UiTheme.Dock());
        AddChild(_window);
        _root = new VBoxContainer();
        _root.AddThemeConstantOverride("separation", 6);
        _window.AddChild(_root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(new IconRect("tool:inventory", 24, UiTheme.Gold, tooltip: false));
        _title = UiKit.Text(UiTheme.Title, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        var close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        close.Icon = Icons.Get("tool:close", 16);
        close.ExpandIcon = false;
        close.FocusMode = FocusModeEnum.None;
        close.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
        close.TooltipText = Loc.T("inv.close");
        head.AddChild(close);
        _root.AddChild(head);
        _root.AddChild(UiTheme.Rule());

        _body = new HBoxContainer();
        _body.AddThemeConstantOverride("separation", 12);
        _root.AddChild(_body);
        _left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _left.AddThemeConstantOverride("separation", 6);
        _right.AddThemeConstantOverride("separation", 6);
        _body.AddChild(_left);
        _body.AddChild(_right);

        _legacy = new FitLabel(4, UiTheme.Body, UiTheme.Small) { Visible = false };
        _legacy.AddThemeColorOverride("font_color", UiTheme.Warn);
        _root.AddChild(_legacy);

        _confirm = new HBoxContainer { Visible = false };
        _confirm.AddThemeConstantOverride("separation", 8);
        _confirmText = UiKit.Text(UiTheme.Body, UiTheme.Warn);
        _confirmText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _confirmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _confirm.AddChild(_confirmText);
        _confirm.AddChild(UiKit.Button("", ConfirmBin, 13));
        _confirm.AddChild(UiKit.Button("", CancelBin, 13));
        _root.AddChild(_confirm);

        _hint = new FitLabel(2, UiTheme.Small, UiTheme.Caption);
        _hint.AddThemeColorOverride("font_color", UiTheme.Muted);
        _root.AddChild(_hint);

        _ghost = new DragGhost();
        AddChild(_ghost);
        ApplyLayout();
    }

    // ---- layout ------------------------------------------------------------------------------------------------

    /// <summary>Tells the panel where it may go: the screen minus what the key panel covers. The window centres in <paramref name="area"/>.</summary>
    public void Layout(Rect2 area, Vector2 viewport)
    {
        if (_area == area && _viewport == viewport) return;
        _area = area;
        _viewport = viewport;
        ApplyLayout();
    }

    private int TileFor()
    {
        float width = Math.Min(_area.Size.X - 24f, 940f);
        float perColumn = (width - 24f - 12f) / 2f; // window margins and the gap between the two panels
        int byWidth = (int)((perColumn - 5 * 4f) / 6f);
        int byHeight = (int)((_area.Size.Y - 20f - 232f) / 4f) - 4;
        return Math.Clamp(Math.Min(byWidth, byHeight), 34, 70);
    }

    private void ApplyLayout()
    {
        if (_window is null) return;
        int tile = TileFor();
        if (tile != _tile) { _tile = tile; _signature = ""; }
        float width = Math.Min(_area.Size.X - 24f, 940f);
        _window.CustomMinimumSize = new Vector2(width, 0);
        _window.ResetSize();
        _window.Size = new Vector2(width, _window.GetCombinedMinimumSize().Y);
        _window.Position = new Vector2(_area.Position.X + (_area.Size.X - width) / 2f, _area.Position.Y + 10f);
    }

    // ---- open and close ----------------------------------------------------------------------------------------

    /// <summary>Shows the panel. <paramref name="target"/> picks the container beside the bag (a reactor you aimed at); <paramref name="machine"/> adds a machine's tab.</summary>
    public void Open(ContainerRef? target = null, int machine = -1)
    {
        _machine = machine;
        if (target is { } t) _other = t;
        else if (machine >= 0) _other = ContainerRef.MachineIn(machine);
        Visible = true;
        _signature = "";
        _held = null; _binPending = null; _drag = null;
        Refresh();
        FirstSlot()?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        Visible = false;
        EndDrag(false, default);
        _held = null; _binPending = null;
    }

    private InventorySlot? FirstSlot() => _slots.FirstOrDefault(s => s.Container.Kind == ContainerKind.Person);

    /// <summary>Esc: cancels what is in progress (a drag, a picked-up stack, the bin question); false when nothing was, so the game closes the panel.</summary>
    public bool HandleEscape()
    {
        if (_drag is not null) { EndDrag(false, default); return true; }
        if (_binPending is not null) { CancelBin(); return true; }
        if (_held is not null) { _held = null; RefreshFlags(); return true; }
        return false;
    }

    // ---- content ----------------------------------------------------------------------------------------------

    private BeltNetwork Net => _net();

    private int Me => _player();

    private int BurnInterval => Net.BurnInterval(ModuleKind.Reactor);

    private string ItemIdOf(int item) => item >= 0 && item < _content.Items.Count ? _content.Items[item].Id : "";

    private string TipFor(int item, int count) => item < 0 ? "" : InventoryView.Tooltip(_content, item, count, _content.StackSizeOf(item), BurnInterval);

    private IEnumerable<InventoryTab> ComputeTabs()
    {
        var net = Net;
        int me = Me;
        yield return new InventoryTab(ContainerRef.Hold, Loc.T("inv.tab.hold"), "tool:hold", Loc.T("inv.hold.title"));
        if (!net.StructureRules) yield break;
        var reactors = new List<InventoryTab>();
        foreach (var s in net.Structures())
        {
            if (s.Owner != me) continue;
            if (s.Kind == StructureKind.Station && net.CheckReach(me, ContainerRef.Stock(s.Id)) == Reject.None)
                yield return new InventoryTab(ContainerRef.Stock(s.Id), Loc.T("inv.tab.stock"), "tool:stock", Loc.T("inv.stock.title"));
            foreach (var (key, kind) in s.Modules)
                if (ModuleKindIds.IsReactor(kind) && net.CheckReach(me, ContainerRef.Reactor(s.Id, key.Mx, key.My, key.Mz)) == Reject.None)
                    reactors.Add(new InventoryTab(ContainerRef.Reactor(s.Id, key.Mx, key.My, key.Mz), "", "module:" + ModuleKindIds.Id(kind), Loc.T("inv.reactor.title")));
        }
        for (int i = 0; i < reactors.Count; i++)
            yield return reactors[i] with { Label = reactors.Count == 1 ? Loc.T("inv.tab.reactor") : Loc.T("inv.tab.reactorN", i + 1) };
        if (_machine >= 0 && net.IsMachineAlive(_machine) && net.CheckReach(me, ContainerRef.MachineIn(_machine)) == Reject.None)
        {
            string name = Loc.Name("machine", _content.Machines[net.GetMachineInfo(_machine).Type].Id);
            yield return new InventoryTab(ContainerRef.MachineIn(_machine), name, "tool:machine", name);
        }
    }

    private static bool SameTab(ContainerRef a, ContainerRef b)
    {
        bool machineA = a.Kind is ContainerKind.MachineIn or ContainerKind.MachineOut, machineB = b.Kind is ContainerKind.MachineIn or ContainerKind.MachineOut;
        if (machineA || machineB) return machineA && machineB && a.Id == b.Id;
        return a == b;
    }

    /// <summary>Re-reads the world: rebuilds the grids when what they show changed shape, otherwise only updates the slots.</summary>
    public void Refresh()
    {
        if (!Visible || _window is null) return;
        _title.Text = Loc.T("inv.title");
        var net = Net;
        bool legacy = net.Rules.LegacyInventory || !net.HasPlayer(Me);
        _legacy.Visible = legacy;
        _body.Visible = !legacy;
        _hint.Visible = !legacy;
        if (legacy)
        {
            _legacy.SetFit(Loc.T("inv.legacy"));
            ApplyLayout();
            return;
        }
        _tabs = ComputeTabs().ToList();
        if (!_tabs.Any(t => SameTab(t.Ref, _other))) _other = _tabs[0].Ref;
        var otherRef = _tabs.First(t => SameTab(t.Ref, _other)).Ref;
        _other = otherRef;
        int columns = 6;
        var stacks = OtherStacks();
        string sig = string.Join("|", Loc.Lang, _tile, string.Join(",", _tabs.Select(t => t.Ref)), _other, stacks.Count, _content.Items.Count);
        if (sig != _signature)
        {
            _signature = sig;
            Rebuild(columns, stacks);
        }
        UpdateContents(stacks);
        ApplyLayout();
    }

    /// <summary>The (slot, item, count) stacks of the container beside the bag, in list order.</summary>
    private List<(int Slot, int Item, int Count)> OtherStacks()
    {
        var list = new List<(int, int, int)>();
        if (_other.Kind is ContainerKind.Hold or ContainerKind.Stock or ContainerKind.Reactor)
            list.AddRange(Net.ContainerStacks(Me, _other));
        return list;
    }

    private void Rebuild(int columns, List<(int Slot, int Item, int Count)> stacks)
    {
        var focus = GetViewport()?.GuiGetFocusOwner() as InventorySlot;
        var focusKey = focus is null ? default((ContainerRef, int)?) : (focus.Container, focus.Slot);
        foreach (var child in _left.GetChildren()) { _left.RemoveChild(child); child.QueueFree(); }
        foreach (var child in _right.GetChildren()) { _right.RemoveChild(child); child.QueueFree(); }
        _slots.Clear();
        _chips.Clear();
        _feedButtons.Clear();
        _bin = null; _selected = null; _feedTip = null; _burnLine = null; _gauge = null; _gaugeText = null;
        BuildBag(columns);
        BuildOther(columns, stacks);
        if (focusKey is { } fk) _slots.FirstOrDefault(s => (s.Container, s.Slot) == fk)?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private InventorySlot NewSlot(ContainerRef c, int slot, int pixels)
    {
        var s = new InventorySlot(this, c, slot, pixels);
        _slots.Add(s);
        return s;
    }

    private GridContainer NewGrid(int columns)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        return grid;
    }

    private Label Heading(string text)
    {
        var l = UiKit.Text(UiTheme.Heading, Colors.White);
        l.Text = text;
        return l;
    }

    private void BuildBag(int columns)
    {
        var head = new HBoxContainer();
        head.AddChild(Heading(Loc.T("inv.you")));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        head.AddChild(spacer);
        _slotsLabel = UiKit.Text(UiTheme.Body, UiTheme.Muted);
        head.AddChild(_slotsLabel);
        _left.AddChild(head);

        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 4);
        chips.AddThemeConstantOverride("v_separation", 4);
        float chipWidth = (columns * _tile + (columns - 1) * 4f - 4f * 4f) / 5f;
        foreach (var cat in InventoryView.Categories)
        {
            var c = cat;
            var b = UiKit.Button("", () => { _filter = c; RefreshFilter(); }, 12);
            b.ToggleMode = true;
            b.Icon = Icons.Get(InventoryView.CategoryIcon(c), 16);
            b.ExpandIcon = false;
            b.AddThemeConstantOverride("icon_max_width", 16);
            b.CustomMinimumSize = new Vector2(chipWidth, 0);
            b.TooltipText = Loc.T(InventoryView.CategoryKey(c));
            UiFit.ShrinkButton(b, Loc.T(InventoryView.CategoryKey(c)), chipWidth, 12, 9, iconWidth: 20f);
            chips.AddChild(b);
            _chips[c] = b;
        }
        _left.AddChild(chips);

        var grid = NewGrid(columns);
        for (int i = 0; i < InventoryRules.Slots; i++) grid.AddChild(NewSlot(ContainerRef.Person, i, _tile));
        _left.AddChild(grid);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 8);
        var sort = UiKit.Button("", () => _send(new SortInventory()), 13);
        sort.Icon = Icons.Get("tool:sort", 18);
        sort.ExpandIcon = false;
        sort.TooltipText = Loc.T("inv.sort.tip");
        UiFit.ShrinkButton(sort, Loc.T("inv.sort"), columns * _tile / 2f, 13, 10, iconWidth: 22f);
        foot.AddChild(sort);
        foot.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var binLabel = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        binLabel.Text = Loc.T("inv.bin");
        binLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foot.AddChild(binLabel);
        _bin = NewSlot(ContainerRef.Void, -1, Math.Max(34, _tile * 3 / 4));
        _bin.IsBin = true;
        _bin.Tip = Loc.T("inv.bin.tip");
        foot.AddChild(_bin);
        _left.AddChild(foot);
    }

    private void BuildOther(int columns, List<(int Slot, int Item, int Count)> stacks)
    {
        var tabs = new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation", 4);
        tabs.AddThemeConstantOverride("v_separation", 4);
        float tabWidth = Math.Max(70f, (columns * _tile + (columns - 1) * 4f - (_tabs.Count - 1) * 4f) / Math.Max(1, _tabs.Count));
        foreach (var tab in _tabs)
        {
            var t = tab;
            var b = UiKit.Button("", () => { _other = t.Ref; _signature = ""; Refresh(); }, 13);
            b.ToggleMode = true;
            b.ButtonPressed = SameTab(t.Ref, _other);
            b.Icon = Icons.Get(t.Icon, 18);
            b.ExpandIcon = false;
            b.AddThemeConstantOverride("icon_max_width", 18);
            b.CustomMinimumSize = new Vector2(Math.Min(tabWidth, 150f), 0);
            b.TooltipText = t.Tip;
            UiFit.ShrinkButton(b, t.Label, Math.Min(tabWidth, 150f), 13, 9, iconWidth: 22f);
            tabs.AddChild(b);
        }
        _right.AddChild(tabs);
        switch (_other.Kind)
        {
            case ContainerKind.Hold or ContainerKind.Stock: BuildList(columns, stacks); break;
            case ContainerKind.Reactor: BuildReactor(); break;
            case ContainerKind.MachineIn or ContainerKind.MachineOut: BuildMachine(columns); break;
        }
        if (_tabs.Count == 1 && _tabs[0].Ref.Kind == ContainerKind.Hold && Net.StructureRules)
        {
            var note = new FitLabel(3, UiTheme.Small, UiTheme.Caption);
            note.AddThemeColorOverride("font_color", UiTheme.Dim);
            note.FitWidth = columns * _tile + (columns - 1) * 4f;
            note.SetFit(Loc.T("inv.stock.none"));
            _right.AddChild(note);
        }
    }

    private void BuildList(int columns, List<(int Slot, int Item, int Count)> stacks)
    {
        _right.AddChild(Heading(Loc.T(_other.Kind == ContainerKind.Hold ? "inv.hold.title" : "inv.stock.title")));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _gauge = new GaugeBar(60f, 8f);
        _gaugeText = UiKit.Text(UiTheme.Body, UiTheme.Muted);
        row.AddChild(new IconRect(_other.Kind == ContainerKind.Hold ? "status:cargo" : "tool:stock", 18, UiTheme.Muted, tooltip: false));
        row.AddChild(_gauge);
        row.AddChild(_gaugeText);
        _right.AddChild(row);
        int total = InventoryView.PaddedSlots(stacks.Count, columns, 4);
        var grid = NewGrid(columns);
        for (int i = 0; i < total; i++) grid.AddChild(NewSlot(_other, -1, _tile));
        _right.AddChild(grid);
    }

    private void BuildMachine(int columns)
    {
        var net = Net;
        var info = net.GetMachineInfo(_other.Id);
        string name = Loc.Name("machine", _content.Machines[info.Type].Id);
        _right.AddChild(Heading(Loc.T("inv.machine.title", Capitalise(name))));
        var inLabel = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        inLabel.Text = Loc.T("inv.machine.in");
        _right.AddChild(inLabel);
        var gridIn = NewGrid(columns);
        int slots = Math.Max(columns, _tile >= 0 ? columns : columns);
        for (int i = 0; i < slots; i++) gridIn.AddChild(NewSlot(ContainerRef.MachineIn(_other.Id), -1, _tile));
        _right.AddChild(gridIn);
        var outLabel = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        outLabel.Text = Loc.T("inv.machine.out");
        _right.AddChild(outLabel);
        var gridOut = NewGrid(columns);
        for (int i = 0; i < slots; i++) gridOut.AddChild(NewSlot(ContainerRef.MachineOut(_other.Id), -1, _tile));
        _right.AddChild(gridOut);
    }

    private void BuildReactor()
    {
        var o = _other;
        int index = _tabs.Where(t => t.Ref.Kind == ContainerKind.Reactor).ToList().FindIndex(t => t.Ref == o) + 1;
        _right.AddChild(Heading(_tabs.Count(t => t.Ref.Kind == ContainerKind.Reactor) > 1 ? Loc.T("inv.tab.reactorN", index) : Loc.T("inv.tab.reactor")));
        var sub = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        sub.Text = Loc.T("inv.reactor.title");
        _right.AddChild(sub);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var slot = NewSlot(o, InventoryRules.ReactorSlot, (int)(_tile * 1.4f));
        row.AddChild(slot);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 3);
        _burnLine = new FitLabel(3, UiTheme.Body, UiTheme.Small);
        _burnLine.AddThemeColorOverride("font_color", Colors.White);
        _gaugeText = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        var first = new FitLabel(3, UiTheme.Small, UiTheme.Caption);
        first.AddThemeColorOverride("font_color", UiTheme.Dim);
        first.FitWidth = Math.Max(120f, 4.6f * _tile - 20f);
        first.SetFit(Loc.T("inv.reactor.first"));
        col.AddChild(_burnLine);
        col.AddChild(_gaugeText);
        col.AddChild(first);
        row.AddChild(col);
        _right.AddChild(row);

        var feedTitle = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        feedTitle.Text = Loc.T("inv.feed.title");
        feedTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _right.AddChild(feedTitle);
        var buttons = new GridContainer { Columns = 2 };
        buttons.AddThemeConstantOverride("h_separation", 4);
        buttons.AddThemeConstantOverride("v_separation", 4);
        float bw = (6 * _tile + 5 * 4f - 4f) / 2f;
        foreach (var source in InventoryView.FeedChoices)
        {
            var s = source;
            var b = UiKit.Button("", () => _send(new SetFeedSource(o.Id, o.Mx, o.My, o.Mz, s)), 12);
            b.ToggleMode = true;
            b.CustomMinimumSize = new Vector2(bw, 0);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.TooltipText = Loc.T(InventoryView.FeedKey(s) + ".tip");
            UiFit.ShrinkButton(b, Loc.T(InventoryView.FeedKey(s)), bw, 12, 9);
            buttons.AddChild(b);
            _feedButtons.Add(b);
        }
        _right.AddChild(buttons);
        _feedTip = new FitLabel(3, UiTheme.Small, UiTheme.Caption);
        _feedTip.AddThemeColorOverride("font_color", UiTheme.Muted);
        _feedTip.FitWidth = 6 * _tile + 5 * 4f;
        _right.AddChild(_feedTip);
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ---- per refresh ----------------------------------------------------------------------------------------------

    private void UpdateContents(List<(int Slot, int Item, int Count)> stacks)
    {
        var net = Net;
        int me = Me;
        var person = _slots.Where(s => s.Container.Kind == ContainerKind.Person).ToList();
        foreach (var s in person)
        {
            var st = net.InventoryStack(me, s.Slot);
            Fill(s, st?.Item ?? -1, st?.Count ?? 0);
        }
        _slotsLabel!.Text = Loc.T("inv.slots", net.InventoryUsedSlots(me), InventoryRules.Slots);

        switch (_other.Kind)
        {
            case ContainerKind.Hold or ContainerKind.Stock:
            {
                var list = _slots.Where(s => s.Container == _other).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    if (i < stacks.Count) { list[i].Slot = stacks[i].Slot; Fill(list[i], stacks[i].Item, stacks[i].Count); }
                    else { list[i].Slot = -1; Fill(list[i], -1, 0); }
                }
                int used = _other.Kind == ContainerKind.Hold ? net.HoldTotal(me) : net.StockTotal(_other.Id);
                int cap = _other.Kind == ContainerKind.Hold ? net.HoldCapacity(me) : net.StockCapacity(_other.Id);
                float f = cap > 0 ? used / (float)cap : 0f;
                _gauge!.Set(f, f >= 1f ? UiTheme.Bad : f > 0.85f ? UiTheme.Warn : UiTheme.Good);
                _gaugeText!.Text = $"{used}/{cap}";
                break;
            }
            case ContainerKind.Reactor:
            {
                var slot = _slots.First(s => s.Container == _other);
                if (net.TryGetReactorFuel(_other.Id, _other.Mx, _other.My, _other.Mz, out var info))
                {
                    Fill(slot, info.Item, info.Count);
                    _burnLine!.SetFit(InventoryView.BurnLine(info));
                    _burnLine.AddThemeColorOverride("font_color", info.TotalTicksLeft > 0 ? Colors.White : UiTheme.Warn);
                    _gaugeText!.Text = info.Item >= 0 ? $"{info.Count}/{info.Capacity}" : Loc.T("inv.reactor.slot");
                    for (int i = 0; i < _feedButtons.Count; i++) _feedButtons[i].SetPressedNoSignal(InventoryView.FeedChoices[i] == info.Feed);
                    _feedTip!.SetFit(Loc.T(InventoryView.FeedKey(info.Feed) + ".tip"));
                    slot.Tip = info.Item >= 0 ? TipFor(info.Item, info.Count) : Loc.T("inv.reactor.slot");
                }
                break;
            }
            case ContainerKind.MachineIn or ContainerKind.MachineOut:
            {
                FillBuffer(ContainerKind.MachineIn);
                FillBuffer(ContainerKind.MachineOut);
                break;
            }
        }
        foreach (var chip in _chips) chip.Value.SetPressedNoSignal(chip.Key == _filter);
        if (_bin is not null) _bin.Tip = Loc.T("inv.bin.tip");
        RefreshFilter();
        RefreshFlags();
        UpdateHint();
    }

    private void FillBuffer(ContainerKind kind)
    {
        var c = new ContainerRef(kind, _other.Id);
        var stacks = Net.ContainerStacks(Me, c);
        var list = _slots.Where(s => s.Container == c).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (i < stacks.Count) { list[i].Slot = stacks[i].Slot; Fill(list[i], stacks[i].Item, stacks[i].Count); }
            else { list[i].Slot = -1; Fill(list[i], -1, 0); }
        }
    }

    private void Fill(InventorySlot slot, int item, int count)
    {
        if (count <= 0 || item < 0) { slot.Set(-1, 0, 0, "", slot.Container.Kind == ContainerKind.Reactor ? Loc.T("inv.reactor.slot") : "", false); return; }
        string id = ItemIdOf(item);
        slot.Set(item, count, _content.StackSizeOf(item), id, TipFor(item, count), false);
    }

    private void RefreshFilter()
    {
        foreach (var s in _slots)
        {
            bool dim = !s.Empty && !InventoryView.Matches(_filter, s.ItemId);
            if (s.Dim != dim) { s.Dim = dim; s.QueueRedraw(); }
        }
        foreach (var chip in _chips) chip.Value.SetPressedNoSignal(chip.Key == _filter);
    }

    private void RefreshFlags()
    {
        foreach (var s in _slots)
            s.SetFlags(IsLifted(s), s.DropTarget && _drag is not null, s == _selected);
    }

    private bool IsLifted(InventorySlot s) =>
        (_drag is not null && s == _drag) || (_held is { } h && h.From == s.Container && h.Slot == s.Slot);

    private void UpdateHint()
    {
        _confirm.Visible = _binPending is not null;
        if (_binPending is { } b)
        {
            _confirmText.Text = Loc.T("inv.bin.confirm", b.Count, Capitalise(Loc.Name("item", ItemIdOf(b.Item))));
            if (_confirm.GetChildCount() >= 3)
            {
                ((Button)_confirm.GetChild(1)).Text = Loc.T("inv.bin.yes");
                ((Button)_confirm.GetChild(2)).Text = Loc.T("inv.bin.no");
            }
        }
        string text = _held is { } h ? Loc.T("inv.picked", h.Count, Capitalise(Loc.Name("item", ItemIdOf(h.Item)))) : Loc.T("inv.hint") + "\n" + Loc.T("inv.hint.keys");
        _hint.FitWidth = Math.Max(200f, _window.Size.X - 30f);
        _hint.SetFit(text);
        _hint.AddThemeColorOverride("font_color", _held is not null ? UiTheme.Gold : UiTheme.Muted);
    }

    // ---- input from the slots -------------------------------------------------------------------------------------------

    internal void SlotPressed(InventorySlot slot, bool right)
    {
        _pressed = slot;
        _pressRight = right;
        _selected = slot;
        RefreshFlags();
    }

    internal void BeginDrag(InventorySlot slot, bool right)
    {
        if (slot.Empty || slot.IsBin) return;
        _drag = slot;
        _dragHalf = right;
        _held = null;
        string icon = Icons.ItemId(slot.ItemId);
        int count = right ? InventoryView.HalfOf(slot.Count) : slot.Count;
        _ghost.Show(icon, count, _tile);
        RefreshFlags();
    }

    internal void SlotReleased(InventorySlot slot, bool right, bool shift, bool doubleClick, Vector2 globalMouse)
    {
        if (_drag is not null)
        {
            EndDrag(true, globalMouse);
            return;
        }
        if (slot.Empty || slot.IsBin) return;
        if (right) { OpenMenu(slot, globalMouse); return; } // a right click without a drag: the menu (a right drag still moves half)
        if (shift || doubleClick) QuickMove(slot);
    }

    private PopupMenu? _menu;
    private InventorySlot? _menuSlot;
    private const int MenuOne = 0, MenuHalf = 1, MenuAll = 2, MenuMove = 3;

    /// <summary>The slot menu (right click, or the Menu key / Shift + F10 on a focused slot): drop one, half or all onto the ground, or move to the other container.</summary>
    internal void OpenMenu(InventorySlot slot, Vector2 globalPos)
    {
        if (slot.Empty || slot.IsBin) return;
        if (_menu is null)
        {
            _menu = new PopupMenu();
            AddChild(_menu);
            _menu.IdPressed += OnMenuChosen;
        }
        _menuSlot = slot;
        _menu.Clear();
        bool canDrop = slot.Container.Kind is ContainerKind.Person or ContainerKind.Hold;
        if (canDrop)
        {
            _menu.AddItem(Loc.T("inv.menu.one"), MenuOne);
            _menu.AddItem(Loc.T("inv.menu.half"), MenuHalf);
            _menu.SetItemDisabled(_menu.GetItemIndex(MenuHalf), slot.Count < 2);
            _menu.AddItem(Loc.T("inv.menu.all"), MenuAll);
        }
        string move = slot.Container.Kind != ContainerKind.Person ? Loc.T("inv.menu.toBag")
            : _other is { Kind: not ContainerKind.Person and not ContainerKind.Hold and not ContainerKind.Void and not ContainerKind.MachineOut } ? Loc.T("inv.menu.toOther")
            : Loc.T("inv.menu.toHold");
        _menu.AddItem(move, MenuMove);
        _menu.Popup(new Rect2I((Vector2I)globalPos, Vector2I.Zero));
    }

    private void OnMenuChosen(long id)
    {
        if (_menuSlot is not { } s || s.Empty) return;
        switch ((int)id)
        {
            case MenuOne: DropToWorldRequested?.Invoke(s.Container, s.Slot, 1); break;
            case MenuHalf: DropToWorldRequested?.Invoke(s.Container, s.Slot, InventoryView.HalfOf(s.Count)); break;
            case MenuAll: DropToWorldRequested?.Invoke(s.Container, s.Slot, s.Count); break;
            case MenuMove: QuickMove(s); break;
        }
    }

    private InventorySlot? SlotAt(Vector2 global) => _slots.FirstOrDefault(s => s.IsVisibleInTree() && s.GetGlobalRect().HasPoint(global));

    private void EndDrag(bool drop, Vector2 globalMouse)
    {
        var from = _drag;
        bool half = _dragHalf;
        _drag = null;
        _pressed = null;
        _ghost.Visible = false;
        foreach (var s in _slots) s.DropTarget = false;
        if (drop && from is not null && SlotAt(globalMouse) is { } to && to != from) Drop(from, to, half);
        else if (drop && from is not null && SlotAt(globalMouse) is null && from.Container.Kind is ContainerKind.Person or ContainerKind.Hold && !_window.GetGlobalRect().HasPoint(globalMouse))
            DropToWorldRequested?.Invoke(from.Container, from.Slot, half ? InventoryView.HalfOf(from.Count) : from.Count);
        RefreshFlags();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _drag is null) return;
        var over = SlotAt(GetGlobalMousePosition());
        foreach (var s in _slots) s.DropTarget = s == over && s != _drag && InventoryView.CanDrop(_drag.Container, s.Container);
        foreach (var s in _slots) s.QueueRedraw();
    }

    private void Drop(InventorySlot from, InventorySlot to, bool half)
    {
        if (to.IsBin)
        {
            if (from.Container.Kind != ContainerKind.Person) { _toast(Loc.T("inv.toast.noOther")); return; }
            AskBin(from, half ? InventoryView.HalfOf(from.Count) : from.Count);
            return;
        }
        if (!InventoryView.CanDrop(from.Container, to.Container)) { Deny(); return; }
        _send(InventoryView.Drop(from.Container, from.Slot, from.Count, to.Container, to.Slot, half));
        Confirm();
    }

    private void QuickMove(InventorySlot slot)
    {
        if (slot.Empty || slot.IsBin) return;
        _send(InventoryView.QuickMove(slot.Container, slot.Slot, _other));
        Confirm();
    }

    internal void SlotActivated(InventorySlot slot, bool shift, bool ctrl)
    {
        if (slot.IsBin)
        {
            if (_held is { } h && h.From.Kind == ContainerKind.Person) { _held = null; AskBin(h.From, h.Slot, h.Item, h.Count); }
            return;
        }
        if (shift) { QuickMove(slot); return; }
        if (_held is { } held)
        {
            _held = null;
            var from = _slots.FirstOrDefault(s => s.Container == held.From && s.Slot == held.Slot);
            if (from is not null && from != slot) Drop(from, slot, half: false);
            RefreshFlags();
            UpdateHint();
            return;
        }
        if (slot.Empty) return;
        _held = (slot.Container, slot.Slot, slot.Item, ctrl ? InventoryView.HalfOf(slot.Count) : slot.Count);
        RefreshFlags();
        UpdateHint();
    }

    internal void SlotBinRequested(InventorySlot slot)
    {
        if (slot.Empty || slot.Container.Kind != ContainerKind.Person) return;
        AskBin(slot, slot.Count);
    }

    private void AskBin(InventorySlot from, int count) => AskBin(from.Container, from.Slot, from.Item, count);

    private void AskBin(ContainerRef from, int slot, int item, int count)
    {
        _binPending = (from, slot, item, count);
        UpdateHint();
        ApplyLayout();
    }

    private void ConfirmBin()
    {
        if (_binPending is not { } b) return;
        _binPending = null;
        _send(new MoveItems(b.From, b.Slot, ContainerRef.Void, InventoryRules.AnySlot, b.Count));
        _toast(Loc.T("inv.toast.binned", b.Count, Capitalise(Loc.Name("item", ItemIdOf(b.Item)))));
        UpdateHint();
        ApplyLayout();
    }

    private void CancelBin()
    {
        _binPending = null;
        UpdateHint();
        ApplyLayout();
    }

    private static void Confirm() => AudioManager.PlayUi(Sfx.UiClick);

    private static void Deny() => AudioManager.PlayUi(Sfx.UiError);

    // ---- test and screenshot hooks ---------------------------------------------------------------------------------------

    /// <summary>The slot of a container whose stack holds the item (tests and screenshots).</summary>
    public InventorySlot? FindSlot(ContainerRef container, int item) => _slots.FirstOrDefault(s => s.Container == container && s.Item == item && !s.Empty);

    /// <summary>Selects a slot as if it were clicked, and marks it for the screenshot.</summary>
    public void SelectSlot(InventorySlot? slot)
    {
        _selected = slot;
        RefreshFlags();
    }

    /// <summary>Starts a pending bin question for a slot, as a drop on the bin would.</summary>
    public void AskBinFor(InventorySlot slot) => AskBin(slot, slot.Count);

    /// <summary>Switches the filter chip (tests, screenshots).</summary>
    public void SetFilter(ItemCategory filter)
    {
        _filter = filter;
        RefreshFilter();
    }

    public override void _Input(InputEvent ev)
    {
        if (!Visible || ev is not InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right } || _drag is null) return;
        // A drag that ends outside every slot (the mouse can leave the window) is dropped on nothing.
        if (SlotAt(GetGlobalMousePosition()) is null) EndDrag(true, GetGlobalMousePosition()); // outside the window it drops onto the ground
    }
}
