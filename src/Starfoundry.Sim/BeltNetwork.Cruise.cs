namespace Starfoundry.Sim;

/// <summary>
/// The cruise drive and its approach governor (ADR 0053, stage 2 slice). Cruise is a flight mode for the empty void between the systems and the rock clusters, kilometres wide: the engines spin up to
/// the cruise speed in seconds and the ship keeps its course. A governor keeps it from ever arriving fast: the allowed speed is a function of the position only, the braking curve to the edge of the
/// nearest <em>zone</em> (a system's radius, a cluster's reach plus a few field sectors), so with no zone nearby cruise speed is allowed and near one it drops to boost speed. Everything is integer maths.
/// </summary>
public sealed partial class BeltNetwork
{
    /// <summary>True when the player's cruise drive is engaged.</summary>
    public bool IsCruising(int player) => _players.TryGetValue(player, out var p) && p.Cruise;

    /// <summary>Engages or releases the cruise drive (see <see cref="SetCruise"/>). The caller checked <see cref="CanCruise"/>.</summary>
    public void SetCruise(int player, bool on)
    {
        var p = GetOrCreatePlayer(player);
        if (on == p.Cruise) return;
        p.Cruise = on;
        if (on) ClearMining(p); // a ship under way is not mining
    }

    /// <summary>True when the cruise drive may be engaged here and now: the world has the scale, the ship has inertia, and the governor allows at least the drop-out speed at this position.</summary>
    public bool CanCruise(int player)
    {
        if (_scale is null || !FlightInertia || !_players.TryGetValue(player, out var p)) return false;
        return CruiseAllowedMilli(p, _scale) >= (long)_scale.Cruise.DropUnitsPerTick * 1000;
    }

    /// <summary>The speed the governor allows the ship at its position, in thousandths of a unit per tick (the cruise speed far from every zone; 0 without the world scale).</summary>
    public long CruiseAllowed(int player) =>
        _scale is not null && _players.TryGetValue(player, out var p) ? CruiseAllowedMilli(p, _scale) : 0;

    /// <summary>The distance in units from a point to the edge of the nearest zone (0 inside one), or <see cref="long.MaxValue"/> when none is within <paramref name="horizonUnits"/>.</summary>
    public long ZoneEdgeDistance(long xUnits, long yUnits, long zUnits, long horizonUnits) =>
        _scale is null ? long.MaxValue : ZoneEdge(xUnits, yUnits, zUnits, horizonUnits, _scale);

    private long CruiseAllowedMilli(Player p, WorldScaleRules sc)
    {
        long cap = (long)sc.Cruise.SpeedUnitsPerTick * 1000;
        long k = 2000 * sc.Cruise.CurveMilli;
        long horizon = cap * cap / k + 1;
        long d = ZoneEdge(p.X, p.Y, p.Z, horizon, sc);
        return d >= horizon ? cap : Math.Min(cap, IntMath.ISqrt(k * d));
    }

    private long ZoneEdge(long x, long y, long z, long horizon, WorldScaleRules sc)
    {
        long best = long.MaxValue, cu = Units.CellUnits;
        if (Galaxy is { } g)
        {
            long r = (long)g.Rules.SystemRadiusCells * cu;
            foreach (var s in g.Systems) best = Math.Min(best, SphereEdge(x, y, z, s.X * cu, s.Y * cu, s.Z * cu, r, horizon));
        }
        if (_clusters is not null && _field is not null && Galaxy is not null)
        {
            long zone = (long)sc.Cruise.ZoneSectors * _field.SectorCells;
            long box = horizon / cu + 1 + _clusters.MaxReachCells + zone;
            long cx = x / cu, cy = y / cu, cz = z / cu;
            foreach (var p in ClusterField.ParentsInBox(_field, _clusters, Galaxy.Seed, cx - box, cx + box, cy - box, cy + box, cz - box, cz + box, Rules.LegacyResources ? 100 : Rules.ResourceDensityPercent))
            {
                if (ParentBeyondField(p)) continue;
                best = Math.Min(best, SphereEdge(x, y, z, p.X * cu + cu / 2, p.Y * cu + cu / 2, p.Z * cu + cu / 2, (p.Radius + zone) * cu, horizon));
            }
        }
        return best;
    }

    /// <summary>Distance from a point to the surface of a sphere (0 inside), or <see cref="long.MaxValue"/> when farther than the horizon. Checked by box first, so the squares stay small.</summary>
    private static long SphereEdge(long x, long y, long z, long cx, long cy, long cz, long r, long horizon)
    {
        long reach = r + horizon, dx = x - cx, dy = y - cy, dz = z - cz;
        if (Math.Abs(dx) > reach || Math.Abs(dy) > reach || Math.Abs(dz) > reach) return long.MaxValue;
        long d2 = dx * dx + dy * dy + dz * dz;
        if (d2 <= r * r) return 0;
        long d = IntMath.ISqrt(d2) - r;
        return d > horizon ? long.MaxValue : d;
    }

    /// <summary>
    /// The speed update of the cruise drive inside <see cref="MoveInertial"/>: replaces the intent target while engaged and caps the speed to what the governor allows.
    /// Returns the cruise acceleration rate when the drive is pushing, 0 otherwise. Cruise drops out (and releases) when the governor leaves less than the drop-out speed, on a power brownout or without a course.
    /// </summary>
    private void CruiseTarget(Player p, Structure s, long full, ref long tx, ref long ty, ref long tz, out bool pushing)
    {
        pushing = false;
        if (_scale is not { } sc || !p.Cruise) return;
        long allowed = CruiseAllowedMilli(p, sc);
        if (allowed < (long)sc.Cruise.DropUnitsPerTick * 1000 || s.Satisfaction < PowerRules.FullSatisfaction) { p.Cruise = false; return; }
        long vx = p.VelX, vy = p.VelY, vz = p.VelZ;
        long ix = tx, iy = ty, iz = tz;
        if ((ix | iy | iz) == 0) { ix = vx; iy = vy; iz = vz; } // no key held: keep the course
        long len = IntMath.ISqrt(ix * ix + iy * iy + iz * iz);
        if (len == 0) return;
        tx = ix * allowed / len; ty = iy * allowed / len; tz = iz * allowed / len;
        pushing = true;
    }

    /// <summary>The hard cap of the governor: a speed above both the engines and the allowed speed is shed at the braking rate of the drive, keeping the heading. Position-only, so no flight can outrun the curve.</summary>
    private void CruiseCap(Player p, long full)
    {
        if (_scale is not { } sc) return;
        long v2 = (long)p.VelX * p.VelX + (long)p.VelY * p.VelY + (long)p.VelZ * p.VelZ;
        if (v2 <= full * full) return;
        long len = IntMath.ISqrt(v2), limit = Math.Max(full, CruiseAllowedMilli(p, sc));
        if (len <= limit) return;
        long next = Math.Max(limit, len - sc.Cruise.BrakeMilli);
        p.VelX = (int)((long)p.VelX * next / len); p.VelY = (int)((long)p.VelY * next / len); p.VelZ = (int)((long)p.VelZ * next / len);
    }
}
