using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The key rebinding page (docs/prototypes/key-rebinding.md): every action of <see cref="InputActions"/> grouped by what the player is doing,
/// with its icon, name, key cap and Rebind, Clear and Reset buttons, a search box and Reset all. The rules (reserved keys, conflicts, swap,
/// optional actions) live in the engine-free <see cref="KeyBindingEditor"/>; this class draws them and runs the capture dialog.
/// It is built inside <see cref="MenuScreen"/> for the main menu, the pause menu and the Settings tab of the ship computer.
/// </summary>
public partial class KeyBindingsPage : VBoxContainer
{
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);
    private static readonly Color Gold = new(1f, 0.9f, 0.5f);
    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.97f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.9f);
    private static readonly Color CapBg = new(0.1f, 0.13f, 0.2f);
    private static readonly Color CapEdge = new(0.45f, 0.58f, 0.82f, 0.6f);

    /// <summary>True while a dialog of this page is open: the game ignores its own keys (Tab, Esc and the actions) so a key press is only a candidate for the binding.</summary>
    public static bool Capturing { get; private set; }

    private enum Mode { None, Capture, Dialog }

    private sealed record Row(Control Node, string Search, int Section);

    private KeyBindingEditor Editor => KeyBindingStore.Editor;
    private readonly List<Row> _rows = new();
    private readonly List<Control> _headers = new();
    private VBoxContainer _list = null!;
    private ScrollContainer _scroll = null!;
    private LineEdit _search = null!;
    private Label _status = null!, _noMatch = null!;
    private TextureRect _fadeTop = null!, _fadeBottom = null!;
    private Button _resetAll = null!;
    private CanvasLayer _layer = null!;
    private Control _overlay = null!;
    private Label _dialogTitle = null!, _dialogBody = null!;
    private HBoxContainer _dialogButtons = null!;
    private Mode _mode = Mode.None;
    private KeySlot _target;
    private string _focusName = "";
    private string _statusText = "";

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var intro = UiKit.Text(12, Muted);
        intro.Text = Loc.T("controls.intro");
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        intro.CustomMinimumSize = new Vector2(100, 0);
        AddChild(intro);

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        _search = new LineEdit { Name = "KeySearch", PlaceholderText = Loc.T("controls.search"), ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 32) };
        _search.TextChanged += _ => ApplyFilter();
        bar.AddChild(_search);
        _resetAll = UiKit.Button(Loc.T("controls.resetAll"), () => { Editor.ResetAll(); SetStatus(Loc.T("controls.resetAll")); }, 13);
        _resetAll.Name = "KeyResetAll";
        _resetAll.CustomMinimumSize = new Vector2(150, 32);
        _resetAll.TooltipText = Loc.T("controls.tip.resetAll");
        bar.AddChild(_resetAll);
        AddChild(bar);

        var host = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(100, 120), ClipContents = true };
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true, Name = "KeyScroll" };
        _scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 3);
        _scroll.AddChild(_list);
        host.AddChild(_scroll);
        _fadeTop = Fade(host, top: true);
        _fadeBottom = Fade(host, top: false);
        AddChild(host);

        _noMatch = UiKit.Text(13, Muted);
        _noMatch.Text = Loc.T("controls.noMatch");
        _noMatch.HorizontalAlignment = HorizontalAlignment.Center;
        _noMatch.Visible = false;
        AddChild(_noMatch);

        _status = UiKit.Text(12, new Color(0.6f, 0.9f, 0.65f));
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.Text = _statusText;
        AddChild(_status);

        BuildOverlay();
        Editor.Changed += OnEditorChanged;
        BuildRows();
    }

    public override void _ExitTree()
    {
        Editor.Changed -= OnEditorChanged;
        Capturing = false;
    }

    private void OnEditorChanged() { if (IsInsideTree()) CallDeferred(MethodName.RebuildKeepingView); }

    private TextureRect Fade(Control host, bool top)
    {
        var grad = new Gradient { Colors = new[] { new Color(PanelBg, top ? 1f : 0f), new Color(PanelBg, top ? 0f : 1f) }, Offsets = new[] { 0f, 1f } };
        var tex = new GradientTexture2D { Gradient = grad, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 4, Height = 24 };
        var rect = new TextureRect { Texture = tex, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore, Visible = false, CustomMinimumSize = new Vector2(0, 24) };
        rect.SetAnchorsAndOffsetsPreset(top ? LayoutPreset.TopWide : LayoutPreset.BottomWide);
        rect.OffsetTop = top ? 0 : -24;
        rect.OffsetBottom = top ? 24 : 0;
        host.AddChild(rect);
        return rect;
    }

    public override void _Process(double delta)
    {
        var bar = _scroll.GetVScrollBar();
        float max = (float)(bar.MaxValue - bar.Page);
        _fadeTop.Visible = _scroll.ScrollVertical > 3;
        _fadeBottom.Visible = max > 3 && _scroll.ScrollVertical < max - 3;
    }

    // ---- rows ------------------------------------------------------------------------------------------------

    private static string GroupKey(InputAction a) =>
        a.Contexts == InputContext.Docked ? "docked" : a.Group switch
        {
            HelpGroup.Ship or HelpGroup.Weapons => "ship",
            HelpGroup.Build => "build",
            HelpGroup.Computer => "computer",
            _ => "system",
        };

    private static readonly string[] GroupOrder = { "ship", "build", "computer", "docked", "system" };

    private static string CapText(string key) => key == KeyRules.Unbound ? "–" : InputActions.Cap(key) is { } c && c.StartsWith('@') ? Loc.T("key.cap." + c[1..]) : InputActions.Cap(key);

    private static string ContextText(InputAction a)
    {
        var parts = new List<string>();
        foreach (var (flag, id) in new[] { (InputContext.Flying, "flying"), (InputContext.Walking, "walking"), (InputContext.Build, "build"), (InputContext.Computer, "computer"), (InputContext.Docked, "docked"), (InputContext.Global, "global"), (InputContext.Menu, "menu") })
            if ((a.Contexts & flag) != 0) parts.Add(Loc.T("controls.ctx." + id));
        return string.Join(" · ", parts);
    }

    /// <summary>The name of one slot: "Move · forward", or just the action name when it has a single key.</summary>
    private static string SlotName(InputAction a, int slot)
    {
        string name = Loc.T(a.NameKey);
        if (a.Keys.Length <= 1) return name;
        string slotKey = $"act.{a.Id}.slot.{slot}";
        return Loc.Has(slotKey) ? $"{name} · {Loc.T(slotKey)}" : Loc.T("controls.slotOf", name, slot + 1, a.Keys.Length);
    }

    private void BuildRows()
    {
        foreach (var c in _list.GetChildren()) { _list.RemoveChild(c); c.QueueFree(); }
        _rows.Clear();
        _headers.Clear();
        for (int g = 0; g < GroupOrder.Length; g++)
        {
            var actions = InputActions.All.Where(a => GroupKey(a) == GroupOrder[g]).ToList();
            if (actions.Count == 0) continue;
            var header = UiKit.Text(15, new Color(0.55f, 0.8f, 1f));
            header.Text = Loc.T("controls.group." + GroupOrder[g]);
            header.CustomMinimumSize = new Vector2(0, 28);
            header.VerticalAlignment = VerticalAlignment.Bottom;
            _list.AddChild(header);
            _headers.Add(header);
            foreach (var a in actions)
            {
                if (KeyRules.IsRebindable(a))
                    for (int slot = 0; slot < a.Keys.Length; slot++) AddRow(a, slot, g);
                else AddFixedRow(a, g);
            }
        }
        ApplyFilter();
    }

    private HBoxContainer NewRow(InputAction a, string name, string context, int section, string caps)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        var icon = new TextureRect { Texture = Icons.Get(a.Icon, 24), CustomMinimumSize = new Vector2(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(icon);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(60, 0), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        text.AddThemeConstantOverride("separation", 0);
        var title = UiKit.Text(14);
        title.Text = name;
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        title.TooltipText = name + "\n" + Loc.T(a.HintKey);
        title.MouseFilter = MouseFilterEnum.Pass;
        var ctx = UiKit.Text(10, Muted);
        ctx.Text = context;
        ctx.ClipText = true;
        ctx.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.AddChild(title);
        text.AddChild(ctx);
        row.AddChild(text);
        _list.AddChild(row);
        _rows.Add(new Row(row, (name + " " + context + " " + caps + " " + Loc.T(a.HintKey)).ToLowerInvariant(), section));
        return row;
    }

    private static Control Cap(string text, bool custom, bool muted = false)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(64, 28), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        box.AddThemeStyleboxOverride("panel", UiKit.Box(CapBg, custom ? Gold : CapEdge, 5, 4f));
        var l = UiKit.Text(14, custom ? Gold : muted ? Muted : Colors.White);
        l.Text = text;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.ClipText = true;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        box.AddChild(l);
        return box;
    }

    private static Button Small(string text, string tip, Action pressed, int width, string name)
    {
        var b = UiKit.Button(text, pressed, 12);
        b.Name = name;
        b.CustomMinimumSize = new Vector2(width, 28);
        b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        b.TooltipText = tip;
        UiFit.ShrinkButton(b, text, width, 12, 8);
        return b;
    }

    private void AddRow(InputAction a, int slot, int section)
    {
        string key = Editor.KeyAt(a.Id, slot);
        bool custom = key != a.Keys[slot];
        string name = SlotName(a, slot);
        var row = NewRow(a, name, ContextText(a), section, CapText(key) + " " + key);
        row.AddChild(Cap(CapText(key), custom, key == KeyRules.Unbound));
        string id = a.Id;
        row.AddChild(Small(Loc.T("controls.rebind"), Loc.T("controls.tip.rebind", name), () => BeginCapture(new KeySlot(id, slot)), 92, $"rb_{id}_{slot}"));
        if (KeyRules.IsOptional(id))
        {
            var clear = Small(Loc.T("controls.clear"), Loc.T("controls.tip.clear"), () => { Editor.Clear(id, slot); SetStatus(Loc.T("controls.cleared", name)); }, 72, $"clr_{id}_{slot}");
            clear.Disabled = key == KeyRules.Unbound;
            row.AddChild(clear);
        }
        else
        {
            var spacer = new Control { CustomMinimumSize = new Vector2(72, 0), TooltipText = Loc.T("controls.tip.required") };
            row.AddChild(spacer);
        }
        var reset = Small(Loc.T("controls.reset"), Loc.T("controls.tip.reset"), () => { Editor.Reset(id); SetStatus(Loc.T("controls.reset") + ": " + Loc.T(a.NameKey)); }, 84, $"rst_{id}_{slot}");
        reset.Disabled = !Editor.IsCustom(id);
        row.AddChild(reset);
    }

    private void AddFixedRow(InputAction a, int section)
    {
        string caps = string.Join(" ", InputActions.Caps(a.Id).Select(c => c.StartsWith('@') ? Loc.T("key.cap." + c[1..]) : c));
        var row = NewRow(a, Loc.T(a.NameKey), ContextText(a), section, caps);
        var cap = Cap(caps, custom: false, muted: true);
        cap.CustomMinimumSize = new Vector2(64, 28);
        cap.TooltipText = Loc.T("controls.tip.fixed");
        row.AddChild(cap);
        var fixedLabel = UiKit.Text(12, Muted);
        fixedLabel.Text = Loc.T("controls.fixed");
        fixedLabel.CustomMinimumSize = new Vector2(92 + 72 + 84 + 16, 0);
        fixedLabel.HorizontalAlignment = HorizontalAlignment.Center;
        fixedLabel.VerticalAlignment = VerticalAlignment.Center;
        fixedLabel.MouseFilter = MouseFilterEnum.Stop;
        fixedLabel.TooltipText = Loc.T("controls.tip.fixed");
        row.AddChild(fixedLabel);
    }

    private void ApplyFilter()
    {
        string q = _search?.Text.Trim().ToLowerInvariant() ?? "";
        var shown = new HashSet<int>();
        foreach (var r in _rows)
        {
            bool on = q.Length == 0 || r.Search.Contains(q);
            r.Node.Visible = on;
            if (on) shown.Add(r.Section);
        }
        int h = 0;
        for (int g = 0; g < GroupOrder.Length; g++)
            if (InputActions.All.Any(a => GroupKey(a) == GroupOrder[g])) _headers[h++].Visible = shown.Contains(g);
        if (_noMatch is not null) _noMatch.Visible = shown.Count == 0;
        if (_resetAll is not null) _resetAll.Disabled = Editor.Overrides.Count == 0;
    }

    private void RebuildKeepingView()
    {
        if (!IsInsideTree()) return;
        int scroll = _scroll.ScrollVertical;
        string focus = !string.IsNullOrEmpty(_focusName) ? _focusName : GetViewport().GuiGetFocusOwner()?.Name.ToString() ?? "";
        BuildRows();
        _scroll.ScrollVertical = scroll;
        Callable.From(() =>
        {
            if (!IsInsideTree() || _mode != Mode.None) return;
            _scroll.ScrollVertical = scroll;
            if (focus.Length > 0 && FindChild(focus, true, false) is Button b && !b.Disabled) b.GrabFocus();
        }).CallDeferred();
    }

    private void SetStatus(string text)
    {
        _statusText = text;
        if (_status is not null) _status.Text = text;
    }

    // ---- capture and dialogs ---------------------------------------------------------------------------------------------

    private void BuildOverlay()
    {
        _layer = new CanvasLayer { Layer = 80, Visible = false };
        _overlay = new Control { MouseFilter = MouseFilterEnum.Stop, Theme = UiTheme.Create() };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.7f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.AddChild(dim);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.AddChild(centre);
        var panel = new PanelContainer { Name = "KeyDialog" };
        panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, PanelEdge, 8, 20f));
        centre.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);
        _dialogTitle = UiKit.Text(20, Gold);
        _dialogTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _dialogBody = UiKit.Text(15);
        _dialogBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dialogBody.HorizontalAlignment = HorizontalAlignment.Center;
        _dialogBody.CustomMinimumSize = new Vector2(440, 0);
        _dialogButtons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _dialogButtons.AddThemeConstantOverride("separation", 12);
        box.AddChild(_dialogTitle);
        box.AddChild(_dialogBody);
        box.AddChild(_dialogButtons);
        _layer.AddChild(_overlay);
        AddChild(_layer);
    }

    private void ShowDialog(Mode mode, string title, string body, params (string Label, Action Run)[] buttons)
    {
        _mode = mode;
        Capturing = true;
        _dialogTitle.Text = title;
        _dialogBody.Text = body;
        foreach (var c in _dialogButtons.GetChildren()) { _dialogButtons.RemoveChild(c); c.QueueFree(); }
        foreach (var (label, run) in buttons)
        {
            var b = UiKit.Button(label, run, 15, 130);
            b.CustomMinimumSize = new Vector2(130, 36);
            b.Name = "Dlg" + _dialogButtons.GetChildCount();
            UiFit.ShrinkButton(b, label, 130, 15, 9);
            _dialogButtons.AddChild(b);
        }
        _layer.Visible = true;
        if (_dialogButtons.GetChildCount() > 0) ((Button)_dialogButtons.GetChild(0)).CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CloseDialog()
    {
        _mode = Mode.None;
        Capturing = false;
        _layer.Visible = false;
        RebuildKeepingView();
    }

    /// <summary>Opens the "press the new key" dialog for one slot.</summary>
    public void BeginCapture(KeySlot slot)
    {
        _target = slot;
        _focusName = $"rb_{slot.ActionId}_{slot.Slot}";
        var a = InputActions.Get(slot.ActionId);
        ShowDialog(Mode.Capture, Loc.T("controls.capture.title"), Loc.T("controls.capture.body", SlotName(a, slot.Slot), CapText(Editor.KeyAt(slot.ActionId, slot.Slot))),
            (Loc.T("controls.cancelBtn"), CloseDialog));
    }

    public override void _Input(InputEvent e)
    {
        if (_mode == Mode.None || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (_mode == Mode.Dialog)
        {
            if (k.Keycode == Key.Escape) { GetViewport().SetInputAsHandled(); CloseDialog(); }
            return; // other keys move the focus between the dialog's buttons
        }
        GetViewport().SetInputAsHandled();
        if (k.Keycode == Key.Escape) { CloseDialog(); return; }
        Key code = k.Keycode != Key.None ? k.Keycode : k.PhysicalKeycode;
        string name = code == Key.KpEnter ? "Enter" : code.ToString();
        if ((k.CtrlPressed || k.MetaPressed) && code is not (Key.Ctrl or Key.Meta)) name = "Ctrl"; // a chord is not a key
        HandleCaptured(name);
    }

    /// <summary>What the page does with a key name pressed while the capture dialog is open (also called by the screenshot and test drivers).</summary>
    public void HandleCaptured(string key)
    {
        var slot = _target;
        var a = InputActions.Get(slot.ActionId);
        string me = SlotName(a, slot.Slot);
        var result = Editor.Preview(slot.ActionId, slot.Slot, key);
        switch (result.Kind)
        {
            case AssignKind.Ok:
                Editor.Assign(slot.ActionId, slot.Slot, key);
                SetStatus(Loc.T("controls.assigned", me, CapText(key)));
                CloseDialog();
                break;
            case AssignKind.NoChange:
                CloseDialog();
                break;
            case AssignKind.Reserved:
                Notice(Loc.T("controls.reserved.title"), Loc.T("controls.reserved.body", CapText(key)));
                break;
            case AssignKind.Conflict:
                ShowConflict(slot, key, result, me);
                break;
            default:
                Notice(Loc.T("controls.invalid.title"), Loc.T("controls.invalid.body"));
                break;
        }
    }

    private void Notice(string title, string body) =>
        ShowDialog(Mode.Dialog, title, body, (Loc.T("controls.retry"), () => BeginCapture(_target)), (Loc.T("controls.cancelBtn"), CloseDialog));

    private void ShowConflict(KeySlot slot, string key, AssignResult result, string me)
    {
        string cap = CapText(key);
        string Describe(KeySlot s) => SlotName(InputActions.Get(s.ActionId), s.Slot);
        if (result.Conflicts.Count > 1)
        {
            Notice(Loc.T("controls.conflict.title"), Loc.T("controls.conflict.several", cap, string.Join(", ", result.Conflicts.Select(Describe))));
            return;
        }
        var other = result.Conflicts[0];
        string where = ContextText(InputActions.Get(other.ActionId));
        if (!result.CanSwap)
        {
            Notice(Loc.T("controls.conflict.title"), Loc.T("controls.conflict.noSwap", cap, Describe(other), where));
            return;
        }
        string mine = CapText(Editor.KeyAt(slot.ActionId, slot.Slot));
        ShowDialog(Mode.Dialog, Loc.T("controls.conflict.title"), Loc.T("controls.conflict.swap", cap, Describe(other), where, me, mine),
            (Loc.T("controls.swapBtn"), () =>
            {
                Editor.Swap(slot.ActionId, slot.Slot, key);
                SetStatus(Loc.T("controls.assigned", me, cap));
                CloseDialog();
            }),
            (Loc.T("controls.cancelBtn"), CloseDialog));
    }
}
