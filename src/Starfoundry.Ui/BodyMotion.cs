using System.Numerics;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>
/// An axis-aligned walkable volume in the local cells of a structure (one module). With a <paramref name="Shell"/> the walkable part of the
/// box is cut down to the inside of the module's drawn shell (the ship's floor and ceiling chamfers, nose, tail, door frames, station walls).
/// </summary>
public readonly record struct BodyBox(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ, HullShell? Shell = null);

/// <summary>
/// A solid box standing on the floors of a structure (a machine, the workbench), in its local cells. The body is blocked by it when it rises more than a
/// step above the feet (<see cref="BodyMotion.StepHeight"/>): belts and floor plates stay walkable.
/// </summary>
public readonly record struct BodySolid(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ);

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
    public const float MaxSubStep = 0.1f;
    /// <summary>How close to the pilot seat (horizontally, in cells) the body must be to sit down.</summary>
    public const float SeatReach = 2.6f;
    /// <summary>The highest edge the body walks over (a belt, a threshold, a coaming): a solid whose top is lower does not block it.</summary>
    public const float StepHeight = 0.3f;

    /// <summary>The module boxes of a structure, given its module coordinates and the module size in cells.</summary>
    public static BodyBox[] Volumes(IEnumerable<(int Mx, int My, int Mz)> modules, float sizeX, float sizeY, float sizeZ)
    {
        var list = new List<BodyBox>();
        foreach (var (mx, my, mz) in modules)
            list.Add(new BodyBox(mx * sizeX, my * sizeY, mz * sizeZ, (mx + 1) * sizeX, (my + 1) * sizeY, (mz + 1) * sizeZ));
        return list.ToArray();
    }

    /// <summary>
    /// The module boxes of a structure with their shells: <paramref name="maskOf"/> gives the neighbour (and docking) mask of a module, so the
    /// body is held inside what the model draws. <paramref name="ship"/> picks a ship hull or a station box.
    /// </summary>
    public static BodyBox[] Volumes(IEnumerable<KeyValuePair<(int Mx, int My, int Mz), ModuleKind>> modules, Func<(int Mx, int My, int Mz), int> maskOf, bool ship)
    {
        var list = new List<BodyBox>();
        foreach (var (key, kind) in modules)
        {
            var (mx, my, mz) = key;
            list.Add(new BodyBox(mx * StationGrid.ModuleX, my * StationGrid.ModuleY, mz * StationGrid.ModuleZ,
                (mx + 1) * StationGrid.ModuleX, (my + 1) * StationGrid.ModuleY, (mz + 1) * StationGrid.ModuleZ, HullProfile.ShellOf(kind, ship, maskOf(key))));
        }
        return list.ToArray();
    }

    private static bool PointIn(IReadOnlyList<BodyBox> volumes, float x, float y, float z)
    {
        for (int i = 0; i < volumes.Count; i++)
        {
            var b = volumes[i];
            // The feet stand on the floor of the box: boxes stacked above or below this floor are another level.
            if (y >= b.MinY - 0.001f && y < b.MaxY && x >= b.MinX && x <= b.MaxX && z >= b.MinZ && z <= b.MaxZ)
            {
                if (b.Shell is not { } shell) return true;
                float lift = y - b.MinY;
                if (HullProfile.Clear(shell, x - b.MinX, z - b.MinZ, HullProfile.DeckTop + lift, HullProfile.BodyHeight + lift)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when the whole body (a square of half-width <see cref="Radius"/>) at <paramref name="position"/> is inside the union. The footprint is
    /// sampled on a grid (<see cref="Samples"/> x <see cref="Samples"/>, a point every <see cref="Radius"/> / 2), not just at its four corners, so a
    /// solid thinner than the body (a door frame beside the opening, a wall between two modules) cannot slip between the samples.
    /// </summary>
    public static bool Contains(Vector3 position, IReadOnlyList<BodyBox> volumes, IReadOnlyList<BodySolid>? solids = null) => Fits(volumes, position.X, position.Y, position.Z, solids);

    /// <summary>True when the point is inside a solid that rises above a step over the feet and starts below the head.</summary>
    private static bool InSolid(IReadOnlyList<BodySolid> solids, float x, float y, float z)
    {
        for (int i = 0; i < solids.Count; i++)
        {
            var s = solids[i];
            if (x > s.MinX && x < s.MaxX && z > s.MinZ && z < s.MaxZ && s.MaxY > y + StepHeight && s.MinY < y + HullProfile.BodyHeight) return true;
        }
        return false;
    }

    /// <summary>Points per side of the footprint grid.</summary>
    public const int Samples = 5;

    private static bool Fits(IReadOnlyList<BodyBox> volumes, float x, float y, float z, IReadOnlyList<BodySolid>? solids = null)
    {
        for (int i = 0; i < Samples; i++)
        {
            float px = x + Radius * (2f * i / (Samples - 1) - 1f);
            for (int j = 0; j < Samples; j++)
            {
                float pz = z + Radius * (2f * j / (Samples - 1) - 1f);
                if (!PointIn(volumes, px, y, pz) || (solids is not null && InSolid(solids, px, y, pz))) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Moves the body for <paramref name="dt"/> seconds along the horizontal part of <paramref name="wishDirection"/> (length up to 1;
    /// longer is normalised) at <paramref name="speed"/> cells per second, and returns the new position. A step that would leave the
    /// union is replaced by its X part, then its Z part, so the body slides along walls. The height never changes.
    /// </summary>
    public static Vector3 Step(Vector3 position, Vector3 wishDirection, float speed, float dt, IReadOnlyList<BodyBox> volumes, IReadOnlyList<BodySolid>? solids = null)
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
            if (Fits(volumes, x + sx, y, z + sz, solids)) { x += sx; z += sz; }
            else if (sx != 0f && Fits(volumes, x + sx, y, z, solids)) x += sx;
            else if (sz != 0f && Fits(volumes, x, y, z + sz, solids)) z += sz;
            else if (SlideAlong(volumes, x, y, z, sx, sz, solids, out float tx, out float tz)) { x += tx; z += tz; }
            else break;
        }
        return new Vector3(x, y, z);
    }

    /// <summary>The widest turn (degrees, either way) of a step that slides along a slanted wall; a push straight into a wall (90 degrees) stays put.</summary>
    private static readonly float[] SlideAngles = { 10f, 20f, 30f, 40f, 50f, 60f, 70f };

    /// <summary>
    /// Slides along a wall that is slanted against both axes (the tapered nose, a chamfer met at an angle): the step is turned away from the wall
    /// by the smallest angle that fits, shortened by the cosine of the turn, so the body glides along the surface instead of sticking.
    /// </summary>
    private static bool SlideAlong(IReadOnlyList<BodyBox> volumes, float x, float y, float z, float sx, float sz, IReadOnlyList<BodySolid>? solids, out float tx, out float tz)
    {
        tx = tz = 0f;
        float len = MathF.Sqrt(sx * sx + sz * sz);
        if (len < 1e-6f) return false;
        foreach (float deg in SlideAngles)
        {
            float a = deg * (MathF.PI / 180f), c = MathF.Cos(a), s = MathF.Sin(a);
            foreach (float sign in new[] { 1f, -1f })
            {
                float rx = sx * c - sign * sz * s, rz = sign * sx * s + sz * c; // the step turned by sign * a, length len * cos(a) after the shortening
                rx *= c; rz *= c;
                if (!Fits(volumes, x + rx, y, z + rz, solids)) continue;
                tx = rx; tz = rz;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The nearest spot where the body fits (the floor of the nearest module, shrunk by the body radius), for when the module it stood
    /// in was removed. Returns the input when there are no volumes.
    /// </summary>
    public static Vector3 Nearest(Vector3 position, IReadOnlyList<BodyBox> volumes, IReadOnlyList<BodySolid>? solids = null)
    {
        var best = position;
        float bestD = float.MaxValue;
        void Consider(BodyBox b, float x, float z)
        {
            float dx = x - position.X, dz = z - position.Z, dy = b.MinY - position.Y;
            float d = dx * dx + dz * dz + dy * dy;
            if (d < bestD && Fits(volumes, x, b.MinY, z, solids)) { bestD = d; best = new Vector3(x, b.MinY, z); }
        }
        foreach (var b in volumes)
            Consider(b, Math.Clamp(position.X, b.MinX + Radius, b.MaxX - Radius), Math.Clamp(position.Z, b.MinZ + Radius, b.MaxZ - Radius));
        if (bestD < float.MaxValue) return best;
        // Every clamped spot is inside a chamfer or a wall of its shell: search the floor of each shaped box on a grid.
        foreach (var b in volumes)
            for (float x = b.MinX + Radius; x <= b.MaxX - Radius + 1e-3f; x += 0.25f)
                for (float z = b.MinZ + Radius; z <= b.MaxZ - Radius + 1e-3f; z += 0.25f)
                    Consider(b, x, z);
        if (bestD < float.MaxValue) return best;
        foreach (var b in volumes) // no spot fits anywhere (a ship of only low modules): the old clamp, so the caller still gets a position
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
