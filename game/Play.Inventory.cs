using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The personal inventory in the game (ADR 0035): the F10 key and the reactor or machine you aim at open <see cref="InventoryPanel"/>; this file builds it, lays it out clear
/// of the key panel, refreshes it a few times a second and says what a rejected move was.
/// </summary>
public partial class Play
{
    private InventoryPanel? _inventory;
    private int _inventoryFrame;

    private bool InventoryOpen => _inventory is { Visible: true };

    private void BuildInventoryPanel()
    {
        _inventory = new InventoryPanel(() => Net, () => Player, _content, c => Send(c), t => ToastLogistics(t, 4.0));
        _inventory.CloseRequested += CloseInventory;
        _inventory.DropToWorldRequested += DropToWorld;
        _uiRoot.AddChild(_inventory);
    }

    /// <summary>F10: opens the inventory on what the crosshair is on (a reactor module, a machine), otherwise beside the hold; a second press closes it.</summary>
    private void ToggleInventory() => ToggleComputerTab(ComputerTab.Inventory);

    private void CloseInventory()
    {
        if (_computerTab == ComputerTab.Inventory) CloseComputer();
        else _inventory?.Close();
    }

    /// <summary>What the player is working with: a reactor module under the crosshair, or the machine under it (or the one selected).</summary>
    private (ContainerRef? Target, int Machine) InventoryAimTarget()
    {
        if (!_structuresOn) return (null, -1);
        if (AimActive && AimedModule() is { } m && ModuleKindIds.IsReactor(m.Kind)) return (ContainerRef.Reactor(m.S, m.Key.Mx, m.Key.My, m.Key.Mz), -1);
        int machine = AimActive && _hover.Kind == HoverKind.Machine ? _hover.Id : _selMachine;
        if (machine >= 0 && Net.IsMachineAlive(machine)) return (ContainerRef.MachineIn(machine), machine);
        return (null, -1);
    }

    /// <summary>Per frame: puts the window where the key panel leaves room and re-reads the world a few times a second.</summary>
    private void UpdateInventory(Vector2 size)
    {
        if (_inventory is null || !_inventory.Visible) return;
        float bottom = _keyPanel is { Visible: true } ? _keyRect.Position.Y - 8f : size.Y - 8f;
        if (_computerTab == ComputerTab.Inventory) { var dock = DockRect(size); _inventory.Layout(dock, size); } // under the tab strip, in the computer's column
        else _inventory.Layout(new Rect2(0, 0, size.X, Math.Max(280f, bottom)), size);
        if (++_inventoryFrame % 6 == 0) _inventory.Refresh();
    }

    /// <summary>The outcome of a move, a feed change or a sort: a click on success, the reason on a refusal.</summary>
    private void InventoryResult(CommandResult r)
    {
        if (!r.Ok)
        {
            ToastLogistics(Why(r.Reject), 4.0);
            return;
        }
        if (r.Command is SetFeedSource sf) ToastProduction(L("inv.toast.fed", L(InventoryView.FeedKey(sf.Source))), 3.0);
        _inventory?.Refresh();
    }
}
