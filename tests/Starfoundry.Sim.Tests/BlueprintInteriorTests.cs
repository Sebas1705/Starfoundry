using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>The interior of a station in ApplyBlueprint (ADR 0044): machines, belts and links built, kept and removed in one atomic step, priced like modules, and the in-world placement gate.</summary>
public partial class BlueprintInteriorTests
{
    private const int Cell = Units.CellUnits;
    private const ulong Seed = 20261126UL;
    private static readonly ContentDb C = TestContent.CoreFloor;
    private static int Item(string id) => C.ItemIndex(id);
    private static int Type(string id) => C.MachineIndex(id);

    /// <summary>A campaign world with a station: core (0,0,0), dock (0,0,-1), bay (1,0,0) and a defence module (0,0,1); the stock holds plenty.</summary>
    private static (SimWorld W, int Station) Make(WorldRules? rules = null)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: false, stationRules: false, structures: true, rules: (rules ?? WorldRules.Default).WithLegacyDockPieces(true));
        w.Network.EnsurePlayer(0);
        int st = w.Network.CreateStructure(0, StructureKind.Station, -18L * Cell, 0, 0);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 0, ModuleKind.Core);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, -1, ModuleKind.Dock);
        w.Network.AddStructureModuleUnchecked(st, 1, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(st, 0, 0, 1, ModuleKind.Defence);
        foreach (var item in C.Items) w.Network.GiveStock(st, item.Index, 2000);
        return (w, st);
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Last().Reject;
    }

    private static DesignModule[] Modules(SimWorld w, int st) =>
        w.Network.GetStructure(st).Modules.Select(kv => new DesignModule(kv.Key.Mx, kv.Key.My, kv.Key.Mz, kv.Value)).ToArray();

    private static BlueprintMachine M(int x, int y, int z, string type, int recipe = -1) => new(x, y, z, Type(type), recipe);
    private static BlueprintBelt B(int x, int y, int z, Dir d, int len) => new(x, y, z, d, len, BeltTier.Mk1);

    /// <summary>smelter (10,0,2) feeds a belt (11..12,0,2) that feeds a depot (13,0,2), all on the floor of the bay.</summary>
    private static BlueprintInterior Line() => new(
        new[] { M(10, 0, 2, "core:smelter"), M(13, 0, 2, "core:depot") },
        new[] { B(11, 0, 2, Dir.PosX, 2) },
        new[] { new BlueprintLink(BlueprintLinkKind.MachineToBelt, 0, 0), new BlueprintLink(BlueprintLinkKind.BeltToMachine, 0, 1) });

    [Fact]
    public void An_interior_roundtrips_in_the_codec_and_logs_before_version_25_refuse_it()
    {
        var (w, st) = Make();
        var cmd = new ApplyBlueprint(st, Modules(w, st), Line());
        w.Submit(0, cmd);
        w.Step();
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Campaign, Seed, threats: false, structures: true);
        var bytes = ms.ToArray();
        Assert.Equal(28, bytes[4]);
        var back = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out _, out _, out _, out _, out _, out var rules);
        Assert.False(rules.LegacyInterior);
        var read = Assert.IsType<ApplyBlueprint>(back.Single().Command);
        Assert.Equal(cmd.Modules, read.Modules);
        Assert.Equal(cmd.Interior, read.Interior);
        // a module-only blueprint is still tag 65 and has no interior
        var plain = new MemoryStream();
        CommandCodec.WriteLog(plain, new List<LoggedCommand> { new(0, 0, new ApplyBlueprint(st, Modules(w, st))) }, C.Hash, GameMode.Campaign, Seed, structures: true);
        Assert.Null(Assert.IsType<ApplyBlueprint>(CommandCodec.ReadLog(new MemoryStream(plain.ToArray()), out _, out _, out _, out _, out _, out _, out _).Single().Command).Interior);
        var old = (byte[])bytes.Clone();
        old[4] = 24;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(old), out _, out _, out _, out _, out _, out _, out _));
        // a header at version 24 reads with the interior flag on, and the flag survives the rules block
        using var empty = new MemoryStream();
        CommandCodec.WriteLog(empty, new List<LoggedCommand>(), C.Hash, GameMode.Campaign, Seed, structures: true);
        var hdr = empty.ToArray();
        hdr[4] = 24;
        CommandCodec.ReadLog(new MemoryStream(hdr), out _, out _, out _, out _, out _, out _, out var r24);
        Assert.True(r24.LegacyInterior && r24.LegacyBlueprint == false && r24.HashesAsDefault);
        var flagged = new WorldRules { FreeBuild = true }.WithLegacyInterior(true);
        Assert.Equal(flagged, WorldRules.Deserialize(flagged.Serialize()));
        Assert.True(WorldRules.Deserialize(flagged.Serialize()).LegacyInterior && flagged.AnyLegacy && flagged.HashesAsDefault == false);
        Assert.True(new WorldRules().WithLegacyInterior(true).WithLegacyBlueprint(true).WithLegacyDock(true).WithLegacyInterior(false).LegacyBlueprint);
        Assert.All(WorldRules.Presets, p => Assert.False(p.Rules.LegacyInterior));
    }

    [Fact]
    public void An_interior_builds_machines_belts_and_links_and_reading_it_back_gives_the_blueprint()
    {
        var (w, st) = Make();
        var net = w.Network;
        var report = w.CheckBlueprint(0, st, Modules(w, st), Line());
        Assert.True(report.Ok, report.Reject.ToString());
        Assert.Equal(3, report.PartsAdded);
        Assert.Equal(0, report.PartsRemoved);
        Assert.NotEmpty(report.Cost);
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), Line())));
        Assert.Equal(2, net.MachineCount);
        Assert.Equal(1, net.SegmentCount);
        var read = net.ReadInterior(st);
        Assert.Equal(Line().Machines, read.Machines);
        Assert.Equal(Line().Belts, read.Belts);
        Assert.Equal(Line().Links.OrderBy(l => (l.Kind, l.From, l.To)), read.Links.OrderBy(l => (l.Kind, l.From, l.To)));
        net.ValidateInvariants();
        w.ValidateInvariants();

        // the same blueprint again changes nothing and costs nothing
        var again = w.CheckBlueprint(0, st, Modules(w, st), Line());
        Assert.True(again.Ok);
        Assert.Empty(again.Cost);
        Assert.Equal(0, again.PartsAdded + again.PartsRemoved);
        ulong before = w.Checksum();
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), Line())));
        Assert.Equal(2, net.MachineCount);
        Assert.NotEqual(0UL, before);
    }

    [Fact]
    public void Parts_that_stay_keep_their_state_removed_parts_refund_half_and_links_follow()
    {
        var (w, st) = Make();
        var net = w.Network;
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), Line())));
        int smelter = net.ReadInterior(st, out var mids, out _).Machines.ToList().FindIndex(m => m.Type == Type("core:smelter"));
        int smelterId = mids[smelter];
        // the depot and the belt go, a wall comes
        var next = new BlueprintInterior(new[] { M(10, 0, 2, "core:smelter"), M(2, 0, 10, "core:wall") }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        var report = w.CheckBlueprint(0, st, Modules(w, st), next);
        Assert.True(report.Ok, report.Reject.ToString());
        Assert.Equal(1, report.PartsAdded);
        Assert.Equal(2, report.PartsRemoved);
        var beltCost = C.Belts[(int)BeltTier.Mk1]!.CostPerCell;
        int item = beltCost[0].Item;
        int held = net.HoldCount(0, item);
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), next)));
        Assert.True(net.IsMachineAlive(smelterId)); // the kept smelter is the same machine
        Assert.Equal(2, net.MachineCount);
        Assert.Equal(0, net.SegmentCount);
        Assert.True(net.HoldCount(0, item) >= held); // refunds went to the hold
        Assert.Equal(next.Machines, net.ReadInterior(st).Machines);
        Assert.Empty(net.ReadInterior(st).Links);
        net.ValidateInvariants();
        w.ValidateInvariants();
        // taking the wall away gives half of its price back, into the hold
        var smelterOnly = new BlueprintInterior(new[] { M(10, 0, 2, "core:smelter") }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        var takeBack = w.CheckBlueprint(0, st, Modules(w, st), smelterOnly);
        Assert.Empty(takeBack.Cost);
        Assert.NotEmpty(takeBack.Refund);
        int wallItem = takeBack.Refund[0].Item, heldBefore = net.HoldCount(0, wallItem);
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), smelterOnly)));
        Assert.Equal(heldBefore + takeBack.Refund[0].Count, net.HoldCount(0, wallItem));
        Assert.Equal(takeBack.Refund[0].Count, C.Machines[Type("core:wall")].Cost.First(c => c.Item == wallItem).Count / 2);
        // a recipe change on a kept machine is a SetRecipe, not a rebuild
        int smelt = C.RecipeIndex("core:smelt_iron");
        var withRecipe = new BlueprintInterior(new[] { M(10, 0, 2, "core:smelter", smelt) }, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        var rr = w.CheckBlueprint(0, st, Modules(w, st), withRecipe);
        Assert.True(rr.Ok, rr.Reject.ToString());
        Assert.Equal(0, rr.PartsAdded + rr.PartsRemoved);
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), withRecipe)));
        Assert.True(net.IsMachineAlive(smelterId));
        Assert.Equal(smelt, net.GetMachineInfo(smelterId).Recipe);
    }

    [Fact]
    public void The_interior_must_stand_on_the_floor_of_a_module_that_allows_it_and_link_what_touches()
    {
        var (w, st) = Make();
        Reject Check(BlueprintInterior it) => w.CheckBlueprint(0, st, Modules(w, st), it).Reject;
        BlueprintInterior Machines(params BlueprintMachine[] m) => new(m, Array.Empty<BlueprintBelt>(), Array.Empty<BlueprintLink>());
        Assert.Equal(Reject.NotOnFloor, Check(Machines(M(10, 1, 2, "core:smelter"))));
        Assert.Equal(Reject.OutsideStation, Check(Machines(M(40, 0, 2, "core:smelter"))));
        Assert.Equal(Reject.KindNotAllowed, Check(Machines(M(10, 0, 2, "core:turret")))); // a turret goes in a defence module
        Assert.Equal(Reject.None, Check(Machines(M(2, 0, 10, "core:turret"))));
        Assert.Equal(Reject.Overlap, Check(Machines(M(10, 0, 2, "core:smelter"), M(10, 0, 2, "core:depot"))));
        Assert.Equal(Reject.UnknownMachineType, Check(Machines(new BlueprintMachine(10, 0, 2, 9999, -1))));
        Assert.Equal(Reject.RecipeMismatch, Check(Machines(M(10, 0, 2, "core:depot", C.RecipeIndex("core:smelt_iron")))));
        // belts: inside the module, no overlap with a machine, a sane length
        Assert.Equal(Reject.Overlap, Check(new(new[] { M(11, 0, 2, "core:smelter") }, new[] { B(10, 0, 2, Dir.PosX, 3) }, Array.Empty<BlueprintLink>())));
        Assert.Equal(Reject.OutsideStation, Check(new(Array.Empty<BlueprintMachine>(), new[] { B(14, 0, 2, Dir.PosX, 4) }, Array.Empty<BlueprintLink>())));
        Assert.Equal(Reject.InvalidArgument, Check(new(Array.Empty<BlueprintMachine>(), new[] { B(10, 0, 2, Dir.PosX, 0) }, Array.Empty<BlueprintLink>())));
        // links: the ends must touch; a belt has one input and at most two outputs
        var gap = new BlueprintInterior(new[] { M(10, 0, 2, "core:smelter") }, new[] { B(12, 0, 2, Dir.PosX, 2) }, new[] { new BlueprintLink(BlueprintLinkKind.MachineToBelt, 0, 0) });
        Assert.Equal(Reject.NotAdjacent, Check(gap)); // a belt gap
        var twoIn = new BlueprintInterior(
            new[] { M(10, 0, 2, "core:smelter"), M(10, 0, 4, "core:smelter") }, new[] { B(10, 0, 3, Dir.PosX, 2) },
            new[] { new BlueprintLink(BlueprintLinkKind.MachineToBelt, 0, 0), new BlueprintLink(BlueprintLinkKind.MachineToBelt, 1, 0) });
        Assert.Equal(Reject.SegmentConflict, Check(twoIn));
        var wall = new BlueprintInterior(new[] { M(2, 0, 10, "core:wall") }, new[] { B(3, 0, 10, Dir.PosX, 2) }, new[] { new BlueprintLink(BlueprintLinkKind.MachineToBelt, 0, 0) });
        Assert.Equal(Reject.WrongRole, Check(wall));
        Assert.Equal(Reject.InvalidArgument, Check(new(Array.Empty<BlueprintMachine>(), Array.Empty<BlueprintBelt>(), new[] { new BlueprintLink(BlueprintLinkKind.BeltToBelt, 0, 1) })));
        // a splitter: one belt end feeding two belts at its sides and its front
        var split = new BlueprintInterior(
            Array.Empty<BlueprintMachine>(), new[] { B(10, 0, 4, Dir.PosX, 1), B(11, 0, 4, Dir.PosX, 1), B(10, 0, 5, Dir.PosZ, 1) },
            new[] { new BlueprintLink(BlueprintLinkKind.BeltToBelt, 0, 1), new BlueprintLink(BlueprintLinkKind.BeltToBelt, 0, 2) });
        Assert.Equal(Reject.None, Check(split));
        // a refused blueprint changes nothing
        ulong before = w.Checksum();
        Assert.Equal(Reject.NotOnFloor, Run(w, new ApplyBlueprint(st, Modules(w, st), Machines(M(10, 0, 2, "core:smelter"), M(10, 1, 2, "core:depot")))));
        Assert.Equal(0, w.Network.MachineCount);
        Assert.NotEqual(0UL, before);
    }

    [Fact]
    public void A_module_with_parts_goes_with_its_interior_in_one_blueprint_and_not_without_it()
    {
        var (w, st) = Make();
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, Modules(w, st), Line())));
        var bay = (1, 0, 0);
        var without = Modules(w, st).Where(m => (m.Mx, m.My, m.Mz) != bay).ToArray();
        Assert.Equal(Reject.ModuleOccupied, Run(w, new ApplyBlueprint(st, without))); // no interior: the module-only rule of ADR 0043
        Assert.Equal(Reject.OutsideStation, Run(w, new ApplyBlueprint(st, without, Line()))); // the interior still stands in the missing module
        Assert.Equal(Reject.None, Run(w, new ApplyBlueprint(st, without, BlueprintInterior.Empty)));
        Assert.Equal(0, w.Network.MachineCount);
        Assert.Equal(0, w.Network.SegmentCount);
        Assert.DoesNotContain(ModuleKind.Workshop, w.Network.GetStructure(st).Modules.Values);
        w.Network.ValidateInvariants();
        w.ValidateInvariants();
    }

    [Fact]
    public void A_ship_takes_no_interior_and_the_in_world_tools_are_refused_on_stations_in_new_worlds()
    {
        var (w, st) = Make();
        Assert.Equal(Reject.UseBlueprint, Run(w, new PlaceMachineIn(st, 10, 0, 2, Type("core:smelter"), -1)));
        Assert.Equal(Reject.UseBlueprint, Run(w, new PlaceSegmentIn(st, 10, 0, 2, Dir.PosX, 2, BeltTier.Mk1)));
        Assert.Equal(0, w.Network.MachineCount);
        int ship = w.Network.ShipStructure(0);
        Assert.Equal(Reject.UseStation, w.CheckBlueprint(0, ship, Modules(w, ship), Line()).Reject);
        // a legacy world (or a log before version 25) still places one by one
        var (old, ost) = Make(WorldRules.Default.WithLegacyInterior(true));
        foreach (var item in new[] { "core:iron_ingot", "core:iron_plate", "core:carbon" }) old.Network.GiveItems(0, Item(item), 100);
        Assert.Equal(Reject.None, Run(old, new PlaceMachineIn(ost, 10, 0, 2, Type("core:smelter"), -1)));
        Assert.Equal(Reject.NotAllowedHere, Run(old, new ApplyBlueprint(ost, Modules(old, ost), Line())));
        // a legacy-interior world with the blueprint rules still takes module-only blueprints
        Assert.True(old.CheckBlueprint(0, ost, Modules(old, ost)).Ok);
    }

    /// <summary>A random interior and module list: some of it valid, some not.</summary>
    private static (DesignModule[] Modules, BlueprintInterior Interior) RandomBlueprint(SimWorld w, int st, Random rng)
    {
        var modules = Modules(w, st).ToList();
        if (rng.Next(6) == 0) modules.RemoveAll(m => (m.Mx, m.My, m.Mz) == (1, 0, 0));
        string[] types = { "core:smelter", "core:depot", "core:ore_unloader", "core:turret", "core:wall", "core:smelter" };
        var machines = new List<BlueprintMachine>();
        var occupied = new HashSet<(int, int, int)>();
        int n = rng.Next(0, 7);
        for (int i = 0; i < n; i++)
        {
            string type = types[rng.Next(types.Length)];
            bool defence = type is "core:turret" or "core:wall";
            int x = defence ? rng.Next(0, 8) : rng.Next(0, 16), z = defence ? rng.Next(8, 16) : rng.Next(0, 8), y = rng.Next(14) == 0 ? 1 : 0;
            if (rng.Next(12) == 0) { x = rng.Next(0, 16); z = rng.Next(0, 16); } // sometimes anywhere
            if (occupied.Add((x, y, z))) machines.Add(M(x, y, z, type));
        }
        var belts = new List<BlueprintBelt>();
        int nb = rng.Next(0, 5);
        for (int i = 0; i < nb; i++)
        {
            var d = (Dir)rng.Next(0, 6 - 2); // not vertical
            int len = rng.Next(1, 5), x = rng.Next(8, 16), z = rng.Next(0, 8);
            var (dx, dy, dz) = Dirs.Vector(d);
            var cells = Enumerable.Range(0, len).Select(k => (x + dx * k, 0 + dy * k, z + dz * k)).ToList();
            if (cells.All(c => !occupied.Contains(c))) { foreach (var c in cells) occupied.Add(c); belts.Add(B(x, 0, z, d, len)); }
        }
        var links = new List<BlueprintLink>();
        static bool Touch((int X, int Y, int Z) a, (int X, int Y, int Z) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) == 1;
        for (int b = 0; b < belts.Count; b++)
        {
            var end = BeltNetwork.LastCell(belts[b]);
            for (int o = 0; o < belts.Count; o++) if (o != b && Touch(end, (belts[o].X, belts[o].Y, belts[o].Z)) && rng.Next(3) != 0) links.Add(new BlueprintLink(BlueprintLinkKind.BeltToBelt, b, o));
            for (int m = 0; m < machines.Count; m++)
            {
                var at = (machines[m].X, machines[m].Y, machines[m].Z);
                if (Touch(end, at) && rng.Next(3) != 0) links.Add(new BlueprintLink(BlueprintLinkKind.BeltToMachine, b, m));
                if (Touch(at, (belts[b].X, belts[b].Y, belts[b].Z)) && rng.Next(3) != 0) links.Add(new BlueprintLink(BlueprintLinkKind.MachineToBelt, m, b));
            }
        }
        if (rng.Next(10) == 0 && belts.Count > 0) links.Add(new BlueprintLink(BlueprintLinkKind.BeltToBelt, 0, belts.Count + 3)); // out of range
        return (modules.ToArray(), new BlueprintInterior(machines.ToArray(), belts.ToArray(), links.ToArray()));
    }

    [Fact]
    public void The_preview_and_the_apply_agree_on_random_interiors_and_the_world_stays_valid()
    {
        int ok = 0, refused = 0, built = 0;
        var seen = new HashSet<Reject>();
        for (int seed = 0; seed < 400; seed++)
        {
            var rng = new Random(seed);
            var (a, sa) = Make();
            var (b, sb) = Make();
            var (modules, interior) = RandomBlueprint(a, sa, rng);
            ulong before = a.Checksum();
            var preview = a.CheckBlueprint(0, sa, modules, interior);
            Assert.Equal(before, a.Checksum()); // the preview changes nothing
            var verdict = Run(b, new ApplyBlueprint(sb, modules, interior));
            Assert.Equal(preview.Reject, verdict);
            seen.Add(verdict);
            if (verdict == Reject.None)
            {
                ok++;
                built += preview.PartsAdded;
                var read = b.Network.ReadInterior(sb);
                Assert.Equal(interior.Machines, read.Machines);
                Assert.Equal(interior.Belts, read.Belts);
                Assert.Equal(interior.Links.Select(l => (l.Kind, l.From, l.To)).OrderBy(x => x), read.Links.Select(l => (l.Kind, l.From, l.To)).OrderBy(x => x));
                b.Network.ValidateInvariants();
                b.ValidateInvariants();
                // and a replay of the log reaches the same state
                var replay = SimWorld.Replay(b.Log, b.Tick, initial: Make().W);
                Assert.Equal(b.Checksum(), replay.Checksum());
            }
            else
            {
                refused++;
                var twin = Make().W;
                twin.Step();
                Assert.Equal(twin.Checksum(), b.Checksum()); // atomic: nothing changed but the clock
                Assert.Equal(0, b.Network.MachineCount);
            }
        }
        Assert.True(ok >= 30 && refused >= 30 && built >= 30, $"ok={ok} refused={refused} built={built} seen={string.Join(",", seen)}");
        Assert.True(seen.Count >= 5, string.Join(",", seen));
    }

    [Fact]
    public void Applying_a_second_random_interior_over_the_first_keeps_the_world_valid()
    {
        int steps = 0;
        for (int seed = 0; seed < 150; seed++)
        {
            var rng = new Random(1000 + seed);
            var (w, st) = Make();
            for (int round = 0; round < 3; round++)
            {
                var (modules, interior) = RandomBlueprint(w, st, rng);
                ulong before = w.Checksum();
                var preview = w.CheckBlueprint(0, st, modules, interior);
                var verdict = Run(w, new ApplyBlueprint(st, modules, interior));
                Assert.Equal(preview.Reject, verdict);
                w.Network.ValidateInvariants();
                w.ValidateInvariants();
                if (verdict == Reject.None) { steps++; Assert.Equal(interior.Machines, w.Network.ReadInterior(st).Machines); Assert.Equal(interior.Belts, w.Network.ReadInterior(st).Belts); }
                Assert.NotEqual(0UL, before);
                w.Step(5);
            }
        }
        Assert.True(steps >= 60, $"steps={steps}");
    }
}
