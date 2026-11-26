using System.Text.Json;

namespace Starfoundry.Sim;

/// <summary>One item the infinite rock field can hold, with its weight and the distance from home (cells) at which it starts to appear.</summary>
public sealed record FieldItemDef(int Item, int Weight, int MinDistanceCells);

/// <summary>
/// The infinite rock field (ADR 0040), the optional <c>field</c> object of the <c>world</c> section: resource asteroids generated on demand,
/// sector by sector, from the world seed. All lengths are in cells. Content without it has no field (the finite galaxy only).
/// </summary>
/// <param name="SectorCells">Side of one cubic sector.</param>
/// <param name="LoadCells">Sectors within this distance of a ship are materialized.</param>
/// <param name="KeepCells">Materialized sectors farther than this from every ship are forgotten (their mined state is remembered).</param>
/// <param name="ExclusionCells">No rock lies within this distance of a system anchor (the start and the systems stay as generated).</param>
/// <param name="MarginCells">Rocks keep this far from the faces of their sector, so a sector never overlaps its neighbours.</param>
/// <param name="RocksMin">Rocks per sector, inclusive range, at a resource density of 100.</param>
/// <param name="MassMin">Rock mass range in thousandths of the reference rock (radius grows with the cube root of the mass).</param>
/// <param name="MassBias">Exponent of the mass draw: 1 is uniform, 4 gives many small rocks and a few huge ones.</param>
/// <param name="DensityMin">Density range in percent (as <see cref="DepositRules"/>): it scales both the amount and the mining speed.</param>
/// <param name="UnitAmount">Ore a reference rock (mass 1000) of density 100 holds.</param>
/// <param name="StepTicks">Ticks between two streaming passes.</param>
/// <param name="MaxLoaded">Most rocks materialized at once (a cap on memory and on every per-deposit loop).</param>
/// <param name="WorldLimitCells">The ship, stations and enemies may go this far from the origin on each axis (instead of the finite world cube).</param>
public sealed record FieldRules(
    int SectorCells, int LoadCells, int KeepCells, int ExclusionCells, int MarginCells, int RocksMin, int RocksMax, int MassMin, int MassMax, int MassBias,
    int DensityMin, int DensityMax, int UnitAmount, int StepTicks, int MaxLoaded, int WorldLimitCells, FieldItemDef[] Items)
{
    /// <summary>Two rocks of a sector keep at least this many cells between their surfaces.</summary>
    public const int GapCells = 3;

    /// <summary>Position tries per rock before it is dropped.</summary>
    public const int Tries = 12;

    /// <summary>The world limit in units.</summary>
    public int WorldLimitUnits => WorldLimitCells * Units.CellUnits;

    /// <summary>Radius in units of a rock of this mass: the reference radius (a rock of mass 1000) times the cube root of mass / 1000.</summary>
    public static int RadiusOfMass(int mass) => (int)(DepositRules.BaseRockRadiusUnits * IntMath.ICbrt(Math.Max(1L, mass) * 1_000_000L) / 1000);

    /// <summary>Ore a rock of this mass and density holds (at least 1): <c>mass * density * unit / 100000</c>.</summary>
    public int AmountOf(int mass, int density) => (int)Math.Max(1L, (long)mass * density * UnitAmount / 100_000);

    internal static FieldRules Load(JsonElement f, Dictionary<string, int> itemIds)
    {
        ContentDb.RequireObjectInternal(f, "field", "sector_cells", "load_cells", "keep_cells", "exclusion_cells", "margin_cells", "rocks", "mass", "mass_bias", "density",
            "unit_amount", "step_ticks", "max_loaded", "world_limit_cells", "items");
        int sector = ContentDb.IntInternal(f, "sector_cells", "field", 40, 2000);
        int load = ContentDb.IntInternal(f, "load_cells", "field", 40, 4000);
        int keep = ContentDb.IntInternal(f, "keep_cells", "field", 40, 6000);
        if (keep <= load + sector / 4) throw new InvalidDataException("'keep_cells' in field must exceed 'load_cells' by more than a quarter of a sector.");
        int margin = ContentDb.IntInternal(f, "margin_cells", "field", 0, sector / 4);
        var (rmin, rmax) = ContentDb.RangeInternal(f, "rocks", "field", 0, 100);
        var (mmin, mmax) = ContentDb.RangeInternal(f, "mass", "field", 10, 100_000);
        var (dmin, dmax) = ContentDb.RangeInternal(f, "density", "field", DepositRules.MinDensity, DepositRules.MaxDensity);
        int maxRadiusCells = (RadiusOfMass(mmax) * 160 / 100 + Units.CellUnits - 1) / Units.CellUnits; // the drawn rock reaches about 1.6 times its radius
        if (margin < maxRadiusCells) throw new InvalidDataException($"'margin_cells' in field must be at least {maxRadiusCells} (the biggest rock).");
        var items = new List<FieldItemDef>();
        var seen = new HashSet<int>();
        foreach (var e in ContentDb.ArrayInternal(f, "items"))
        {
            ContentDb.RequireObjectInternal(e, "field item", "item", "weight", "min_distance_cells");
            string id = ContentDb.StrInternal(e, "item", "field item");
            if (!itemIds.TryGetValue(id, out int item)) throw new InvalidDataException($"'field' uses unknown item '{id}'.");
            if (!seen.Add(item)) throw new InvalidDataException($"'field' lists item '{id}' twice.");
            items.Add(new FieldItemDef(item, ContentDb.IntInternal(e, "weight", id, 1, 1000), e.TryGetProperty("min_distance_cells", out _) ? ContentDb.IntInternal(e, "min_distance_cells", id, 0, 1_000_000) : 0));
        }
        if (items.Count == 0 || items.Count > 32) throw new InvalidDataException("'items' in field needs 1 to 32 entries.");
        if (!items.Any(i => i.MinDistanceCells == 0)) throw new InvalidDataException("'items' in field needs an item with min_distance_cells 0.");
        return new FieldRules(sector, load, keep, ContentDb.IntInternal(f, "exclusion_cells", "field", 0, 4000), margin, rmin, rmax, mmin, mmax,
            ContentDb.IntInternal(f, "mass_bias", "field", 1, 8), dmin, dmax, ContentDb.IntInternal(f, "unit_amount", "field", 1, 100_000),
            ContentDb.IntInternal(f, "step_ticks", "field", 1, 600), ContentDb.IntInternal(f, "max_loaded", "field", 16, 100_000),
            ContentDb.IntInternal(f, "world_limit_cells", "field", 20_000, 4_000_000), items.ToArray());
    }

    internal void MixInto(Action<long> mix)
    {
        mix(-3401);
        mix(SectorCells); mix(LoadCells); mix(KeepCells); mix(ExclusionCells); mix(MarginCells); mix(RocksMin); mix(RocksMax); mix(MassMin); mix(MassMax); mix(MassBias);
        mix(DensityMin); mix(DensityMax); mix(UnitAmount); mix(StepTicks); mix(MaxLoaded); mix(WorldLimitCells);
        mix(Items.Length);
        foreach (var i in Items) { mix(i.Item); mix(i.Weight); mix(i.MinDistanceCells); }
    }
}
