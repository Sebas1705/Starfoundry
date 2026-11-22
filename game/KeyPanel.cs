using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The contextual key panel at the bottom right of the HUD: groups of rows, each row drawn as key caps (rounded boxes) and a label. The rows
/// come from the engine-free <see cref="KeyHints"/>; this class only draws them. A row is clickable while the mouse is free (clicking presses
/// its key), lit while its tool or menu is active, dimmed with the reason as tooltip when it is unavailable and red when it is an alert.
/// The panel rebuilds its nodes only when the rows change, so calling <see cref="Set"/> every frame is cheap.
/// </summary>
public partial class KeyPanel : PanelContainer
{
    private static readonly Color PanelBg = new(0.04f, 0.06f, 0.10f, 0.78f);
    private static readonly Color PanelEdge = new(0.45f, 0.58f, 0.82f, 0.38f);
    private static readonly Color AlertEdge = new(0.9f, 0.35f, 0.3f, 0.85f);
    private static readonly Color Muted = new(0.62f, 0.7f, 0.84f);
    private static readonly Color Gold = new(1f, 0.9f, 0.5f);
    private static readonly Color Dim = new(0.5f, 0.53f, 0.6f);
    private static readonly Color Red = new(1f, 0.5f, 0.45f);

    private readonly VBoxContainer _root = new();
    private readonly List<Control> _capCells = new();
    private string _signature = "", _shown = "";
    private static readonly bool Snap = Array.Exists(OS.GetCmdlineUserArgs(), a => a.Contains("-shot")); // headless screenshots run frames too fast for a fade

    /// <summary>A row was clicked: the engine key it stands for (a Godot <c>Key</c> name such as "B" or "Escape").</summary>
    public event Action<string>? KeyPressed;

    public KeyPanel()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _root.AddThemeConstantOverride("separation", 5);
        AddChild(_root);
    }

    /// <summary>Forgets the drawn rows so the next <see cref="Set"/> rebuilds them (the tooltips changed).</summary>
    public void Invalidate() => _signature = "";

    /// <summary>
    /// Shows the groups. <paramref name="sizeClass"/> is 0 for compact (small windows), 1 for normal and 2 for large; <paramref name="extraTip"/>
    /// may give a row a tooltip of its own (the quick keys list their costs).
    /// </summary>
    public void Set(IReadOnlyList<KeyGroup> groups, int sizeClass, Func<KeyRow, string?> extraTip)
    {
        string signature = sizeClass + Loc.Lang.ToString() + string.Join("|", groups.Select(g =>
            g.TitleKey + string.Join(",", g.TitleArgs) + ":" + string.Join(";", g.Rows.Select(r =>
                $"{r.Id}/{r.State}/{r.LabelKey}/{(r.LabelArgs is null ? "" : string.Join(",", r.LabelArgs))}/{string.Join("+", r.Caps)}"))));
        if (signature == _signature) return;
        _signature = signature;

        foreach (var child in _root.GetChildren()) { _root.RemoveChild(child); child.QueueFree(); }
        _capCells.Clear();
        int labelSize = sizeClass switch { 0 => 12, 1 => 13, _ => 15 };
        int capSize = sizeClass switch { 0 => 11, 1 => 12, _ => 13 };
        int rowGap = sizeClass == 0 ? 2 : 3;
        _root.AddThemeConstantOverride("separation", sizeClass == 0 ? 3 : 6);
        bool alert = groups.Any(g => g.TitleKey == "key.ctx.alert");
        var card = UiKit.Box(PanelBg, alert ? AlertEdge : PanelEdge, 8, sizeClass == 0 ? 7f : 10f);
        card.ContentMarginLeft = card.ContentMarginRight = sizeClass == 0 ? 10f : 13f;
        card.ShadowColor = new Color(0f, 0f, 0f, 0.35f);
        card.ShadowSize = 8;
        AddThemeStyleboxOverride("panel", card);
        string shown = string.Join("|", groups.Select(g => g.TitleKey + ":" + string.Join(",", g.Rows.Select(r => r.Id))));
        if (IsInsideTree() && !Snap && shown != _shown && _shown.Length > 0) // another context eases in instead of popping (a tooltip refresh does not)
        {
            Modulate = new Color(1f, 1f, 1f, 0.35f);
            CreateTween().TweenProperty(this, "modulate:a", 1f, 0.18);
        }
        _shown = shown;

        foreach (var group in groups)
        {
            bool alertGroup = group.TitleKey == "key.ctx.alert";
            var header = UiKit.Text(sizeClass == 0 ? 11 : 12, alertGroup ? Red : Muted);
            header.Text = Loc.T(group.TitleKey, group.TitleArgs).ToUpperInvariant();
            header.ClipText = true;
            _root.AddChild(header);

            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", rowGap);
            _root.AddChild(grid);
            foreach (var row in group.Rows) AddRow(grid, row, labelSize, capSize, extraTip(row));
        }

        // The caps column takes the width of the widest cap group so every label lines up.
        float capWidth = _capCells.Count == 0 ? 0f : _capCells.Max(c => c.GetCombinedMinimumSize().X);
        foreach (var cell in _capCells) cell.CustomMinimumSize = new Vector2(capWidth, 0f);
        ResetSize();
    }

    private void AddRow(GridContainer grid, KeyRow row, int labelSize, int capSize, string? extraTip)
    {
        bool info = row.State == KeyRowState.Info;
        var caps = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        caps.AddThemeConstantOverride("separation", 3);
        foreach (var cap in row.Caps) caps.AddChild(Cap(CapText(cap), row.State, capSize));
        var label = UiKit.Text(labelSize, LabelColour(row.State));
        label.Text = Loc.T(row.LabelKey, row.LabelArgs ?? Array.Empty<object>());
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        grid.AddChild(caps);
        grid.AddChild(label);
        _capCells.Add(caps);

        string tip = TipFor(row, extraTip);
        var cells = new Control[] { caps, label };
        foreach (var cell in cells)
        {
            if (row.State == KeyRowState.Disabled) cell.Modulate = new Color(1f, 1f, 1f, 0.55f);
            if (tip.Length > 0 || row.Clickable) cell.MouseFilter = MouseFilterEnum.Stop;
            if (tip.Length > 0) cell.TooltipText = tip;
        }
        if (!row.Clickable || row.ActionKey is not { } action) return;
        foreach (var cell in cells)
        {
            cell.MouseDefaultCursorShape = CursorShape.PointingHand;
            cell.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
                cell.AcceptEvent();
                AudioManager.PlayUi(Sfx.UiClick);
                KeyPressed?.Invoke(action);
            };
            cell.MouseEntered += () => { foreach (var c in cells) c.Modulate = new Color(1.3f, 1.3f, 1.3f); };
            cell.MouseExited += () => { foreach (var c in cells) c.Modulate = Colors.White; };
        }
    }

    private static string CapText(string cap) => cap.StartsWith('@') ? Loc.T("key.cap." + cap[1..]) : cap;

    private static string TipFor(KeyRow row, string? extraTip)
    {
        var sb = new StringBuilder();
        if (row.State == KeyRowState.Disabled && row.ReasonKey is { } reason) sb.Append(Loc.T(reason, row.ReasonArgs ?? Array.Empty<object>()));
        else if (row.State == KeyRowState.Alert && row.ReasonKey is { } alertReason) sb.Append(Loc.T(alertReason, row.ReasonArgs ?? Array.Empty<object>()));
        if (extraTip is { Length: > 0 }) sb.Append(sb.Length > 0 ? "\n" : "").Append(extraTip);
        if (row.Clickable && sb.Length == 0) sb.Append(Loc.T("key.tip.click"));
        return sb.ToString();
    }

    private static Color LabelColour(KeyRowState state) => state switch
    {
        KeyRowState.Active => Gold,
        KeyRowState.Disabled => Dim,
        KeyRowState.Alert => Red,
        KeyRowState.Info => Muted,
        _ => Colors.White,
    };

    /// <summary>A key cap: a small rounded box around the key's name.</summary>
    private static Control Cap(string text, KeyRowState state, int size)
    {
        var (bg, edge, fg) = state switch
        {
            KeyRowState.Active => (new Color(0.5f, 0.38f, 0.1f, 0.95f), new Color(1f, 0.85f, 0.4f), Gold),
            KeyRowState.Alert => (new Color(0.45f, 0.12f, 0.1f, 0.95f), new Color(1f, 0.45f, 0.4f), new Color(1f, 0.85f, 0.8f)),
            KeyRowState.Disabled => (new Color(0.1f, 0.12f, 0.17f, 0.9f), new Color(0.3f, 0.34f, 0.44f), Dim),
            KeyRowState.Info => (new Color(0.1f, 0.13f, 0.2f, 0.9f), new Color(0.32f, 0.4f, 0.55f), new Color(0.8f, 0.86f, 0.96f)),
            _ => (new Color(0.16f, 0.22f, 0.34f, 0.97f), new Color(0.55f, 0.68f, 0.92f), Colors.White),
        };
        var box = UiKit.Box(bg, edge, 4, 0f);
        box.ContentMarginLeft = box.ContentMarginRight = 6f;
        box.ContentMarginTop = box.ContentMarginBottom = 1f;
        box.BorderWidthBottom = 2; // a thicker lower edge reads as the side of a key
        var cap = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", box);
        var label = UiKit.Text(size, fg);
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.CustomMinimumSize = new Vector2(text.Length <= 1 ? 12f : 0f, 0f);
        cap.AddChild(label);
        return cap;
    }
}
