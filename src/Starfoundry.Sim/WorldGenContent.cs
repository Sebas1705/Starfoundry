using System.Text.Json;

namespace Starfoundry.Sim;

/// <summary>What an obstacle kind does ([ADR 0028](docs/adr/0028-world-generation-obstacles-and-collisions.md)).</summary>
public enum ObstacleBehaviour
{
    /// <summary>A rock a ship cannot pass: it is pushed out and takes collision damage by closing speed.</summary>
    Solid = 0,

    /// <summary>A mine or unstable crystal: a ship coming within the trigger range starts a fuse, then it explodes.</summary>
    Mine = 1,

    /// <summary>A gas or ion region: ships inside it are slowed and their reactors give less power.</summary>
    Cloud = 2,

    /// <summary>A derelict wreck: a ship that stays near it for a while salvages a few items.</summary>
    Wreck = 3,

    /// <summary>Debris that drifts to and fro along a line; hits like a solid rock but is never pushed.</summary>
    Drifter = 4,
}

/// <summary>A star type: weights the pick for a system and scales what the system holds.</summary>
/// <param name="RichnessPercent">Deposit sizes of the system, in percent.</param>
/// <param name="AsteroidPercent">Density of rocks (belts, clusters, scatter), in percent.</param>
/// <param name="HazardPercent">Density of mines, clouds and drifting debris, in percent.</param>
/// <param name="MinDepth">Only systems at least this many jumps from home can have it.</param>
public sealed record StarTypeDef(int Index, string Id, int Weight, int RichnessPercent, int AsteroidPercent, int HazardPercent, int MinDepth);

/// <summary>One kind of obstacle. All lengths are in cells unless named units; speeds are in units per tick.</summary>
/// <param name="RadiusMin">Radius range in cells' tenths (10 = one cell), so small rocks can be a fraction of a cell.</param>
/// <param name="Damage">Solid and drifter: damage at <see cref="WorldGenRules.ReferenceSpeedUnits"/> (a ship at base speed). Mine: damage at the centre of the blast.</param>
/// <param name="TriggerCells">Mine: a ship module within this many cells of the centre arms the fuse.</param>
/// <param name="FuseTicks">Mine: ticks between arming and the explosion.</param>
/// <param name="BlastCells">Mine: radius of the explosion; it also sets off other mines inside it.</param>
/// <param name="SlowPercent">Cloud: percent of the engine speed lost inside.</param>
/// <param name="DrainPercent">Cloud: percent of the reactor output lost inside.</param>
/// <param name="DriftUnitsPerTick">Drifter: speed along its line.</param>
/// <param name="Loot">Wreck: possible contents; one entry is chosen per wreck when the galaxy is generated.</param>
public sealed record ObstacleKindDef(
    int Index, string Id, ObstacleBehaviour Behaviour, int Weight, int RadiusMin, int RadiusMax, int Damage = 0, int TriggerCells = 0, int FuseTicks = 0,
    int BlastCells = 0, int SlowPercent = 0, int DrainPercent = 0, int DriftUnitsPerTick = 0, LootSpec[] Loot = default!)
{
    public LootSpec[] Loot { get; init; } = Loot ?? Array.Empty<LootSpec>();

    /// <summary>
    /// Solid and drifter: the radius a ship hits, in percent of the generated radius (ADR 0033). The model is drawn larger than its nominal radius
    /// (a rock's bumps reach 1.5 to 1.8 times it), so the hit radius follows the drawn size: <c>--measure-models</c> prints the measurement these
    /// numbers come from. 100 (the default) is the nominal radius.
    /// </summary>
    public int HitPercent { get; init; } = 100;
}

/// <summary>Items a wreck can hold: <c>CountMin..CountMax</c> of the item.</summary>
public sealed record LootSpec(int Item, int CountMin, int CountMax);

/// <summary>
/// The world generation, boost and collision rules of the content (the optional <c>world</c> section). Content without it generates
/// the legacy galaxy and has no obstacles, boost or collisions, exactly as before ADR 0028.
/// </summary>
/// <param name="SpacingCells">System spacing of the wide galaxy.</param>
/// <param name="SystemRadiusCells">System radius of the wide galaxy.</param>
/// <param name="HomeStar">The star type of home (an index into <see cref="StarTypes"/>).</param>
/// <param name="AnchorClearCells">Solid rocks and hazards stay this far from a system anchor.</param>
/// <param name="DepositClearCells">Solid rocks stay this far from a deposit.</param>
/// <param name="HazardClearCells">Mines and drifting debris stay this far from an anchor or a deposit.</param>
/// <param name="DepthRichnessPercent">Deposits get this many percent more per jump from home.</param>
/// <param name="OuterRichnessPercent">Deposits at the outer edge of a system get this many percent more than those at the inner edge.</param>
/// <param name="BoostPercent">Engine speed percent while boosting (100 = no boost).</param>
/// <param name="SafeSpeedUnits">Closing speed at or below which a bump does no damage.</param>
/// <param name="ReferenceSpeedUnits">The closing speed at which an obstacle deals its <c>damage</c> (base ship speed).</param>
/// <param name="ImpactCooldownTicks">After a collision a structure takes no further collision damage for this long.</param>
/// <param name="SalvageRangeCells">A ship module within this range of a wreck can salvage it.</param>
/// <param name="SalvageTicks">Ticks of slow flight near a wreck that salvage it.</param>
public sealed record WorldGenRules(
    int SpacingCells, int SystemRadiusCells, int HomeStar, int AnchorClearCells, int DepositClearCells, int HazardClearCells,
    int DepthRichnessPercent, int OuterRichnessPercent, int BoostPercent, int SafeSpeedUnits, int ReferenceSpeedUnits, int ImpactCooldownTicks,
    int SalvageRangeCells, int SalvageTicks, StarTypeDef[] StarTypes, ObstacleKindDef[] Kinds)
{
    /// <summary>
    /// Units added to the hit radius of every rock and debris a ship module meets: the hull is drawn a little beyond its module boxes (ADR 0033).
    /// Applies to ships only (stations, ports and docking are not padded) and not with <see cref="WorldRules.LegacyCollisions"/>. 0: none.
    /// </summary>
    public int HullPadUnits { get; init; }

    /// <summary>True: the rock of a revealed, not exhausted deposit is solid like a rock obstacle (ADR 0033), with <see cref="DepositDamage"/> and <see cref="DepositHitPercent"/>.</summary>
    public bool DepositsSolid { get; init; }

    /// <summary>Damage a solid deposit does at <see cref="ReferenceSpeedUnits"/>.</summary>
    public int DepositDamage { get; init; }

    /// <summary>Hit radius of a deposit rock in percent of its nominal radius (see <see cref="DepositRules.NominalRadiusUnits"/>).</summary>
    public int DepositHitPercent { get; init; } = 100;

    /// <summary>Flight inertia (ADR 0033): ticks a ship at full engine speed needs to reach it from rest. 0: the velocity follows the thrust at once.</summary>
    public int AccelTicks { get; init; }

    /// <summary>Ticks a ship needs to stop from full engine speed (braking is stronger than accelerating).</summary>
    public int BrakeTicks { get; init; }

    /// <summary>The infinite rock field (ADR 0040), or null when the content has none.</summary>
    public FieldRules? Field { get; init; }

    /// <summary>True when the content defines flight inertia.</summary>
    public bool HasInertia => AccelTicks > 0 && BrakeTicks > 0;

    /// <summary>The index of the kind with this id, or -1.</summary>
    public int KindIndex(string id)
    {
        for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].Id == id) return i;
        return -1;
    }

    internal static WorldGenRules Load(JsonElement w, Dictionary<string, int> itemIds, GalaxyRules? galaxy, IReadOnlyList<ArchetypeDef> archetypes)
    {
        if (galaxy is null) throw new InvalidDataException("'world' needs a galaxy section.");
        ContentDb.RequireObjectInternal(w, "world", "spacing_cells", "system_radius_cells", "home_star", "anchor_clear_cells", "deposit_clear_cells", "hazard_clear_cells",
            "depth_richness_percent", "outer_richness_percent", "boost_percent", "collision", "salvage", "star_types", "obstacles", "flight", "field");
        int radius = ContentDb.IntInternal(w, "system_radius_cells", "world", galaxy.SystemRadiusCells, 800);
        int spacing = ContentDb.IntInternal(w, "spacing_cells", "world", galaxy.SpacingCells, 2200);
        if (spacing < 2 * radius) throw new InvalidDataException("'spacing_cells' in world must be at least twice 'system_radius_cells'.");

        ContentDb.RequireObjectInternal(w.GetProperty("collision"), "collision", "safe_speed_units", "reference_speed_units", "cooldown_ticks", "hull_pad_units", "deposits_solid", "deposit_damage", "deposit_hit_percent");
        var col = w.GetProperty("collision");
        int safe = ContentDb.IntInternal(col, "safe_speed_units", "collision", 0, 200);
        int reference = ContentDb.IntInternal(col, "reference_speed_units", "collision", 1, 400);
        if (safe >= reference) throw new InvalidDataException("'safe_speed_units' in collision must be below 'reference_speed_units'.");
        int cooldown = ContentDb.IntInternal(col, "cooldown_ticks", "collision", 1, 600);
        int hullPad = col.TryGetProperty("hull_pad_units", out _) ? ContentDb.IntInternal(col, "hull_pad_units", "collision", 0, 480) : 0;
        bool depositsSolid = col.TryGetProperty("deposits_solid", out var solidEl) && solidEl.ValueKind == JsonValueKind.True;
        if (col.TryGetProperty("deposits_solid", out var solidCheck) && solidCheck.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("'deposits_solid' in collision must be true or false.");
        int depositDamage = col.TryGetProperty("deposit_damage", out _) ? ContentDb.IntInternal(col, "deposit_damage", "collision", 0, 10_000) : 0;
        int depositHit = col.TryGetProperty("deposit_hit_percent", out _) ? ContentDb.IntInternal(col, "deposit_hit_percent", "collision", 50, 400) : 100;
        if (depositsSolid && depositDamage == 0) throw new InvalidDataException("'deposit_damage' in collision is needed when deposits are solid.");
        int accelTicks = 0, brakeTicks = 0;
        if (w.TryGetProperty("flight", out var fl))
        {
            ContentDb.RequireObjectInternal(fl, "flight", "accel_ticks", "brake_ticks");
            accelTicks = ContentDb.IntInternal(fl, "accel_ticks", "flight", 1, 600);
            brakeTicks = ContentDb.IntInternal(fl, "brake_ticks", "flight", 1, 600);
        }

        ContentDb.RequireObjectInternal(w.GetProperty("salvage"), "salvage", "range_cells", "ticks");
        var sal = w.GetProperty("salvage");

        var stars = new List<StarTypeDef>();
        var starIds = new HashSet<string>();
        foreach (var e in ContentDb.ArrayInternal(w, "star_types"))
        {
            ContentDb.RequireObjectInternal(e, "star type", "id", "weight", "richness_percent", "asteroid_percent", "hazard_percent", "min_depth");
            string id = ContentDb.IdInternal(e, "star type");
            if (!starIds.Add(id)) throw new InvalidDataException($"Duplicate star type '{id}'.");
            stars.Add(new StarTypeDef(stars.Count, id, ContentDb.IntInternal(e, "weight", id, 1, 1000), ContentDb.IntInternal(e, "richness_percent", id, 25, 400),
                ContentDb.IntInternal(e, "asteroid_percent", id, 0, 400), ContentDb.IntInternal(e, "hazard_percent", id, 0, 400),
                e.TryGetProperty("min_depth", out _) ? ContentDb.IntInternal(e, "min_depth", id, 0, 32) : 0));
        }
        if (stars.Count == 0 || stars.Count > 16) throw new InvalidDataException("'star_types' in world needs 1 to 16 entries.");
        string homeStarId = ContentDb.StrInternal(w, "home_star", "world");
        int homeStar = stars.FindIndex(s => s.Id == homeStarId);
        if (homeStar < 0) throw new InvalidDataException($"'home_star' in world names unknown star type '{homeStarId}'.");
        if (stars.All(s => s.MinDepth > 0)) throw new InvalidDataException("'star_types' in world needs a type without a minimum depth.");

        var kinds = new List<ObstacleKindDef>();
        var kindIds = new HashSet<string>();
        foreach (var e in ContentDb.ArrayInternal(w, "obstacles"))
        {
            ContentDb.RequireObjectInternal(e, "obstacle kind", "id", "behaviour", "weight", "radius_tenths", "damage", "trigger_cells", "fuse_ticks", "blast_cells",
                "slow_percent", "drain_percent", "drift_units_per_tick", "loot", "hit_percent");
            string id = ContentDb.IdInternal(e, "obstacle kind");
            if (!kindIds.Add(id)) throw new InvalidDataException($"Duplicate obstacle kind '{id}'.");
            string b = ContentDb.StrInternal(e, "behaviour", id);
            var behaviour = b switch
            {
                "solid" => ObstacleBehaviour.Solid, "mine" => ObstacleBehaviour.Mine, "cloud" => ObstacleBehaviour.Cloud,
                "wreck" => ObstacleBehaviour.Wreck, "drifter" => ObstacleBehaviour.Drifter,
                _ => throw new InvalidDataException($"Obstacle kind '{id}' has an unknown behaviour '{b}'."),
            };
            var (rmin, rmax) = ContentDb.RangeInternal(e, "radius_tenths", id, 5, 1000);
            int Opt(string name, int max) => e.TryGetProperty(name, out _) ? ContentDb.IntInternal(e, name, id, 0, max) : 0;
            var loot = new List<LootSpec>();
            if (e.TryGetProperty("loot", out _))
            {
                foreach (var l in ContentDb.ArrayInternal(e, "loot"))
                {
                    ContentDb.RequireObjectInternal(l, "loot entry", "item", "count");
                    string item = ContentDb.StrInternal(l, "item", $"loot of '{id}'");
                    if (!itemIds.TryGetValue(item, out int itemIndex)) throw new InvalidDataException($"Obstacle kind '{id}' uses unknown item '{item}'.");
                    var (cmin, cmax) = ContentDb.RangeInternal(l, "count", id, 1, 200);
                    loot.Add(new LootSpec(itemIndex, cmin, cmax));
                }
            }
            var def = new ObstacleKindDef(kinds.Count, id, behaviour, ContentDb.IntInternal(e, "weight", id, 1, 1000), rmin, rmax, Opt("damage", 10_000), Opt("trigger_cells", 100),
                Opt("fuse_ticks", 300), Opt("blast_cells", 100), Opt("slow_percent", 90), Opt("drain_percent", 90), Opt("drift_units_per_tick", 100), loot.ToArray())
            {
                HitPercent = e.TryGetProperty("hit_percent", out _) ? ContentDb.IntInternal(e, "hit_percent", id, 50, 400) : 100,
            };
            switch (behaviour)
            {
                case ObstacleBehaviour.Solid or ObstacleBehaviour.Drifter when def.Damage == 0: throw new InvalidDataException($"Obstacle kind '{id}' needs a damage.");
                case ObstacleBehaviour.Mine when def.Damage == 0 || def.TriggerCells == 0 || def.FuseTicks == 0 || def.BlastCells == 0:
                    throw new InvalidDataException($"Mine kind '{id}' needs damage, trigger_cells, fuse_ticks and blast_cells.");
                case ObstacleBehaviour.Cloud when def.SlowPercent == 0 && def.DrainPercent == 0: throw new InvalidDataException($"Cloud kind '{id}' needs slow_percent or drain_percent.");
                case ObstacleBehaviour.Wreck when def.Loot.Length == 0: throw new InvalidDataException($"Wreck kind '{id}' needs loot.");
                case ObstacleBehaviour.Drifter when def.DriftUnitsPerTick == 0: throw new InvalidDataException($"Drifter kind '{id}' needs drift_units_per_tick.");
            }
            kinds.Add(def);
        }
        foreach (var needed in new[] { ObstacleBehaviour.Solid, ObstacleBehaviour.Mine, ObstacleBehaviour.Cloud, ObstacleBehaviour.Wreck, ObstacleBehaviour.Drifter })
            if (!kinds.Any(k => k.Behaviour == needed)) throw new InvalidDataException($"'obstacles' in world needs a '{needed}' kind.");
        if (kinds.Count > 32) throw new InvalidDataException("'obstacles' in world has at most 32 kinds.");

        foreach (var a in archetypes)
            if (a.WideDistanceMax > radius) throw new InvalidDataException($"Archetype '{a.Id}' places wide deposits outside the wide system radius.");

        return new WorldGenRules(spacing, radius, homeStar,
            ContentDb.IntInternal(w, "anchor_clear_cells", "world", 0, 400), ContentDb.IntInternal(w, "deposit_clear_cells", "world", 0, 400),
            ContentDb.IntInternal(w, "hazard_clear_cells", "world", 0, 400), ContentDb.IntInternal(w, "depth_richness_percent", "world", 0, 100),
            ContentDb.IntInternal(w, "outer_richness_percent", "world", 0, 300), ContentDb.IntInternal(w, "boost_percent", "world", 100, 1000), safe, reference, cooldown,
            ContentDb.IntInternal(sal, "range_cells", "salvage", 1, 100), ContentDb.IntInternal(sal, "ticks", "salvage", 1, 3600), stars.ToArray(), kinds.ToArray())
        {
            HullPadUnits = hullPad, DepositsSolid = depositsSolid, DepositDamage = depositDamage, DepositHitPercent = depositHit, AccelTicks = accelTicks, BrakeTicks = brakeTicks,
            Field = w.TryGetProperty("field", out var fieldEl) ? FieldRules.Load(fieldEl, itemIds) : null,
        };
    }

    internal void MixInto(Action<long> mix, Action<string> mixStr)
    {
        mix(SpacingCells); mix(SystemRadiusCells); mix(HomeStar); mix(AnchorClearCells); mix(DepositClearCells); mix(HazardClearCells);
        mix(DepthRichnessPercent); mix(OuterRichnessPercent); mix(BoostPercent); mix(SafeSpeedUnits); mix(ReferenceSpeedUnits); mix(ImpactCooldownTicks);
        mix(SalvageRangeCells); mix(SalvageTicks);
        // ADR 0033 values hash only when set, so content without them keeps its hash.
        if (HullPadUnits != 0 || DepositsSolid || DepositDamage != 0 || DepositHitPercent != 100 || AccelTicks != 0 || BrakeTicks != 0)
        {
            mix(-3301); mix(HullPadUnits); mix(DepositsSolid ? 1 : 0); mix(DepositDamage); mix(DepositHitPercent); mix(AccelTicks); mix(BrakeTicks);
        }
        Field?.MixInto(mix); // hashes only when the content has a field (ADR 0040)
        mix(StarTypes.Length);
        foreach (var s in StarTypes) { mixStr(s.Id); mix(s.Weight); mix(s.RichnessPercent); mix(s.AsteroidPercent); mix(s.HazardPercent); mix(s.MinDepth); }
        mix(Kinds.Length);
        foreach (var k in Kinds)
        {
            mixStr(k.Id); mix((int)k.Behaviour); mix(k.Weight); mix(k.RadiusMin); mix(k.RadiusMax); mix(k.Damage); mix(k.TriggerCells); mix(k.FuseTicks); mix(k.BlastCells);
            mix(k.SlowPercent); mix(k.DrainPercent); mix(k.DriftUnitsPerTick);
            if (k.HitPercent != 100) { mix(-3302); mix(k.HitPercent); }
            mix(k.Loot.Length);
            foreach (var l in k.Loot) { mix(l.Item); mix(l.CountMin); mix(l.CountMax); }
        }
    }
}
