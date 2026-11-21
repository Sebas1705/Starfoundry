namespace Starfoundry.Ui;

/// <summary>A plain 3D vector for the engine-free HUD maths.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public float Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public float Length => MathF.Sqrt(Dot(this));

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}

/// <summary>
/// The camera as the HUD needs it: three orthonormal axes and the vertical field of view. A Godot camera looks along
/// -Z with +X to the right and +Y up, so pass <c>-basis.Z</c>, <c>basis.X</c> and <c>basis.Y</c>.
/// </summary>
public readonly record struct CameraBasis(Vec3 Forward, Vec3 Right, Vec3 Up, float VerticalFovRadians);

/// <summary>Where a marker goes on screen. <see cref="X"/> and <see cref="Y"/> are pixels from the top left.</summary>
/// <param name="Visible">False when the target is too close to bother with a marker.</param>
/// <param name="OnScreen">True when the target is in view (the marker sits on it); false when it is off to a side or behind
/// (the marker sits on the screen edge and points toward it).</param>
/// <param name="AngleRadians">Direction of the arrow on screen, measured from "right" counter-clockwise with screen up as positive
/// (so 0 points right, pi/2 up, pi left, -pi/2 down). Only meaningful off screen.</param>
public readonly record struct MarkerPlacement(bool Visible, bool OnScreen, float X, float Y, float AngleRadians, float Distance);

/// <summary>
/// Projection and edge-clamping for the directional markers of the first-person HUD: you can look away from a deposit, a
/// threat or your base, so a small marker shows where it is and how far. No engine types, so the maths is unit tested.
/// </summary>
public static class Markers
{
    /// <summary>
    /// Places a marker for a target at <paramref name="relative"/> (target minus camera, in cells) on a screen of
    /// <paramref name="width"/> x <paramref name="height"/> pixels. The marker stays inside the rectangle inset by the margins;
    /// targets nearer than <paramref name="hideBelow"/> cells are not shown.
    /// </summary>
    public static MarkerPlacement Place(Vec3 relative, CameraBasis cam, float width, float height,
        float marginLeft = 24f, float marginTop = 24f, float marginRight = 24f, float marginBottom = 24f, float hideBelow = 4f)
    {
        float dist = relative.Length;
        if (dist < hideBelow || width <= 0f || height <= 0f) return new MarkerPlacement(false, false, width / 2f, height / 2f, 0f, dist);

        float depth = relative.Dot(cam.Forward), sx = relative.Dot(cam.Right), sy = relative.Dot(cam.Up);
        float cx = width / 2f, cy = height / 2f;
        float x0 = marginLeft, x1 = width - marginRight, y0 = marginTop, y1 = height - marginBottom;

        if (depth > 1e-3f)
        {
            float focal = cy / MathF.Tan(cam.VerticalFovRadians / 2f); // pixels from the eye to the screen plane
            float px = cx + sx / depth * focal, py = cy - sy / depth * focal;
            if (px >= x0 && px <= x1 && py >= y0 && py <= y1) return new MarkerPlacement(true, true, px, py, 0f, dist);
        }

        // Off screen: take the direction of the target around the view axis and put the marker where that ray leaves the box.
        float dx = sx, dy = -sy; // screen space, y down
        if (MathF.Abs(dx) < 1e-5f && MathF.Abs(dy) < 1e-5f) dy = depth > 0f ? 0f : 1f; // dead ahead or dead behind
        if (MathF.Abs(dx) < 1e-5f && MathF.Abs(dy) < 1e-5f) dx = 1f;
        float halfW = (x1 - x0) / 2f, halfH = (y1 - y0) / 2f;
        float mx = (x0 + x1) / 2f, my = (y0 + y1) / 2f;
        float tx = MathF.Abs(dx) < 1e-5f ? float.MaxValue : halfW / MathF.Abs(dx);
        float ty = MathF.Abs(dy) < 1e-5f ? float.MaxValue : halfH / MathF.Abs(dy);
        float t = MathF.Min(tx, ty);
        return new MarkerPlacement(true, false, mx + dx * t, my + dy * t, MathF.Atan2(-dy, dx), dist);
    }

    /// <summary>
    /// The centre of the group of points around the one nearest to <paramref name="from"/>: every point within
    /// <paramref name="radius"/> of that nearest one. Null when there are no points. Used for the "your base" marker.
    /// </summary>
    public static Vec3? NearestCluster(IReadOnlyList<Vec3> points, Vec3 from, float radius)
    {
        if (points.Count == 0) return null;
        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            float d = (points[i] - from).Length;
            if (d < best) { best = d; nearest = i; }
        }
        var anchor = points[nearest];
        float sx = 0, sy = 0, sz = 0;
        int n = 0;
        foreach (var p in points)
        {
            if ((p - anchor).Length > radius) continue;
            sx += p.X; sy += p.Y; sz += p.Z;
            n++;
        }
        return new Vec3(sx / n, sy / n, sz / n);
    }
}
