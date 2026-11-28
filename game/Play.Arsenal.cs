using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The arsenal bar at the bottom centre (docs/prototypes/arsenal.md): the weapons the ship has fitted as numbered slots, the slot in hand, and the keys that move the selection.
/// It is a view over <see cref="ArsenalModel"/>; the weapon group in hand (ADR 0036) stays the single piece of state of the simulation side, so a slot only takes its group in hand:
/// all the lasers act together and all the guns act together. Nothing here touches the command log.
/// </summary>
public partial class Play
{
    private readonly ArsenalModel _arsenal = new();
    private readonly ArsenalVisibility _arsenalVis = new();
    private ArsenalBar? _arsenalBar;
    private double _arsenalFitClock = 99.0;
    private float _arsenalTop = -1f; // top edge of the bar on screen, eased with its fade (-1: not drawn); the hint and the toasts stay above it

    /// <summary>What the bar and the cockpit weapons page draw from.</summary>
    internal ArsenalModel Arsenal => _arsenal;

    /// <summary>Number keys 1 to 9: the slot of that number takes its weapon group in hand.</summary>
    private void ArsenalSelect(int index)
    {
        if (!Net.HasPlayer(Player) || Standing) return;
        _arsenalVis.Touch();
        if (!_arsenal.Select(index)) return;
        var before = _weaponGroup;
        if (_arsenal.Group is { } g) PickWeaponGroup(g);
        if (_weaponGroup == before) AudioManager.PlayUi(Sfx.UiClick); // a group change clicks by itself
    }

    /// <summary>V and the mouse wheel: the next (+1) or previous (-1) slot, wrapping around.</summary>
    private void ArsenalStep(int direction)
    {
        if (!Net.HasPlayer(Player) || Standing) { if (Standing) ToastLogistics(L("toast.sitToFly")); return; }
        _arsenalVis.Touch();
        if (_arsenal.IsEmpty) { PickWeaponGroup(_weaponGroup == WeaponGroup.Laser ? WeaponGroup.Guns : WeaponGroup.Laser); return; } // nothing fitted: the old answer (a rejection toast)
        if (!_arsenal.Step(direction)) return;
        var before = _weaponGroup;
        if (_arsenal.Group is { } g) PickWeaponGroup(g);
        if (_weaponGroup == before) AudioManager.PlayUi(Sfx.UiClick); // a group change clicks by itself
    }

    private void RefitArsenal()
    {
        var fitted = new List<FittedWeapon>(4);
        if (!HardpointsOn)
        {
            fitted.Add(new FittedWeapon(ArsenalKind.Laser, 1, 1)); // a world without hardpoint modules: one implicit laser and one implicit gun
            fitted.Add(new FittedWeapon(ArsenalKind.Gun, 1, 1));
        }
        else
        {
            foreach (var m in Net.MiningLasers(Player)) fitted.Add(new FittedWeapon(ArsenalKind.Laser, m.Kind == ModuleKind.MiningLaserMk2 ? 2 : 1, 1));
            foreach (var m in Net.Guns(Player)) fitted.Add(new FittedWeapon(ArsenalKind.Gun, m.Kind == ModuleKind.GunHeavy ? 3 : m.Kind == ModuleKind.GunMk2 ? 2 : 1, 1));
        }
        _arsenal.Fit(fitted);
    }

    private void RefreshArsenalLive()
    {
        var info = Net.GetPlayerInfo(Player);
        int lost = 0;
        int ship = ShipStruct;
        if (_structuresOn && Net.StructureRules && ship >= 0 && HasReactor(ship)) lost = Math.Clamp(1000 - Net.PowerStatus(ship).SatisfactionPermille, 0, 1000);
        int readout = MiningReadout(out int left, out bool slowed);
        _arsenal.SetLive(ArsenalKind.Laser, new ArsenalLive(
            Firing: _laserOn && Net.LaserFiring(Player), PowerLostPermille: lost, Blocked: _laserOn && _laserOutOfRangeToasted,
            HasDeposit: left >= 0, MineTenths: readout, DepositLeft: Math.Max(0, left), Slowed: slowed));
        var threat = _content.Threat;
        bool ammo = threat is { ShipAmmoPerVolley: > 0 };
        int rounds = ammo ? Net.HoldCount(Player, threat!.ShipAmmoItem) : 0;
        _arsenal.SetLive(ArsenalKind.Gun, new ArsenalLive(
            Firing: info.Armed, PowerLostPermille: lost, Dry: ammo && rounds < threat!.ShipAmmoPerVolley, UsesAmmo: ammo, Ammo: rounds));
    }

    /// <summary>Per frame: keeps the model in step with the ship and the weapon in hand, fades the bar and lays it out at the bottom centre.</summary>
    private void UpdateArsenal(float dt)
    {
        bool player = Net.HasPlayer(Player) && !_demo && !Overview;
        if (player)
        {
            _arsenalFitClock += dt;
            if (_arsenalFitClock >= 0.25) { _arsenalFitClock = 0.0; RefitArsenal(); }
            if (_arsenal.Group != _weaponGroup) _arsenal.SelectGroup(_weaponGroup); // something else changed the weapon in hand (the cockpit button, a toast key)
            RefreshArsenalLive();
        }
        _arsenal.Animate(dt);

        var mode = CurrentHudMode;
        if (_focus is not null && mode == HudMode.Full) mode = HudMode.Cockpit; // looking at a display: its data is not repeated outside
        var ctx = new ArsenalContext(!Standing, _tool == ToolMode.Build, ThreatNowHud(), ComputerOpen, mode, CurrentHudDetail);
        bool otherPanel = PanelOpen() && !ComputerOpen;
        bool show = player && !otherPanel && (!_arsenal.IsEmpty || HardpointsOn); // an empty arsenal of a hardpoint ship shows its hint
        float alpha = _arsenalVis.Tick(dt, ctx, show, _arsenal.AnyFiring);
        if (_arsenalBar is null)
        {
            if (alpha <= 0.01f) return;
            _arsenalBar = new ArsenalBar(_arsenal) { Picked = i => { if (!Standing) ArsenalSelect(i); } };
            _uiRoot.AddChild(_arsenalBar);
        }
        _arsenalBar.Compact = _arsenalVis.Compact;
        _arsenalBar.Visible = alpha > 0.01f;
        if (!_arsenalBar.Visible) { _arsenalTop = -1f; return; }
        _arsenalBar.Modulate = new Color(1f, 1f, 1f, alpha);
        _arsenalBar.MouseFilter = alpha > 0.4f && !_mouseLook ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore; // clickable with the mouse free (the ship computer)

        var size = GetViewport().GetVisibleRect().Size;
        var bar = _arsenalBar.PreferredSize();
        _arsenalBar.Size = bar;
        // The bar sits at the bottom centre, between the hold strip (left) and the key panel (right). When the slots do not fit in that gap
        // (a small window with many weapons) the bar shrinks and slides into it instead of running over either panel.
        float gapL = 12f, gapR = size.X - 12f;
        foreach (var other in new Control?[] { _toolbar, _keyPanel })
        {
            if (other is null || !other.Visible || other.Modulate.A < 0.05f) continue;
            var o = new Rect2(other.Position, other.Size * other.Scale);
            if (o.End.Y < size.Y - bar.Y - 16f) continue; // ends above the bar's row
            if (o.GetCenter().X < size.X / 2f) gapL = Math.Max(gapL, o.End.X + 12f);
            else gapR = Math.Min(gapR, o.Position.X - 12f);
        }
        float fit = Math.Clamp((gapR - gapL) / Math.Max(1f, bar.X), 0.5f, 1f);
        _arsenalBar.Scale = new Vector2(fit, fit);
        var drawn = bar * fit;
        var pos = new Vector2(Math.Clamp((size.X - drawn.X) / 2f, gapL, Math.Max(gapL, gapR - drawn.X)), size.Y - drawn.Y - 8f);
        foreach (var other in new Control?[] { _toolbar, _keyPanel })
        {
            if (other is null || !other.Visible || other.Modulate.A < 0.05f) continue;
            var o = new Rect2(other.Position, other.Size * other.Scale);
            if (new Rect2(pos, drawn).Grow(6f).Intersects(o)) pos.Y = Math.Min(pos.Y, o.Position.Y - drawn.Y - 8f); // last resort: never over the hold strip or the key panel
        }
        _arsenalBar.Position = pos;
        _arsenalTop = size.Y - (size.Y - pos.Y + 4f) * alpha;
        _arsenalBar.QueueRedraw();
    }
}
