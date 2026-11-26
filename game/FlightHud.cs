using System;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The throttle and speed gauge of the flight readout (ADR 0033): one bar. The fill is the real speed as a share of what the engines can do now
/// (boost included), the white tick is the speed the thrust asks for. With inertia the fill chases the tick, so the player sees the ship
/// catching up while accelerating and shedding speed while braking; the fill turns amber while the boost runs.
/// </summary>
public partial class ThrottleGauge : Control
{
    private float _actual, _target;
    private bool _boost;

    public ThrottleGauge(float width = 150f, float height = 10f)
    {
        CustomMinimumSize = new Vector2(width, height);
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The shares are of the current top speed (0..1; the tick may sit past the fill).</summary>
    public void Set(float actual, float target, bool boost)
    {
        actual = Math.Clamp(actual, 0f, 1f);
        target = Math.Clamp(target, 0f, 1f);
        if (Math.Abs(actual - _actual) < 0.002f && Math.Abs(target - _target) < 0.002f && boost == _boost) return;
        _actual = actual; _target = target; _boost = boost;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        int rad = (int)(r.Size.Y / 2f);
        DrawStyleBox(UiTheme.Box(UiTheme.Track, new Color(1f, 1f, 1f, 0.06f), rad, 0f), r);
        if (_actual > 0.002f)
        {
            var fill = _boost ? new Color(0.55f, 0.85f, 1f) : UiTheme.Accent;
            DrawStyleBox(UiTheme.Box(fill, null, rad, 0f), new Rect2(0, 0, Math.Max(r.Size.Y, r.Size.X * _actual), r.Size.Y));
        }
        // The speed asked for: a tick with a small arrow head above and below, so it reads on the fill and on the empty track.
        float x = Math.Clamp(r.Size.X * _target, 1f, r.Size.X - 1f);
        var tick = new Color(1f, 1f, 1f, _target < 0.01f ? 0.35f : 0.95f);
        if (_target < 0.01f) { DrawLine(new Vector2(x, 1f), new Vector2(x, r.Size.Y - 1f), tick, 2f); return; } // at rest: a small tick inside the track (the arrow heads read as an I-beam cursor at zero)
        DrawLine(new Vector2(x, -2f), new Vector2(x, r.Size.Y + 2f), tick, 2f);
        DrawColoredPolygon(new[] { new Vector2(x - 3.5f, -4f), new Vector2(x + 3.5f, -4f), new Vector2(x, 0f) }, tick);
        DrawColoredPolygon(new[] { new Vector2(x - 3.5f, r.Size.Y + 4f), new Vector2(x + 3.5f, r.Size.Y + 4f), new Vector2(x, r.Size.Y) }, tick);
    }
}

/// <summary>
/// The velocity vector on the 3D view (ADR 0033): a ring where the ship is going (prograde) and a ring with a cross where it is coming from
/// (retrograde). Both are screen points computed from the world velocity, so they have nothing to do with a horizon or an up: with the ship
/// turned any way, the ring says where the motion goes and a pilot brakes by pointing the nose at the cross and thrusting.
/// </summary>
public partial class VelocityMarkers : Control
{
    private Vector2? _prograde, _retrograde;
    private float _alpha;

    public VelocityMarkers()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Screen points (null: off screen or too slow to say) and an opacity that grows with the speed.</summary>
    public void Set(Vector2? prograde, Vector2? retrograde, float alpha)
    {
        if (prograde == _prograde && retrograde == _retrograde && Math.Abs(alpha - _alpha) < 0.01f) return;
        _prograde = prograde; _retrograde = retrograde; _alpha = alpha;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_alpha <= 0.01f) return;
        var good = new Color(0.55f, 1f, 0.6f, 0.9f * _alpha);
        var warn = new Color(1f, 0.75f, 0.35f, 0.85f * _alpha);
        var shade = new Color(0f, 0f, 0f, 0.5f * _alpha);
        if (_prograde is { } p)
        {
            DrawArc(p + new Vector2(0.5f, 1f), 10f, 0f, Mathf.Tau, 28, shade, 3f, true);
            DrawArc(p, 10f, 0f, Mathf.Tau, 28, good, 1.8f, true);
            DrawCircle(p, 1.6f, good);
            DrawLine(p + new Vector2(10f, 0f), p + new Vector2(18f, 0f), good, 1.8f, true); // the wings and the fin of the classic marker
            DrawLine(p - new Vector2(10f, 0f), p - new Vector2(18f, 0f), good, 1.8f, true);
            DrawLine(p - new Vector2(0f, 10f), p - new Vector2(0f, 17f), good, 1.8f, true);
        }
        if (_retrograde is { } q)
        {
            DrawArc(q + new Vector2(0.5f, 1f), 10f, 0f, Mathf.Tau, 28, shade, 3f, true);
            DrawArc(q, 10f, 0f, Mathf.Tau, 28, warn, 1.8f, true);
            const float k = 6.5f;
            DrawLine(q + new Vector2(-k, -k), q + new Vector2(k, k), warn, 1.8f, true);
            DrawLine(q + new Vector2(-k, k), q + new Vector2(k, -k), warn, 1.8f, true);
        }
    }
}
