using System;
using System.Linq;
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
    private bool _solidsHadBench;
    private BodySolid[] _bodySolids = Array.Empty<BodySolid>(); // the machines and the workbench: the body walks round them
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
        bool hasBench = BenchBox() is not null;
        if (_bodyVolumesVersion == Net.StructureVersion && hasBench == _solidsHadBench) return; // the workbench appears a frame after the structure
        _solidsHadBench = hasBench;
        _bodyVolumesVersion = Net.StructureVersion;
        // Docked: the station's modules join the walkable volumes (in the ship's local cells), so the body walks through the airlock.
        _bodyVolumes = DockedBodyVolumes(out _stationBodyVolumes);
        _bodySolids = BuildBodySolids();
    }

    /// <summary>
    /// The solid things on the floors of the ship (and, docked, of the station): every machine fills its cell, the workbench its footprint. Belts, plates and
    /// hatches are low enough to walk over (<see cref="BodyMotion.StepHeight"/>) and are not listed.
    /// </summary>
    private BodySolid[] BuildBodySolids()
    {
        int ship = ShipStruct;
        if (ship < 0) return Array.Empty<BodySolid>();
        var list = new System.Collections.Generic.List<BodySolid>();
        Vector3 off = Vector3.Zero;
        int station = -1;
        if (Net.TryGetDock(Player, out var d) && Net.StructureExists(d.Station))
        {
            station = d.Station;
            off = StationView.OriginCells(Net, d.Station) - StationView.OriginCells(Net, ship);
        }
        for (int id = 0; id < Net.MachineIdLimit; id++)
        {
            if (!Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            Vector3 shift = m.Structure == ship ? Vector3.Zero : m.Structure == station ? off : new Vector3(float.NaN, 0, 0);
            if (float.IsNaN(shift.X)) continue;
            var c = CellCenter(m.X, m.Y, m.Z) + shift;
            list.Add(new BodySolid(c.X - 0.5f, c.Y - 0.5f, c.Z - 0.5f, c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f));
        }
        if (BenchBox() is { } bb) list.Add(new BodySolid(bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Min.X + bb.Size.X, bb.Min.Y + bb.Size.Y, bb.Min.Z + bb.Size.Z));
        return list.ToArray();
    }

    private bool _climbJump;

    /// <summary>Which way E climbs when a ladder is within reach: up when only the way up exists or the view looks up, else down; null when there is no ladder.</summary>
    private bool? LadderWay()
    {
        var (up, down) = Stairwell.Reachable(Sys(_body), _bodyVolumes);
        if (up && down) return _pitch > 0.05f;
        if (up) return true;
        return down ? false : null;
    }

    /// <summary>E at a ladder: moves the body to the other deck (the eye glides), or says the landing is blocked. False when there is no ladder within reach.</summary>
    private bool TryClimb()
    {
        RefreshBodyVolumes();
        if (LadderWay() is not { } up) return false;
        if (Stairwell.Try(Sys(_body), up, _bodyVolumes, _bodySolids) is not { } move) { ToastLogistics(L("toast.ladderBlocked")); return true; }
        _body = FromSys(move.Destination);
        _climbJump = true;
        _audio?.Play(Sfx.Airlock);
        return true;
    }

    /// <summary>E: sit down when standing within reach of the seat; stand up when seated.</summary>
    private void ToggleSeat()
    {
        if (!CanWalk) return;
        if (_seated) StandUp();
        else if (TryClimb()) { }
        else if (BodyMotion.InSeatReach(Sys(_body), Sys(SeatPosition))) Sit();
        else ToastLogistics(L("toast.seatTooFar"));
    }

    private void StandUp()
    {
        RefreshBodyVolumes();
        _seated = false;
        _focus = null; // walking: the view is the player's again
        SetLaser(false);
        _body = StandSpot;
        if (!BodyMotion.Contains(Sys(_body), _bodyVolumes, _bodySolids)) _body = FromSys(BodyMotion.Nearest(Sys(_body), _bodyVolumes, _bodySolids));
        // Flight is off while standing: the thrust is cleared once and any autopilot run or pending mining flight is dropped.
        _waypoint = null;
        SendThrust((0, 0, 0), force: true);
        Toast(L("toast.stoodUp"));
    }

    private void Sit() => _seated = true;

    private const float WalkAccel = 34f, WalkDecel = 40f; // cells per second squared: a walk starts and stops over about a tenth of a second
    private System.Numerics.Vector3 _walkVelocity;
    private float _bobPhase;
    private bool _wasSeated = true;
    private readonly EyeBlend _eyeBlend = new();

    /// <summary>Per frame: walks the body from the keys (with a little acceleration), keeps it inside the ship, hides the seat and glides the eye between seat and feet.</summary>
    private void UpdateBody(float dt)
    {
        UpdateBodyWalk(dt);
        bool smooth = SettingsStore.Current.SmoothLook && !_hudSnap;
        bool jumped = _seated != _wasSeated || _climbJump;
        _climbJump = false;
        _wasSeated = _seated;
        if (jumped) _walkVelocity = System.Numerics.Vector3.Zero;
        var wanted = RawEyeLocal();
        var eased = _eyeBlend.Update(Sys(wanted), jumped && smooth, dt, 6.5f);
        _eyeNow = FromSys(eased);
    }

    private Vector3 _eyeNow;

    private void UpdateBodyWalk(float dt)
    {
        int ship = ShipStruct;
        if (ship >= 0) _stationView.SetSeatVisible(ship, !_seated || _shotOutside);
        if (!Standing) { _walkVelocity = System.Numerics.Vector3.Zero; return; }
        RefreshBodyVolumes();
        if (_bodyVolumes.Length == 0) { Sit(); return; }
        if (!BodyMotion.Contains(Sys(_body), _bodyVolumes, _bodySolids)) _body = FromSys(BodyMotion.Nearest(Sys(_body), _bodyVolumes, _bodySolids)); // the module under the feet was removed
        var wishDir = Vector3.Zero;
        float speed = Input.IsKeyPressed(Key.Shift) ? BodyMotion.RunSpeed : BodyMotion.WalkSpeed;
        if (_mouseLook)
        {
            float forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
            float right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
            var fwd = new Vector3(-MathF.Sin(_yaw), 0f, -MathF.Cos(_yaw));
            wishDir = fwd * forward + CockpitRight() * right;
            if (wishDir.LengthSquared() > 1f) wishDir = wishDir.Normalized();
        }
        var wanted = new System.Numerics.Vector3(wishDir.X * speed, 0f, wishDir.Z * speed);
        bool smooth = SettingsStore.Current.SmoothLook && !_hudSnap;
        _walkVelocity = smooth ? MotionSmoothing.Approach(_walkVelocity, wanted, WalkAccel, WalkDecel, dt) : wanted;
        float v = _walkVelocity.Length();
        if (v < 1e-4f) return;
        // BodyMotion.Step takes a direction whose length (up to 1) scales the speed: the velocity over the run speed, moved at the run speed.
        var p = BodyMotion.Step(Sys(_body), _walkVelocity / BodyMotion.RunSpeed, BodyMotion.RunSpeed, dt, _bodyVolumes, _bodySolids);
        _body = new Vector3(p.X, p.Y, p.Z);
        if (smooth) _bobPhase += v * dt * 2.2f;
    }

    /// <summary>The eye where the pilot is (seat or head), without the glide of a stand-up or sit-down.</summary>
    private Vector3 RawEyeLocal() => _seated ? SeatOffset : _body + new Vector3(0f, BodyMotion.EyeHeight, 0f);

    /// <summary>Camera position inside the ship, in the structure's local cells: the eye, gliding between the seat and the standing height, with a faint bob while walking.</summary>
    private Vector3 EyeLocal()
    {
        if (_demo || _hudSnap || !CanWalk) return RawEyeLocal(); // the screenshot runs place the eye exactly
        var eye = _eyeNow == default ? RawEyeLocal() : _eyeNow;
        if (!_seated && SettingsStore.Current.SmoothLook && !_hudSnap) eye.Y += MathF.Sin(_bobPhase) * 0.012f * Math.Clamp(_walkVelocity.Length() / BodyMotion.WalkSpeed, 0f, 1.5f);
        return eye;
    }

    /// <summary>The "E: sit" / "E: stand" text near the crosshair, or empty when there is nothing to do.</summary>
    private string SeatPromptText()
    {
        if (!CanWalk || _demo) return "";
        if (_seated) return ""; // the key panel already says E stands you up
        if (LadderWay() is { } way) return L(way ? "hint.climbUp" : "hint.climbDown");
        return BodyMotion.InSeatReach(Sys(_body), Sys(SeatPosition)) ? L("hint.sit") : "";
    }

    // ---- --walk-shot=path ---------------------------------------------------------------------------------

    private string? _walkShotPath;
    private bool _walkShot;
    private int _walkShotFrame;
    private const float WalkAimYaw = 0f;

    /// <summary>
    /// <c>--walk-shot=path</c>: a scripted headless run of the structures campaign: stands up, places the body in the workshop looking at the
    /// machines (<c>path</c>), moves it into the cargo module (<c>path.cargo.png</c>), then sits (<c>path.seat.png</c>) and quits.
    /// </summary>
    private void WalkShotFrame()
    {
        if (!CanWalk) return;
        _walkShotFrame++;
        if (_walkShotFrame >= 251 && StoryOpen) CloseStory(); // a scenario opens its briefing first
        if (BevelShotFrame()) return;
        if (StairsShotFrame()) return;
        void Save(string p) { GetViewport().GetTexture().GetImage().SavePng(p); GD.Print("SHOT saved " + p); }
        switch (_walkShotFrame)
        {
            case 3:
                // A fabrication bay beside the hull with a small factory on its floor (ADR 0031): smelter, belt, depot, and a second belt line.
                if (Net.GetStructure(ShipStruct).Modules.Values.All(k => k != ModuleKind.Workshop))
                {
                    Net.AddStructureModuleUnchecked(ShipStruct, 1, 0, 1, ModuleKind.Workshop);
                    int smelter = Net.AddMachine(9, 0, 10, _content.MachineIndex("core:smelter"), _content.RecipeIndex("core:smelt_iron"), 0, ShipStruct);
                    int depot = Net.AddMachine(14, 0, 10, _content.MachineIndex("core:depot"), -1, 0, ShipStruct);
                    int line = Net.AddSegment(10, 0, 10, 1, 0, 0, 4, BeltTier.Mk1, ShipStruct);
                    Net.ConnectFromMachine(smelter, line);
                    Net.ConnectToMachine(line, depot);
                    Net.AddSegment(10, 0, 13, 0, 0, 1, 2, BeltTier.Mk2, ShipStruct);
                    _structureVersion = -1;
                }
                break;
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
                _body = new Vector3(11f, 0f, 14.5f); // standing on the bay floor, looking down at the belts with the belt tool in hand
                _seated = false; _yaw = WalkAimYaw; _pitch = -0.55f;
                if (_palette.FindIndex(b => b.IsBelt) is var belt and >= 0) SelectBuild(belt);
                break;
            case 170: Save(_walkShotPath + ".bay.png"); break;
            case 171:
                _tool = ToolMode.Pointer; // no tool: the crosshair on a belt shows its card (right click demolishes)
                _pitch = -0.35f;
                break;
            case 210: Save(_walkShotPath + ".belt.png"); break;
            case 211:
                if (_palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation) is var machine and >= 0) SelectBuild(machine);
                _pitch = -0.2f; // a machine tool aimed at a wall: not on a floor, refused
                break;
            case 250:
                Save(_walkShotPath + ".wall.png");
                break;
            case 251: // the workbench (ADR 0031): plates, standing in the hull module and aiming at the bench with no tool in hand
                if (OS.GetCmdlineUserArgs().Contains("--ui-lang=en")) Loc.Lang = Language.En;
                _tool = ToolMode.Pointer;
                Net.GiveItems(Player, _content.ItemIndex("core:iron_ingot"), 20);
                Net.GiveItems(Player, _content.ItemIndex("core:iron_ore"), 20);
                Send(new SetShipRecipe(_content.RecipeIndex("core:ship_press")));
                if (BenchBox() is { } bb) // stand in the aisle beside the bench, wherever the ship put it, and face it
                {
                    float sign = _benchSpot!.Value.Wall == BenchWall.Left ? 1f : -1f;
                    var centre = bb.Min + bb.Size / 2f;
                    _body = new Vector3(centre.X + sign * 2.9f, bb.Min.Y, centre.Z + 1.9f);
                    _yaw = MathF.Atan2(sign * 2.9f, 1.9f); _pitch = -0.2f;
                }
                break;
            case 300: Save(_walkShotPath + ".bench.png"); break;
            case 301: Send(new SetShipRecipe(_content.RecipeIndex("core:ship_smelt"))); break;
            case 340: Save(_walkShotPath + ".bench.ingot.png"); break;
            case 341: Send(new SetShipRecipe(-1)); break; // bench off: lamps dark, card says Off
            case 360:
                Save(_walkShotPath + ".bench.off.png");
                GetTree().Quit();
                break;
        }
    }
}
