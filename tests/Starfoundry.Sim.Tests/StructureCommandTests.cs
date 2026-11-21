using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class StructureCommandTests
{
    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private const ulong Seed = 20261121UL;

    private static SimWorld World(GameMode mode = GameMode.Sandbox, bool structures = false)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed, threats: false, stationRules: false, structures: structures);
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

    private static int Ship(SimWorld w) => w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);

    [Fact]
    public void Machine_allowed_only_in_matching_module_kind()
    {
        var w = World();
        int s = Ship(w);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull));
        Ok(w, new PlaceModuleIn(s, 0, 0, 1, ModuleKind.Defence));
        Ok(w, new PlaceModuleIn(s, 0, 1, 0, ModuleKind.Cargo));

        Ok(w, new PlaceMachineIn(s, 1, 1, 1, Smelter, -1));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 9, 1, 1, Smelter, -1))); // hull
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 1, 5, 1, Smelter, -1))); // cargo
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 2, 1, 2, Turret, -1))); // workshop
        Ok(w, new PlaceMachineIn(s, 1, 1, 9, Turret, -1)); // defence
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceMachineIn(s, 2, 1, 9, Smelter, -1))); // defence
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachineIn(s, 100, 1, 1, Smelter, -1))); // no module there
        Assert.Equal(Reject.OutOfBounds, Run(w, new PlaceMachineIn(s, 600, 1, 1, Smelter, -1)));
        Assert.Equal(2, w.Network.MachineCount);
        w.ValidateInvariants();
    }

    [Fact]
    public void Module_adjacent_to_existing_only()
    {
        var w = World();
        int s = Ship(w);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModuleIn(s, 2, 0, 0, ModuleKind.Hull)));
        Assert.Equal(Reject.NotAdjacent, Run(w, new PlaceModuleIn(s, 1, 1, 0, ModuleKind.Hull))); // diagonal
        Assert.Equal(Reject.Overlap, Run(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Hull)));
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Core))); // only the first can be the core
        Assert.Equal(Reject.OutOfBounds, Run(w, new PlaceModuleIn(s, 1000, 0, 0, ModuleKind.Hull)));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull));
        Ok(w, new PlaceModuleIn(s, 1, -1, 0, ModuleKind.Hull));
        Assert.Equal(3, w.Network.GetStructure(s).Modules.Count);

        // A station starts with its core.
        int st = w.Network.CreateStructure(0, StructureKind.Station, 20_000, 0, 0);
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceModuleIn(st, 0, 0, 0, ModuleKind.Hull)));
        Ok(w, new PlaceModuleIn(st, 0, 0, 0, ModuleKind.Core));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new PlaceModuleIn(Foreign(w), 0, 0, 0, ModuleKind.Core))); // not the player's
    }

    private static int Foreign(SimWorld w) => w.Network.CreateStructure(7, StructureKind.Ship, 0, 0, 0);

    [Fact]
    public void Module_cost_taken_from_hold()
    {
        var w = World(GameMode.Campaign);
        int s = Ship(w);
        Assert.Equal(Reject.NotEnoughItems, Run(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop)));
        Assert.Empty(w.Network.GetStructure(s).Modules);

        var workshop = C.Structures!.ModuleCost(ModuleKind.Workshop);
        var hull = C.Structures!.ModuleCost(ModuleKind.Hull);
        w.Network.GiveItems(0, Ingot, workshop[0].Count + hull[0].Count);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Assert.Equal(hull[0].Count, w.Network.HoldCount(0, Ingot));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull));
        Assert.Equal(0, w.Network.HoldCount(0, Ingot));
        Assert.Equal(Reject.NotEnoughItems, Run(w, new PlaceModuleIn(s, 2, 0, 0, ModuleKind.Hull)));
        Assert.Equal(2, w.Network.GetStructure(s).Modules.Count);
    }

    [Fact]
    public void Remove_refused_when_occupied_or_disconnected()
    {
        var w = World();
        int s = Ship(w);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceModuleIn(s, 2, 0, 0, ModuleKind.Hull));
        Ok(w, new PlaceMachineIn(s, 9, 1, 1, Smelter, -1));

        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModuleIn(s, 1, 0, 0))); // holds a smelter and links the ends
        Assert.Equal(Reject.OutsideStation, Run(w, new RemoveModuleIn(s, 5, 0, 0)));
        Ok(w, new PlaceSegmentIn(s, 0, 3, 0, Dir.PosX, 8, BeltTier.Mk1));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModuleIn(s, 0, 0, 0))); // a belt lies inside

        // Empty the middle module: it still joins the other two, so it stays.
        Ok(w, new RemoveMachine(0));
        Assert.Equal(Reject.ModuleOccupied, Run(w, new RemoveModuleIn(s, 1, 0, 0))); // would split the ship
        Ok(w, new RemoveModuleIn(s, 2, 0, 0)); // the end one is free to go
        w.ValidateInvariants();
    }

    [Fact]
    public void Remove_refunds_half()
    {
        var w = World(GameMode.Campaign);
        int s = Ship(w);
        var workshop = C.Structures!.ModuleCost(ModuleKind.Workshop);
        var hull = C.Structures!.ModuleCost(ModuleKind.Hull);
        w.Network.GiveItems(0, Ingot, workshop[0].Count + hull[0].Count);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull));
        Assert.Equal(0, w.Network.HoldCount(0, Ingot));
        Ok(w, new RemoveModuleIn(s, 1, 0, 0));
        Assert.Equal(hull[0].Count / 2, w.Network.HoldCount(0, Ingot));
        Assert.Single(w.Network.GetStructure(s).Modules);
    }

    [Fact]
    public void Belt_must_lie_inside_modules_of_its_structure()
    {
        var w = World();
        int s = Ship(w);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull));
        Ok(w, new PlaceModuleIn(s, 0, 0, 1, ModuleKind.Cargo));

        Ok(w, new PlaceSegmentIn(s, 0, 0, 0, Dir.PosX, 16, BeltTier.Mk1)); // workshop into hull
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegmentIn(s, -1, 1, 1, Dir.PosX, 4, BeltTier.Mk1)));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegmentIn(s, 10, 1, 1, Dir.PosX, 8, BeltTier.Mk1))); // runs out of the hull
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegmentIn(s, 2, 3, 2, Dir.PosY, 3, BeltTier.Mk1))); // above the module
        Assert.Equal(Reject.KindNotAllowed, Run(w, new PlaceSegmentIn(s, 0, 1, 7, Dir.PosZ, 3, BeltTier.Mk1))); // into the cargo module
        Assert.Equal(Reject.Overlap, Run(w, new PlaceSegmentIn(s, 3, 0, 0, Dir.PosX, 2, BeltTier.Mk1)));
        Assert.Equal(Reject.InvalidArgument, Run(w, new PlaceSegmentIn(s, 0, 1, 1, Dir.PosX, 0, BeltTier.Mk1)));

        // Another structure has no modules there.
        int other = Ship(w);
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegmentIn(other, 0, 0, 0, Dir.PosX, 4, BeltTier.Mk1)));
        Assert.Equal(1, w.Network.SegmentCount);
        w.ValidateInvariants();
    }

    [Fact]
    public void Same_cell_in_two_structures_both_accepted()
    {
        var w = World();
        int a = Ship(w), b = Ship(w);
        foreach (int s in new[] { a, b })
        {
            Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
            Ok(w, new PlaceMachineIn(s, 2, 1, 2, Smelter, -1));
            Ok(w, new PlaceSegmentIn(s, 0, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        }
        Assert.Equal(2, w.Network.MachineCount);
        Assert.Equal(2, w.Network.SegmentCount);
        var (ka, ia) = w.EntityAt(a, 2, 1, 2);
        var (kb, ib) = w.EntityAt(b, 2, 1, 2);
        Assert.Equal(SimWorld.EntityKind.Machine, ka);
        Assert.Equal(SimWorld.EntityKind.Machine, kb);
        Assert.NotEqual(ia, ib);
        Assert.Equal(Reject.Overlap, Run(w, new PlaceMachineIn(a, 2, 1, 2, Smelter, -1)));
        w.ValidateInvariants();
    }

    [Fact]
    public void Unknown_structure_rejected()
    {
        var w = World();
        Assert.Equal(Reject.NoSuchStructure, Run(w, new PlaceMachineIn(5, 0, 0, 0, Smelter, -1)));
        Assert.Equal(Reject.NoSuchStructure, Run(w, new PlaceSegmentIn(5, 0, 0, 0, Dir.PosX, 2, BeltTier.Mk1)));
        Assert.Equal(Reject.NoSuchStructure, Run(w, new PlaceModuleIn(5, 0, 0, 0, ModuleKind.Core)));
        Assert.Equal(Reject.NoSuchStructure, Run(w, new RemoveModuleIn(5, 0, 0, 0)));
        Assert.Equal(Reject.NoSuchStructure, Run(w, new PlaceModuleIn(0, 0, 0, 0, ModuleKind.Core))); // the legacy world has no modules
        Assert.Equal(Reject.NoSuchStructure, Run(w, new PlaceModuleIn(-1, 0, 0, 0, ModuleKind.Core)));
        Assert.Equal(0, w.Network.MachineCount);
    }

    [Fact]
    public void Old_commands_still_target_structure_zero()
    {
        var w = World();
        int s = Ship(w);
        Ok(w, new PlaceMachine(2, 0, 2, Smelter, -1));
        Ok(w, new PlaceSegment(5, 0, 5, Dir.PosX, 3, BeltTier.Mk1));
        Ok(w, new PlaceMachineIn(0, 9, 0, 9, Smelter, -1));
        Ok(w, new PlaceSegmentIn(0, 5, 0, 7, Dir.PosX, 3, BeltTier.Mk1));
        Assert.Equal((SimWorld.EntityKind.Machine, 0), w.EntityAt(2, 0, 2));
        Assert.Equal((SimWorld.EntityKind.Machine, 1), w.EntityAt(0, 9, 0, 9));
        Assert.Equal((SimWorld.EntityKind.None, -1), w.EntityAt(s, 2, 0, 2));
        for (int id = 0; id < w.Network.MachineIdLimit; id++) Assert.Equal(0, w.Network.GetMachineInfo(id).Structure);
        w.ValidateInvariants();
    }

    [Fact]
    public void Legacy_placement_rejected_when_structure_rules_on()
    {
        var w = World(structures: true);
        Assert.True(w.Network.StructureRules);
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachine(2, 0, 2, Smelter, -1)));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegment(0, 0, 0, Dir.PosX, 3, BeltTier.Mk1)));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceMachineIn(0, 2, 0, 2, Smelter, -1)));
        Assert.Equal(Reject.OutsideStation, Run(w, new PlaceSegmentIn(0, 0, 0, 0, Dir.PosX, 3, BeltTier.Mk1)));
        Assert.Equal(Reject.NotAllowedHere, Run(w, new PlaceModule(0, 0, 0)));
        Assert.Equal(0, w.Network.MachineCount);
        Assert.Equal(0, w.Network.SegmentCount);

        // Structures still work, and a ship is always within build range.
        int s = Ship(w);
        Ok(w, new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop));
        Ok(w, new PlaceMachineIn(s, 2, 1, 2, Smelter, -1));

        // A station far from the ship is out of range; one close by is not.
        w.Network.EnsurePlayer(0);
        int far = w.Network.CreateStructure(0, StructureKind.Station, 1_000_000, 0, 0);
        Assert.Equal(Reject.OutOfRange, Run(w, new PlaceModuleIn(far, 0, 0, 0, ModuleKind.Core)));
        int near = w.Network.CreateStructure(0, StructureKind.Station, -10L * Units.CellUnits, 0, 0);
        Ok(w, new PlaceModuleIn(near, 0, 0, 0, ModuleKind.Core));
        w.ValidateInvariants();
    }

    [Fact]
    public void Header_flag_roundtrip_and_old_headers_read_as_off()
    {
        foreach (bool structures in new[] { false, true })
            foreach (bool station in new[] { false, true })
            {
                using var ms = new MemoryStream();
                CommandCodec.WriteLog(ms, new List<LoggedCommand>(), 77, GameMode.Campaign, 5, threats: true, stationRules: station, structures: structures);
                ms.Position = 0;
                CommandCodec.ReadLog(ms, out ulong hash, out var mode, out ulong seed, out bool threats, out bool st, out bool sr);
                Assert.Equal((77UL, GameMode.Campaign, 5UL, true, station, structures), (hash, mode, seed, threats, st, sr));
            }

        // Older headers (version 5, and version 6 logs written before the flag) carry no structures bit and read as off.
        foreach (byte version in new byte[] { 5, 6 })
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("SFCL"));
                w.Write(version);
                w.Write(9UL); w.Write((byte)0); w.Write(3UL);
                w.Write((byte)(version == 5 ? 1 : 3));
                w.Write(0);
            }
            ms.Position = 0;
            CommandCodec.ReadLog(ms, out _, out _, out _, out bool threats, out bool stationRules, out bool structures);
            Assert.True(threats);
            Assert.Equal(version == 6, stationRules);
            Assert.False(structures);
        }

        // Version 5 cannot carry the structures bit, and bit 3 is unknown always.
        foreach (var (version, flags) in new[] { ((byte)5, (byte)4), ((byte)6, (byte)8) })
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("SFCL"));
                w.Write(version);
                w.Write(9UL); w.Write((byte)0); w.Write(3UL); w.Write(flags); w.Write(0);
            }
            ms.Position = 0;
            Assert.Throws<InvalidDataException>(() => CommandCodec.ReadLog(ms, out _, out _, out _, out _, out _, out _));
        }
    }

    private static List<Command> SampleCommands(int s) => new()
    {
        new PlaceModuleIn(s, 0, 0, 0, ModuleKind.Workshop),
        new PlaceModuleIn(s, 1, 0, 0, ModuleKind.Hull),
        new PlaceMachineIn(s, 2, 1, 2, Smelter, -1),
        new PlaceSegmentIn(s, 0, 0, 0, Dir.PosX, 8, BeltTier.Mk1),
        new RemoveModuleIn(s, 1, 0, 0),
        new PlaceMachineIn(9, 0, 0, 0, Smelter, -1),
    };

    [Fact]
    public void Replay_and_save_roundtrip_with_structure_commands()
    {
        // Codec round trip of every new command, alone and inside a batch.
        foreach (var c in SampleCommands(1))
            Assert.Equal(c, CommandCodec.FromBytes(CommandCodec.ToBytes(c)));
        var batch = new Batch(SampleCommands(1).ToArray());
        Assert.Equal(batch.Commands, ((Batch)CommandCodec.FromBytes(CommandCodec.ToBytes(batch))).Commands);

        SimWorld Fresh()
        {
            var w = World(structures: true);
            Assert.Equal(1, Ship(w));
            return w;
        }

        var a = Fresh();
        foreach (var c in SampleCommands(1)) a.Submit(0, c);
        a.Step();
        Assert.Equal(new[] { Reject.None, Reject.None, Reject.None, Reject.None, Reject.None, Reject.NoSuchStructure },
            a.LastResults.Select(r => r.Reject).ToArray());
        a.Step(30);
        a.ValidateInvariants();

        // Replaying the log into an identically prepared world gives the same checksum.
        using var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, a.Log, C.Hash, GameMode.Sandbox, Seed, structures: true);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out _, out _, out _, out _, out bool structures);
        Assert.True(structures);
        Assert.Equal(a.Log.Count, log.Count);
        var b = SimWorld.Replay(log, a.Tick, Fresh());
        Assert.Equal(a.Checksum(), b.Checksum());

        // A save keeps the flag. The structure itself is setup, not a command, so the loaded world rejects the commands
        // the same way the original does when it lacks the structure.
        var plain = World(structures: true);
        foreach (var c in SampleCommands(1)) plain.Submit(0, c);
        plain.Step(5);
        using var save = new MemoryStream();
        SaveGame.Write(save, plain, C, GameMode.Sandbox, Seed, threats: false, player: 0, stationRules: false, structures: true);
        save.Position = 0;
        Assert.True(SaveGame.Peek(save).Structures);
        save.Position = 0;
        var loaded = SaveGame.Read(save, C, out var info);
        Assert.True(info.Structures);
        Assert.True(loaded.Network.StructureRules);
        Assert.Equal(plain.Checksum(), loaded.Checksum());
    }

    [Fact]
    public void Checksum_changes_with_a_module_kind()
    {
        SimWorld With(ModuleKind kind)
        {
            var w = World();
            int s = Ship(w);
            Ok(w, new PlaceModuleIn(s, 0, 0, 0, kind));
            return w;
        }
        var workshop = With(ModuleKind.Workshop);
        Assert.Equal(workshop.Checksum(), With(ModuleKind.Workshop).Checksum());
        Assert.NotEqual(workshop.Checksum(), With(ModuleKind.Hull).Checksum());

        // The structures setting is part of the world too.
        Assert.NotEqual(World().Checksum(), World(structures: true).Checksum());
    }
}
