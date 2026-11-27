using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Saved designs of the blueprint editor (phase 5): slots, variants, presets, rename, duplicate and delete, kept outside the simulation and the command log.</summary>
public class BlueprintLibraryTests
{
    private const int Cell = Units.CellUnits;
    private static readonly ContentDb C = TestContent.CoreFloor;

    private static (SimWorld W, int Station) Make()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261126UL, threats: false, stationRules: false, structures: true, rules: WorldRules.Default.WithLegacyDockPieces(true));
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Dock);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        foreach (var item in C.Items) w.Network.GiveStock(st, item.Index, 2000);
        return (w, st);
    }

    private static BlueprintSession StationSession(SimWorld w, int st)
    {
        var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        s.Open(st);
        s.OpenFloor((1, 0, 0));
        s.PickPlanMachine(C.MachineIndex("core:smelter"));
        Assert.Null(s.PlanClick((9, 0, 2)));
        s.PickPlanMachine(C.MachineIndex("core:depot"));
        Assert.Null(s.PlanClick((13, 0, 2)));
        s.SetPlanTool(PlanTool.Belt);
        Assert.Null(s.PlanDrag((10, 0, 2), (12, 0, 2)));
        s.CloseFloor();
        return s;
    }

    [Fact]
    public void A_station_design_with_machines_and_belts_round_trips_and_loads_into_a_fresh_draft()
    {
        var (w, st) = Make();
        var s = StationSession(w, st);
        var lib = new BlueprintLibrary(new MemoryBlueprintStore(), C);
        var design = s.ToDesign("Smelting hall", "compact");
        string? file = lib.Save(design);
        Assert.NotNull(file);
        var back = lib.Load(file!)!;
        Assert.Equal("station", back.Structure);
        Assert.Equal("Smelting hall", back.Name);
        Assert.Equal("compact", back.Variant);
        Assert.Equal(design.Modules.OrderBy(m => m.Cell), back.Modules.OrderBy(m => m.Cell));
        Assert.Equal(design.Interior, back.Interior);
        Assert.True(back.Interior.Links.Length >= 2);

        // loading it into a second session on an identical station reproduces the draft (and the same price)
        var (w2, st2) = Make();
        var t = new BlueprintSession(C, ShipHandling.Default, () => w2, 0);
        t.Open(st2);
        Assert.False(t.Dirty);
        Assert.NotNull(t.LoadDesign(back));
        Assert.Equal(s.Interior.ToInterior(), t.Interior.ToInterior());
        Assert.Equal(s.Cost.Lines.Select(x => (x.Item, x.Net)), t.Cost.Lines.Select(x => (x.Item, x.Net)));
        Assert.True(t.CanApply);
        Assert.True(t.Interior.Undo()); // the load was one undoable step
        Assert.Empty(t.Interior.Machines);
    }

    [Fact]
    public void A_design_for_the_other_kind_of_structure_is_refused_and_nothing_changes()
    {
        var (w, st) = Make();
        var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        s.Open(st);
        var shipDesign = new DesignData("ship", "Skiff", "", false, new[] { (((0, 0, 0)), ModuleKind.Hull, 0) }, BlueprintInterior.Empty);
        Assert.Null(s.LoadDesign(shipDesign));
        Assert.False(s.Dirty);
    }

    [Fact]
    public void Slots_variants_presets_rename_duplicate_and_delete_work_and_stay_per_structure_kind()
    {
        var store = new MemoryBlueprintStore();
        var lib = new BlueprintLibrary(store, C);
        DesignData D(string kind, string name, string variant = "", bool preset = false) =>
            new(kind, name, variant, preset, new[] { (((0, 0, 0)), kind == "ship" ? ModuleKind.Cockpit : ModuleKind.Core, 0) }, BlueprintInterior.Empty);

        var a = lib.Save(D("ship", "Miner"))!;
        var b = lib.Save(D("ship", "Hauler"))!;
        var c = lib.Save(D("station", "Hub"))!;
        Assert.Equal(new[] { "Hauler", "Miner" }, lib.List("ship").Select(i => i.Name));
        Assert.Equal(new[] { "Hub" }, lib.List("station").Select(i => i.Name));

        // saving the same name again overwrites; another variant is a second slot
        Assert.Equal(a, lib.Save(D("ship", "Miner")));
        Assert.Equal(2, lib.List("ship").Count);
        var v = lib.Duplicate(a, "long range")!;
        Assert.NotEqual(a, v);
        Assert.Equal(3, lib.List("ship").Count);
        Assert.Null(lib.Duplicate(a, "long range")); // exists
        Assert.Null(lib.Duplicate(a, "  ")); // empty

        // presets sort first
        Assert.True(lib.SetPreset(b, true));
        Assert.Equal("Hauler", lib.List("ship")[0].Name);
        Assert.True(lib.List("ship")[0].Preset);

        // rename keeps the variant, refuses a taken name or an empty one
        var r = lib.Rename(v, "Prospector")!;
        Assert.Equal("Prospector", lib.Load(r)!.Name);
        Assert.Equal("long range", lib.Load(r)!.Variant);
        Assert.Null(lib.Rename(a, "Hauler")); // taken
        Assert.Null(lib.Rename(r, ""));
        Assert.Null(lib.Load(v));

        Assert.True(lib.Delete(r));
        Assert.False(lib.Delete(r));
        Assert.Equal(2, lib.List("ship").Count);
        Assert.NotNull(lib.Load(c));
    }

    [Fact]
    public void Bad_or_foreign_files_never_load_and_the_old_format_still_reads()
    {
        var store = new MemoryBlueprintStore();
        var lib = new BlueprintLibrary(store, C);
        foreach (var bad in new[] { "", "nonsense", BlueprintDesignFormat.Header + "\nname=x\nm=0,0,0,not_a_kind,0\n", BlueprintDesignFormat.Header + "\nname=x\nm=0,0,0,hull,9\n",
                     BlueprintDesignFormat.Header + "\nname=x\nmachine=0,0,0,no_such,-\n", BlueprintDesignFormat.Header + "\nname=x\nlink=0,0,0,0,1,0,0\n", BlueprintDesignFormat.Header + "\nm=0,0,0,hull,0\n" })
            Assert.Null(BlueprintDesignFormat.Parse(bad, C));
        store.Write("ship_bad.bp", "garbage");
        Assert.Empty(lib.List("ship"));

        // a version 1 blueprint (the phase 2 slot file) reads as a ship design with no interior
        var d = new BlueprintDraft();
        d.Load(new[] { new KeyValuePair<(int, int, int), ModuleKind>((0, 0, 0), ModuleKind.Hull) });
        var old = BlueprintDesignFormat.Parse(BlueprintFormat.Serialize(d, "Old slot"), C)!;
        Assert.Equal("ship", old.Structure);
        Assert.Equal("Old slot", old.Name);
        Assert.Single(old.Modules);
        Assert.Empty(old.Interior.Machines);
    }

    [Fact]
    public void The_library_is_bounded_and_names_are_cleaned()
    {
        var lib = new BlueprintLibrary(new MemoryBlueprintStore(), C);
        DesignData D(string name) => new("ship", name, "", false, new[] { (((0, 0, 0)), ModuleKind.Hull, 0) }, BlueprintInterior.Empty);
        for (int i = 0; i < BlueprintLibrary.MaxDesigns; i++) Assert.NotNull(lib.Save(D("d" + i)));
        Assert.Null(lib.Save(D("one too many")));
        Assert.NotNull(lib.Save(D("d3"))); // an overwrite is always fine
        Assert.Null(lib.Save(D("   ")));
        Assert.Equal("a b", BlueprintDesignFormat.Clean("a\nb".Replace('\n', ' ')));
        Assert.True(BlueprintDesignFormat.Clean(new string('x', 200)).Length <= BlueprintDesignFormat.MaxName);
        var file = BlueprintLibrary.FileFor("ship", "../My/..\\Skiff", "v/1");
        Assert.DoesNotContain('/', file); Assert.DoesNotContain('\\', file); Assert.DoesNotContain("..", file); Assert.EndsWith(".bp", file);
    }
}
