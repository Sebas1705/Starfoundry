using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The ship computer (Tab): one panel at the right with a tab strip (map, research, inventory, build, info, designer, settings, systems). The
/// tabs reuse the panels that already existed, laid out in the area under the strip; this file owns which tab is open, the direct keys
/// (B, I, M), the two small pages that are new (Systems) or embedded (Settings), and the frame layout. The simulation keeps running while
/// it is open; the mouse is free while it is.
/// </summary>
public partial class Play
{
    private ComputerTab? _computerTab;
    private ShipComputerBar _computerBar = null!;
    private MenuScreen _settingsPage = null!;
    private PanelContainer _systemsPage = null!;
    private Label _sysDeposit = null!, _sysSpeed = null!, _sysAssist = null!;
    private Button _sysDrones = null!, _sysPause = null!;
    private readonly Button[] _sysScreens = new Button[3];
    private int _computerFrame;
    private readonly System.Collections.Generic.List<(Control Node, string Key)> _sysStatic = new(); // labels and buttons with fixed text, re-read when the language changes

    private bool ComputerOpen => _computerTab is not null;

    private ComputerAvailability ComputerAvail => new(Net.Galaxy is not null, ShipyardDocked || _uiAudit);

    // ---- building -----------------------------------------------------------------------------------------------

    private void BuildComputer()
    {
        _computerBar = new ShipComputerBar { Visible = false, Name = "ShipComputerBar" };
        _computerBar.TabChosen += SwitchComputerTab;
        _computerBar.CloseRequested += CloseComputer;
        _uiRoot.AddChild(_computerBar);

        _settingsPage = new MenuScreen("settings.title", dim: false) { Embedded = true, Visible = false, Name = "ComputerSettings" };
        _uiRoot.AddChild(_settingsPage);
        _settingsPage.Open(MenuScreen.Page.Settings);
        _settingsPage.Visible = false;

        BuildSystemsPage();
    }

    private static Label PageLabel(int size, Color? colour = null)
    {
        var l = UiKit.Text(size, colour ?? UiTheme.Text);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    private void BuildSystemsPage()
    {
        _systemsPage = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop, Name = "ComputerSystems" };
        _systemsPage.AddThemeStyleboxOverride("panel", UiKit.Box(new Color(0.05f, 0.07f, 0.11f, 0.95f), new Color(0.35f, 0.45f, 0.65f, 0.8f), 8, 14f));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);
        _systemsPage.AddChild(scroll);
        _uiRoot.AddChild(_systemsPage);

        Label Title(string key) { var t = PageLabel(16, UiTheme.Gold); t.Text = Loc.T(key); t.Name = key; _sysStatic.Add((t, key)); return t; }

        box.AddChild(Title("computer.systems.drones"));
        _sysDeposit = PageLabel(13, UiTheme.Muted);
        box.AddChild(_sysDeposit);
        _sysDrones = UiKit.Button("", AssignDronesKey, 14);
        _sysDrones.Name = "DronesButton";
        box.AddChild(_sysDrones);

        box.AddChild(UiTheme.Rule(UiTheme.Accent, 0.3f));
        var displays = Title("computer.systems.displays");
        box.AddChild(displays);
        for (int i = 0; i < 3; i++)
        {
            var screen = (CockpitScreen)i;
            var b = UiKit.Button("", () => { CloseComputer(); SetFocus(screen); }, 14);
            b.Alignment = HorizontalAlignment.Left;
            b.Name = "Display" + screen;
            _sysScreens[i] = b;
            box.AddChild(b);
        }

        box.AddChild(UiTheme.Rule(UiTheme.Accent, 0.3f));
        box.AddChild(Title("computer.systems.time"));
        _sysSpeed = PageLabel(14);
        box.AddChild(_sysSpeed);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var slower = UiKit.Button("", () => _speed = Math.Max(1, _speed - 1), 14);
        var faster = UiKit.Button("", () => _speed = Math.Min(10, _speed + 1), 14);
        _sysStatic.Add((slower, "computer.systems.slower")); _sysStatic.Add((faster, "computer.systems.faster"));
        _sysPause = UiKit.Button("", () => _paused = !_paused, 14);
        slower.Name = "SlowerButton"; faster.Name = "FasterButton"; _sysPause.Name = "PauseButton";
        foreach (var b in new[] { slower, faster, _sysPause }) { b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(b); }
        box.AddChild(row);

        box.AddChild(UiTheme.Rule(UiTheme.Accent, 0.3f));
        _sysAssist = PageLabel(14);
        box.AddChild(_sysAssist);
        var assist = UiKit.Button("", ToggleFlightAssist, 14);
        assist.Name = "AssistButton";
        _sysStatic.Add((assist, "computer.systems.assistToggle"));
        box.AddChild(assist);
        var dump = UiKit.Button("", DumpSpareOre, 14);
        dump.Name = "DumpButton";
        _sysStatic.Add((dump, "computer.systems.dump"));
        box.AddChild(dump);
    }

    /// <summary>Per tick of the page: the texts that follow the world and the language.</summary>
    private void RefreshSystemsPage()
    {
        foreach (var (node, key) in _sysStatic)
        {
            if (node is Label l) l.Text = Loc.T(key);
            else if (node is Button b) { b.Text = Loc.T(key); if (key == "computer.systems.dump") b.TooltipText = Loc.T("computer.systems.dumpTip"); }
        }
        int bay = _structuresOn && Net.HasPlayer(Player) ? DroneStructure() : -1;
        int deposit = DepositUsable(_selectedDeposit) ? _selectedDeposit : -1;
        _sysDeposit.Text = deposit >= 0 ? Loc.T("computer.systems.deposit", ItemName(Net.DepositItem(deposit))) : Loc.T("computer.systems.depositNone");
        bool assigned = bay >= 0 && Net.AssignedDeposit(bay) >= 0;
        _sysDrones.Text = bay < 0 ? Loc.T("computer.systems.dronesNone") : assigned && (deposit < 0 || deposit == Net.AssignedDeposit(bay)) ? Loc.T("computer.systems.dronesStop") : Loc.T("computer.systems.dronesAssign");
        _sysDrones.Disabled = bay < 0;
        for (int i = 0; i < 3; i++)
        {
            var screen = (CockpitScreen)i;
            bool has = ConsoleReady && _cockpit is not null;
            string page = has ? Loc.T("cp.page." + _cockpit!.Pages.PageOn(screen).ToString().ToLowerInvariant()) : "";
            _sysScreens[i].Text = has ? Loc.T("computer.systems.focus", Loc.T("computer.screen." + screen.ToString().ToLowerInvariant()), page) : Loc.T("computer.systems.displaysNone");
            _sysScreens[i].Disabled = !has;
        }
        _sysSpeed.Text = Loc.T("computer.systems.speed", _speed);
        _sysPause.Text = _paused ? Loc.T("computer.systems.resume") : Loc.T("computer.systems.pause");
        _sysAssist.Text = Loc.T("computer.systems.assist", Loc.T(FlightAssistOn ? "computer.on" : "computer.off"));
    }

    // ---- opening, switching and closing ---------------------------------------------------------------------------

    /// <summary>Opens the computer on a tab, or switches to it when it is already open.</summary>
    private void OpenComputer(ComputerTab tab)
    {
        if (_demo || PauseOpen || EndOpen || StoryOpen || _computerBar is null) return;
        if (!ComputerAvail.Has(tab))
        {
            Toast(Loc.T(ShipComputerTabs.WhyOff(tab, ComputerAvail)!), 4.0);
            return;
        }
        var old = _computerTab;
        if (old is { } was) HideComputerContent(was);
        else
        {
            _beltAnchor = null;
            _dragStart = null;
            _selMachine = -1;
        }
        _computerTab = tab;
        _helpOpen = false;
        _uiRoot.MoveChild(_computerBar, -1);
        ShowComputerContent(tab);
    }

    private void SwitchComputerTab(ComputerTab tab) => OpenComputer(tab);

    private void CloseComputer()
    {
        if (_computerTab is not { } tab) return;
        HideComputerContent(tab);
        _computerTab = null;
        _computerBar.Visible = false;
        _wantCapture = true; // the mouse goes back to the view
    }

    private void ToggleComputer()
    {
        if (ComputerOpen) CloseComputer();
        else OpenComputer(ComputerAvail.Has(_lastComputerTab) ? _lastComputerTab : ComputerTab.Research);
    }

    private ComputerTab _lastComputerTab = ComputerTab.Research;

    /// <summary>A direct key (B, I, M) or a button: that tab, or closes the computer when it is already showing.</summary>
    private void ToggleComputerTab(ComputerTab tab)
    {
        if (_computerTab == tab) CloseComputer();
        else OpenComputer(tab);
    }

    private static void HideNow(Control? c)
    {
        if (c is null) return;
        UiFx.Cancel(c);
        c.Visible = false;
    }

    private void HideComputerContent(ComputerTab tab)
    {
        _lastComputerTab = tab;
        switch (tab)
        {
            case ComputerTab.Map: _mapOpen = false; break;
            case ComputerTab.Research: HideNow(_researchPanel); break;
            case ComputerTab.Inventory: _inventory?.Close(); break;
            case ComputerTab.Build: HideNow(_buildMenu); break;
            case ComputerTab.Info: _detailsOpen = false; break;
            case ComputerTab.Designer: HideNow(_designer); break;
            case ComputerTab.Settings: _settingsPage.Visible = false; break;
            case ComputerTab.Systems: _systemsPage.Visible = false; break;
        }
    }

    private void ShowComputerContent(ComputerTab tab)
    {
        switch (tab)
        {
            case ComputerTab.Map: _mapOpen = true; break;
            case ComputerTab.Research: _researchPanel?.Open(); break;
            case ComputerTab.Inventory:
                if (_inventory is not null)
                {
                    var (target, machine) = InventoryAimTarget();
                    _inventory.Open(target, machine);
                }
                break;
            case ComputerTab.Build: _buildMenu?.Open(_tool == ToolMode.Build ? CategoryOfSelection() : null); break;
            case ComputerTab.Info: _detailsOpen = true; break;
            case ComputerTab.Designer:
                if (_blueprint is not null && UseBlueprintEditor) { CallDeferred(nameof(OpenBlueprintFromComputer)); break; } // the 3D plan takes over
                if (_designer is not null)
                {
                    SetLaser(false);
                    _designer.TopInset = ShipComputerBar.Height + 4f;
                    _designer.Open();
                }
                break;
            case ComputerTab.Settings: _settingsPage.Open(MenuScreen.Page.Settings); break;
            case ComputerTab.Systems: _systemsPage.Visible = true; RefreshSystemsPage(); break;
        }
    }

    /// <summary>Opens the inventory tab on a given container (a reactor or a machine the crosshair is on).</summary>
    private void OpenInventoryOn(ContainerRef target, int machine = -1)
    {
        if (_inventory is null) return;
        OpenComputer(ComputerTab.Inventory);
        _inventory.Open(target, machine);
    }

    // ---- per frame ------------------------------------------------------------------------------------------------

    /// <summary>Lays the strip and the pages that are not panels of their own in the column at the right.</summary>
    private void UpdateComputer(Vector2 size)
    {
        if (_computerBar is null) return;
        bool open = ComputerOpen;
        _computerBar.Visible = open;
        if (!open) return;
        var dock = DockRect(size);
        _computerBar.Set(dock.Position.X, 10f, dock.Size.X, _computerTab, ComputerAvail);
        if (_computerTab == ComputerTab.Settings) _settingsPage.Place(dock);
        if (_computerTab == ComputerTab.Systems)
        {
            _systemsPage.Position = dock.Position;
            _systemsPage.Size = dock.Size;
            if (++_computerFrame % 6 == 0) RefreshSystemsPage();
        }
        if (_computerTab == ComputerTab.Designer && _designer is not null) _designer.TopInset = ShipComputerBar.Height + 4f;
    }

    /// <summary>The wheel: the weapon group in flight, the build entries with a tool in hand.</summary>
    private void WheelStep(int step)
    {
        if (ComputerOpen || PanelOpen() || Standing && _tool != ToolMode.Build) return;
        if (_tool == ToolMode.Build)
        {
            int n = _palette.Count;
            for (int i = 1; i <= n; i++)
            {
                int index = ((_selected + step * i) % n + n) % n;
                if (_palette[index].IsModule || _palette[index].IsStation) continue;
                SelectBuild(index);
                return;
            }
            return;
        }
        ArsenalStep(step < 0 ? -1 : 1); // the wheel steps through the arsenal slots (inside the ship computer it zooms: WheelStep is not reached there)
    }
}
