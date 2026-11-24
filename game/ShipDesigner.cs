using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The ship designer (ADR 0032), opened with F2 while the ship is docked at a station that has a shipyard. It edits a staged copy of the ship's module
/// layout on its own grid (<see cref="ShipDesignEditor"/>): a layer of the grid with a button per cell, the module list, a spinning 3D preview built from the
/// real module models, and the verdict and price the simulation gives the staged layout. Applying sends one <see cref="DesignerApply"/>.
/// </summary>
public partial class ShipDesigner : Control
{
    private static readonly Color Muted = new(0.65f, 0.72f, 0.85f);
    private static readonly Color Good = new(0.5f, 0.9f, 0.55f);
    private static readonly Color Bad = new(1f, 0.55f, 0.45f);

    private readonly ContentDb _content;
    private readonly Func<SimWorld> _world;
    private readonly int _player;
    private readonly Func<ItemStack[], string> _stacks;
    private readonly ShipDesignEditor _editor = new();
    private readonly Dictionary<(int, int, int), Button> _cells = new();
    private readonly List<(ModuleKind Kind, Button Button)> _palette = new();

    private GridContainer _grid = null!;
    private HFlowContainer _costFlow = null!;
    private IconLabel _status = null!;
    private Label _layerLabel = null!, _selected = null!, _cost = null!, _weapons = null!, _hint = null!;
    private Button _remove = null!, _move = null!, _upgrade = null!, _apply = null!, _revert = null!;
    private SubViewport _view = null!;
    private Node3D _shipRoot = null!, _pivot = null!;
    private Camera3D _camera = null!;
    private int _layer;
    private ModuleKind? _kind;
    private (int Mx, int My, int Mz)? _sel;
    private bool _moving;
    private double _time;
    private float _yaw = 0.7f;
    private bool _dragging;
    private string _signature = "";

    /// <summary>The player pressed Apply: the game submits the command.</summary>
    public event Action<DesignerApply>? ApplyRequested;

    public event Action? CloseRequested;

    /// <summary>A refused edit or a result the game should say aloud (a toast).</summary>
    public event Action<string>? Say;

    public ShipDesigner(ContentDb content, Func<SimWorld> world, int player, Func<ItemStack[], string> stacks)
    {
        _content = content;
        _world = world;
        _player = player;
        _stacks = stacks;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public ShipDesignEditor Editor => _editor;

    public ModuleKind? SelectedKind => _kind;

    private static string KindName(ModuleKind k) => Loc.T("module." + k.ToString().ToLowerInvariant());

    private static string Short(ModuleKind k) => k switch
    {
        ModuleKind.Cockpit => "CK", ModuleKind.Engine => "EN", ModuleKind.Reactor => "RE", ModuleKind.ReactorMk2 => "R2", ModuleKind.Cargo => "CG",
        ModuleKind.Workshop => "WK", ModuleKind.Defence => "DF", ModuleKind.Hull => "HL", ModuleKind.DroneBay => "DB", ModuleKind.Shield => "SH",
        ModuleKind.Sensor => "SN", ModuleKind.Dock => "DK", ModuleKind.MiningLaser => "ML", ModuleKind.MiningLaserMk2 => "L2", ModuleKind.Gun => "GN",
        ModuleKind.GunMk2 => "G2", ModuleKind.Core => "CO", ModuleKind.Shipyard => "SY", _ => "??",
    };

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0.02f, 0.72f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Dock());
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.OffsetLeft = 36; panel.OffsetRight = -36; panel.OffsetTop = 24; panel.OffsetBottom = -24;
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        panel.AddChild(root);

        var head = new HBoxContainer();
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var title = UiKit.Text(24, Colors.White); title.Text = Loc.T("designer.title"); title.Name = "Title";
        head.AddChild(new IconRect("tool:designer", 30, UiTheme.Gold, tooltip: false));
        var sub = UiKit.Text(13, Muted); sub.Text = Loc.T("designer.subtitle");
        titles.AddChild(title); titles.AddChild(sub);
        head.AddChild(titles);
        head.AddChild(UiKit.Button(Loc.T("designer.close"), () => CloseRequested?.Invoke(), 14));
        root.AddChild(head);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 14);
        root.AddChild(body);

        // left: the 3D preview and what the layout adds up to
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
        left.AddThemeConstantOverride("separation", 6);
        var previewTitle = UiKit.Text(15, Muted); previewTitle.Text = Loc.T("designer.preview");
        left.AddChild(previewTitle);
        var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(430, 300), MouseFilter = MouseFilterEnum.Stop, Name = "Preview" };
        container.GuiInput += OnPreviewInput;
        _view = new SubViewport { OwnWorld3D = true, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa2X, Size = new Vector2I(430, 300), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        container.AddChild(_view);
        left.AddChild(container);
        BuildPreviewScene();
        _weapons = UiKit.Text(14, Colors.White); _weapons.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(_weapons);
        _hint = UiKit.Text(12, Muted); _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; _hint.Text = Loc.T("designer.hardpointHint");
        left.AddChild(_hint);
        body.AddChild(left);

        // centre: the layer of the grid
        var centre = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centre.AddThemeConstantOverride("separation", 6);
        var bar = new HBoxContainer();
        bar.AddChild(UiKit.Button(Loc.T("designer.layerDown"), () => { _layer--; _signature = ""; }, 13, 80));
        _layerLabel = UiKit.Text(15, Colors.White); _layerLabel.CustomMinimumSize = new Vector2(240, 0); _layerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        bar.AddChild(_layerLabel);
        bar.AddChild(UiKit.Button(Loc.T("designer.layerUp"), () => { _layer++; _signature = ""; }, 13, 80));
        centre.AddChild(bar);
        var bow = UiKit.Text(12, Muted); bow.Text = "^ " + Loc.T("designer.front"); bow.HorizontalAlignment = HorizontalAlignment.Center;
        centre.AddChild(bow);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Name = "GridScroll" };
        _grid = new GridContainer();
        _grid.AddThemeConstantOverride("h_separation", 3);
        _grid.AddThemeConstantOverride("v_separation", 3);
        scroll.AddChild(_grid);
        centre.AddChild(scroll);
        var stern = UiKit.Text(12, Muted); stern.Text = "v " + Loc.T("designer.back"); stern.HorizontalAlignment = HorizontalAlignment.Center;
        centre.AddChild(stern);
        body.AddChild(centre);

        // right: the module list and the selected module
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        right.AddThemeConstantOverride("separation", 4);
        var palTitle = UiKit.Text(15, Muted); palTitle.Text = Loc.T("designer.palette");
        right.AddChild(palTitle);
        var palScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var pal = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pal.AddThemeConstantOverride("separation", 3);
        foreach (var kind in ShipDesignEditor.Catalogue(_content))
        {
            var k = kind;
            var b = UiKit.Button(KindName(k), () => PickKind(k), 13);
            b.Alignment = HorizontalAlignment.Left;
            b.Icon = Icons.Get(IconCatalog.ForModule(k), 24);
            b.ExpandIcon = false;
            b.AddThemeConstantOverride("icon_max_width", 24);
            b.AddThemeConstantOverride("h_separation", 8);
            b.AddThemeColorOverride("icon_normal_color", ModuleModels.RoleColour(k).Lightened(0.3f));
            b.AddThemeColorOverride("icon_hover_color", Colors.White);
            b.AddThemeColorOverride("icon_pressed_color", Colors.White);
            b.AddThemeColorOverride("icon_hover_pressed_color", Colors.White);
            b.TooltipText = Loc.T("module." + k.ToString().ToLowerInvariant() + ".purpose");
            b.ToggleMode = true;
            pal.AddChild(b);
            _palette.Add((k, b));
        }
        palScroll.AddChild(pal);
        palScroll.CustomMinimumSize = new Vector2(0, 120);
        right.AddChild(palScroll);
        right.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 10) }); // keeps the selection text off the clipped end of the list
        _selected = UiKit.Text(14, Colors.White); _selected.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_selected);
        var row = new HBoxContainer();
        _remove = UiKit.Button(Loc.T("designer.remove"), RemoveSelected, 13);
        _move = UiKit.Button(Loc.T("designer.move"), StartMove, 13);
        row.AddChild(_remove); row.AddChild(_move);
        right.AddChild(row);
        _upgrade = UiKit.Button("", UpgradeSelected, 13);
        right.AddChild(_upgrade);
        body.AddChild(right);

        // bottom: the verdict, the price and the buttons
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 12);
        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _status = new IconLabel(15, 18, Colors.White); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _costFlow = new HFlowContainer { MouseFilter = MouseFilterEnum.Pass };
        _costFlow.AddThemeConstantOverride("h_separation", 12);
        _cost = UiKit.Text(13, Muted); _cost.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        texts.AddChild(_status); texts.AddChild(_cost); texts.AddChild(_costFlow);
        foot.AddChild(texts);
        _revert = UiKit.Button(Loc.T("designer.revert"), () => { _editor.Revert(); _sel = null; _moving = false; _signature = ""; }, 14);
        _apply = UiKit.Button(Loc.T("designer.apply"), () => ApplyRequested?.Invoke(_editor.ToCommand()), 16, 170);
        foot.AddChild(_revert); foot.AddChild(_apply);
        root.AddChild(foot);
        var legend = UiKit.Text(12, Muted); legend.Text = Loc.T("designer.hint"); legend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(legend);
    }

    // ---- state ---------------------------------------------------------------------------------------------

    /// <summary>Opens the designer on the ship's current layout.</summary>
    public void Open()
    {
        Reload();
        Visible = true;
    }

    public void Close() => Visible = false;

    /// <summary>Loads the ship's layout from the world again (after an apply, or on opening).</summary>
    public void Reload()
    {
        var net = _world().Network;
        int ship = net.ShipStructure(_player);
        if (ship >= 0) _editor.Load(net.GetStructure(ship).Modules);
        _sel = null; _moving = false; _kind = null;
        _layer = 0;
        _signature = "";
    }

    public void PickKind(ModuleKind kind)
    {
        _kind = kind;
        _moving = false;
        foreach (var (k, b) in _palette) b.SetPressedNoSignal(k == kind);
        _signature = "";
    }

    /// <summary>Clicks a cell of the grid (also what the buttons do): add the chosen kind to a free cell, select a module, or drop a module being moved.</summary>
    public void ClickCell((int Mx, int My, int Mz) key)
    {
        if (_moving && _sel is { } from)
        {
            Report(_editor.Move(from, key));
            if (_editor.Modules.ContainsKey(key)) _sel = key;
            _moving = false;
        }
        else if (_editor.Modules.ContainsKey(key)) _sel = key;
        else if (_kind is { } kind) Report(_editor.Add(key, kind, _content));
        else Say?.Invoke(Loc.T("designer.hint"));
        _signature = "";
    }

    public void RemoveSelected()
    {
        if (_sel is { } key) { Report(_editor.Remove(key)); _sel = null; _moving = false; _signature = ""; }
    }

    public void StartMove()
    {
        if (_sel is null) return;
        _moving = !_moving;
        _signature = "";
    }

    public void UpgradeSelected()
    {
        if (_sel is { } key) { Report(_editor.Upgrade(key, _content)); _signature = ""; }
    }

    private void Report(DesignEditResult r)
    {
        string? key = r switch
        {
            DesignEditResult.Occupied => "designer.edit.occupied",
            DesignEditResult.NotAdjacent => "designer.edit.notAdjacent",
            DesignEditResult.OutOfBounds => "designer.edit.outOfBounds",
            DesignEditResult.KindNotAllowed => "designer.edit.kindNotAllowed",
            DesignEditResult.NoUpgrade => "designer.edit.noUpgrade",
            DesignEditResult.TooManyModules => "designer.edit.tooMany",
            DesignEditResult.Empty => "designer.edit.empty",
            DesignEditResult.SameCell => "designer.edit.sameCell",
            _ => null,
        };
        if (key is not null) Say?.Invoke(Loc.T(key));
    }

    // ---- drawing -------------------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _time += delta;
        if (!_dragging) _yaw += (float)delta * 0.35f;
        _pivot.Rotation = new Vector3(0f, _yaw, 0f);
        string sig = string.Join(";", _editor.Modules.Select(kv => $"{kv.Key}{(int)kv.Value}")) + $"|{_layer}|{_kind}|{_sel}|{_moving}";
        if (sig == _signature) return;
        _signature = sig;
        Rebuild();
    }

    private void Rebuild()
    {
        var net = _world();
        var (min, max) = _editor.Bounds(1);
        _layerLabel.Text = Loc.T("designer.layer", _layer);
        _grid.Columns = max.X - min.X + 1;
        foreach (var c in _grid.GetChildren()) { _grid.RemoveChild(c); c.QueueFree(); }
        _cells.Clear();
        for (int z = min.Z; z <= max.Z; z++)
            for (int x = min.X; x <= max.X; x++)
            {
                var key = (x, _layer, z);
                _grid.AddChild(MakeCell(key));
            }

        for (int i = 0; i < _palette.Count; i++) _palette[i].Button.SetPressedNoSignal(_palette[i].Kind == _kind);

        // the selected module
        if (_sel is { } s && _editor.Modules.TryGetValue(s, out var sk))
        {
            _selected.Text = Loc.T("designer.selected", $"{KindName(sk)} ({s.Mx}, {s.My}, {s.Mz})") + (_moving ? "\n" + Loc.T("designer.moving") : "");
            _remove.Disabled = false; _move.Disabled = false;
            if (_editor.UpgradeFor(s, _content) is { } up) { _upgrade.Disabled = false; _upgrade.Text = Loc.T("designer.upgrade", KindName(up)); }
            else { _upgrade.Disabled = true; _upgrade.Text = Loc.T("designer.noUpgrade"); }
        }
        else
        {
            _selected.Text = Loc.T("designer.nothingSelected");
            _remove.Disabled = _move.Disabled = _upgrade.Disabled = true;
            _upgrade.Text = Loc.T("designer.noUpgrade");
        }

        var (lasers, lp, guns, gp) = _editor.Hardpoints(_content);
        _weapons.Text = Loc.T("designer.weapons", lasers, lp, guns, gp);
        if (_editor.Flight(_content, net.Network.BonusPercent(BonusKind.EngineSpeed), net.Network.BonusPercent(BonusKind.EngineAccel)) is { } fl)
            _weapons.Text += "\n" + (fl.SecondsToTop > 0
                ? Loc.T("designer.flight", fl.Engines, fl.TopCellsPerSecond.ToString("F1", Loc.Culture), fl.SecondsToTop.ToString("F1", Loc.Culture))
                : Loc.T("designer.flightPlain", fl.Engines, fl.TopCellsPerSecond.ToString("F1", Loc.Culture)));

        // the verdict and the price
        var report = _editor.Check(net, _player);
        foreach (var c in _costFlow.GetChildren()) { _costFlow.RemoveChild(c); c.QueueFree(); }
        if (!_editor.Dirty)
        {
            _status.Text = Loc.T("designer.unchanged"); _status.AddThemeColorOverride("font_color", Muted);
            _status.SetIcon("tool:info", Muted);
            _cost.Text = "";
            _apply.Disabled = true;
        }
        else
        {
            bool ok = report.Ok;
            _status.Text = ok ? Loc.T("designer.valid") + "  -  " + Loc.T("designer.changes", report.Added, report.Removed, report.Moved)
                : Loc.T("designer.invalid", RejectTexts.Key(report.Reject) is { } k ? Loc.T(k) : report.Reject.ToString());
            _status.AddThemeColorOverride("font_color", ok ? Good : Bad);
            _status.SetIcon(ok ? "status:check" : "status:cross", ok ? Good : Bad);
            // The price as icons with counts (red when the hold lacks them), the refund after it in green.
            _cost.Text = report.Cost.Length == 0 ? Loc.T("designer.free") : Loc.T("designer.cost", "").TrimEnd();
            foreach (var c in report.Cost)
            {
                var ic = new IconCount(IconCatalog.ForItem(_content.Items[c.Item].Id), 20);
                ic.SetCost(IconCatalog.ForItem(_content.Items[c.Item].Id), c.Count, net.Network.HoldCount(_player, c.Item));
                _costFlow.AddChild(ic);
            }
            if (report.Refund.Length > 0)
            {
                var cap = UiKit.Text(13, Muted); cap.Text = "   " + Loc.T("designer.refund", "").TrimEnd();
                _costFlow.AddChild(cap);
                foreach (var c in report.Refund)
                {
                    var id = IconCatalog.ForItem(_content.Items[c.Item].Id);
                    var ic = new IconCount(id, 20);
                    ic.SetText(id, "+" + c.Count, Good);
                    _costFlow.AddChild(ic);
                }
            }
            _apply.Disabled = !ok;
        }
        _revert.Disabled = !_editor.Dirty;
        RebuildPreview();
    }

    private Button MakeCell((int Mx, int My, int Mz) key)
    {
        var b = new Button { CustomMinimumSize = new Vector2(46, 40), FocusMode = FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 13);
        Color fill, border = new Color(0.2f, 0.25f, 0.35f);
        int bw = 1;
        if (_editor.Modules.TryGetValue(key, out var kind))
        {
            var role = ModuleModels.RoleColour(kind);
            fill = new Color(role.R * 0.45f, role.G * 0.45f, role.B * 0.45f, 0.95f);
            b.Icon = Icons.Get(IconCatalog.ForModule(kind), 24);
            b.ExpandIcon = false;
            b.AddThemeConstantOverride("icon_max_width", 24);
            b.AddThemeColorOverride("icon_normal_color", role.Lightened(0.35f));
            b.AddThemeColorOverride("icon_hover_color", Colors.White);
            b.AddThemeColorOverride("icon_pressed_color", Colors.White);
            b.TooltipText = KindName(kind);
            bool original = _editor.IsOriginal(key, kind);
            border = original ? role : new Color(1f, 1f, 0.5f);
            bw = original ? 1 : 3;
            if (_sel == key) { border = Colors.White; bw = 3; }
        }
        else
        {
            bool adjacent = _editor.CanAddAt(key);
            fill = adjacent ? new Color(0.09f, 0.13f, 0.2f, 0.9f) : new Color(0.05f, 0.06f, 0.09f, 0.5f);
            if (adjacent) b.Text = _moving || _kind is not null ? "+" : "";
            if (!adjacent && !_moving) b.Disabled = true;
        }
        var sb = UiKit.Box(fill, border, 4, 2f);
        sb.SetBorderWidthAll(bw);
        foreach (var st in new[] { "normal", "hover", "pressed", "disabled", "focus" }) b.AddThemeStyleboxOverride(st, sb);
        b.Pressed += () => ClickCell(key);
        _cells[key] = b;
        return b;
    }

    // ---- the 3D preview ------------------------------------------------------------------------------------

    private void BuildPreviewScene()
    {
        _view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.02f, 0.03f, 0.06f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
                AmbientLightEnergy = 0.9f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.6f, RotationDegrees = new Vector3(-40, 35, 0) };
        _view.AddChild(sun);
        _pivot = new Node3D { Name = "Pivot" };
        _view.AddChild(_pivot);
        _shipRoot = new Node3D { Name = "Ship" };
        _pivot.AddChild(_shipRoot);
        _camera = new Camera3D { Fov = 40f };
        _view.AddChild(_camera);
    }

    private void RebuildPreview()
    {
        foreach (var c in _shipRoot.GetChildren()) { _shipRoot.RemoveChild(c); c.QueueFree(); }
        if (_editor.Modules.Count == 0) return;
        ModuleKind? KindAt(int x, int y, int z) => _editor.Modules.TryGetValue((x, y, z), out var kind) ? kind : null;
        var (min, max) = _editor.Bounds(0);
        foreach (var (key, kind) in _editor.Modules)
        {
            var node = ModuleModels.Build(kind, true, 0, ModuleModels.NeighbourMask(KindAt, key.Mx, key.My, key.Mz));
            node.Position = new Vector3(key.Mx * StationGrid.ModuleX, key.My * StationGrid.ModuleY, key.Mz * StationGrid.ModuleZ);
            _shipRoot.AddChild(node);
        }
        var size = new Vector3((max.X - min.X + 1) * StationGrid.ModuleX, (max.Y - min.Y + 1) * StationGrid.ModuleY, (max.Z - min.Z + 1) * StationGrid.ModuleZ);
        var centre = new Vector3(min.X * StationGrid.ModuleX, min.Y * StationGrid.ModuleY, min.Z * StationGrid.ModuleZ) + size / 2f;
        _shipRoot.Position = -centre;
        float radius = Math.Max(size.Length() / 2f, 8f);
        _camera.Position = new Vector3(0, radius * 0.5f, radius * 1.55f);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private void OnPreviewInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
        else if (e is InputEventMouseMotion mm && _dragging) _yaw += mm.Relative.X * 0.01f;
    }
}
