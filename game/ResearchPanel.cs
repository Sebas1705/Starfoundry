using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The research panel (T): the research tree (a band per science field, round nodes with icons, progress rings and lines to what they require) and,
/// under it, the selected node's name, state, description, progress, cost as icons with counts, requirements, what it unlocks and a Start button.
/// Arrows move the selection between nodes (they take the focus), hovering a node shows its details, Enter starts the selected research, Esc and T
/// close the panel (the game handles those two). The display model comes from <see cref="ResearchView"/>; this class only draws it.
/// </summary>
public partial class ResearchPanel : Control
{
    private static readonly Color Muted = UiTheme.Muted;
    private static readonly Color Good = UiTheme.Good;
    private static readonly Color Bad = UiTheme.Bad;
    private static readonly Color Gold = UiTheme.Gold;

    private readonly Func<IReadOnlyList<ResearchEntry>> _entries;

    private PanelContainer _panel = null!;
    private ScrollContainer _treeScroll = null!;
    private ResearchTreeView _tree = null!;
    private Label _title = null!, _desc = null!, _howText = null!, _name = null!;
    private IconLabel _current = null!, _stateLine = null!;
    private IconRect _bigIcon = null!;
    private HBoxContainer _progressRow = null!;
    private GaugeBar _bar = null!;
    private Label _percent = null!;
    private Label _costHead = null!, _reqHead = null!, _unlockHead = null!;
    private HFlowContainer _costBox = null!, _reqBox = null!;
    private VBoxContainer _unlockBox = null!;
    private Button _start = null!;
    private int _selected = -1;
    private string _detailSignature = "";
    private IReadOnlyList<ResearchEntry> _shown = Array.Empty<ResearchEntry>();

    /// <summary>The player asked to research this node (Start button or Enter).</summary>
    public event Action<int>? StartRequested;

    public event Action? CloseRequested;

    public int Selected => _selected;

    public ResearchPanel(Func<IReadOnlyList<ResearchEntry>> entries)
    {
        _entries = entries;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        // Docked at the right: the root only hosts the panel and lets every click outside it through to the game.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Dock());
        AddChild(_panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(root);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        head.AddChild(new IconRect("tool:research", 26, UiTheme.Gold, tooltip: false));
        _title = UiKit.Text(UiTheme.Title, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        var close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        close.Icon = Icons.Get("tool:close", 16);
        close.ExpandIcon = false;
        close.AddThemeColorOverride("icon_normal_color", UiTheme.Muted);
        close.FocusMode = FocusModeEnum.None;
        close.Name = "CloseButton";
        head.AddChild(close);
        root.AddChild(head);
        root.AddChild(UiTheme.Rule());
        _current = new IconLabel(UiTheme.Small + 1, 16, Gold) { ClipText = true };
        _current.SetIcon("status:research", Gold);
        root.AddChild(_current);

        // The tree on top, the selected node's details below it (a docked panel is tall and narrow).
        _treeScroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        _tree = new ResearchTreeView();
        _tree.NodeFocused += Select;
        _tree.NodeClicked += i => { Select(i); };
        _treeScroll.AddChild(_tree);
        root.AddChild(_treeScroll);
        root.AddChild(UiTheme.Rule(UiTheme.Edge, 0.5f));

        var detailScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        var detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", 5);
        detailScroll.AddChild(detail);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        _bigIcon = new IconRect("generic", 40, null, tooltip: false) { SizeFlagsVertical = SizeFlags.ShrinkBegin };
        top.AddChild(_bigIcon);
        var titleCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titleCol.AddThemeConstantOverride("separation", 1);
        _name = UiKit.Text(UiTheme.Heading + 2, Colors.White);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _stateLine = new IconLabel(UiTheme.Body, 16, Muted);
        _stateLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titleCol.AddChild(_name);
        titleCol.AddChild(_stateLine);
        top.AddChild(titleCol);
        detail.AddChild(top);

        _desc = UiKit.Text(UiTheme.Body);
        _desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detail.AddChild(_desc);

        _progressRow = new HBoxContainer();
        _progressRow.AddThemeConstantOverride("separation", 8);
        _bar = new GaugeBar(120f, 10f);
        _percent = UiKit.Text(UiTheme.Body, Muted);
        _progressRow.AddChild(_bar);
        _progressRow.AddChild(_percent);
        detail.AddChild(_progressRow);

        _costHead = Head(detail);
        _costBox = Flow(detail);
        _reqHead = Head(detail);
        _reqBox = Flow(detail);
        _unlockHead = Head(detail);
        _unlockBox = new VBoxContainer();
        _unlockBox.AddThemeConstantOverride("separation", 2);
        detail.AddChild(_unlockBox);
        _howText = UiKit.Text(UiTheme.Small, Muted);
        _howText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detail.AddChild(_howText);
        root.AddChild(detailScroll);

        _start = UiKit.Button("", () => StartSelected(), 16);
        _start.FocusMode = FocusModeEnum.None;
        _start.Name = "StartButton";
        _start.CustomMinimumSize = new Vector2(0, 38);
        root.AddChild(_start);
        ApplyDock();
    }

    private static Label Head(VBoxContainer parent)
    {
        var l = UiKit.Text(UiTheme.Small, UiTheme.Muted);
        parent.AddChild(l);
        return l;
    }

    private static HFlowContainer Flow(VBoxContainer parent)
    {
        var f = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
        f.AddThemeConstantOverride("h_separation", 14);
        f.AddThemeConstantOverride("v_separation", 3);
        parent.AddChild(f);
        return f;
    }

    /// <summary>The rectangle the panel fills: a right-hand column set by the game from the viewport and the key panel below it.</summary>
    public Rect2 Dock
    {
        get => _dock;
        set
        {
            if (_dock == value) return;
            _dock = value;
            ApplyDock();
        }
    }

    private Rect2 _dock = new(0, 0, 480, 600);

    private void ApplyDock()
    {
        if (_panel is null) return;
        _panel.CustomMinimumSize = _dock.Size;
        _panel.Position = _dock.Position;
        _panel.Size = _dock.Size;
        _treeScroll.CustomMinimumSize = new Vector2(0, Math.Clamp(_dock.Size.Y * 0.46f, 130f, 420f));
    }

    /// <summary>Shows the panel with the most useful node selected and focused.</summary>
    public void Open()
    {
        Visible = true;
        _detailSignature = "";
        _selected = -1;
        Refresh(Size);
        var shown = _shown;
        int pick = shown.FirstOrDefault(e => e.State == ResearchState.Researching)?.Index
            ?? shown.FirstOrDefault(e => e.State == ResearchState.Available)?.Index
            ?? shown.FirstOrDefault()?.Index ?? -1;
        Select(pick);
        if (_tree.Node(pick) is { } b) b.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;

    /// <summary>Selects a node and shows its details (a focused or hovered node calls this too).</summary>
    public void Select(int node)
    {
        _selected = node;
        _tree.SetSelected(node);
        FillDetail();
    }

    /// <summary>Re-reads the world: refreshes the tree (rebuilding it only when the node set changed) and the details.</summary>
    public void Refresh(Vector2 viewport)
    {
        if (!Visible) return;
        _shown = _entries();
        _title.Text = Loc.T("research.title");
        ((Button)_panel.FindChild("CloseButton", true, false)!).TooltipText = Loc.T("research.close");
        _howText.Text = Loc.T("research.how");
        _tree.Set(_shown);
        var cur = _shown.FirstOrDefault(e => e.State == ResearchState.Researching);
        _current.Text = cur is null ? Loc.T("research.currentNone") : Loc.T("research.current", $"{ResearchView.Name(cur)} ({(int)Math.Round(cur.Progress * 100)}%)");
        FillDetail();
    }

    private static Color StateColor(ResearchState s) => s switch
    {
        ResearchState.Researching => Gold,
        ResearchState.Available => Good,
        ResearchState.Locked => new Color(0.7f, 0.72f, 0.8f),
        _ => new Color(0.55f, 0.75f, 0.95f),
    };

    private static string StateIcon(ResearchState s) => s switch
    {
        ResearchState.Researching => "status:research",
        ResearchState.Available => "tool:start",
        ResearchState.Locked => "status:locked",
        _ => "status:check",
    };

    private void FillDetail()
    {
        var e = _shown.FirstOrDefault(x => x.Index == _selected);
        string sig = e is null ? "none" + Loc.Lang + _shown.Count : $"{e.Index}|{e.State}|{(int)(e.Progress * 1000)}|{e.Level}|{Loc.Lang}|" + string.Join(",", e.Cost.Select(c => c.Delivered + "/" + c.InHold)) + string.Join(",", e.Requires.Select(r => r.Done));
        if (sig == _detailSignature) return;
        _detailSignature = sig;
        foreach (var box in new Container[] { _costBox, _reqBox, _unlockBox }) foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
        if (e is null)
        {
            _name.Text = "";
            _stateLine.Text = "";
            _stateLine.SetIcon("");
            _desc.Text = _shown.Count == 0 ? Loc.T("research.empty") : "";
            _progressRow.Visible = false;
            _costHead.Text = _reqHead.Text = _unlockHead.Text = "";
            _bigIcon.Visible = false;
            _start.Disabled = true;
            _start.Text = "";
            return;
        }
        var field = Icons.FieldTint(e.Field);
        _bigIcon.Visible = true;
        _bigIcon.Set(IconCatalog.ForResearch(e.Id), e.State == ResearchState.Locked ? new Color(0.5f, 0.55f, 0.66f) : field);
        _name.Text = ResearchView.Name(e);
        var sc = StateColor(e.State);
        _stateLine.Text = ResearchView.StateName(e.State) + (e.State == ResearchState.Locked ? "  -  " + ResearchView.LockedReason(e) : "");
        _stateLine.AddThemeColorOverride("font_color", sc);
        _stateLine.SetIcon(StateIcon(e.State), sc);
        _desc.Text = Loc.T(e.DescriptionKey);

        bool showProgress = e.State == ResearchState.Researching || (e.Progress > 0 && e.State != ResearchState.Done);
        _progressRow.Visible = showProgress;
        _bar.Set((float)e.Progress, e.State == ResearchState.Researching ? Gold : field);
        _percent.Text = $"{(int)Math.Round(e.Progress * 100)}%";

        // Cost: an icon and delivered/needed per item; red when the hold cannot cover what is still missing.
        _costHead.Text = e.State == ResearchState.Done ? "" : Loc.T("research.cost");
        if (e.State != ResearchState.Done)
            foreach (var c in e.Cost)
            {
                var ic = new IconCount(IconCatalog.ForItem(c.ItemId), 20, UiTheme.Body);
                bool missing = !c.Have && c.InHold < c.Needed - c.Delivered;
                ic.SetText(IconCatalog.ForItem(c.ItemId), $"{c.Delivered}/{c.Needed}", c.Have ? Good : missing ? Bad : UiTheme.Text,
                    Icons.Name(IconCatalog.ForItem(c.ItemId)) + ": " + Loc.T("research.costHold", ResearchView.CostLineText(c), c.InHold));
                _costBox.AddChild(ic);
            }

        // Requirements as chips: the node's icon, its name and a tick or a cross.
        _reqHead.Text = Loc.T("research.requires");
        if (e.Requires.Count == 0) _reqBox.AddChild(Chip("status:check", Loc.T("research.requiresNone"), Muted, Muted));
        foreach (var r in e.Requires)
        {
            var other = _shown.FirstOrDefault(x => x.Index == r.Index);
            var tint = r.Done ? Good : Bad;
            var chip = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass, TooltipText = Loc.Name("research", r.Id) };
            chip.AddThemeConstantOverride("separation", 4);
            chip.AddChild(new IconRect(IconCatalog.ForResearch(r.Id), 18, other is null ? tint : Icons.FieldTint(other.Field), tooltip: false));
            var l = UiKit.Text(UiTheme.Body, tint);
            l.Text = Loc.Name("research", r.Id);
            chip.AddChild(l);
            chip.AddChild(new IconRect(r.Done ? "status:check" : "status:cross", 14, tint, tooltip: false));
            _reqBox.AddChild(chip);
        }

        _unlockHead.Text = e.Unlocks.Count > 0 ? Loc.T("research.unlocks") : "";
        foreach (var u in e.Unlocks)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new IconRect(UnlockIcon(u), 18, UnlockTint(u), tooltip: false));
            var l = UiKit.Text(UiTheme.Body);
            l.Text = ResearchView.UnlockText(u);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(l);
            _unlockBox.AddChild(row);
        }

        _start.Text = e.State switch
        {
            ResearchState.Available => Loc.T("research.start"),
            ResearchState.Researching => Loc.T("research.startCurrent"),
            ResearchState.Locked => Loc.T("research.startLocked"),
            _ => Loc.T("research.startDone"),
        };
        _start.Disabled = !e.CanStart;
    }

    private static Control Chip(string icon, string text, Color iconTint, Color textColour)
    {
        var chip = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeConstantOverride("separation", 4);
        chip.AddChild(new IconRect(icon, 16, iconTint, tooltip: false));
        var l = UiKit.Text(UiTheme.Body, textColour);
        l.Text = text;
        chip.AddChild(l);
        return chip;
    }

    private string UnlockIcon(ResearchUnlock u) => u.Kind switch
    {
        ResearchUnlockKind.Machine => IconCatalog.ForMachine(u.Id),
        ResearchUnlockKind.Recipe => "tool:recipe",
        ResearchUnlockKind.Belt => "belt:" + u.Id,
        ResearchUnlockKind.Ability => u.Id switch
        {
            "jump" => "tool:jump",
            "shield" => "module:shield",
            "sensor" => "module:sensor",
            "reactor_mk2" => "module:reactor_mk2",
            _ => "module:drone_bay",
        },
        _ => "tool:boost",
    };

    private static Color? UnlockTint(ResearchUnlock u) => u.Kind == ResearchUnlockKind.Bonus ? UiTheme.Gold : null;

    private void StartSelected()
    {
        var e = _shown.FirstOrDefault(x => x.Index == _selected);
        if (e is { CanStart: true }) StartRequested?.Invoke(e.Index);
    }

    public override void _Input(InputEvent ev)
    {
        if (!Visible || ev is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter }) return;
        GetViewport().SetInputAsHandled();
        StartSelected();
    }
}
