using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class CommandTests
{
    private static CommandResult Run(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int RunOk(SimWorld w, Command c)
    {
        var r = Run(w, c);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.Created.Count > 0 ? r.Created[0] : -1;
    }

    private static PlaceSegment Belt(int x, int y, int z, Dir d = Dir.PosX, int len = 4, BeltTier t = BeltTier.Mk1) =>
        new(x, y, z, d, len, t);

    // ---- application -----------------------------------------------------------------------

    [Fact]
    public void CommandsWaitForTheNextTickAndReportTheirResult()
    {
        var w = new SimWorld();
        w.Submit(7, Belt(0, 0, 0));

        Assert.Equal(0, w.Network.SegmentCount);
        Assert.Equal(1, w.PendingCommands);

        w.Step();

        Assert.Equal(1, w.Network.SegmentCount);
        var r = Assert.Single(w.LastResults);
        Assert.True(r.Ok);
        Assert.Equal(0L, r.Tick);
        Assert.Equal(7, r.Player);
        Assert.Equal(new[] { 0 }, r.Created);
        Assert.Equal(1L, w.Tick);
        Assert.Equal(0, w.PendingCommands);
    }

    [Fact]
    public void CommandsApplyInSubmissionOrder()
    {
        ulong Winner(bool firstIsA)
        {
            var w = new SimWorld();
            var a = Belt(0, 0, 0, Dir.PosX, 4);
            var b = Belt(2, 0, 0, Dir.PosZ, 3); // crosses cell (2,0,0)
            w.Submit(0, firstIsA ? a : b);
            w.Submit(0, firstIsA ? b : a);
            w.Step();
            Assert.True(w.LastResults[0].Ok);
            Assert.Equal(Reject.Overlap, w.LastResults[1].Reject);
            return (ulong)w.Network.GetSegmentInfo(0).LengthCells;
        }

        Assert.Equal(4UL, Winner(true));
        Assert.Equal(3UL, Winner(false));
    }

    [Fact]
    public void LogRecordsEveryCommandWithItsTick()
    {
        var w = new SimWorld();
        w.Step(5);
        Run(w, Belt(0, 0, 0));
        Run(w, new RemoveSegment(99)); // rejected, still logged

        Assert.Equal(2, w.Log.Count);
        Assert.Equal(5L, w.Log[0].Tick);
        Assert.Equal(6L, w.Log[1].Tick);
    }

    // ---- validation -----------------------------------------------------------------------

    [Fact]
    public void PlaceRejectsBadArgumentsAndOverlaps()
    {
        var w = new SimWorld();
        Assert.Equal(Reject.InvalidArgument, Run(w, Belt(0, 0, 0, len: 0)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, Belt(0, 0, 0, len: SimWorld.MaxSegmentCells + 1)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, Belt(0, 0, 0, t: (BeltTier)9)).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, Belt(0, 0, 0, d: (Dir)200)).Reject);
        Assert.Equal(Reject.OutOfBounds, Run(w, Belt(SimWorld.MaxCoord + 1, 0, 0)).Reject);
        Assert.Equal(Reject.OutOfBounds, Run(w, Belt(SimWorld.MaxCoord - 1, 0, 0, len: 5)).Reject);
        Assert.Equal(Reject.OutOfBounds, Run(w, Belt(int.MaxValue, 0, 0)).Reject);
        Assert.Equal(0, w.Network.SegmentCount);

        RunOk(w, Belt(0, 0, 0, Dir.PosX, 4));
        Assert.Equal(Reject.Overlap, Run(w, Belt(3, 0, 0, Dir.PosZ, 2)).Reject);
        Assert.Equal(Reject.Overlap, Run(w, Belt(-2, 0, 0, Dir.PosX, 3)).Reject);
        Assert.True(Run(w, Belt(4, 0, 0, Dir.PosX, 2)).Ok); // touching is fine
        Assert.True(Run(w, Belt(0, 1, 0, Dir.PosX, 4)).Ok); // another layer is fine
        w.ValidateInvariants();
    }

    [Fact]
    public void ConnectChecksGeometryAndLimits()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 4));      // last cell (3,0,0)
        int ahead = RunOk(w, Belt(4, 0, 0, Dir.PosX, 4));  // straight ahead
        int side = RunOk(w, Belt(3, 0, 1, Dir.PosZ, 2));   // to the side
        int up = RunOk(w, Belt(3, 1, 0, Dir.PosY, 2));     // above
        int far = RunOk(w, Belt(10, 0, 0, Dir.PosX, 2));
        int diagonal = RunOk(w, Belt(4, 0, 1, Dir.PosX, 2));

        Assert.Equal(Reject.UnknownSegment, Run(w, new ConnectSegments(a, 99)).Reject);
        Assert.Equal(Reject.UnknownSegment, Run(w, new ConnectSegments(-1, a)).Reject);
        Assert.Equal(Reject.SelfConnection, Run(w, new ConnectSegments(a, a)).Reject);
        Assert.Equal(Reject.NotAdjacent, Run(w, new ConnectSegments(a, far)).Reject);
        Assert.Equal(Reject.NotAdjacent, Run(w, new ConnectSegments(a, diagonal)).Reject);

        Assert.True(Run(w, new ConnectSegments(a, ahead)).Ok);
        Assert.Equal(Reject.AlreadyConnected, Run(w, new ConnectSegments(a, ahead)).Reject);
        Assert.True(Run(w, new ConnectSegments(a, side)).Ok);
        Assert.Equal(Reject.OutputsFull, Run(w, new ConnectSegments(a, up)).Reject);

        Assert.Equal(Reject.NotConnected, Run(w, new DisconnectSegments(ahead, side)).Reject);
        Assert.True(Run(w, new DisconnectSegments(a, side)).Ok);
        Assert.True(Run(w, new ConnectSegments(a, up)).Ok);
        w.ValidateInvariants();
    }

    [Fact]
    public void SinkAndSourceRules()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 4));
        int b = RunOk(w, Belt(4, 0, 0, Dir.PosX, 4));
        RunOk(w, new ConnectSegments(a, b));

        Assert.Equal(Reject.SinkHasOutputs, Run(w, new SetSink(a)).Reject);
        RunOk(w, new SetSink(b));
        Assert.Equal(Reject.FromIsSink, Run(w, new ConnectSegments(b, a)).Reject);
        RunOk(w, new ClearSink(b));
        Assert.True(Run(w, new SetSink(b)).Ok);

        Assert.Equal(Reject.InvalidArgument, Run(w, new SetSource(a, Array.Empty<ushort>())).Reject);
        Assert.Equal(Reject.UnknownSegment, Run(w, new SetSource(42, new ushort[] { 1 })).Reject);
        Assert.True(Run(w, new SetSource(a, new ushort[] { 1, 2 })).Ok);
        Assert.True(w.Network.HasSource(a));
        Assert.True(Run(w, new ClearSource(a)).Ok);
        Assert.False(w.Network.HasSource(a));
    }

    // ---- behaviour ---------------------------------------------------------------------------

    [Fact]
    public void ABeltBuiltOnlyWithCommandsCarriesItsFullThroughput()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 6, BeltTier.Mk2));
        int b = RunOk(w, Belt(6, 0, 0, Dir.PosX, 6, BeltTier.Mk2));
        RunOk(w, new ConnectSegments(a, b));
        RunOk(w, new SetSource(a, new ushort[] { 0 }));
        RunOk(w, new SetSink(b));

        w.Step(30 * 20);
        long before = w.Network.Consumed;
        w.Step(30 * 20);
        double perSecond = (w.Network.Consumed - before) / 20.0;

        Assert.InRange(perSecond, 10 * 0.97, 10 * 1.03);
        w.ValidateInvariants();
    }

    [Fact]
    public void RemovingASegmentDestroysItsItemsAndFreesTheGrid()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        int b = RunOk(w, Belt(4, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        int c = RunOk(w, Belt(8, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        RunOk(w, new ConnectSegments(a, b));
        RunOk(w, new ConnectSegments(b, c));
        RunOk(w, new SetSource(a, new ushort[] { 0 }));
        RunOk(w, new SetSink(c));
        w.Step(300);

        int onB = w.Network.CountOn(b);
        Assert.True(onB > 0);
        RunOk(w, new RemoveSegment(b));

        Assert.False(w.Network.IsAlive(b));
        Assert.Equal(onB, w.Network.Destroyed);
        Assert.Equal(0, w.Network.OutputCount(a));
        Assert.Equal(Reject.UnknownSegment, Run(w, new RemoveSegment(b)).Reject);
        Assert.Equal(Reject.UnknownSegment, Run(w, new ConnectSegments(a, b)).Reject);
        w.ValidateInvariants();

        // The cells are free again but the id is not reused.
        int again = RunOk(w, Belt(4, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        Assert.Equal(3, again);

        // The belt before the gap fills up; nothing is lost.
        w.Step(600);
        Assert.Equal(w.Network.Spawned, w.Network.Consumed + w.Network.Destroyed + w.Network.InTransit);
        w.ValidateInvariants();
    }

    [Fact]
    public void DisconnectingStopsTheFlowWithoutLosingItems()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        int b = RunOk(w, Belt(4, 0, 0, Dir.PosX, 4, BeltTier.Mk2));
        RunOk(w, new ConnectSegments(a, b));
        RunOk(w, new SetSource(a, new ushort[] { 0 }));
        RunOk(w, new SetSink(b));
        w.Step(300);
        Assert.True(w.Network.Consumed > 0);

        RunOk(w, new DisconnectSegments(a, b));
        w.Step(300);
        long consumed = w.Network.Consumed;
        w.Step(300);

        Assert.Equal(consumed, w.Network.Consumed);
        Assert.Equal(0L, w.Network.Destroyed);
        w.ValidateInvariants();
    }

    // ---- batches -----------------------------------------------------------------------------

    [Fact]
    public void BatchAppliesAllCommandsAndResolvesReferences()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 4));

        var r = Run(w, new Batch(new Command[]
        {
            Belt(4, 0, 0, Dir.PosX, 4),
            Belt(8, 0, 0, Dir.PosX, 4),
            new ConnectSegments(a, -1),
            new ConnectSegments(-1, -2),
            new SetSink(-2),
        }));

        Assert.True(r.Ok);
        Assert.Equal(new[] { 1, 2 }, r.Created);
        Assert.True(w.Network.IsConnected(a, 1));
        Assert.True(w.Network.IsConnected(1, 2));
        Assert.True(w.Network.IsSink(2));
        w.ValidateInvariants();
    }

    [Fact]
    public void FailedBatchRollsBackEverythingIncludingIds()
    {
        var w = new SimWorld();
        int a = RunOk(w, Belt(0, 0, 0, Dir.PosX, 4));

        var r = Run(w, new Batch(new Command[]
        {
            Belt(4, 0, 0, Dir.PosX, 4),
            new ConnectSegments(a, -1),
            new SetSink(-1),
            Belt(1, 0, 0, Dir.PosX, 2), // overlaps segment 0
        }));

        Assert.Equal(Reject.Overlap, r.Reject);
        Assert.Equal(3, r.FailedIndex);
        Assert.Empty(r.Created);
        Assert.Equal(1, w.Network.SegmentCount);
        Assert.Equal(1, w.Network.SegmentIdLimit);
        Assert.Equal(0, w.Network.OutputCount(a));

        // Rolled back to the state before the batch: only the tick advanced.
        w.ValidateInvariants();
        var clean = new SimWorld();
        RunOk(clean, Belt(0, 0, 0, Dir.PosX, 4));
        clean.Step((int)(w.Tick - clean.Tick));
        Assert.Equal(clean.Checksum(), w.Checksum());

        // The cells claimed inside the failed batch are free and the id counter was restored.
        Assert.Equal(1, RunOk(w, Belt(4, 0, 0, Dir.PosX, 4)));
    }

    [Fact]
    public void BatchRejectsNestingEmptyAndDanglingReferences()
    {
        var w = new SimWorld();
        Assert.Equal(Reject.NestedBatch, Run(w, new Batch(new Command[] { new Batch(new Command[] { Belt(0, 0, 0) }) })).Reject);
        Assert.Equal(Reject.InvalidArgument, Run(w, new Batch(Array.Empty<Command>())).Reject);

        var r = Run(w, new Batch(new Command[] { new SetSink(-1) }));
        Assert.Equal(Reject.UnknownSegment, r.Reject);
        Assert.Equal(0, r.FailedIndex);
        Assert.Equal(0, w.Network.SegmentCount);
    }

    [Fact]
    public void WorldWrappingAScenarioIndexesItsCells()
    {
        var w = new SimWorld(Scenarios.ParallelRows(2, 2, 10));
        w.ValidateInvariants();

        Assert.Equal(Reject.Overlap, Run(w, Belt(3, 0, 0, Dir.PosZ, 2)).Reject);
        Assert.True(Run(w, Belt(0, 1, 0, Dir.PosX, 10)).Ok);
    }

    [Fact]
    public void WrappingOverlappingSegmentsIsRejected()
    {
        var net = new BeltNetwork();
        net.AddSegment(0, 0, 0, 1, 0, 0, 4, BeltTier.Mk1);
        net.AddSegment(2, 0, 0, 0, 0, 1, 3, BeltTier.Mk1);
        Assert.Throws<ArgumentException>(() => new SimWorld(net));
    }

    // ---- codec -------------------------------------------------------------------------------

    private static IEnumerable<Command> SampleCommands()
    {
        yield return new PlaceSegment(-5, 12, 9999, Dir.NegZ, 17, BeltTier.Mk3);
        yield return new RemoveSegment(3);
        yield return new ConnectSegments(1, -2);
        yield return new DisconnectSegments(4, 5);
        yield return new SetSource(2, new ushort[] { 0, 3, 65535 });
        yield return new ClearSource(6);
        yield return new SetSink(7);
        yield return new ClearSink(8);
        yield return new Batch(new Command[] { Belt(0, 0, 0), new ConnectSegments(0, -1), new SetSink(-1) });
    }

    [Fact]
    public void EveryCommandSurvivesARoundTrip()
    {
        foreach (var c in SampleCommands())
        {
            byte[] bytes = CommandCodec.ToBytes(c);
            var back = CommandCodec.FromBytes(bytes);
            Assert.Equal(c.GetType(), back.GetType());
            Assert.Equal(bytes, CommandCodec.ToBytes(back));
        }

        Assert.Equal(new PlaceSegment(1, 2, 3, Dir.PosY, 4, BeltTier.Mk2),
            CommandCodec.FromBytes(CommandCodec.ToBytes(new PlaceSegment(1, 2, 3, Dir.PosY, 4, BeltTier.Mk2))));
    }

    [Fact]
    public void DecoderRejectsMalformedInput()
    {
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(new byte[] { 200 }));
        Assert.Throws<EndOfStreamException>(() => CommandCodec.FromBytes(new byte[] { 1, 0, 0 }));
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(CommandCodec.ToBytes(new SetSink(1)).Append((byte)0).ToArray()));

        // Pattern longer than allowed.
        var tooLong = new MemoryStream();
        using (var w = new BinaryWriter(tooLong))
        {
            w.Write((byte)5); w.Write(0); w.Write(SimWorld.MaxPattern + 1);
        }
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(tooLong.ToArray()));

        // Batches nested one level deeper than the format allows.
        var nested = new MemoryStream();
        using (var w = new BinaryWriter(nested))
        {
            for (int i = 0; i < CommandCodec.MaxNesting - 1; i++) { w.Write((byte)9); w.Write(1); }
            w.Write((byte)9); w.Write(0);
        }
        Assert.Throws<InvalidDataException>(() => CommandCodec.FromBytes(nested.ToArray()));
    }

    [Fact]
    public void SubmitRefusesCommandsThatCouldNeverBeLogged()
    {
        var w = new SimWorld();
        Command deep = new SetSink(0);
        for (int i = 0; i < CommandCodec.MaxNesting; i++) deep = new Batch(new[] { deep });

        Assert.Throws<ArgumentException>(() => w.Submit(0, deep));
        Assert.Throws<ArgumentException>(() => w.Submit(0, new Batch(new Command[] { null! })));
        Assert.Throws<ArgumentException>(() => w.Submit(0, new Batch(null!)));
        Assert.Throws<ArgumentException>(() => w.Submit(0, new SetSource(0, null!)));
        Assert.Throws<ArgumentException>(() => w.Submit(0, new SetSource(0, new ushort[SimWorld.MaxPattern + 1])));
        Assert.Throws<ArgumentNullException>(() => w.Submit(0, null!));
        Assert.Equal(0, w.PendingCommands);

        // A batch inside a batch is well formed (it can be logged) and is rejected by the world.
        Assert.Equal(Reject.NestedBatch,
            Run(w, new Batch(new Command[] { new Batch(new Command[] { new SetSink(0) }) })).Reject);
    }

    [Fact]
    public void LogRoundTripsAndRejectsCorruptHeaders()
    {
        var log = SampleCommands().Select((c, i) => new LoggedCommand(i * 3, i % 2, c)).ToList();
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, log);

        ms.Position = 0;
        var back = CommandCodec.ReadLog(ms);
        Assert.Equal(log.Count, back.Count);
        for (int i = 0; i < log.Count; i++)
        {
            Assert.Equal(log[i].Tick, back[i].Tick);
            Assert.Equal(log[i].Player, back[i].Player);
            Assert.Equal(CommandCodec.ToBytes(log[i].Command), CommandCodec.ToBytes(back[i].Command));
        }

        byte[] bytes = ms.ToArray();
        var badMagic = (byte[])bytes.Clone(); badMagic[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(badMagic)));
        var badVersion = (byte[])bytes.Clone(); badVersion[4] = 99;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(badVersion)));

        var outOfOrder = new MemoryStream();
        CommandCodec.WriteLog(outOfOrder, new[]
        {
            new LoggedCommand(5, 0, new SetSink(0)),
            new LoggedCommand(2, 0, new SetSink(0)),
        });
        outOfOrder.Position = 0;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(outOfOrder));
    }

    // ---- fuzz and replay ---------------------------------------------------------------------

    private static Command RandomCommand(Random rng, SimWorld w)
    {
        var net = w.Network;
        int limit = net.SegmentIdLimit;
        int Pick() => rng.Next(-1, limit + 2);
        int PickAlive()
        {
            for (int tries = 0; tries < 20; tries++)
            {
                int id = rng.Next(0, Math.Max(1, limit));
                if (net.IsAlive(id)) return id;
            }
            return -1;
        }

        switch (rng.Next(0, 10))
        {
            case 0:
            case 1:
                return new PlaceSegment(rng.Next(0, 6), rng.Next(0, 2), rng.Next(0, 6),
                    (Dir)(rng.Next(0, 20) == 0 ? 9 : rng.Next(0, 6)), rng.Next(0, 6), (BeltTier)rng.Next(0, 3));
            case 2:
            case 3:
            {
                // Extend an existing belt in a batch so the connection is geometrically valid.
                int from = PickAlive();
                if (from < 0) return new RemoveSegment(Pick());
                var f = net.GetSegmentInfo(from);
                var d = (Dir)rng.Next(0, 6);
                var (dx, dy, dz) = Dirs.Vector(d);
                int ox = f.OriginX + f.DirX * (f.LengthCells - 1) + (rng.Next(0, 2) == 0 ? f.DirX : dx);
                int oy = f.OriginY + f.DirY * (f.LengthCells - 1) + (rng.Next(0, 2) == 0 ? f.DirY : dy);
                int oz = f.OriginZ + f.DirZ * (f.LengthCells - 1) + (rng.Next(0, 2) == 0 ? f.DirZ : dz);
                var cmds = new List<Command>
                {
                    new PlaceSegment(ox, oy, oz, d, rng.Next(1, 5), (BeltTier)rng.Next(0, 3)),
                    new ConnectSegments(from, -1),
                };
                if (rng.Next(0, 3) == 0) cmds.Add(new SetSink(-1));
                if (rng.Next(0, 4) == 0) cmds.Add(new Batch(Array.Empty<Command>()));
                return new Batch(cmds.ToArray());
            }
            case 4: return new RemoveSegment(rng.Next(0, 8) == 0 ? Pick() : PickAlive());
            case 5: return new ConnectSegments(Pick(), Pick());
            case 6: return new DisconnectSegments(Pick(), Pick());
            case 7:
            {
                int n = rng.Next(0, 5);
                var types = new ushort[n];
                for (int i = 0; i < n; i++) types[i] = (ushort)rng.Next(0, 4);
                return new SetSource(rng.Next(0, 3) == 0 ? Pick() : PickAlive(), types);
            }
            case 8: return rng.Next(0, 2) == 0 ? new SetSink(PickAlive()) : new ClearSink(PickAlive());
            default: return new ClearSource(PickAlive());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomCommandStormKeepsInvariantsAndReplaysIdentically(int seed)
    {
        const int ticks = 2500;
        var rng = new Random(seed);
        var w = new SimWorld();
        var checksums = new Dictionary<long, ulong>();
        int ok = 0, rejected = 0, rolledBack = 0, batchesOk = 0, removals = 0;

        for (int t = 0; t < ticks; t++)
        {
            int n = rng.Next(0, 4);
            for (int i = 0; i < n; i++) w.Submit(rng.Next(0, 3), RandomCommand(rng, w));
            w.Step();

            foreach (var r in w.LastResults)
            {
                if (r.Ok) ok++; else rejected++;
                if (r.Command is Batch && !r.Ok && r.FailedIndex >= 0) rolledBack++;
                if (r.Command is Batch && r.Ok) batchesOk++;
                if (r.Command is RemoveSegment && r.Ok) removals++;
            }
            w.ValidateInvariants();
            if (t == 600 || t == 1500) checksums[w.Tick] = w.Checksum();
        }

        // The storm must actually exercise both paths.
        Assert.True(ok > 100, $"only {ok} commands succeeded");
        Assert.True(rejected > 100, $"only {rejected} commands were rejected");
        Assert.True(rolledBack > 0, "no batch was rolled back");
        Assert.True(batchesOk > 0, "no batch succeeded");
        Assert.True(removals > 0, "nothing was removed");
        Assert.True(w.Network.Spawned > 0 && w.Network.Consumed > 0, "no items flowed");

        // Replay straight from the log, and again after a trip through the binary format.
        Assert.Equal(w.Checksum(), SimWorld.Replay(w.Log, ticks).Checksum());

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log);
        ms.Position = 0;
        var restored = CommandCodec.ReadLog(ms);
        var replay = SimWorld.Replay(restored, ticks);
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();

        // Replaying only part of the log reproduces the intermediate states too.
        foreach (var (tick, checksum) in checksums)
            Assert.Equal(checksum, SimWorld.Replay(restored, tick).Checksum());
    }

    [Fact]
    public void DifferentLogsGiveDifferentStates()
    {
        var a = new SimWorld();
        var b = new SimWorld();
        RunOk(a, Belt(0, 0, 0));
        RunOk(b, Belt(0, 0, 1));
        Assert.NotEqual(a.Checksum(), b.Checksum());
    }
}
