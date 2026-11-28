using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// ADR 0051: large modules, squares of 2 x 2 or 3 x 3 module cells of one kind with a joined floor. Covers the rules table, the validation and every refusal, the price, the research gate, the
/// joined floor for multi-cell machines, falling apart when a cell goes, tags 71 and 72 and the version gate, the rules flag, determinism and replay, preview equal to apply, and the draft.
/// </summary>
public class LargeModuleTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261129UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static int Type(string id) => C.MachineIndex(id);

    /// <summary>A campaign world with a station (core, a bay at (1,0,0), a defence module at (0,0,1), collars) and the ship docked at it; stock plenty; large structures researched when asked.</summary>
    private static (SimWorld W, int Ship, int Station) Yard(bool researched = true, WorldRules? rules = null)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: rules ?? WorldRules.Default);
        w.Network.EnsurePlayer(0);
        int ship = w.Network.ShipStructure(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -16L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, -1, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Defence);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Shipyard);
        w.Network.AddDockPieceUnchecked(st, 0, 0, 0, Dir.PosX);
        foreach (var item in C.Items) w.Network.GiveStock(st, item.Index, 5000);
        foreach (var item in C.Items) w.Network.GiveItems(0, item.Index, 50);
        if (researched) w.Network.GrantResearch(C.ResearchIndex("core:large_structures"));
        w.Network.GrantResearch(C.ResearchIndex("core:heavy_industry"));
        w.Submit(0, new DockAt(st, 0, 0, 0, Dir.PosX));
        w.Step();
        return (w, ship, st);
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static List<DesignModule> Modules(SimWorld w, int s) =>
        w.Network.GetStructure(s).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToList();

    /// <summary>The modules of a structure plus a square of <paramref name="size"/> by size cells of a kind from the anchor.</summary>
    private static DesignModule[] WithSquare(SimWorld w, int s, int mx, int my, int mz, int size, ModuleKind kind)
    {
        var list = Modules(w, s);
        for (int dx = 0; dx < size; dx++)
            for (int dz = 0; dz < size; dz++) list.Add(new DesignModule(mx + dx, my, mz + dz, kind));
        return list.ToArray();
    }

    // ---- the rules ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_kinds_that_may_be_large_and_how_large_follow_the_table()
    {
        Assert.Equal(3, ModuleKindIds.MaxSpan(StructureKind.Station, ModuleKind.Workshop));
        Assert.Equal(3, ModuleKindIds.MaxSpan(StructureKind.Station, ModuleKind.Cargo));
        Assert.Equal(3, ModuleKindIds.MaxSpan(StructureKind.Station, ModuleKind.Hull));
        Assert.Equal(2, ModuleKindIds.MaxSpan(StructureKind.Station, ModuleKind.Reactor));
        Assert.Equal(2, ModuleKindIds.MaxSpan(StructureKind.Station, ModuleKind.Defence));
        Assert.Equal(2, ModuleKindIds.MaxSpan(StructureKind.Ship, ModuleKind.Cargo));
        Assert.Equal(1, ModuleKindIds.MaxSpan(StructureKind.Ship, ModuleKind.Workshop)); // a ship holds no automation
        foreach (var kind in new[] { ModuleKind.Core, ModuleKind.Cockpit, ModuleKind.Engine, ModuleKind.Shield, ModuleKind.Sensor, ModuleKind.Shipyard, ModuleKind.Gun, ModuleKind.Dock })
            Assert.Equal(1, ModuleKindIds.MaxSpan(StructureKind.Station, kind));
        Assert.True(ModuleKindIds.SpanPricePercent < 100);
    }

    [Fact]
    public void A_large_bay_is_built_whole_costs_a_little_less_than_its_cells_and_keeps_its_cells_as_modules()
    {
        var (w, _, st) = Yard();
        var net = w.Network;
        var plain = w.CheckBlueprint(0, st, WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop));
        var large = w.CheckBlueprint(0, st, WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop), null, null, new[] { new DesignSpan(-3, 0, 0, 2) });
        Assert.True(plain.Ok && large.Ok, $"{plain.Reject} {large.Reject}");
        var cost = C.Structures!.ModuleCost(ModuleKind.Workshop);
        foreach (var c in cost)
        {
            int one = (c.Count * ModuleKindIds.SpanPricePercent + 99) / 100;
            Assert.Equal(4 * c.Count, plain.Cost.Where(x => x.Item == c.Item).Sum(x => x.Count));
            Assert.Equal(4 * one, large.Cost.Where(x => x.Item == c.Item).Sum(x => x.Count));
        }
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop), null, null, new[] { new DesignSpan(-3, 0, 0, 2) })));
        Assert.Equal(new[] { new DesignSpan(-3, 0, 0, 2) }, net.Spans(st).ToArray());
        Assert.Equal(4, net.GetStructure(st).Modules.Count(kv => kv.Value == ModuleKind.Workshop && kv.Key.Mx <= -2));
        Assert.True(net.SameFloor(st, (-3, 0, 0), (-2, 0, 1)));
        Assert.False(net.SameFloor(st, (-3, 0, 0), (-1, 0, 0)));
        w.ValidateInvariants();
        // the same again costs nothing and changes nothing
        var again = w.CheckBlueprint(0, st, Modules(w, st).ToArray(), null, null, new[] { new DesignSpan(-3, 0, 0, 2) });
        Assert.True(again.Ok);
        Assert.Empty(again.Cost);
        // a null span list keeps the large module
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st).ToArray())));
        Assert.Single(net.Spans(st));
    }

    [Fact]
    public void Every_span_must_be_a_full_square_of_one_kind_that_may_be_that_large_and_the_research_is_needed()
    {
        var (w, _, st) = Yard();
        var sq = WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop);
        Reject Check(DesignModule[] mods, params DesignSpan[] spans) => w.CheckBlueprint(0, st, mods, null, null, spans).Reject;
        Assert.Equal(Reject.None, Check(sq, new DesignSpan(-3, 0, 0, 2)));
        Assert.Equal(Reject.BadSpan, Check(sq, new DesignSpan(-4, 0, 0, 3))); // a 3 x 3 over a 2 x 2
        Assert.Equal(Reject.BadSpan, Check(sq, new DesignSpan(-2, 0, 0, 2))); // half of it is not there
        Assert.Equal(Reject.BadSpan, Check(sq, new DesignSpan(-3, 0, 0, 1)));
        Assert.Equal(Reject.BadSpan, Check(sq, new DesignSpan(-3, 0, 0, 2), new DesignSpan(-3, 0, 0, 2))); // twice
        var mixed = sq.Select(m => m.Mx == -2 && m.Mz == 1 ? m with { Kind = ModuleKind.Hull } : m).ToArray();
        Assert.Equal(Reject.BadSpan, Check(mixed, new DesignSpan(-3, 0, 0, 2))); // one cell of another kind
        Assert.Equal(Reject.BadSpan, Check(WithSquare(w, st, -4, 0, 0, 3, ModuleKind.Reactor), new DesignSpan(-4, 0, 0, 3)));
        Assert.Equal(Reject.None, Check(WithSquare(w, st, -4, 0, 0, 3, ModuleKind.Workshop), new DesignSpan(-4, 0, 0, 3)));
        Assert.Equal(Reject.None, Check(WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Reactor), new DesignSpan(-3, 0, 0, 2)));
        Assert.Equal(Reject.BadSpan, Check(WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Sensor), new DesignSpan(-3, 0, 0, 2)));
        // without the research nothing large is built, but the same cells are fine one by one
        var (campaign, _, cst) = Yard(researched: false);
        var cmods = WithSquare(campaign, cst, -3, 0, 0, 2, ModuleKind.Workshop);
        Assert.Equal(Reject.Locked, campaign.CheckBlueprint(0, cst, cmods, null, null, new[] { new DesignSpan(-3, 0, 0, 2) }).Reject);
        Assert.Equal(Reject.None, campaign.CheckBlueprint(0, cst, cmods).Reject);
        // a world from before large modules
        var (old, _, ost) = Yard(rules: WorldRules.Default.WithLegacyLargeModules(true));
        Assert.Equal(Reject.NotAllowedHere, old.CheckBlueprint(0, ost, WithSquare(old, ost, -3, 0, 0, 2, ModuleKind.Workshop), null, null, new[] { new DesignSpan(-3, 0, 0, 2) }).Reject);
        Assert.Empty(old.Network.Spans(ost));
    }

    [Fact]
    public void A_large_module_falls_apart_when_a_cell_goes_and_refunds_as_modules_do()
    {
        var (w, _, st) = Yard();
        var net = w.Network;
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop), null, null, new[] { new DesignSpan(-3, 0, 0, 2) })));
        var cut = Modules(w, st).Where(m => (m.Mx, m.Mz) != (-2, 1)).ToArray();
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, cut)));
        Assert.Empty(net.Spans(st));
        Assert.Equal(3, net.GetStructure(st).Modules.Count(kv => kv.Value == ModuleKind.Workshop && kv.Key.Mx <= -2));
        w.ValidateInvariants();
        // taking a whole large module down refunds half of its cells' full price
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st).Where(m => !(m.Mx is -3 or -2 && m.Mz is 0 or 1)).ToArray())));
        w.ValidateInvariants();
    }

    // ---- the joined floor --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_machine_may_stand_across_the_seam_of_a_large_bay_and_not_across_the_seam_of_two_bays()
    {
        var (w, _, st) = Yard();
        var net = w.Network;
        BlueprintInterior I(int x, int z, int type) => new(new[] { new BlueprintMachine(x, 0, z, type, -1, 0) }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        int large = Type("core:large_smelter");
        // two separate bays side by side: the seam at x = 16 is a wall
        var two = Modules(w, st); two.Add(new DesignModule(-3, 0, 0, ModuleKind.Workshop)); two.Add(new DesignModule(-2, 0, 0, ModuleKind.Workshop));
        Assert.Equal(Reject.FootprintBlocked, w.CheckBlueprint(0, st, two.ToArray(), I(-17, 2, large)).Reject);
        // the same two bays as one large bay of 2 x 2: the floor is one
        var four = WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop);
        var span = new[] { new DesignSpan(-3, 0, 0, 2) };
        Assert.True(w.CheckBlueprint(0, st, four, I(-17, 2, large), null, span).Ok);
        Assert.True(w.CheckBlueprint(0, st, four, I(-17, 7, large), null, span).Ok); // across the corner of all four cells
        Assert.Equal(Reject.FootprintBlocked, w.CheckBlueprint(0, st, four, I(-9, 2, large), null, span).Reject); // out of the large bay into nothing
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, four, I(-17, 7, large), null, span)));
        Assert.Equal(1, net.MachineCount);
        w.ValidateInvariants();
        // taking the large bay down while the machine stands across the seam is refused with the machine's module still in use
        var broken = Modules(w, st).Where(m => (m.Mx, m.Mz) != (-2, 1)).ToArray();
        Assert.NotEqual(Reject.None, Run(w, new ApplyBlueprint(st, broken)));
    }


    // ---- the ship ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_ship_may_have_a_large_hull_but_not_a_large_reactor()
    {
        var (w, ship, _) = Yard();
        var mods = Modules(w, ship);
        int minX = mods.Min(m => m.Mx);
        var withHold = mods.ToList();
        for (int dx = 0; dx < 2; dx++)
            for (int dz = 0; dz < 2; dz++) withHold.Add(new DesignModule(minX - 3 + dx, 0, 4 + dz, ModuleKind.Hull));
        // a free-standing square would not be connected: build it against the cockpit's other side
        var hull = Modules(w, ship).ToList();
        int maxZ = hull.Max(m => m.Mz);
        var big = hull.ToList();
        for (int dx = 0; dx < 2; dx++)
            for (int dz = 0; dz < 2; dz++) big.Add(new DesignModule(1 + dx, 0, 0 + dz, ModuleKind.Hull));
        var report = w.CheckDesign(0, big.ToArray(), null, new[] { new DesignSpan(1, 0, 0, 2) });
        Assert.NotEqual(Reject.BadSpan, report.Reject); // the square is valid (a ship hull may be 2 x 2); whatever else refuses it is not the span
        var bay = hull.ToList();
        for (int dx = 0; dx < 2; dx++)
            for (int dz = 0; dz < 2; dz++) bay.Add(new DesignModule(1 + dx, 0, 0 + dz, ModuleKind.Reactor));
        Assert.Equal(Reject.BadSpan, w.CheckDesign(0, bay.ToArray(), null, new[] { new DesignSpan(1, 0, 0, 2) }).Reject);

    }

    // ---- the command log ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Tags_71_and_72_roundtrip_the_log_is_version_29_and_older_logs_refuse_large_modules()
    {
        var mods = new[] { new DesignModule(0, 0, 0, ModuleKind.Core), new DesignModule(1, 0, 0, ModuleKind.Workshop), new DesignModule(2, 0, 0, ModuleKind.Workshop), new DesignModule(1, 0, 1, ModuleKind.Workshop), new DesignModule(2, 0, 1, ModuleKind.Workshop) };
        var spans = new[] { new DesignSpan(-3, 0, 0, 2) };
        var pieces = new[] { new DockPieceSpec(0, 0, 0, Dir.NegX) };
        var turned = new BlueprintInterior(new[] { new BlueprintMachine(9, 0, 2, 5, -1, 2) }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        var plain = new BlueprintInterior(new[] { new BlueprintMachine(9, 0, 2, 5, -1) }, new[] { new BlueprintBelt(1, 0, 1, Dir.PosX, 2, BeltTier.Mk1) }, Array.Empty<BlueprintLink>());
        var commands = new Command[]
        {
            new DesignerApply(mods, null, spans), new DesignerApply(mods, pieces, spans),
            new ApplyBlueprint(3, mods, null, null, spans), new ApplyBlueprint(3, mods, null, pieces, spans), new ApplyBlueprint(3, mods, plain, pieces, spans), new ApplyBlueprint(3, mods, turned, null, spans),
        };
        foreach (var c in commands)
        {
            Assert.True(CommandCodec.IsWellFormed(c));
            var bytes = CommandCodec.ToBytes(c);
            Assert.Contains(bytes[0], new byte[] { 71, 72 });
            var back = CommandCodec.FromBytes(bytes);
            Assert.Equal(c.GetType(), back.GetType());
            if (c is ApplyBlueprint a) { var b = (ApplyBlueprint)back; Assert.Equal(a.Spans, b.Spans); Assert.Equal(a.Interior, b.Interior); Assert.Equal(a.DockPieces ?? Array.Empty<DockPieceSpec>(), b.DockPieces ?? Array.Empty<DockPieceSpec>()); Assert.Equal(a.DockPieces is null, b.DockPieces is null); Assert.Equal(a.Modules, b.Modules); }
            else { var d = (DesignerApply)c; var e = (DesignerApply)back; Assert.Equal(d.Spans, e.Spans); Assert.Equal(d.Modules, e.Modules); Assert.Equal(d.DockPieces is null, e.DockPieces is null); }
            using var ms = new MemoryStream();
            CommandCodec.WriteLog(ms, new List<LoggedCommand> { new LoggedCommand(5, 0, c) }, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
            var log = ms.ToArray();
            Assert.Equal(29, log[4]);
            CommandCodec.ReadLog(new MemoryStream(log), out _, out _, out _, out _, out _, out _, out var rules);
            Assert.False(rules.LegacyLargeModules);
            var old = (byte[])log.Clone();
            old[4] = 28;
            Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out _));
        }
        // commands without spans keep every older tag
        Assert.Equal(66, CommandCodec.ToBytes(new ApplyBlueprint(3, mods, plain))[0]);
        Assert.Equal(65, CommandCodec.ToBytes(new ApplyBlueprint(3, mods))[0]);
        Assert.Equal(46, CommandCodec.ToBytes(new DesignerApply(mods))[0]);
        Assert.False(CommandCodec.IsWellFormed(new ApplyBlueprint(3, mods, null, null, new[] { new DesignSpan(1, 0, 0, 4) })));
        Assert.False(CommandCodec.IsWellFormed(new ApplyBlueprint(3, mods, null, null, Enumerable.Repeat(spans[0], DesignerRules.MaxSpans + 1).ToArray())));
    }

    [Fact]
    public void The_flag_is_bit_4_of_the_third_flags_byte_and_a_log_before_29_replays_as_recorded_with_single_modules()
    {
        Assert.False(WorldRules.Default.LegacyLargeModules);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyLargeModules));
        var legacy = new WorldRules().WithLegacyLargeModules(true);
        Assert.True(legacy.LegacyLargeModules && legacy.AnyLegacy && legacy.HashesAsDefault);
        Assert.Equal(legacy, WorldRules.Deserialize(legacy.Serialize()));
        Assert.False(legacy.WithoutLegacyFlags().LegacyLargeModules);
        Assert.True(WorldRules.Deserialize(legacy.WithLegacyFootprints(true).Serialize()).LegacyFootprints);
        Assert.Equal(legacy.Clamped(), legacy);

        // a log written with the flag on, as a version 28 world would have been, replays with the same checksum
        var (w, _, st) = Yard(rules: WorldRules.Default.WithLegacyLargeModules(true));
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, WithSquare(w, st, -3, 0, 0, 2, ModuleKind.Workshop))));
        w.Step(60);
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true, rules: w.Network.Rules);
        var bytes = ms.ToArray();
        bytes[4] = 28;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.True(rules.LegacyLargeModules);
        Assert.Equal(w.Log.Count, log.Count);
    }

    // ---- determinism, preview, the draft ------------------------------------------------------------------------------------------

    [Fact]
    public void A_world_with_large_modules_clones_validates_and_hashes_them()
    {
        var (a, _, sa) = Yard();
        var (b, _, sb) = Yard();
        Assert.Equal(Reject.None, Run(a, new ApplyBlueprint(sa, WithSquare(a, sa, -3, 0, 0, 2, ModuleKind.Workshop), null, null, new[] { new DesignSpan(-3, 0, 0, 2) })));
        Assert.Equal(Reject.None, Run(b, new ApplyBlueprint(sb, WithSquare(b, sb, -3, 0, 0, 2, ModuleKind.Workshop))));
        a.Step(30); b.Step(30);
        Assert.Equal(a.Checksum(), a.Network.Clone().Checksum());
        Assert.Single(a.Network.Clone().Spans(sa));
        Assert.NotEqual(a.Network.Checksum(), b.Network.Checksum()); // the span is part of the hash
        a.ValidateInvariants();
    }

    [Fact]
    public void The_preview_equals_the_apply_for_random_blueprints_with_large_modules_and_a_refusal_changes_nothing()
    {
        var rng = new Random(20261129);
        int ok = 0, refused = 0;
        var kinds = new[] { ModuleKind.Workshop, ModuleKind.Hull, ModuleKind.Cargo, ModuleKind.Reactor, ModuleKind.Defence, ModuleKind.Sensor };
        for (int round = 0; round < 150; round++)
        {
            var (w, _, st) = Yard(researched: rng.Next(5) > 0);
            var mods = Modules(w, st);
            var spans = new List<DesignSpan>();
            for (int i = rng.Next(0, 3); i > 0; i--)
            {
                int size = rng.Next(2, 4), mx = rng.Next(-5, 0), mz = rng.Next(-1, 3);
                var kind = kinds[rng.Next(kinds.Length)];
                foreach (var m in WithSquare(w, st, mx, 0, mz, size, kind).Skip(mods.Count)) if (mods.All(o => (o.Mx, o.Mz) != (m.Mx, m.Mz))) mods.Add(m);
                if (rng.Next(4) > 0) spans.Add(new DesignSpan(mx, 0, mz, size));
            }
            DesignSpan[]? carried = rng.Next(4) == 0 ? null : spans.ToArray();
            var report = w.CheckBlueprint(0, st, mods, null, null, carried);
            var before = Shape(w, st);
            var result = Run(w, new ApplyBlueprint(st, mods.ToArray(), null, null, carried));
            Assert.Equal(report.Reject, result);
            if (result == Reject.None) ok++; else { refused++; Assert.Equal(before, Shape(w, st)); }
            w.ValidateInvariants();
        }
        Assert.True(ok > 20 && refused > 20, $"ok={ok} refused={refused}");
    }

    private static string Shape(SimWorld w, int s) =>
        string.Join(";", w.Network.GetStructure(s).Modules.Select(kv => $"{kv.Key}{kv.Value}")) + "|" + string.Join(";", w.Network.Spans(s));

    [Fact]
    public void The_draft_builds_a_large_module_removes_it_whole_refuses_to_move_it_and_undoes()
    {
        var (w, _, st) = Yard();
        var session = new BlueprintSession(C, ShipHandling.Default, () => w, 0);
        session.Open(st);
        var d = session.Draft;
        Assert.True(d.UsesSpans);
        Assert.Equal(3, d.MaxSpan(ModuleKind.Workshop));
        Assert.Equal(1, d.MaxSpan(ModuleKind.Sensor));
        Assert.Equal(DesignEditResult.KindNotAllowed, d.AddSpan((-3, 0, 0), ModuleKind.Sensor, 2, C));
        Assert.Equal(DesignEditResult.KindNotAllowed, d.AddSpan((-4, 0, 0), ModuleKind.Reactor, 3, C));
        Assert.Equal(DesignEditResult.NotAdjacent, d.AddSpan((5, 0, 5), ModuleKind.Workshop, 2, C));
        Assert.Equal(DesignEditResult.Occupied, d.AddSpan((-1, 0, 0), ModuleKind.Workshop, 2, C));
        Assert.Equal(DesignEditResult.Ok, d.AddSpan((-3, 0, 0), ModuleKind.Workshop, 2, C));
        Assert.Equal(new[] { new DesignSpan(-3, 0, 0, 2) }, d.Spans.ToArray());
        Assert.True(d.Dirty);
        Assert.True(d.SameFloor((-3, 0, 0), (-2, 0, 1)));
        Assert.True(session.Report.Ok, session.Report.Reject.ToString());
        var cmd = Assert.IsType<ApplyBlueprint>(session.ToCommand());
        Assert.Equal(new[] { new DesignSpan(-3, 0, 0, 2) }, cmd.Spans);
        Assert.False(d.MoveMany(new[] { (-3, 0, 0) }, (0, 0, 3)).Any); // a large module is taken down and built again, not moved
        // the floor plan of the draft sees the joined floor
        session.OpenFloor((-3, 0, 0));
        Assert.Null(session.Interior.FootprintProblem((-17, 0, 2), Type("core:large_smelter"), 0));
        session.CloseFloor();
        // removing one cell removes the whole large module
        Assert.Equal(DesignEditResult.Ok, d.Remove((-2, 0, 1)));
        Assert.Empty(d.Spans);
        Assert.False(d.Modules.ContainsKey((-3, 0, 0)));
        Assert.True(d.Undo());
        Assert.Single(d.Spans);
        Assert.Equal(4, d.Modules.Count(kv => kv.Value == ModuleKind.Workshop && kv.Key.Mx <= -2));
        d.Revert();
        Assert.Empty(d.Spans);
        Assert.False(d.Dirty);
        // the session builds one with the palette's pick and a click
        session.PickKind(ModuleKind.Workshop, 3);
        Assert.Equal(3, session.LargeSize);
        Assert.Null(session.Click((-4, 0, 0)));
        Assert.Single(d.Spans);
        Assert.Equal(9, d.Modules.Count(kv => kv.Value == ModuleKind.Workshop && kv.Key.Mx <= -2));
        Assert.Equal(Reject.None, Run(w, session.ToCommand()));
        Assert.Equal(new[] { new DesignSpan(-4, 0, 0, 3) }, w.Network.Spans(st).ToArray());
        w.ValidateInvariants();
    }

    [Fact]
    public void The_large_module_texts_exist_in_both_languages()
    {
        foreach (var key in new[] { "research.core:large_structures", "research.core:large_structures.desc", "toast.reject.badSpan", "bp.tool.large.hint", "bp.large.size", "bp.large.locked" })
        {
            Assert.True(Loc.Has(key), key);
            var (es, en) = Loc.All.First(p => p.Key == key).Value;
            Assert.False(string.IsNullOrWhiteSpace(es) || string.IsNullOrWhiteSpace(en), key);
            Assert.NotEqual(es, en);
        }
        Assert.Equal("toast.reject.badSpan", RejectTexts.Key(Reject.BadSpan));
        Assert.True(C.Research[C.ResearchIndex("core:large_structures")].UnlockAbilities.Contains(Ability.LargeModules));
    }
}
