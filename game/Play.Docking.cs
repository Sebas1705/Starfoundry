using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Docking at a station (ADR 0029): the contextual guidance (a port marker, the guide lights and the dock key row only while a free port of
/// one of your stations is close), the assisted approach, the animated settling of the ship into the port, undocking with a push-off, the
/// two transfer keys, and walking across the airlock (the body keeps the ship's local frame; the station's modules are added to the walkable
/// volumes while docked).
/// </summary>
public partial class Play
{
    /// <summary>A free port closer than this (cells) shows the marker, the guide lights and the dock row.</summary>
    private const int DockGuideCells = 45;

    /// <summary>The assisted approach docks when the ports are this close (cells); the ship then settles the rest of the way.</summary>
    private const float DockAssistFinishCells = 3.2f;

    private const float DockSlideSeconds = 1.3f;

    private DockCandidate _dockCand;
    private bool _dockNear;
    private bool _dockAssist;

    private Vector3 _dockSlideStart, _dockSlideOffset;
    private float _dockSlideT = 1f;
    private bool _wasDocked, _dockClampPending;
    private Vector3 _lastBasePos;
    private bool _bodyInStation;
    private BodyBox[] _stationBodyVolumes = Array.Empty<BodyBox>();
    private bool _transferToast;

    private static Vector3 Cells((long X, long Y, long Z) u) => new Vector3(u.X, u.Y, u.Z) / Units.CellUnits;

    /// <summary>Docked at a station right now (structure worlds only).</summary>
    private bool IsDockedNow => _structuresOn && Net.IsDocked(Player);

    /// <summary>The direction a station port faces, as a unit vector in the world XZ plane.</summary>
    private static Vector3 FaceVector(Dir face) => face switch
    {
        Dir.PosX => new Vector3(1, 0, 0),
        Dir.NegX => new Vector3(-1, 0, 0),
        Dir.PosZ => new Vector3(0, 0, 1),
        _ => new Vector3(0, 0, -1),
    };

    /// <summary>The ship's port and the station port of the current candidate, in world cells.</summary>
    private (Vector3 Ship, Vector3 Station) CandidatePorts(DockCandidate c)
    {
        var ship = Cells(Net.PortWorldPoint(ShipStruct, c.ShipMx, c.ShipMy, c.ShipMz, DockRules.Opposite(c.Face)));
        var station = Cells(Net.PortWorldPoint(c.Station, c.Mx, c.My, c.Mz, c.Face));
        return (ship, station);
    }

    // ---- per frame -------------------------------------------------------------------------------------

    /// <summary>
    /// Per frame, before the camera is placed: finds the port to guide to (none while docked or without a ship), lights its guide lights and
    /// drives the settling animation of a ship that just docked.
    /// </summary>
    private void UpdateDock(float dt)
    {
        if (!_structuresOn || !Net.HasPlayer(Player) || ShipStruct < 0) { _dockNear = false; _dockAssist = false; _stationView.SetGuide(-1, default); return; }
        bool docked = Net.IsDocked(Player);
        _dockNear = !docked && Net.TryFindDockCandidate(Player, DockGuideCells, out _dockCand);
        if (!_dockNear) _dockAssist = false;
        _stationView.SetGuide(_dockNear ? _dockCand.Station : -1, _dockNear ? (_dockCand.Mx, _dockCand.My, _dockCand.Mz) : default);

        // The ship snaps to the port pose in the simulation; the view slides it there.
        var basePos = ShipBasePosition(0f);
        if (docked && !_wasDocked)
        {
            _dockSlideStart = _lastBasePos - basePos;
            _dockSlideT = _dockSlideStart.Length() > 0.05f ? 0f : 1f;
            _dockClampPending = true;
        }
        _wasDocked = docked;
        _lastBasePos = basePos + _dockSlideOffset;
        if (_dockSlideT < 1f)
        {
            _dockSlideT = MathF.Min(1f, _dockSlideT + dt / DockSlideSeconds);
            float k = 1f - _dockSlideT;
            _dockSlideOffset = _dockSlideStart * (k * k * k);
        }
        else _dockSlideOffset = Vector3.Zero;
        if (_dockClampPending && _dockSlideT >= 1f)
        {
            _dockClampPending = false;
            _audio?.Play(Sfx.DockClamp);
            ToastProduction(L("toast.docked"), 5.0);
        }
        UpdateAirlockCrossing();
    }

    /// <summary>The marker over the port being guided to, only while it is close (see <see cref="DockGuideCells"/>).</summary>
    private void DockMarker(Action<string, Color, Vector3, float> add)
    {
        if (!_dockNear) return;
        var (_, station) = CandidatePorts(_dockCand);
        add(L("mark.dockPort"), _dockCand.InRange && _dockCand.SlowEnough && _dockCand.Aligned ? new Color(0.35f, 1f, 0.55f) : new Color(0.3f, 0.9f, 0.85f), station, 2f);
    }

    /// <summary>What the key panel needs to know about docking.</summary>
    private (DockHint Hint, bool Ready, int DistanceCells, int SpeedPercent, int Used, int Capacity) DockKeyInfo()
    {
        if (!_structuresOn || !Net.HasPlayer(Player)) return (DockHint.None, false, 0, 0, 0, 0);
        if (Net.TryGetDock(Player, out var d)) return (DockHint.Docked, false, 0, 0, Net.StockTotal(d.Station), Net.StockCapacity(d.Station));
        if (!_dockNear) return (DockHint.None, false, 0, 0, 0, 0);
        int speed = (int)(_dockCand.SpeedMilli * 100L / (DockRules.MaxSpeedUnitsPerTick * 1000L));
        return (DockHint.Near, _dockCand.InRange && _dockCand.SlowEnough && _dockCand.Aligned, (int)(_dockCand.DistanceUnits / Units.CellUnits), speed, 0, 0);
    }

    // ---- keys ------------------------------------------------------------------------------------------

    /// <summary>U: undock when docked; dock when a port is in range and the ship is slow enough; otherwise start (or cancel) the assisted approach.</summary>
    private void DockKey()
    {
        if (!_structuresOn || !Net.HasPlayer(Player)) return;
        if (Net.TryGetDock(Player, out var dock))
        {
            if (_bodyInStation) { ToastLogistics(L("toast.undockAboard")); return; }
            var face = dock.StationFace;
            Send(new Undock());
            // Push off along the port's axis so the ship clears the station by itself.
            _waypoint = ShipPosition(0f) + FaceVector(face) * 14f;
            return;
        }
        if (Standing) { ToastLogistics(L("toast.sitToFly")); return; }
        if (!_dockNear) { ToastLogistics(L("toast.noPortNear", DockGuideCells)); return; }
        if (_dockAssist) { ToastLogistics(L("toast.dockInProgress")); return; } // a second F does not cancel it: Esc or flying by hand does
        if (_dockCand.InRange && _dockCand.SlowEnough && _dockCand.Aligned)
        {
            Send(new DockAt(_dockCand.Station, _dockCand.Mx, _dockCand.My, _dockCand.Mz, _dockCand.Face));
            return;
        }
        _dockAssist = true;
        _waypoint = null;
        ToastLogistics(L("toast.approachStarted"));
    }

    /// <summary>Comma: moves everything in the hold except the reactor fuel into the station stock.</summary>
    private void UnloadKey()
    {
        if (!IsDockedNow) return;
        int fuel = _content.FuelItem, cell = _content.FuelCellItem;
        foreach (var item in _content.Items)
            if (item.Index != fuel && item.Index != cell && Net.HoldCount(Player, item.Index) > 0)
                Send(new TransferItems(item.Index, 0, true));
        _transferToast = true;
    }

    /// <summary>Period: moves the stock into the hold as far as it fits, the reactor fuel first.</summary>
    private void LoadKey()
    {
        if (!IsDockedNow || !Net.TryGetDock(Player, out var dock)) return;
        int fuel = _content.FuelItem, cell = _content.FuelCellItem;
        var order = new List<int>();
        if (fuel >= 0) order.Add(fuel);
        if (cell >= 0) order.Add(cell);
        order.AddRange(_content.Items.Select(i => i.Index).Where(i => i != fuel && i != cell));
        foreach (int i in order)
            if (Net.StockCount(dock.Station, i) > 0) Send(new TransferItems(i, 0, false));
        _transferToast = true;
    }

    /// <summary>The result of a docking command: the sounds and the toasts.</summary>
    private void DockResult(CommandResult r)
    {
        switch (r.Command)
        {
            case Undock when r.Ok:
                _audio?.Play(Sfx.DockRelease);
                ToastProduction(L("toast.undocked"), 4.0);
                break;
            case DockAt when r.Ok:
                _dockAssist = false;
                break;
            case DockAt or Undock or TransferItems when !r.Ok:
                if (_dockShot) GD.Print("DOCKSHOT rejected " + r.Command + " " + r.Reject);
                ToastLogistics(Why(r.Reject));
                _audio?.Play(Sfx.UiError);
                _dockAssist = false;
                break;
            case TransferItems when r.Ok:
                if (_dockShot) GD.Print("DOCKSHOT transfer ok");
                _audio?.Play(Sfx.UiConfirm);
                break;
        }
    }

    /// <summary>After a batch of transfer commands, one toast with the stock.</summary>
    private void FlushTransferToast()
    {
        if (!_transferToast) return;
        _transferToast = false;
        if (Net.TryGetDock(Player, out var d)) ToastProduction(L("toast.stockNow", Net.StockTotal(d.Station), Net.StockCapacity(d.Station)), 4.0);
    }

    // ---- assisted approach ------------------------------------------------------------------------------

    /// <summary>
    /// The thrust of the assisted approach. Far away it flies at full speed to a staging point on the port's axis, then down the axis at a
    /// speed that falls with the distance (never above the docking limit once close), and docks when the ports nearly meet.
    /// </summary>
    private (int X, int Y, int Z) DockAssistThrust()
    {
        if (!_dockAssist || !_dockNear) { _dockAssist = false; return (0, 0, 0); }
        var c = _dockCand;
        var (sp, tp) = CandidatePorts(c);
        var outv = FaceVector(c.Face);
        var rel = sp - tp;
        float along = rel.Dot(outv);
        float lateral = (rel - outv * along).Length();
        float dist = rel.Length();

        if (dist <= DockAssistFinishCells && c.SlowEnough && c.Aligned)
        {
            if (_world.PendingCommands == 0) Send(new DockAt(c.Station, c.Mx, c.My, c.Mz, c.Face));
            return (0, 0, 0);
        }

        bool staging = along < 10f && lateral > 2.0f;
        var target = staging ? tp + outv * 14f : tp;
        var to = target - sp;
        float d = to.Length();
        float fullUnits = MathF.Max(1f, Net.ShipSpeedUnitsPerTick(Player));
        float want = staging ? MathF.Min(fullUnits, 3f + d * 1.2f) : 2.5f + d * 0.9f;
        if (!staging && d < 14f) want = MathF.Min(want, DockRules.MaxSpeedUnitsPerTick - 1f);
        float fraction = Math.Clamp(want / fullUnits, 0.05f, 1f);
        return d < 0.05f ? (0, 0, 0) : ToThrust(to / d * fraction);
    }

    // ---- walking across the airlock ----------------------------------------------------------------------

    /// <summary>
    /// The walkable volumes while docked: the ship's modules and the station's, in the ship's local cells. The station is a separate
    /// structure placed at its own origin, so its modules are shifted by the difference of the two origins (a whole number of cells).
    /// </summary>
    private BodyBox[] DockedBodyVolumes(out BodyBox[] stationOnly)
    {
        var ship = ShellVolumes(ShipStruct);
        stationOnly = Array.Empty<BodyBox>();
        if (!Net.TryGetDock(Player, out var d) || !Net.StructureExists(d.Station)) return ship;
        var off = StationView.OriginCells(Net, d.Station) - StationView.OriginCells(Net, ShipStruct);
        stationOnly = ShellVolumes(d.Station)
            .Select(b => b with { MinX = b.MinX + off.X, MinY = b.MinY + off.Y, MinZ = b.MinZ + off.Z, MaxX = b.MaxX + off.X, MaxY = b.MaxY + off.Y, MaxZ = b.MaxZ + off.Z }).ToArray();
        return ship.Concat(stationOnly).ToArray();
    }

    /// <summary>
    /// The walkable boxes of a structure, each with the shell the model draws round it (its neighbour and docking mask, the same one
    /// <see cref="StationView"/> builds the module from), so the body is held inside the drawn hull: its chamfers, nose, door frames and walls.
    /// </summary>
    private BodyBox[] ShellVolumes(int structure)
    {
        var st = Net.GetStructure(structure);
        var dock = StationView.DockBits(Net, st);
        ModuleKind? KindAt(int x, int y, int z) => st.Modules.TryGetValue((x, y, z), out var kind) ? kind : null;
        int Mask((int Mx, int My, int Mz) k) => ModuleModels.NeighbourMask(KindAt, k.Mx, k.My, k.Mz) | dock.GetValueOrDefault(k);
        return BodyMotion.Volumes(st.Modules, Mask, st.Kind == StructureKind.Ship);
    }

    private static bool PointInBoxes(BodyBox[] boxes, Vector3 p)
    {
        foreach (var b in boxes)
            if (p.X >= b.MinX && p.X <= b.MaxX && p.Z >= b.MinZ && p.Z <= b.MaxZ && p.Y >= b.MinY - 0.001f && p.Y < b.MaxY) return true;
        return false;
    }

    /// <summary>Crossing the airlock (the body leaves the ship's modules for the station's or back) plays the door sound and says where you are.</summary>
    private void UpdateAirlockCrossing()
    {
        if (!Standing || !IsDockedNow) { _bodyInStation = false; return; }
        bool shipSide = PointInBoxes(BodyMotion.Volumes(Net.GetStructure(ShipStruct).Modules.Keys, StationGrid.ModuleX, StationGrid.ModuleY, StationGrid.ModuleZ), _body);
        bool inStation = !shipSide && PointInBoxes(_stationBodyVolumes, _body);
        if (inStation == _bodyInStation) return;
        _bodyInStation = inStation;
        _audio?.Play(Sfx.Airlock);
        ToastLogistics(L(inStation ? "toast.enterStation" : "toast.backAboard"), 3.0);
    }

    // ---- --dock-shot=path --------------------------------------------------------------------------------

    private string? _dockShotPath;
    private bool _dockShot;
    private int _dockShotFrame;

    /// <summary>
    /// <c>--dock-shot=path</c>: a scripted run in a structure sandbox. A station core stands a few cells off the starter ship's flank; it
    /// saves the approach (<c>path</c>: the port marker, the guide lights and the key row), the docked ship from outside
    /// (<c>path.docked.png</c>), the view from the cockpit while docked (<c>path.cockpit.png</c>) and a walk across the airlock into the
    /// station (<c>path.airlock.png</c>, <c>path.station.png</c>), then quits.
    /// </summary>
    private int _dockShotPhase, _dockShotWait;
    private long _dockShotTick;

    /// <summary>The phases wait on the simulation (and a few frames for the view to settle), not on a frame count, so a fast machine gets the same pictures.</summary>
    private void DockShotFrame()
    {
        if (!_structuresOn || ShipStruct < 0) return;
        void Save(string p) { GetViewport().GetTexture().GetImage().SavePng(p); GD.Print("SHOT saved " + p); }
        int ship = ShipStruct;
        _dockShotWait++;
        bool Wait(int frames) { if (_dockShotWait < frames) return true; _dockShotWait = 0; _dockShotPhase++; return false; }
        switch (_dockShotPhase)
        {
            case 0:
            {
                // A station whose +X port faces the ship's -X flank port, 22 cells away, with a second module and a dock module.
                long cu = Units.CellUnits;
                var o = Net.GetStructure(ship);
                int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX - (8 + 22) * cu, o.OriginY, o.OriginZ);
                Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Workshop);
                if (Net.DockPieceRules) Net.AddDockPieceUnchecked(st, 0, 0, 0, Dir.PosX); else Net.AddStructureModuleUnchecked(st, -1, 0, 0, ModuleKind.Dock); // ADR 0047: a docking collar on the core's +X face (a dock module before it)
                Net.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Reactor);
                Net.GiveStock(st, _content.FuelItem, 8);
                _yaw = MathF.PI * 0.5f; _pitch = -0.05f; // look along -X, towards the station
                _dockShotWait = 0; _dockShotPhase = 1;
                break;
            }
            case 1:
                if (Wait(60)) break;
                Save(_dockShotPath!); // the approach: marker, guide lights, key row with the guidance
                _dockAssist = true;   // the assisted approach flies the ship in and docks
                _dockShotTick = Net.Tick;
                break;
            case 2:
                if (IsDockedNow) { _dockShotWait = 0; _dockShotPhase = 3; }
                else if (Net.Tick > _dockShotTick + 30 * 90) { GD.Print("DOCKSHOT assisted approach did not dock"); GetTree().Quit(); }
                else if (_dockShotWait == 100) Save(_dockShotPath + ".approach.png"); // mid-way
                break;
            case 3:
                if (Wait(90)) break; // the settling slide and the clamp
                Save(_dockShotPath + ".cockpit.png");
                _shotOutside = true;
                {
                    var o = Net.GetStructure(ship);
                    var at = new Vector3(o.OriginX, o.OriginY, o.OriginZ) / Units.CellUnits + new Vector3(0f, 1.5f, 6f);
                    _shotCam = (at + new Vector3(8f, 11f, 26f), at);
                }
                break;
            case 4:
                if (Wait(15)) break;
                Save(_dockShotPath + ".docked.png"); // from outside: the airlock joined, the guide lights off
                _shotOutside = false;
                StandUpForShot();
                _body = new Vector3(5f, 0f, 4f);
                _yaw = MathF.PI * 0.5f; _pitch = -0.08f;
                break;
            case 5:
                if (Wait(20)) break;
                Save(_dockShotPath + ".walk.png"); // on foot in the cockpit module, the airlock ahead
                _body = new Vector3(-0.6f, 0f, 4f); // in the doorway
                break;
            case 6:
                if (Wait(20)) break;
                Save(_dockShotPath + ".airlock.png");
                _body = new Vector3(-6f, 0f, 4f); // inside the station's core module
                break;
            case 7:
                if (Wait(30)) break;
                Save(_dockShotPath + ".station.png");
                GD.Print($"DOCKSHOT inStation={_bodyInStation} volumes={_bodyVolumes.Length} stock={(Net.TryGetDock(Player, out var d) ? Net.StockTotal(d.Station) : -1)}");
                _body = new Vector3(2.0f, 0f, 4f);
                break;
            case 8:
                if (Wait(10)) break;
                Send(new TransferItems(_content.FuelItem, 0, true));
                _dockShotTick = Net.Tick; _dockShotWait = 0; _dockShotPhase = 9;
                break;
            case 9:
                if (Net.Tick < _dockShotTick + 4) break; // the simulation steps at 30 ticks a second, slower than a fast machine draws frames
                GD.Print($"DOCKSHOT unloaded hydrogen stock={(Net.TryGetDock(Player, out var d2) ? Net.StockCount(d2.Station, _content.FuelItem) : -1)}");
                Save(_dockShotPath + ".hud.png");
                GetTree().Quit();
                break;
        }
    }

    private void StandUpForShot()
    {
        _seated = true;
        ToggleSeat();
    }
}
