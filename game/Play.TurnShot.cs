using System;
using System.Linq;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--turn-shot=path</c> (with <c>--cockpit-size=WxH</c> and <c>--ui-lang=en|es</c>): a scripted run of the structures campaign that exercises the rotation
/// dynamics through the real input path (<see cref="LookBy"/> and <see cref="UpdateAttitude"/>), with the settings file left alone. It turns the ship
/// through 180 degrees with the mouse (the pilot keeps dragging until the nose is round), prints how long that took and how many SetAttitude commands the
/// throttle let through (<c>TURNSHOT ...</c> lines), saves the flight display with the turn-rate indicator mid-turn (<c>path.turn.png</c>), then checks the flight
/// assist: free rotation keeps spinning, switching the assist on brings the ship to rest (<c>path.free.png</c>). The tuning numbers are in
/// <c>content/ship_handling.json</c>.
/// </summary>
public partial class Play
{
    private string? _turnShotPath;
    private int _turnShotFrame;
    private bool TurnShotActive => _turnShotPath is not null;
    private int _attitudeCommands;
    private double _turnShotStart, _turnShotLast;
    private float _turnShotFed;
    private int _turnShotCommands0, _turnShotFrames;
    private bool _turnShotMid, _turnShotDone;
    private System.Numerics.Quaternion _turnShotFrom = System.Numerics.Quaternion.Identity;

    private void TurnShotSetup(string[] args)
    {


        CockpitShotSetup(args); // the same seated start as the cockpit shots: language, a few items, no mouse capture
    }

    private void TurnShotFrame()
    {
        if (_turnShotPath is not { } path) return;
        _turnShotFrame++;
        _wantCapture = false;
        var win = GetWindow();
        var args = OS.GetCmdlineUserArgs();
        var sizeArg = args.FirstOrDefault(a => a.StartsWith("--cockpit-size="))?["--cockpit-size=".Length..].Split('x');
        double now = Time.GetTicksMsec() / 1000.0;
        void Shot(string name)
        {
            GetViewport().GetTexture().GetImage().SavePng(path + "." + name + ".png");
            GD.Print($"TURNSHOT saved {path}.{name}.png {GetViewport().GetVisibleRect().Size}");
        }
        float Angle() => FlightOrientation.AngleFromIdentity(System.Numerics.Quaternion.Concatenate(_shipQuat, System.Numerics.Quaternion.Inverse(_turnShotFrom)));
        switch (_turnShotFrame)
        {
            case 3:
                if (sizeArg is { Length: 2 } && int.TryParse(sizeArg[0], out int w) && int.TryParse(sizeArg[1], out int h)) SetWindowSize(win, w, h);
                else SetWindowSize(win, 1280, 720);
                break;
            case 12:
                _inertiaShot = ShipInertia.Full;
                _assistShot = true;
                _hudModeShot = HudMode.Cockpit;
                _yaw = 0f; _pitch = -0.35f;
                _focus = null;
                break;
            case 40:
                if (ConsoleReady) _cockpit!.Pages.Show(CockpitScreen.Center, CockpitPage.Flight);
                break;
            case 90:
                GD.Print($"TURNSHOT ship stats: rate {_turnStats.RateDegPerSecond:F1} deg/s, accel {_turnStats.AccelDegPerSecondSquared:F1} deg/s2, inertia {Inertia}, assist {FlightAssistOn}, modules {Net.GetStructure(ShipStruct).Modules.Count}");
                _turnShotFrom = _shipQuat;
                _turnShotStart = now;
                _turnShotLast = now;
                _turnShotFed = 0f;
                _turnShotCommands0 = _attitudeCommands;
                _turnShotFrames = 0;
                break;
        }
        if (_turnShotFrame >= 91 && !_turnShotDone && _turnShotStart > 0)
        {
            _turnShotFrames++;
            float dt = (float)(now - _turnShotLast);
            _turnShotLast = now;
            // the pilot drags the mouse right at about 8 radians a second until the whole half turn has been given
            float give = MathF.Min(MathF.PI - _turnShotFed, 8f * Math.Clamp(dt, 0.001f, 0.1f));
            if (give > 0f)
            {
                float before = _turn.Pending.Y;
                LookBy(new Vector2(give / (MouseSensitivity * SettingsStore.Current.MouseSensitivity), 0f));
                _turnShotFed += _turn.Pending.Y - before;
            }
            float angle = Angle();
            if (!_turnShotMid && angle > MathF.PI / 2f) { _turnShotMid = true; Shot("turn"); _cockpit?.Capture(CockpitScreen.Center).SavePng(path + ".tex.turn.png"); GD.Print($"TURNSHOT mid-turn rates deg/s {_turn.OmegaDegrees}"); }
            if (angle >= MathF.PI * 0.99f && _turn.Omega.Length() < 0.02f)
            {
                _turnShotDone = true;
                double took = now - _turnShotStart;
                int cmds = _attitudeCommands - _turnShotCommands0;
                GD.Print($"TURNSHOT half turn took {took:F2} s over {_turnShotFrames} frames ({_turnShotFrames / took:F0} fps), attitude commands {cmds} ({cmds / took:F1} per second), final angle {Mathf.RadToDeg(angle):F1} deg");
                _turnShotFrame = 1000; // go on to the assist check
            }
            else if (now - _turnShotStart > 15.0) { GD.Print("TURNSHOT FAIL the half turn did not finish in 15 s"); GetTree().Quit(1); }
        }
        // the flight assist check: a spin with the assist off keeps going, the assist stops it
        switch (_turnShotFrame)
        {
            case 1010: _assistShot = false; _turn.AddLook(0.4f, 0f, _turnStats); break;
            case 1090: GD.Print($"TURNSHOT free rotation: yaw rate {_turn.OmegaDegrees.Y:F1} deg/s"); Shot("free"); _cockpit?.Capture(CockpitScreen.Center).SavePng(path + ".tex.free.png"); break;
            case 1200: GD.Print($"TURNSHOT free rotation 1.5 s later: yaw rate {_turn.OmegaDegrees.Y:F1} deg/s (it should not have damped)"); _assistShot = true; break;
            case 1500: GD.Print($"TURNSHOT assist back on: yaw rate {_turn.OmegaDegrees.Y:F2} deg/s (it should be 0)"); break;
            case 1510: GetTree().Quit(); break;
        }
    }
}
