using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>The panels of the blueprint editor: layout, status, cost, stats and the validation list. Rebuilt from the session whenever it changes.</summary>
public partial class BlueprintEditor
{
    private const float TopBarHeight = 58f;

    /// <summary>The rectangles of the panels for a window of the given size (top bar, palette, side panel, foot bar). Also what a headless check measures.</summary>
    public static IReadOnlyDictionary<string, Rect2> Layout(Vector2 size, float topInset = 0f)
    {
        float top = TopBarHeight + topInset, foot = 62f;
        bool narrow = size.X < 1000f;
        float left = Math.Clamp(size.X * 0.24f, narrow ? 210f : 250f, 330f);
        float right = Math.Clamp(size.X * 0.26f, narrow ? 240f : 290f, 400f);
        float body = Math.Max(120f, size.Y - top - foot);
        return new Dictionary<string, Rect2>
        {
            ["TopBar"] = new Rect2(0, topInset, size.X, TopBarHeight),
            ["PalettePanel"] = new Rect2(0, top, left, body),
            ["SidePanel"] = new Rect2(size.X - right, top, right, body),
            ["FootBar"] = new Rect2(0, size.Y - foot, size.X, foot),
        };
    }

    private void Relayout(Vector2 size)
    {
        _lastSize = size;
        var l = Layout(size, TopInset);
        foreach (var (name, rect) in l)
        {
            var c = name switch { "TopBar" => _topBar, "PalettePanel" => _palettePanel, "SidePanel" => (Control)_sidePanel, _ => _footBar };
            c.Position = rect.Position;
            c.Size = rect.Size;
        }
        _title.FitWidth = Math.Clamp(size.X * 0.14f, 120f, 190f);
        _sceneDirty = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var size = Size.X > 1f ? Size : GetViewportRect().Size;
        if (size != _lastSize) Relayout(size);
        if (_refresh) RefreshPanels();
        if (_sceneDirty) RebuildScene();
        ApplyCamera();
    }

    private static void ClearChildren(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    private static Label Cell(string text, Color colour, int size = UiTheme.Small, HorizontalAlignment align = HorizontalAlignment.Left, string name = "")
    {
        var l = UiKit.Text(size, colour);
        l.Text = text;
        l.HorizontalAlignment = align;
        if (name.Length > 0) l.Name = name;
        l.ClipText = true;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return l;
    }

    /// <summary>Redraws every panel from the session.</summary>
    private void RefreshPanels()
    {
        _refresh = false;
        var s = _s;
        var cam = s.Camera;

        // tools, palette and view
        foreach (var (t, b) in _toolButtons) b.SetPressedNoSignal(s.Tool == t);
        foreach (var (k, b) in _palette) b.SetPressedNoSignal(s.Tool == BlueprintTool.Place && s.Kind == k);
        SyncViewButtons();
        _undo.Disabled = !s.Draft.CanUndo;
        _redo.Disabled = !s.Draft.CanRedo;
        _paste.Disabled = s.Draft.ClipboardCount == 0;
        _upgrade.Disabled = s.Selection.Count != 1 || ModuleKindIds.UpgradeOf(s.Draft.Modules.GetValueOrDefault(s.Selection.FirstOrDefault())) is not { } up || _content.Structures is not { } sc || !sc.IsDefined(up);
        _revert.Disabled = !s.Draft.Dirty;
        _apply.Disabled = !s.CanApply;
        _load.Disabled = false;
        _selectionLabel.SetFit(s.Carrying is not null ? Loc.T("bp.carrying") : s.Selection.Count > 0 ? Loc.T("bp.selected", s.Selection.Count) : Loc.T("bp.nothingSelected"));
        FitButtons();

        // the deck slider: from the lowest to the highest layer of the draft
        var (min, max) = s.Draft.Bounds(0);
        _settingDeck = true;
        _deck.MinValue = min.Y; _deck.MaxValue = Math.Max(max.Y, min.Y + 1);
        _deck.Editable = max.Y > min.Y;
        _deck.SetValueNoSignal(Math.Clamp(cam.SectionLayer, min.Y, Math.Max(max.Y, min.Y + 1)));
        _settingDeck = false;
        _deckLabel.SetFit(cam.View == PlanView.Section ? Loc.T("bp.section.deck", cam.SectionLayer) : Loc.T("bp.view." + cam.View.ToString().ToLowerInvariant()));

        // the status line
        var errors = s.Validation.Errors.ToList();
        if (!s.Draft.Dirty)
        {
            _status.SetFit(Loc.T("bp.status.unchanged"));
            _status.AddThemeColorOverride("font_color", Muted);
            _statusIcon.Set("tool:info", Muted);
        }
        else if (errors.Count > 0 || !s.Report.Ok)
        {
            string why = errors.Count > 0 ? errors[0].Text() : RejectTexts.Key(s.Report.Reject) is { } rk ? Loc.T(rk) : s.Report.Reject.ToString();
            _status.SetFit(why);
            _status.AddThemeColorOverride("font_color", Bad);
            _statusIcon.Set("status:cross", Bad);
        }
        else
        {
            _status.SetFit(Loc.T("bp.status.changes", s.Diff.Added.Count + s.Diff.Moved.Count, s.Diff.Removed.Count + s.Diff.Moved.Count, s.Diff.Moved.Count));
            _status.AddThemeColorOverride("font_color", Good);
            _statusIcon.Set("status:check", Good);
        }

        RefreshCost();
        RefreshStats();
        RefreshValidation();
    }

    private void FitButtons()
    {
        UiFit.ShrinkButton(_apply, Loc.T("bp.apply"), 150f, 16, 11);
        UiFit.ShrinkButton(_revert, Loc.T("bp.revert"), 120f, 14, 10);
        UiFit.ShrinkButton(_save, Loc.T("bp.save"), 120f, 13, 10);
        UiFit.ShrinkButton(_load, Loc.T("bp.load"), 120f, 13, 10);
        UiFit.ShrinkButton(_close, Loc.T("bp.close"), 90f, 14, 10);
    }

    private void RefreshCost()
    {
        var cost = _s.Cost;
        ClearChildren(_costGrid);
        _costSummary.SetFit(!cost.Pays ? Loc.T("bp.cost.free") : cost.Lines.Count == 0 ? Loc.T("bp.cost.none") : Loc.T("bp.cost.pays"));
        _costSummary.AddThemeColorOverride("font_color", Muted);
        if (cost.Lines.Count > 0)
        {
            _costGrid.AddChild(Cell("", Muted));
            _costGrid.AddChild(Cell(Loc.T("bp.cost.price"), Muted, UiTheme.Caption, HorizontalAlignment.Right));
            _costGrid.AddChild(Cell(Loc.T("bp.cost.refund"), Muted, UiTheme.Caption, HorizontalAlignment.Right));
            _costGrid.AddChild(Cell(Loc.T("bp.cost.net"), Muted, UiTheme.Caption, HorizontalAlignment.Right));
        }
        foreach (var l in cost.Lines)
        {
            var id = IconCatalog.ForItem(l.ItemId);
            bool short_ = l.Missing > 0;
            var name = new HBoxContainer { Name = "CostItem_" + l.ItemId, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            name.AddThemeConstantOverride("separation", 4);
            name.AddChild(new IconRect(id, 18, short_ ? Bad : (Color?)null, tooltip: true));
            var nl = Cell(Loc.Name("item", l.ItemId), short_ ? Bad : Colors.White);
            nl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.AddChild(nl);
            _costGrid.AddChild(name);
            _costGrid.AddChild(Cell(l.Price > 0 ? l.Price.ToString() : "-", Colors.White, UiTheme.Small, HorizontalAlignment.Right, "Price_" + l.ItemId));
            _costGrid.AddChild(Cell(l.Refund > 0 ? "+" + l.Refund : "-", l.Refund > 0 ? Good : Muted, UiTheme.Small, HorizontalAlignment.Right, "Refund_" + l.ItemId));
            _costGrid.AddChild(Cell(l.Net > 0 ? l.Net.ToString() : l.Net < 0 ? "+" + (-l.Net) : "0", short_ ? Bad : l.Net < 0 ? Good : Colors.White, UiTheme.Body, HorizontalAlignment.Right, "Net_" + l.ItemId));
        }
        var missing = cost.Missing.Select(m => new ItemStack(m.Item, m.Missing)).ToArray();
        _costMissing.SetFit(missing.Length > 0 ? Loc.T("bp.cost.missing", _stacks(missing)) : "");
        _costMissing.Visible = missing.Length > 0;
    }

    private void RefreshStats()
    {
        ClearChildren(_statsList);
        foreach (var r in _s.StatRows())
        {
            var row = new HBoxContainer { Name = "Stat_" + r.LabelKey[8..], SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new IconRect(r.Icon, 16, Muted, tooltip: false));
            var name = Cell(Loc.T(r.LabelKey), Muted);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.CustomMinimumSize = new Vector2(60, 0);
            row.AddChild(name);
            var good = r.Sign;
            bool negativePower = r.LabelKey == "bp.stat.power" && r.After < 0;
            row.AddChild(Cell(r.Text(Loc.Culture), negativePower ? Bad : Colors.White, UiTheme.Body, HorizontalAlignment.Right, "Value"));
            row.AddChild(Cell(r.DeltaText(Loc.Culture), good > 0 ? Good : good < 0 ? Bad : Muted, UiTheme.Small, HorizontalAlignment.Right, "Delta"));
            ((Label)row.GetChild(2)).CustomMinimumSize = new Vector2(48, 0);
            ((Label)row.GetChild(3)).CustomMinimumSize = new Vector2(46, 0);
            _statsList.AddChild(row);
        }
    }

    private void RefreshValidation()
    {
        ClearChildren(_valList);
        var v = _s.Validation;
        int errors = v.Errors.Count(), warnings = v.Warnings.Count();
        _valSummary.SetFit(Loc.T("bp.val.errors", errors) + "  -  " + Loc.T("bp.val.warnings", warnings));
        _valSummary.AddThemeColorOverride("font_color", errors > 0 ? Bad : warnings > 0 ? Warn : Good);
        int i = 0;
        foreach (var e in v.Entries)
        {
            var colour = e.Severity switch { ValidationSeverity.Error => Bad, ValidationSeverity.Warning => Warn, _ => Muted };
            var entry = e;
            var b = new Button
            {
                Name = "Validation_" + i,
                Text = e.Text(),
                Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                ExpandIcon = false,
                FocusMode = FocusModeEnum.All,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 28),
                TooltipText = e.Frame is not null ? e.Text() : "",
            };
            UiTheme.StyleButton(b);
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", colour);
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            b.Icon = Icons.Get(e.Icon, 18);
            b.AddThemeConstantOverride("icon_max_width", 18);
            b.AddThemeConstantOverride("h_separation", 6);
            b.AddThemeColorOverride("icon_normal_color", colour);
            b.Pressed += () => { _s.Frame(entry); _sceneDirty = true; };
            _valList.AddChild(b);
            i++;
        }
    }

    // ---- a state dump for the agent harness and the headless check ---------------------------------------------------

    /// <summary>JSON-ish one-line summary of what the editor shows (tool, selection, ghosts, cost, validation, camera, panels).</summary>
    public string Describe()
    {
        var s = _s;
        var ent = s.Validation.Entries.Select(e => $"{e.Severity}:{e.Code}").ToArray();
        var cost = s.Cost.Lines.Select(l => $"{l.ItemId}={l.Net}{(l.Missing > 0 ? $"!{l.Missing}" : "")}");
        return $"open={Visible} tool={s.Tool} kind={s.Kind} sel={s.Selection.Count} modules={s.Draft.Count} dirty={s.Draft.Dirty} canApply={s.CanApply} " +
               $"added={s.Diff.Added.Count} removed={s.Diff.Removed.Count} moved={s.Diff.Moved.Count} invalid={s.Diff.Invalid.Count} " +
               $"cost=[{string.Join(",", cost)}] pays={s.Cost.Pays} power={s.Stats.PowerBalance} mass={s.Stats.Mass:F1} turn={s.Stats.Turn.RateDegPerSecond:F0} " +
               $"validation=[{string.Join(",", ent)}] view={s.Camera.View} dist={s.Camera.Distance:F0} undo={s.Draft.UndoDepth} redo={s.Draft.RedoDepth} " +
               $"ghostNodes={_ghosts.GetChildCount()} realNodes={_real.GetChildCount()} valButtons={_valList.GetChildCount()} costRows={_costGrid.GetChildCount()} statRows={_statsList.GetChildCount()}";
    }

    /// <summary>Checks that the panels are where <see cref="Layout"/> says, fit in the window, do not overlap and that the text inside them fits; returns the problems (empty when fine).</summary>
    public List<string> LayoutProblems(Vector2 size)
    {
        var problems = new List<string>();
        Relayout(size);
        RefreshPanels();
        var rects = new List<(string, Rect2)>();
        foreach (var n in new[] { "TopBar", "PalettePanel", "SidePanel", "FootBar" })
        {
            var c = GetNode<Control>(n);
            var r = new Rect2(c.Position, c.Size);
            rects.Add((n, r));
            if (r.Position.X < -0.5f || r.Position.Y < -0.5f || r.End.X > size.X + 0.5f || r.End.Y > size.Y + 0.5f) problems.Add($"{n} leaves the window: {r} in {size}");
            if (r.Size.X < 120f || r.Size.Y < 40f) problems.Add($"{n} is too small: {r.Size}");
        }
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                if (rects[i].Item2.Intersects(rects[j].Item2, false)) problems.Add($"{rects[i].Item1} overlaps {rects[j].Item1}");
        foreach (var n in new[] { "Title", "Status", "Legend", "CostSummary", "CostMissing", "SelectionLabel", "DeckLabel", "ValidationSummary" })
            if (FindChild(n, true, false) is FitLabel f && f.Truncated) problems.Add($"{n} is truncated: '{f.Text}'");
        foreach (var n in new[] { "ApplyButton", "RevertButton", "SaveButton", "LoadButton", "CloseButton" })
            if (FindChild(n, true, false) is Button b && b.Visible && b.Text.Length > 0 && b.TooltipText == b.Text) { /* shrunk until clipped: the full text became the tooltip */ problems.Add($"{n} text is clipped: '{b.Text}'"); }
        return problems;
    }
}
