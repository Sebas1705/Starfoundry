using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class StationRulesTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly ulong Seed = 20261121UL;
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Drill = C.MachineIndex("core:drill");
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int Wall = C.MachineIndex("core:wall");
    private static readonly int Unloader = C.MachineIndex("core:unloader");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int Plate = C.ItemIndex("core:iron_plate");

    private static SimWorld World(bool rules, GameMode mode = GameMode.Sandbox)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: rules);
        w.Network.EnsurePlayer(0);
        return w;
    }

    private static Reject Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single().Reject;
    }

    private static void Ok(SimWorld w, Command c) => Assert.Equal(Reject.None, Run(w, c));

    [Fact]
    public void Free_build_unchanged_when_rules_off()
    {
        var w = World(rules: false);
        Assert.False(w.Network.StationRules);
        Ok(w, new PlaceMachine(500, 0, 500, Smelter, -1));
        Ok(w, new PlaceMachine(-300, 40, 9, Drill, -1));
        Ok(w, new PlaceSegment(0, 0, 0, Dir.PosX, 5, BeltTier.Mk1));
        w.ValidateInvariants();
    }

    [Fact]
    public void Machine_outside_station_rejected()
    {
        var w = World(rules: true);
        Assert.True(w.Network.StationRules);
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(2, 2, 1, Smelter, -1))); // no module yet
        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(8, 0, 0, Smelter, -1))); // next module over
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(-1, 0, 0, Smelter, -1)));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(0, 4, 0, Smelter, -1))); // above the module
        Assert.Equal(0, w.Network.MachineCount);
    }

    [Fact]
    public void Machine_inside_station_accepted()
    {
        var w = World(rules: true);
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceMachine(2, 2, 1, Smelter, -1));
        Ok(w, new PlaceMachine(7, 3, 7, Smelter, -1)); // far corner of the module
        Ok(w, new PlaceMachine(0, 0, 0, Smelter, -1)); // near corner
        Ok(w, new PlaceSegment(1, 2, 5, Dir.PosX, 4, BeltTier.Mk1));
        w.ValidateInvariants();
    }

    [Fact]
    public void Segment_straddling_an_edge_rejected()
    {
        var w = World(rules: true);
        Ok(w, new PlaceModule(0, 0, 0));
        // Cells 5..10 along x: the last three lie in the next module.
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegment(5, 0, 0, Dir.PosX, 6, BeltTier.Mk1)));
        // Starting outside and ending inside is refused too.
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegment(-2, 0, 0, Dir.PosX, 4, BeltTier.Mk1)));
        Ok(w, new PlaceSegment(0, 0, 0, Dir.PosX, 8, BeltTier.Mk1)); // exactly the module width
        Ok(w, new PlaceModule(1, 0, 0));
        Ok(w, new PlaceSegment(5, 3, 0, Dir.PosX, 6, BeltTier.Mk1)); // the neighbour makes it legal
        w.ValidateInvariants();
    }

    [Fact]
    public void Drill_not_allowed_with_rules_on()
    {
        var w = World(rules: true);
        Ok(w, new PlaceModule(0, 0, 0));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new PlaceMachine(2, 2, 1, Drill, -1)));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new PlaceMachine(200, 2, 1, Drill, -1)));
        Assert.Equal(0, w.Network.MachineCount);
    }

    [Fact]
    public void Unloader_available_without_research_with_rules_on()
    {
        var on = World(rules: true, GameMode.Campaign);
        Assert.False(on.Network.IsMachineLocked(Unloader));
        Assert.True(on.Network.IsMachineLocked(C.MachineIndex("core:research_lab")) || !C.MachineGated[C.MachineIndex("core:research_lab")]);
        on.Network.GiveItems(0, Ingot, 5000);
        on.Network.GiveItems(0, Plate, 5000);
        Ok(on, new PlaceModule(0, 0, 0));
        Ok(on, new PlaceMachine(2, 2, 1, Unloader, -1));

        var off = World(rules: false, GameMode.Campaign);
        Assert.True(off.Network.IsMachineLocked(Unloader));
        off.Network.GiveItems(0, Ingot, 5000);
        off.Network.GiveItems(0, Plate, 5000);
        Assert.Equal(Reject.Locked, Run(off, new PlaceMachine(2, 2, 1, Unloader, -1)));
    }

    [Fact]
    public void Turret_and_wall_only_inside_station()
    {
        var w = World(rules: true);
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(3, 3, 1, Turret, -1)));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(3, 3, 1, Wall, -1)));
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceMachine(3, 3, 1, Turret, -1));
        Ok(w, new PlaceMachine(4, 3, 1, Wall, -1));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(12, 3, 1, Wall, -1)));
        w.ValidateInvariants();
    }

    [Fact]
    public void Modules_are_not_gated_by_the_rules()
    {
        var w = World(rules: true);
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceModule(1, 0, 0));
        Ok(w, new PlaceModule(0, 1, 0));
        Ok(w, new RemoveModule(0, 1, 0));
    }

    private static SimWorld Fresh(GameMode mode, ulong seed, bool threats, bool rules)
    {
        var w = SimWorld.CreateGalaxy(C, mode, seed, threats, rules);
        w.Network.EnsurePlayer(0);
        return w;
    }

    private static byte[] WriteLog(SimWorld w, bool rules)
    {
        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, threats: false, stationRules: rules);
        return ms.ToArray();
    }

    [Fact]
    public void Old_log_header_reads_as_rules_off()
    {
        // A log written with the rules off has the same layout as a version 5 log: only the version byte differs.
        var w = World(rules: false);
        Ok(w, new PlaceMachine(9, 9, 9, Smelter, -1));
        byte[] bytes = WriteLog(w, rules: false);
        Assert.Equal(29, bytes[4]);
        bytes[4] = 5;
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out ulong hash, out var mode, out ulong seed, out bool threats, out bool rules);
        Assert.Equal((C.Hash, GameMode.Sandbox, Seed, false, false), (hash, mode, seed, threats, rules));
        Assert.Single(log);

        // A version 5 header cannot carry the rules flag.
        byte[] withRules = WriteLog(World(rules: true), rules: true);
        withRules[4] = 5;
        Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(new MemoryStream(withRules), out _, out _, out _, out _, out _));

        // The current version reads the flag.
        byte[] current = WriteLog(World(rules: true), rules: true);
        CommandCodec.ReadLog(new MemoryStream(current), out _, out _, out _, out bool threatsNow, out bool rulesNow);
        Assert.False(threatsNow);
        Assert.True(rulesNow);
    }

    private static SimWorld Built()
    {
        var w = World(rules: true);
        Ok(w, new PlaceModule(0, 0, 0));
        Ok(w, new PlaceMachine(1, 1, 1, Smelter, -1));
        Ok(w, new PlaceSegment(2, 1, 1, Dir.PosX, 4, BeltTier.Mk1));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(20, 1, 1, Smelter, -1)));
        w.Step(300);
        return w;
    }

    [Fact]
    public void Save_roundtrip_keeps_rules_flag()
    {
        var original = Built();
        var ms = new MemoryStream();
        SaveGame.Write(ms, original, C, GameMode.Sandbox, Seed, threats: false, player: 0, stationRules: true);

        var info = SaveGame.Peek(new MemoryStream(ms.ToArray()));
        Assert.True(info.StationRules);
        Assert.False(info.Threats);

        var loaded = SaveGame.Read(new MemoryStream(ms.ToArray()), C, out var read);
        Assert.True(read.StationRules);
        Assert.True(loaded.Network.StationRules);
        Assert.Equal(original.Checksum(), loaded.Checksum());
        // The rules still apply in the loaded world.
        Assert.Equal(Reject.OutsideStation, Run(loaded, new PlaceMachine(20, 1, 1, Smelter, -1)));

        var plain = new MemoryStream();
        SaveGame.Write(plain, World(rules: false), C, GameMode.Sandbox, Seed, threats: false, player: 0);
        Assert.False(SaveGame.Peek(new MemoryStream(plain.ToArray())).StationRules);
    }

    [Fact]
    public void Replay_with_rules_matches_checksum()
    {
        var original = Built();
        byte[] bytes = WriteLog(original, rules: true);
        var log = CommandCodec.ReadLog(new MemoryStream(bytes), out _, out var mode, out ulong seed, out bool threats, out bool rules);
        Assert.True(rules);
        var replayed = SimWorld.Replay(log, original.Tick, Fresh(mode, seed, threats, rules));
        Assert.Equal(original.Checksum(), replayed.Checksum());

        // A world created without the flag diverges: the placements are refused.
        var wrong = SimWorld.Replay(log, original.Tick, Fresh(mode, seed, threats, false));
        Assert.NotEqual(original.Checksum(), wrong.Checksum());
    }
}
