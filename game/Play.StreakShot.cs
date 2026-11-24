using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--streak-shot=path</c>: a scripted headless check of the motion streaks (ADR 0033). The streaks are given a world velocity (the ship's forward,
/// strafe, up and down axes at several attitudes, at cruise and at boost speed, slowly as when braking, and at rest) and the cockpit view is saved as
/// <c>path.NAME.png</c>. For each one the lines the streaks drew are projected to the screen and checked: every line must lie on the line through the point
/// where the velocity vector meets the view (the heading), which is what makes them converge on it and flow past opposite to the motion. Prints
/// <c>STREAK name segments=N maxDeviationDeg=X</c> and quits.
/// </summary>
public partial class Play
{
    private string? _streakShotPath;
    private int _streakShotPhase, _streakShotWait;

    private static Vector3 V(Vec3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Name, attitude (pitch, yaw, roll in degrees), velocity in the ship frame (right, up, forward) in cells per second.</summary>
    private static readonly (string Name, float Pitch, float Yaw, float Roll, float Right, float Up, float Forward)[] StreakCases =
    {
        ("forward", 0, 0, 0, 0, 0, 6),
        ("boost", 0, 0, 0, 0, 0, 24),
        ("braking", 0, 0, 0, 0, 0, 2.5f),
        ("strafe", 0, 0, 0, 8, 0, 0),
        ("up", 0, 0, 0, 0, 8, 0),
        ("down", 0, 0, 0, 0, -8, 0),
        ("rolled90", 0, 0, 90, 0, 0, 8),
        ("tumbled", 25, -40, 130, 3, 2, 10),
        ("reverse", 0, 0, 0, 0, 0, -6),
        ("still", 0, 0, 0, 0, 0, 0),
    };

    private void StreakShotFrame()
    {
        if (!_structuresOn || ShipStruct < 0) return;
        if (_streakShotPhase >= StreakCases.Length + 1) { Ambience.ForcedVelocity = null; GetTree().Quit(); return; }
        if (_streakShotPhase == 0)
        {
            Ambience.DebugSegments = new();
            _yaw = 0f; _pitch = 0f;
            _streakShotPhase = 1; _streakShotWait = 0;
            return;
        }
        var c = StreakCases[_streakShotPhase - 1];
        _shipQuat = Attitude(c.Pitch, c.Yaw, c.Roll);
        var fwd = V(FlightOrientation.Forward(_shipQuat)); var right = V(FlightOrientation.Right(_shipQuat)); var up = V(FlightOrientation.Up(_shipQuat));
        var vel = right * c.Right + up * c.Up + fwd * c.Forward;
        Ambience.ForcedVelocity = vel;
        if (++_streakShotWait < 12) return;
        _streakShotWait = 0;

        var cam = _camera;
        int n = 0, bad = 0; float worst = 0f, sum = 0f;
        var segments = Ambience.DebugSegments!;
        var eye = cam.GlobalPosition;
        var dir = vel.Length() > 0.01f ? vel.Normalized() : Vector3.Zero;
        foreach (var (a, b) in segments)
        {
            if (cam.IsPositionBehind(a) || cam.IsPositionBehind(b)) continue;
            // Collinear in the world: the speck, its far end and the heading point (eye + dir * k) lie in one plane through the eye, and the line itself is parallel to dir.
            var along = (b - a).Normalized();
            float parallel = MathF.Acos(Math.Clamp(along.Dot(dir), -1f, 1f)) * 180f / MathF.PI;
            // On the screen the line must point at (or directly away from) the heading point.
            var sa = cam.UnprojectPosition(a); var sb = cam.UnprojectPosition(b);
            var vpWorld = eye + dir * 500f;
            float dev = parallel;
            if (!cam.IsPositionBehind(vpWorld))
            {
                var vp = cam.UnprojectPosition(vpWorld);
                var l = sb - sa; var m = vp - sa;
                if (l.Length() > 3f && m.Length() > 3f)
                {
                    float ang = MathF.Acos(Math.Clamp(l.Normalized().Dot(m.Normalized()), -1f, 1f)) * 180f / MathF.PI;
                    dev = Math.Max(dev, Math.Min(ang, 180f - ang));
                }
            }
            n++; sum += dev; worst = Math.Max(worst, dev); if (dev > 1.5f) bad++;
        }
        GetViewport().GetTexture().GetImage().SavePng($"{_streakShotPath}.{c.Name}.png");
        GD.Print($"STREAK {c.Name} velocity=({vel.X:F1},{vel.Y:F1},{vel.Z:F1}) segments={segments.Count} checked={n} maxDeviationDeg={worst:F2} meanDeg={(n > 0 ? sum / n : 0):F2} over1.5deg={bad}");
        _streakShotPhase++;
    }
}
