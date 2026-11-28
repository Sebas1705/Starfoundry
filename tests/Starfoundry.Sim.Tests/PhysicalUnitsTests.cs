using System.Globalization;
using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The physical table (<c>content/physical_units.json</c>, ADR 0052): complete, consistent with the recipes and the reactor, and the interface speaks metres.</summary>
public class PhysicalUnitsTests
{
    private static string Json() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "physical_units.json"));

    private static PhysicalTable Shipped() => PhysicalTable.FromJson(Json());

    private static ContentDb Core => TestContent.CoreFloor;

    [Fact]
    public void TheShippedFileEqualsTheBuiltInDefaults()
    {
        var file = Shipped();
        var def = PhysicalTable.Default;
        Assert.Equal(def.CellMetres, file.CellMetres);
        Assert.Equal(def.PowerUnitKw, file.PowerUnitKw);
        Assert.Equal(def.ModuleMassUnitTonnes, file.ModuleMassUnitTonnes);
        Assert.Equal(def.ReferenceLotKg, file.ReferenceLotKg);
        Assert.Equal(def.RockDensityKgPerM3, file.RockDensityKgPerM3);
        Assert.Equal(def.BaseRockRadiusMetres, file.BaseRockRadiusMetres);
        Assert.Equal(def.Items.OrderBy(p => p.Key).ToList(), file.Items.OrderBy(p => p.Key).ToList());
    }

    [Fact]
    public void EveryItemOfTheContentHasAMassAndADensity()
    {
        var table = Shipped();
        foreach (var item in Core.Items)
        {
            Assert.True(table.Items.ContainsKey(item.Id), $"physical_units.json has no entry for {item.Id}");
            var p = table.Items[item.Id];
            Assert.InRange(p.Kg, 0.5, 100);
            Assert.InRange(p.DensityKgPerM3, 50, 10_000);
        }
        foreach (var id in table.Items.Keys) Assert.Contains(Core.Items, i => i.Id == id); // no stray entry for an item that does not exist
    }

    [Fact]
    public void NoRecipeCreatesMass()
    {
        var table = Shipped();
        foreach (var r in Core.Recipes)
        {
            if (r.Inputs.Length == 0) continue; // extraction from a rock
            double input = r.Inputs.Sum(s => table.Of(Core.Items[s.Item].Id).Kg * s.Count);
            double output = r.Outputs.Sum(s => table.Of(Core.Items[s.Item].Id).Kg * s.Count);
            Assert.True(output <= input + 1e-9, $"{r.Id} makes {output} kg out of {input} kg");
        }
    }

    [Fact]
    public void TheReferenceLotIsTheTypicalSolidItem()
    {
        var table = Shipped();
        var solids = new[] { "core:iron_ore", "core:iron_ingot", "core:iron_plate", "core:ice", "core:quartz", "core:titanium_ore", "core:nickel_ore" };
        double mean = solids.Average(id => table.Of(id).Kg);
        Assert.InRange(mean, table.ReferenceLotKg * 0.8, table.ReferenceLotKg * 1.3);
    }

    [Fact]
    public void HydrogenEnergyIsWhatRealHydrogenGives()
    {
        var table = Shipped();
        int output = Core.Structures!.PowerOutput(ModuleKind.Reactor);
        Assert.Equal(100, output); // 10 MW at 100 kW a unit
        int burnTicks = 900;
        double joules = Physical.Watts(output) * burnTicks / Units.TicksPerSecond;
        double perKg = joules / table.Of("core:hydrogen").Kg;
        Assert.InRange(perKg, 100e6, 140e6); // hydrogen gives about 120 MJ/kg
    }

    [Fact]
    public void EveryModuleKindHasAMassInTonnes()
    {
        var handling = ShipHandling.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "ship_handling.json")));
        foreach (ModuleKind kind in Enum.GetValues<ModuleKind>())
        {
            if (!Core.Structures!.IsDefined(kind)) continue;
            double t = Physical.ModuleKg(handling, kind) / 1000.0;
            Assert.InRange(t, 5, 100);
        }
        var starter = new[] { ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine, ModuleKind.MiningLaser, ModuleKind.Gun };
        Assert.InRange(Physical.ModulesKg(handling, starter) / 1000.0, 172.0, 173.0); // the 7-module reference ship is about 170 t
    }

    [Fact]
    public void AnUnknownItemFallsBackToTheReferenceLotAndABadFileIsRefused()
    {
        var table = Shipped();
        Assert.Equal(table.ReferenceLotKg, table.Of("mod:mystery").Kg);
        Assert.Throws<InvalidDataException>(() => PhysicalTable.FromJson("{ not json"));
        Assert.Throws<InvalidDataException>(() => PhysicalTable.FromJson("{ \"items\": { \"core:iron_ore\": { \"kg\": -1, \"density\": 100 } } }"));
        Assert.Throws<InvalidDataException>(() => PhysicalTable.FromJson("{ \"power_unit_kw\": 0 }"));
        var custom = PhysicalTable.FromJson("{ \"power_unit_kw\": 10, \"items\": { \"core:iron_ore\": { \"kg\": 50, \"density\": 2500 } } }");
        Assert.Equal(10, custom.PowerUnitKw);
        Assert.Equal(50, custom.Of("core:iron_ore").Kg);
        Assert.Equal(20, custom.Of("core:iron_ingot").Kg); // the rest keeps the defaults
    }

    [Fact]
    public void StacksReportTheirMassAndVolume()
    {
        Assert.Equal(600, Physical.Kg("core:iron_ore", 20));
        Assert.Equal("600 kg", Physical.StackMass("core:iron_ore", 20, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(0.012, Physical.Table.Of("core:iron_ore").VolumeM3, 6);
        Assert.Equal(1500, Physical.NominalKg(60)); // the starter hold
    }

    // ---- the wording ------------------------------------------------------------------------------------------

    [Fact]
    public void TheInterfaceNoLongerSaysCellsForADistanceOrASpeed()
    {
        var allowed = new[] { "fuel cell", "fuel_cell", "celda de combustible", "celdas de combustible", "celdas.", "cell", "core:fuel_cell" };
        foreach (var (key, (es, en)) in Loc.All)
        {
            if (key.Contains("fuel_cell", StringComparison.Ordinal) || key.Contains("fuelcell", StringComparison.OrdinalIgnoreCase)) continue;
            string e = en.Replace("fuel cells", "").Replace("fuel cell", "").Replace("Fuel cells", "").Replace("Fuel cell", "");
            string s = es.Replace("celdas de combustible", "").Replace("celda de combustible", "").Replace("las celdas", "").Replace("celdas o", "").Replace("o celdas", "");
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(e, @"\bcells?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), $"{key} still says cells: {en}");
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(s, @"\bceldas?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), $"{key} still says celdas: {es}");
            Assert.DoesNotContain("uds. de mapa", es);
            Assert.DoesNotContain("map units", en);
        }
        Assert.NotEmpty(allowed);
    }

    [Fact]
    public void EveryChangedTextKeepsItsPlaceholdersInBothLanguages()
    {
        foreach (var key in new[] { "hud.hold", "hud.power.fuel", "hud.flightGaugeTip", "key.dockGuide", "hazard.near", "inv.tip.mass", "inv.tip.energy", "tip.stack", "build.mass" })
        {
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            var pes = System.Text.RegularExpressions.Regex.Matches(es, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
            var pen = System.Text.RegularExpressions.Regex.Matches(en, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(pes, pen);
        }
    }

    [Fact]
    public void TheTextsReadInMetresInBothLanguages()
    {
        var old = Loc.Lang;
        try
        {
            Loc.Lang = Language.En;
            Assert.Equal("Rock, 11 m away", Loc.T("hazard.near", "Rock", Physical.Dist(11)));
            Assert.Equal("Collision at 4.5 m/s: -12 to hull", Loc.T("toast.collision", 12, "hull", Metric.Speed(4.5, Loc.Culture)));
            Assert.Equal("Port 12 m away, speed 60% of the limit", Loc.T("key.dockGuide", Physical.Dist(12), 60));
            Loc.Lang = Language.Es;
            Assert.Equal("Roca a 1,2 km", Loc.T("hazard.near", "Roca", Physical.Dist(1234)));
            Assert.Equal("Puerto a 12 m, velocidad 60% del límite", Loc.T("key.dockGuide", Physical.Dist(12), 60));
            Assert.Equal("Gasta 500 kW", Loc.T("build.powerUses", Physical.Pow(5)));
        }
        finally { Loc.Lang = old; }
    }

    [Fact]
    public void TheStatsRowsCarryUnitsAndTheSizeOfTheShip()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        var handling = ShipHandling.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "ship_handling.json")));
        var cells = new Dictionary<(int Mx, int My, int Mz), ModuleKind>
        {
            [(0, 0, 0)] = ModuleKind.Cockpit, [(1, 0, 0)] = ModuleKind.Hull, [(2, 0, 0)] = ModuleKind.Cargo, [(3, 0, 0)] = ModuleKind.Reactor, [(4, 0, 0)] = ModuleKind.Engine,
        };
        var stats = ShipStats.Compute(cells, Core, handling, default);
        Assert.Equal(40, stats.SizeXMetres); // five modules of 8 m
        Assert.Equal(4, stats.SizeYMetres);
        Assert.Equal(8, stats.SizeZMetres);
        Assert.Equal(40, stats.LengthMetres);
        var rows = StatRows.Build(stats, stats).ToDictionary(r => r.LabelKey);
        Assert.Equal("40 m", rows["bp.stat.length"].Text(en));
        Assert.EndsWith(" t", rows["bp.stat.mass"].Text(en));
        Assert.Equal("6.0 m/s", rows["bp.stat.thrust"].Text(en));
        Assert.Equal("5.0 m/s²", rows["bp.stat.accel"].Text(en));
        Assert.Equal("+100", new StatRow("x", "x", 500, 400, "0", true, Quantity.HoldItems).DeltaText(en));
        Assert.Contains("MW", rows["bp.stat.power"].Text(en));
        Assert.Contains("≈", rows["bp.stat.cargo"].Text(en));
    }

    [Fact]
    public void AMachineMakesAKnownMassPerSecond()
    {
        // a smelter makes one iron ingot (20 kg) per second
        Assert.Equal(20.0, MachineView.OutputKgPerSecond(new[] { ("core:iron_ingot", 1) }, Units.TicksPerSecond), 9);
        Assert.Equal("72 t/h", Metric.MassFlow(20, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(0.0, MachineView.OutputKgPerSecond(new[] { ("core:iron_ingot", 1) }, 0));
    }

    [Fact]
    public void TheInventoryTooltipNamesMassVolumeAndEnergy()
    {
        var old = Loc.Lang;
        try
        {
            Loc.Lang = Language.En;
            int ore = Core.Items.First(i => i.Id == "core:iron_ore").Index;
            string tip = InventoryView.Tooltip(Core, ore, 20, 100, 900);
            Assert.Contains("Mass: 30 kg per item, takes 12 L", tip);
            Assert.Contains("This stack: 600 kg", tip);
            int h2 = Core.Items.First(i => i.Id == "core:hydrogen").Index;
            string fuel = InventoryView.Tooltip(Core, h2, 0, 100, 900);
            Assert.Contains("Energy: 300 MJ (83 kWh)", fuel);
        }
        finally { Loc.Lang = old; }
    }
}
