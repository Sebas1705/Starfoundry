using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Deposit density: how rich a vein is scales hand mining time and drill yield, and stays out of the hash at 100.</summary>
public class DensityTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Ore = C.ItemIndex("core:iron_ore");
    private static readonly int Drill = C.MachineIndex("core:drill");
    private static readonly int DrillIron = C.RecipeIndex("core:drill_iron");

    private static SimWorld World(int density, GameMode mode = GameMode.Sandbox, int amount = 5000)
    {
        var w = new SimWorld(C, mode);
        w.Network.AddDeposit(Ore, amount, density: density);
        return w;
    }

    private static CommandResult Run(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        return w.LastResults.Single();
    }

    private static int Mined(SimWorld w, int ticks)
    {
        Assert.True(Run(w, new StartMining(0)).Ok);
        w.Step(ticks - 1);
        return (int)w.Network.MinedByHand;
    }

    [Fact]
    public void DensityDefaultsToOneHundredAndIsExposed()
    {
        var w = new SimWorld(C, GameMode.Sandbox);
        int d = w.Network.AddDeposit(Ore, 100);
        Assert.Equal(100, w.Network.DepositDensity(d));
        var info = w.Network.GetDepositInfo(d);
        Assert.Equal((d, Ore, 100, 100, 100), (info.Id, info.Item, info.Initial, info.Remaining, info.Density));
        Assert.True(info.Revealed);
        Assert.Equal(15, w.Network.MineTicksFor(d));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(201)]
    [InlineData(0)]
    public void DensityOutsideTheRangeIsRefused(int density)
    {
        var w = new SimWorld(C, GameMode.Sandbox);
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Network.AddDeposit(Ore, 100, density: density));
    }

    [Fact]
    public void DensityOneHundredMinesAtTheOldRate()
    {
        var w = World(100);
        Assert.Equal(20, Mined(w, 300)); // the same figure the base-rate loop test pins
        Assert.Equal(PlayerRules.MineTicksPerItem, w.Network.MineTicksFor(0));
        w.ValidateInvariants();
    }

    [Fact]
    public void ADenserVeinMinesFasterAndASparserOneSlower()
    {
        var rich = World(200);
        Assert.Equal(7, rich.Network.MineTicksFor(0)); // 15 * 100 / 200, integer
        Assert.Equal(300 / 7, Mined(rich, 300));
        rich.ValidateInvariants();

        var poor = World(50);
        Assert.Equal(30, poor.Network.MineTicksFor(0)); // exactly half the speed
        Assert.Equal(10, Mined(poor, 300));
        poor.ValidateInvariants();

        var poorest = World(25);
        Assert.Equal(60, poorest.Network.MineTicksFor(0));
        Assert.Equal(5, Mined(poorest, 300));
        poorest.ValidateInvariants();
    }

    [Fact]
    public void MiningTimeNeverDropsBelowOneTick()
    {
        Assert.Equal(1, DepositRules.Scale(1, 200));
        Assert.Equal(1, DepositRules.Scale(15, 10000));
        Assert.Equal(7, DepositRules.Scale(15, 200));
        Assert.Equal(60, DepositRules.Scale(15, 25));
    }

    [Fact]
    public void HandMiningRespectsTheHoldLimitAtAnyDensity()
    {
        var w = World(200, GameMode.Campaign);
        Assert.True(Run(w, new StartMining(0)).Ok);
        w.Step(PlayerRules.HoldCapacity * 8 + 100);
        Assert.Equal(PlayerRules.HoldCapacity, w.Network.HoldTotal(0));
        w.ValidateInvariants();
    }

    /// <summary>Places a drill on the deposit and returns the time its first craft was set to.</summary>
    private static int FirstCraftTime(int density)
    {
        var w = World(density);
        w.Submit(0, new PlaceMachine(0, 0, 0, Drill, DrillIron));
        w.Step();
        int drill = w.LastResults.Single().CreatedMachines.Single();
        w.Submit(0, new LinkDeposit(drill, 0));
        for (int i = 0; i < 5; i++)
        {
            w.Step();
            var info = w.Network.GetMachineInfo(drill);
            if (info.Crafting)
            {
                w.ValidateInvariants();
                return info.Remaining;
            }
        }
        throw new Xunit.Sdk.XunitException("the drill never started");
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(150)]
    [InlineData(200)]
    public void DrillCraftTimeScalesByDensity(int density)
    {
        int expected = DepositRules.Scale(C.Recipes[DrillIron].TimeTicks, density);
        // The craft starts and ticks once in the same step, so the first reading is the scaled time or one less.
        Assert.InRange(FirstCraftTime(density), expected - 1, expected);
    }

    [Fact]
    public void DrillYieldOnABeltToASinkScalesWithDensity()
    {
        // drill -> belt -> sink never blocks, so the items drilled in a minute follow 100 / density of the craft time.
        long Drilled(int density)
        {
            var w = World(density);
            var r = Run(w, new Batch(new Command[]
            {
                new PlaceMachine(0, 0, 0, Drill, DrillIron),
                new PlaceSegment(1, 0, 0, Dir.PosX, 4, BeltTier.Mk1),
                new LinkDeposit(-1, 0),
                new ConnectFromMachine(-1, -1),
                new SetSink(-1),
            }));
            Assert.True(r.Ok, r.Reject.ToString());
            w.Step(30 * 60);
            w.ValidateInvariants();
            return w.Network.DrilledTotal;
        }

        long slow = Drilled(50), normal = Drilled(100), fast = Drilled(200);
        Assert.InRange(slow, 14, 16);   // 120 ticks a craft: 15 in a minute
        Assert.InRange(normal, 29, 31); // 60 ticks a craft
        Assert.InRange(fast, 59, 61);   // 30 ticks a craft
    }

    [Fact]
    public void DensityIsHashedOnlyWhenItDiffersFromOneHundred()
    {
        var a = World(100);
        var b = new SimWorld(C, GameMode.Sandbox);
        b.Network.AddDeposit(Ore, 5000); // default density
        Assert.Equal(a.Checksum(), b.Checksum());

        var c = World(150);
        Assert.NotEqual(a.Checksum(), c.Checksum());
        var d = World(160);
        Assert.NotEqual(c.Checksum(), d.Checksum());
    }

    [Fact]
    public void ClonePreservesDensity()
    {
        var w = World(135);
        var clone = w.Network.Clone();
        Assert.Equal(135, clone.DepositDensity(0));
        Assert.Equal(w.Network.Checksum(), clone.Checksum());
    }

    [Fact]
    public void AMidMineSaveStaysValidAfterTheDensityChangesTheTarget()
    {
        // Progress built up on a sparse vein is a valid state; switching to a richer one resets it.
        var w = new SimWorld(C, GameMode.Sandbox);
        w.Network.AddDeposit(Ore, 100, density: 25);
        w.Network.AddDeposit(Ore, 100, density: 200);
        Assert.True(Run(w, new StartMining(0)).Ok);
        w.Step(50);
        w.ValidateInvariants();
        Assert.True(Run(w, new StartMining(1)).Ok);
        w.Step(30);
        w.ValidateInvariants();
    }

    // ---- generation --------------------------------------------------------------------------------

    private const string LegacyContent = """
        {"schema":1,"items":[{"id":"t:a"},{"id":"t:b"}],"machines":[{"id":"t:m"}],
         "recipes":[{"id":"t:r","machine":"t:m","inputs":[{"item":"t:a","count":1}],"outputs":[{"item":"t:b","count":1}],"time_s":1}],
         "galaxy":{"system_count":8,"spacing_cells":800,"lanes_per_system":2,"system_radius_cells":150},
         "archetypes":[
           {"id":"t:home","home":true,"weight":0,"distance_cells":[5,10],"deposits":[{"item":"t:a","count":[1,1],"amount":[100,100]}]},
           {"id":"t:belt","weight":2,"distance_cells":[10,40],"deposits":[{"item":"t:a","count":[1,3],"amount":[50,200]}]},
           {"id":"t:rich","weight":1,"distance_cells":[10,40],"deposits":[{"item":"t:b","count":[2,3],"amount":[50,200]}]}]}
        """;

    [Fact]
    public void ContentWithoutDensityGeneratesTheLegacyGalaxy()
    {
        var content = ContentDb.FromJson(LegacyContent);
        var g = Galaxy.Generate(content, 4242);
        Assert.All(g.Deposits, d => Assert.Equal(100, d.Density));
        // Pinned from the generator before densities existed: positions and amounts must not move.
        ulong h = 14695981039346656037UL;
        void Mix(long v) { for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= 1099511628211UL; } }
        foreach (var s in g.Systems) { Mix(s.X); Mix(s.Y); Mix(s.Z); Mix(s.Archetype); foreach (int l in s.Lanes) Mix(l); }
        foreach (var d in g.Deposits) { Mix(d.System); Mix(d.Item); Mix(d.Amount); Mix(d.X); Mix(d.Y); Mix(d.Z); }
        Assert.Equal(LegacyGalaxyHash, h);
    }

    [Fact]
    public void ALegacyGalaxyWorldChecksumIsUnchanged()
    {
        var content = ContentDb.FromJson(LegacyContent);
        var w = SimWorld.CreateGalaxy(content, GameMode.Sandbox, 4242);
        w.Step(100);
        Assert.Equal(LegacyWorldChecksum, w.Checksum());
    }

    private const ulong LegacyGalaxyHash = 18314980229807682679UL;
    private const ulong LegacyWorldChecksum = 8434274401388376860UL;

    private static ContentDb WithDensity(string density) =>
        ContentDb.FromJson(LegacyContent.Replace(
            "{\"id\":\"t:belt\",\"weight\":2,\"distance_cells\":[10,40],\"deposits\":[{\"item\":\"t:a\",\"count\":[1,3],\"amount\":[50,200]}]}",
            "{\"id\":\"t:belt\",\"weight\":2,\"distance_cells\":[10,40],\"deposits\":[{\"item\":\"t:a\",\"count\":[1,3],\"amount\":[50,200]" + density + "}]}"));

    [Fact]
    public void ADensityRangeIsDrawnDeterministicallyInsideTheRange()
    {
        var content = WithDensity(",\"density\":[60,140]");
        var a = Galaxy.Generate(content, 77);
        var b = Galaxy.Generate(content, 77);
        Assert.Equal(a.Deposits.Select(d => d.Density), b.Deposits.Select(d => d.Density));
        Assert.Contains(a.Deposits, d => d.Density != 100);
        Assert.All(a.Deposits, d => Assert.InRange(d.Density, 60, 140));
    }

    [Fact]
    public void ADensityRangeInfluencesOnlyTheSpecThatDeclaresIt()
    {
        var plain = Galaxy.Generate(ContentDb.FromJson(LegacyContent), 77);
        var ranged = Galaxy.Generate(WithDensity(",\"density\":[60,140]"), 77);
        Assert.Equal(plain.Systems.Count, ranged.Systems.Count);
        // Systems and lanes come before any deposit draw, so adding a density to one spec leaves them untouched.
        Assert.Equal(plain.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)), ranged.Systems.Select(s => (s.X, s.Y, s.Z, s.Archetype)));
        Assert.Equal(plain.Systems.Select(s => s.Lanes), ranged.Systems.Select(s => s.Lanes), new LaneComparer());
    }

    private sealed class LaneComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[]? x, int[]? y) => x!.SequenceEqual(y!);
        public int GetHashCode(int[] o) => o.Length;
    }

    [Theory]
    [InlineData(",\"density\":[24,100]")]
    [InlineData(",\"density\":[100,201]")]
    [InlineData(",\"density\":[120,100]")]
    [InlineData(",\"density\":[100]")]
    public void InvalidDensityRangesAreRefused(string density)
    {
        Assert.Throws<InvalidDataException>(() => WithDensity(density));
    }

    [Fact]
    public void ADensityGalaxyWorldCarriesTheDensitiesIntoTheDeposits()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, 20261120UL);
        var g = w.Network.Galaxy!;
        for (int i = 0; i < g.Deposits.Count; i++) Assert.Equal(g.Deposits[i].Density, w.Network.DepositDensity(i));
        Assert.Contains(g.Deposits, d => d.Density != 100);
        w.ValidateInvariants();
    }

    [Fact]
    public void ReplayAndSaveRoundTripWithADensityDeposit()
    {
        const ulong seed = 20261120UL;
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, seed);
        w.Network.EnsurePlayer(0);
        int ice = Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == C.ItemIndex("core:ice") && w.Network.IsDepositRevealed(d));
        Assert.NotEqual(100, w.Network.DepositDensity(ice));
        var (x, _, z) = w.Network.DepositPosition(ice);
        for (int i = 0; i < 30 * 30 && !w.Network.InMineRange(0, ice); i++)
        {
            long px = w.Network.GetPlayerInfo(0).X, pz = w.Network.GetPlayerInfo(0).Z;
            int dx = Math.Abs(x * Units.CellUnits - px) > 120 ? Math.Sign(x * Units.CellUnits - px) : 0;
            int dz = Math.Abs(z * Units.CellUnits - pz) > 120 ? Math.Sign(z * Units.CellUnits - pz) : 0;
            w.Submit(0, new SetMovement(dx, 0, dz));
            w.Step();
        }
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(ice));
        w.Step(30 * 20);
        Assert.True(w.Network.HoldCount(0, C.ItemIndex("core:ice")) > 0);

        var ms = new MemoryStream();
        SaveGame.Write(ms, w, C, GameMode.Campaign, seed, threats: false, player: 0);
        var loaded = SaveGame.Read(new MemoryStream(ms.ToArray()), C, out _);
        Assert.Equal(w.Checksum(), loaded.Checksum());
        loaded.ValidateInvariants();
        Assert.Equal(w.Network.DepositDensity(ice), loaded.Network.DepositDensity(ice));
    }
}
