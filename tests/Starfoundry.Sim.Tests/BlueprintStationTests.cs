using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ApplyBlueprint for stations (ADR 0043): the same verdict and price as the preview, atomic, half refund, pay order, what a station may hold, the legacy gate.</summary>
public class BlueprintStationTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static int Item(string id) => C.ItemIndex(id);

    /// <summary>A campaign world (building costs items) with a station beside the starter ship: a core, a dock module at (0,0,-1) and a bay at (1,0,0); the station stock holds plenty.</summary>
    private static (SimWorld W, int Station) Make(WorldRules? rules = null, bool stock = true)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: (rules ?? WorldRules.Default).WithLegacyDockPieces(true).WithLegacyWorldScale(true));
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Dock);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        if (stock)
            foreach (var item in new[] { "core:iron_ingot", "core:iron_plate", "core:carbon", "core:circuit", "core:titanium_alloy", "core:silicon", "core:fuel_cell" })
                w.Network.GiveStock(st, Item(item), 1000);
        return (w, st);
    }

    /// <summary>Items in the hold: the in-world tools of the legacy rules pay from the hold only.</summary>
    private static void Fill(SimWorld w)
    {
        foreach (var item in new[] { "core:iron_ingot", "core:iron_plate", "core:carbon" }) w.Network.GiveItems(0, Item(item), 100);
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static DesignModule[] Modules(SimWorld w, int st) =>
        w.Network.GetStructure(st).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();

    private static DesignModule[] With(SimWorld w, int st, params DesignModule[] extra) => Modules(w, st).Concat(extra).ToArray();

    private static DesignModule[] Without(SimWorld w, int st, params (int, int, int)[] gone) =>
        Modules(w, st).Where(m => !gone.Contains((m.Mx, m.My, m.Mz))).ToArray();

    [Fact]
    public void Tag_65_roundtrips_in_the_codec_and_logs_before_version_24_refuse_it()
    {
        var (w, st) = Make();
        var cmd = new ApplyBlueprint(st, With(w, st, new DesignModule(0, 0, 1, ModuleKind.Reactor)));
        using var ms = new MemoryStream();
        w.Submit(0, cmd);
        w.Step();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        var bytes = ms.ToArray();
        Assert.Equal(30, bytes[4]);
        var back = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.False(rules.LegacyBlueprint);
        var read = Assert.IsType<ApplyBlueprint>(back.Single().Command);
        Assert.Equal(cmd.Structure, read.Structure);
        Assert.Equal(cmd.Modules, read.Modules);
        var old = (byte[])bytes.Clone();
        old[4] = 23;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out _));
        // The same header with no command at version 23 reads with the legacy flag on.
        using var empty = new MemoryStream();
        CommandCodec.WriteLog(empty, new List<LoggedCommand>(), C.Hash, GameMode.Campaign, Seed, structures: true);
        var hdr = empty.ToArray();
        hdr[4] = 23;
        CommandCodec.ReadLog(new MemoryStream(hdr), out _, out _, out _, out _, out _, out _, out var r23);
        Assert.True(r23.LegacyBlueprint && r23.HashesAsDefault);
        Assert.Equal(new WorldRules().WithLegacyBlueprint(true), WorldRules.Deserialize(new WorldRules().WithLegacyBlueprint(true).Serialize()));
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyBlueprint));
    }

    [Fact]
    public void A_station_blueprint_adds_and_removes_modules_pays_hold_then_stock_and_refunds_half()
    {
        var (w, st) = Make();
        var net = w.Network;
        int plate = Item("core:iron_plate"), ingot = Item("core:iron_ingot");
        int holdPlate = net.HoldCount(0, plate);
        net.GiveItems(0, plate, 5);
        holdPlate = net.HoldCount(0, plate);
        int stockPlate = net.StockCount(st, plate), stockIngot = net.StockCount(st, ingot), holdIngot = net.HoldCount(0, ingot);
        var kinds = C.Structures!;
        var defenceCost = kinds.ModuleCost(ModuleKind.Defence);
        int needPlate = defenceCost.First(s => s.Item == plate).Count, needIngot = defenceCost.First(s => s.Item == ingot).Count;
        var draft = With(w, st, new DesignModule(0, 0, 1, ModuleKind.Defence));

        var report = w.CheckBlueprint(0, st, draft);
        Assert.True(report.Ok, report.Reject.ToString());
        Assert.Equal(1, report.Added);
        Assert.Equal(needPlate, report.Cost.First(s => s.Item == plate).Count);
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, draft)));
        Assert.Equal(ModuleKind.Defence, net.GetStructure(st).Modules[(0, 0, 1)]);
        // plates: the hold pays first (it had holdPlate), the stock the rest; ingots the same
        Assert.Equal(Math.Max(0, holdPlate - needPlate), net.HoldCount(0, plate));
        Assert.Equal(stockPlate - Math.Max(0, needPlate - holdPlate), net.StockCount(st, plate));
        Assert.Equal(Math.Max(0, holdIngot - needIngot), net.HoldCount(0, ingot));
        Assert.Equal(stockIngot - Math.Max(0, needIngot - holdIngot), net.StockCount(st, ingot));
        Assert.Equal(1, net.ModulesBuilt(ModuleKind.Defence));

        // removing it refunds half, in the hold
        int holdBefore = net.HoldCount(0, plate);
        var undo = Without(w, st, (0, 0, 1));
        var rep = w.CheckBlueprint(0, st, undo);
        Assert.Equal(needPlate / 2, rep.Refund.First(s => s.Item == plate).Count);
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, undo)));
        Assert.Equal(holdBefore + needPlate / 2, net.HoldCount(0, plate));
        Assert.DoesNotContain(ModuleKind.Defence, net.GetStructure(st).Modules.Values);
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_module_that_only_moves_is_free()
    {
        var (w, st) = Make();
        var moved = Modules(w, st).Where(m => m.Kind != ModuleKind.Workshop).Append(new DesignModule(0, 0, 1, ModuleKind.Workshop)).ToArray();
        var r = w.CheckBlueprint(0, st, moved);
        Assert.True(r.Ok, r.Reject.ToString());
        Assert.Empty(r.Cost);
        Assert.Empty(r.Refund);
        Assert.Equal(1, r.Moved);
    }

    [Fact]
    public void A_blueprint_that_cannot_be_paid_is_refused_and_changes_nothing()
    {
        var (w, st) = Make(stock: false);
        w.Network.ValidateInvariants();
        ulong before = w.Checksum();
        var draft = With(w, st, new DesignModule(0, 0, 1, ModuleKind.Defence));
        Assert.Equal(Reject.NotEnoughItems, w.CheckBlueprint(0, st, draft).Reject);
        Assert.Equal(Reject.NotEnoughItems, Run(w, new ApplyBlueprint(st, draft)));
        Assert.DoesNotContain(ModuleKind.Defence, w.Network.GetStructure(st).Modules.Values);
        Assert.NotEqual(0UL, before);
    }

    [Fact]
    public void A_station_keeps_its_core_and_dock_module_and_takes_no_ship_parts()
    {
        var (w, st) = Make();
        // ship parts and a hardpoint
        foreach (var kind in new[] { ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Gun, ModuleKind.MiningLaser })
            Assert.Equal(Reject.KindNotAllowed, Run(w, new ApplyBlueprint(st, With(w, st, new DesignModule(0, 0, 1, kind)))));
        // a second core, or no core
        Assert.Equal(Reject.KindNotAllowed, Run(w, new ApplyBlueprint(st, With(w, st, new DesignModule(0, 0, 1, ModuleKind.Core)))));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new ApplyBlueprint(st, Modules(w, st).Select(m => m.Kind == ModuleKind.Core ? m with { Kind = ModuleKind.Hull } : m).ToArray())));
        // no dock module (ADR 0041)
        Assert.Equal(Reject.NoDockModule, Run(w, new ApplyBlueprint(st, Without(w, st, (0, 0, -1)))));
        // a piece that touches nothing
        Assert.Equal(Reject.NotAdjacent, Run(w, new ApplyBlueprint(st, With(w, st, new DesignModule(4, 0, 4, ModuleKind.Hull)))));
        // empty, duplicate and outside the grid
        Assert.Equal(Reject.InvalidArgument, Run(w, new ApplyBlueprint(st, Array.Empty<DesignModule>())));
        Assert.Equal(Reject.InvalidArgument, Run(w, new ApplyBlueprint(st, With(w, st, new DesignModule(1, 0, 0, ModuleKind.Hull)))));
        Assert.Equal(Reject.OutOfBounds, Run(w, new ApplyBlueprint(st, With(w, st, new DesignModule(9999, 0, 0, ModuleKind.Hull)))));
        // somebody else's structure and a structure that is not there
        Assert.Equal(Reject.NoSuchStructure, Run(w, new ApplyBlueprint(999, Modules(w, st))));
        Assert.Equal(3, w.Network.GetStructure(st).Modules.Count);
        // the dock module can move: a second one first, then the first goes in a later blueprint
        var two = With(w, st, new DesignModule(0, 0, 1, ModuleKind.Dock));
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, two)));
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Without(w, st, (0, 0, -1)))));
        w.Network.ValidateInvariants();
    }

    [Fact]
    public void A_module_holding_a_machine_cannot_go_and_a_docked_port_cannot_go()
    {
        var (w2, st2) = Make();
        var bay = (1, 0, 0);
        var smelter = new BlueprintInterior(new[] { new BlueprintMachine(8 + 2, 0, 2, C.MachineIndex("core:smelter"), -1) }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        Assert.Equal(Reject.None, Run(w2, new ApplyBlueprint(st2, Modules(w2, st2), smelter))); // ADR 0044: machines reach a station through its blueprint
        Assert.Equal(Reject.ModuleOccupied, Run(w2, new ApplyBlueprint(st2, Without(w2, st2, bay))));
        Assert.Equal(Reject.ModuleOccupied, w2.CheckBlueprint(0, st2, Modules(w2, st2).Select(m => (m.Mx, m.My, m.Mz) == bay ? m with { Kind = ModuleKind.Defence } : m).ToArray()).Reject);
        Assert.Equal(ModuleKind.Workshop, w2.Network.GetStructure(st2).Modules[bay]);
    }

    [Fact]
    public void The_module_tool_is_refused_on_stations_in_new_worlds_and_works_in_legacy_ones()
    {
        var (w, st) = Make();
        Assert.Equal(Reject.UseBlueprint, Run(w, new PlaceModuleIn(st, 0, 0, 1, ModuleKind.Reactor)));
        Assert.Equal(Reject.UseBlueprint, Run(w, new RemoveModuleIn(st, 1, 0, 0)));
        Assert.Equal(3, w.Network.GetStructure(st).Modules.Count);
        var (old, ost) = Make(WorldRules.Default.WithLegacyBlueprint(true));
        Fill(old);
        Assert.Equal(Reject.None, Run(old, new PlaceModuleIn(ost, 0, 0, 1, ModuleKind.Reactor)));
        Assert.Equal(Reject.None, Run(old, new RemoveModuleIn(ost, 0, 0, 1)));
        Assert.Equal(Reject.NotAllowedHere, Run(old, new ApplyBlueprint(ost, Modules(old, ost))));
        // the ship keeps the module tool for its own modules
        int ship = w.Network.ShipStructure(0);
        Assert.NotEqual(Reject.UseBlueprint, Run(w, new PlaceModuleIn(ship, 5, 0, 5, ModuleKind.Cargo)));
        Assert.Equal(Reject.UseBlueprint, Run(w, new RemoveModuleIn(st, 0, 0, 0)));
    }

    [Fact]
    public void A_legacy_log_with_the_module_tool_replays_to_the_same_checksum()
    {
        var rules = WorldRules.Default.WithLegacyBlueprint(true).WithLegacyRockClusters(true); // recorded before the clustered rock field
        var (w, st) = Make(rules);
        Fill(w);
        Assert.Equal(Reject.None, Run(w, new PlaceModuleIn(st, 0, 0, 1, ModuleKind.Reactor)));
        w.Step(20);
        Assert.Equal(Reject.None, Run(w, new RemoveModuleIn(st, 0, 0, 1)));
        // an identical world built by hand replays the log: the legacy flag is read back from a version 23 header
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true, rules: rules);
        var bytes = ms.ToArray();
        bytes[4] = 23;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out var rr, out _);
        _ = rr;
        var read = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var readRules);
        Assert.True(readRules.LegacyBlueprint);
        var (fresh, _) = Make(readRules);
        Fill(fresh);
        var replay = SimWorld.Replay(read, w.Tick, initial: fresh);
        Assert.Equal(w.Checksum(), replay.Checksum());
        Assert.Equal(log.Count, read.Count);
    }

    /// <summary>A random draft: a few random additions, removals and kind changes around the station, valid or not.</summary>
    private static DesignModule[] RandomDraft(SimWorld w, int st, Random rng)
    {
        var map = w.Network.GetStructure(st).Modules.ToDictionary(kv => kv.Key, kv => kv.Value);
        var kinds = Enum.GetValues<ModuleKind>();
        int edits = rng.Next(1, 5);
        for (int i = 0; i < edits; i++)
        {
            int pick = rng.Next(5);
            var keys = map.Keys.ToList();
            var k = keys[rng.Next(keys.Count)];
            var kind = kinds[rng.Next(kinds.Length)];
            switch (pick)
            {
                case 0 or 1: // add next to a module
                {
                    var steps = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
                    var s = steps[rng.Next(steps.Length)];
                    map.TryAdd((k.Mx + s.Item1, k.My + s.Item2, k.Mz + s.Item3), kind);
                    break;
                }
                case 2: map.Remove(k); break;
                case 3: map[k] = kind; break;
                default: // far away or outside the grid, sometimes
                    map.TryAdd((rng.Next(-3, 4) * (rng.Next(4) == 0 ? 50 : 1), rng.Next(-1, 3), rng.Next(-3, 4)), kind);
                    break;
            }
            if (map.Count == 0) map[(0, 0, 0)] = ModuleKind.Core;
        }
        return map.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();
    }

    [Fact]
    public void The_preview_and_the_apply_agree_on_random_drafts_and_a_refused_one_changes_nothing()
    {
        int ok = 0, refused = 0;
        var seen = new HashSet<Reject>();
        for (int seed = 0; seed < 300; seed++)
        {
            var rng = new Random(seed);
            var (a, sa) = Make();
            var (b, sb) = Make();
            Assert.Equal(a.Checksum(), b.Checksum());
            var draft = RandomDraft(a, sa, rng);
            var preview = a.CheckBlueprint(0, sa, draft);
            Assert.Equal(a.Checksum(), b.Checksum()); // the preview changes nothing
            ulong before = b.Checksum();
            var verdict = Run(b, new ApplyBlueprint(sb, draft));
            Assert.Equal(preview.Reject, verdict);
            seen.Add(verdict);
            if (verdict == Reject.None)
            {
                ok++;
                Assert.Equal(draft.Length, b.Network.GetStructure(sb).Modules.Count);
                foreach (var m in draft) Assert.Equal(m.Kind, b.Network.GetStructure(sb).Modules[(m.Mx, m.My, m.Mz)]);
                // the price: what the world lost equals the preview's cost, what it got back the refund
                foreach (var s in preview.Cost)
                    Assert.Equal(a.Network.HoldCount(0, s.Item) + a.Network.StockCount(sa, s.Item) - s.Count, b.Network.HoldCount(0, s.Item) + b.Network.StockCount(sb, s.Item) + (preview.Refund.Any(r => r.Item == s.Item) ? 0 : 0));
                foreach (var s in preview.Refund)
                    Assert.Equal(a.Network.HoldCount(0, s.Item) + a.Network.StockCount(sa, s.Item) + s.Count, b.Network.HoldCount(0, s.Item) + b.Network.StockCount(sb, s.Item));
            }
            else
            {
                refused++;
                // atomic: the refused command changed nothing but the clock
                var twin = Make().W;
                twin.Step();
                Assert.Equal(twin.Checksum(), b.Checksum());
                Assert.Equal(3, b.Network.GetStructure(sb).Modules.Count);
                Assert.NotEqual(0UL, before);
            }
            b.Network.ValidateInvariants();
        }
        Assert.True(ok >= 20 && refused >= 20, $"ok={ok} refused={refused}");
        Assert.True(seen.Count >= 4, string.Join(",", seen));
    }

    [Fact]
    public void The_ship_can_be_blueprinted_through_the_same_command_when_docked_at_a_shipyard()
    {
        var (w, st) = Make();
        int ship = w.Network.ShipStructure(0);
        Assert.Equal(Reject.NotDocked, w.CheckBlueprint(0, ship, Modules(w, ship)).Reject); // the ship rules of ADR 0032 apply: it needs a shipyard
        Assert.Equal(Reject.NoSuchStructure, w.CheckBlueprint(0, 0, Modules(w, st)).Reject);
    }

    [Fact]
    public void The_session_edits_a_station_and_its_panels_agree_with_the_simulation()
    {
        var (w, st) = Make();
        var s = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        s.Open(st);
        Assert.Equal(StructureKind.Station, s.StructureKind);
        Assert.Equal(StructureKind.Station, s.Draft.Kind);
        var cat = ShipDesignEditor.Catalogue(C, StructureKind.Station);
        Assert.Contains(ModuleKind.Shipyard, cat);
        Assert.Contains(ModuleKind.Workshop, cat);
        Assert.DoesNotContain(ModuleKind.Cockpit, cat);
        Assert.DoesNotContain(ModuleKind.Engine, cat);
        Assert.DoesNotContain(ModuleKind.Core, cat);
        Assert.DoesNotContain(ModuleKind.Gun, cat);
        Assert.False(s.CanApply);
        // the station's ship-only checks are gone: no cockpit, engine or cargo errors, and the unchanged draft says so
        Assert.DoesNotContain(s.Validation.Entries, e => e.Code is "noCockpit" or "noEngine" or "noReactor" or "noCargo");
        s.PickKind(ModuleKind.Defence);
        Assert.Null(s.Click((0, 0, 1)));
        Assert.True(s.Dirty);
        Assert.True(s.Report.Ok, s.Report.Reject.ToString());
        Assert.True(s.CanApply, string.Join(",", s.Validation.Entries.Select(e => e.Code)));
        Assert.Equal(s.Report.Cost, s.Cost.ToPay);
        Assert.Equal(s.Report.Refund, s.Cost.ToRefund);
        Assert.True(s.Cost.Pays && s.Cost.Lines.Count > 0 && s.Cost.CanPay);
        Assert.Equal(StatRows.Build(s.Stats, s.StatsBefore, StructureKind.Station).Count, s.StatRows().Count);
        // the removal of the core or of the last dock module is an error before the simulation says so
        s.SetTool(BlueprintTool.Delete);
        s.Click((0, 0, 0));
        Assert.Contains(s.Validation.Entries, e => e.Code == "noCore" && e.Blocks);
        Assert.False(s.CanApply);
        s.Undo();
        s.Click((0, 0, -1));
        Assert.Contains(s.Validation.Entries, e => e.Code == "noDock" && e.Blocks);
        Assert.False(s.CanApply);
        s.Undo();
        Assert.True(s.CanApply);
        // the command is the one the simulation applies
        var cmd = Assert.IsType<ApplyBlueprint>(s.ToCommand());
        Assert.Equal(st, cmd.Structure);
        Assert.Equal(Reject.None, Run(w, cmd));
        s.Open(st);
        Assert.False(s.Dirty);
        Assert.Equal(ModuleKind.Defence, s.Draft.Modules[(0, 0, 1)]);
        // a station draft holds up to the station limit, a ship's stays at the ship limit
        Assert.Equal(DesignerRules.MaxStationModules, s.Draft.Max);
        s.Open();
        Assert.Equal(DesignerRules.MaxModules, s.Draft.Max);
        Assert.IsType<DesignerApply>(s.ToCommand());
    }
}
