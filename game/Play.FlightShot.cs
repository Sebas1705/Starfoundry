using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--flight-shot=path</c>: a scripted headless run of the structures campaign that shows free flight. It puts a small station ahead of the
/// ship and saves the cockpit view at four attitudes: level (<c>path</c>), rolled upside down (<c>path.inverted.png</c>), tumbled about all
/// three axes (<c>path.tumble.png</c>) and a pitch past the vertical (<c>path.loop.png</c>); then standing inside the upside-down ship (<c>path.walk.png</c>) and the 3D star map from two orbit angles
/// (<c>path.map.png</c>, <c>path.map2.png</c>), and quits. It prints the attitude axes next to each image.
/// </summary>
public partial class Play
{
    private string? _flightShotPath;
    private bool _flightShot;
    private int _flightShotPhase, _flightShotWait;

    private static System.Numerics.Quaternion Attitude(float pitchDeg, float yawDeg, float rollDeg) =>
        FlightOrientation.Rotate(System.Numerics.Quaternion.Identity, Mathf.DegToRad(pitchDeg), Mathf.DegToRad(yawDeg), Mathf.DegToRad(rollDeg));

    private void FlightShotFrame()
    {
        if (!_structuresOn || ShipStruct < 0) return;
        void Save(string p)
        {
            GetViewport().GetTexture().GetImage().SavePng(p);
            var f = FlightOrientation.Forward(_shipQuat);
            var u = FlightOrientation.Up(_shipQuat);
            GD.Print($"SHOT saved {p} forward=({f.X:F2},{f.Y:F2},{f.Z:F2}) up=({u.X:F2},{u.Y:F2},{u.Z:F2})");
        }
        bool Wait(int frames) { if (++_flightShotWait < frames) return true; _flightShotWait = 0; _flightShotPhase++; return false; }
        int ship = ShipStruct;
        switch (_flightShotPhase)
        {
            case 0:
            {
                // A station ahead and a bit to the right (the ship's nose is -Z), so there is something to turn toward.
                long cu = Units.CellUnits;
                var o = Net.GetStructure(ship);
                int st = Net.CreateStructure(Player, StructureKind.Station, o.OriginX + 14 * cu, o.OriginY - 4 * cu, o.OriginZ - 70 * cu);
                Net.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
                Net.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Workshop);
                Net.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Reactor);
                _yaw = 0f; _pitch = 0f;
                _shipQuat = System.Numerics.Quaternion.Identity;
                _flightShotWait = 0; _flightShotPhase = 1;
                break;
            }
            case 1: if (Wait(60)) break; Save(_flightShotPath!); _shipQuat = Attitude(0f, 0f, 180f); break;
            case 2: if (Wait(20)) break; Save(_flightShotPath + ".inverted.png"); _shipQuat = Attitude(25f, -40f, 130f); break;
            case 3: if (Wait(20)) break; Save(_flightShotPath + ".tumble.png"); _shipQuat = Attitude(115f, 0f, 0f); break;
            case 4:
                if (Wait(20)) break;
                Save(_flightShotPath + ".loop.png");
                // Upside down in the world, stand up: the ship's own floor is still the floor (walking is upright in the ship's frame).
                _shipQuat = Attitude(0f, 0f, 180f);
                StandUpForShot();
                _body = new Vector3(4f, 0f, 9f);
                _yaw = MathF.PI; _pitch = -0.2f;
                break;
            case 5:
                if (Wait(30)) break;
                Save(_flightShotPath + ".walk.png");
                GD.Print($"FLIGHTSHOT standing={Standing} hover={_hover.Kind} camUpY={_camera.GlobalTransform.Basis.Y.Y:F2}");
                _shipQuat = Attitude(0f, 0f, 0f);
                _mapOpen = true;
                break;
            case 6: if (Wait(20)) break; Save(_flightShotPath + ".map.png"); _map.Orbit(1.9f, -0.5f); break;
            case 7: if (Wait(10)) break; Save(_flightShotPath + ".map2.png"); GetTree().Quit(); break;
        }
    }
}
