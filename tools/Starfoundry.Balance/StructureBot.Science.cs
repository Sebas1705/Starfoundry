using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>
/// The science tier (ADR 0026), played only with <see cref="Science"/> (<c>--science</c>): the electronics branch the default plan skips, then one
/// wing per field on the station and the fifteen nodes of the field. A wing is one or two fabrication bays side by side (the belts cross the seam
/// between them, ADR 0031) with its chain on the floor and a laboratory of its own. The default path never calls any of this.
/// </summary>
public sealed partial class StructureBot
{
    /// <summary>Build the science chains and research the extended tree after the base plan (when left alone).</summary>
    public bool Science { get; init; }

    private void PlayScience()
    {
        if (_station < 0 || !Net.StructureExists(_station)) { Stuck("no station for the science wings"); return; }
        Goal("science: the electronics branch the tier is built on");
        // Quartz and the metals come from other systems; each trip stays within what the hold can carry.
        if (!Expedition("quartz, nickel and titanium for silicon and electronics", ("core:quartz", 130), ("core:nickel_ore", 50), ("core:titanium_ore", 40)))
            Stuck("the first science expedition did not bring everything");
        ResearchNow("core:silicon_processing", QuartzRaw, NiPlates);
        ResearchNow("core:electronics", SiliconIngots, TiPlates);
        if (!Expedition("quartz, nickel and titanium for shielding, sensors and Mk2 power", ("core:quartz", 210), ("core:nickel_ore", 110), ("core:titanium_ore", 45)))
            Stuck("the second science expedition did not bring everything");
        ResearchNow("core:shielding", SiliconIngots, TiPlates);
        ResearchNow("core:sensors", SiliconIngots, NiPlates);
        ResearchNow("core:advanced_power", SiliconIngots, NiPlates);

        Goal("science: power for the wings");
        FetchCarbon(18, "carbon for two more station reactors");
        foreach (var (x, y, z) in new[] { (0, -1, 1), (1, -1, 1) })
            if (Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for a wing reactor")) PlaceModule(_station, x, y, z, ModuleKind.Reactor);

        // Engineering: circuits (silicon and nickel plates) and iron plates into the bench.
        BuildWing(1, 2, EngineeringWing, "engineering");
        foreach (var (id, science) in new[] { ("core:structural_plating", 10), ("core:alloy_hulls", 15), ("core:repair_drones", 20), ("core:automated_turrets", 25), ("core:cargo_expansion", 30), ("core:plasma_drive", 35) })
        {
            EnsureMetals(science);
            ResearchNow(id);
            Reinforce();
        }
        // Physics: silicon and nickel plates into the physics lab.
        BuildWing(2, 2, PhysicsWing, "physics");
        foreach (var (id, science) in new[] { ("core:field_theory", 10), ("core:efficient_reactors", 15), ("core:long_range_jump", 20), ("core:targeting_lasers", 25), ("core:quantum_sensors", 30) })
        {
            EnsureMetals(science);
            ResearchNow(id);
            Reinforce();
        }
        // Biology: ice and carbon into biomass, plates and biomass into science.
        BuildWing(3, 1, BiologyWing, "biology");
        foreach (var (id, science) in new[] { ("core:algae_fuel", 10), ("core:closed_loop", 15), ("core:medical_bay", 20), ("core:bio_sensors", 25), ("core:symbiotic_drones", 30) })
        {
            EnsureBiomassInputs(science);
            ResearchNow(id);
            Reinforce();
        }
        if (_c.Research.Where(n => n.Field != ResearchField.Basic).All(n => Net.IsResearched(n.Index))) Milestone("Science tier complete");
    }

    /// <summary>Quartz and nickel ore for <paramref name="science"/> items of circuits or physics science (two ore of each per item), fetched in one trip.</summary>
    private void EnsureMetals(int science)
    {
        int need = science * 2 + 10;
        if (Have("core:quartz") >= need && Have("core:nickel_ore") >= need) return;
        if (!Expedition("quartz and nickel ore for science", ("core:quartz", Math.Min(need + 40, 160)), ("core:nickel_ore", Math.Min(need + 40, 160))))
            Stuck("the science expedition did not bring enough quartz and nickel ore");
    }

    /// <summary>Ice (two per biomass) and carbon (one) for the greenhouse; the home drones keep the ice coming, the rest is hand mined.</summary>
    private void EnsureBiomassInputs(int science)
    {
        if (Have("core:carbon") < science + 20) FetchCarbon(science + 30, "carbon for biomass");
        if (Have("core:ice") < science * 2 + 10 && FindDeposit(Item("core:ice"), 0) >= 0) Gather(Item("core:ice"), science * 2 + 30, "ice for biomass", 0);
    }

    /// <summary>A wing of <paramref name="bays"/> fabrication bays side by side at module row z = <paramref name="mz"/>, starting in the column of the station core.</summary>
    private void BuildWing(int mz, int bays, Action<int, int, int, int> layout, string name)
    {
        Goal($"science: the {name} wing ({bays} fabrication bay(s) with its chain and laboratory)");
        for (int i = 0; i < bays; i++)
            if (PlaceModule(_station, i, 0, mz, ModuleKind.Workshop) < 0) return;
        var (ox, oy, oz) = Origin(0, 0, mz);
        layout(_station, ox, oy, oz);
    }

    private void Connect(int belt, int machine)
    {
        if (belt >= 0 && machine >= 0) Failed(Cmd(new ConnectToMachine(belt, machine)), "ConnectToMachine");
    }

    /// <summary>Nickel ore to plates (row 0) and quartz to silicon (row 1), both into the machine at (7, 0): the circuit assembler or the physics lab.</summary>
    private int MetalInputs(int st, int ox, int oy, int oz, string consumer, string recipe)
    {
        int target = PlaceMachine(st, ox + 7, oy, oz, consumer, recipe);
        int plate = PlaceMachine(st, ox + 5, oy, oz, "core:assembler", "core:nickel_plate");
        Connect(SmelterRow(st, ox, oy, oz, 0, 0, 4, "core:smelt_nickel", "core:nickel_ore_unloader").Belt, plate);
        Link(st, plate, ox + 6, oy, oz, Dir.PosX, 1, target);
        Connect(SmelterRow(st, ox, oy, oz, 0, 1, 7, "core:smelt_silicon", "core:quartz_unloader").Belt, target);
        return target;
    }

    /// <summary>Two bays: circuits in the first, the bench, the iron plate assembler (two smelter rows) and the laboratory in the second.</summary>
    private void EngineeringWing(int st, int ox, int oy, int oz)
    {
        int circuit = MetalInputs(st, ox, oy, oz, "core:assembler", "core:make_circuit");
        int bench = PlaceMachine(st, ox + 9, oy, oz, "core:engineering_bench", "core:make_engineering_science");
        Link(st, circuit, ox + 8, oy, oz, Dir.PosX, 1, bench);
        int iron = PlaceMachine(st, ox + 9, oy, oz + 2, "core:assembler", "core:iron_plate");
        Connect(SmelterRow(st, ox, oy, oz, 0, 2, 8).Belt, iron);
        Connect(SmelterRow(st, ox, oy, oz, 0, 3, 9).Belt, iron);
        Link(st, iron, ox + 9, oy, oz + 1, Dir.NegZ, 1, bench);
        int lab = PlaceMachine(st, ox + 11, oy, oz, "core:research_lab");
        Link(st, bench, ox + 10, oy, oz, Dir.PosX, 1, lab);
    }

    /// <summary>Two bays: nickel plates and silicon into the physics lab, whose science crosses the seam into a laboratory.</summary>
    private void PhysicsWing(int st, int ox, int oy, int oz)
    {
        int phys = MetalInputs(st, ox, oy, oz, "core:physics_lab", "core:make_physics_science");
        int lab = PlaceMachine(st, ox + 9, oy, oz, "core:research_lab");
        Link(st, phys, ox + 8, oy, oz, Dir.PosX, 1, lab);
    }

    /// <summary>One bay: ice and carbon into the first greenhouse (biomass), its biomass and the iron plate line into the second (science), then a laboratory.</summary>
    private void BiologyWing(int st, int ox, int oy, int oz)
    {
        int g1 = PlaceMachine(st, ox + 3, oy, oz + 2, "core:greenhouse", "core:grow_biomass");
        int ice = PlaceMachine(st, ox, oy, oz + 2, "core:ice_unloader");
        Link(st, ice, ox + 1, oy, oz + 2, Dir.PosX, 2, g1);
        int carbon = PlaceMachine(st, ox + 3, oy, oz, "core:carbon_unloader");
        Link(st, carbon, ox + 3, oy, oz + 1, Dir.PosZ, 1, g1);
        // The plate line's sink is the second greenhouse at (7, 2): iron plates from below, biomass from the first one along row 2.
        PlateLine(st, ox, oy, oz, 4, "core:greenhouse", out int g2, "core:make_biology_science");
        Link(st, g1, ox + 4, oy, oz + 2, Dir.PosX, 3, g2);
        int lab = PlaceMachine(st, ox + 7, oy, oz, "core:research_lab");
        Link(st, g2, ox + 7, oy, oz + 1, Dir.NegZ, 1, lab);
    }
}
