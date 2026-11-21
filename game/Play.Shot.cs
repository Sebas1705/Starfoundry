using System;
using System.Globalization;
using System.Linq;
using Godot;
using Starfoundry.Sim;

namespace Starfoundry.Game;

/// <summary>
/// <c>--structures-shot=path</c>: a scripted headless run of the structures campaign for screenshots. The ship flies to the nearest iron
/// deposit, mines for a while (the starter factory fills its belts), flies again, and then two images are saved: <c>path</c> from
/// outside the ship looking at it and its running factory, and <c>path.cockpit.png</c> from the pilot seat.
/// Options: --shot-sandbox (free build), --shot-ticks=N (mining ticks, default 2400), --shot-yaw=deg, --shot-pitch=deg (the cockpit
/// view), --shot-tool=belt|module|station|&lt;machine name&gt;, --shot-kind=&lt;module kind&gt;, --shot-layer=N,
/// --shot-found=dx,dz (found a station that many cells from the ship before the cockpit image, sandbox or enough items),
/// --shot-skip-outside (only the cockpit image), --threats (keep the Remnant on).
/// </summary>
public partial class Play
{
    private bool _shotOutside;
    private int _shotPhase;
    private long _shotTick0;
    private int _shotSaveIn;
    private string? _shotSaveTo;
    private int _shotMiningTicks = 2400;
    private float _shotYaw = 180f, _shotPitch = -35f;
    private string? _shotTool, _shotKind, _shotFound;
    private bool _shotSkipOutside;
    private (int, int, int) _shotThrust;

    private static string? ShotArg(string[] args, string key) =>
        args.FirstOrDefault(a => a.StartsWith(key + "="))?[(key.Length + 1)..];

    private void StructuresShotSetup(string[] args)
    {
        if (ShotArg(args, "--shot-ticks") is { } t && int.TryParse(t, out int ticks)) _shotMiningTicks = Math.Max(0, ticks);
        if (ShotArg(args, "--shot-yaw") is { } y && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw)) _shotYaw = yaw;
        if (ShotArg(args, "--shot-pitch") is { } p && float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch)) _shotPitch = pitch;
        if (ShotArg(args, "--shot-layer") is { } l && int.TryParse(l, out int layer)) _cursorY = layer;
        _shotTool = ShotArg(args, "--shot-tool");
        _shotKind = ShotArg(args, "--shot-kind");
        _shotFound = ShotArg(args, "--shot-found");
        _shotSkipOutside = args.Contains("--shot-skip-outside");
        _wantCapture = false;
        _shotPhase = _powerShotPath is not null || _droneShotPath is not null || _shotMiningTicks == 0 ? 3 : 0;
    }

    private void ShotThrust((int X, int Y, int Z) t)
    {
        if (t == _shotThrust) return;
        _shotThrust = t;
        Send(new SetThrust(t.X, t.Y, t.Z));
    }

    /// <summary>Called before every simulation tick of a <c>--structures-shot</c> run.</summary>
    private void StructuresShotThink()
    {
        if (_powerShotPath is not null) { PowerShotThink(); return; }
        if (_droneShotPath is not null) { DroneShotThink(); return; }
        if (_world.PendingCommands > 0 || ShipStruct < 0) return;
        switch (_shotPhase)
        {
            case 0: // fly to the nearest iron deposit and start mining
            {
                int iron = _content.ItemIndex("core:iron_ore");
                int dep = Enumerable.Range(0, Net.DepositCount)
                    .Where(d => Net.DepositItem(d) == iron && Net.IsDepositRevealed(d))
                    .OrderBy(d => { var q = Net.DepositPosition(d); return (new Vector3(q.X, q.Y, q.Z) - ShipPosition(0f)).LengthSquared(); })
                    .DefaultIfEmpty(-1).First();
                if (dep < 0) { _shotPhase = 3; break; }
                if (Net.InMineRange(Player, dep))
                {
                    ShotThrust((0, 0, 0));
                    Send(new StartMining(dep));
                    _shotTick0 = _world.Tick;
                    _shotPhase = 1;
                    break;
                }
                var q2 = Net.DepositPosition(dep);
                var to = new Vector3(q2.X, q2.Y, q2.Z) - ShipPosition(0f);
                ShotThrust(ToThrust(to.Normalized()));
                break;
            }
            case 1: // mine while the starter factory works
                if (_world.Tick - _shotTick0 >= _shotMiningTicks)
                {
                    _shotTick0 = _world.Tick;
                    _shotPhase = 2;
                }
                break;
            case 2: // fly again so the capture shows a moving ship with items on its belts
                ShotThrust(ToThrust(new Vector3(0.6f, 0.3f, -0.7f)));
                if (_world.Tick - _shotTick0 >= 60) _shotPhase = 3;
                break;
            case 3: // the outside image
                if (_shotSkipOutside) { _shotPhase = 4; break; }
                _shotOutside = true;
                _shotSaveTo = _structuresShotPath;
                _shotSaveIn = 10;
                _shotPhase = 40; // waiting for the save
                break;
            case 4: // back to the seat: stop, pick the tool and the view for the cockpit image
                _shotOutside = false;
                ShotThrust((0, 0, 0));
                _yaw = Mathf.DegToRad(_shotYaw);
                _pitch = Mathf.DegToRad(_shotPitch);
                if (_shotFound is not null && _structuresOn && !FoundShotStation()) break; // retries until it worked
                ShotSelectTool();
                _shotTick0 = _world.Tick;
                _shotPhase = 5;
                break;
            case 5:
                if (_world.Tick - _shotTick0 >= 20)
                {
                    _shotSaveTo = _structuresShotPath + ".cockpit.png";
                    _shotSaveIn = 10;
                    _shotPhase = 50;
                }
                break;
        }
    }

    private bool _shotFoundSent;

    /// <summary>Founds a station near the ship (--shot-found=dx,dz in cells from the ship origin); true once it exists.</summary>
    private bool FoundShotStation()
    {
        if (Net.Structures().Any(s => s.Kind == StructureKind.Station)) return true;
        if (_shotFoundSent) return true; // sent on an earlier tick; whatever the sim said, carry on with the image
        _shotFoundSent = true;
        var parts = _shotFound!.Split(',');
        int dx = 12, dz = 16;
        if (parts.Length == 2) { int.TryParse(parts[0], out dx); int.TryParse(parts[1], out dz); }
        var o = ShipPosition(0f);
        Send(new FoundStation((int)MathF.Round(o.X) + dx, (int)MathF.Round(o.Y), (int)MathF.Round(o.Z) + dz));
        return false;
    }

    private void ShotSelectTool()
    {
        if (_shotKind is not null && Enum.TryParse<ModuleKind>(_shotKind, true, out var kind)) _moduleKind = kind;
        if (_shotTool is null) return;
        int index = _shotTool switch
        {
            "belt" => 0,
            "module" => _palette.FindIndex(b => b.IsModule),
            "station" => _palette.FindIndex(b => b.IsStation),
            _ => _palette.FindIndex(b => !b.IsBelt && !b.IsModule && !b.IsStation && b.Label.Contains(_shotTool, StringComparison.OrdinalIgnoreCase)),
        };
        if (index >= 0) SelectBuild(index);
    }

    /// <summary>Per frame: saves the pending image once its countdown ends and moves the script on.</summary>
    private void StructuresShotFrame()
    {
        if (_shotSaveIn <= 0 || --_shotSaveIn != 0 || _shotSaveTo is null) return;
        GetViewport().GetTexture().GetImage().SavePng(_shotSaveTo);
        GD.Print("SHOT saved " + _shotSaveTo);
        if (_shotPhase == 40) _shotPhase = 4;
        else GetTree().Quit();
    }
}
