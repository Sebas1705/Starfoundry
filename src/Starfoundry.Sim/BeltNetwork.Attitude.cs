namespace Starfoundry.Sim;

/// <summary>The nearest point of a module's box to a sphere centre (ADR 0030): the vector in the world frame and its exact squared length.</summary>
/// <param name="D2">Squared distance, measured in the structure's own frame so it is exact; 0 when the centre is inside the box.</param>
internal readonly record struct BoxVector(long X, long Y, long Z, long D2);

public sealed partial class BeltNetwork
{
    // ---- the frame of a turned structure -----------------------------------------------------------------

    /// <summary>The pivot a ship turns about, in units from the structure origin: the seat's eye of the first cockpit module (the module (0,0,0) without one).</summary>
    public static (long X, long Y, long Z) PivotOf(Structure s)
    {
        (int Mx, int My, int Mz) m = (0, 0, 0);
        foreach (var (key, kind) in s.ModuleMap)
            if (kind == ModuleKind.Cockpit) { m = key; break; } // sorted: the first cockpit by coordinate, as the seat locator picks it
        long cu = Units.CellUnits;
        return ((long)m.Mx * StationGrid.ModuleX * cu + AttitudeRules.PivotX, (long)m.My * StationGrid.ModuleY * cu + AttitudeRules.PivotY,
                (long)m.Mz * StationGrid.ModuleZ * cu + AttitudeRules.PivotZ);
    }

    /// <summary>
    /// A point of a structure's own frame (units from its origin) in the world (units): the origin plus the point turned about the pilot seat.
    /// With the identity attitude this is the plain origin plus the point.
    /// </summary>
    public (long X, long Y, long Z) StructureToWorld(Structure s, long lx, long ly, long lz)
    {
        if (s.Attitude.IsIdentity) return (s.OriginX + lx, s.OriginY + ly, s.OriginZ + lz);
        var (px, py, pz) = PivotOf(s);
        var (rx, ry, rz) = s.Attitude.Rotate(lx - px, ly - py, lz - pz);
        return (s.OriginX + px + rx, s.OriginY + py + ry, s.OriginZ + pz + rz);
    }

    /// <summary>The inverse of <see cref="StructureToWorld"/>: a world point (units) in the structure's own frame, in units from its origin.</summary>
    public (long X, long Y, long Z) WorldToStructure(Structure s, long wx, long wy, long wz)
    {
        if (s.Attitude.IsIdentity) return (wx - s.OriginX, wy - s.OriginY, wz - s.OriginZ);
        var (px, py, pz) = PivotOf(s);
        var (rx, ry, rz) = s.Attitude.RotateInverse(wx - s.OriginX - px, wy - s.OriginY - py, wz - s.OriginZ - pz);
        return (px + rx, py + ry, pz + rz);
    }

    /// <summary>A direction of a structure's own frame turned into the world frame (no translation).</summary>
    internal static (long X, long Y, long Z) DirectionToWorld(Structure s, long x, long y, long z) => s.Attitude.Rotate(x, y, z);

    // ---- the attitude -----------------------------------------------------------------------------------

    /// <summary>The attitude of a structure (the identity for a station, a structure that was never turned and unknown structures).</summary>
    public Attitude AttitudeOf(int structure) => _structures.TryGetValue(structure, out var s) ? s.Attitude : Attitude.Identity;

    /// <summary>Sets the attitude of the player's ship structure (the command layer has checked it).</summary>
    internal void SetShipAttitude(int player, Attitude attitude)
    {
        if (!_players.TryGetValue(player, out var p) || p.ShipStructure < 0 || !_structures.TryGetValue(p.ShipStructure, out var ship)) return;
        ship.Attitude = attitude;
    }

    /// <summary>Setup and tests: turns a ship structure without a command. Stations cannot be turned.</summary>
    public void SetStructureAttitude(int structure, Attitude attitude)
    {
        var s = GetStructure(structure);
        if (s.Kind != StructureKind.Ship && !attitude.IsIdentity) throw new ArgumentException("Only ships can be turned.", nameof(structure));
        s.Attitude = attitude;
    }

    // ---- boxes -------------------------------------------------------------------------------------------

    /// <summary>The corners of a module box in the structure's own frame (units from the origin): low and size.</summary>
    private static ((long X, long Y, long Z) Lo, (long X, long Y, long Z) Size) ModuleBox((int Mx, int My, int Mz) key)
    {
        long cu = Units.CellUnits;
        return (((long)key.Mx * StationGrid.ModuleX * cu, (long)key.My * StationGrid.ModuleY * cu, (long)key.Mz * StationGrid.ModuleZ * cu),
                (StationGrid.ModuleX * cu, StationGrid.ModuleY * cu, StationGrid.ModuleZ * cu));
    }

    /// <summary>The vector from a sphere centre to the nearest point of a module's box, turned with the structure (zero length when the centre is inside the box).</summary>
    internal BoxVector BoxToPoint(Structure s, (int Mx, int My, int Mz) key, long cx, long cy, long cz)
    {
        var (lo, size) = ModuleBox(key);
        // The centre in the structure's frame, relative to the origin: the box is axis aligned there, whatever the attitude.
        var (lx, ly, lz) = WorldToStructure(s, cx, cy, cz);
        long dx = Math.Clamp(lx, lo.X, lo.X + size.X) - lx, dy = Math.Clamp(ly, lo.Y, lo.Y + size.Y) - ly, dz = Math.Clamp(lz, lo.Z, lo.Z + size.Z) - lz;
        long d2 = dx * dx + dy * dy + dz * dz;
        if (s.Attitude.IsIdentity) return new BoxVector(dx, dy, dz, d2);
        var (wx, wy, wz) = DirectionToWorld(s, dx, dy, dz);
        return new BoxVector(wx, wy, wz, d2);
    }

    /// <summary>
    /// For a sphere centre inside a module box: the direction out through the nearest face, in the world frame, with a length of <paramref name="length"/>
    /// (1 for the axis-aligned case), and the depth to that face.
    /// </summary>
    internal ((long X, long Y, long Z) Dir, long Depth) InsideBoxOf(Structure s, (int Mx, int My, int Mz) key, long cx, long cy, long cz, long length)
    {
        var (lo, size) = ModuleBox(key);
        var (lx, ly, lz) = WorldToStructure(s, cx, cy, cz);
        long[] low = { lo.X, lo.Y, lo.Z }, sz = { size.X, size.Y, size.Z }, c = { lx, ly, lz };
        long best = long.MaxValue; int axis = 1, sign = 1;
        for (int a = 0; a < 3; a++)
        {
            long toLo = c[a] - low[a], toHi = low[a] + sz[a] - c[a];
            if (toLo < best) { best = toLo; axis = a; sign = 1; }
            if (toHi < best) { best = toHi; axis = a; sign = -1; }
        }
        // The centre is nearer the low face: the box must move up the axis to leave it behind, and the other way round.
        var (dx, dy, dz) = (axis == 0 ? sign * length : 0L, axis == 1 ? sign * length : 0L, axis == 2 ? sign * length : 0L);
        return (DirectionToWorld(s, dx, dy, dz), best);
    }

    /// <summary>The smallest axis-aligned box (world units) around a module of a structure: exact when the structure is level, otherwise a safe bound.</summary>
    public ((long X, long Y, long Z) Min, (long X, long Y, long Z) Max) ModuleWorldBounds(Structure s, (int Mx, int My, int Mz) key)
    {
        var (lo, size) = ModuleBox(key);
        if (s.Attitude.IsIdentity) return ((s.OriginX + lo.X, s.OriginY + lo.Y, s.OriginZ + lo.Z), (s.OriginX + lo.X + size.X, s.OriginY + lo.Y + size.Y, s.OriginZ + lo.Z + size.Z));
        long minX = long.MaxValue, minY = long.MaxValue, minZ = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue, maxZ = long.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var (x, y, z) = StructureToWorld(s, lo.X + ((i & 1) != 0 ? size.X : 0), lo.Y + ((i & 2) != 0 ? size.Y : 0), lo.Z + ((i & 4) != 0 ? size.Z : 0));
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        return ((minX, minY, minZ), (maxX, maxY, maxZ));
    }

    // ---- state: hash and check ---------------------------------------------------------------------------

    private static void MixAttitude(Structure s, Action<long> mix)
    {
        if (s.Attitude.IsIdentity) return; // a level ship hashes exactly as before attitudes existed
        mix(-601); mix(s.Attitude.W); mix(s.Attitude.X); mix(s.Attitude.Y); mix(s.Attitude.Z);
    }

    private void ValidateAttitude(Structure s)
    {
        if (s.Attitude.IsIdentity) return;
        if (s.Kind != StructureKind.Ship) throw new InvalidOperationException($"structure {s.Id} is no ship but has an attitude");
        if (!Attitude.TryCreate(s.Attitude.W, s.Attitude.X, s.Attitude.Y, s.Attitude.Z, out var again) || again != s.Attitude)
            throw new InvalidOperationException($"ship {s.Id} has an attitude that is not a canonical unit quaternion");
        if (s.DockedTo >= 0) throw new InvalidOperationException($"ship {s.Id} is docked but not level");
    }
}
