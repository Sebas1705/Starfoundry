using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The player body: seated at the pilot seat (flight, the camera at the seat) or standing and walking inside the ship. The body
/// position is client-only, in local cells of the ship structure (feet on the floor of the module it is over); the camera is the ship
/// root's interpolated position plus the body, so the player keeps their place while the ship flies. Walking inside stations is a
/// later step.
/// </summary>
public partial class Play
{
    private bool _seated = true;
    private Vector3 _body; // feet, local cells of the ship structure
    private BodyBox[] _bodyVolumes = Array.Empty<BodyBox>();
    private int _bodyVolumesVersion = -1;

    private SeatSpots _seatSpots = SeatLocator.Locate(Array.Empty<System.Collections.Generic.KeyValuePair<(int Mx, int My, int Mz), ModuleKind>>());
    private int _seatShip = -2, _seatVersion = -1;

    /// <summary>
    /// The pilot seat of the real Cockpit module of the ship (the first by sorted coordinate; the structure origin when none is left),
    /// cached per structure version so it follows a destroyed or removed cockpit.
    /// </summary>
    private SeatSpots Seat
    {
        get
        {
            int ship = ShipStruct;
            if (ship < 0) return _seatSpots;
            if (ship != _seatShip || _seatVersion != Net.StructureVersion)
            {
                _seatShip = ship;
                _seatVersion = Net.StructureVersion;
                _seatSpots = SeatLocator.Locate(Net.GetStructure(ship).Modules);
            }
            return _seatSpots;
        }
    }

    private Vector3 SeatPosition => FromSys(Seat.Seat);
    private Vector3 StandSpot => FromSys(Seat.Stand);
    private Vector3 SeatOffset => FromSys(Seat.Eye);

    private static System.Numerics.Vector3 Sys(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 FromSys(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private bool CanWalk => _structuresOn && ShipStruct >= 0;

    /// <summary>Standing and walking inside the ship (false while seated or without a ship structure).</summary>
    private bool Standing => !_seated && CanWalk;

    private void RefreshBodyVolumes()
    {
        if (!CanWalk) { _bodyVolumes = Array.Empty<BodyBox>(); return; }
        if (_bodyVolumesVersion == Net.StructureVersion) return;
        _bodyVolumesVersion = Net.StructureVersion;
        _bodyVolumes = BodyMotion.Volumes(Net.GetStructure(ShipStruct).Modules.Keys, StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ);
    }

    /// <summary>E: sit down when standing within reach of the seat; stand up when seated.</summary>
    private void ToggleSeat()
    {
        if (!CanWalk) return;
        if (_seated) StandUp();
        else if (BodyMotion.InSeatReach(Sys(_body), Sys(SeatPosition))) Sit();
        else ToastLogistics(L("toast.seatTooFar"));
    }

    private void StandUp()
    {
        RefreshBodyVolumes();
        _seated = false;
        SetLaser(false);
        _body = StandSpot;
        if (!BodyMotion.Contains(Sys(_body), _bodyVolumes)) _body = FromSys(BodyMotion.Nearest(Sys(_body), _bodyVolumes));
        // Flight is off while standing: the thrust is cleared once and any autopilot run or pending mining flight is dropped.
        _waypoint = null;
        SendThrust((0, 0, 0), force: true);
        Toast(L("toast.stoodUp"));
    }

    private void Sit() => _seated = true;

    /// <summary>Per frame: walks the body from the keys, keeps it inside the ship and hides the seat from the seated camera.</summary>
    private void UpdateBody(float dt)
    {
        int ship = ShipStruct;
        if (ship >= 0) _stationView.SetSeatVisible(ship, !_seated || _shotOutside);
        if (!Standing) return;
        RefreshBodyVolumes();
        if (_bodyVolumes.Length == 0) { Sit(); return; }
        if (!BodyMotion.Contains(Sys(_body), _bodyVolumes)) _body = FromSys(BodyMotion.Nearest(Sys(_body), _bodyVolumes)); // the module under the feet was removed
        if (!_mouseLook) return;
        float forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
        float right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        if (forward == 0 && right == 0) return;
        var fwd = new Vector3(-MathF.Sin(_yaw), 0f, -MathF.Cos(_yaw));
        var wish = fwd * forward + CockpitRight() * right;
        float speed = Input.IsKeyPressed(Key.Shift) ? BodyMotion.RunSpeed : BodyMotion.WalkSpeed;
        var p = BodyMotion.Step(new System.Numerics.Vector3(_body.X, _body.Y, _body.Z), new System.Numerics.Vector3(wish.X, 0f, wish.Z), speed, dt, _bodyVolumes);
        _body = new Vector3(p.X, p.Y, p.Z);
    }

    /// <summary>Camera position inside the ship, in the structure's local cells.</summary>
    private Vector3 EyeLocal() => _seated ? SeatOffset : _body + new Vector3(0f, BodyMotion.EyeHeight, 0f);

    /// <summary>The "E: sit" / "E: stand" text near the crosshair, or empty when there is nothing to do.</summary>
    private string SeatPromptText()
    {
        if (!CanWalk || _demo) return "";
        if (_seated) return ""; // the key panel already says E stands you up
        return BodyMotion.InSeatReach(Sys(_body), Sys(SeatPosition)) ? L("hint.sit") : "";
    }

    // ---- --walk-shot=path ---------------------------------------------------------------------------------

    private string? _walkShotPath;
    private bool _walkShot;
    private int _walkShotFrame;

    /// <summary>
    /// <c>--walk-shot=path</c>: a scripted headless run of the structures campaign: stands up, places the body in the workshop looking at the
    /// machines (<c>path</c>), moves it into the cargo module (<c>path.cargo.png</c>), then sits (<c>path.seat.png</c>) and quits.
    /// </summary>
    private void WalkShotFrame()
    {
        if (!CanWalk) return;
        _walkShotFrame++;
        void Save(string p) { GetViewport().GetTexture().GetImage().SavePng(p); GD.Print("SHOT saved " + p); }
        switch (_walkShotFrame)
        {
            case 5:
                _seated = true; ToggleSeat(); // exercises the real stand-up path
                _body = new Vector3(4f, 0f, 9f);
                _yaw = MathF.PI; _pitch = -0.2f;
                break;
            case 40: Save(_walkShotPath!); break;
            case 41:
                _body = new Vector3(4f, 0f, 20f);
                _yaw = MathF.PI * 0.8f; _pitch = -0.1f;
                break;
            case 80: Save(_walkShotPath + ".cargo.png"); break;
            case 81:
                _body = StandSpot;
                ToggleSeat();
                _yaw = MathF.PI; _pitch = -0.35f;
                break;
            case 120:
                Save(_walkShotPath + ".seat.png");
                GD.Print($"WALKSHOT seated={_seated} volumes={_bodyVolumes.Length}");
                GetTree().Quit();
                break;
        }
    }
}
