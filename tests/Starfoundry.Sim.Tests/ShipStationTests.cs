using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0039: a ship holds ship modules only (no machines, belts, turrets, walls or Fabrication bays, <see cref="Reject.UseStation"/>), stations keep the old rules,
/// the workbench is a slow queue of one craft at a time, and logs older than version 20 replay with <see cref="WorldRules.LegacyShipAutomation"/>.
/// </summary>
public class ShipStationTests
{
    private static readonly ContentDb C = TestContent.CoreFloor;
    private const ulong Seed = 20261121UL;
    private static int Type(string id) => C.MachineIndex(id);
    private static int Item(string id) => C.ItemIndex(id);

    private static SimWorld World(WorldRules? rules = null)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: (rules ?? new WorldRules { FreeBuild = true }).WithLegacyBlueprint(true).WithLegacyInterior(true));
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

    /// <summary>A world with a station founded beside the ship: core at (0,0,0), a bay at (1,0,0) and a defence module at (0,0,1); the dock module is at (0,0,-1).</summary>
    private static (SimWorld W, int Station) WithStation(WorldRules? rules = null)
    {
        var w = World(rules);
        Assert.Equal(Reject.None, Run(w, new FoundStation(10, 0, -12)));
        int st = w.Network.Structures().First(s => s.Kind == StructureKind.Station).Id;
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(st, 1, 0, 0, ModuleKind.Workshop)));
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(st, 0, 0, 1, ModuleKind.Defence)));
        return (w, st);
    }

    [Fact]
    public void A_new_world_has_station_first_rules_and_the_flag_is_off()
    {
        var w = World();
        Assert.False(w.Network.Rules.LegacyShipAutomation);
        Assert.True(w.Network.StationFirst);
        Assert.False(World(new WorldRules().WithLegacyShipAutomation(true)).Network.StationFirst);
        Assert.False(SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, structures: false).Network.StationFirst); // worlds without structures are untouched
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyShipAutomation));
    }

    [Fact]
    public void A_ship_refuses_machines_belts_turrets_walls_and_bays_with_use_station()
    {
        var w = World();
        int s = Ship(w);
        Assert.Equal(Reject.UseStation, Run(w, new PlaceModuleIn(s, 1, 0, 1, ModuleKind.Workshop)));
        Assert.Equal(Reject.UseStation, Run(w, new PlaceMachineIn(s, 1, 0, 9, Type("core:smelter"), -1)));
        Assert.Equal(Reject.UseStation, Run(w, new PlaceMachineIn(s, 1, 0, 9, Type("core:turret"), -1)));
        Assert.Equal(Reject.UseStation, Run(w, new PlaceMachineIn(s, 1, 0, 9, Type("core:wall"), -1)));
        Assert.Equal(Reject.UseStation, Run(w, new PlaceSegmentIn(s, 1, 0, 9, Dir.PosX, 3, BeltTier.Mk1)));
        Assert.Equal(0, w.Network.MachineCount);
        Assert.Equal(0, w.Network.SegmentCount);
        Assert.DoesNotContain(ModuleKind.Workshop, w.Network.GetStructure(s).Modules.Values);
        // Other ship modules still go in.
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(s, -1, 0, 2, ModuleKind.Cargo)));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void The_designer_refuses_a_bay_on_a_ship_and_the_catalogue_rule_moves_the_bay_to_stations()
    {
        Assert.True(ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Workshop)); // the old rule
        Assert.False(ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Workshop, stationFirst: true));
        Assert.True(ModuleKindIds.AllowedOn(StructureKind.Station, ModuleKind.Workshop, stationFirst: true));
        Assert.True(ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Cargo, stationFirst: true));
        Assert.False(ModuleKindIds.AllowedOn(StructureKind.Station, ModuleKind.Cockpit, stationFirst: true));
        Assert.False(ModuleKindIds.AllowedOn(StructureKind.Station, ModuleKind.Gun, stationFirst: true));
        Assert.True(ModuleKindIds.AllowedOn(StructureKind.Ship, ModuleKind.Workshop, stationFirst: false));
    }

    [Fact]
    public void A_station_keeps_bays_machines_belts_turrets_and_walls()
    {
        var (w, st) = WithStation();
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(st, 1, 0, 1, Type("core:smelter"), C.RecipeIndex("core:smelt_iron"))));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(st, 5, 0, 1, Type("core:assembler"), C.RecipeIndex("core:iron_plate"))));
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(st, 2, 0, 1, Dir.PosX, 3, BeltTier.Mk1)));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(st, 1, 0, 10, Type("core:turret"), -1)));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(st, 1, 0, 13, Type("core:wall"), -1)));
        w.Step(20 * 30);
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_legacy_world_lets_a_ship_automate_as_before()
    {
        var w = World(new WorldRules { FreeBuild = true }.WithLegacyShipAutomation(true));
        int s = Ship(w);
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(s, 1, 0, 1, ModuleKind.Workshop)));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 9, 0, 9, Type("core:smelter"), -1)));
        Assert.Equal(Reject.None, Run(w, new PlaceSegmentIn(s, 10, 0, 9, Dir.PosX, 3, BeltTier.Mk1)));
    }

    [Fact]
    public void The_use_station_reject_has_es_and_en_texts()
    {
        string? key = RejectTexts.Key(Reject.UseStation);
        Assert.NotNull(key);
        var text = Loc.All.ToDictionary(p => p.Key, p => p.Value)[key!];
        Assert.Contains("estación", text.Es);
        Assert.Contains("station", text.En);
    }

    // ---- the flag, the codec and old logs ---------------------------------------------------------------------------

    [Fact]
    public void The_flag_roundtrips_in_the_rules_block_hashes_as_default_and_keeps_the_other_flags()
    {
        var legacy = new WorldRules().WithLegacyShipAutomation(true);
        Assert.True(legacy.LegacyShipAutomation && legacy.AnyLegacy);
        Assert.Equal(2, legacy.Serialize()[0]);
        Assert.Equal(8, legacy.Serialize()[1 + 8 * 4 + 1] & 8); // bit 3 of the second flags byte
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.True(legacy.WithoutLegacyFlags().IsDefault);
        Assert.True(legacy.HashesAsDefault);
        Assert.NotEqual(WorldRules.Default, legacy);
        Assert.True(legacy.WithLegacyFlight(true).LegacyShipAutomation);
        Assert.True(legacy.WithLegacyInventory(true).LegacyShipAutomation && legacy.WithLegacyInventory(true).LegacyInventory);
        Assert.True(legacy.Clamped().LegacyShipAutomation);
        Assert.True(legacy.WithResourceDensity(120).LegacyShipAutomation);
        Assert.False(legacy.WithLegacyShipAutomation(false).LegacyShipAutomation);
        var bad = legacy.Serialize();
        bad[1 + 8 * 4 + 1] = 128; // bit 7 is the blueprint flag of ADR 0043
        Assert.True(WorldRules.Deserialize(bad).LegacyBlueprint);
    }

    [Fact]
    public void A_log_is_version_20_older_logs_read_with_the_flag_and_cannot_hold_the_bench_commands()
    {
        var w = World();
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        Assert.Equal(29, ms.ToArray()[4]);
        CommandCodec.ReadLog(new MemoryStream(ms.ToArray()), out _, out _, out _, out _, out _, out _, out var now);
        Assert.False(now.LegacyShipAutomation);
        for (byte version = 6; version <= 19; version++)
        {
            var old = ms.ToArray();
            old[4] = version;
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.True(rules.LegacyShipAutomation, $"version {version}");
            Assert.True(rules.WithoutLegacyFlags().IsDefault);
        }
        var withBench = new List<LoggedCommand> { new(0, 0, new QueueBench(0, 2)), new(1, 0, new Batch(new Command[] { new ClearBench() })) };
        var ms2 = new MemoryStream();
        CommandCodec.WriteLog(ms2, withBench, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        var back = CommandCodec.ReadLog(new MemoryStream(ms2.ToArray()));
        Assert.Equal(2, back.Count);
        Assert.Equal(withBench[0].Command, back[0].Command);
        Assert.IsType<ClearBench>(Assert.IsType<Batch>(back[1].Command).Commands.Single());
        var v19 = ms2.ToArray();
        v19[4] = 19;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(v19)));
    }

    [Fact]
    public void The_bench_commands_roundtrip_and_malformed_ones_are_refused()
    {
        foreach (Command c in new Command[] { new QueueBench(3, 1), new QueueBench(7, 5), new ClearBench() })
        {
            byte[] bytes = CommandCodec.ToBytes(c);
            Assert.Equal(c, CommandCodec.FromBytes(bytes));
        }
        Assert.Equal(63, CommandCodec.ToBytes(new QueueBench(0, 1))[0]);
        Assert.Equal(64, CommandCodec.ToBytes(new ClearBench())[0]);
        Assert.False(CommandCodec.IsWellFormed(new QueueBench(0, 0)));
        Assert.False(CommandCodec.IsWellFormed(new QueueBench(0, PlayerRules.BenchQueueMax + 1)));
        Assert.Throws<ArgumentException>(() => CommandCodec.ToBytes(new QueueBench(0, 99)));
    }

    [Fact]
    public void An_old_log_replays_byte_for_byte_with_the_legacy_ship_rules()
    {
        // What a version 19 build recorded: a ship that builds a bay, a factory and uses the repeating workbench.
        var rules = new WorldRules { FreeBuild = true }.WithLegacyShipAutomation(true);
        var w = World(rules);
        int s = Ship(w);
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(s, 1, 0, 1, ModuleKind.Workshop)));
        Assert.Equal(Reject.None, Run(w, new PlaceMachineIn(s, 9, 0, 9, Type("core:smelter"), C.RecipeIndex("core:smelt_iron"))));
        w.Network.GiveItems(0, Item("core:iron_ore"), 6);
        Assert.Equal(Reject.None, Run(w, new SetShipRecipe(C.RecipeIndex("core:ship_smelt"))));
        w.Step(20 * 30);
        Assert.Equal(6, w.Network.HoldCount(0, Item("core:iron_ingot"))); // 2 s a craft as recorded
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true, rules: rules);
        var back = CommandCodec.ReadLog(new MemoryStream(ms.ToArray()), out _, out _, out _, out _, out _, out _, out var readRules);
        Assert.True(readRules.LegacyShipAutomation);
        var prepared = World(rules); prepared.Network.GiveItems(0, Item("core:iron_ore"), 6);
        var replay = SimWorld.Replay(back, w.Tick, initial: prepared);
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.Network.ValidateInvariants();
    }

    // ---- the workbench -----------------------------------------------------------------------------------------------

    private static int Smelt => C.RecipeIndex("core:ship_smelt");
    private static int Press => C.RecipeIndex("core:ship_press");
    private static int Ammo => C.RecipeIndex("core:ship_ammo");
    private const int Tps = Units.TicksPerSecond;

    [Fact]
    public void The_bench_makes_one_item_every_8_seconds_one_at_a_time()
    {
        var w = World();
        var n = w.Network;
        n.GiveItems(0, Item("core:iron_ore"), 3);
        Assert.Equal(Reject.None, Run(w, new QueueBench(Smelt, 3)));
        Assert.Equal(2, n.GetPlayerInfo(0).BenchQueue.Count); // the first craft started at once and took its ore
        Assert.True(n.GetPlayerInfo(0).ShipCrafting);
        int ingot = Item("core:iron_ingot");
        w.Step(Tps * 7);
        Assert.Equal(0, n.HoldCount(0, ingot));
        Assert.True(n.GetPlayerInfo(0).ShipCrafting);
        w.Step(Tps * 2);
        Assert.Equal(1, n.HoldCount(0, ingot)); // the first finished after 8 s
        Assert.Equal(1, n.GetPlayerInfo(0).BenchQueue.Count);
        w.Step(Tps * 8);
        Assert.Equal(2, n.HoldCount(0, ingot)); // never two at a time
        w.Step(Tps * 8);
        Assert.Equal(3, n.HoldCount(0, ingot));
        Assert.Empty(n.GetPlayerInfo(0).BenchQueue);
        Assert.False(n.GetPlayerInfo(0).ShipCrafting);
        n.ValidateInvariants();
    }

    [Fact]
    public void The_bench_runs_a_mixed_queue_in_order()
    {
        var w = World();
        var n = w.Network;
        n.GiveItems(0, Item("core:iron_ore"), 2);
        Assert.Equal(Reject.None, Run(w, new QueueBench(Smelt, 2)));
        Assert.Equal(Reject.None, Run(w, new QueueBench(Press, 1)));
        Assert.Equal(Reject.None, Run(w, new QueueBench(Ammo, 1)));
        w.Step(Tps * 8 * 4 + 20);
        Assert.Equal(1, n.HoldCount(0, Item("core:iron_plate"))); // 2 ingots smelted, then pressed
        Assert.Equal(0, n.HoldCount(0, Item("core:iron_ingot")));
        Assert.Equal(0, n.HoldCount(0, Item("core:ammo"))); // the ammo craft had no ingot left to wait on
        Assert.Single(n.GetPlayerInfo(0).BenchQueue);
        n.GiveItems(0, Item("core:iron_ingot"), 1);
        w.Step(Tps * 9);
        Assert.Equal(4, n.HoldCount(0, Item("core:ammo")));
        n.ValidateInvariants();
    }

    [Fact]
    public void The_bench_queue_holds_five_and_waits_for_inputs()
    {
        var w = World();
        var n = w.Network;
        Assert.Equal(Reject.None, Run(w, new QueueBench(Press, 5)));
        Assert.Equal(Reject.NoRoom, Run(w, new QueueBench(Smelt, 1)));
        Assert.Equal(5, n.GetPlayerInfo(0).BenchQueue.Count);
        w.Step(Tps * 20);
        Assert.Equal(5, n.GetPlayerInfo(0).BenchQueue.Count); // no ingots: nothing starts, nothing is lost
        Assert.False(n.GetPlayerInfo(0).ShipCrafting);
        n.GiveItems(0, Item("core:iron_ingot"), 4);
        w.Step(Tps * 8 * 2 + 10);
        Assert.Equal(2, n.HoldCount(0, Item("core:iron_plate")));
        Assert.Equal(0, n.HoldCount(0, Item("core:iron_ingot")));
        Assert.Equal(3, n.GetPlayerInfo(0).BenchQueue.Count);
        n.ValidateInvariants();
    }

    [Fact]
    public void The_bench_only_takes_workbench_recipes_and_clear_returns_the_inputs()
    {
        var w = World();
        var n = w.Network;
        Assert.Equal(Reject.NotAShipRecipe, Run(w, new QueueBench(C.RecipeIndex("core:smelt_iron"), 1)));
        Assert.Equal(Reject.UnknownRecipe, Run(w, new QueueBench(9999, 1)));
        n.GiveItems(0, Item("core:iron_ingot"), 2);
        Assert.Equal(Reject.None, Run(w, new QueueBench(Ammo, 3)));
        w.Step(Tps * 2);
        Assert.True(n.GetPlayerInfo(0).ShipCrafting);
        Assert.Equal(1, n.HoldCount(0, Item("core:iron_ingot"))); // the first craft took its ingot
        Assert.Equal(Reject.None, Run(w, new ClearBench()));
        Assert.Equal(2, n.HoldCount(0, Item("core:iron_ingot"))); // and gave it back
        Assert.Empty(n.GetPlayerInfo(0).BenchQueue);
        Assert.False(n.GetPlayerInfo(0).ShipCrafting);
        n.ValidateInvariants();
    }

    [Fact]
    public void A_station_first_world_refuses_the_repeating_recipe_and_a_legacy_world_refuses_the_queue()
    {
        Assert.Equal(Reject.NotAllowedHere, Run(World(), new SetShipRecipe(Smelt)));
        var legacy = World(new WorldRules { FreeBuild = true }.WithLegacyShipAutomation(true));
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new QueueBench(Smelt, 1)));
        Assert.Equal(Reject.NotAllowedHere, Run(legacy, new ClearBench()));
        Assert.Equal(Reject.None, Run(legacy, new SetShipRecipe(Smelt)));
    }

    [Fact]
    public void The_workbench_is_deterministic_replays_clones_and_saves_with_a_queue()
    {
        var w = World();
        w.Network.GiveItems(0, Item("core:iron_ore"), 5);
        Assert.Equal(Reject.None, Run(w, new QueueBench(Smelt, 4)));
        w.Step(Tps * 10);
        Assert.Equal(Reject.None, Run(w, new QueueBench(Press, 1)));
        w.Step(Tps * 5);
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true, rules: w.Network.Rules);
        var log = CommandCodec.ReadLog(new MemoryStream(ms.ToArray()));
        var prepared = World(); prepared.Network.GiveItems(0, Item("core:iron_ore"), 5);
        var replay = SimWorld.Replay(log, w.Tick, initial: prepared);
        Assert.Equal(w.Checksum(), replay.Checksum());
        var clone = w.Network.Clone();
        Assert.Equal(w.Checksum(), clone.Checksum());
        Assert.Equal(w.Network.GetPlayerInfo(0).BenchQueue, clone.GetPlayerInfo(0).BenchQueue);
        w.Step(Tps * 30); clone.Step(Tps * 30);
        Assert.Equal(w.Checksum(), clone.Checksum());
        Assert.NotEqual(0, w.Network.HoldCount(0, Item("core:iron_ingot")) + w.Network.HoldCount(0, Item("core:iron_plate")));
        // The queue is part of the checksum.
        var a = World(); a.Network.GiveItems(0, Item("core:iron_ore"), 1);
        var b = World(); b.Network.GiveItems(0, Item("core:iron_ore"), 1);
        Run(a, new QueueBench(Smelt, 1)); Run(b, new QueueBench(Smelt, 2));
        Assert.NotEqual(a.Checksum(), b.Checksum());
    }
}
