using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class MachineTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Extractor = C.MachineIndex("core:extractor");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Assembler = C.MachineIndex("core:assembler");
    private static readonly int MineIron = C.RecipeIndex("core:mine_iron");
    private static readonly int MineCarbon = C.RecipeIndex("core:mine_carbon");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int Plate = C.RecipeIndex("core:iron_plate");
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int PlateItem = C.ItemIndex("core:iron_plate");
    private static readonly int Carbon = C.ItemIndex("core:carbon");

    private static SimWorld World() => new(C);

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int Ok(SimWorld w, Command c)
    {
        var r = Run(w, c);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    private static PlaceSegment Belt(int x, int y, int z, Dir d = Dir.PosX, int len = 4, BeltTier t = BeltTier.Mk2) =>
        new(x, y, z, d, len, t);

    /// <summary>Extractor, belt, smelter, belt, assembler, belt, sink, all in one atomic batch using references.</summary>
    private static Batch ChainBatch() => new(new Command[]
    {
        new PlaceMachine(0, 0, 0, Extractor, MineIron),   // machine -1
        new PlaceMachine(5, 0, 0, Smelter, SmeltIron),    // machine -2
        new PlaceMachine(10, 0, 0, Assembler, Plate),     // machine -3
        Belt(1, 0, 0),                                    // segment -1
        Belt(6, 0, 0),                                    // segment -2
        Belt(11, 0, 0),                                   // segment -3
        new ConnectFromMachine(-1, -1),
        new ConnectToMachine(-1, -2),
        new ConnectFromMachine(-2, -2),
        new ConnectToMachine(-2, -3),
        new ConnectFromMachine(-3, -3),
        new SetSink(-3),
    });

    // ---- production ---------------------------------------------------------------------------

    [Fact]
    public void AnExtractorProducesOneItemPerRecipeTime()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(0, 0, 0, Extractor, MineIron));
        int belt = Ok(w, Belt(1, 0, 0, Dir.PosX, 6, BeltTier.Mk3));
        Ok(w, new ConnectFromMachine(m, belt));
        Ok(w, new SetSink(belt));

        w.Step(300);
        long before = w.Network.Consumed;
        w.Step(600); // 20 seconds, one ore every 30 ticks
        Assert.InRange(w.Network.Consumed - before, 19, 21);
        w.ValidateInvariants();
    }

    [Fact]
    public void AFullChainBuiltInOneBatchRunsAtTheSlowestMachinesRate()
    {
        var w = World();
        var r = Run(w, ChainBatch());
        Assert.True(r.Ok, r.Reject.ToString());
        Assert.Equal(new[] { 0, 1, 2 }, r.CreatedMachines);
        Assert.Equal(new[] { 0, 1, 2 }, r.Created);

        // Extractor 1 ore/s, smelter 0.5 ingots/s (bottleneck), assembler needs 2 ingots: 0.25 plates/s.
        for (int t = 0; t < 30 * 60; t++)
        {
            w.Step();
            if (t % 7 == 0) w.ValidateInvariants();
        }
        long before = w.Network.Consumed;
        w.Step(30 * 120);
        long plates = w.Network.Consumed - before;

        Assert.InRange(plates, 28, 32);
        w.ValidateInvariants();
    }

    [Fact]
    public void AMachineStopsWhenItsOutputBackedUpAndLosesNothing()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(0, 0, 0, Extractor, MineIron));
        int belt = Ok(w, Belt(1, 0, 0, Dir.PosX, 3, BeltTier.Mk2)); // dead end, no sink
        Ok(w, new ConnectFromMachine(m, belt));

        w.Step(30 * 60);

        Assert.Equal(13, w.Network.CountOn(belt));                      // 3 cells at 4 per cell, plus the item on the end line
        Assert.Equal(2, w.Network.MachineOutCount(m, Ore));              // two crafts' worth of room
        Assert.False(w.Network.GetMachineInfo(m).Crafting);
        Assert.Equal(15, w.Network.MachineOutputsCreated);
        Assert.Equal(0, w.Network.Consumed);
        w.ValidateInvariants();
    }

    [Fact]
    public void AnItemTheMachineCannotUseJamsTheBeltInsteadOfDisappearing()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(5, 0, 0, Smelter, SmeltIron));
        int belt = Ok(w, Belt(1, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        Ok(w, new ConnectToMachine(belt, m));
        Ok(w, new SetSource(belt, new ushort[] { (ushort)Ingot })); // the smelter wants ore, not ingots

        w.Step(600);

        Assert.Equal(17, w.Network.CountOn(belt));
        Assert.Equal(0, w.Network.GetMachineInfo(m).InputItems);
        Assert.Equal(0, w.Network.MachineInputsConsumed);
        w.ValidateInvariants();
    }

    [Fact]
    public void AMachineWithoutARecipeRefusesItemsAndKeepsThemOnTheBelt()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(5, 0, 0, Smelter, -1));
        int belt = Ok(w, Belt(1, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        Ok(w, new ConnectToMachine(belt, m));
        Ok(w, new SetSource(belt, new ushort[] { (ushort)Ore }));
        w.Step(300);
        Assert.Equal(17, w.Network.CountOn(belt));

        // Giving it a recipe unblocks the belt.
        Ok(w, new SetRecipe(m, SmeltIron));
        w.Step(300);
        Assert.True(w.Network.MachineInputsConsumed > 0);
        w.ValidateInvariants();
    }

    [Fact]
    public void ChangingTheRecipeSwitchesWhatIsProducedAndDiscardsTheBuffers()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(0, 0, 0, Extractor, MineIron));
        int belt = Ok(w, Belt(1, 0, 0, Dir.PosX, 4, BeltTier.Mk3)); // dead end, so items stay visible
        Ok(w, new ConnectFromMachine(m, belt));
        w.Step(200);

        var samples = new ItemSample[64];
        int n = w.Network.FillSamples(samples);
        Assert.True(n > 0);
        Assert.All(samples.Take(n), s => Assert.Equal(Ore, s.Type));

        int held = w.Network.GetMachineInfo(m).OutputItems;
        long destroyed = w.Network.Destroyed;
        Ok(w, new SetRecipe(m, MineCarbon));
        Assert.Equal(destroyed + held, w.Network.Destroyed);

        w.Step(300);
        n = w.Network.FillSamples(samples);
        // Samples run from the front of the belt to the tail: old ore first, new carbon behind it.
        Assert.Equal(Ore, samples[0].Type);
        Assert.Equal(Carbon, samples[n - 1].Type);
        int firstCarbon = Array.FindIndex(samples, 0, n, s => s.Type == Carbon);
        Assert.All(samples.Skip(firstCarbon).Take(n - firstCarbon), s => Assert.Equal(Carbon, s.Type));

        // Clearing the recipe stops production.
        Ok(w, new SetRecipe(m, -1));
        long created = w.Network.MachineOutputsCreated;
        w.Step(300);
        Assert.Equal(created, w.Network.MachineOutputsCreated);
        w.ValidateInvariants();
    }

    [Fact]
    public void RemovingAMachineDestroysItsContentsDetachesBeltsAndFreesTheCell()
    {
        var w = World();
        Ok(w, ChainBatch());
        w.Step(30 * 60);

        var info = w.Network.GetMachineInfo(1);
        long held = info.InputItems + info.OutputItems;
        Assert.True(held > 0 || info.Crafting);
        long destroyed = w.Network.Destroyed;

        Ok(w, new RemoveMachine(1));

        Assert.False(w.Network.IsMachineAlive(1));
        Assert.Equal(destroyed + held, w.Network.Destroyed);
        Assert.False(w.Network.FeedsAnyMachine(0));
        Assert.False(w.Network.IsFedByAnyMachine(1));
        Assert.Equal(Reject.UnknownMachine, Run(w, new RemoveMachine(1)).Reject);
        w.ValidateInvariants();

        Assert.Equal(3, Ok(w, new PlaceMachine(5, 0, 0, Smelter, SmeltIron))); // ids are never reused

        // Removing a belt next to a machine detaches it from the machine too.
        Ok(w, new RemoveSegment(1));
        w.Step(100);
        w.ValidateInvariants();
    }

    // ---- validation ---------------------------------------------------------------------------

    [Fact]
    public void WorldsWithoutContentRejectMachines()
    {
        var w = new SimWorld();
        Assert.Equal(Reject.NoContent, Run(w, new PlaceMachine(0, 0, 0, 0, -1)).Reject);
        Assert.Equal(Reject.UnknownMachine, Run(w, new RemoveMachine(0)).Reject);
    }

    [Fact]
    public void PlacingMachinesIsValidated()
    {
        var w = World();
        Assert.Equal(Reject.UnknownMachineType, Run(w, new PlaceMachine(0, 0, 0, 99, -1)).Reject);
        Assert.Equal(Reject.UnknownMachineType, Run(w, new PlaceMachine(0, 0, 0, -1, -1)).Reject);
        Assert.Equal(Reject.UnknownRecipe, Run(w, new PlaceMachine(0, 0, 0, Smelter, 99)).Reject);
        Assert.Equal(Reject.UnknownRecipe, Run(w, new PlaceMachine(0, 0, 0, Smelter, -2)).Reject);
        Assert.Equal(Reject.RecipeMismatch, Run(w, new PlaceMachine(0, 0, 0, Smelter, MineIron)).Reject);
        Assert.Equal(Reject.OutOfBounds, Run(w, new PlaceMachine(SimWorld.MaxCoord + 1, 0, 0, Smelter, -1)).Reject);
        Assert.Equal(0, w.Network.MachineCount);

        Ok(w, new PlaceMachine(0, 0, 0, Smelter, SmeltIron));
        Assert.Equal(Reject.Overlap, Run(w, new PlaceMachine(0, 0, 0, Extractor, -1)).Reject);
        Ok(w, Belt(1, 0, 0, Dir.PosX, 2));
        Assert.Equal(Reject.Overlap, Run(w, new PlaceMachine(2, 0, 0, Extractor, -1)).Reject);
        Assert.Equal(Reject.Overlap, Run(w, Belt(0, 0, -1, Dir.PosZ, 3)).Reject); // a belt cannot cross a machine
        Assert.Equal(Reject.RecipeMismatch, Run(w, new SetRecipe(0, Plate)).Reject);
        Assert.Equal(Reject.UnknownRecipe, Run(w, new SetRecipe(0, 50)).Reject);
        Assert.Equal(Reject.UnknownMachine, Run(w, new SetRecipe(7, SmeltIron)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void ConnectingMachinesChecksGeometryAndLimits()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(5, 0, 0, Smelter, SmeltIron));
        int a = Ok(w, Belt(1, 0, 0, Dir.PosX, 4));            // last cell (4,0,0) touches the machine
        int far = Ok(w, Belt(1, 3, 0, Dir.PosX, 4));
        int o1 = Ok(w, Belt(6, 0, 0, Dir.PosX, 2));
        int o2 = Ok(w, Belt(5, 1, 0, Dir.PosY, 2));
        int o3 = Ok(w, Belt(5, 0, 1, Dir.PosZ, 2));

        Assert.Equal(Reject.UnknownMachine, Run(w, new ConnectToMachine(a, 9)).Reject);
        Assert.Equal(Reject.UnknownSegment, Run(w, new ConnectToMachine(99, m)).Reject);
        Assert.Equal(Reject.NotAdjacent, Run(w, new ConnectToMachine(far, m)).Reject);
        Assert.Equal(Reject.NotAdjacent, Run(w, new ConnectFromMachine(m, far)).Reject);
        Assert.Equal(Reject.NotAdjacent, Run(w, new ConnectFromMachine(m, a)).Reject); // a's start is not next to the machine

        Ok(w, new ConnectToMachine(a, m));
        Assert.Equal(Reject.AlreadyConnected, Run(w, new ConnectToMachine(a, m)).Reject);

        Ok(w, new ConnectFromMachine(m, o1));
        Assert.Equal(Reject.AlreadyConnected, Run(w, new ConnectFromMachine(m, o1)).Reject);
        Ok(w, new ConnectFromMachine(m, o2));
        Assert.Equal(Reject.MachineOutputsFull, Run(w, new ConnectFromMachine(m, o3)).Reject);

        Assert.Equal(Reject.NotConnected, Run(w, new DisconnectMachine(o3, m)).Reject);
        Ok(w, new DisconnectMachine(o2, m));
        Ok(w, new ConnectFromMachine(m, o3));
        Ok(w, new DisconnectMachine(a, m));
        Assert.False(w.Network.FeedsAnyMachine(a));
        w.ValidateInvariants();
    }

    [Fact]
    public void BeltsLinkedToMachinesCannotAlsoHaveConflictingLinks()
    {
        var w = World();
        int m = Ok(w, new PlaceMachine(5, 0, 0, Smelter, SmeltIron));
        int m2 = Ok(w, new PlaceMachine(4, 1, 0, Smelter, SmeltIron));
        int a = Ok(w, Belt(1, 0, 0, Dir.PosX, 4));   // feeds machine m, last cell (4,0,0)
        int next = Ok(w, Belt(4, 0, 1, Dir.PosZ, 2));
        int fed = Ok(w, Belt(6, 0, 0, Dir.PosX, 3)); // fed by machine m
        int up = Ok(w, Belt(6, 2, 0, Dir.NegY, 2));  // last cell (6,1,0) touches fed's first cell
        Ok(w, new ConnectToMachine(a, m));
        Ok(w, new ConnectFromMachine(m, fed));

        // A belt that feeds a machine cannot feed anything else.
        Assert.Equal(Reject.SinkHasOutputs, Run(w, new SetSink(a)).Reject);
        Assert.Equal(Reject.SegmentConflict, Run(w, new ConnectSegments(a, next)).Reject);
        Assert.Equal(Reject.SegmentConflict, Run(w, new ConnectToMachine(a, m2)).Reject);

        // A belt fed by a machine cannot have a source or input belts.
        Assert.Equal(Reject.SegmentConflict, Run(w, new SetSource(fed, new ushort[] { 0 })).Reject);
        Assert.Equal(Reject.SegmentConflict, Run(w, new ConnectSegments(up, fed)).Reject);

        // A belt with input belts cannot be fed by a machine.
        int s = Ok(w, Belt(0, 6, 0, Dir.PosX, 2));
        int t = Ok(w, Belt(-2, 7, 0, Dir.PosX, 2));
        Ok(w, new SetSink(s));
        int m3 = Ok(w, new PlaceMachine(0, 5, 0, Extractor, MineIron));
        Ok(w, new ClearSink(s));
        Assert.Equal(Reject.NotAdjacent, Run(w, new ConnectSegments(t, s)).Reject); // sanity: t is not next to s
        int t2 = Ok(w, Belt(-2, 6, 0, Dir.PosX, 2)); // last cell (-1,6,0) touches s's first cell
        Ok(w, new ConnectSegments(t2, s));
        Assert.Equal(Reject.SegmentConflict, Run(w, new ConnectFromMachine(m3, s)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void FailedBatchWithMachinesRollsBackEverything()
    {
        var w = World();
        var r = Run(w, new Batch(new Command[]
        {
            new PlaceMachine(0, 0, 0, Extractor, MineIron),
            Belt(1, 0, 0),
            new ConnectFromMachine(-1, -1),
            Belt(2, 0, 0), // overlaps the belt placed above
        }));

        Assert.Equal(Reject.Overlap, r.Reject);
        Assert.Equal(3, r.FailedIndex);
        Assert.Empty(r.CreatedMachines);
        Assert.Equal(0, w.Network.MachineCount);
        Assert.Equal(0, w.Network.MachineIdLimit);
        Assert.Equal(0, w.Network.SegmentCount);
        w.ValidateInvariants();

        // Same cells and ids are available again.
        Assert.Equal(0, Ok(w, new PlaceMachine(0, 0, 0, Extractor, MineIron)));
    }

    // ---- codec --------------------------------------------------------------------------------

    [Fact]
    public void MachineCommandsSurviveARoundTrip()
    {
        Command[] commands =
        {
            new PlaceMachine(-4, 5, 6, 2, 3), new RemoveMachine(9), new SetRecipe(1, -1),
            new ConnectToMachine(3, 4), new ConnectFromMachine(5, -1), new DisconnectMachine(6, 7),
            new Batch(new Command[] { new PlaceMachine(0, 0, 0, 0, 0), new ConnectFromMachine(-1, 2) }),
        };
        foreach (var c in commands)
        {
            byte[] bytes = CommandCodec.ToBytes(c);
            var back = CommandCodec.FromBytes(bytes);
            Assert.Equal(c.GetType(), back.GetType());
            Assert.Equal(bytes, CommandCodec.ToBytes(back));
        }
        Assert.Equal(commands[0], CommandCodec.FromBytes(CommandCodec.ToBytes(commands[0])));
    }

    [Fact]
    public void LogsCarryTheContentHash()
    {
        var w = World();
        Ok(w, ChainBatch());
        w.Step(100);

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash);
        ms.Position = 0;
        var back = CommandCodec.ReadLog(ms, out ulong hash);

        Assert.Equal(C.Hash, hash);
        Assert.Equal(w.Log.Count, back.Count);
        Assert.Equal(w.Checksum(), SimWorld.Replay(back, w.Tick, content: C).Checksum());

        // Replaying against different content must not be mistaken for the same state.
        var other = ContentDb.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json"))
            .Replace("\"time_s\": 2.0", "\"time_s\": 3.0"));
        Assert.NotEqual(C.Hash, other.Hash);
        Assert.NotEqual(w.Checksum(), SimWorld.Replay(back, w.Tick, content: other).Checksum());
    }

    // ---- fuzz ---------------------------------------------------------------------------------

    private static int PickAliveMachine(Random rng, BeltNetwork net)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            int id = rng.Next(0, Math.Max(1, net.MachineIdLimit));
            if (net.IsMachineAlive(id)) return id;
        }
        return -1;
    }

    private static Command RandomMachineCommand(Random rng, SimWorld w)
    {
        var net = w.Network;
        int PickRecipeFor(int type)
        {
            var ids = C.Recipes.Where(r => r.Machine == type).Select(r => r.Index).ToList();
            return rng.Next(0, 6) == 0 ? -1 : ids[rng.Next(0, ids.Count)];
        }

        switch (rng.Next(0, 9))
        {
            case 0:
            {
                int type = rng.Next(0, 12) == 0 ? 9 : rng.Next(0, C.Machines.Count);
                int recipe = rng.Next(0, 10) == 0 ? rng.Next(-2, 8) : (C.IsMachine(type) ? PickRecipeFor(type) : -1);
                return new PlaceMachine(rng.Next(0, 6), rng.Next(0, 2), rng.Next(0, 6), type, recipe);
            }
            case 1:
            case 2:
            {
                // Put a machine on a free cell next to the end of a belt that still has no output, and feed it.
                int seg = PickSegment(rng, net, s => net.OutputCount(s) == 0 && !net.IsSink(s) && !net.FeedsAnyMachine(s));
                if (seg < 0) return new RemoveMachine(rng.Next(-1, 6));
                var f = net.GetSegmentInfo(seg);
                int lx = f.OriginX + f.DirX * (f.LengthCells - 1);
                int ly = f.OriginY + f.DirY * (f.LengthCells - 1);
                int lz = f.OriginZ + f.DirZ * (f.LengthCells - 1);
                var (dx, dy, dz) = FreeNeighbor(rng, net, lx, ly, lz);
                if (dx == 0 && dy == 0 && dz == 0) return new SetRecipe(rng.Next(-1, 4), rng.Next(-1, 5));
                int type = rng.Next(0, C.Machines.Count);
                return new Batch(new Command[]
                {
                    new PlaceMachine(lx + dx, ly + dy, lz + dz, type, PickRecipeFor(type)),
                    new ConnectToMachine(seg, -1),
                });
            }
            case 3:
            case 4:
            {
                // Start a belt on a free cell next to a machine that still has room for an output, and let the machine feed it.
                int m = PickMachine(rng, net, id => net.MachineOutputCount(id) < 2);
                if (m < 0) return new SetRecipe(rng.Next(-1, 4), rng.Next(-1, 5));
                var info = net.GetMachineInfo(m);
                var (dx, dy, dz) = FreeNeighbor(rng, net, info.X, info.Y, info.Z);
                if (dx == 0 && dy == 0 && dz == 0) return new RemoveMachine(rng.Next(-1, 6));
                var dir = (Dir)rng.Next(0, 6);
                var (vx, vy, vz) = Dirs.Vector(dir);
                int len = 1;
                while (len < 4 && IsFree(net, info.X + dx + vx * len, info.Y + dy + vy * len, info.Z + dz + vz * len)) len++;
                var cmds = new List<Command>
                {
                    new PlaceSegment(info.X + dx, info.Y + dy, info.Z + dz, dir, len, (BeltTier)rng.Next(0, 3)),
                    new ConnectFromMachine(m, -1),
                };
                if (rng.Next(0, 2) == 0) cmds.Add(new SetSink(-1));
                return new Batch(cmds.ToArray());
            }
            case 5: return new RemoveMachine(rng.Next(0, 6) == 0 ? rng.Next(-1, net.MachineIdLimit + 2) : PickAliveMachine(rng, net));
            case 6: return new SetRecipe(PickAliveMachine(rng, net), rng.Next(-2, C.Recipes.Count + 2));
            case 7: return new ConnectToMachine(rng.Next(-1, net.SegmentIdLimit + 1), rng.Next(-1, net.MachineIdLimit + 1));
            default:
                return rng.Next(0, 2) == 0
                    ? new ConnectFromMachine(rng.Next(-1, net.MachineIdLimit + 1), rng.Next(-1, net.SegmentIdLimit + 1))
                    : new DisconnectMachine(rng.Next(-1, net.SegmentIdLimit + 1), rng.Next(-1, net.MachineIdLimit + 1));
        }
    }

    private static int PickSegment(Random rng, BeltNetwork net, Func<int, bool> filter)
    {
        var ids = Enumerable.Range(0, net.SegmentIdLimit).Where(id => net.IsAlive(id) && filter(id)).ToList();
        return ids.Count == 0 ? -1 : ids[rng.Next(0, ids.Count)];
    }

    private static int PickMachine(Random rng, BeltNetwork net, Func<int, bool> filter)
    {
        var ids = Enumerable.Range(0, net.MachineIdLimit).Where(id => net.IsMachineAlive(id) && filter(id)).ToList();
        return ids.Count == 0 ? -1 : ids[rng.Next(0, ids.Count)];
    }

    /// <summary>True if no belt or machine occupies the cell. Linear scan: fine for the small fuzz worlds.</summary>
    private static bool IsFree(BeltNetwork net, int x, int y, int z)
    {
        for (int id = 0; id < net.SegmentIdLimit; id++)
        {
            if (!net.IsAlive(id)) continue;
            var s = net.GetSegmentInfo(id);
            for (int k = 0; k < s.LengthCells; k++)
                if (s.OriginX + s.DirX * k == x && s.OriginY + s.DirY * k == y && s.OriginZ + s.DirZ * k == z) return false;
        }
        for (int id = 0; id < net.MachineIdLimit; id++)
        {
            if (!net.IsMachineAlive(id)) continue;
            var m = net.GetMachineInfo(id);
            if (m.X == x && m.Y == y && m.Z == z) return false;
        }
        return true;
    }

    /// <summary>A random free face neighbour of the cell, or (0,0,0) if there is none.</summary>
    private static (int dx, int dy, int dz) FreeNeighbor(Random rng, BeltNetwork net, int x, int y, int z)
    {
        var options = new List<(int, int, int)>();
        for (int d = 0; d < 6; d++)
        {
            var (dx, dy, dz) = Dirs.Vector((Dir)d);
            if (IsFree(net, x + dx, y + dy, z + dz)) options.Add((dx, dy, dz));
        }
        return options.Count == 0 ? (0, 0, 0) : options[rng.Next(0, options.Count)];
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void RandomCommandStormWithMachinesKeepsInvariantsAndReplaysIdentically(int seed)
    {
        const int ticks = 3000;
        var rng = new Random(seed);
        var w = World();
        var checksums = new Dictionary<long, ulong>();
        int machinesPlaced = 0, machinesRemoved = 0, recipeChanges = 0, linked = 0, rolledBack = 0;

        for (int t = 0; t < ticks; t++)
        {
            int n = rng.Next(0, 4);
            for (int i = 0; i < n; i++)
            {
                var cmd = rng.Next(0, 5) < 2 ? CommandTests.RandomCommand(rng, w) : RandomMachineCommand(rng, w);
                w.Submit(rng.Next(0, 3), cmd);
            }
            w.Step();

            foreach (var r in w.LastResults)
            {
                if (r.Ok)
                {
                    machinesPlaced += r.CreatedMachines.Count;
                    if (r.Command is RemoveMachine) machinesRemoved++;
                    if (r.Command is SetRecipe) recipeChanges++;
                    if (r.Command is ConnectToMachine or ConnectFromMachine) linked++;
                    if (r.Command is Batch b) linked += b.Commands.Count(c => c is ConnectToMachine or ConnectFromMachine);
                }
                if (r.Command is Batch && !r.Ok && r.FailedIndex >= 0) rolledBack++;
            }
            w.ValidateInvariants();
            if (t == 700 || t == 2000) checksums[w.Tick] = w.Checksum();
        }

        Assert.True(machinesPlaced > 5, $"only {machinesPlaced} machines placed");
        Assert.True(machinesRemoved > 0, "no machine was removed");
        Assert.True(recipeChanges > 0, "no recipe was changed");
        Assert.True(linked > 3, $"only {linked} machine links made");
        Assert.True(rolledBack > 0, "no batch was rolled back");
        Assert.True(w.Network.MachineOutputsCreated > 0, "nothing was crafted");

        Assert.Equal(w.Checksum(), SimWorld.Replay(w.Log, ticks, content: C).Checksum());

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash);
        ms.Position = 0;
        var restored = CommandCodec.ReadLog(ms, out ulong hash);
        Assert.Equal(C.Hash, hash);
        var replay = SimWorld.Replay(restored, ticks, content: C);
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();

        foreach (var (tick, checksum) in checksums)
            Assert.Equal(checksum, SimWorld.Replay(restored, tick, content: C).Checksum());
    }
}
