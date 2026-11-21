using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The research panel (T): every research node in a list grouped by state (researching, available, locked, done) and, beside it, the
/// selected node's description, progress, cost with what the lab already has, requirements, what it unlocks and a Start button.
/// Arrows move the selection (the list buttons take the focus), Enter starts the selected research, Esc and T close it (the game
/// handles those two). The display model comes from <see cref="ResearchView"/>; this class only draws it.
/// </summary>
public partial class ResearchPanel : Control
{
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);
    private static readonly Color Good = new(0.5f, 0.9f, 0.55f);
    private static readonly Color Bad = new(1f, 0.55f, 0.45f);
    private static readonly Color Gold = new(1f, 0.88f, 0.5f);

    private readonly Func<IReadOnlyList<ResearchEntry>> _entries;

    private PanelContainer _panel = null!;
    private VBoxContainer _list = null!;
    private ScrollContainer _listScroll = null!;
    private Label _title = null!, _stateLine = null!, _desc = null!, _current = null!, _howText = null!, _keys = null!;
    private ProgressBar _bar = null!;
    private RichTextLabel _body = null!;
    private Button _start = null!;
    private readonly Dictionary<int, Button> _buttons = new();
    private string _signature = "";
    private int _selected = -1;
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
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.05f, 0.07f, 0.11f, 0.95f), new Color(0.35f, 0.45f, 0.65f, 0.85f), 8, 16f));
        centre.AddChild(_panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(root);

        var head = new HBoxContainer();
        _title = UiKit.Text(24, Colors.White);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        _current = UiKit.Text(14, Gold);
        _current.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(_current);
        var close = UiKit.Button("", () => CloseRequested?.Invoke(), 13);
        close.FocusMode = FocusModeEnum.None;
        close.Name = "CloseButton";
        head.AddChild(close);
        root.AddChild(head);

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 14);
        root.AddChild(split);

        _listScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 3);
        _listScroll.AddChild(_list);
        split.AddChild(_listScroll);

        var detail = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        detail.AddThemeConstantOverride("separation", 6);
        _stateLine = UiKit.Text(14, Muted);
        _desc = UiKit.Text(14);
        _desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _desc.CustomMinimumSize = new Vector2(540, 0);
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12), MouseFilter = MouseFilterEnum.Ignore };
        _bar.AddThemeStyleboxOverride("background", UiKit.Box(new Color(0.12f, 0.14f, 0.2f), null, 3, 0f));
        _bar.AddThemeStyleboxOverride("fill", UiKit.Box(new Color(0.35f, 0.7f, 1f), null, 3, 0f));
        _body = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(540, 0) };
        _body.AddThemeFontSizeOverride("normal_font_size", 14);
        _body.AddThemeFontSizeOverride("bold_font_size", 14);
        _start = UiKit.Button("", () => StartSelected(), 16, 300);
        _start.FocusMode = FocusModeEnum.None;
        _start.Name = "StartButton";
        _start.CustomMinimumSize = new Vector2(300, 38);
        foreach (var c in new Control[] { _stateLine, _desc, _bar, _body, _start }) detail.AddChild(c);
        split.AddChild(detail);

        _howText = UiKit.Text(12, Muted);
        _howText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _howText.CustomMinimumSize = new Vector2(940, 0);
        root.AddChild(new HSeparator());
        root.AddChild(_howText);
        _keys = UiKit.Text(12, new Color(0.5f, 0.56f, 0.68f));
        root.AddChild(_keys);
    }

    /// <summary>Shows the panel with the most useful entry selected and focused.</summary>
    public void Open()
    {
        Visible = true;
        _signature = "";
        _selected = -1;
        Refresh(Size);
        var shown = _shown;
        int pick = shown.FirstOrDefault(e => e.State == ResearchState.Researching)?.Index
            ?? shown.FirstOrDefault(e => e.State == ResearchState.Available)?.Index
            ?? shown.FirstOrDefault()?.Index ?? -1;
        Select(pick);
        if (_buttons.TryGetValue(pick, out var b)) b.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close() => Visible = false;

    /// <summary>Selects a node and shows its details (the list button of the focused node calls this too).</summary>
    public void Select(int node)
    {
        _selected = node;
        FillDetail();
    }

    /// <summary>Re-reads the world: rebuilds the list when a state changed, otherwise updates texts in place.</summary>
    public void Refresh(Vector2 viewport)
    {
        if (!Visible) return;
        _shown = _entries();
        _panel.CustomMinimumSize = new Vector2(Math.Min(viewport.X - 40f, 1100f), Math.Min(viewport.Y - 40f, 640f));
        _listScroll.CustomMinimumSize = new Vector2(Math.Clamp(viewport.X * 0.3f, 300f, 420f), Math.Min(viewport.Y - 200f, 470f));
        _title.Text = Loc.T("research.title");
        ((Button)_panel.FindChild("CloseButton", true, false)!).Text = Loc.T("research.close");
        _howText.Text = Loc.T("research.how");
        _keys.Text = Loc.T("research.keys");

        string signature = string.Join(",", _shown.Select(e => $"{e.Index}:{(int)e.State}")) + Loc.Lang;
        if (signature != _signature)
        {
            RebuildList();
            _signature = signature;
        }
        else foreach (var e in _shown) if (_buttons.TryGetValue(e.Index, out var b)) b.Text = ListText(e);
        var cur = _shown.FirstOrDefault(e => e.State == ResearchState.Researching);
        _current.Text = cur is null ? Loc.T("research.currentNone") : Loc.T("research.current", $"{ResearchView.Name(cur)} ({(int)Math.Round(cur.Progress * 100)}%)");
        FillDetail();
    }

    private static string ListText(ResearchEntry e)
    {
        string text = ResearchView.Name(e);
        if (e.State == ResearchState.Researching) text += $"   {(int)Math.Round(e.Progress * 100)}%";
        else if (e.State == ResearchState.Done) text += "   [x]";
        return text;
    }

    private void RebuildList()
    {
        int keep = _selected;
        foreach (var child in _list.GetChildren()) { _list.RemoveChild(child); child.QueueFree(); }
        _buttons.Clear();
        ResearchState? group = null;
        foreach (var e in _shown)
        {
            if (group != e.State)
            {
                group = e.State;
                var h = UiKit.Text(13, StateColor(e.State));
                h.Text = ResearchView.StateName(e.State).ToUpperInvariant() + $"  ({_shown.Count(x => x.State == e.State)})";
                _list.AddChild(h);
            }
            int index = e.Index;
            var b = UiKit.Button(ListText(e), () => { }, 14);
            b.Alignment = HorizontalAlignment.Left;
            b.ClipText = true;
            b.CustomMinimumSize = new Vector2(0, 30);
            b.AddThemeColorOverride("font_color", e.State == ResearchState.Locked ? new Color(0.55f, 0.58f, 0.66f) : e.State == ResearchState.Done ? new Color(0.6f, 0.8f, 0.65f) : Colors.White);
            b.FocusEntered += () => Select(index);
            b.MouseEntered += () => { if (_selected != index) Select(index); };
            _buttons[e.Index] = b;
            _list.AddChild(b);
        }
        if (keep >= 0 && _buttons.TryGetValue(keep, out var kept) && Visible && HasFocusInside()) kept.CallDeferred(Control.MethodName.GrabFocus);
    }

    private bool HasFocusInside() => GetViewport()?.GuiGetFocusOwner() is { } f && (f == this || IsAncestorOf(f));

    private static Color StateColor(ResearchState s) => s switch
    {
        ResearchState.Researching => Gold,
        ResearchState.Available => Good,
        ResearchState.Locked => new Color(0.6f, 0.62f, 0.7f),
        _ => new Color(0.55f, 0.75f, 0.95f),
    };

    private void FillDetail()
    {
        var e = _shown.FirstOrDefault(x => x.Index == _selected);
        if (e is null)
        {
            _stateLine.Text = "";
            _desc.Text = _shown.Count == 0 ? Loc.T("research.empty") : "";
            _body.Text = "";
            _bar.Visible = false;
            _start.Disabled = true;
            _start.Text = "";
            return;
        }
        _stateLine.Text = ResearchView.StateName(e.State) + (e.State == ResearchState.Locked ? "  -  " + ResearchView.LockedReason(e) : "");
        _stateLine.AddThemeColorOverride("font_color", StateColor(e.State));
        _desc.Text = Loc.T(e.DescriptionKey);
        _bar.Visible = e.State == ResearchState.Researching || e.Progress > 0 && e.State != ResearchState.Done;
        _bar.Value = e.Progress;

        var sb = new StringBuilder();
        if (e.State != ResearchState.Done)
        {
            if (_bar.Visible) sb.Append(Loc.T("research.progress", (int)Math.Round(e.Progress * 100))).Append("\n\n");
            sb.Append("[b]").Append(Loc.T("research.cost")).Append("[/b]\n");
            foreach (var c in e.Cost)
            {
                string line = Loc.T("research.costHold", ResearchView.CostLineText(c), c.InHold);
                sb.Append("   [color=#").Append((c.Have ? Good : Colors.White).ToHtml(false)).Append(']').Append(line).Append("[/color]\n");
            }
        }
        sb.Append("\n[b]").Append(Loc.T("research.requires")).Append("[/b]\n");
        if (e.Requires.Count == 0) sb.Append("   ").Append(Loc.T("research.requiresNone")).Append('\n');
        foreach (var r in e.Requires)
            sb.Append("   [color=#").Append((r.Done ? Good : Bad).ToHtml(false)).Append(']').Append(r.Done ? "[x] " : "[  ] ").Append(Loc.Name("research", r.Id)).Append("[/color]\n");
        sb.Append("\n[b]").Append(Loc.T("research.unlocks")).Append("[/b]\n");
        foreach (var u in e.Unlocks) sb.Append("   ").Append(ResearchView.UnlockText(u)).Append('\n');
        _body.Text = sb.ToString().TrimEnd('\n');

        _start.Text = e.State switch
        {
            ResearchState.Available => Loc.T("research.start"),
            ResearchState.Researching => Loc.T("research.startCurrent"),
            ResearchState.Locked => Loc.T("research.startLocked"),
            _ => Loc.T("research.startDone"),
        };
        _start.Disabled = !e.CanStart;
    }


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
