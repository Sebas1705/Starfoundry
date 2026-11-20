using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class RepairBayTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Bay = C.MachineIndex("core:repair_bay");
    private static readonly int Wall = C.MachineIndex("core:wall");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Extractor = C.MachineIndex("core:extractor");
    private static readonly int MineIron = C.RecipeIndex("core:mine_iron");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");
    private static readonly RepairerDef Def = C.Machines[Bay].Repairer!;

    private const ulong Seed = 20261120UL;

    private static SimWorld World() => SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: true);

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    private static int Hp(SimWorld w, int machine) => w.Network.GetMachineInfo(machine).Hp;
    private static int MaxHp(int type) => C.Machines[type].Hp;

    /// <summary>An ingot line (extractor, smelter, belts) along +X at depth z that ends in a repair bay; returns the bay.</summary>
    private static int BuildBayWithSupply(SimWorld w, int z, int x0 = 0)
    {
        int ex = Ok(w, new PlaceMachine(x0, 0, z, Extractor, MineIron));
        int b1 = Ok(w, new PlaceSegment(x0 + 1, 0, z, Dir.PosX, 3, BeltTier.Mk1));
        int sm = Ok(w, new PlaceMachine(x0 + 4, 0, z, Smelter, SmeltIron));
        int b2 = Ok(w, new PlaceSegment(x0 + 5, 0, z, Dir.PosX, 3, BeltTier.Mk1));
        int bay = Ok(w, new PlaceMachine(x0 + 8, 0, z, Bay, -1));
        Ok(w, new ConnectFromMachine(ex, b1));
        Ok(w, new ConnectToMachine(b1, sm));
        Ok(w, new ConnectFromMachine(sm, b2));
        Ok(w, new ConnectToMachine(b2, bay));
        return bay;
    }

    /// <summary>A lab hurt by one wave, then a wall placed far away to draw the enemies off it.</summary>
    private static int HurtLabThenDecoy(SimWorld w)
    {
        int lab = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && Hp(w, lab) > MaxHp(Lab) - 12; i++) w.Step();
        Assert.True(Hp(w, lab) < MaxHp(Lab) && w.Network.IsMachineAlive(lab));
        Ok(w, new PlaceMachine(60, 0, 0, Wall, -1)); // from now on the enemies attack the wall
        return lab;
    }

    [Fact]
    public void ABayIsALoadedQuietMachineThatTakesIngotsAndNoOutputBelts()
    {
        Assert.Equal(MachineRole.Repairer, C.Machines[Bay].Role);
        Assert.Equal(Ingot, Def.Item);
        Assert.True(C.Machines[Bay].Signature <= 1, "a bay is nearly quiet");
        Assert.True(Def.Heal * 1000 / Def.IntervalTicks * Units.TicksPerSecond / 1000 < MaxHp(Wall), "a bay heals slowly next to the damage of a wave");

        var w = World();
        int bay = Ok(w, new PlaceMachine(5, 0, 0, Bay, -1));
        int belt = Ok(w, new PlaceSegment(6, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        w.Submit(0, new ConnectFromMachine(bay, belt));
        w.Step();
        Assert.Equal(Reject.WrongRole, w.LastResults.Single().Reject);
        w.ValidateInvariants();
    }

    [Fact]
    public void AFedBayHealsTheMachinesInRangeAndUsesOneIngotPerHeal()
    {
        var w = World();
        int lab = HurtLabThenDecoy(w);
        int hurt = Hp(w, lab);
        int bay = BuildBayWithSupply(w, z: 4);
        long consumedBefore = w.Network.MachineInputsConsumed;

        for (int i = 0; i < 30 * 60 && Hp(w, lab) < MaxHp(Lab); i++) w.Step();

        Assert.Equal(MaxHp(Lab), Hp(w, lab));
        Assert.True(w.Network.AutoRepaired >= MaxHp(Lab) - hurt);
        Assert.True(w.Network.AutoRepaired <= (MaxHp(Lab) - hurt) + Def.Heal * 2, "it never heals much beyond what was missing");
        Assert.True(w.Network.MachineInputsConsumed > consumedBefore, "healing used ingots");
        w.ValidateInvariants();
        Assert.True(w.Network.IsMachineAlive(bay));
    }

    [Fact]
    public void ABayWithoutIngotsOrWithTheTargetOutOfRangeDoesNothing()
    {
        // No supply at all.
        var w = World();
        int lab = HurtLabThenDecoy(w);
        int hurt = Hp(w, lab);
        Ok(w, new PlaceMachine(3, 0, 0, Bay, -1));
        for (int i = 0; i < 600; i++) w.Step();
        Assert.Equal(hurt, Hp(w, lab));
        Assert.Equal(0, w.Network.AutoRepaired);

        // Supplied, but too far from the damaged lab.
        var far = World();
        int lab2 = HurtLabThenDecoy(far);
        int hurt2 = Hp(far, lab2);
        BuildBayWithSupply(far, z: 40);
        for (int i = 0; i < 900; i++) far.Step();
        Assert.Equal(hurt2, Hp(far, lab2));
        Assert.Equal(0, far.Network.AutoRepaired);
        far.ValidateInvariants();
    }

    [Fact]
    public void ABayDoesNotRunWithoutThreatsAndHealingIsDeterministic()
    {
        // Same inputs, same checksum, twice.
        ulong Run()
        {
            var w = World();
            HurtLabThenDecoy(w);
            BuildBayWithSupply(w, z: 4);
            for (int i = 0; i < 1500; i++) w.Step();
            return w.Checksum();
        }
        Assert.Equal(Run(), Run());

        var calm = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false);
        BuildBayWithSupply(calm, z: 4);
        for (int i = 0; i < 600; i++) calm.Step();
        Assert.Equal(0, calm.Network.AutoRepaired);
    }

    [Fact]
    public void RepairerContentIsValidated()
    {
        const string head = "{\"schema\":1,\"items\":[{\"id\":\"t:a\"}],\"machines\":[";
        const string ok = "{\"id\":\"t:r\",\"role\":\"repairer\",\"repairer\":{\"range_cells\":5,\"heal\":3,\"interval_ticks\":10,\"item\":\"t:a\",\"item_cap\":4}}";
        var c = ContentDb.FromJson(head + ok + "],\"recipes\":[]}");
        Assert.Equal(new RepairerDef(5, 3, 10, 0, 4), c.Machines[0].Repairer);
        Assert.NotEqual(c.Hash, ContentDb.FromJson(head + ok.Replace("\"heal\":3", "\"heal\":4") + "],\"recipes\":[]}").Hash);

        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + "{\"id\":\"t:r\",\"role\":\"repairer\"}],\"recipes\":[]}"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"item\":\"t:a\"", "\"item\":\"t:zz\"") + "],\"recipes\":[]}"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"role\":\"repairer\"", "\"role\":\"wall\"") + "],\"recipes\":[]}"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok.Replace("\"heal\":3", "\"heal\":0") + "],\"recipes\":[]}"));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(head + ok +
            "],\"recipes\":[{\"id\":\"t:x\",\"machine\":\"t:r\",\"inputs\":[],\"outputs\":[{\"item\":\"t:a\",\"count\":1}],\"time_s\":1}]}"));
    }
}
