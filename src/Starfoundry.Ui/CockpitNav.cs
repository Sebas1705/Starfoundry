using System.Numerics;

namespace Starfoundry.Ui;

/// <summary>
/// Decides when a cockpit screen frames itself: the pilot has looked at it (the crosshair rests on it) for <see cref="DwellSeconds"/> without moving the mouse
/// much, and no flight key is down. It lets go again when the mouse moves away (more than <see cref="MoveLimitPixels"/> within the last quarter of a
/// second), when a flight key is pressed or when the crosshair leaves the screen. F4 (full focus) is a separate state that this never touches. Engine-free.
/// </summary>
public sealed class AutoFrameTracker
{
    public const float DwellSeconds = 0.4f;
    public const float MoveLimitPixels = 30f;
    /// <summary>The mouse movement counts for this long (a decaying sum with this time constant).</summary>
    public const float MoveMemorySeconds = 0.25f;
    /// <summary>How long the crosshair may be off the framed screen before the frame lets go (the head is still turning towards it).</summary>
    public const float LostGraceSeconds = 0.25f;

    private CockpitScreen? _looking;
    private float _dwell, _move, _lost;

    /// <summary>The screen that is framed now, or null.</summary>
    public CockpitScreen? Framed { get; private set; }

    public void Reset()
    {
        Framed = null;
        _looking = null;
        _dwell = _move = _lost = 0f;
    }

    /// <param name="dt">Seconds since the last call.</param>
    /// <param name="aimed">The screen the crosshair is on, or null.</param>
    /// <param name="mousePixels">How far the mouse moved this frame, in pixels.</param>
    /// <param name="flightInput">A flight key (thrust, roll, boost) is down.</param>
    /// <param name="allowed">False while something else owns the view (a full focus, a menu, a build tool, no console).</param>
    public CockpitScreen? Update(float dt, CockpitScreen? aimed, float mousePixels, bool flightInput, bool allowed)
    {
        if (!(dt > 0f)) dt = 0f;
        _move = _move * MathF.Exp(-dt / MoveMemorySeconds) + (float.IsFinite(mousePixels) ? Math.Abs(mousePixels) : 0f);
        if (!allowed || flightInput)
        {
            Reset();
            return null;
        }
        if (Framed is { } f)
        {
            _lost = aimed == f ? 0f : _lost + dt;
            if (_move > MoveLimitPixels || _lost > LostGraceSeconds)
            {
                Framed = null;
                _looking = null;
                _dwell = 0f;
                _lost = 0f;
            }
            return Framed;
        }
        if (aimed is { } a && _move <= MoveLimitPixels)
        {
            if (_looking == a) _dwell += dt;
            else { _looking = a; _dwell = dt; }
            if (_dwell >= DwellSeconds) { Framed = a; _lost = 0f; }
        }
        else
        {
            _looking = aimed;
            _dwell = 0f;
        }
        return Framed;
    }
}

/// <summary>
/// The camera of the 3D map on the navigation page: a view of the space round the ship, turned by the mouse and zoomed by the wheel. The map's own axes
/// are the ship's: X to the right, Y up, Z ahead (a point 100 cells ahead is <c>(0, 0, 100)</c>). <see cref="Project"/> gives the position on the map in
/// units of the map radius (the edge of the map is 1), so the drawing only multiplies by the radius in pixels. Engine-free.
/// </summary>
public sealed class NavMapCamera
{
    public const float MinRange = 40f, MaxRange = 6000f;
    public const float MinTilt = 0.15f, MaxTilt = MathF.PI / 2f;
    public const float DefaultTilt = 0.95f;

    /// <summary>Turn about the ship's up axis, radians; 0 has the nose at the top of the map.</summary>
    public float Yaw { get; private set; }

    /// <summary>How far the view looks down on the ship's plane: pi/2 straight down (plan view), small values nearly edge on.</summary>
    public float Tilt { get; private set; } = DefaultTilt;

    /// <summary>The distance in cells that the edge of the map stands for.</summary>
    public float Range { get; private set; } = 300f;

    /// <summary>True until the pilot zooms: the range then follows the target (see <see cref="Follow"/>).</summary>
    public bool Auto { get; private set; } = true;

    public void Reset()
    {
        Yaw = 0f;
        Tilt = DefaultTilt;
        Auto = true;
    }

    public void Orbit(float yawDelta, float tiltDelta)
    {
        if (float.IsFinite(yawDelta)) Yaw = MathF.IEEERemainder(Yaw + yawDelta, MathF.Tau);
        if (float.IsFinite(tiltDelta)) Tilt = Math.Clamp(Tilt + tiltDelta, MinTilt, MaxTilt);
    }

    /// <summary>Zooms by wheel steps: positive zooms in (a smaller range). Turns the automatic range off.</summary>
    public void Zoom(int steps)
    {
        if (steps == 0) return;
        Auto = false;
        Range = Math.Clamp(Range * MathF.Pow(0.78f, steps), MinRange, MaxRange);
    }

    /// <summary>With the automatic range on, frames a target at <paramref name="distance"/> cells (with a margin), eased by the caller.</summary>
    public void Follow(float distance, float dt)
    {
        if (!Auto) return;
        float wanted = Math.Clamp(distance * 1.25f, MinRange, MaxRange);
        float k = 1f - MathF.Exp(-4f * Math.Max(0f, dt));
        Range += (wanted - Range) * k;
    }

    /// <summary>
    /// A point in the ship's frame on the map: <c>X</c> to the right and <c>Y</c> up the map, in units of the radius (the map's edge is 1); <c>Depth</c> is
    /// larger for points further from the viewer (for drawing order), and <c>Height</c> is where the point's shadow on the ship's plane is, so a stem can be
    /// drawn down to it.
    /// </summary>
    public readonly record struct Point(float X, float Y, float Depth, float ShadowX, float ShadowY);

    public Point Project(Vector3 local)
    {
        float cy = MathF.Cos(Yaw), sy = MathF.Sin(Yaw);
        float x1 = local.X * cy + local.Z * sy;
        float z1 = -local.X * sy + local.Z * cy;
        float y1 = local.Y;
        float st = MathF.Sin(Tilt), ct = MathF.Cos(Tilt);
        float inv = 1f / Range;
        return new Point(x1 * inv, (z1 * st + y1 * ct) * inv, (z1 * ct - y1 * st) * inv, x1 * inv, z1 * st * inv);
    }
}

/// <summary>How the navigation page chooses and words what it shows. Engine-free.</summary>
public static class NavPage
{
    /// <summary>
    /// The target the big numbers describe: the pick when it is still in the list, else the selected deposit, else the first row (the nearest threat first, see
    /// <see cref="NavList"/>), else nothing.
    /// </summary>
    public static NavTarget? Current(IReadOnlyList<NavTarget> rows, NavKind? pickedKind, int pickedId)
    {
        if (pickedKind is { } k && rows.FirstOrDefault(r => r.Kind == k && r.Id == pickedId) is { } picked) return picked;
        return rows.FirstOrDefault(r => r.Selected) ?? rows.FirstOrDefault();
    }

    /// <summary>The distance as shown in the big number: whole cells, "1.2k" from a thousand, "--" with no target.</summary>
    public static string BigDistance(NavTarget? t, IFormatProvider culture) => t is null ? "--" : CockpitMath.Distance(t.Distance, culture);

    /// <summary>
    /// The fonts of the three big values of the page (target name, distance, speed), in texture pixels, for a texture <paramref name="textureWidth"/> pixels
    /// wide. They are chosen so that, on the starter console seen from the seat at 720p, each is at least 14 pixels tall on the screen without any zoom.
    /// </summary>
    public static (int Name, int Distance, int Speed) KeyFonts(int textureWidth) => textureWidth >= 560 ? (52, 56, 56) : (34, 40, 40);
}
