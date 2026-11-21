using System.Numerics;

namespace Starfoundry.Ui;

/// <summary>An axis-aligned walkable volume in the local cells of a structure (one module).</summary>
public readonly record struct BodyBox(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ);

/// <summary>
/// The pilot's body inside a ship: walking in the local space of the structure, clamped to the union of its module volumes.
/// No gravity and no jump: the feet stay on the floor of the module the body is over (the module's local y base), and the eye
/// is a constant <see cref="EyeHeight"/> above it. A hull opening between adjacent modules of the same level is implied, so
/// the body walks from one module to the next; it never leaves the union. Engine-free and deterministic.
/// </summary>
public static class BodyMotion
{
    public const float EyeHeight = 1.7f;
    public const float WalkSpeed = 4f;
    public const float RunSpeed = 7f;
    /// <summary>Half the width of the body: it keeps this far from every outer wall.</summary>
    public const float Radius = 0.3f;
    /// <summary>The longest stretch moved in one go, so a long frame cannot jump across a gap.</summary>
    public const float MaxSubStep = 0.2f;
    /// <summary>How close to the pilot seat (horizontally, in cells) the body must be to sit down.</summary>
    public const float SeatReach = 2.6f;

    /// <summary>The module boxes of a structure, given its module coordinates and the module size in cells.</summary>
    public static BodyBox[] Volumes(IEnumerable<(int Mx, int My, int Mz)> modules, float sizeX, float sizeY, float sizeZ)
    {
        var list = new List<BodyBox>();
        foreach (var (mx, my, mz) in modules)
            list.Add(new BodyBox(mx * sizeX, my * sizeY, mz * sizeZ, (mx + 1) * sizeX, (my + 1) * sizeY, (mz + 1) * sizeZ));
        return list.ToArray();
    }

    private static bool PointIn(IReadOnlyList<BodyBox> volumes, float x, float y, float z)
    {
        for (int i = 0; i < volumes.Count; i++)
        {
            var b = volumes[i];
            // The feet stand on the floor of the box: boxes stacked above or below this floor are another level.
            if (y >= b.MinY - 0.001f && y < b.MaxY && x >= b.MinX && x <= b.MaxX && z >= b.MinZ && z <= b.MaxZ) return true;
        }
        return false;
    }

    /// <summary>True when the whole body (a square of half-width <see cref="Radius"/>) at <paramref name="position"/> is inside the union.</summary>
    public static bool Contains(Vector3 position, IReadOnlyList<BodyBox> volumes) => Fits(volumes, position.X, position.Y, position.Z);

    private static bool Fits(IReadOnlyList<BodyBox> volumes, float x, float y, float z) =>
        PointIn(volumes, x - Radius, y, z - Radius) && PointIn(volumes, x + Radius, y, z - Radius)
        && PointIn(volumes, x - Radius, y, z + Radius) && PointIn(volumes, x + Radius, y, z + Radius);

    /// <summary>
    /// Moves the body for <paramref name="dt"/> seconds along the horizontal part of <paramref name="wishDirection"/> (length up to 1;
    /// longer is normalised) at <paramref name="speed"/> cells per second, and returns the new position. A step that would leave the
    /// union is replaced by its X part, then its Z part, so the body slides along walls. The height never changes.
    /// </summary>
    public static Vector3 Step(Vector3 position, Vector3 wishDirection, float speed, float dt, IReadOnlyList<BodyBox> volumes)
    {
        float wx = wishDirection.X, wz = wishDirection.Z;
        float len = MathF.Sqrt(wx * wx + wz * wz);
        if (len < 1e-6f || speed <= 0f || dt <= 0f) return position;
        if (len > 1f) { wx /= len; wz /= len; len = 1f; }
        float dist = speed * dt * len;
        int n = Math.Max(1, (int)MathF.Ceiling(dist / MaxSubStep));
        float sx = wx * speed * dt / n, sz = wz * speed * dt / n;
        float x = position.X, y = position.Y, z = position.Z;
        for (int i = 0; i < n; i++)
        {
            if (Fits(volumes, x + sx, y, z + sz)) { x += sx; z += sz; }
            else if (sx != 0f && Fits(volumes, x + sx, y, z)) x += sx;
            else if (sz != 0f && Fits(volumes, x, y, z + sz)) z += sz;
            else break;
        }
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// The nearest spot where the body fits (the floor of the nearest module, shrunk by the body radius), for when the module it stood
    /// in was removed. Returns the input when there are no volumes.
    /// </summary>
    public static Vector3 Nearest(Vector3 position, IReadOnlyList<BodyBox> volumes)
    {
        var best = position;
        float bestD = float.MaxValue;
        foreach (var b in volumes)
        {
            float x = Math.Clamp(position.X, b.MinX + Radius, b.MaxX - Radius), z = Math.Clamp(position.Z, b.MinZ + Radius, b.MaxZ - Radius);
            float dx = x - position.X, dz = z - position.Z, dy = b.MinY - position.Y;
            float d = dx * dx + dz * dz + dy * dy;
            if (d < bestD) { bestD = d; best = new Vector3(x, b.MinY, z); }
        }
        return best;
    }

    /// <summary>True when the body is close enough to the seat (horizontal distance, same level) to sit down.</summary>
    public static bool InSeatReach(Vector3 body, Vector3 seat) =>
        (body.X - seat.X) * (body.X - seat.X) + (body.Z - seat.Z) * (body.Z - seat.Z) <= SeatReach * SeatReach
        && MathF.Abs(body.Y - seat.Y) < 1f;
}
