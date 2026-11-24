using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// How speed is felt (ADR 0033): the ship has inertia, so the view shows it. The state here is smoothed from the simulation's velocity and the
/// thrust it was asked for: the speed in cells per second, what the engines can do now, how hard the ship is accelerating, and from those the
/// field-of-view kick, the engine rumble, the length of the engine flame, the stretch of the dust streaks and the pitch of the engine sound.
/// Everything is client-only (nothing here is saved or hashed). See docs/prototypes/flight-feel.md.
/// </summary>
public partial class Play
{
    private float _targetShown; // the demanded speed as the gauge shows it: eased, so a key press or release does not snap it
    private float _speedCps, _topCps, _targetCps, _accelNorm, _fovKick, _flameLevel, _rumble;
    private Vector3 _velCps;
    private (int X, int Y, int Z) _lastVelMilli;
    private long _lastVelTick = -1;
    private float _accelRaw;

    private ThrottleGauge _speedGauge = null!;
    private VelocityMarkers _velMarkers = null!;

    private const float CellsPerSecondPerMilli = Units.TicksPerSecond / (1000f * Units.CellUnits);

    /// <summary>Base ship speed in cells per second (48 units per tick): the speed the kick and the streaks are scaled by.</summary>
    private const float BaseCps = PlayerRules.MoveUnitsPerTick * Units.TicksPerSecond / (float)Units.CellUnits;

    private void BuildFlightFeelUi()
    {
        _speedGauge = new ThrottleGauge { Visible = true };
        _velMarkers = new VelocityMarkers { Visible = false };
        _uiRoot.AddChild(_velMarkers);
    }

    /// <summary>Per frame, before the camera is set: the smoothed speed, the demand and the acceleration, and what the view does with them.</summary>
    private void UpdateFlightFeel(float dt)
    {
        if (!Net.HasPlayer(Player) || _demo || Overview)
        {
            _speedCps = _topCps = _targetCps = _targetShown = _accelNorm = _fovKick = _flameLevel = _rumble = 0f;
            _velCps = Vector3.Zero;
            return;
        }
        var pi = Net.GetPlayerInfo(Player);
        bool inertia = Net.HasFlightInertia && ShipStruct >= 0;
        float top, accelNormRaw = _accelRaw;
        Vector3 v;
        if (inertia)
        {
            var vm = Net.ShipVelocityMilli(Player);
            v = new Vector3(vm.X, vm.Y, vm.Z) * CellsPerSecondPerMilli;
            top = Net.ShipTopSpeedMilli(Player) * CellsPerSecondPerMilli;
            long tick = Net.Tick;
            if (tick != _lastVelTick)
            {
                long ticks = _lastVelTick < 0 ? 1 : Math.Max(1, tick - _lastVelTick);
                int limit = Math.Max(1, Net.ShipAccelMilli(Player));
                var d = (vm.X - _lastVelMilli.X, vm.Y - _lastVelMilli.Y, vm.Z - _lastVelMilli.Z);
                float change = MathF.Sqrt((float)d.Item1 * d.Item1 + (float)d.Item2 * d.Item2 + (float)d.Item3 * d.Item3) / ticks;
                _accelRaw = accelNormRaw = Math.Clamp(change / limit, 0f, 1.5f);
                _lastVelMilli = vm; _lastVelTick = tick;
            }
        }
        else
        {
            v = (ShipPosition(1f) - ShipPosition(0f)) * Units.TicksPerSecond; // a world without inertia: the speed is the step of the position
            top = ShipStruct >= 0 ? Net.ShipSpeedUnitsPerTick(Player) * Units.TicksPerSecond / (float)Units.CellUnits * (Net.IsBoosting(Player) && _content.World is { } w ? w.BoostPercent / 100f : 1f) : BaseCps;
            accelNormRaw = 0f;
        }
        top = MathF.Max(top, 0.01f);
        float demand = MathF.Sqrt((float)pi.ThrustX * pi.ThrustX + (float)pi.ThrustY * pi.ThrustY + (float)pi.ThrustZ * pi.ThrustZ) / PlayerRules.ThrustScale;
        if (demand == 0f && (pi.MoveX | pi.MoveY | pi.MoveZ) != 0) demand = 1f;
        float jump = Net.JumpRemaining(Player) > 0 ? 0f : 1f;
        _velCps = v;
        _topCps = top;
        _targetCps = Net.CanFly(Player) ? Math.Min(demand, 1f) * top * jump : 0f;
        _targetShown = Mathf.Lerp(_targetShown, _targetCps, 1f - MathF.Exp(-12f * dt));
        float k = 1f - MathF.Exp(-9f * dt);
        _speedCps = Mathf.Lerp(_speedCps, v.Length(), k);
        _accelNorm = Mathf.Lerp(_accelNorm, accelNormRaw, 1f - MathF.Exp(-7f * dt));

        // Field of view: a little wider with speed (more at boost), a little more while the engines push hard.
        float boost = Net.IsBoosting(Player) ? 1f : 0f;
        float cruise = Math.Clamp(_speedCps / BaseCps, 0f, 1f), fast = Math.Clamp((_speedCps - BaseCps) / (3f * BaseCps), 0f, 1f);
        float wantKick = 2.5f * cruise + 5.5f * fast + 1.5f * _accelNorm * boost;
        if (!Piloting) wantKick = 0f;
        _fovKick = Mathf.Lerp(_fovKick, wantKick, 1f - MathF.Exp(-4f * dt));

        // The rumble of the engines: with the push and with the boost, nothing when drifting; none in the headless shots (they compare images).
        float wantRumble = Piloting && !_hudSnap ? Math.Clamp(0.55f * _accelNorm + 0.45f * boost * fast, 0f, 1f) : 0f;
        _rumble = Mathf.Lerp(_rumble, wantRumble, 1f - MathF.Exp(-6f * dt));

        // The engine flame: short when the engines idle, long when they push, longest at boost.
        float wantFlame = Net.CanFly(Player) ? 0.35f + 0.5f * Math.Clamp(demand, 0f, 1f) + 0.45f * Math.Clamp(_accelNorm, 0f, 1f) + 0.6f * boost * Math.Clamp(demand, 0f, 1f) : 0f;
        if (!inertia) wantFlame = 0f; // the old flame rule applies there
        _flameLevel = Mathf.Lerp(_flameLevel, wantFlame, 1f - MathF.Exp(-10f * dt));
    }

    /// <summary>The camera's field of view: the setting plus the kick of the speed (a bigger window would fight the setting's range, so it is capped).</summary>
    private float FlightFov() => Math.Min(SettingsStore.Current.Fov + _fovKick, 110f);

    /// <summary>A small shake of the eye from the engines (added to the impact shake): a few hundredths of a cell at most.</summary>
    private Vector3 RumbleOffset()
    {
        if (_rumble <= 0.002f) return Vector3.Zero;
        float t = (float)_animTime;
        float a = 0.016f * _rumble;
        return new Vector3(MathF.Sin(t * 83f), MathF.Sin(t * 71f + 1.3f), MathF.Sin(t * 97f + 2.1f)) * a;
    }

    /// <summary>The speed readout and the throttle gauge of the flight card, and the prograde and retrograde markers on the view.</summary>
    private void UpdateFlightReadout()
    {
        bool show = !_demo && !Overview && Net.HasPlayer(Player);
        _velMarkers.Visible = show && Piloting && MarkersWanted; // the cockpit-only and minimal modes leave the velocity markers to the flight display
        if (!show) return;
        string cur = _speedCps.ToString("F1", Loc.Culture), top = _topCps.ToString("F1", Loc.Culture);
        _flightSpeed.SetText("status:speed", L("hud.flightSpeedOf", cur, top), UiTheme.Text, Icons.Name("status:speed"));
        _speedGauge.Set(_speedCps / _topCps, _targetShown / _topCps, Net.IsBoosting(Player));
        float accelCps2 = Net.ShipAccelMilli(Player) * (float)Units.TicksPerSecond * Units.TicksPerSecond / (1000f * Units.CellUnits);
        _speedGauge.TooltipText = L("hud.flightGaugeTip", _targetCps.ToString("F1", Loc.Culture), accelCps2.ToString("F1", Loc.Culture));

        // Prograde and retrograde: where the velocity points on the screen (not drawn when still or when the point is behind the camera).
        float speed = _velCps.Length();
        Vector2? pro = null, retro = null;
        float alpha = Math.Clamp((speed - 0.6f) / 2.4f, 0f, 1f);
        if (_velMarkers.Visible && alpha > 0f)
        {
            var dir = _velCps / speed;
            var eye = _camera.GlobalPosition;
            var viewport = GetViewport().GetVisibleRect().Size;
            Vector2? Project(Vector3 d)
            {
                var point = eye + d * 200f;
                if (_camera.IsPositionBehind(point)) return null;
                var s = _camera.UnprojectPosition(point);
                return s.X < 24f || s.Y < 24f || s.X > viewport.X - 24f || s.Y > viewport.Y - 24f ? null : s;
            }
            pro = Project(dir);
            retro = Project(-dir);
        }
        _velMarkers.Set(pro, retro, alpha);
    }
}
