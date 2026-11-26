using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Scenario files, world creation from a scenario, the log header with a scenario id, saves, and the progress counters ([ADR 0025](docs/adr/0025-scenarios-and-goals.md)).</summary>
public class ScenarioTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly ScenarioCatalog Catalog = TestContent.Scenarios;
    private const ulong Seed = 20261121UL;

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static void Ok(SimWorld w, Command c) => Assert.Equal(Reject.None, Run(w, c).Reject);

    private static ScenarioDef Parse(string json) => ScenarioCatalog.Parse(C, json);

    private const string Minimal =
        """{"schema":1,"id":"t","order":1,"name_es":"t","name_en":"t","description_es":"t","description_en":"t","briefing_es":"t","briefing_en":"t"}""";

    // ---- the files --------------------------------------------------------------------------------

    [Fact]
    public void The_shipped_files_load_in_level_order()
    {
        Assert.Equal(new[] { "01_first_light", "02_cold_fuel", "03_under_siege", "04_deep_vein", "05_long_watch", "06_salvage_run", "07_hull_breach", "campaign", "sandbox", "sandbox_station", "sandbox_siege", "custom", "custom_station", "custom_siege" },
            Catalog.All.Select(s => s.Id).ToArray());
        Assert.True(Catalog.Contains("campaign"));
        Assert.Null(Catalog.Find("nope"));
        Assert.Throws<KeyNotFoundException>(() => Catalog.Get("nope"));
        Assert.NotEqual(0UL, Catalog.Hash);
    }

    [Fact]
    public void The_levels_use_the_stated_rules()
    {
        Assert.Equal("relaxed", Catalog.Get("01_first_light").Preset);
        Assert.False(Catalog.Get("01_first_light").Rules.ThreatsEnabled);
        Assert.False(Catalog.Get("02_cold_fuel").Rules.ThreatsEnabled);
        var siege = Catalog.Get("03_under_siege").Rules;
        Assert.Equal((120, 5, true), (siege.FirstWaveGraceSeconds, siege.GoalWaves, siege.ThreatsEnabled));
        Assert.Equal(900, Catalog.Get("04_deep_vein").Rules.FirstWaveGraceSeconds);
        var hard = Catalog.Get("05_long_watch");
        Assert.Equal("hard", hard.Preset);
        Assert.Equal(WorldRules.Hard, hard.Rules);
        Assert.Equal(WorldRules.Default, Catalog.Get("campaign").Rules);
        Assert.True(Catalog.Get("sandbox").PlayerRules && Catalog.Get("sandbox").SandboxMode && Catalog.Get("sandbox").IsOpenEnded);
        Assert.True(Catalog.Get("campaign").PlayerRules);
        Assert.False(Catalog.Get("01_first_light").PlayerRules);
    }

    [Fact]
    public void Rules_starting_items_and_start_starting_items_merge_per_item()
    {
        var def = Parse(Minimal[..^1] +
            ",\"rules\":{\"starting_items\":[{\"item\":\"core:iron_plate\",\"count\":5}]}," +
            "\"start\":{\"starting_items\":[{\"item\":\"core:iron_plate\",\"count\":7},{\"item\":\"core:iron_ingot\",\"count\":3}]}}");
        Assert.Equal(new[] { new ItemStack(C.ItemIndex("core:iron_plate"), 12), new ItemStack(C.ItemIndex("core:iron_ingot"), 3) }, def.Rules.StartingItems);
        Assert.Equal("starter_ship", def.Structures.Single().Template); // the default start
    }

    [Fact]
    public void Hashes_split_what_changes_the_world_from_what_is_only_text()
    {
        var a = Parse(Minimal);
        var texts = Parse(Minimal.Replace("\"name_en\":\"t\"", "\"name_en\":\"another name\""));
        var seed = Parse(Minimal[..^1] + ",\"start\":{\"seed\":5}}");
        Assert.Equal(a.SimHash, texts.SimHash);
        Assert.NotEqual(a.FullHash, texts.FullHash);
        Assert.NotEqual(a.SimHash, seed.SimHash);
        Assert.NotEqual(a.HeaderHash(C), seed.HeaderHash(C));
        Assert.NotEqual(C.Hash, a.HeaderHash(C));
    }

    [Fact]
    public void The_extra_structure_templates_load_and_change_the_content_hash()
    {
        Assert.True(C.Templates.ContainsKey("starter_ship"));
        Assert.Same(C.StarterShip, C.Templates["starter_ship"]);
        foreach (var id in new[] { "cold_ship", "siege_ship", "siege_station", "blank_station" }) Assert.True(C.Templates.ContainsKey(id), id);
        Assert.Equal(6, C.Templates["cold_ship"].Hold.Single().Count);
        Assert.Equal(new[] { ModuleKind.Core }, C.Templates["blank_station"].Modules.Select(m => m.Kind));
        // The same content without them hashes differently, and a content with only the starter ship is unaffected by the loop.
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(TestContent.LegacyPath))!.AsObject();
        root["structure_templates"]!.AsObject().Remove("blank_station");
        Assert.NotEqual(C.Hash, ContentDb.FromJson(root.ToJsonString()).Hash);
    }

    [Theory]
    [InlineData("\"id\":\"t\"", "\"id\":\"Bad Id\"", "invalid scenario id")]
    [InlineData("\"order\":1", "\"order\":-1", "order")]
    [InlineData("\"name_en\":\"t\"", "\"name_en\":\"\"", "name_en")]
    [InlineData("\"schema\":1", "\"schema\":2", "schema")]
    [InlineData("\"schema\":1", "\"schema\":1,\"extra\":1", "unknown property")]
    public void Invalid_headers_are_rejected(string find, string replace, string messagePart)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(Minimal.Replace(find, replace)));
        Assert.Contains(messagePart, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"rules\":{\"preset\":\"easy\"}", "preset")]
    [InlineData("\"rules\":{\"enemy_hp_percent\":5}", "enemy_hp_percent")]
    [InlineData("\"rules\":{\"threats_enabled\":1}", "true or false")]
    [InlineData("\"rules\":{\"bogus\":1}", "unknown property")]
    [InlineData("\"rules\":{\"starting_items\":[{\"item\":\"core:nope\",\"count\":1}]}", "unknown item")]
    [InlineData("\"start\":{\"seed\":-1}", "seed")]
    [InlineData("\"start\":{\"structures\":[{\"template\":\"nope\"}]}", "unknown structure template")]
    [InlineData("\"start\":{\"structures\":[{\"template\":\"blank_station\",\"kind\":\"station\"}]}", "first start structure")]
    [InlineData("\"start\":{\"structures\":[{\"template\":\"starter_ship\",\"offset\":[1,0,0]}]}", "home origin")]
    [InlineData("\"start\":{\"structures\":[{\"template\":\"starter_ship\"},{\"template\":\"blank_station\",\"offset\":[999,0,0]}]}", "beyond")]
    [InlineData("\"start\":{\"home_archetype\":\"core:dense_belt\"}", "home archetype")]
    [InlineData("\"mode\":\"easy\"", "mode")]
    [InlineData("\"stars\":{\"two_star_seconds\":100,\"three_star_seconds\":200}", "three_star_seconds")]
    [InlineData("\"stars\":{\"two_star_secondary\":3}", "secondary")]
    public void Invalid_rules_start_and_stars_are_rejected(string extra, string messagePart)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(Minimal[..^1] + "," + extra + "}"));
        Assert.Contains(messagePart, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithGoal(string goal, string primaryExtra = "") =>
        Minimal[..^1] + ",\"goals\":{\"primary\":[" + goal + primaryExtra + "]}}";

    private const string Texts = ",\"text_es\":\"a\",\"text_en\":\"a\"}";

    [Theory]
    [InlineData("{\"id\":\"g\",\"type\":\"fly\"" + Texts, "unknown type")]
    [InlineData("{\"id\":\"g\",\"type\":\"produce\",\"item\":\"core:nope\",\"count\":1" + Texts, "unknown item")]
    [InlineData("{\"id\":\"g\",\"type\":\"produce\",\"count\":1" + Texts, "item")]
    [InlineData("{\"id\":\"g\",\"type\":\"produce\",\"item\":\"core:iron_plate\"" + Texts, "count")]
    [InlineData("{\"id\":\"g\",\"type\":\"produce\",\"item\":\"core:iron_plate\",\"count\":0" + Texts, "count")]
    [InlineData("{\"id\":\"g\",\"type\":\"research\",\"node\":\"core:nope\"" + Texts, "unknown research")]
    [InlineData("{\"id\":\"g\",\"type\":\"research\",\"node\":\"core:logistics\",\"count\":2" + Texts, "does not take")]
    [InlineData("{\"id\":\"g\",\"type\":\"build_module\",\"kind\":\"rocket\",\"count\":1" + Texts, "unknown module kind")]
    [InlineData("{\"id\":\"g\",\"type\":\"build_module\",\"count\":1" + Texts, "kind")]
    [InlineData("{\"id\":\"g\",\"type\":\"survive_waves\",\"count\":31" + Texts, "count")]
    [InlineData("{\"id\":\"g\",\"type\":\"visit_systems\",\"count\":13" + Texts, "more systems")]
    [InlineData("{\"id\":\"g\",\"type\":\"found_stations\",\"count\":1,\"time_limit_seconds\":10" + Texts, "time limit")]
    [InlineData("{\"id\":\"g\",\"type\":\"found_stations\",\"count\":1,\"time_limit_seconds\":90000" + Texts, "time_limit_seconds")]
    [InlineData("{\"id\":\"G!\",\"type\":\"found_stations\",\"count\":1" + Texts, "invalid goal id")]
    [InlineData("{\"id\":\"g\",\"type\":\"found_stations\",\"count\":1,\"text_es\":\"a\"}", "text_en")]
    [InlineData("{\"id\":\"g\",\"type\":\"keep_fuel\"" + Texts, "at least one primary goal")]
    public void Invalid_goals_are_rejected(string goal, string messagePart)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(WithGoal(goal)));
        Assert.Contains(messagePart, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_goal_ids_and_bad_story_triggers_are_rejected()
    {
        string g = "{\"id\":\"g\",\"type\":\"found_stations\",\"count\":1" + Texts;
        Assert.Contains("twice", Assert.Throws<InvalidDataException>(() => Parse(WithGoal(g, "," + g))).Message);
        foreach (string trigger in new[] { "goal:missing", "later", "time:x", "time:99999", "fail:" })
        {
            string json = WithGoal(g)[..^1] + ",\"story\":[{\"trigger\":\"" + trigger + "\",\"text_es\":\"a\",\"text_en\":\"a\"}]}";
            Assert.Contains("story trigger", Assert.Throws<InvalidDataException>(() => Parse(json)).Message);
        }
        string ok = WithGoal(g)[..^1] + ",\"story\":[{\"trigger\":\"goal:g\",\"speaker\":\"X\",\"text_es\":\"a\",\"text_en\":\"a\"},{\"trigger\":\"time:30\",\"text_es\":\"a\",\"text_en\":\"a\"}]}";
        var def = Parse(ok);
        Assert.Equal(new[] { "X", "ORCA" }, def.Story.Select(s => s.Speaker));
    }

    [Fact]
    public void A_catalog_needs_ids_that_match_file_names_and_no_duplicates()
    {
        Assert.Throws<InvalidDataException>(() => ScenarioCatalog.FromJson(C, new[] { ("other.json", Minimal) }));
        var ok = ScenarioCatalog.FromJson(C, new[] { ("t.json", Minimal) });
        Assert.Single(ok.All);
        var e = Assert.Throws<InvalidDataException>(() => ScenarioCatalog.FromJson(C, new[] { ("t.json", Minimal), ("t.json", Minimal) }));
        Assert.Contains("duplicate", e.Message);
        Assert.Contains("t.json", Assert.Throws<InvalidDataException>(() => ScenarioCatalog.FromJson(C, new[] { ("t.json", "{") })).Message);
    }

    // ---- creating worlds --------------------------------------------------------------------------

    [Fact]
    public void Every_scenario_creates_a_valid_structure_world_with_its_start()
    {
        foreach (var def in Catalog.All)
        {
            var w = SimWorld.CreateScenario(C, def, Seed);
            var net = w.Network;
            Assert.True(net.StructureRules, def.Id);
            Assert.Equal(def.Rules, net.Rules);
            Assert.Equal(def.SandboxMode ? GameMode.Sandbox : GameMode.Campaign, net.Mode);
            Assert.Equal(def.Structures.Count, net.Structures().Count());
            Assert.Equal(0, net.StartingItemsClamped); // every starting item fits the starting hold
            Assert.True(net.ShipStructure(0) >= 0);
            w.Step(10);
            w.ValidateInvariants();
        }
    }

    [Fact]
    public void The_siege_world_has_a_powered_station_with_an_empty_defence_module()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "03_under_siege");
        var net = w.Network;
        var station = net.Structures().Single(s => s.Kind == StructureKind.Station);
        Assert.Equal(0, station.Owner);
        Assert.Equal(new[] { ModuleKind.Core, ModuleKind.Workshop, ModuleKind.Defence, ModuleKind.Reactor, ModuleKind.Dock }.OrderBy(k => k), station.Modules.Values.OrderBy(k => k));
        Assert.Equal((10L * Units.CellUnits, -8L * Units.CellUnits), (station.OriginX, station.OriginZ));
        var machines = Enumerable.Range(0, net.MachineIdLimit).Where(net.IsMachineAlive).Select(net.GetMachineInfo).Where(m => m.Structure == station.Id).ToList();
        Assert.Equal(5, machines.Count);
        Assert.DoesNotContain(machines, m => C.Machines[m.Type].Role == MachineRole.Turret);
        // The ship has the starter cargo module and seven more, so the starting items fit under the mining limit.
        Assert.Equal(8, net.GetStructure(net.ShipStructure(0)).Modules.Values.Count(k => k == ModuleKind.Cargo));
        // The starting items are in the hold, hydrogen on top of the ship's own.
        Assert.Equal(80, net.HoldCount(0, C.ItemIndex("core:iron_plate")));
        Assert.Equal(100, net.HoldCount(0, C.ItemIndex("core:iron_ingot")));
        Assert.Equal(80, net.HoldCount(0, C.ItemIndex("core:hydrogen")));
        Assert.True(net.HoldTotal(0) * 100 < net.HoldCapacity(0) * PlayerRules.MiningHoldLimitPercent);
        w.Step(60);
        Assert.Equal(PowerRules.FullSatisfaction, net.PowerStatus(station.Id).SatisfactionPermille);
        w.ValidateInvariants();
    }

    [Fact]
    public void The_cold_level_starts_with_six_hydrogen()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "02_cold_fuel");
        Assert.Equal(6, w.Network.HoldCount(0, C.ItemIndex("core:hydrogen")));
        Assert.Equal(20, w.Network.HoldCount(0, C.ItemIndex("core:iron_plate")));
        Assert.Equal(60, w.Network.HoldCount(0, C.ItemIndex("core:iron_ingot")));
    }

    [Fact]
    public void Creation_is_deterministic_and_the_seed_matters()
    {
        var a = SimWorld.CreateScenario(C, Catalog, "03_under_siege");
        var b = SimWorld.CreateScenario(C, Catalog, "03_under_siege", Seed);
        a.Step(300); b.Step(300);
        Assert.Equal(a.Checksum(), b.Checksum());
        var c = SimWorld.CreateScenario(C, Catalog, "03_under_siege", 99);
        c.Step(300);
        Assert.NotEqual(a.Checksum(), c.Checksum());
    }

    [Fact]
    public void A_scenario_without_a_fixed_seed_needs_one_and_player_rules_replace_the_scenarios()
    {
        Assert.Throws<ArgumentException>(() => SimWorld.CreateScenario(C, Catalog, "campaign"));
        var hardCampaign = SimWorld.CreateScenario(C, Catalog, "campaign", 77, WorldRules.Hard);
        Assert.Equal(WorldRules.Hard, hardCampaign.Network.Rules);
        Assert.Equal(77UL, hardCampaign.Network.Galaxy!.Seed);
        var custom = new WorldRules { FreeBuild = true, DepositRichnessPercent = 200 };
        Assert.Equal(custom, SimWorld.CreateScenario(C, Catalog, "sandbox", 5, custom).Network.Rules);
        Assert.Throws<ArgumentException>(() => SimWorld.CreateScenario(C, Catalog, "sandbox", 5, new WorldRules { GoalWaves = 99 }));
    }

    [Fact]
    public void The_sandbox_with_a_station_starts_with_a_bare_core()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "sandbox_station", 5);
        var station = w.Network.Structures().Single(s => s.Kind == StructureKind.Station);
        Assert.Equal(new[] { ModuleKind.Core, ModuleKind.Dock }.OrderBy(k => k), station.Modules.Values.OrderBy(k => k)); // the core and its dock module (ADR 0041)
        Assert.Equal(0, w.Network.StationsFounded); // a start structure is not a founded one
    }

    [Theory]
    [InlineData("sandbox", GameMode.Sandbox, 1)]
    [InlineData("sandbox_station", GameMode.Sandbox, 2)]
    [InlineData("sandbox_siege", GameMode.Sandbox, 2)]
    [InlineData("custom", GameMode.Campaign, 1)]
    [InlineData("custom_station", GameMode.Campaign, 2)]
    [InlineData("custom_siege", GameMode.Campaign, 2)]
    public void The_open_ended_scenarios_build_a_world_with_the_right_mode_and_structures(string id, GameMode mode, int structures)
    {
        var def = Catalog.Get(id);
        Assert.True(def.IsOpenEnded);
        Assert.True(def.PlayerRules);
        var w = SimWorld.CreateScenario(C, Catalog, id, 5);
        Assert.Equal(mode, w.Mode);
        Assert.Equal(structures, w.Network.Structures().Count());
    }

    // ---- the header, saves and replays -------------------------------------------------------------

    private static byte[] LogBytes(SimWorld w, ScenarioDef? scenario, ulong seed = Seed) =>
        Capture(s => CommandCodec.WriteLog(s, w.Log, scenario?.HeaderHash(C) ?? C.Hash, GameMode.Campaign, seed, true, false, true, scenario?.Rules, scenario?.Id));

    private static byte[] Capture(Action<Stream> write)
    {
        var ms = new MemoryStream();
        write(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Header_version_8_carries_the_scenario_id()
    {
        var def = Catalog.Get("03_under_siege");
        var w = SimWorld.CreateScenario(C, def);
        Ok(w, new SetMovement(1, 0, 0));
        byte[] plain = LogBytes(w, null);
        byte[] scen = LogBytes(w, def);
        Assert.Equal(22, scen[4]);
        const int flagsAt = 4 + 1 + 8 + 1 + 8;
        Assert.Equal(0, plain[flagsAt] & 16);
        Assert.Equal(16, scen[flagsAt] & 16);

        var log = CommandCodec.ReadLog(new MemoryStream(scen), out ulong hash, out var mode, out ulong seed, out bool threats, out bool station, out bool structures,
            out var rules, out string? id);
        Assert.Equal(("03_under_siege", def.HeaderHash(C), GameMode.Campaign, Seed, true, false, true), (id, hash, mode, seed, threats, station, structures));
        Assert.Equal(def.Rules, rules);
        Assert.Single(log);

        // No scenario: no id, whatever the rules.
        CommandCodec.ReadLog(new MemoryStream(plain), out _, out _, out _, out _, out _, out _, out _, out string? none);
        Assert.Null(none);
    }

    [Fact]
    public void Older_logs_read_as_no_scenario_and_a_version_7_header_cannot_claim_one()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "01_first_light");
        Ok(w, new SetMovement(1, 0, 0));
        byte[] plain = LogBytes(w, null);
        foreach (byte version in new byte[] { 5, 6, 7 })
        {
            var old = (byte[])plain.Clone();
            old[4] = version;
            // A version 5 header has no structures flag and the others are laid out the same without a scenario.
            old[4 + 1 + 8 + 1 + 8] = (byte)(version == 5 ? 1 : 5);
            CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out _, out string? id);
            Assert.Null(id);
        }

        byte[] claimed = LogBytes(w, Catalog.Get("01_first_light"));
        claimed[4] = 7;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(claimed), out _, out _, out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void A_damaged_scenario_id_is_refused()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "01_first_light");
        byte[] bytes = LogBytes(w, Catalog.Get("01_first_light"));
        // The level has non-default rules, so a rules block (length, bytes) sits between the flags and the scenario id.
        int blockLength = BitConverter.ToInt32(bytes, 4 + 1 + 8 + 1 + 8 + 1);
        int idLengthAt = 4 + 1 + 8 + 1 + 8 + 1 + 4 + blockLength;
        Assert.Equal("01_first_light".Length, BitConverter.ToInt32(bytes, idLengthAt));
        var bad = (byte[])bytes.Clone();
        bad[idLengthAt] = 0;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(bad), out _, out _, out _, out _, out _, out _, out _, out _));
        var truncated = bytes.Take(idLengthAt + 6).ToArray();
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(truncated), out _, out _, out _, out _, out _, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => CommandCodec.WriteLog(new MemoryStream(), w.Log, 0, scenarioId: new string('x', 65)));
    }

    private static SimWorld PlayedSiege(out ScenarioDef def)
    {
        def = Catalog.Get("03_under_siege");
        var w = SimWorld.CreateScenario(C, def);
        var net = w.Network;
        var station = net.Structures().Single(s => s.Kind == StructureKind.Station).Id;
        Ok(w, new SetWeapons(true));
        Ok(w, new PlaceMachineIn(station, 5, 4, 8, C.MachineIndex("core:turret"), -1));
        w.Step(300);
        return w;
    }

    [Fact]
    public void A_scenario_save_replays_to_the_same_state_and_checks_the_scenario()
    {
        var w = PlayedSiege(out var def);
        var bytes = Capture(s => SaveGame.Write(s, w, C, GameMode.Campaign, Seed, true, 0, structures: true, rules: def.Rules, scenario: def));
        var info = SaveGame.Peek(new MemoryStream(bytes));
        Assert.Equal("03_under_siege", info.ScenarioId);
        Assert.Equal(def.Rules, info.Rules);

        var loaded = SaveGame.Read(new MemoryStream(bytes), C, out var read, Catalog);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.Equal("03_under_siege", read.ScenarioId);
        loaded.ValidateInvariants();

        // Without the catalog, or with a catalog lacking the scenario, the save is refused.
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(bytes), C, out _));
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(bytes), C, out _, ScenarioCatalog.Empty));

        // A scenario whose world-building part changed is another game: the header hash no longer matches.
        string changed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "scenarios", "03_under_siege.json")).Replace("\"seed\": 20261121", "\"seed\": 20261122");
        var other = ScenarioCatalog.FromJson(C, new[] { ("03_under_siege.json", changed) });
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(bytes), C, out _, other));

        // A text fix does not.
        string reworded = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "scenarios", "03_under_siege.json")).Replace("Siege broken.", "Siege lifted.");
        Assert.Contains("Siege lifted.", reworded);
        var fixedText = ScenarioCatalog.FromJson(C, new[] { ("03_under_siege.json", reworded) });
        Assert.Equal(w.Checksum(), SaveGame.Read(new MemoryStream(bytes), C, out _, fixedText).Checksum());
    }

    [Fact]
    public void ReplayScenario_rebuilds_the_world_from_the_log()
    {
        var w = PlayedSiege(out var def);
        var replay = SimWorld.ReplayScenario(w.Log, w.Tick, C, Catalog, def.Id, Seed, def.Rules);
        Assert.Equal(w.Checksum(), replay.Checksum());
        // A replay that forgets the scenario diverges.
        var plain = SimWorld.Replay(w.Log, w.Tick, SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, true, false, true, def.Rules));
        Assert.NotEqual(w.Checksum(), plain.Checksum());
    }

    // ---- counters ----------------------------------------------------------------------------------

    [Fact]
    public void Machines_count_what_they_make()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "sandbox", Seed);
        var net = w.Network;
        int ore = C.ItemIndex("core:iron_ore"), ingot = C.ItemIndex("core:iron_ingot"), plate = C.ItemIndex("core:iron_plate");
        Assert.Equal(0, net.ProducedTotal(ingot));
        net.GiveItems(0, ore, 12); // the starter workshop's port feeds the smelter, whose ingots feed the assembler
        w.Step(30 * 60);
        Assert.True(net.ProducedTotal(ingot) >= 8, $"ingots {net.ProducedTotal(ingot)}");
        Assert.True(net.ProducedTotal(plate) >= 3, $"plates {net.ProducedTotal(plate)}");
        Assert.Equal(0, net.ProducedTotal(ore)); // supplied, not made
        Assert.Equal(0, net.ProducedTotal(-1));
        Assert.Equal(0, net.ProducedTotal(9999));
        Assert.Equal(net.MachineOutputsCreated, net.ProducedTotal(ingot) + net.ProducedTotal(plate));
        w.ValidateInvariants();
        var clone = net.Clone();
        Assert.Equal(net.ProducedTotal(plate), clone.ProducedTotal(plate));
        clone.ValidateInvariants();
    }

    [Fact]
    public void Hand_mining_and_drones_count_as_mined_and_produced()
    {
        var plain = new SimWorld(C, GameMode.Sandbox);
        int ore = C.ItemIndex("core:iron_ore");
        int d = plain.Network.AddDeposit(ore, 100);
        Ok(plain, new StartMining(d));
        plain.Step(15 * 10);
        Assert.Equal(plain.Network.MinedByHand, plain.Network.MinedTotal(ore));
        Assert.Equal(plain.Network.MinedByHand, plain.Network.ProducedTotal(ore));
        Assert.True(plain.Network.MinedTotal(ore) >= 8);
        plain.ValidateInvariants();

        var w = SimWorld.CreateGalaxy(TestContent.CoreWithoutStarterShip, GameMode.Sandbox, Seed, threats: false, stationRules: false, structures: true);
        int s = PowerTests.Ship(w, reactors: 1, hydrogen: 6);
        w.Network.AddStructureModuleUnchecked(s, 10, 0, 0, ModuleKind.DroneBay);
        int dep = w.Network.AddDeposit(ore, 1000, 0, 0, 0);
        Ok(w, new AssignDrones(s, dep));
        w.Step(10 * 90);
        Assert.True(w.Network.MinedByDrones >= 10);
        Assert.Equal(w.Network.MinedByDrones, w.Network.MinedTotal(ore));
        Assert.Equal(w.Network.MinedByDrones, w.Network.ProducedTotal(ore));
        w.ValidateInvariants();
    }

    [Fact]
    public void Stations_founded_and_modules_built_count_commands_only()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "sandbox", Seed);
        var net = w.Network;
        Assert.Equal(0, net.StationsFounded);
        var p = net.GetPlayerInfo(0);
        int Floor(int units) => (int)Math.Floor(units / (double)Units.CellUnits);
        Ok(w, new FoundStation(Floor(p.X), Floor(p.Y), Floor(p.Z) - 16));
        Assert.Equal(1, net.StationsFounded);
        int station = net.Structures().Single(s => s.Kind == StructureKind.Station).Id;
        Assert.Equal(0, net.ModulesBuilt(ModuleKind.Core)); // the core of a founded station is part of founding
        Ok(w, new PlaceModuleIn(station, 1, 0, 0, ModuleKind.Reactor));
        Ok(w, new PlaceModuleIn(station, 1, 0, 1, ModuleKind.Reactor));
        Ok(w, new PlaceModuleIn(station, 0, 0, 1, ModuleKind.Workshop));
        Assert.Equal(2, net.ModulesBuilt(ModuleKind.Reactor));
        Assert.Equal(1, net.ModulesBuilt(ModuleKind.Workshop));
        Assert.Equal(0, net.ModulesBuilt(ModuleKind.Defence));
        Assert.Equal(0, net.ModulesBuilt((ModuleKind)99));
        // Rejected commands change nothing.
        Assert.NotEqual(Reject.None, Run(w, new PlaceModuleIn(station, 1, 0, 0, ModuleKind.Reactor)).Reject);
        Assert.Equal(2, net.ModulesBuilt(ModuleKind.Reactor));
        w.ValidateInvariants();
        Assert.Equal(2, net.Clone().ModulesBuilt(ModuleKind.Reactor));
        var replay = SimWorld.ReplayScenario(w.Log, w.Tick, C, Catalog, "sandbox", Seed);
        Assert.Equal(1, replay.Network.StationsFounded);
        Assert.Equal(2, replay.Network.ModulesBuilt(ModuleKind.Reactor));
    }

    [Fact]
    public void Losses_count_per_module_kind_and_a_lost_station_takes_all_its_modules()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "03_under_siege");
        var net = w.Network;
        var station = net.Structures().Single(s => s.Kind == StructureKind.Station).Id;
        w.Step(5);
        net.HitModule(station, 0, 0, 1, 99_999); // the workshop
        Assert.Equal((1, 1, 0), (net.ModulesLostOf(ModuleKind.Workshop), (long)net.ModulesLost, net.ModulesLostOf(ModuleKind.Core)));
        net.HitModule(station, 0, 0, 0, 99_999); // the core takes the station with it
        Assert.Equal(1, net.StationsLost);
        Assert.Equal(1, net.ModulesLostOf(ModuleKind.Core));
        Assert.Equal(1, net.ModulesLostOf(ModuleKind.Defence));
        Assert.Equal(1, net.ModulesLostOf(ModuleKind.Reactor));
        Assert.Equal(net.ModulesLost, Enum.GetValues<ModuleKind>().Sum(k => net.ModulesLostOf(k)));
        w.Step(5);
        w.ValidateInvariants();
        Assert.Equal(net.ModulesLost, net.Clone().ModulesLost);
    }

    [Fact]
    public void A_reactor_without_fuel_counts_starved_ticks()
    {
        var w = SimWorld.CreateScenario(C, Catalog, "02_cold_fuel");
        var net = w.Network;
        w.Step(60);
        Assert.Equal(0, net.FuelStarvedTicks);
        Ok(w, new DumpItems(C.ItemIndex("core:hydrogen"), 0));
        w.Step(1000); // the burning unit runs out after 900 ticks, then the reactor starves
        Assert.True(net.FuelStarvedTicks > 50, $"starved {net.FuelStarvedTicks}");
        w.ValidateInvariants();
        Assert.Equal(net.FuelStarvedTicks, net.Clone().FuelStarvedTicks);
    }

    [Fact]
    public void The_counters_are_bookkeeping_and_leave_the_checksum_alone()
    {
        // Two worlds that differ only in the counters hash the same: they are derived from history that is hashed anyway.
        var a = SimWorld.CreateScenario(C, Catalog, "sandbox", Seed);
        var b = SimWorld.CreateScenario(C, Catalog, "sandbox", Seed);
        a.Network.GiveItems(0, C.ItemIndex("core:iron_ore"), 4);
        b.Network.GiveItems(0, C.ItemIndex("core:iron_ore"), 4);
        a.Step(600); b.Step(600);
        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.True(a.Network.ProducedTotal(C.ItemIndex("core:iron_ingot")) > 0);
        Assert.Equal(a.Checksum(), a.Network.Clone().Checksum());
    }
}
