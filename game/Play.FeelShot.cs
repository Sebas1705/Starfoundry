using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--gun-shot=path</c>: the guns in hand fire along the crosshair with no enemy in the sky. <c>path</c> is the seat with the tracers in the air, <c>path.impact.png</c> the
/// sparks where a shot meets a rock, <c>path.outside.png</c> the ship from outside. <c>--overflow-shot=path</c>: the hold is full and the laser breaks a small rock anyway;
/// <c>path</c> is the seat (the toast says the surplus floats), <c>path.outside.png</c> the loose ore beside the rock (ADR 0036). Both print a line starting with
/// <c>FEELSHOT</c> with what the simulation did.
/// </summary>
public partial class Play
{
    private string? _gunShotPath, _overflowShotPath, _dropShotPath;
    private int _feelPhase, _feelWait;

    private void FeelShotSetup(string[] args)
    {
        _gunShotPath = args.FirstOrDefault(a => a.StartsWith("--gun-shot="))?["--gun-shot=".Length..];
        _overflowShotPath = args.FirstOrDefault(a => a.StartsWith("--overflow-shot="))?["--overflow-shot=".Length..];
        _dropShotPath = args.FirstOrDefault(a => a.StartsWith("--drop-shot="))?["--drop-shot=".Length..];
    }

    private bool FeelShotOn => _gunShotPath is not null || _overflowShotPath is not null || _dropShotPath is not null;

    private void FeelShotFrame()
    {
        if (!FeelShotOn || !_structuresOn || ShipStruct < 0) return;
        void Save(string p) { GetViewport().GetTexture().GetImage().SavePng(p); GD.Print("SHOT saved " + p); }
        _feelWait++;
        bool Wait(int frames) { if (_feelWait < frames) return true; _feelWait = 0; _feelPhase++; return false; }
        long cu = Units.CellUnits;
        int ship = ShipStruct;
        int iron = _content.ItemIndex("core:iron_ore");

        if (_dropShotPath is not null)
        {
            DropShotFrame(Save);
            return;
        }

        if (_gunShotPath is not null)
        {
            switch (_feelPhase)
            {
                case 0:
                {
                    // Two rocks straight ahead of the gun, the near one smaller; nothing else in the sky.
                    var eye = AimOrigin();
                    int ex = (int)MathF.Round(eye.X), ey = (int)MathF.Round(eye.Y), ez = (int)MathF.Round(eye.Z);
                    Net.AddDeposit(iron, 700, ex, ey, ez - 24); // on the crosshair ray: the shots converge on it
                    Net.AddDeposit(iron, 2500, ex + 9, ey + 3, ez - 46);
                    _yaw = 0f; _pitch = 0f;
                    _weaponGroup = WeaponGroup.Guns;
                    SetLaser(false);
                    _speed = 2;
                    _feelPhase = 1; _feelWait = 0;
                    break;
                }
                case 1:
                    if (Wait(4)) break;
                    _feelShotFire = true; // the trigger is held: UpdateGuns sends the aim and arms the guns
                    if (ComputeAim(out var aim, out _)) GD.Print($"FEELSHOT aim {aim} converge {ConvergeCells(AimOrigin(), AimDirection()):F1} cells");
                    break;
                case 2:
                    // Wait for tracers in the air and the first impact on the near rock.
                    if (Net.ProjectileCount >= 2 && _feelWait > 2) { Save(_gunShotPath); _feelWait = 0; _feelPhase = 3; }
                    else if (_feelWait > 400) { var pi = Net.GetPlayerInfo(Player); GD.Print($"FEELSHOT no tracers armed={pi.Armed} aim={Net.AimOf(Player)} tick={Net.Tick} legacy={Net.Rules.LegacyFeel} guns={Net.Guns(Player).Count} cd={Net.GetPlayerInfo(Player).Hull} pending={_world.PendingCommands} last={_world.LastResults.LastOrDefault()?.Reject}"); GetTree().Quit(); }
                    break;
                case 3:
                    if (Net.Impacts.Count > 0 && Net.Impacts.Any(i => i.Kind == ImpactKind.Deposit))
                    {
                        GD.Print($"FEELSHOT impact {Net.Impacts[0]} projectiles {Net.ProjectileCount}");
                        _feelWait = 0; _feelPhase = 4;
                    }
                    else if (_feelWait > 300) { GD.Print("FEELSHOT no impact"); _feelWait = 0; _feelPhase = 4; }
                    break;
                case 4:
                    if (Wait(3)) break;
                    Save(_gunShotPath + ".impact.png");
                    _shotOutside = true;
                    {
                        var o = Net.GetStructure(ship);
                        var at = View.Cells(o.OriginX, o.OriginY, o.OriginZ) + new Vector3(4f, 5f, -14f);
                        _shotCam = (at + new Vector3(15f, 7f, 22f), at);
                    }
                    break;
                case 5:
                    if (Wait(6)) break;
                    Save(_gunShotPath + ".outside.png");
                    GD.Print($"FEELSHOT done shots={Net.AmmoFired} projectiles={Net.ProjectileCount} vel=({Net.GetPlayerInfo(Player).VelX},{Net.GetPlayerInfo(Player).VelY},{Net.GetPlayerInfo(Player).VelZ})");
                    GetTree().Quit();
                    break;
            }
            return;
        }

        switch (_feelPhase)
        {
            case 0:
            {
                // A full hold and a small rock straight ahead of the nose laser.
                int room = Net.HoldCapacity(Player) - Net.HoldTotal(Player);
                if (room > 0) Net.GiveItems(Player, _content.ItemIndex("core:carbon"), room);
                int dep = Net.AddDeposit(iron, 18, 500, 0, 500);
                var laser = Net.MiningLasers(Player).First();
                var o = Net.GetStructure(ship);
                var (lx, ly, lz) = (laser.X - o.OriginX, laser.Y - o.OriginY, laser.Z - o.OriginZ);
                // Park so that the laser is 7 cells in front of the rock (it looks along -z) and 2 above it.
                Net.SetStructureOrigin(ship, 500L * cu - lx, 2L * cu - ly, 507L * cu - lz);
                _yaw = 0f; _pitch = 0f;
                _speed = 4;
                _overflowDeposit = dep;
                Send(new StartMining(dep));
                _feelPhase = 1; _feelWait = 0;
                break;
            }
            case 1:
                if (Net.PickupCount > 0 && Net.DepositRemaining(_overflowDeposit) < 14 && _feelWait > 10) { _feelWait = 0; _feelPhase = 2; }
                else if (_feelWait > 1500) { GD.Print($"FEELSHOT no pickups remaining={Net.DepositRemaining(_overflowDeposit)} mining={Net.MiningDeposit(Player)}"); GetTree().Quit(); }
                break;
            case 2:
                if (Wait(10)) break;
                Save(_overflowShotPath!);
                _shotOutside = true;
                {
                    var p = Net.DepositPosition(_overflowDeposit);
                    var at = View.FromCells(p.X, p.Y, p.Z);
                    _shotCam = (at + new Vector3(-10f, 4f, 3f), at + new Vector3(0f, 0f, 3f));
                }
                break;
            case 3:
                if (Wait(10)) break;
                Save(_overflowShotPath + ".outside.png");
                GD.Print($"FEELSHOT overflow hold={Net.HoldTotal(Player)}/{Net.HoldCapacity(Player)} pickups={Net.PickupCount} items={string.Join(",", Net.Pickups().Select(k => k.Count))} rock left={Net.DepositRemaining(_overflowDeposit)}");
                GetTree().Quit();
                break;
        }
    }

    private int _overflowDeposit;
    private bool _feelShotFire;
}
