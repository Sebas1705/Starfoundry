using Starfoundry.Sim;

namespace Starfoundry.Balance;

/// <summary>
/// The science tier (ADR 0026), played only with <see cref="Science"/> (<c>--science</c>): the electronics branch the default plan skips,
/// then one wing per field on the station (a workshop module with the chain and its own laboratory), then the fifteen nodes of the
/// field. The default path never calls any of this.
/// </summary>
public sealed partial class StructureBot
{
    /// <summary>Build the science chains and research the extended tree after the base plan (when left alone).</summary>
    public bool Science { get; init; }

    private static readonly Feed NiPlates = new(FeedKind.Plates, "core:nickel_ore_unloader", "core:smelt_nickel", "core:nickel_plate");
    private static readonly Feed SiliconIngots = new(FeedKind.Ingots, "core:quartz_unloader", "core:smelt_silicon");

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
        foreach (var (x, y, z) in new[] { (1, -1, 0), (2, -1, 0) })
            if (Need(new[] { new ItemStack(Item("core:carbon"), 8) }, "carbon for a wing reactor")) PlaceModule(_station, x, y, z, ModuleKind.Reactor);

        // Engineering: circuits (silicon and nickel plates) and iron plates into the bench.
        BuildWing(2, EngineeringWing, "engineering");
        foreach (var (id, science) in new[] { ("core:structural_plating", 10), ("core:alloy_hulls", 15), ("core:repair_drones", 20), ("core:automated_turrets", 25), ("core:cargo_expansion", 30) })
        {
            EnsureMetals(science);
            ResearchNow(id);
            Reinforce();
        }
        // Physics: silicon and nickel plates into the physics lab.
        BuildWing(3, PhysicsWing, "physics");
        foreach (var (id, science) in new[] { ("core:field_theory", 10), ("core:efficient_reactors", 15), ("core:long_range_jump", 20), ("core:targeting_lasers", 25), ("core:quantum_sensors", 30) })
        {
            EnsureMetals(science);
            ResearchNow(id);
            Reinforce();
        }
        // Biology: ice and carbon into biomass, plates and biomass into science.
        BuildWing(4, BiologyWing, "biology");
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

    private void BuildWing(int mx, Action<int, int, int, int> layout, string name)
    {
        Goal($"science: the {name} wing (a workshop module with its chain and laboratory)");
        if (PlaceModule(_station, mx, 0, 0, ModuleKind.Workshop) < 0) return;
        var (ox, oy, oz) = Origin(mx, 0, 0);
        layout(_station, ox, oy, oz);
    }

    private void Connect(int belt, int machine)
    {
        if (belt >= 0 && machine >= 0) Failed(Cmd(new ConnectToMachine(belt, machine)), "ConnectToMachine");
    }

    /// <summary>Two iron smelter rows (z and z+1) into a plate assembler at (7,1,z), as in a plate line. Returns the assembler.</summary>
    private int PlateAssemblerAt(int st, int ox, int oy, int oz, int z)
    {
        int asm = PlaceMachine(st, ox + 7, oy + 1, oz + z, "core:assembler", "core:iron_plate");
        Connect(SmelterRow(st, ox, oy, oz, 1, z, 6).Belt, asm);
        Connect(SmelterRow(st, ox, oy, oz, 1, z + 1, 7).Belt, asm);
        return asm;
    }

    /// <summary>Nickel ore to plates and quartz to silicon, into the machine at (7,1,0): the circuit assembler or the physics lab.</summary>
    private int MetalInputs(int st, int ox, int oy, int oz, string consumer, string recipe)
    {
        int target = PlaceMachine(st, ox + 7, oy + 1, oz, consumer, recipe);
        int plate = PlaceMachine(st, ox + 5, oy + 1, oz, "core:assembler", "core:nickel_plate");
        Connect(SmelterRow(st, ox, oy, oz, 1, 0, 4, "core:smelt_nickel", "core:nickel_ore_unloader").Belt, plate);
        Link(st, plate, ox + 6, oy + 1, oz, Dir.PosX, 1, target);
        Connect(SmelterRow(st, ox, oy, oz, 2, 0, 7, "core:smelt_silicon", "core:quartz_unloader").Belt, target);
        return target;
    }

    private void EngineeringWing(int st, int ox, int oy, int oz)
    {
        int circuit = MetalInputs(st, ox, oy, oz, "core:assembler", "core:make_circuit");
        int bench = PlaceMachine(st, ox + 7, oy + 1, oz + 2, "core:engineering_bench", "core:make_engineering_science");
        Link(st, circuit, ox + 7, oy + 1, oz + 1, Dir.PosZ, 1, bench);
        int iron = PlateAssemblerAt(st, ox, oy, oz, 4);
        Link(st, iron, ox + 7, oy + 1, oz + 3, Dir.NegZ, 1, bench);
        int lab = PlaceMachine(st, ox + 7, oy + 3, oz + 2, "core:research_lab");
        Link(st, bench, ox + 7, oy + 2, oz + 2, Dir.PosY, 1, lab);
    }

    private void PhysicsWing(int st, int ox, int oy, int oz)
    {
        int phys = MetalInputs(st, ox, oy, oz, "core:physics_lab", "core:make_physics_science");
        int lab = PlaceMachine(st, ox + 7, oy + 1, oz + 2, "core:research_lab");
        Link(st, phys, ox + 7, oy + 1, oz + 1, Dir.PosZ, 1, lab);
    }

    private void BiologyWing(int st, int ox, int oy, int oz)
    {
        int g1 = PlaceMachine(st, ox + 3, oy + 1, oz, "core:greenhouse", "core:grow_biomass");
        int ice = PlaceMachine(st, ox, oy + 1, oz, "core:ice_unloader");
        Link(st, ice, ox + 1, oy + 1, oz, Dir.PosX, 2, g1);
        int carbon = PlaceMachine(st, ox, oy + 2, oz, "core:carbon_unloader");
        Link(st, carbon, ox + 1, oy + 2, oz, Dir.PosX, 3, g1);
        int g2 = PlaceMachine(st, ox + 7, oy + 1, oz, "core:greenhouse", "core:make_biology_science");
        Link(st, g1, ox + 4, oy + 1, oz, Dir.PosX, 3, g2);
        int iron = PlateAssemblerAt(st, ox, oy, oz, 2);
        Link(st, iron, ox + 7, oy + 1, oz + 1, Dir.NegZ, 1, g2);
        int lab = PlaceMachine(st, ox + 7, oy + 3, oz, "core:research_lab");
        Link(st, g2, ox + 7, oy + 2, oz, Dir.PosY, 1, lab);
    }
}
