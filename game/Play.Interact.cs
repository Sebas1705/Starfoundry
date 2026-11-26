using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The interact key (F) and the key dispatcher: every key is looked up in <see cref="InputActions"/> for the contexts that are active
/// (the computer open, a build tool in hand, standing, docked) and the action it names is run here. F does one thing at a time, chosen
/// by what the crosshair is on and where the ship is: <see cref="InteractNow"/> names it for both the key and the key panel.
/// </summary>
public partial class Play
{
    /// <summary>What F does right now, in order of what the player is most clearly asking for.</summary>
    private HintInteract InteractNow()
    {
        if (_demo || !Net.HasPlayer(Player)) return HintInteract.None;
        if (_focus is not null) return HintInteract.Console;
        if (StackInReach() is not null) return HintInteract.PickUp;
        if (_tool != ToolMode.Build && AimActive && AimingAtBench()) return StationFirstWorld ? HintInteract.BenchPanel : HintInteract.Bench; // the aimed workbench wins over the seat, standing or seated
        if (Standing)
        {
            if (LadderWay() is not null) return HintInteract.Climb;
            return BodyMotion.InSeatReach(Sys(_body), Sys(SeatPosition)) ? HintInteract.Sit : HintInteract.SitFar;
        }
        if (AimActive)
        {
            if (_cockpitAim is not null && ConsoleReady && _tool != ToolMode.Build) return HintInteract.Console;
            if (_hover.Kind == HoverKind.Machine && Net.IsMachineAlive(_hover.Id)) return HintInteract.Machine;
            if (_structuresOn && AimedModule() is { } module)
            {
                if (ModuleKindIds.IsReactor(module.Kind)) return HintInteract.Reactor;
                if (module.Kind == ModuleKind.Shipyard && ShipyardDocked) return HintInteract.Shipyard;
                if (module.Kind == ModuleKind.Core && StationBlueprintAt(module.S)) return HintInteract.CoreConsole; // ADR 0043
            }
            if (_hover.Kind == HoverKind.Deposit && _structuresOn && DroneStructure() >= 0) return HintInteract.Drones;
        }
        if (_structuresOn)
        {
            if (IsDockedNow) return HintInteract.Undock;
            if (_dockNear)
                return _dockAssist ? HintInteract.DockCancel : _dockCand.InRange && _dockCand.SlowEnough && _dockCand.Aligned ? HintInteract.Dock : HintInteract.DockAssist;
        }
        return CanWalk ? HintInteract.Stand : HintInteract.None;
    }

    /// <summary>True for a station of the player's in a world where stations are edited in the blueprint editor.</summary>
    private bool StationBlueprintAt(int structure) =>
        Net.BlueprintRules && Net.StructureExists(structure) && Net.GetStructure(structure).Kind == StructureKind.Station && Net.GetStructure(structure).Owner == Player;

    private void OpenStationBlueprintDeferred(int station) => OpenStationBlueprint(station);

    private void InteractKey()
    {
        if (CloseStationPanel()) return; // F closes the workbench or machine panel that is open
        switch (InteractNow())
        {
            case HintInteract.Console:
                if (_focus is not null) SetFocus(null);
                else if (_cockpitAim is { } aim) SetFocus(aim.Screen);
                break;
            case HintInteract.Machine:
                _selMachine = _hover.Id; // its panel (recipe, repair, remove) opens; the mouse is free to use the buttons
                if (!MachineUsesPanel(_hover.Id)) _wantCapture = false; // the new panel frees the mouse itself and gives it back on close
                break;
            case HintInteract.Reactor:
                if (AimedModule() is { } reactor) OpenInventoryOn(ContainerRef.Reactor(reactor.S, reactor.Key.Mx, reactor.Key.My, reactor.Key.Mz));
                break;
            case HintInteract.Shipyard: OpenComputer(ComputerTab.Designer); break;
            case HintInteract.CoreConsole:
                if (AimedModule() is { } core) CallDeferred(nameof(OpenStationBlueprintDeferred), core.S);
                break;
            case HintInteract.Bench: ToggleRefinery(); break;
            case HintInteract.BenchPanel: BenchKey(); break;
            case HintInteract.Drones: AssignDronesKey(); break;
            case HintInteract.PickUp: PickUpAimed(); break;
            case HintInteract.Dock: case HintInteract.DockAssist: case HintInteract.DockCancel: case HintInteract.Undock: DockKey(); break;
            case HintInteract.Climb: case HintInteract.Sit: case HintInteract.SitFar: case HintInteract.Stand: ToggleSeat(); break;
        }
    }

    // ---- keys -------------------------------------------------------------------------------------------------

    private void OnKey(InputEventKey k)
    {
        if (KeyBindingsPage.Capturing) return; // the rebinding page is waiting for a key
        if (k.CtrlPressed || k.MetaPressed) return; // no binding uses them
        if (CockpitKey(k)) return; // the keys of a focused display
        string key = k.Keycode == Key.KpEnter ? "Enter" : k.Keycode.ToString();
        var active = InputActions.Active(ComputerOpen, _tool == ToolMode.Build, Standing, IsDockedNow, blueprint: BlueprintOpen);
        if (InputActions.Find(key, active) is { } action) RunAction(action.Id, k.Keycode, InputActions.SlotOf(action.Id, key));
    }

    private void RunAction(string id, Key key, int slot)
    {
        int digit = key == Key.Key0 ? 9 : key is >= Key.Key1 and <= Key.Key9 ? (int)(key - Key.Key1) : -1;
        switch (id)
        {
            case "computer": ToggleComputer(); break;
            case "openBuild": ToggleComputerTab(ComputerTab.Build); break;
            case "openInventory": ToggleComputerTab(ComputerTab.Inventory); break;
            case "openMap": ToggleComputerTab(ComputerTab.Map); break;
            case "hudDetail": CycleHudDetail(); break;
            case "help": _helpOpen = !_helpOpen; break;
            case "quickSave": SaveGameToDisk("quicksave", silent: false); break;
            case "quickLoad": LoadGameFromDisk(); break;
            case "fullscreen":
                SettingsStore.Set(SettingsStore.Current with { Fullscreen = DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen });
                break;
            case "cancel": CancelKey(); break;
            case "interact": InteractKey(); break;
            case "assist": ToggleFlightAssist(); break;
            case "scan": Send(new Scan()); break;
            case "weaponNext": ArsenalStep(1); break;
            case "weaponPick":
                if (digit >= 0) ArsenalSelect(digit);
                break;
            case "hotbar":
                if (digit >= 0 && digit < _palette.Count && !_palette[digit].IsModule && !_palette[digit].IsStation) SelectBuild(digit);
                break;
            case "rotate": _dir = _dir switch { Dir.PosX => Dir.PosZ, Dir.PosZ => Dir.NegX, Dir.NegX => Dir.NegZ, _ => Dir.PosX }; Toast(L("toast.beltFacing", _dir.ToString())); break;
            case "recipe": CycleRecipeKey(); break;
            case "kind":
                if (_structuresOn) { CycleModuleKind(); break; } // first press: the module tool; next ones: the kind
                int module = _palette.FindIndex(b => b.IsModule);
                if (module >= 0) SelectBuild(module);
                break;
            case "layer" when _tool == ToolMode.Build && !(FloorRule && !ModuleSelected): _cursorY += slot == 1 ? 1 : -1; break; // on the floor rule only the module tool has layers
            case "unload": UnloadKey(); break;
            case "load": LoadKey(); break;
            case "tabs" when _computerTab is { } tab: SwitchComputerTab(ShipComputerTabs.Step(tab, slot == 0 ? -1 : 1, ComputerAvail)); break;
            case "tabPick" when digit >= 0:
                if (ShipComputerTabs.FromNumber(digit + 1, ComputerAvail) is { } picked) SwitchComputerTab(picked);
                break;
            case "mapChoose" when _computerTab == ComputerTab.Map: CycleSystem(slot == 0 ? -1 : 1); break;
            case "mapTurn" when _computerTab == ComputerTab.Map: _map.Orbit(slot == 0 ? -0.25f : 0.25f, 0f); break; // rotate the 3D map
            case "mapJump" when _computerTab == ComputerTab.Map: SendJump(); break;
            case "mapReset" when _computerTab == ComputerTab.Map: _map.ResetView(); break;
        }
    }

    /// <summary>Esc: closes the innermost thing, one step at a time, and last opens the pause menu.</summary>
    private void CancelKey()
    {
        if (_helpOpen) _helpOpen = false;
        else if (CloseStationPanel()) { }
        else if (ComputerOpen)
        {
            if (_computerTab == ComputerTab.Inventory && _inventory!.HandleEscape()) return;
            CloseComputer();
        }
        else if (_dockAssist) { _dockAssist = false; SendThrust((0, 0, 0), force: true); ToastLogistics(L("toast.approachCancelled")); }
        else if (_beltAnchor is not null) { _beltAnchor = null; _dragStart = null; } // first Esc cancels a belt line in progress
        else if (_selMachine >= 0) _selMachine = -1;
        else if (_tool == ToolMode.Build) _tool = ToolMode.Pointer;
        else if (_mapOpen) _mapOpen = false;
        else { _waypoint = null; _wantCapture = false; OpenPause(); } // last: let go of the mouse and open the pause menu
    }

    /// <summary>The ship computer key is read before the focus system can take Tab for itself (a focused button would swallow it).</summary>
    public override void _Input(InputEvent e)
    {
        if (KeyBindingsPage.Capturing) return; // the rebinding page is waiting for a key
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab } k || k.CtrlPressed || k.AltPressed || k.ShiftPressed) return;
        if (_demo || PauseOpen || EndOpen || StoryOpen || BlueprintOpen) return;
        GetViewport().SetInputAsHandled();
        ToggleComputer();
    }
}
