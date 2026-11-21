using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--defence-shot=path</c>: a scripted run of the structures campaign for the defence visuals. The starter ship gets a defence
/// module and a station stands nearby; a wave is forced and stepped until the enemies damage a module. Images: <c>path</c> (from
/// outside, the damaged module with its tint, sparks, bar and the toasts), <c>path.cockpit.png</c> (from the seat: vignette, the bar of
/// the module in the crosshair), <c>path.destroyed.png</c> (just after a module fell) and <c>path.station.png</c> (a station core falling,
/// mid collapse). The sim does the first damage; the rest is sped up with the setup hook <see cref="BeltNetwork.HitModule"/>.
/// </summary>
public partial class Play
{
    private string? _defenceShotPath;
    private int _dfPhase;
    private int _dfTicksPerFrame = 12;
    private long _dfT0;
    private int _dfSaveIn;
    private string? _dfSaveTo;
    private (int Mx, int My, int Mz) _dfTarget;
    private int _dfStation = -1;

    private void DefenceShotSetup()
    {
        _wantCapture = false;
        _shotPhase = 3; // the structures-shot script stays out of it
        _cursorY = DefaultStructureLayer;
        int ship = ShipStruct;
        if (ship < 0) return;
        Net.AddStructureModuleUnchecked(ship, 1, 0, 1, ModuleKind.Defence);
        var o = ShipPosition(0f);
        long cu = Units.CellUnits;
        _dfStation = Net.CreateStructure(Player, StructureKind.Station, ((long)o.X + 40) * cu, (long)o.Y * cu, ((long)o.Z + 8) * cu);
        foreach (var (x, z, k) in new[] { (0, 0, ModuleKind.Core), (1, 0, ModuleKind.Workshop), (0, 1, ModuleKind.Defence), (1, 1, ModuleKind.Hull) })
            Net.AddStructureModuleUnchecked(_dfStation, x, 0, z, k);
        Net.ScheduleWave(0);
    }

    /// <summary>A setup hit between ticks: the sim clears its hit list at the next tick, so the game reads it at once (as it does after every tick).</summary>
    private void ShotHit(int structure, int mx, int my, int mz, int damage)
    {
        SnapStructures();
        Net.HitModule(structure, mx, my, mz, damage);
        WatchDefence();
    }

    private void DfSave(string path, int frames)
    {
        _dfSaveTo = path;
        _dfSaveIn = frames;
    }

    private Vector3 DfModuleCentre(int structure, (int Mx, int My, int Mz) k) => StructOrigin(structure) + StationView.Centre(k.Mx, k.My, k.Mz);

    /// <summary>Puts the outside camera on a point, a little off to the side so the module and the beams reaching it are in view.</summary>
    private void DfCamera(Vector3 at, bool towardsEnemy)
    {
        var dir = new Vector3(0f, 0f, -1f);
        var enemies = Net.Enemies();
        if (towardsEnemy && enemies.Count > 0)
        {
            var e = enemies.OrderBy(en => (new Vector3(en.X, en.Y, en.Z) / CellUnits - at).LengthSquared()).First();
            var d = new Vector3(e.X, e.Y, e.Z) / CellUnits - at;
            d.Y = 0f;
            if (d.LengthSquared() > 1f) dir = d.Normalized();
        }
        var side = dir.Cross(Vector3.Up).Normalized();
        _shotCam = (at + side * 22f + Vector3.Up * 9f - dir * 3f, at + dir * 4f);
        _shotOutside = true;
    }

    /// <summary>Called before every simulation tick: waits for the enemies to hurt a module, then lets them work a little longer.</summary>
    private void DefenceShotThink()
    {
        int ship = ShipStruct;
        if (ship < 0 || _dfPhase > 1) return;
        var st = Net.GetStructure(ship);
        if (_dfPhase == 0)
        {
            foreach (var (k, kind) in st.Modules)
                if (Net.ModuleHp(ship, k.Mx, k.My, k.Mz) < Net.ModuleMaxHp(kind)) { _dfTarget = k; _dfT0 = Net.Tick; _dfPhase = 1; break; }
            return;
        }
        if (Net.Tick - _dfT0 < 45) return;
        // The target is hurt: bring it below half so the sparks and the red bar show, then take the picture slowly (one tick a frame).
        if (st.Modules.TryGetValue(_dfTarget, out var tk))
        {
            int max = Net.ModuleMaxHp(tk), hp = Net.ModuleHp(ship, _dfTarget.Mx, _dfTarget.My, _dfTarget.Mz), want = (int)(max * 0.42f);
            if (hp > want) ShotHit(ship, _dfTarget.Mx, _dfTarget.My, _dfTarget.Mz, hp - want);
            // The workshop next to the seat is hurt too, so the crosshair can find a damaged module from the cockpit.
            if (st.Modules.TryGetValue((0, 0, 1), out var wk) && wk == ModuleKind.Workshop && _dfTarget != (0, 0, 1))
            {
                int wmax = Net.ModuleMaxHp(wk), whp = Net.ModuleHp(ship, 0, 0, 1);
                if (whp > (int)(wmax * 0.55f)) ShotHit(ship, 0, 0, 1, whp - (int)(wmax * 0.55f));
            }
            _dfTicksPerFrame = 1;
            DfCamera(DfModuleCentre(ship, _dfTarget), towardsEnemy: true);
            DfSave(_defenceShotPath!, 16);
        }
        _dfPhase = 2;
    }

    /// <summary>Per frame: saves the pending image when its countdown ends and moves the script on.</summary>
    private void DefenceShotFrame()
    {
        if (_defenceShotPath is null || _dfPhase < 2) return;
        if (_dfSaveIn > 0)
        {
            if (--_dfSaveIn > 0) return;
            Save(_dfSaveTo!);
            _dfPhase++;
        }
        int ship = ShipStruct;
        if (ship < 0) { GetTree().Quit(); return; }
        switch (_dfPhase)
        {
            case 3: // from the seat, looking at the damaged module
            {
                _shotOutside = false;
                var eye = ShipPosition(0f) + EyeLocal();
                // Look at the floor of the workshop module beside the seat: the crosshair then rests on a damaged module.
                var d = (DfModuleCentre(ship, (0, 0, 1)) with { Y = _cursorY + ShipPosition(0f).Y + 0.5f } - eye).Normalized();
                _yaw = MathF.Atan2(-d.X, -d.Z);
                _pitch = MathF.Asin(d.Y);
                DfSave(_defenceShotPath + ".cockpit.png", 12);
                _dfPhase = 4;
                break;
            }
            case 5: // a module falls
            {
                var st = Net.GetStructure(ship);
                var victim = st.Modules.Where(m => m.Value != ModuleKind.Cockpit && m.Value != ModuleKind.Reactor && m.Value != ModuleKind.Engine)
                    .Select(m => m.Key).Where(k => k != _dfTarget).DefaultIfEmpty(_dfTarget).First();
                var at = DfModuleCentre(ship, victim);
                _dfTicksPerFrame = 1;
                ShotHit(ship, victim.Mx, victim.My, victim.Mz, 9999);
                DfCamera(at, towardsEnemy: false);
                DfSave(_defenceShotPath + ".destroyed.png", 9);
                _dfPhase = 6;
                break;
            }
            case 7: // a station core falls
            {
                if (_dfStation < 0 || !Net.Structures().Any(s => s.Id == _dfStation)) { GetTree().Quit(); return; }
                var at = StructOrigin(_dfStation) + StationView.Centre(0, 0, 0);
                ShotHit(_dfStation, 0, 0, 0, 9999);
                _shotCam = (at + new Vector3(-26f, 13f, 20f), at + new Vector3(4f, 0f, 2f));
                _shotOutside = true;
                DfSave(_defenceShotPath + ".station.png", 24);
                _dfPhase = 8;
                break;
            }
            case 9:
                GetTree().Quit();
                break;
        }
    }
}
