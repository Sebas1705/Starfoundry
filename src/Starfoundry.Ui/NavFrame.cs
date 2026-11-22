namespace Starfoundry.Ui;

/// <summary>One axis of the navigation gizmo on the screen: where its tip is and whether it points toward or away from the pilot.</summary>
/// <param name="Label">The axis name ("X", "Y", "Z").</param>
/// <param name="X">Tip to the right of the gizmo centre, -1 to 1 (the axis is a unit vector).</param>
/// <param name="Y">Tip above the gizmo centre, -1 to 1 (screen up is positive).</param>
/// <param name="Depth">+1 when the axis points straight ahead, -1 when it points straight back at the pilot.</param>
public readonly record struct GizmoAxis(string Label, float X, float Y, float Depth)
{
    /// <summary>True when the axis points mostly away from the pilot (draw it fainter).</summary>
    public bool Receding => Depth > 0f;
}

/// <summary>
/// The fixed reference frame of the navigation gizmo. Space has no up, so the three axes are the galactic frame that the star map and every
/// system share (+X, +Y, +Z of the galaxy coordinates): they never move, and the ship's attitude is read against them. Engine-free.
/// </summary>
public static class NavFrame
{
    public static readonly Vec3[] GalacticAxes = { new(1f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f) };
    public static readonly string[] GalacticLabels = { "X", "Y", "Z" };

    /// <summary>The three galactic axes as the camera sees them.</summary>
    public static GizmoAxis[] Project(CameraBasis cam)
    {
        var result = new GizmoAxis[GalacticAxes.Length];
        for (int i = 0; i < result.Length; i++)
        {
            var a = GalacticAxes[i];
            result[i] = new GizmoAxis(GalacticLabels[i], a.Dot(cam.Right), a.Dot(cam.Up), a.Dot(cam.Forward));
        }
        return result;
    }

    /// <summary>The unit direction from one point to another (zero when they coincide); used for the gate toward a chosen system.</summary>
    public static Vec3 DirectionTo(Vec3 from, Vec3 to)
    {
        var d = to - from;
        float len = d.Length;
        return len < 1e-6f ? new Vec3(0f, 0f, 0f) : new Vec3(d.X / len, d.Y / len, d.Z / len);
    }

    /// <summary>
    /// The angle in degrees between where the ship points and a direction: 0 when it is dead ahead, 180 when it is behind. It does not depend on
    /// the ship's roll, so an upside-down pilot gets the same number as an upright one.
    /// </summary>
    public static float TurnDegrees(Vec3 direction, CameraBasis cam)
    {
        float len = direction.Length;
        if (len < 1e-6f) return 0f;
        float c = Math.Clamp(direction.Dot(cam.Forward) / len, -1f, 1f);
        return MathF.Acos(c) * 180f / MathF.PI;
    }
}
