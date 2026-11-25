using System;
using System.Collections.Generic;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>Items on the ground (ADR 0038): dragging a stack out of the inventory drops it in front of the player, F on an aimed stack picks it up.</summary>
public partial class Play
{
    private readonly Queue<string> _groundToasts = new();

    /// <summary>The dropped stack under the crosshair when it is within hand reach on foot or the tractor range seated; null otherwise.</summary>
    private PickupInfo? StackInReach()
    {
        if (_demo || !AimActive || _hover.Kind != HoverKind.Stack) return null;
        foreach (var k in Net.Pickups())
        {
            if (k.Id != _hover.Id) continue;
            float reach = Standing ? FeelRules.HandReachUnits : FeelRules.TractorRangeUnits + 6 * CellUnits; // the ship is long: the sim measures from the nearest module
            var d = new Vector3(k.X, k.Y, k.Z) / CellUnits - AimOrigin();
            return d.Length() * CellUnits <= reach ? k : null;
        }
        return null;
    }

    private void PickUpAimed()
    {
        if (StackInReach() is not { } k) return;
        var eye = AimOrigin() * CellUnits;
        _groundToasts.Enqueue(L("toast.pickedUp", k.Count, ItemName(k.Item)));
        Send(new PickUp(k.Id, !Standing, (int)eye.X, (int)eye.Y, (int)eye.Z));
    }

    /// <summary>Releases a stack dragged out of the inventory window: it lands a step in front of the player (on the floor when walking, ahead of the ship when seated).</summary>
    private void DropToWorld(ContainerRef from, int slot, int count)
    {
        var dir = -_camera.GlobalTransform.Basis.Z;
        var at = Standing ? AimOrigin() + new Vector3(dir.X, 0f, dir.Z).Normalized() * 1.2f + Vector3.Down * 0.9f : AimOrigin() + dir * 3f;
        at *= CellUnits;
        var stack = Net.ContainerStacks(Player, from);
        foreach (var (s, item, have) in stack)
        {
            if (s != slot) continue;
            _groundToasts.Enqueue(L("toast.dropped", Math.Min(count, have), ItemName(item)));
            Send(new DropItems(from, slot, count, (int)at.X, (int)at.Y, (int)at.Z));
            return;
        }
    }

    private void GroundResult(CommandResult r)
    {
        string? toast = _groundToasts.Count > 0 ? _groundToasts.Dequeue() : null;
        if (!r.Ok) { ToastLogistics(Why(r.Reject), 4.0); return; }
        if (toast is not null) ToastLogistics(toast, 3.0);
        _inventory?.Refresh();
    }

    /// <summary>The look of a dropped stack: a small crate tinted by the item.</summary>
    private static StandardMaterial3D CrateMaterial(int item)
    {
        var c = Color.FromHsv(item * 0.137f % 1f, 0.55f, 0.95f);
        return new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.6f };
    }
}
