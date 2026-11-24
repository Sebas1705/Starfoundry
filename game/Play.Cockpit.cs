using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The cockpit displays and what goes with them (see docs/prototypes/cockpit-displays.md): the three screens of the pilot's console
/// (<see cref="CockpitDisplays"/>), the data they show, the crosshair as the pointer (hover, click, focus), the HUD modes (F3) and the camera work
/// that makes a screen readable (F4 or a click on a screen moves the view in on it). Everything here is client-side: the simulation only
/// sees the same commands the keys send.
/// </summary>
public partial class Play
{
    private CockpitDisplays? _cockpit;
    private CockpitScreen? _focus;
    private float _focusOffYaw, _focusOffPitch;
    private float _fovNow;
    private float _cockpitClock, _cockpitTime;
    private bool _swallowRelease;
    private (CockpitScreen Screen, Vector2 Pixel)? _cockpitAim;
    private CockpitSnapshot _snap = new();
    private readonly Dictionary<CockpitScreen, Vector2?> _lastHover = new();

    /// <summary>The HUD mode a screenshot run pins (the settings file is never touched by a shot).</summary>
    private HudMode? _hudModeShot;

    private HudMode CurrentHudMode => _hudModeShot ?? SettingsStore.Current.HudMode;

    /// <summary>Which families of the outside overlay show now: the mode and whether the pilot sits.</summary>
    private HudLayers HudLayersNow => HudModes.Layers(_focus is not null && CurrentHudMode == HudMode.Full ? HudMode.Cockpit : CurrentHudMode, ShipHudContext); // looking at a display: its data is not repeated outside

    /// <summary>The ship has cockpit displays to look at.</summary>
    /// <summary>The crosshair works as a pointer: the mouse is captured (or a screenshot run drives it).</summary>
    private bool CockpitPointer => _mouseLook || CockpitShotActive;

    private bool ConsoleReady => !_demo && _cockpit is { IsAttached: true };

    private bool KeyPanelWanted => HudLayersNow.KeyPanel || PanelOpen() || _tool == ToolMode.Build || _focus is not null;

    // ---- the HUD mode ----------------------------------------------------------------------------------

    /// <summary>F3 (and the button on every display): the next HUD mode, saved in the settings.</summary>
    private void CycleHudMode()
    {
        var next = HudModes.Next(CurrentHudMode);
        if (_hudModeShot is not null) _hudModeShot = next;
        else SettingsStore.Set(SettingsStore.Current with { HudMode = next });
        string name = L("cp.hud." + HudModes.Id(next));
        Toast(L(ShipHudContext ? "toast.hudMode" : "toast.hudModeSeat", name), 3.0);
    }

    // ---- attaching the displays and feeding them -----------------------------------------------------

    private void EnsureCockpit()
    {
        if (_cockpit is not null) return;
        _cockpit = new CockpitDisplays { Name = "CockpitDisplays" };
        AddChild(_cockpit);
    }

    /// <summary>
    /// Per frame, after the camera is placed: puts the displays on the cockpit module, finds what the crosshair is on, and redraws the screens
    /// (about fifteen times a second, the focused one every frame).
    /// </summary>
    private void UpdateCockpitDisplays(float dt)
    {
        if (_demo || Overview || !_structuresOn || ShipStruct < 0 || !Net.HasPlayer(Player)) { _focus = null; return; }
        EnsureCockpit();
        var module = Seat.FromCockpit ? _stationView.ModuleNode(ShipStruct, Seat.Module) : null;
        _cockpit!.Attach(module);
        if (!_cockpit.IsAttached || PanelOpen()) { _focus = null; if (!_cockpit.IsAttached) return; } // a menu or the map needs the whole view
        _cockpitTime += dt;

        // what the crosshair is on
        _cockpitAim = null;
        if (CockpitPointer && AimActive && !PanelOpen() && _tool != ToolMode.Build && _cockpit.Raycast(AimOrigin(), AimDirection(), out var screen, out var pixel, out _))
            _cockpitAim = (screen, pixel);
        bool refreshAll = false;
        for (int i = 0; i < 3; i++)
        {
            var sc = (CockpitScreen)i;
            var view = _cockpit.View(sc);
            Vector2? hover = _cockpitAim is { } a && a.Screen == sc ? a.Pixel : null;
            bool changed = hover != _lastHover.GetValueOrDefault(sc);
            _lastHover[sc] = hover;
            view.Hover = hover;
            view.Focused = _focus == sc;
            if (changed || _focus == sc) _cockpit.Refresh(sc);
        }

        _cockpitClock -= dt;
        if (_cockpitClock <= 0f || _hudSnap)
        {
            _cockpitClock = 1f / 15f;
            _snap = BuildCockpitSnapshot();
            _cockpit?.SetDim(CockpitMath.PowerDim(_snap.Flight.PowerPermille, _snap.HasReactor || _snap.Flight.PowerProduction > 0));
            refreshAll = true;
        }
        for (int i = 0; i < 3; i++)
        {
            var view = _cockpit.View((CockpitScreen)i);
            view.Snapshot = _snap;
            view.Time = _cockpitTime;
        }
        if (refreshAll) _cockpit.Refresh();
    }

    private CockpitSnapshot BuildCockpitSnapshot()
    {
        if (_syntheticSnapshot is { } fake) return fake;
        var snap = new CockpitSnapshot { HudMode = CurrentHudMode, Online = Net.HasPlayer(Player) && !Net.ShipLost(Player) };
        if (!snap.Online) return snap;
        int ship = ShipStruct;
        var p = Net.GetPlayerInfo(Player);
        var st = Net.PowerStatus(ship);

        // The speed, what the engines can do now and the demanded speed are the flight-feel numbers (Play.FlightFeel.cs): the same ones the HUD gauge shows.
        bool boosting = Net.IsBoosting(Player);
        bool canBoost = _content.World is { BoostPercent: > 100 } && Net.CanFly(Player);
        float top = MathF.Max(_topCps, 0.01f);
        float thrust = Math.Clamp(_targetCps / top, 0f, 1f);

        bool reactor = HasReactor(ship);
        long fuelLeft = PowerFormat.TimeLeftTicks(st, BurnInterval);
        int maxHull = Net.ThreatActive ? Net.ShipMaxHull : 0;
        int holdUsed = Net.HoldTotal(Player), holdCap = Net.HoldCapacity(Player);
        snap.Flight = new FlightDisplayData(_speedCps, top, thrust, boosting, canBoost, _targetShown, Net.ThreatActive ? p.Hull : 0, maxHull,
            st.Production, st.Demand, st.SatisfactionPermille, st.HydrogenInHold, fuelLeft, holdUsed, holdCap);
        snap.HasReactor = reactor;

        // attitude and velocity, in the ship's own axes (not the head's)
        var b = ShipBasis;
        var shipCam = new CameraBasis(V(-b.Z), V(b.X), V(b.Y), Mathf.DegToRad(SettingsStore.Current.Fov));
        snap.Axes = NavFrame.Project(shipCam);
        var vel = _velCps; // the velocity of the flight-feel state (cells per second)
        snap.Velocity = CockpitMath.Velocity(vel.Dot(b.X), vel.Dot(b.Y), vel.Dot(-b.Z), 0.2f);

        // systems
        int modules = 0, damaged = 0;
        foreach (var (key, kind) in Net.GetStructure(ship).Modules)
        {
            modules++;
            if (!Net.ThreatActive) continue;
            int max = Net.ModuleMaxHp(kind);
            if (max > 0 && Net.ModuleHp(ship, key.Mx, key.My, key.Mz) < max) damaged++;
        }
        int system = Net.CurrentSystem(Player);
        int enemies = 0;
        bool threatNow = ThreatNowHud();
        if (Net.ThreatActive && system >= 0) enemies = Net.GetSystemThreat(Math.Max(0, system)).Enemies;
        snap.Systems = new SystemsDisplayData(modules, damaged, p.Hull, maxHull, holdUsed, holdCap, 0, threatNow);
        snap.Weapons = new WeaponsDisplayData(HardpointsOn, !HardpointsOn || Net.HasMiningLaser(Player), !HardpointsOn || Net.HasGun(Player), _weaponGroup, p.Armed,
            _laserOn, Net.LaserFiring(Player), Net.ThreatActive, enemies);

        // navigation
        snap.HasGalaxy = Net.Galaxy is not null;
        snap.SystemName = Net.Galaxy is { } gal && system >= 0 ? gal.Systems[system].Name : L("hud.deepSpace");
        snap.Targets = BuildNavTargets(shipCam, system);
        snap.Autopilot = _waypoint is not null;
        snap.DockNear = _dockNear;
        snap.DockAssist = _dockAssist;
        snap.Docked = IsDockedNow;
        snap.ScanReady = Net.ScanCooldown(Player) == 0;
        snap.Jumping = p.JumpRemaining > 0;
        snap.JumpSeconds = (int)MathF.Ceiling(p.JumpRemaining / (float)Units.TicksPerSecond);
        snap.Rebuilding = p.RespawnRemaining > 0;
        return snap;
    }

    private IReadOnlyList<NavTarget> BuildNavTargets(CameraBasis shipCam, int system)
    {
        var list = new List<NavTarget>();
        var ship = ShipPosition(0f);
        foreach (int d in RevealedDeposits())
        {
            if (Net.DepositRemaining(d) <= 0) continue;
            var q = Net.DepositPosition(d);
            var v = new Vector3(q.X, q.Y, q.Z) - ship;
            list.Add(new NavTarget(NavKind.Deposit, d, ItemName(Net.DepositItem(d)), v.Length(), NavFrame.TurnDegrees(V(v), shipCam),
                Icons.ItemId(_content.Items[Net.DepositItem(d)].Id), d == _selectedDeposit));
        }
        EnemyInfo? nearest = null;
        float best = float.MaxValue;
        foreach (var en in Net.Enemies())
        {
            if (system >= 0 && en.System != system) continue;
            float d2 = (new Vector3(en.X, en.Y, en.Z) / CellUnits - ship).LengthSquared();
            if (d2 < best) { best = d2; nearest = en; }
        }
        if (nearest is { } e)
        {
            var v = new Vector3(e.X, e.Y, e.Z) / CellUnits - ship;
            list.Add(new NavTarget(NavKind.Enemy, -1, Loc.Name("enemy", _content.Enemies[e.Type].Id), v.Length(), NavFrame.TurnDegrees(V(v), shipCam), IconCatalog.ForEnemy(_content.Enemies[e.Type].Id)));
        }
        if (_dockNear)
        {
            var (_, station) = CandidatePorts(_dockCand);
            var v = station - ship;
            list.Add(new NavTarget(NavKind.Port, -1, L("cp.nav.port"), v.Length(), NavFrame.TurnDegrees(V(v), shipCam), "status:port"));
        }
        if (_selectedSystem >= 0 && Net.Galaxy is { } galaxy && system >= 0 && _selectedSystem != system && Net.IsSystemKnown(_selectedSystem))
        {
            var from = galaxy.Systems[system];
            var to = galaxy.Systems[_selectedSystem];
            var dir = NavFrame.DirectionTo(new Vec3(from.X, from.Y, from.Z), new Vec3(to.X, to.Y, to.Z));
            float mapDistance = new Vector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z).Length();
            list.Add(new NavTarget(NavKind.Gate, _selectedSystem, L("mark.gate", to.Name), mapDistance, NavFrame.TurnDegrees(dir, shipCam), "status:gate"));
        }
        return NavList.Build(list, 6);
    }

    // ---- pressing buttons ------------------------------------------------------------------------------

    private void CockpitPress(CockpitScreen screen, CockpitButton button)
    {
        AudioManager.PlayUi(Sfx.UiClick);
        switch (button.Action)
        {
            case CockpitAction.ShowPage: _cockpit!.Pages.Show(screen, (CockpitPage)button.Arg); break;
            case CockpitAction.ToggleWeaponGroup:
            case CockpitAction.ToggleArmed: ToggleWeapons(); break;
            case CockpitAction.ToggleMining: ToggleMining(); break;
            case CockpitAction.ToggleAutopilot: ToggleAutopilot(); break;
            case CockpitAction.ToggleDockAssist: DockKey(); break;
            case CockpitAction.CycleHudMode: CycleHudMode(); break;
            case CockpitAction.PrevTarget: CycleDeposit(-1); break;
            case CockpitAction.NextTarget: CycleDeposit(1); break;
            case CockpitAction.SelectTarget: SelectNavRow(button.Arg); break;
            case CockpitAction.Scan: Send(new Scan()); break;
            case CockpitAction.LeaveFocus: SetFocus(null); break;
        }
        _cockpit!.Refresh();
    }

    private void SelectNavRow(int row)
    {
        if (row < 0 || row >= _snap.Targets.Count) return;
        var t = _snap.Targets[row];
        if (!t.Selectable) return;
        _selectedDeposit = t.Id;
        _selMachine = -1;
        ToastProduction(L("toast.depositSelected", ItemName(Net.DepositItem(t.Id))));
        _snap = BuildCockpitSnapshot(); // the highlight follows at once
    }

    /// <summary>
    /// The autopilot button: flies toward the chosen deposit and stops inside the mining range (the same straight run as the guided mining flight); pressed again
    /// it stops. The thrust it sends is the ordinary SetThrust.
    /// </summary>
    private void ToggleAutopilot()
    {
        if (_waypoint is not null)
        {
            _waypoint = null;
            SendThrust((0, 0, 0), force: true);
            ToastLogistics(L("toast.approachCancelled"));
            return;
        }
        if (Standing) { ToastLogistics(L("toast.sitToFly")); return; }
        if (!Net.IsDeposit(_selectedDeposit) || !Net.IsDepositRevealed(_selectedDeposit)) { ToastLogistics(L("cp.nav.noPick")); return; }
        var q = Net.DepositPosition(_selectedDeposit);
        var ship = ShipPosition(0f);
        var to = new Vector3(q.X, q.Y, q.Z) - ship;
        float stop = PlayerRules.MineRangeCells * 0.6f;
        if (to.Length() <= stop) { ToastLogistics(L("toast.depositSelected", ItemName(Net.DepositItem(_selectedDeposit)))); return; }
        _waypoint = ship + to.Normalized() * (to.Length() - stop);
    }

    // ---- focus: moving the view in on a screen ---------------------------------------------------------

    private void SetFocus(CockpitScreen? screen)
    {
        if (screen is not null && !ConsoleReady) { ToastLogistics(L("toast.noConsole")); return; }
        _focus = screen;
        _focusOffYaw = _focusOffPitch = 0f;
        if (screen is null) _swallowRelease = false;
    }

    /// <summary>Keys that belong to the displays; true when one was used.</summary>
    private bool CockpitKey(InputEventKey k)
    {
        if (k.Keycode == Key.F3) { CycleHudMode(); return true; }
        if (k.Keycode == Key.F4)
        {
            if (_focus is not null) SetFocus(null);
            else SetFocus(CockpitScreen.Center);
            return true;
        }
        if (_focus is not { } f) return false;
        switch (k.Keycode)
        {
            case Key.Escape: SetFocus(null); return true;
            case Key.Left: SetFocus(CockpitPages.Step(f, -1)); return true;
            case Key.Right: SetFocus(CockpitPages.Step(f, 1)); return true;
            case Key.Up: _cockpit!.Pages.Cycle(f, -1); _cockpit.Refresh(); return true;
            case Key.Down: _cockpit!.Pages.Cycle(f, 1); _cockpit.Refresh(); return true;
        }
        return false;
    }

    /// <summary>
    /// The left button on a display: a button under the crosshair is pressed; otherwise the click moves the view in on that screen. The matching release is
    /// swallowed so it does not select what is behind. Right button leaves the focus.
    /// </summary>
    private bool CockpitMouse(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed && _swallowRelease) { _swallowRelease = false; return true; }
        if (!mb.Pressed || !CockpitPointer || _cockpit is null) return false;
        if (_focus is not null && mb.ButtonIndex == MouseButton.Right) { SetFocus(null); return true; }
        if (mb.ButtonIndex != MouseButton.Left || _tool == ToolMode.Build || _cockpitAim is not { } aim) return false;
        _swallowRelease = true;
        if (_cockpit.ButtonAt(aim.Screen, aim.Pixel) is { } button) CockpitPress(aim.Screen, button);
        else if (_focus != aim.Screen) SetFocus(aim.Screen);
        return true;
    }

    /// <summary>Mouse motion while a display is in focus: moves the view over the screen (a small, clamped offset) instead of steering. True when used.</summary>
    private bool CockpitLook(Vector2 relative)
    {
        if (_focus is null) return false;
        var settings = SettingsStore.Current;
        float k = MouseSensitivity * settings.MouseSensitivity * Math.Clamp(_fovNow / MathF.Max(1f, settings.Fov), 0.2f, 1f) * 0.8f;
        float fov = Mathf.DegToRad(_fovNow > 1f ? _fovNow : settings.Fov);
        float aspect = Math.Max(1f, GetViewport().GetVisibleRect().Size.Aspect());
        float limitPitch = fov * 0.5f * 0.6f, limitYaw = limitPitch * aspect;
        _focusOffYaw = Math.Clamp(_focusOffYaw - relative.X * k, -limitYaw, limitYaw);
        _focusOffPitch = Math.Clamp(_focusOffPitch - relative.Y * k * (settings.InvertMouseY ? -1f : 1f), -limitPitch, limitPitch);
        return true;
    }

    /// <summary>
    /// Per frame, before the camera is placed: turns the head toward the focused screen and narrows the field of view so the screen fills the view;
    /// without focus the field of view eases back (a little wider while boosting). The FOV is eased in every case.
    /// </summary>
    private void UpdateCockpitHead(float dt)
    {
        if (Overview) return;
        var settings = SettingsStore.Current;
        float flightFov = FlightFov(); // the setting plus the flight-feel kick: one field-of-view path, the kick eases itself (Play.FlightFeel.cs)
        float target = flightFov;
        if (_focus is { } f && _cockpit is not null && _cockpit.TryGetCentre(f, out var centre, out _))
        {
            var eye = _camera.GlobalPosition;
            var toScreen = centre - eye;
            float dist = MathF.Max(0.5f, toScreen.Length());
            var local = ShipBasis.Inverse() * (toScreen / dist);
            float ty = MathF.Atan2(-local.X, -local.Z) + _focusOffYaw;
            float tp = MathF.Asin(Math.Clamp(local.Y, -1f, 1f)) + _focusOffPitch;
            _yaw = _hudSnap ? ty : MotionSmoothing.DampAngle(_yaw, ty, 10f, dt);
            _pitch = _hudSnap ? tp : MotionSmoothing.Damp(_pitch, tp, 10f, dt);
            var size = _cockpit.WorldSize(f);
            float aspect = Math.Max(1f, GetViewport().GetVisibleRect().Size.Aspect());
            const float fill = 0.70f; // how much of the view the screen takes along its tighter side
            float half = MathF.Max(size.Y * 0.5f / fill, size.X * 0.5f / fill / aspect) * 1.0f;
            target = Math.Clamp(Mathf.RadToDeg(2f * MathF.Atan(half / dist)), 18f, flightFov);
        }
        else _focusOffYaw = _focusOffPitch = 0f;
        if (_fovNow <= 0f) _fovNow = flightFov;
        // Only the focus zoom is eased here; with no display in focus the view follows the flight field of view exactly once it is back from a zoom.
        bool zooming = _focus is not null || _fovNow < flightFov - 0.05f;
        _fovNow = _hudSnap || !zooming ? target : MotionSmoothing.Damp(_fovNow, target, 8f, dt);
        _camera.Fov = _fovNow;
    }
}
