using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The station-first interface (ADR 0039): a ship holds ship modules only, so aboard a ship the floor-building tools are closed (with a one-line hint where the build
/// tool would be), the workbench has a panel (F on it, a slow queue of manual crafts) and a station machine has a panel too (F or a click). The decisions are in
/// <see cref="StationFirstUi"/>, <see cref="BenchView"/> and <see cref="MachineView"/>; this file wires them to the game.
/// </summary>
public partial class Play
{
    private BenchPanel? _benchPanel;
    private MachinePanel? _machinePanel;
    private int _stationFrame;

    /// <summary>The world is station-first: ships take no automation (new games and logs from version 20).</summary>
    private bool StationFirstWorld => _structuresOn && Net.StationFirst;

    /// <summary>Floor building is closed now: station-first world, aboard a ship that is not docked at a station.</summary>
    private bool AutomationBlocked => StationFirstUi.AutomationBlocked(StationFirstWorld, ShipStruct >= 0 && !IsDockedNow);

    private bool BenchPanelOpen => _benchPanel is { Visible: true };

    private bool MachinePanelOpen => _machinePanel is { Visible: true };

    /// <summary>Whether a build palette item needs a station while the ship is blocked (belts, machines, and the Fabrication bay of the module tool).</summary>
    private bool ItemBlockedNow(BuildItem item) =>
        AutomationBlocked && (item.IsBelt || (!item.IsModule && !item.IsStation) || (item.IsModule && StationFirstUi.KindBlocked(_moduleKind, true)));

    private bool EntryBlockedNow(BuildEntry e) => StationFirstUi.EntryBlocked(e, AutomationBlocked);

    /// <summary>The crafting machines of a station-first world open the new panel; every other machine keeps the small inspector.</summary>
    private bool MachineUsesPanel(int machine) =>
        StationFirstWorld && Net.IsMachineAlive(machine) && _content.Machines[Net.GetMachineInfo(machine).Type].Role == MachineRole.Crafter;

    private void BuildStationPanels()
    {
        _benchPanel = new BenchPanel(() => Net, () => Player, _content, c => Send(c), () => StationFirstWorld);
        _benchPanel.CloseRequested += CloseBenchPanel;
        _uiRoot.AddChild(_benchPanel);

        _machinePanel = new MachinePanel(() => Net, _content, c => Send(c), SatisfactionOf, NeedsPower, RecipesFor,
            id => { Send(new RemoveMachine(id)); _selMachine = -1; }, id => { if (_content.Machines[Net.GetMachineInfo(id).Type].Role == MachineRole.Drill) RelinkDrill(id, Net.GetMachineInfo(id).Recipe); });
        _machinePanel.CloseRequested += () => _selMachine = -1;
        _uiRoot.AddChild(_machinePanel);
    }

    private void OpenBenchPanel()
    {
        if (_benchPanel is null) return;
        _selMachine = -1;
        _benchPanel.Open();
    }

    private void CloseBenchPanel() => _benchPanel?.Close();

    /// <summary>F on the workbench: the panel in a station-first world, the old recipe cycle in a legacy one.</summary>
    private void BenchKey()
    {
        if (BenchView.UsesPanel(StationFirstWorld)) { if (BenchPanelOpen) CloseBenchPanel(); else OpenBenchPanel(); }
        else ToggleRefinery();
    }

    /// <summary>The key to close whichever station panel is open (F and Esc). True when one was.</summary>
    private bool CloseStationPanel()
    {
        if (BenchPanelOpen) { CloseBenchPanel(); return true; }
        if (MachinePanelOpen) { _selMachine = -1; _machinePanel!.Close(); return true; }
        return false;
    }

    /// <summary>Pressing the build key aboard a ship says where automation is built; the module tab stays usable.</summary>
    private void NoteBuildAboard()
    {
        if (AutomationBlocked) ToastLogistics(StationFirstUi.Hint(true), 4.0);
    }

    /// <summary>Per frame: puts the station panels where the key panel leaves room, keeps them in step with the world, and puts away a tool the ship cannot use.</summary>
    private void UpdateStationPanels(Vector2 size)
    {
        if (_benchPanel is null || _machinePanel is null) return;
        if (_tool == ToolMode.Build && _selected >= 0 && _selected < _palette.Count && ItemBlockedNow(_palette[_selected]))
        {
            _tool = ToolMode.Pointer; // e.g. the ship left a station's dock with a machine in hand
            _beltAnchor = null;
            _dragStart = null;
            ToastLogistics(StationFirstUi.Hint(true), 4.0);
        }
        if (_selMachine >= 0 && !_demo && MachineUsesPanel(_selMachine))
        {
            if (_machinePanel.Machine != _selMachine || !_machinePanel.Visible) { CloseBenchPanel(); _machinePanel.Open(_selMachine); }
        }
        else if (_machinePanel.Visible) _machinePanel.Close();
        if (!Net.HasPlayer(Player) || _demo) { _benchPanel.Close(); return; }

        float bottom = _keyPanel is { Visible: true } ? _keyRect.Position.Y - 8f : size.Y - 8f;
        _stationFrame++;
        if (_benchPanel.Visible)
        {
            _benchPanel.Layout(size, bottom);
            if (_stationFrame % 6 == 0) _benchPanel.Refresh();
        }
        if (_machinePanel.Visible)
        {
            _machinePanel.Layout(size, bottom);
            if (_stationFrame % 6 == 0) _machinePanel.Refresh();
        }
    }
}
