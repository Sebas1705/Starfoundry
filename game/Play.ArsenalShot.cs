using System;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--arsenal-shot=path</c>: the ship gets a second Mk1 laser, a Mk2 laser and a Mk2 gun on top of its own laser and gun, and the arsenal bar is saved in the states a player meets:
/// <c>path.laser.png</c> (laser in hand), <c>path.ease.png</c> (the marker halfway to the gun), <c>path.gun.png</c>, <c>path.firing.png</c> (guns armed), <c>path.minimal.png</c> (HUD detail
/// level Minimal: one slot), <c>path.cockpit.png</c> (cockpit-only HUD: the bar is not drawn), <c>path.outside.png</c> (the ship seen from outside, bar on screen). Run it at
/// <c>--resolution 1280x720</c> and <c>1920x1080</c> with <c>--ui-lang=es</c> or <c>en</c>. Prints lines starting with <c>ARSENALSHOT</c>.
/// </summary>
public partial class Play
{
    private string? _arsenalShotPath;
    private int _arsenalPhase, _arsenalWait;

    private void ArsenalShotSetup(string[] args) => _arsenalShotPath = args.FirstOrDefault(a => a.StartsWith("--arsenal-shot="))?["--arsenal-shot=".Length..];

    private void ArsenalShotFrame()
    {
        if (_arsenalShotPath is not { } path || !_structuresOn || ShipStruct < 0) return;
        void Save(string suffix) { GetViewport().GetTexture().GetImage().SavePng(path + suffix); GD.Print("SHOT saved " + path + suffix); }
        _arsenalWait++;
        bool Wait(int frames) { if (_arsenalWait < frames) return true; _arsenalWait = 0; _arsenalPhase++; return false; }
        int ship = ShipStruct;
        long cu = Units.CellUnits;
        switch (_arsenalPhase)
        {
            case 0:
            {
                // Free cells next to the hull for the extra weapons (the shot skips the shipyard).
                var modules = Net.GetStructure(ship).Modules;
                var free = modules.Keys
                    .SelectMany(k => new[] { (k.Mx + 1, k.My, k.Mz), (k.Mx - 1, k.My, k.Mz), (k.Mx, k.My + 1, k.Mz), (k.Mx, k.My - 1, k.Mz), (k.Mx, k.My, k.Mz + 1), (k.Mx, k.My, k.Mz - 1) })
                    .Where(c => !modules.ContainsKey(c)).Distinct().ToList();
                var kinds = new[] { ModuleKind.MiningLaser, ModuleKind.MiningLaserMk2, ModuleKind.GunMk2 };
                for (int i = 0; i < kinds.Length && i < free.Count; i++) Net.AddStructureModuleUnchecked(ship, free[i].Item1, free[i].Item2, free[i].Item3, kinds[i]);
                _weaponGroup = WeaponGroup.Laser;
                _arsenalFitClock = 99.0;
                var userArgs = OS.GetCmdlineUserArgs();
                if (userArgs.Contains("--ui-lang=en")) Loc.Lang = Language.En; // the settings apply their language after the setup: pin it again here
                if (userArgs.Contains("--ui-lang=es")) Loc.Lang = Language.Es;
                _hudModeShot = HudMode.Full;
                _hudDetailShot = HudDetail.Standard;
                _yaw = 0f; _pitch = 0f;
                _arsenalPhase = 1; _arsenalWait = 0;
                break;
            }
            case 1:
                if (Wait(40)) break;
                GD.Print($"ARSENALSHOT slots={string.Join(",", _arsenal.Slots.Select(s => $"{s.Kind}{s.Mark}x{s.Count}"))} selected={_arsenal.Selected} alpha={_arsenalVis.Alpha:F2}");
                Save(".laser.png");
                ArsenalSelect(2); // the Mk1 gun
                break;
            case 2:
                if (Wait(2)) break; // two frames: the marker is on its way
                Save(".ease.png");
                break;
            case 3:
                if (Wait(30)) break;
                Save(".gun.png");
                _feelShotFire = true; // the trigger is held: the guns arm
                break;
            case 4:
                if (Wait(40)) break;
                GD.Print($"ARSENALSHOT armed={Net.GetPlayerInfo(Player).Armed} state={_arsenal.StateOf(_arsenal.Selected)} group={_weaponGroup}");
                Save(".firing.png");
                _feelShotFire = false;
                _hudDetailShot = HudDetail.Minimal;
                _arsenalVis.Touch();
                break;
            case 5:
                if (Wait(30)) break;
                Save(".minimal.png");
                _hudDetailShot = HudDetail.Standard;
                _hudModeShot = HudMode.Cockpit;
                break;
            case 6:
                if (Wait(30)) break;
                GD.Print($"ARSENALSHOT cockpit-mode alpha={_arsenalVis.Alpha:F2}");
                Save(".cockpit.png");
                _hudModeShot = HudMode.Full;
                _arsenalVis.Touch();
                _shotOutside = true;
                {
                    var o = Net.GetStructure(ship);
                    var at = new Vector3(o.OriginX, o.OriginY, o.OriginZ) / cu + new Vector3(4f, 3f, -4f);
                    _shotCam = (at + new Vector3(14f, 6f, 16f), at);
                }
                break;
            case 7:
                if (Wait(30)) break;
                Save(".outside.png");
                GetTree().Quit();
                break;
        }
    }
}
