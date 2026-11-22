using System;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Free flight (see docs/prototypes/free-flight.md). In space there is no up: the ship's attitude is a quaternion turned only about the ship's
/// own axes (mouse = pitch and yaw, Z/X or the arrow keys = roll), with no pitch limit, no horizon lock and no levelling. The attitude is a
/// client-side matter: the simulation only ever sees the thrust as a world-space vector, which the client computes from the attitude, so the
/// command log, the saves and the replays are unchanged. The ship's structure root is turned about the pilot seat, so the seat (and the eye)
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

    private Basis ShipBasis => new(new Quaternion(_shipQuat.X, _shipQuat.Y, _shipQuat.Z, _shipQuat.W));

    private Basis HeadBasis => new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch);

    /// <summary>The camera's orientation in the world: the ship's attitude, then where the head looks inside the ship.</summary>
    private Basis CameraWorldBasis() => ShipBasis * HeadBasis;

    /// <summary>
    /// True while the mouse steers the ship (and the roll keys work): seated, free, not docked, no build tool in hand and Alt not held. Otherwise the
    /// mouse turns the head inside the ship, upright in the ship's own frame.
    /// </summary>
    private bool Piloting => _mouseLook && !Standing && !IsDockedNow && _tool != ToolMode.Build && !PanelOpen() && !Input.IsKeyPressed(Key.Alt);

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
        var settings = SettingsStore.Current;
        float sens = MouseSensitivity * settings.MouseSensitivity;
        float vertical = relative.Y * sens * (settings.InvertMouseY ? -1f : 1f);
        if (Piloting)
        {
            _shipQuat = FlightOrientation.Rotate(_shipQuat, -vertical, relative.X * sens, 0f);
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
        if (Overview || _demo) return;
        if (Piloting)
        {
            float roll = ((Input.IsPhysicalKeyPressed(Key.X) || Input.IsPhysicalKeyPressed(Key.Right)) ? 1f : 0f)
                       - ((Input.IsPhysicalKeyPressed(Key.Z) || Input.IsPhysicalKeyPressed(Key.Left)) ? 1f : 0f);
            if (roll != 0f) _shipQuat = FlightOrientation.Rotate(_shipQuat, 0f, 0f, roll * RollRate * dt);
            float k = 1f - MathF.Exp(-HeadRelaxRate * dt);
            _yaw = Mathf.LerpAngle(_yaw, 0f, k);
            _pitch = Mathf.Lerp(_pitch, 0f, k);
            if (MathF.Abs(_yaw) < 1e-3f) _yaw = 0f;
            if (MathF.Abs(_pitch) < 1e-3f) _pitch = 0f;
        }
        else if (IsDockedNow)
        {
            // A docking port is axis-aligned: once locked on, the ship eases back onto the port's axes.
            _shipQuat = FlightOrientation.AngleFromIdentity(_shipQuat) < 0.002f
                ? System.Numerics.Quaternion.Identity
                : FlightOrientation.Align(_shipQuat, 1f - MathF.Exp(-DockAlignRate * dt));
        }
    }
}
