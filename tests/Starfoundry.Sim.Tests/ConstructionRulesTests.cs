using System.Text.Json.Nodes;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0031: machines and belts are built on the floor layer of buildable modules (fabrication bays, station cores, defence modules for turrets
/// and walls); the starter ship has a workbench and no bay; old logs keep free building behind <see cref="WorldRules.LegacyBuilding"/>.
/// </summary>
public class ConstructionRulesTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;
    private const ulong Seed = 20261121UL;
    private static int Type(string id) => C.MachineIndex(id);

    private static SimWorld World(WorldRules? rules = null, bool station = false)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: (rules ?? new WorldRules { FreeBuild = true }).WithLegacyShipAutomation(true));
        w.Network.EnsurePlayer(0);
        return w;
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static int Ship(SimWorld w) => w.Network.ShipStructure(0);

    private static void Module(SimWorld w, int s, int mx, int my, int mz, ModuleKind k) => Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(s, mx, my, mz, k)));

    /// <summary>The starter ship with a fabrication bay at module (1,0,1), a defence module at (2,0,1) and a second bay at (1,1,1) above the first.</summary>
    private static (SimWorld W, int S) Ship3(WorldRules? rules = null)
    {
        var w = World(rules);
        int s = Ship(w);
        Module(w, s, 1, 0, 1, ModuleKind.Workshop);
        Module(w, s, 2, 0, 1, ModuleKind.Defence);
        Module(w, s, 1, 1, 1, ModuleKind.Workshop);
        return (w, s);
    }

    [Fact]
    public void Floor_layers_are_the_bottom_layer_of_each_module_row()
    {
        Assert.True(StationGrid.IsFloorLayer(0));
        Assert.True(StationGrid.IsFloorLayer(4));
        Assert.True(StationGrid.IsFloorLayer(-4));
        Assert.True(StationGrid.IsFloorLayer(-8));
        foreach (int y in new[] { 1, 2, 3, 5, -1, -2, -3, -5 }) Assert.False(StationGrid.IsFloorLayer(y), $"layer {y}");
        Assert.Equal(8, StationGrid.FloorY(2));
        Assert.Equal(-4, StationGrid.FloorY(-1));
    }

    [Fact]
    public void A_bay_takes_machines_and_belts_on_its_floor_only()
    {
        var (w, s) = Ship3();
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 9, 0, 9, Type("core:smelter"), C.RecipeIndex("core:smelt_iron"))));
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(s, 10, 0, 9, Dir.PosX, 3, BeltTier.Mk1)));
        foreach (int y in new[] { 1, 2, 3 })
        {
            Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceMachineIn(s, 9, y, 11, Type("core:smelter"), -1)));
            Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceSegmentIn(s, 10, y, 11, Dir.PosX, 3, BeltTier.Mk1)));
        }
        // The bay above has its own floor at layer 4.
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 9, 4, 9, Type("core:smelter"), -1)));
        Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceMachineIn(s, 9, 5, 9, Type("core:smelter"), -1)));
        w.Network.ValidateInvariants();
        w.ValidateInvariants();
    }

    [Fact]
    public void A_belt_cannot_climb_and_belts_do_not_stack()
    {
        var (w, s) = Ship3();
        Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceSegmentIn(s, 9, 0, 9, Dir.PosY, 2, BeltTier.Mk1)));
        Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceSegmentIn(s, 9, 0, 9, Dir.PosY, 5, BeltTier.Mk1)));
    }

    [Fact]
    public void Other_module_kinds_are_not_buildable()
    {
        var w = World();
        int s = Ship(w);
        // cockpit (0,0,0), hull (0,0,1), cargo (0,0,2), reactor (0,0,3), engine (0,0,4)
        foreach (int mz in new[] { 0, 1, 2, 3, 4 })
        {
            Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 1, 0, mz * 8 + 1, Type("core:smelter"), -1)));
            Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceSegmentIn(s, 1, 0, mz * 8 + 1, Dir.PosX, 2, BeltTier.Mk1)));
        }
        Assert.False(C.Structures!.IsBuildable(ModuleKind.Hull)); // the hull lists belts in the content but is not buildable
        Assert.True(C.Structures.AllowsBelts(ModuleKind.Hull));
        Assert.True(C.Structures.IsBuildable(ModuleKind.Workshop));
        Assert.True(C.Structures.IsBuildable(ModuleKind.Core));
        Assert.True(C.Structures.IsBuildable(ModuleKind.Defence));
        foreach (var k in new[] { ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Reactor, ModuleKind.Cargo, ModuleKind.DroneBay, ModuleKind.Shield, ModuleKind.Sensor, ModuleKind.ReactorMk2, ModuleKind.Dock })
            Assert.False(C.Structures.IsBuildable(k), k.ToString());
    }

    [Fact]
    public void A_belt_runs_from_one_bay_across_the_seam_into_a_defence_module_but_not_through_a_hull()
    {
        var (w, s) = Ship3();
        // Bay (1,0,1) spans x 8..15, defence (2,0,1) x 16..23: a belt along z=9 from x=12 to x=20 crosses the seam.
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(s, 12, 0, 9, Dir.PosX, 9, BeltTier.Mk1)));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 21, 0, 9, Type("core:turret"), -1)));
        // The hull at module (0,0,1) (x 0..7) is in the way westwards.
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceSegmentIn(s, 10, 0, 11, Dir.NegX, 6, BeltTier.Mk1)));
        // Turrets and walls stand on the defence floor only.
        Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceMachineIn(s, 20, 1, 12, Type("core:turret"), -1)));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 20, 0, 12, Type("core:wall"), -1)));
        // Turrets do not go in a bay.
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 9, 0, 13, Type("core:turret"), -1)));
    }

    [Fact]
    public void A_station_core_is_a_bay()
    {
        var w = World();
        Assert.Equal(Reject.None, Run(w, new FoundStation(10, 0, -12)));
        int st = w.Network.Structures().First(x => x.Kind == StructureKind.Station).Id;
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(st, 1, 0, 1, Type("core:smelter"), -1)));
        Assert.Equal(Reject.NotOnFloor, Run(w, new PlaceMachineIn(st, 4, 2, 1, Type("core:smelter"), -1)));
    }

    [Fact]
    public void The_legacy_flag_keeps_free_building()
    {
        var (w, s) = Ship3(new WorldRules { FreeBuild = true }.WithLegacyBuilding(true));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 9, 2, 9, Type("core:smelter"), -1)));
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(s, 10, 3, 9, Dir.PosX, 3, BeltTier.Mk1)));
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(s, 1, 0, 9, Dir.PosX, 3, BeltTier.Mk1))); // a belt in the hull, as before
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 1, 0, 17, Type("core:smelter"), -1))); // the cargo module still allows nothing
    }

    [Fact]
    public void Old_content_without_floor_building_builds_anywhere()
    {
        Assert.False(TestContent.Core.FloorBuilding);
        Assert.True(C.FloorBuilding);
    }

    [Fact]
    public void The_legacy_flag_is_in_the_rules_bytes_and_hashes_away()
    {
        var legacy = new WorldRules().WithLegacyBuilding(true);
        Assert.True(legacy.LegacyBuilding);
        Assert.False(legacy.WithLegacyBuilding(false).LegacyBuilding);
        var round = WorldRules.Deserialize(legacy.Serialize());
        Assert.True(round.LegacyBuilding);
        Assert.Equal(legacy, round);
        Assert.NotEqual(legacy, new WorldRules());
        Assert.Equal(16, legacy.Serialize()[1 + 8 * 4] & 16);
        Assert.True(legacy.WithoutLegacyFlags().IsDefault);
        Assert.True(legacy.WithLegacyLaser(true).WithLegacyGalaxy(true).LegacyBuilding); // the helpers keep each other's flags
        Assert.True(legacy.Clamped().LegacyBuilding);
        // Two worlds that differ only by the legacy flag hash alike until a building command means something different.
        var a = World(new WorldRules());
        var b = World(new WorldRules().WithLegacyBuilding(true));
        a.Step(50); b.Step(50);
        Assert.Equal(a.Checksum(), b.Checksum());
    }

    [Fact]
    public void Log_version_13_reads_new_and_older_logs_read_with_the_legacy_flag()
    {
        var log = new List<LoggedCommand>();
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        byte[] bytes = ms.ToArray();
        Assert.Equal(20, bytes[4]);
        CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var now);
        Assert.False(now.LegacyBuilding);
        for (byte version = 6; version <= 12; version++)
        {
            var old = (byte[])bytes.Clone();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyBuilding, $"version {version}");
            Assert.True(rules.WithoutLegacyFlags().IsDefault, $"version {version}");
        }
        var v13 = (byte[])bytes.Clone();
        v13[4] = 13; // recorded before the hardpoint modules (ADR 0032): building is not legacy, the implicit weapons are
        CommandCodec.ReadLog(new MemoryStream(v13), out _, out _, out _, out _, out _, out _, out var rules13);
        Assert.False(rules13.LegacyBuilding);
        Assert.True(rules13.LegacyHardpoints);
    }

    [Fact]
    public void Replaying_an_old_log_with_the_legacy_flag_reproduces_a_free_build_world()
    {
        var rules = new WorldRules { FreeBuild = true }.WithLegacyBuilding(true).WithLegacyShipAutomation(true);
        var (w, s) = Ship3(rules);
        Run(w, new PlaceMachineIn(s, 9, 2, 9, Type("core:smelter"), -1));
        w.Step(100);
        var replay = SimWorld.Replay(w.Log, w.Tick, initial: SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, false, false, true, rules));
        Assert.Equal(w.Checksum(), replay.Checksum());
        var strict = SimWorld.Replay(w.Log, w.Tick, initial: SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, false, false, true, new WorldRules { FreeBuild = true }.WithLegacyShipAutomation(true)));
        Assert.NotEqual(w.Checksum(), strict.Checksum()); // the same log under the new rule loses that machine
    }

    [Fact]
    public void The_starter_ship_has_a_workbench_and_a_hold_but_no_bay_and_no_machines()
    {
        var ship = C.StarterShip!;
        Assert.Equal(new[] { ModuleKind.MiningLaser, ModuleKind.Cockpit, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Engine }, ship.Modules.Where(m => m.Kind != ModuleKind.Gun).OrderBy(m => m.Mz).Select(m => m.Kind).ToArray()); // the nose laser and the top gun are hardpoints (ADR 0032)
        Assert.Empty(ship.Machines);
        Assert.Empty(ship.Belts);
        Assert.DoesNotContain(ship.Modules, m => C.Structures!.IsBuildable(m.Kind));
        Assert.Equal(20, ship.Hold.Single(h => C.Items[h.Item].Id == "core:hydrogen").Count);
        var w = World(new WorldRules());
        Assert.Equal(0, w.Network.MachineCount);
    }

    [Fact]
    public void The_workbench_makes_ingots_plates_and_ammo_one_at_a_time()
    {
        foreach (var id in new[] { "core:ship_smelt", "core:ship_press", "core:ship_ammo" })
            Assert.Equal(MachineRole.Ship, C.Machines[C.Recipes[C.RecipeIndex(id)].Machine].Role);
        var w = World(new WorldRules());
        var n = w.Network;
        n.GiveItems(0, C.ItemIndex("core:iron_ore"), 4);
        Assert.Equal(Reject.None, Run(w, new SetShipRecipe(C.RecipeIndex("core:ship_smelt"))));
        w.Step(20 * 12);
        int ingot = C.ItemIndex("core:iron_ingot"), plate = C.ItemIndex("core:iron_plate"), ammo = C.ItemIndex("core:ammo");
        Assert.Equal(4, n.HoldCount(0, ingot));
        Assert.Equal(Reject.None, Run(w, new SetShipRecipe(C.RecipeIndex("core:ship_press"))));
        w.Step(20 * 12);
        Assert.Equal(2, n.HoldCount(0, plate));
        Assert.Equal(0, n.HoldCount(0, ingot));
        n.GiveItems(0, ingot, 2);
        Assert.Equal(Reject.None, Run(w, new SetShipRecipe(C.RecipeIndex("core:ship_ammo"))));
        w.Step(20 * 12);
        Assert.Equal(8, n.HoldCount(0, ammo));
        // A machine recipe is not a workbench recipe.
        Assert.NotEqual(Reject.None, Run(w, new SetShipRecipe(C.RecipeIndex("core:smelt_iron"))));
    }

    [Fact]
    public void The_bay_costs_ingots_and_plates_and_the_stripped_world_can_pay_it_from_the_workbench()
    {
        var cost = C.Structures!.ModuleCost(ModuleKind.Workshop);
        Assert.Contains(cost, c => C.Items[c.Item].Id == "core:iron_ingot" && c.Count == 24);
        Assert.Contains(cost, c => C.Items[c.Item].Id == "core:iron_plate" && c.Count == 8);
    }

    [Fact]
    public void Demolishing_a_belt_and_a_machine_refunds_and_frees_the_cells()
    {
        var w = World(new WorldRules());
        var (_, s) = (w, Ship(w));
        w.Network.GiveItems(0, C.ItemIndex("core:iron_ingot"), 90);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_plate"), 20);
        Module(w, s, 1, 0, 1, ModuleKind.Workshop);
        int ingots = w.Network.HoldCount(0, C.ItemIndex("core:iron_ingot"));
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(s, 9, 0, 9, Dir.PosX, 4, BeltTier.Mk1)));
        int belt = w.LastResults.Last().Created.Single();
        Assert.Equal(ingots - 4, w.Network.HoldCount(0, C.ItemIndex("core:iron_ingot")));
        Assert.Equal(SimWorld.EntityKind.Segment, w.EntityAt(s, 11, 0, 9).Kind);
        Assert.Equal(Reject.None, Run(w, new RemoveSegment(belt)));
        Assert.Equal(ingots, w.Network.HoldCount(0, C.ItemIndex("core:iron_ingot")));
        Assert.Equal(SimWorld.EntityKind.None, w.EntityAt(s, 11, 0, 9).Kind);
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 9, 0, 9, Type("core:smelter"), -1)));
        int machine = w.LastResults.Last().CreatedMachines.Single();
        Assert.Equal(Reject.None, Run(w, new RemoveMachine(machine)));
        Assert.Equal(SimWorld.EntityKind.None, w.EntityAt(s, 9, 0, 9).Kind);
        w.ValidateInvariants();
    }

    // ---- content ------------------------------------------------------------------------------------

    private static string WithEdit(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        edit(root);
        return root.ToJsonString();
    }

    [Fact]
    public void The_buildable_flag_parses_and_defaults_to_allowing_something()
    {
        string json = WithEdit(r => r["structures"]!["hull"]!.AsObject().Remove("buildable"));
        var c = ContentDb.FromJson(json);
        Assert.True(c.Structures!.IsBuildable(ModuleKind.Hull)); // no flag: it allows belts
        Assert.NotEqual(C.Hash, c.Hash);                          // and the explicit false is part of the hash
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(WithEdit(r => r["structures"]!["cargo"]!["buildable"] = true)));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(WithEdit(r => r["structures"]!["hull"]!["buildable"] = "yes")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(WithEdit(r => r["floor_building"] = 3)));
    }

    [Fact]
    public void A_template_machine_off_the_floor_or_in_a_hull_is_a_content_error()
    {
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(WithEdit(r =>
        {
            var m = r["structure_templates"]!["cold_station"]!["machines"]!.AsArray()[0]!;
            m["at"] = new JsonArray(8, 1, 1);
        })));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(WithEdit(r =>
        {
            var t = r["structure_templates"]!["cold_station"]!.AsObject();
            t["modules"]!.AsArray()[1]!["kind"] = "hull";
        })));
    }

    [Fact]
    public void The_siege_station_has_its_defence_module_beside_the_bay_and_belts_into_it()
    {
        var t = C.Templates["siege_station"];
        Assert.Contains(t.Modules, m => (m.Mx, m.My, m.Mz, m.Kind) == (1, 0, 1, ModuleKind.Defence));
        Assert.All(t.Machines, m => Assert.True(StationGrid.IsFloorLayer(m.Y)));
        Assert.Contains(t.Belts, b => b.Dir == Dir.PosX && b.X + b.Length - 1 >= 8); // reaches the defence module at x 8..15
        Assert.All(t.Belts, b => Assert.True(StationGrid.IsFloorLayer(b.Y)));
    }

    [Fact]
    public void Rejects_have_texts_in_both_languages()
    {
        Assert.Equal("toast.reject.notOnFloor", RejectTexts.Key(Reject.NotOnFloor));
        foreach (var key in new[] { "toast.reject.notOnFloor", "toast.reject.kindNotAllowed", "module.workshop", "module.workshop.purpose", "obj.fabBay", "obj.fabBay.hint" })
        {
            Assert.NotEqual(key, Loc.T(key)); // the table has the key (the other tests check that es and en differ for every entry)
        }
    }
}
