using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class WallTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly ThreatRules Rules = C.Threat!;
    private static readonly int Wall = C.MachineIndex("core:wall");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly int PlateItem = C.ItemIndex("core:iron_plate");

    private const ulong Seed = 20261120UL;

    private static SimWorld World(GameMode mode = GameMode.Sandbox) => SimWorld.CreateGalaxy(C, mode, Seed, threats: true);

    private static CommandResult Run(SimWorld w, Command c, int player = 0)
    {
        w.Submit(player, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int Ok(SimWorld w, Command c, int player = 0)
    {
        var r = Run(w, c, player);
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    private static int Hp(SimWorld w, int machine) => w.Network.GetMachineInfo(machine).Hp;
    private static int MaxHp(int type) => C.Machines[type].Hp;

    /// <summary>Lets a wave of scouts reach a lab until it is hurt but still standing; returns the lab.</summary>
    private static int HurtLab(SimWorld w, int x = 0)
    {
        int lab = Ok(w, new PlaceMachine(x, 0, 0, Lab, -1));
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && Hp(w, lab) >= MaxHp(Lab); i++) w.Step();
        Assert.True(Hp(w, lab) < MaxHp(Lab) && w.Network.IsMachineAlive(lab), "the lab should be hurt but alive");
        return lab;
    }

    // ---- walls ------------------------------------------------------------------------------------

    [Fact]
    public void AWallIsToughQuietAndTakesNoBelts()
    {
        var def = C.Machines[Wall];
        Assert.Equal(MachineRole.Wall, def.Role);
        Assert.Equal(0, def.Signature);
        Assert.True(def.Hp > MaxHp(Lab) * 3, "a wall should take far more punishment than a machine");

        var w = World();
        int wall = Ok(w, new PlaceMachine(5, 0, 0, Wall, -1));
        int belt = Ok(w, new PlaceSegment(2, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        Assert.Equal(Reject.WrongRole, Run(w, new ConnectToMachine(belt, wall)).Reject);
        int belt2 = Ok(w, new PlaceSegment(6, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        Assert.Equal(Reject.WrongRole, Run(w, new ConnectFromMachine(wall, belt2)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void WallsHaveNoRecipes()
    {
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(
            "{\"schema\":1,\"items\":[{\"id\":\"t:a\"}],\"machines\":[{\"id\":\"t:w\",\"role\":\"wall\"}]," +
            "\"recipes\":[{\"id\":\"t:r\",\"machine\":\"t:w\",\"inputs\":[],\"outputs\":[{\"item\":\"t:a\",\"count\":1}],\"time_s\":1}]}"));
    }

    [Fact]
    public void EnemiesAttackTheNearestWallBeforeAnyMachine()
    {
        var w = World();
        int lab = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));      // the loudest machine, normally the target
        int smelter = Ok(w, new PlaceMachine(0, 0, 3, Smelter, SmeltIron));
        int wall = Ok(w, new PlaceMachine(30, 0, 0, Wall, -1));   // far from them, but a wall
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && Hp(w, wall) >= MaxHp(Wall); i++) w.Step();

        Assert.True(Hp(w, wall) < MaxHp(Wall), "the wall was attacked");
        Assert.Equal(MaxHp(Lab), Hp(w, lab));
        Assert.Equal(MaxHp(Smelter), Hp(w, smelter));
        w.ValidateInvariants();
    }

    [Fact]
    public void WithSeveralWallsEnemiesTakeTheOneNearestToThem()
    {
        var w = World();
        int west = Ok(w, new PlaceMachine(-40, 0, 0, Wall, -1));
        int east = Ok(w, new PlaceMachine(40, 0, 0, Wall, -1));
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && Hp(w, west) >= MaxHp(Wall) && Hp(w, east) >= MaxHp(Wall); i++) w.Step();

        bool westHit = Hp(w, west) < MaxHp(Wall), eastHit = Hp(w, east) < MaxHp(Wall);
        Assert.True(westHit ^ eastHit, "enemies of one wave go for a single wall, the nearest to them");
    }

    [Fact]
    public void ADestroyedWallIsCountedApartAndFreesItsCell()
    {
        var w = World();
        int wall = Ok(w, new PlaceMachine(5, 0, 0, Wall, -1));
        for (int i = 0; i < 8; i++) { w.Network.ScheduleWave(0); w.Step(Rules.WarningTicks); } // a crowd, far more than one wall holds
        for (int i = 0; i < 30_000 && w.Network.IsMachineAlive(wall); i++) w.Step();

        Assert.False(w.Network.IsMachineAlive(wall));
        Assert.Equal(1, w.Network.WallsLost);
        Assert.Equal(0, w.Network.MachinesLost);
        Ok(w, new PlaceMachine(5, 0, 0, Wall, -1)); // the cell is free again
        w.ValidateInvariants();
    }

    [Fact]
    public void WithoutWallsEnemiesKeepTargetingTheLoudestMachine()
    {
        var w = World();
        int lab = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        int smelter = Ok(w, new PlaceMachine(0, 0, 3, Smelter, SmeltIron));
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && Hp(w, lab) >= MaxHp(Lab); i++) w.Step();
        Assert.True(Hp(w, lab) < MaxHp(Lab));
        Assert.Equal(MaxHp(Smelter), Hp(w, smelter));
    }

    // ---- repairs ----------------------------------------------------------------------------------

    [Fact]
    public void RepairRestoresAllHitPointsAndAnUndamagedMachineIsRefused()
    {
        var w = World(); // sandbox: free
        int quiet = Ok(w, new PlaceMachine(0, 0, 10, Smelter, SmeltIron)); // never targeted while the lab is the loudest
        int lab = HurtLab(w);
        for (int i = 0; i < 2_000 && Hp(w, lab) > MaxHp(Lab) / 2; i++) w.Step(); // let the scouts hurt it properly
        int hurt = Hp(w, lab);
        Assert.Equal(Reject.None, Run(w, new Repair(lab)).Reject);
        Assert.True(Hp(w, lab) > hurt, "hit points went up (the scouts may land a hit in the same tick)");
        Assert.Equal(Reject.NotDamaged, Run(w, new Repair(quiet)).Reject);
        Assert.Equal(Reject.UnknownMachine, Run(w, new Repair(99)).Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void ACampaignRepairCostsHalfTheBuildCostScaledByTheDamageAndAtLeastOneOfEach()
    {
        var w = World(GameMode.Campaign);
        w.Network.GiveItems(0, Ingot, 100);
        w.Network.GiveItems(0, PlateItem, 100);
        int lab = HurtLab(w);

        int missing = MaxHp(Lab) - Hp(w, lab);
        var cost = w.Network.RepairCost(lab);
        Assert.Equal(C.Machines[Lab].Cost.Length, cost.Length);
        foreach (var stack in C.Machines[Lab].Cost)
        {
            int expected = Math.Max(1, (int)Math.Ceiling(stack.Count * missing / (2.0 * MaxHp(Lab))));
            Assert.Equal(expected, cost.Single(c => c.Item == stack.Item).Count);
        }

        int ingotsBefore = w.Network.HoldCount(0, Ingot), platesBefore = w.Network.HoldCount(0, PlateItem);
        Assert.Equal(Reject.None, Run(w, new Repair(lab)).Reject);
        Assert.Equal(ingotsBefore - cost.Single(c => c.Item == Ingot).Count, w.Network.HoldCount(0, Ingot));
        Assert.Equal(platesBefore - cost.Single(c => c.Item == PlateItem).Count, w.Network.HoldCount(0, PlateItem));
        Assert.Equal(MaxHp(Lab), Hp(w, lab));
        w.ValidateInvariants();
    }

    [Fact]
    public void ARepairNeedsTheItemsAndTheShipNearby()
    {
        var w = World(GameMode.Campaign);
        foreach (var stack in C.Machines[Lab].Cost) w.Network.GiveItems(0, stack.Item, stack.Count); // exactly the price of the lab
        int lab = HurtLab(w);
        for (int i = 0; i < 2_000 && Hp(w, lab) > MaxHp(Lab) / 2; i++) w.Step(); // let the scouts hurt it properly
        int hurt = Hp(w, lab);

        // The hold is empty: refused, nothing changes.
        Assert.Equal(Reject.NotEnoughItems, Run(w, new Repair(lab)).Reject);
        Assert.Equal(hurt, Hp(w, lab));

        // Items but too far: refused.
        w.Network.GiveItems(0, Ingot, 100);
        w.Network.GiveItems(0, PlateItem, 100);
        Ok(w, new PlaceMachine(10, 0, 0, Wall, -1)); // from now on the enemies go for the wall and leave the lab alone
        Ok(w, new SetMovement(1, 0, 0));
        w.Step(30 * 12); // about 70 cells away, beyond the building range
        Ok(w, new SetMovement(0, 0, 0));
        Assert.Equal(Reject.OutOfRange, Run(w, new Repair(lab)).Reject);
    }

    [Fact]
    public void RepairSurvivesTheCommandLog()
    {
        var c = new Repair(7);
        Assert.Equal(c, CommandCodec.FromBytes(CommandCodec.ToBytes(c)));
        Assert.Equal(c, ((Batch)CommandCodec.FromBytes(CommandCodec.ToBytes(new Batch(new Command[] { c })))).Commands[0]);
    }

    [Fact]
    public void RepairsReplayExactly()
    {
        var w = World();
        w.Submit(0, new PlaceMachine(0, 0, 0, Lab, -1));
        w.Step();
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 400; i++) w.Step();
        w.Submit(0, new Repair(0));
        w.Step();
        for (int i = 0; i < 400; i++) w.Step();

        var again = World();
        again.Submit(0, new PlaceMachine(0, 0, 0, Lab, -1));
        again.Step();
        again.Network.ScheduleWave(0);
        for (int i = 0; i < 400; i++) again.Step();
        again.Submit(0, new Repair(0));
        again.Step();
        for (int i = 0; i < 400; i++) again.Step();
        Assert.Equal(w.Checksum(), again.Checksum());
    }
}
