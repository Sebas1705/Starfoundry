using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--drone-shot=path</c>: a scripted run of the structures campaign for screenshots of the drone automation. It adds a drone bay to the
/// ship through the sim (no research or cost), assigns the nearest deposit within range, steps the clock until the bay works and saves
/// <c>path</c> (outside, drones in flight), <c>path.cockpit.png</c> (from the pilot seat, looking at the deposit) and
/// <c>path.module.png</c> (the bay model close). Quits after the last image. Option: --shot-ticks=N (ticks stepped before the first image).
/// </summary>
public partial class Play
{
    private string? _droneShotPath;
    private int _dsPhase;
    private long _dsTick0;
    private (Vector3 From, Vector3 At)? _shotCam;
    private int _dsBay = -1;

    private void DroneShotThink()
    {
        if (ShipStruct < 0 || PsSaving || _world.PendingCommands > 0) return;
        string p = _droneShotPath!;
        int ship = ShipStruct;
        switch (_dsPhase)
        {
            case 0: // add the bay on the +X side of the first module, pick the nearest deposit in range
            {
                var st = Net.GetStructure(ship);
                var (mx, my, mz) = st.Modules.Keys.First();
                (int, int, int)[] around = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, 0, 1), (0, 0, -1) };
                foreach (var (dx, dy, dz) in around)
                {
                    var c = (mx + dx, my + dy, mz + dz);
                    if (st.Modules.ContainsKey(c) || !StructureGrid.IsValidLocalModule(c.Item1, c.Item2, c.Item3)) continue;
                    Net.AddStructureModuleUnchecked(ship, c.Item1, c.Item2, c.Item3, ModuleKind.DroneBay);
                    break;
                }
                int dep = Enumerable.Range(0, Net.DepositCount)
                    .Where(d => DepositUsable(d) && Net.DepositInDroneRange(ship, d))
                    .OrderBy(d => { var q = Net.DepositPosition(d); return (View.FromCells(q.X, q.Y, q.Z) - ShipPosition(0f)).LengthSquared(); })
                    .DefaultIfEmpty(-1).First();
                GD.Print($"DRONE SHOT bays={Net.DroneBays(ship)} deposit={dep}");
                if (dep < 0) { GetTree().Quit(); return; }
                _selectedDeposit = dep;
                AssignDronesKey(); // the same path as the F7 key
                _dsTick0 = _world.Tick;
                _dsPhase = 1;
                break;
            }
            case 1: // let the bay run, then the outside image
                if (_world.Tick - _dsTick0 < 150) break;
                {
                    var s = Net.DroneStatus(ship);
                    GD.Print($"DRONE SHOT status working={s.Working} rate={s.RatePerMinute} deposit={s.Deposit} power={s.PowerOk} flying={Starfoundry.Ui.DroneFormat.VisibleDrones(s)}");
                    var origin = _stationView.StructureRoot(ship)?.Position ?? Vector3.Zero;
                    var dp = Net.DepositPosition(s.Deposit);
                    var target = View.FromCells(dp.X, dp.Y, dp.Z);
                    var bay = origin + (_droneFlockBay(ship) ?? new Vector3(4f, 2f, 4f));
                    var mid = bay.Lerp(target, 0.5f);
                    var side = (target - bay).Cross(Vector3.Up).Normalized();
                    float dist = (target - bay).Length();
                    _shotCam = (mid + side * (dist * 0.45f + 6f) + Vector3.Up * (dist * 0.25f + 4f), mid);
                    _shotOutside = true;
                    PsSave(p);
                    _dsPhase = 2;
                }
                break;
            case 2: // the pilot seat, looking at the deposit
            {
                _shotOutside = false;
                var s = Net.DroneStatus(ship);
                var dp = Net.DepositPosition(s.Deposit);
                var eye = ShipPosition(0f) + EyeLocal();
                var to = View.FromCells(dp.X, dp.Y, dp.Z) - eye;
                _yaw = MathF.Atan2(-to.X, -to.Z);
                _pitch = MathF.Asin(to.Y / to.Length());
                _dsTick0 = _world.Tick;
                _dsPhase = 3;
                break;
            }
            case 3:
                if (_world.Tick - _dsTick0 < 20) break;
                PsSave(p + ".cockpit.png");
                _dsPhase = 4;
                break;
            case 4: // the bay model close
            {
                var origin = _stationView.StructureRoot(ship)?.Position ?? Vector3.Zero;
                var bay = origin + (_droneFlockBay(ship) ?? new Vector3(4f, 2f, 4f)) - new Vector3(0, 1.7f, 0);
                _shotCam = (bay + new Vector3(9f, 5.5f, -9.5f), bay + new Vector3(0f, -0.4f, 1.2f));
                _shotOutside = true;
                PsSave(p + ".module.png");
                _dsPhase = 5;
                break;
            }
            case 5:
                GetTree().Quit();
                _dsPhase = 6;
                break;
        }
    }

    /// <summary>The first launch point of a structure's flock in local cells, or null.</summary>
    private Vector3? _droneFlockBay(int structure) =>
        _flocks.TryGetValue(structure, out var f) && f.Bays.Length > 0 ? f.Bays[0] : null;
}
