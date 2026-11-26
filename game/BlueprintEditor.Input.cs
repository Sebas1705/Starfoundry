using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Mouse and keys of the blueprint editor. Every key is looked up in <see cref="InputActions"/> (context "blueprint editor"), so the F1 help and the key page agree.</summary>
public partial class BlueprintEditor
{
    private bool _orbiting, _panning;
    private Vector2 _lastMouse;

    private const float OrbitPerPixel = 0.006f;

    private void OnPlanInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb:
                _lastMouse = mb.Position;
                if (mb.ButtonIndex == MouseButton.Right) _orbiting = mb.Pressed;
                else if (mb.ButtonIndex == MouseButton.Middle) _panning = mb.Pressed;
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) { _s.Camera.Zoom(1f); _sceneDirty = true; }
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) { _s.Camera.Zoom(-1f); _sceneDirty = true; }
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    ClickAt(mb.Position, mb.ShiftPressed || mb.CtrlPressed);
                    if (mb.DoubleClick && _s.StructureKind == StructureKind.Station && _s.Selection.Count == 1) OpenFloorOfSelection(); // a double click on a module opens its floor plan
                }
                break;
            case InputEventMouseMotion mm:
                if (_orbiting) { _s.Camera.Orbit(-mm.Relative.X * OrbitPerPixel, mm.Relative.Y * OrbitPerPixel); SyncViewButtons(); _refresh = true; }
                else if (_panning) _s.Camera.Pan(-mm.Relative.X / Math.Max(1f, _container.Size.Y), mm.Relative.Y / Math.Max(1f, _container.Size.Y));
                else HoverAt(mm.Position);
                _lastMouse = mm.Position;
                break;
        }
    }

    /// <summary>The cell a click or the pointer means with the active tool: the free cell beside the face for Place (and a Move that has picked a module up), the module itself otherwise.</summary>
    private (int Mx, int My, int Mz)? CellFor(Vector2 at)
    {
        var size = _container.Size;
        var pick = _s.Camera.Pick(_s.Camera.RayAt(at.X, at.Y, size.X, size.Y), _s.Draft.Modules);
        return _s.Tool switch
        {
            BlueprintTool.Place => pick.Place ?? pick.Module,
            BlueprintTool.Move when _s.Carrying is not null => pick.Place ?? pick.Module,
            _ => pick.Module,
        };
    }

    private void HoverAt(Vector2 at)
    {
        var cell = CellFor(at);
        if (cell != _hoverCell) { _hoverCell = cell; _sceneDirty = true; }
    }

    /// <summary>A left click at a pixel of the plan (also what a headless run calls with a pixel it has projected).</summary>
    public void ClickAt(Vector2 at, bool additive = false)
    {
        _discardArmed = false;
        var cell = CellFor(at);
        if (cell is null)
        {
            if (_s.Tool == BlueprintTool.Select && !additive) _s.ClearSelection();
            return;
        }
        Report(_s.Click(cell.Value, additive));
    }

    /// <summary>A click on a cell (no mouse): what the harness and the keyboard-only paths use.</summary>
    public void ClickCell((int Mx, int My, int Mz) cell, bool additive = false)
    {
        _discardArmed = false;
        Report(_s.Click(cell, additive));
    }

    /// <summary>The key name the registry knows: a control combination becomes "@ctrlz" and so on, every other key its Godot name.</summary>
    public static string KeyName(InputEventKey k)
    {
        if (k.CtrlPressed || k.MetaPressed)
            return k.Keycode is >= Key.A and <= Key.Z ? "@ctrl" + k.Keycode.ToString().ToLowerInvariant() : "";
        return k.Keycode == Key.KpEnter ? "Enter" : k.Keycode.ToString();
    }

    /// <summary>The game says whether its own help overlay is open (Esc then closes the help first).</summary>
    public Func<bool>? HelpOpen { get; set; }

    /// <summary>The editor's keys are read before the focus system, so the arrow keys nudge and Enter applies even while a button has the focus.</summary>
    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } k || KeyBindingsPage.Capturing) return;
        string key = KeyName(k);
        if (key.Length == 0 || InputActions.Find(key, InputContext.Blueprint) is not { } action) return;
        if (action.Id is "help" or "fullscreen" || (action.Id == "cancel" && HelpOpen?.Invoke() == true)) return; // the game's own keys
        RunAction(action.Id, InputActions.SlotOf(action.Id, key));
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Runs the action a key stands for. Returns false when the key is not one of the editor's (the game then handles it: help, full screen).</summary>
    public bool HandleKey(InputEventKey k)
    {
        string key = KeyName(k);
        if (key.Length == 0) return false;
        if (InputActions.Find(key, InputContext.Blueprint) is not { } action) return false;
        if (action.Id is "help" or "fullscreen") return false;
        RunAction(action.Id, InputActions.SlotOf(action.Id, key));
        return true;
    }

    /// <summary>Esc: closes the innermost thing (a move in hand, the selection, the tool), then the editor (twice when the draft has changes).</summary>
    public void Cancel()
    {
        if (_s.FloorModule is not null)
        {
            if (_s.PlanSelection is not null) _s.SetPlanTool(_s.PlanTool); else CloseFloor();
            return;
        }
        if (_s.Carrying is not null || _s.Selection.Count > 0) { _s.ClearSelection(); return; }
        if (_s.Tool != BlueprintTool.Select) { _s.SetTool(BlueprintTool.Select); return; }
        RequestClose();
    }

    public void RunAction(string id, int slot)
    {
        _discardArmed = id == "cancel" && _discardArmed;
        switch (id)
        {
            case "cancel": Cancel(); break;
            case "bpSelect": SetTool(BlueprintTool.Select); break;
            case "bpPlace": SetTool(BlueprintTool.Place); break;
            case "bpMove": SetTool(BlueprintTool.Move); break;
            case "bpRotate":
                if (_s.Selection.Count > 0) _s.RotateSelection(); else SetTool(BlueprintTool.Rotate);
                break;
            case "bpDelete":
                if (_s.Selection.Count > 0) _s.DeleteSelection(); else SetTool(BlueprintTool.Delete);
                break;
            case "bpMirror": Mirror(); break;
            case "bpUpgrade": Upgrade(); break;
            case "bpCopy": Copy(); break;
            case "bpPaste": Paste(); break;
            case "bpSelectAll": _s.SelectAll(); break;
            case "bpUndo": Undo(); break;
            case "bpRedo": Redo(); break;
            case "bpSave": SaveSlot(1); break;
            case "bpApply": Apply(); break;
            case "bpRevert": Revert(); break;
            case "bpView": _s.Camera.CycleView(_s.DefaultSectionLayer()); SyncViewButtons(); _sceneDirty = true; _refresh = true; break;
            case "bpFrame":
                if (_s.Selection.Count > 0) _s.Camera.FrameCell(_s.Selection.First()); else FrameAll();
                break;
            case "bpTurn": _s.Camera.Orbit(slot == 0 ? 0.2f : -0.2f, 0f); SyncViewButtons(); break;
            case "bpLayer":
            {
                if (_s.Camera.View != PlanView.Section) _s.Camera.SetSection(_s.DefaultSectionLayer());
                else _s.Camera.SetSection(_s.Camera.SectionLayer + (slot == 0 ? 1 : -1));
                SyncViewButtons(); _sceneDirty = true; _refresh = true;
                break;
            }
            case "bpNudge":
            {
                var (dx, dz) = slot switch { 0 => (0, -1), 1 => (0, 1), 2 => (-1, 0), _ => (1, 0) };
                if (_s.Selection.Count > 0) { if (!_s.Nudge(dx, 0, dz).Any) Say?.Invoke(Loc.T("bp.toast.noMove")); }
                else { _s.Camera.Pan(dx * 0.05f, -dz * 0.05f); }
                break;
            }
        }
    }
}
