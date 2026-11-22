namespace Starfoundry.Sim;

/// <summary>What happened to an obstacle this tick, for effects, sounds and toasts. Not part of the simulation state.</summary>
public enum ObstacleEventKind
{
    /// <summary>A structure module hit a rock or drifting debris: <c>Damage</c> is what reached the module, <c>Speed</c> the closing speed in units per tick.</summary>
    Impact = 0,

    /// <summary>A mine or crystal armed its fuse.</summary>
    Armed = 1,

    /// <summary>A mine or crystal exploded.</summary>
    Explosion = 2,

    /// <summary>A wreck was salvaged: <c>Item</c> and <c>Count</c> went into the hold.</summary>
    Salvaged = 3,

    /// <summary>A bump so gentle that it did no damage.</summary>
    Bump = 4,
}

public readonly record struct ObstacleEvent(ObstacleEventKind Kind, int Obstacle, int Structure, int Damage, int Speed, int Item = -1, int Count = 0);

/// <summary>Read-only view of an obstacle: its place now (units), radius (units) and what became of it.</summary>
public readonly record struct ObstacleInfo(
    int Id, int System, int Kind, ObstacleBehaviour Behaviour, long X, long Y, long Z, int Radius, bool Gone, int FuseTicks, int SalvageTicks, int LootItem, int LootCount, int Seed);

/// <summary>What the clouds around a structure do to it: percent of the engine speed lost and of the reactor output lost.</summary>
public readonly record struct CloudEffect(int SlowPercent, int DrainPercent, int Cloud);

internal sealed class ObstacleState
{
    /// <summary>A mine that exploded or a wreck that was salvaged.</summary>
    public bool Gone;

    /// <summary>Mines: ticks until the explosion (0 = not armed).</summary>
    public int Fuse;

    /// <summary>Wrecks: ticks a ship has spent salvaging.</summary>
    public int Salvage;

    public ObstacleState Clone() => new() { Gone = Gone, Fuse = Fuse, Salvage = Salvage };
}

/// <summary>Fixed rules of obstacles and collisions that are not content. Part of the simulation, so changing them changes states.</summary>
public static class ObstacleRules
{
    /// <summary>A collision never does more than this many times the obstacle's damage, however fast the ship is.</summary>
    public const int MaxDamageFactor = 16;

    /// <summary>A ship salvages a wreck only while it flies at most this many times the safe speed.</summary>
    public const int SalvageSpeedFactor = 2;
}

public sealed partial class BeltNetwork
{
    private ObstacleState[] _obstacleState = Array.Empty<ObstacleState>();
    private readonly List<ObstacleEvent> _obstacleEvents = new();

    /// <summary>What obstacles did during the last tick (effects, sounds and toasts only).</summary>
    public IReadOnlyList<ObstacleEvent> ObstacleEvents => _obstacleEvents;

    /// <summary>Number of obstacles of the galaxy (0 for a legacy galaxy or without content rules).</summary>
    public int ObstacleCount => Galaxy?.Obstacles.Count ?? 0;

    private WorldGenRules? WorldGen => Content?.World;

    private void InitObstacles()
    {
        int n = Galaxy?.Obstacles.Count ?? 0;
        _obstacleState = new ObstacleState[n];
        for (int i = 0; i < n; i++) _obstacleState[i] = new ObstacleState();
    }

    // ---- positions -------------------------------------------------------------------------------------

    /// <summary>The centre of an obstacle at a tick (units). Drifters move along their axis in a triangle wave; everything else stands still.</summary>
    public (long X, long Y, long Z) ObstacleCentreAt(int id, long tick)
    {
        var g = Galaxy!.Obstacles[id];
        if (g.Amplitude == 0) return (g.X, g.Y, g.Z);
        int speed = WorldGen!.Kinds[g.Kind].DriftUnitsPerTick;
        long period = 4L * g.Amplitude;
        long s = ((long)g.Phase + tick * speed) % period;
        long off = s < 2L * g.Amplitude ? s - g.Amplitude : 3L * g.Amplitude - s;
        return (g.X + (g.Axis == 0 ? off : 0), g.Y + (g.Axis == 1 ? off : 0), g.Z + (g.Axis == 2 ? off : 0));
    }

    public ObstacleInfo GetObstacle(int id)
    {
        if (Galaxy is null || (uint)id >= (uint)Galaxy.Obstacles.Count) throw new ArgumentOutOfRangeException(nameof(id));
        var g = Galaxy.Obstacles[id];
        var st = _obstacleState[id];
        var c = ObstacleCentreAt(id, Tick);
        return new ObstacleInfo(id, g.System, g.Kind, WorldGen!.Kinds[g.Kind].Behaviour, c.X, c.Y, c.Z, g.Radius, st.Gone, st.Fuse, st.Salvage, g.LootItem, g.LootCount, g.Seed);
    }

    // ---- boost -----------------------------------------------------------------------------------------

    /// <summary>Switches the player's boost on or off (see <see cref="SetBoost"/>).</summary>
    public void SetBoost(int player, bool on)
    {
        if (WorldGen is null) throw new InvalidOperationException("The content has no boost.");
        GetOrCreatePlayer(player).Boost = on;
    }

    public bool IsBoosting(int player) => _players.TryGetValue(player, out var p) && p.Boost;

    // ---- clouds ----------------------------------------------------------------------------------------

    /// <summary>The system a structure is in, judged by its origin: the cell corner of its first module.</summary>
    private int SystemOfStructure(Structure s) => Galaxy is null ? -1 : Galaxy.SystemAt(s.OriginX, s.OriginY, s.OriginZ);

    /// <summary>
    /// The strongest slow and the strongest drain among the clouds that any module of the structure touches. Stateless: computed from the
    /// positions of now, so there is nothing to save. Zero for a legacy galaxy.
    /// </summary>
    public CloudEffect CloudEffectOf(int structure)
    {
        if (Galaxy is null || Galaxy.Obstacles.Count == 0 || WorldGen is null || !_structures.TryGetValue(structure, out var s)) return default;
        int sys = SystemOfStructure(s);
        if (sys < 0) return default;
        int slow = 0, drain = 0, first = -1;
        foreach (int i in Galaxy.ObstaclesOf(sys))
        {
            var def = WorldGen.Kinds[Galaxy.Obstacles[i].Kind];
            if (def.Behaviour != ObstacleBehaviour.Cloud) continue;
            var g = Galaxy.Obstacles[i];
            if (!SphereTouchesStructure(s, g.X, g.Y, g.Z, g.Radius)) continue;
            if (first < 0) first = i;
            slow = Math.Max(slow, def.SlowPercent);
            drain = Math.Max(drain, def.DrainPercent);
        }
        return new CloudEffect(slow, drain, first);
    }

    private bool SphereTouchesStructure(Structure s, long cx, long cy, long cz, long radius)
    {
        foreach (var key in s.ModuleMap.Keys)
            if (BoxToPoint(s, key, cx, cy, cz).D2 <= radius * radius) return true;
        return false;
    }

    // ---- the tick --------------------------------------------------------------------------------------

    /// <summary>
    /// Runs once a tick after the ships have moved (structure worlds with obstacles only): solid rocks push ships out and hurt the module that
    /// hit them, drifting debris hurts whatever it crosses, mines count their fuse down and explode, wrecks are salvaged by ships that linger.
    /// Structures, obstacles and modules are visited in id order, so it is deterministic.
    /// </summary>
    private void StepObstacles()
    {
        _obstacleEvents.Clear();
        if (!StructureRules || Galaxy is null || Galaxy.Obstacles.Count == 0 || WorldGen is not { } w || _structures.Count == 0) return;

        foreach (var s in _structures.Values)
        {
            if (s.ImpactCooldown > 0) s.ImpactCooldown--;
        }

        // Fuses count down first so a mine that is armed this tick explodes no sooner than its fuse says.
        var exploding = new List<int>();
        for (int i = 0; i < _obstacleState.Length; i++)
        {
            var st = _obstacleState[i];
            if (st.Fuse > 0 && --st.Fuse == 0) exploding.Add(i);
        }

        foreach (var s in _structures.ToArray().Select(kv => kv.Value))
        {
            if (!_structures.ContainsKey(s.Id) || s.ModuleMap.Count == 0) continue;
            int sys = SystemOfStructure(s);
            if (sys < 0) continue;
            Player? pilot = null;
            if (s.Kind == StructureKind.Ship) foreach (var p in _players.Values) if (p.ShipStructure == s.Id) { pilot = p; break; }
            if (s.Kind == StructureKind.Ship && pilot is null) continue;

            foreach (int i in Galaxy.ObstaclesOf(sys))
            {
                var st = _obstacleState[i];
                if (st.Gone) continue;
                var g = Galaxy.Obstacles[i];
                var def = w.Kinds[g.Kind];
                switch (def.Behaviour)
                {
                    case ObstacleBehaviour.Solid or ObstacleBehaviour.Drifter:
                        if (def.Behaviour == ObstacleBehaviour.Solid && s.Kind != StructureKind.Ship) break; // a station never moves into a rock
                        Collide(s, pilot, i, def, w);
                        break;
                    case ObstacleBehaviour.Mine:
                        if (st.Fuse == 0 && s.Kind == StructureKind.Ship && SphereTouchesStructure(s, g.X, g.Y, g.Z, g.Radius + (long)def.TriggerCells * Units.CellUnits))
                        {
                            st.Fuse = def.FuseTicks;
                            _obstacleEvents.Add(new ObstacleEvent(ObstacleEventKind.Armed, i, s.Id, 0, 0));
                        }
                        break;
                    case ObstacleBehaviour.Wreck:
                        if (s.Kind == StructureKind.Ship && pilot is not null) Salvage(s, pilot, i, st, g, w);
                        break;
                }
            }
        }

        foreach (int i in exploding) Explode(i, w);
    }

    private void Collide(Structure s, Player? pilot, int id, ObstacleKindDef def, WorldGenRules w)
    {
        var g = Galaxy!.Obstacles[id];
        var c = ObstacleCentreAt(id, Tick);
        var c2 = ObstacleCentreAt(id, Tick + 1);
        long ovx = c2.X - c.X, ovy = c2.Y - c.Y, ovz = c2.Z - c.Z;
        long svx = pilot?.StepDx ?? 0, svy = pilot?.StepDy ?? 0, svz = pilot?.StepDz ?? 0;
        bool push = def.Behaviour == ObstacleBehaviour.Solid;

        // The deepest module is the one that hits.
        (int, int, int) hitKey = default; long hitPen = 0; (long X, long Y, long Z) hitDir = default; long hitDist = 0;
        long r = g.Radius;
        foreach (var key in s.ModuleMap.Keys)
        {
            var box = BoxToPoint(s, key, c.X, c.Y, c.Z);
            long d2 = box.D2;
            if (d2 >= r * r) continue;
            long dist = IntMath.ISqrt(d2);
            long pen;
            (long, long, long) dir;
            long norm = 1;
            if (d2 == 0)
            {
                var (inDir, depth) = InsideBoxOf(s, key, c.X, c.Y, c.Z, 1);
                pen = depth + r; dir = inDir; dist = 1;
                // A turned ship: the direction gets the length of the penetration (a unit vector would round away), and the length divides it again.
                if (!s.Attitude.IsIdentity) { dir = InsideBoxOf(s, key, c.X, c.Y, c.Z, Math.Max(1, pen)).Dir; norm = Math.Max(1, pen); }
            }
            else { dir = (box.X, box.Y, box.Z); pen = r - dist; norm = dist; }
            if (pen > hitPen) { hitPen = pen; hitKey = key; hitDir = dir; hitDist = norm; }
        }
        if (hitPen == 0) return;

        // Closing speed: how fast the module moves into the obstacle along the contact normal (units per tick).
        long rvx = svx - ovx, rvy = svy - ovy, rvz = svz - ovz;
        long closing = -(rvx * hitDir.X + rvy * hitDir.Y + rvz * hitDir.Z) / hitDist;
        // Debris is not pushed and may sweep through a module: what counts is how fast the two move relative to each other, whichever way.
        if (!push) closing = IntMath.ISqrt(rvx * rvx + rvy * rvy + rvz * rvz);

        if (push && pilot is not null)
        {
            // Out along the normal by the depth, plus a unit so the next tick starts clear.
            long len = Math.Max(1, hitDist);
            long px = hitDir.X * hitPen / len, py = hitDir.Y * hitPen / len, pz = hitDir.Z * hitPen / len;
            if (px == 0 && py == 0 && pz == 0) py = 1;
            PushShip(pilot, s, px + Math.Sign(px), py + Math.Sign(py), pz + Math.Sign(pz));
        }

        if (s.ImpactCooldown > 0 || closing <= 0) return;
        if (closing <= w.SafeSpeedUnits)
        {
            _obstacleEvents.Add(new ObstacleEvent(ObstacleEventKind.Bump, id, s.Id, 0, (int)closing));
            return;
        }
        long c2s = closing * closing - (long)w.SafeSpeedUnits * w.SafeSpeedUnits, span = (long)w.ReferenceSpeedUnits * w.ReferenceSpeedUnits - (long)w.SafeSpeedUnits * w.SafeSpeedUnits;
        long damage = Math.Min((long)def.Damage * ObstacleRules.MaxDamageFactor, Math.Max(1, (long)def.Damage * c2s / span));
        s.ImpactCooldown = w.ImpactCooldownTicks;
        int hits = _moduleHits.Count;
        DamageModule(s.Id, hitKey, (int)damage);
        int dealt = _moduleHits.Count > hits ? _moduleHits[^1].Damage : 0; // after the shields
        _obstacleEvents.Add(new ObstacleEvent(ObstacleEventKind.Impact, id, s.Id, dealt, (int)closing));
    }

    private void PushShip(Player p, Structure s, long dx, long dy, long dz)
    {
        long nx = Math.Clamp(p.X + dx, -PlayerRules.WorldLimitUnits, PlayerRules.WorldLimitUnits);
        long ny = Math.Clamp(p.Y + dy, -PlayerRules.WorldLimitUnits, PlayerRules.WorldLimitUnits);
        long nz = Math.Clamp(p.Z + dz, -PlayerRules.WorldLimitUnits, PlayerRules.WorldLimitUnits);
        s.OriginX += nx - p.X; s.OriginY += ny - p.Y; s.OriginZ += nz - p.Z;
        p.X = (int)nx; p.Y = (int)ny; p.Z = (int)nz;
    }

    private void Salvage(Structure s, Player pilot, int id, ObstacleState st, GeneratedObstacle g, WorldGenRules w)
    {
        long speed2 = (long)pilot.StepDx * pilot.StepDx + (long)pilot.StepDy * pilot.StepDy + (long)pilot.StepDz * pilot.StepDz;
        long slow = (long)w.SafeSpeedUnits * ObstacleRules.SalvageSpeedFactor;
        bool near = SphereTouchesStructure(s, g.X, g.Y, g.Z, g.Radius + (long)w.SalvageRangeCells * Units.CellUnits);
        if (!near || speed2 > slow * slow || pilot.ShipLost) { st.Salvage = 0; return; }
        if (++st.Salvage < w.SalvageTicks) return;
        int room = Math.Max(0, HoldCapacityOf(pilot) - pilot.HoldTotal);
        if (room == 0) { st.Salvage = w.SalvageTicks; return; } // wait for room in the hold
        int take = Math.Min(room, g.LootCount);
        AddToHold(pilot, g.LootItem, take);
        st.Gone = true;
        st.Salvage = 0;
        _obstacleEvents.Add(new ObstacleEvent(ObstacleEventKind.Salvaged, id, s.Id, 0, 0, g.LootItem, take));
        StructureVersion++;
    }

    private void Explode(int id, WorldGenRules w)
    {
        var st = _obstacleState[id];
        if (st.Gone) return;
        st.Gone = true;
        st.Fuse = 0;
        StructureVersion++;
        var g = Galaxy!.Obstacles[id];
        var def = w.Kinds[g.Kind];
        long blast = (long)def.BlastCells * Units.CellUnits;
        _obstacleEvents.Add(new ObstacleEvent(ObstacleEventKind.Explosion, id, -1, def.Damage, 0));

        // Collect first: a destroyed module or station changes the tables the loops walk.
        var hits = new List<(int Structure, (int, int, int) Key, int Damage)>();
        foreach (var s in _structures.Values)
        {
            if (SystemOfStructure(s) != g.System) continue;
            foreach (var key in s.ModuleMap.Keys)
            {
                long dist = IntMath.ISqrt(BoxToPoint(s, key, g.X, g.Y, g.Z).D2);
                if (dist >= blast) continue;
                hits.Add((s.Id, key, (int)Math.Max(1, def.Damage * (blast - dist) / blast)));
            }
        }
        foreach (var (structure, key, damage) in hits) DamageModule(structure, key, damage);

        // The blast sets off the mines next to it.
        for (int j = 0; j < _obstacleState.Length; j++)
        {
            var other = Galaxy.Obstacles[j];
            var od = w.Kinds[other.Kind];
            if (j == id || other.System != g.System || od.Behaviour != ObstacleBehaviour.Mine || _obstacleState[j].Gone || _obstacleState[j].Fuse > 0) continue;
            long dx = other.X - g.X, dy = other.Y - g.Y, dz = other.Z - g.Z;
            if (dx * dx + dy * dy + dz * dz <= blast * blast)
            {
                _obstacleState[j].Fuse = od.FuseTicks;
                _obstacleEvents.Add(new ObstacleEvent(ObstacleEventKind.Armed, j, -1, 0, 0));
            }
        }
    }

    // ---- state: copy, hash, check ------------------------------------------------------------------------

    private void CopyObstaclesFrom(BeltNetwork other)
    {
        _obstacleState = other._obstacleState.Select(o => o.Clone()).ToArray();
    }

    private void MixObstacles(Action<long> mix)
    {
        for (int i = 0; i < _obstacleState.Length; i++)
        {
            var o = _obstacleState[i];
            if (!o.Gone && o.Fuse == 0 && o.Salvage == 0) continue; // untouched obstacles hash as nothing
            mix(-501); mix(i); mix(o.Gone ? 1 : 0); mix(o.Fuse); mix(o.Salvage);
        }
    }

    private void ValidateObstacles()
    {
        static void Fail(string msg) => throw new InvalidOperationException(msg);
        int n = Galaxy?.Obstacles.Count ?? 0;
        if (_obstacleState.Length != n) Fail("obstacle state has the wrong size");
        for (int i = 0; i < n; i++)
        {
            var o = _obstacleState[i];
            var def = WorldGen!.Kinds[Galaxy!.Obstacles[i].Kind];
            if (o.Fuse < 0 || o.Fuse > def.FuseTicks) Fail($"obstacle {i} fuse out of range");
            if (o.Salvage < 0 || o.Salvage > WorldGen.SalvageTicks) Fail($"obstacle {i} salvage out of range");
            if (o.Gone && def.Behaviour is not (ObstacleBehaviour.Mine or ObstacleBehaviour.Wreck)) Fail($"obstacle {i} is gone but cannot be");
        }
        foreach (var s in _structures.Values)
            if (s.ImpactCooldown < 0 || (WorldGen is null && s.ImpactCooldown != 0) || (WorldGen is not null && s.ImpactCooldown > WorldGen.ImpactCooldownTicks)) Fail($"structure {s.Id} impact cooldown out of range");
    }
}
