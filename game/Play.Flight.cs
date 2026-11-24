using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Free flight (see docs/prototypes/free-flight.md). In space there is no up: the ship's attitude is a quaternion turned only about the ship's
/// own axes (mouse = pitch and yaw, Z/X or the arrow keys = roll), with no pitch limit, no horizon lock and no levelling. The attitude is a
/// client-side matter for steering (the thrust is a world-space vector the client computes from the attitude); since ADR 0030 the quantised attitude is also sent
/// as SetAttitude so the simulation turns the ship's hit boxes with it (SyncAttitude below). The ship's structure root is turned about the pilot seat, so the seat (and the eye)
/// stay where the simulation puts the ship.
///
/// Walking inside a ship or station keeps an upright yaw/pitch controller: the "head" (<c>_yaw</c>, <c>_pitch</c>) is always relative to the
/// ship's own floor, whatever the ship's attitude is in the world.
/// </summary>
public partial class Play
{
    private System.Numerics.Quaternion _shipQuat = System.Numerics.Quaternion.Identity;
    private const float RollRate = 1.5f; // radians per second while a roll key is held
    private const float DockAlignRate = 3.5f; // how fast an attitude eases back to the port's axes once docked (per second)
    private const float HeadRelaxRate = 9f; // how fast the head returns to the nose when piloting
    private const float LookEase = 24f; // how fast the pending mouse turn is fed into the ship (per second): about 40 ms of lag
    private const float RollEase = 7f; // how fast the roll rate follows the keys (per second)
    private Vector2 _lookPending; // mouse turn not yet applied: x yaw, y pitch (radians)
    private float _rollSpeed; // the current roll rate (radians per second)

    // ---- rotation dynamics (ship inertia): see docs/prototypes/flight-feel.md ----
    private readonly TurnController _turn = new();
    private TurnStats _turnStats = new(90f, 160f);
    private float _turnStatsClock;
    /// <summary>Flight assist as a screenshot or scripted run pins it (the settings file is never touched by those).</summary>
    private bool? _assistShot;

    private ShipInertia Inertia => _inertiaShot ?? SettingsStore.Current.ShipInertia;
    private ShipInertia? _inertiaShot;
    private bool FlightAssistOn => _assistShot ?? SettingsStore.Current.FlightAssist;

    /// <summary>True when the rotation dynamics (not the old 40 ms easing) steer the ship.</summary>
    private bool UsesDynamics => Inertia != ShipInertia.Off;

    /// <summary>The turn limits of the ship as it is now: its modules (mass), the engine research and the inertia setting. Recomputed twice a second.</summary>
    private TurnStats CurrentTurnStats(float dt)
    {
        _turnStatsClock -= dt;
        if (_turnStatsClock > 0f) return _turnStats;
        _turnStatsClock = 0.5f;
        if (ShipStruct < 0 || !Net.HasPlayer(Player)) return _turnStats;
        _turnStats = ClientContent.Handling.StatsFor(Net.GetStructure(ShipStruct).Modules.Values, Net.BonusPercent(BonusKind.EngineSpeed), Net.BonusPercent(BonusKind.EngineAccel), Inertia);
        return _turnStats;
    }

    /// <summary>The flight assist key and the button on the flight display: damp the rotation when the pilot stops turning, or let the ship keep spinning.</summary>
    private void ToggleFlightAssist()
    {
        bool next = !FlightAssistOn;
        if (_assistShot is not null) _assistShot = next;
        else SettingsStore.Set(SettingsStore.Current with { FlightAssist = next });
        ToastLogistics(L(next ? "toast.assistOn" : "toast.assistOff"));
    }


    private Basis ShipBasis => new(new Quaternion(_shipQuat.X, _shipQuat.Y, _shipQuat.Z, _shipQuat.W));

    private Basis HeadBasis => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    /// <summary>The camera's orientation in the world: the ship's attitude, then where the head looks inside the ship.</summary>
    private Basis CameraWorldBasis() => ShipBasis * HeadBasis;

    /// <summary>
    /// True while the mouse steers the ship (and the roll keys work): seated, free, not docked, no build tool in hand and Alt not held. Otherwise the
    /// mouse turns the head inside the ship, upright in the ship's own frame.
    /// </summary>
    private bool Piloting => (_mouseLook || TurnShotActive) && _focus is null && !Standing && !IsDockedNow && _tool != ToolMode.Build && !PanelOpen() && !Input.IsKeyPressed(Key.Alt);

    /// <summary>The ship's root transform: its attitude, turned about the pilot seat so the seat stays at the simulated position.</summary>
    private Transform3D ShipRootTransform(Vector3 shipPos)
    {
        var basis = ShipBasis;
        var pivot = ShipStruct >= 0 ? SeatOffset : Vector3.Zero;
        return new Transform3D(basis, shipPos + pivot - basis * pivot);
    }

    /// <summary>A structure's root transform: identity for the world, the live root otherwise (a station is only translated; the ship is turned).</summary>
    private Transform3D StructXform(int s) =>
        s == 0 ? Transform3D.Identity : _stationView.StructureRoot(s)?.Transform ?? new Transform3D(Basis.Identity, StationView.OriginCells(Net, s));

    private Vector3 StructToWorld(int s, Vector3 local) => StructXform(s) * local;

    /// <summary>The crosshair ray in a structure's own frame (origin and unit direction), for aiming at its cells and machines.</summary>
    private (Vector3 Origin, Vector3 Dir) AimInStruct(int s)
    {
        var inverse = StructXform(s).AffineInverse();
        return (inverse * AimOrigin(), inverse.Basis * AimDirection());
    }

    /// <summary>The camera as the HUD's engine-free maths needs it.</summary>
    private CameraBasis HudCamera()
    {
        var b = _camera.GlobalTransform.Basis;
        return new CameraBasis(V(-b.Z), V(b.X), V(b.Y), Mathf.DegToRad(_camera.Fov));
    }

    /// <summary>Mouse motion: steers the ship while piloting, otherwise turns the head (upright in the ship's frame, clamped).</summary>
    private void LookBy(Vector2 relative)
    {
        _mousePxFrame += relative.Length();
        if (CockpitLook(relative)) return; // a display is in focus: the mouse moves the view over it
        var settings = SettingsStore.Current;
        float sens = MouseSensitivity * settings.MouseSensitivity;
        float vertical = relative.Y * sens * (settings.InvertMouseY ? -1f : 1f);
        if (Piloting)
        {
            // The turn is not applied at once: it joins a pending turn that the attitude control feeds into the ship over a few frames (a little angular inertia).
            if (UsesDynamics) _turn.AddLook(relative.X * sens, -vertical, _turnStats); // rotation dynamics: the mouse sets a desired angular rate (TurnController)
            else if (settings.SmoothLook) _lookPending += new Vector2(relative.X * sens, -vertical);
            else _shipQuat = FlightOrientation.Rotate(_shipQuat, -vertical, relative.X * sens, 0f); // smooth piloting off: the ship follows the mouse at once
            return;
        }
        _yaw -= relative.X * sens;
        _pitch = Mathf.Clamp(_pitch - vertical, -MaxPitch, MaxPitch);
    }

    /// <summary>
    /// Per frame, before the structure roots are placed: roll from the keys, the settle onto a docking port's axes, and the head returning to the
    /// nose while piloting.
    /// </summary>
    private void UpdateAttitude(float dt)
    {
        if (Overview || _demo) { _lookPending = Vector2.Zero; _rollSpeed = 0f; _turn.Reset(); return; }
        if (!Piloting) { _lookPending = Vector2.Zero; _rollSpeed = 0f; _turn.ClearPending(); }
        var stats = CurrentTurnStats(dt);
        if (!UsesDynamics || IsDockedNow || _dockAssist) _turn.Reset(); // the old easing, or a port that takes the ship over: no spin left to carry
        else if (!Piloting) _shipQuat = _turn.Step(_shipQuat, dt, 0f, stats, FlightAssistOn); // not at the controls: the assist (or free spin) goes on
        if (Piloting)
        {
            float roll = ((Input.IsPhysicalKeyPressed(Key.X) || Input.IsPhysicalKeyPressed(Key.Right)) ? 1f : 0f)
                       - ((Input.IsPhysicalKeyPressed(Key.Z) || Input.IsPhysicalKeyPressed(Key.Left)) ? 1f : 0f);
            if (UsesDynamics) _shipQuat = _turn.Step(_shipQuat, dt, roll, stats, FlightAssistOn);
            else
            {
                // The roll rate eases towards the keys (and back to rest when they are released) instead of switching on and off.
                _rollSpeed = SettingsStore.Current.SmoothLook ? Mathf.Lerp(_rollSpeed, roll * RollRate, 1f - MathF.Exp(-RollEase * dt)) : roll * RollRate;
                if (roll == 0f && MathF.Abs(_rollSpeed) < 0.004f) _rollSpeed = 0f;
                if (_rollSpeed != 0f) _shipQuat = FlightOrientation.Rotate(_shipQuat, 0f, 0f, _rollSpeed * dt);
                float lookAmount = 1f - MathF.Exp(-LookEase * dt);
                var lookStep = _lookPending * lookAmount;
                _lookPending -= lookStep;
                if (_lookPending.LengthSquared() < 1e-10f) { lookStep += _lookPending; _lookPending = Vector2.Zero; }
                if (lookStep != Vector2.Zero) _shipQuat = FlightOrientation.Rotate(_shipQuat, lookStep.Y, lookStep.X, 0f);
            }
            float k = _autoFrame.Framed is null ? 1f - MathF.Exp(-HeadRelaxRate * dt) : 0f; // a framed screen keeps the head turned towards it
            _yaw = Mathf.LerpAngle(_yaw, 0f, k);
            _pitch = Mathf.Lerp(_pitch, 0f, k);
            if (MathF.Abs(_yaw) < 1e-3f) _yaw = 0f;
            if (MathF.Abs(_pitch) < 1e-3f) _pitch = 0f;
        }
        if (_dockAssist && !IsDockedNow)
        {
            // The assisted approach turns the ship onto the port's axes as it closes in: the simulation only docks within about 15 degrees of them.
            _shipQuat = FlightOrientation.AngleFromIdentity(_shipQuat) < 0.002f
                ? System.Numerics.Quaternion.Identity
                : FlightOrientation.Align(_shipQuat, 1f - MathF.Exp(-DockAlignRate * 0.6f * dt));
        }
        else if (!Piloting && IsDockedNow)
        {
            // A docking port is axis-aligned: once locked on, the ship eases back onto the port's axes.
            _shipQuat = FlightOrientation.AngleFromIdentity(_shipQuat) < 0.002f
                ? System.Numerics.Quaternion.Identity
                : FlightOrientation.Align(_shipQuat, 1f - MathF.Exp(-DockAlignRate * dt));
        }
    }

    // ---- the simulation's attitude (ADR 0030) ---------------------------------------------------------------

    private readonly AttitudeThrottle _attitudeThrottle = new();
    private bool _attitudeSynced;

    /// <summary>
    /// Keeps the simulation's attitude of the ship equal to the pilot's: the first time a ship exists the client adopts the simulation's attitude
    /// (a loaded game replays its SetAttitude commands), after that the quantised attitude is sent through <see cref="AttitudeThrottle"/>, only
    /// while no other command waits (like the thrust) and never while docked (a docked ship sits on the port's axes).
    /// </summary>
    private void SyncAttitude()
    {
        if (_demo || !_structuresOn || ShipStruct < 0 || !Net.HasPlayer(Player)) return;
        if (!_attitudeSynced)
        {
            _attitudeSynced = true;
            _shipQuat = ShipAttitude.ToQuaternion(Net.AttitudeOf(ShipStruct));
            return;
        }
        if (IsDockedNow || Net.ShipLost(Player) || _world.PendingCommands != 0) return;
        var wanted = ShipAttitude.Quantize(_shipQuat);
        if (_attitudeThrottle.Offer(Net.AttitudeOf(ShipStruct), wanted, Time.GetTicksMsec() / 1000.0) is { } cmd) { Send(cmd); _attitudeCommands++; }
    }
}
