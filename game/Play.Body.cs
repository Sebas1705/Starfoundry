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
        // Docked: the station's modules join the walkable volumes (in the ship's local cells), so the body walks through the airlock.
        _bodyVolumes = DockedBodyVolumes(out _stationBodyVolumes);
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
