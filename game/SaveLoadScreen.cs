using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The Save/Load screen (ADR 0047): every file of the saves folder as a row (name, kind, game, difficulty, seed, game time, wave, system, play time,
/// date), filters (all, mine, checkpoints, this game only) and a sort, a detail pane with the screenshot and the facts, and the actions Load,
/// Overwrite, Rename, Duplicate and Delete, each destructive one behind a confirmation. In a running game it also has the "save as new" row; from the
/// main menu it only loads. The screen never writes a game itself: the host does (events), and calls <see cref="Refresh"/> afterwards.
/// A save that cannot be loaded (other content, damaged, newer) is listed with the reason and is never touched.
/// </summary>
public partial class SaveLoadScreen : Control
{
    /// <summary>What the screen needs from its host. <see cref="SuggestedName"/> is null where saving is not possible (the main menu).</summary>
    public sealed record Context(SaveFolder Folder, Func<SaveMeta, string?> ScenarioName, Func<SaveMeta, SaveCompat> Compat, string? CurrentGameId, string? SuggestedName);

    private static readonly Color PanelBg = new(0.05f, 0.07f, 0.11f, 0.96f);
    private static readonly Color PanelEdge = new(0.35f, 0.45f, 0.65f, 0.8f);
    private static readonly Color Muted = UiTheme.Muted;

    private readonly Context _ctx;
    private readonly bool _canSave;
    private IReadOnlyList<SaveEntry> _all = Array.Empty<SaveEntry>();
    private SaveFilter _filter = SaveFilter.All;
    private SaveSort _sort = SaveSort.Newest;
    private bool _thisGameOnly;
    private string? _selected, _picked; // _picked: the row the player chose (the first row is only pre-selected)
    private string _status = "";
    private Color _statusColor = UiTheme.Good;

    private Label _subtitle = null!, _statusLabel = null!;
    private VBoxContainer _rows = null!, _detail = null!;
    private ScrollContainer _listScroll = null!;
    private LineEdit _newName = null!;
    private Button _load = null!, _overwrite = null!, _rename = null!, _duplicate = null!, _delete = null!, _saveNew = null!, _back = null!;
    private Control _main = null!, _confirm = null!, _renameDialog = null!;
    private Label _confirmText = null!, _renameText = null!;
    private Button _confirmYes = null!, _confirmNo = null!, _renameOk = null!, _renameCancel = null!;
    private LineEdit _renameField = null!;
    private Action? _onConfirm;

    /// <summary>The player chose to load this slot (already confirmed where a confirmation applies).</summary>
    public event Action<string>? LoadRequested;

    /// <summary>The player asked for a save: the name (new save) and the slot to overwrite, or null for a new file.</summary>
    public event Action<string, string?>? SaveRequested;

    /// <summary>The screen wants to close (Back, Esc).</summary>
    public event Action? CloseRequested;

    public SaveLoadScreen(Context ctx)
    {
        _ctx = ctx;
        _canSave = ctx.SuggestedName is not null;
        Name = "SaveLoadScreen";
        UiFx.Present(this);
    }

    public bool InDialog => _confirm.Visible || _renameDialog.Visible;

    // ---- building ---------------------------------------------------------------------------------------------------------

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.78f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _main = new MarginContainer { Name = "Margins", MouseFilter = MouseFilterEnum.Ignore };
        _main.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_right" }) _main.AddThemeConstantOverride(side, 40);
        foreach (var side in new[] { "margin_top", "margin_bottom" }) _main.AddThemeConstantOverride(side, 22);
        AddChild(_main);
        var panel = new PanelContainer { Name = "SavePanel" };
        panel.AddThemeStyleboxOverride("panel", UiKit.Box(PanelBg, PanelEdge, 8, 14f));
        _main.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);

        var title = UiKit.Text(28, Colors.White);
        title.Name = "Title";
        title.Text = Loc.T(_canSave ? "saves.title.save" : "saves.title.load");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(title);
        _subtitle = UiKit.Text(13, Muted);
        _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(_subtitle);

        body.AddChild(BuildToolbar());
        if (_canSave) body.AddChild(BuildNewRow());

        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 12);
        _listScroll = new ScrollContainer { Name = "SaveList", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 5);
        _listScroll.AddChild(_rows);
        split.AddChild(_listScroll);
        var detailScroll = new ScrollContainer { Name = "DetailScroll", CustomMinimumSize = new Vector2(344, 0), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _detail = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 6);
        detailScroll.AddChild(_detail);
        split.AddChild(detailScroll);
        body.AddChild(split);

        _statusLabel = UiKit.Text(13, UiTheme.Good);
        _statusLabel.Name = "SaveStatus";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_statusLabel);

        body.AddChild(BuildActionBar());
        var hint = UiKit.Text(11, new Color(0.5f, 0.56f, 0.68f));
        hint.Text = Loc.T("saves.hint");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(hint);

        BuildConfirm();
        BuildRenameDialog();
        Refresh();
        CallDeferred(MethodName.FocusStart);
    }

    private Control BuildToolbar()
    {
        var bar = new HBoxContainer { Name = "Toolbar" };
        bar.AddThemeConstantOverride("separation", 6);
        var filters = new ButtonGroup();
        foreach (var (f, key, name) in new[] { (SaveFilter.All, "saves.filter.all", "FilterAll"), (SaveFilter.Mine, "saves.filter.mine", "FilterMine"), (SaveFilter.Checkpoints, "saves.filter.checkpoints", "FilterCheckpoints") })
        {
            var filter = f;
            var b = UiKit.Button(Loc.T(key), () => { _filter = filter; Refresh(); }, 14, 110);
            b.Name = name; b.ToggleMode = true; b.ButtonGroup = filters; b.ButtonPressed = filter == _filter;
            b.CustomMinimumSize = new Vector2(110, 32);
            bar.AddChild(b);
        }
        if (_ctx.CurrentGameId is not null)
        {
            var cb = new CheckBox { Name = "ThisGameOnly", Text = Loc.T("saves.thisGame"), ButtonPressed = _thisGameOnly, FocusMode = FocusModeEnum.All };
            cb.AddThemeFontSizeOverride("font_size", 13);
            cb.Toggled += on => { _thisGameOnly = on; Refresh(); };
            bar.AddChild(cb);
        }
        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var sorts = new ButtonGroup();
        foreach (var (s, key, name) in new[] { (SaveSort.Newest, "saves.sort.newest", "SortNewest"), (SaveSort.Oldest, "saves.sort.oldest", "SortOldest"), (SaveSort.Name, "saves.sort.name", "SortName") })
        {
            var sort = s;
            var b = UiKit.Button(Loc.T(key), () => { _sort = sort; Refresh(); }, 14, 96);
            b.Name = name; b.ToggleMode = true; b.ButtonGroup = sorts; b.ButtonPressed = sort == _sort;
            b.CustomMinimumSize = new Vector2(96, 32);
            bar.AddChild(b);
        }
        return bar;
    }

    private Control BuildNewRow()
    {
        var row = new HBoxContainer { Name = "NewSaveRow" };
        row.AddThemeConstantOverride("separation", 8);
        var label = UiKit.Text(14, Muted);
        label.Text = Loc.T("saves.new.label");
        row.AddChild(label);
        _newName = new LineEdit { Name = "NewSaveName", Text = _ctx.SuggestedName ?? "", PlaceholderText = Loc.T("saves.new.placeholder"), MaxLength = SaveMeta.MaxNameLength, ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 34) };
        _newName.TextSubmitted += _ => SaveAsNew();
        row.AddChild(_newName);
        _saveNew = UiKit.Button(Loc.T("saves.new.button"), SaveAsNew, 15, 190);
        _saveNew.Name = "SaveNewButton";
        _saveNew.CustomMinimumSize = new Vector2(190, 34);
        row.AddChild(_saveNew);
        return row;
    }

    private Control BuildActionBar()
    {
        var bar = new HBoxContainer { Name = "Actions", Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", 8);
        Button Make(string key, string name, Action act, int width = 150)
        {
            var b = UiKit.Button(Loc.T(key), act, 16, width);
            b.Name = name;
            b.CustomMinimumSize = new Vector2(width, 40);
            bar.AddChild(b);
            return b;
        }
        _load = Make("saves.load", "LoadButton", AskLoad);
        if (_canSave) _overwrite = Make("saves.overwrite", "OverwriteButton", AskOverwrite, 160);
        _rename = Make("saves.rename", "RenameButton", AskRename);
        _duplicate = Make("saves.duplicate", "DuplicateButton", DoDuplicate);
        _delete = Make("saves.delete", "DeleteButton", AskDelete);
        bar.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
        _back = Make("saves.close", "BackButton", () => CloseRequested?.Invoke(), 130);
        return bar;
    }

    private void BuildConfirm()
    {
        _confirm = new Control { Name = "ConfirmLayer", Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _confirm.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        _confirm.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _confirm.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer { Name = "ConfirmPanel" };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Solid, UiTheme.Gold, 8, 20f, 2));
        centre.AddChild(panel);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        v.AddThemeConstantOverride("separation", 14);
        panel.AddChild(v);
        _confirmText = UiKit.Text(16, Colors.White);
        _confirmText.Name = "ConfirmText";
        _confirmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _confirmText.CustomMinimumSize = new Vector2(540, 0);
        v.AddChild(_confirmText);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        _confirmYes = UiKit.Button(Loc.T("saves.yes"), () => { var act = _onConfirm; CloseDialogs(); act?.Invoke(); }, 17, 150);
        _confirmYes.Name = "ConfirmYes";
        _confirmYes.CustomMinimumSize = new Vector2(150, 40);
        _confirmNo = UiKit.Button(Loc.T("saves.no"), CloseDialogs, 17, 150);
        _confirmNo.Name = "ConfirmNo";
        _confirmNo.CustomMinimumSize = new Vector2(150, 40);
        buttons.AddChild(_confirmYes);
        buttons.AddChild(_confirmNo);
        v.AddChild(buttons);
        AddChild(_confirm);
    }

    private void BuildRenameDialog()
    {
        _renameDialog = new Control { Name = "RenameLayer", Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _renameDialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        _renameDialog.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _renameDialog.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var panel = new PanelContainer { Name = "RenamePanel" };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Solid, UiTheme.Accent, 8, 20f, 2));
        centre.AddChild(panel);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        v.AddThemeConstantOverride("separation", 12);
        panel.AddChild(v);
        _renameText = UiKit.Text(16, Colors.White);
        _renameText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _renameText.CustomMinimumSize = new Vector2(540, 0);
        v.AddChild(_renameText);
        _renameField = new LineEdit { Name = "RenameField", MaxLength = SaveMeta.MaxNameLength, ClearButtonEnabled = true, CustomMinimumSize = new Vector2(0, 36) };
        _renameField.TextSubmitted += _ => DoRename();
        v.AddChild(_renameField);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        _renameOk = UiKit.Button(Loc.T("saves.ok"), DoRename, 17, 150);
        _renameOk.Name = "RenameOk";
        _renameOk.CustomMinimumSize = new Vector2(150, 40);
        _renameCancel = UiKit.Button(Loc.T("saves.cancel"), CloseDialogs, 17, 150);
        _renameCancel.Name = "RenameCancel";
        _renameCancel.CustomMinimumSize = new Vector2(150, 40);
        buttons.AddChild(_renameOk);
        buttons.AddChild(_renameCancel);
        v.AddChild(buttons);
        AddChild(_renameDialog);
    }

    // ---- content ----------------------------------------------------------------------------------------------------------

    /// <summary>Re-reads the folder and rebuilds the list and the detail pane. A status line (and the entry to select) can be given.</summary>
    public void Refresh(string? status = null, string? select = null, bool error = false)
    {
        if (status is not null) { _status = status; _statusColor = error ? UiTheme.Bad : UiTheme.Good; }
        if (select is not null) _selected = select;
        _all = _ctx.Folder.List();
        _picked = null;
        var shown = SaveList.Apply(_all, _filter, _sort, _thisGameOnly ? _ctx.CurrentGameId : null);
        if (_selected is null || shown.All(e => e.Id != _selected)) _selected = shown.FirstOrDefault()?.Id;
        _subtitle.Text = Loc.T("saves.subtitle", _all.Count);

        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        var group = new ButtonGroup();
        foreach (var entry in shown) _rows.AddChild(MakeRow(entry, group));
        if (shown.Count == 0)
        {
            var empty = UiKit.Text(15, Muted);
            empty.Name = "EmptyList";
            empty.Text = Loc.T(_all.Count == 0 ? "saves.empty" : "saves.empty.filter");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.CustomMinimumSize = new Vector2(0, 80);
            empty.VerticalAlignment = VerticalAlignment.Center;
            _rows.AddChild(empty);
        }
        _statusLabel.Text = _status;
        _statusLabel.AddThemeColorOverride("font_color", _statusColor);
        BuildDetail();
        UpdateButtons();
    }

    private SaveEntry? Selected => _selected is null ? null : _all.FirstOrDefault(e => e.Id == _selected);

    private bool CanLoad(SaveEntry e) => e.Meta is { } m && _ctx.Compat(m) == SaveCompat.Ok;

    private static Color KindColor(SaveKind k) => k switch
    {
        SaveKind.Quick => UiTheme.Gold,
        SaveKind.Auto => new Color(0.55f, 0.8f, 0.95f),
        SaveKind.Victory => UiTheme.Good,
        _ => UiTheme.Accent,
    };

    private Button MakeRow(SaveEntry e, ButtonGroup group)
    {
        bool ok = CanLoad(e);
        string title = e.Meta is { } m ? SaveText.Title(m) : Loc.T("saves.row.badName", e.Id);
        string facts = e.Meta is { } mm ? SaveText.Facts(mm, _ctx.ScenarioName(mm)) : Loc.T("saves.short." + (e.Problem ?? SaveProblem.Corrupt).ToString().ToLowerInvariant());
        var b = new Button { Name = "Row_" + e.Id, ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 58), ButtonPressed = e.Id == _selected, TooltipText = title + "\n" + facts, ClipContents = true };
        b.AddThemeStyleboxOverride("normal", UiTheme.Tile());
        b.AddThemeStyleboxOverride("hover", UiTheme.Tile(hover: true));
        b.AddThemeStyleboxOverride("pressed", UiTheme.Tile(selected: true));
        b.AddThemeStyleboxOverride("hover_pressed", UiTheme.Tile(selected: true, hover: true));
        b.AddThemeStyleboxOverride("focus", UiTheme.FocusRing());
        string id = e.Id;
        b.Pressed += () => { if (_picked == id && ok && !InDialog) AskLoad(); else { _picked = id; Select(id); } }; // the first press picks the row, a second one loads it

        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 0);
        h.OffsetLeft = 10; h.OffsetRight = -10; h.OffsetTop = 4; h.OffsetBottom = -4;
        h.AddThemeConstantOverride("separation", 10);
        b.AddChild(h);

        var badge = UiKit.Text(12, e.Meta is { } km ? KindColor(km.Kind) : UiTheme.Bad);
        badge.Text = e.Meta is { } bm ? Loc.T("saves.kind." + bm.Kind.ToString().ToLowerInvariant()) : Loc.T("saves.badge.problem");
        badge.CustomMinimumSize = new Vector2(112, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        h.AddChild(badge);

        var mid = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        mid.AddThemeConstantOverride("separation", 2);
        var t = UiKit.Text(16, ok || e.Meta is null ? Colors.White : new Color(0.8f, 0.74f, 0.6f));
        t.Text = title; t.ClipText = true; t.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; t.TooltipText = title;
        t.CustomMinimumSize = new Vector2(40, 0);
        var f = UiKit.Text(12, ok ? Muted : UiTheme.Warn);
        f.Text = facts; f.ClipText = true; f.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; f.TooltipText = facts;
        f.CustomMinimumSize = new Vector2(40, 0);
        mid.AddChild(t);
        mid.AddChild(f);
        h.AddChild(mid);

        var right = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(150, 0), Alignment = BoxContainer.AlignmentMode.Center };
        right.AddThemeConstantOverride("separation", 2);
        var d = UiKit.Text(13, Colors.White);
        d.Text = SaveText.Date(e.When); d.HorizontalAlignment = HorizontalAlignment.Right;
        var p = UiKit.Text(12, Muted);
        p.Text = e.Meta is { } pm ? SaveText.Clock(pm.PlaySeconds) : Loc.T("saves.preset.unknown");
        p.HorizontalAlignment = HorizontalAlignment.Right;
        right.AddChild(d);
        right.AddChild(p);
        h.AddChild(right);
        return b;
    }

    private void Select(string id)
    {
        _selected = id;
        foreach (var child in _rows.GetChildren()) if (child is Button row) row.SetPressedNoSignal(row.Name == "Row_" + id);
        BuildDetail();
        UpdateButtons();
    }

    private void BuildDetail()
    {
        foreach (var child in _detail.GetChildren()) { _detail.RemoveChild(child); child.QueueFree(); }
        if (Selected is not { } e) return;
        _detail.AddChild(MakeThumbnail(e));
        var name = UiKit.Text(18, Colors.White);
        name.Name = "DetailName";
        name.Text = e.Meta is { } m0 ? SaveText.Title(m0) : e.Id;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.CustomMinimumSize = new Vector2(320, 0);
        _detail.AddChild(name);

        if (e.Meta is { } m)
        {
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 3);
            void Row(string key, string value)
            {
                if (value.Length == 0) return;
                var k = UiKit.Text(13, Muted); k.Text = Loc.T(key);
                var v = UiKit.Text(13, Colors.White); v.Text = value; v.AutowrapMode = TextServer.AutowrapMode.WordSmart; v.SizeFlagsHorizontal = SizeFlags.ExpandFill; v.CustomMinimumSize = new Vector2(150, 0);
                grid.AddChild(k); grid.AddChild(v);
            }
            Row("saves.d.kind", Loc.T("saves.kind." + m.Kind.ToString().ToLowerInvariant()) + (Loc.Has("saves.reason." + m.Reason) && m.Kind != SaveKind.Manual ? " (" + Loc.T("saves.reason." + m.Reason) + ")" : ""));
            Row("saves.d.scenario", SaveText.GameName(m, _ctx.ScenarioName(m)));
            Row("saves.d.preset", SaveText.PresetName(m));
            Row("saves.d.seed", m.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Row("saves.d.game", SaveText.GameClock(m.Ticks));
            Row("saves.d.play", SaveText.Clock(m.PlaySeconds));
            Row("saves.d.wave", m.Wave.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Row("saves.d.system", m.System);
            Row("saves.d.date", SaveText.Date(e.When));
            _detail.AddChild(grid);
            if (_ctx.Compat(m) != SaveCompat.Ok) _detail.AddChild(ProblemLabel(Loc.T(SaveCompatibility.Key(_ctx.Compat(m)))));
        }
        else _detail.AddChild(ProblemLabel(Loc.T(SaveCompatibility.Key(e.Problem ?? SaveProblem.Corrupt))));
        var file = UiKit.Text(11, UiTheme.Dim);
        file.Text = Loc.T("saves.d.file") + ": " + e.Id + SaveFile.Extension;
        file.ClipText = true; file.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; file.TooltipText = file.Text;
        file.CustomMinimumSize = new Vector2(40, 0);
        _detail.AddChild(file);
    }

    private static Label ProblemLabel(string text)
    {
        var l = UiKit.Text(13, UiTheme.Warn);
        l.Name = "Problem";
        l.Text = text;
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(320, 0);
        return l;
    }

    private Control MakeThumbnail(SaveEntry e)
    {
        var frame = new PanelContainer { Name = "ThumbnailFrame", CustomMinimumSize = new Vector2(320, 180) };
        frame.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.02f, 0.03f, 0.06f), UiTheme.Edge, 6, 2f));
        Texture2D? tex = null;
        if (e.Meta is not null)
        {
            try { tex = SaveStore.ThumbnailTexture(_ctx.Folder.Read(e.Id).Thumbnail); }
            catch (Exception) { tex = null; }
        }
        if (tex is not null)
            frame.AddChild(new TextureRect { Name = "Thumbnail", Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(316, 176), MouseFilter = MouseFilterEnum.Ignore });
        else
        {
            var none = UiKit.Text(13, UiTheme.Dim);
            none.Name = "NoThumbnail";
            none.Text = Loc.T("saves.thumb.none");
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;
            frame.AddChild(none);
        }
        return frame;
    }

    private void UpdateButtons()
    {
        var e = Selected;
        bool readable = e?.Meta is not null;
        _load.Disabled = e is null || !CanLoad(e);
        _rename.Disabled = !readable;
        _duplicate.Disabled = !readable;
        _delete.Disabled = e is null;
        _load.TooltipText = e is { Meta: { } m } && !CanLoad(e) ? Loc.T(SaveCompatibility.Key(_ctx.Compat(m))) : "";
        if (_canSave)
        {
            bool auto = e?.Meta is { Kind: SaveKind.Auto };
            _overwrite.Disabled = !readable || auto;
            _overwrite.TooltipText = auto ? Loc.T("saves.noOverwrite.auto") : "";
        }
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    private string NameOf(SaveEntry e) => e.Meta is { } m ? SaveText.Title(m) : e.Id + SaveFile.Extension;

    private void AskLoad()
    {
        if (Selected is not { } e || !CanLoad(e) || InDialog) return;
        string id = e.Id;
        if (_ctx.CurrentGameId is null) { LoadRequested?.Invoke(id); return; } // from the main menu nothing is lost
        Ask(Loc.T("saves.confirm.load", NameOf(e)), () => LoadRequested?.Invoke(id));
    }

    private void AskOverwrite()
    {
        if (!_canSave || Selected is not { Meta: { } m } e || m.Kind == SaveKind.Auto || InDialog) return;
        string id = e.Id, name = m.Name;
        Ask(Loc.T("saves.confirm.overwrite", NameOf(e)), () => SaveRequested?.Invoke(name, id));
    }

    private void AskDelete()
    {
        if (Selected is not { } e || InDialog) return;
        string id = e.Id;
        Ask(Loc.T(e.Meta is null ? "saves.confirm.deleteBad" : "saves.confirm.delete", NameOf(e)), () =>
        {
            try
            {
                int index = _all.ToList().FindIndex(x => x.Id == id);
                _ctx.Folder.Delete(id);
                var rest = _all.Where(x => x.Id != id).ToList();
                _selected = rest.Count == 0 ? null : rest[Math.Min(Math.Max(index, 0), rest.Count - 1)].Id;
                Refresh(Loc.T("saves.status.deleted", NameOf(e)));
            }
            catch (Exception ex) { Refresh(Loc.T("saves.status.failed", ex.Message), error: true); }
        });
    }

    private void AskRename()
    {
        if (Selected is not { Meta: { } m } e || InDialog) return;
        _renameText.Text = Loc.T("saves.rename.prompt", NameOf(e));
        _renameField.Text = m.Name.Length > 0 ? m.Name : SaveText.Title(m);
        _renameDialog.Visible = true;
        _main.ProcessMode = ProcessModeEnum.Disabled;
        _renameField.CallDeferred(Control.MethodName.GrabFocus);
        _renameField.CallDeferred(LineEdit.MethodName.SelectAll);
    }

    private void DoRename()
    {
        if (!_renameDialog.Visible || Selected is not { } e) return;
        string text = SaveNaming.CleanName(_renameField.Text);
        CloseDialogs();
        if (text.Length == 0) { Refresh(Loc.T("saves.status.nameEmpty"), error: true); return; }
        try { Refresh(Loc.T("saves.status.renamed", _ctx.Folder.Rename(e.Id, text)), e.Id); }
        catch (Exception ex) { Refresh(Loc.T("saves.status.failed", ex.Message), error: true); }
    }

    private void DoDuplicate()
    {
        if (Selected is not { Meta: not null } e || InDialog) return;
        try
        {
            string copy = _ctx.Folder.Duplicate(e.Id);
            _filter = SaveFilter.All;
            Refresh(Loc.T("saves.status.duplicated", _ctx.Folder.Find(copy)?.Meta?.Name ?? copy), copy);
        }
        catch (Exception ex) { Refresh(Loc.T("saves.status.failed", ex.Message), error: true); }
    }

    private void SaveAsNew()
    {
        if (!_canSave || InDialog) return;
        string name = SaveNaming.CleanName(_newName.Text);
        if (name.Length == 0) { Refresh(Loc.T("saves.status.nameEmpty"), error: true); return; }
        // Typing the name of a save that exists is not a way to replace it by accident: it says so and offers the Overwrite button.
        if (_all.Any(x => x.Meta is { Kind: not SaveKind.Auto } m && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var clash = _all.First(x => x.Meta is { Kind: not SaveKind.Auto } m && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            _filter = SaveFilter.All;
            Refresh(Loc.T("saves.status.exists"), clash.Id, error: true);
            return;
        }
        SaveRequested?.Invoke(name, null);
    }

    // ---- dialogs ----------------------------------------------------------------------------------------------------------

    private void Ask(string text, Action onYes)
    {
        _confirmText.Text = text;
        _onConfirm = onYes;
        _confirm.Visible = true;
        _main.ProcessMode = ProcessModeEnum.Disabled;
        _confirmNo.CallDeferred(Control.MethodName.GrabFocus); // the safe answer is the one under the focus
    }

    private void CloseDialogs()
    {
        _confirm.Visible = false;
        _renameDialog.Visible = false;
        _onConfirm = null;
        _main.ProcessMode = ProcessModeEnum.Inherit;
        CallDeferred(MethodName.FocusSelectedRow);
    }

    private void FocusSelectedRow()
    {
        if (!IsInsideTree()) return;
        if (_selected is not null && _rows.GetNodeOrNull<Button>("Row_" + _selected) is { } row) row.GrabFocus();
        else _back.GrabFocus();
    }

    private void FocusStart()
    {
        if (!IsInsideTree()) return;
        if (_canSave && _selected is null) _newName.GrabFocus();
        else FocusSelectedRow();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            if (InDialog) CloseDialogs(); else CloseRequested?.Invoke();
        }
        else if (!InDialog && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            if (k.Keycode == Key.Delete) { GetViewport().SetInputAsHandled(); AskDelete(); }
            else if (k.Keycode == Key.F2) { GetViewport().SetInputAsHandled(); AskRename(); }
        }
    }

    // ---- test hooks (the headless check and the audit use them; the names above are the stable interface) ----------------------

    /// <summary>The id of the selected entry, or null.</summary>
    public string? SelectedId => _selected;

    public void SelectForTest(string id) => Select(id);

    /// <summary>Opens the confirmation of a delete for the selected entry (the audit shows the dialog).</summary>
    public void AskDeleteForTest() => AskDelete();

    public void AskRenameForTest() => AskRename();

    public void AskLoadForTest() => AskLoad();

    public void CloseDialogsForTest() => CloseDialogs();

    public void SetFilterForTest(SaveFilter filter, SaveSort sort, bool thisGame) { _filter = filter; _sort = sort; _thisGameOnly = thisGame; Refresh(); }
}
