using System.Text.Json;
using Starfoundry.Sim;

namespace Starfoundry.Ui;

/// <summary>Mass and bulk density of one item kind: the mass of a single item (a lot) and the density of the loose material.</summary>
public readonly record struct ItemPhysics(double Kg, double DensityKgPerM3)
{
    /// <summary>The volume of one item in cubic metres.</summary>
    public double VolumeM3 => DensityKgPerM3 > 0 ? Kg / DensityKgPerM3 : 0.0;
}

/// <summary>
/// The client-side physical table (<c>content/physical_units.json</c>, ADR 0052): what one item weighs, how dense it is, the power unit and the module mass
/// unit. The simulation never reads it and the content hash does not cover it. The built-in defaults equal the shipped file (a test checks it).
/// </summary>
public sealed class PhysicalTable
{
    /// <summary>Metres per grid cell. One: the game was drawn at one cell per metre (the body, the doors, the modules).</summary>
    public double CellMetres { get; init; } = 1.0;

    /// <summary>Kilowatts per power unit of the content (<c>power_output</c>, <c>power_use</c>).</summary>
    public double PowerUnitKw { get; init; } = 100.0;

    /// <summary>Tonnes per unit of the relative module masses of <c>ship_handling.json</c>.</summary>
    public double ModuleMassUnitTonnes { get; init; } = 25.0;

    /// <summary>The typical lot in kilograms, used to turn an item count into an approximate mass (a hold capacity, a rock's ore).</summary>
    public double ReferenceLotKg { get; init; } = 25.0;

    /// <summary>Bulk density of a rock in kg/m3 and the radius in metres of a rock of the reference mass (1,000 thousandths), for the rock readout.</summary>
    public double RockDensityKgPerM3 { get; init; } = 2700.0;
    public double BaseRockRadiusMetres { get; init; } = 1.3;

    public IReadOnlyDictionary<string, ItemPhysics> Items { get; init; } = DefaultItems();

    private static Dictionary<string, ItemPhysics> DefaultItems() => new()
    {
        ["core:iron_ore"] = new(30, 2500), ["core:iron_ingot"] = new(20, 7850), ["core:iron_plate"] = new(36, 7850), ["core:carbon"] = new(12, 1000),
        ["core:ammo"] = new(5, 7000), ["core:ice"] = new(25, 920), ["core:titanium_ore"] = new(32, 2800), ["core:titanium_ingot"] = new(18, 4500),
        ["core:nickel_ore"] = new(30, 2500), ["core:nickel_ingot"] = new(20, 8900), ["core:quartz"] = new(26, 1600), ["core:titanium_plate"] = new(32, 4500),
        ["core:nickel_plate"] = new(36, 8900), ["core:hydrogen"] = new(2.5, 71), ["core:silicon"] = new(20, 2330), ["core:circuit"] = new(6, 1500),
        ["core:titanium_alloy"] = new(30, 4500), ["core:fuel_cell"] = new(18, 1200), ["core:science_engineering"] = new(2, 1000),
        ["core:science_biology"] = new(2, 1000), ["core:science_physics"] = new(2, 1000), ["core:biomass"] = new(20, 800), ["core:carbon_fiber"] = new(8, 1600),
        ["core:composite_plate"] = new(24, 1800), ["core:coolant"] = new(30, 1000), ["core:resonant_crystal"] = new(4, 3000), ["core:focus_lens"] = new(5, 3000),
    };

    public static readonly PhysicalTable Default = new();

    /// <summary>The mass of one item of this content id; the reference lot for an id the table does not know (a mod's item).</summary>
    public ItemPhysics Of(string itemId) =>
        Items.TryGetValue(itemId, out var p) ? p : new ItemPhysics(ReferenceLotKg, 1500.0);

    /// <summary>Reads <c>physical_units.json</c>; throws <see cref="InvalidDataException"/> with a short reason when it is not valid.</summary>
    public static PhysicalTable FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            var items = new Dictionary<string, ItemPhysics>(DefaultItems());
            if (root.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Object)
                foreach (var p in its.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"physical_units.json: item '{p.Name}' must be an object");
                    items[p.Name] = new ItemPhysics(Positive(p.Value, "kg", p.Name), Positive(p.Value, "density", p.Name));
                }
            return new PhysicalTable
            {
                CellMetres = Optional(root, "cell_metres", Default.CellMetres),
                PowerUnitKw = Optional(root, "power_unit_kw", Default.PowerUnitKw),
                ModuleMassUnitTonnes = Optional(root, "module_mass_unit_t", Default.ModuleMassUnitTonnes),
                ReferenceLotKg = Optional(root, "reference_lot_kg", Default.ReferenceLotKg),
                RockDensityKgPerM3 = Optional(root, "rock_density_kg_m3", Default.RockDensityKgPerM3),
                BaseRockRadiusMetres = Optional(root, "base_rock_radius_m", Default.BaseRockRadiusMetres),
                Items = items,
            };
        }
        catch (JsonException e) { throw new InvalidDataException("physical_units.json is not valid JSON: " + e.Message); }
    }

    private static double Positive(JsonElement obj, string name, string owner)
    {
        if (!obj.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out double v) || !double.IsFinite(v) || v <= 0)
            throw new InvalidDataException($"physical_units.json: '{owner}' needs a positive number '{name}'");
        return v;
    }

    private static double Optional(JsonElement obj, string name, double fallback) =>
        obj.TryGetProperty(name, out var e) ? Positive(obj, name, "root") : fallback;
}

/// <summary>
/// Conversions from simulation units to physical ones, and the text of the interface (ADR 0052). Display only: nothing here changes the simulation.
/// A cell is a metre, a simulation unit is 1/240 m, a tick is 1/30 s.
/// </summary>
public static class Physical
{
    /// <summary>The table in use; the game sets it after reading <c>physical_units.json</c> (<c>ClientContent</c>).</summary>
    public static PhysicalTable Table { get; set; } = PhysicalTable.Default;

    /// <summary>Metres in a number of grid cells.</summary>
    public static double Metres(double cells) => cells * Table.CellMetres;

    /// <summary>Metres in a number of simulation units (1/240 cell).</summary>
    public static double MetresOfUnits(double units) => units * Table.CellMetres / Units.CellUnits;

    /// <summary>Metres per second of a speed in cells per second.</summary>
    public static double MetresPerSecond(double cellsPerSecond) => cellsPerSecond * Table.CellMetres;

    /// <summary>Metres per second of a speed in simulation units per tick (48 units per tick is 6 m/s).</summary>
    public static double MetresPerSecondOfUnitsPerTick(double unitsPerTick) => unitsPerTick * Units.TicksPerSecond * Table.CellMetres / Units.CellUnits;

    /// <summary>Watts of a power in the content's units (a reactor's 100 is 10 MW).</summary>
    public static double Watts(double powerUnits) => powerUnits * Table.PowerUnitKw * 1000.0;

    /// <summary>Kilograms in a stack of <paramref name="count"/> items of the content id.</summary>
    public static double Kg(string itemId, long count) => Table.Of(itemId).Kg * Math.Max(0, count);

    /// <summary>Cubic metres taken by a stack of <paramref name="count"/> items of the content id.</summary>
    public static double M3(string itemId, long count) => Table.Of(itemId).VolumeM3 * Math.Max(0, count);

    /// <summary>The approximate mass of <paramref name="items"/> items of the reference lot, in kilograms (a hold capacity).</summary>
    public static double NominalKg(long items) => Math.Max(0, items) * Table.ReferenceLotKg;

    /// <summary>The mass of a module kind in kilograms (<c>ship_handling.json</c> relative mass times the module mass unit).</summary>
    public static double ModuleKg(ShipHandling handling, ModuleKind kind) => handling.MassOf(kind) * Table.ModuleMassUnitTonnes * 1000.0;

    /// <summary>The structure mass of a set of modules in kilograms.</summary>
    public static double ModulesKg(ShipHandling handling, IEnumerable<ModuleKind> modules) => handling.MassOf(modules) * Table.ModuleMassUnitTonnes * 1000.0;

    /// <summary>The relative mass of a ship (<see cref="ShipStats.Mass"/>) in kilograms.</summary>
    public static double RelativeMassKg(double relative) => relative * Table.ModuleMassUnitTonnes * 1000.0;

    /// <summary>Radius in metres of a rock of this mass (thousandths of the reference rock): the reference radius times the cube root.</summary>
    public static double RockRadiusMetres(int massThousandths) => Table.BaseRockRadiusMetres * Math.Cbrt(Math.Max(1, massThousandths) / 1000.0);

    /// <summary>Total mass of a rock of this radius in kilograms (a sphere of the rock density).</summary>
    public static double RockKg(double radiusMetres) => 4.0 / 3.0 * Math.PI * radiusMetres * radiusMetres * radiusMetres * Table.RockDensityKgPerM3;

    // ---- text helpers: one place for how a stack, a hold and a range read ------------------------------------------------------------

    /// <summary>A distance in cells (metres) as text: "45 m".</summary>
    public static string Dist(double cells, IFormatProvider? culture = null) => Metric.Length(Metres(cells), culture);

    /// <summary>A speed in cells per second as text: "6.0 m/s".</summary>
    public static string Spd(double cellsPerSecond, IFormatProvider? culture = null) => Metric.Speed(MetresPerSecond(cellsPerSecond), culture);

    /// <summary>The mass of a stack as text: "600 kg".</summary>
    public static string StackMass(string itemId, long count, IFormatProvider? culture = null) => Metric.Mass(Kg(itemId, count), culture);

    /// <summary>A power in content units as text: "10 MW".</summary>
    public static string Pow(double powerUnits, IFormatProvider? culture = null) => Metric.Power(Watts(powerUnits), culture);
}
