using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>A jump costs carbon, so no system may leave a ship without a way to mine it, and the cost must be readable by the game.</summary>
public class JumpSafetyTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Carbon = C.ItemIndex("core:carbon");
    private static readonly int JumpDrive = C.ResearchIndex("core:jump_drive");
    private const ulong Seed = 20261121UL;

    [Fact]
    public void Every_archetype_has_carbon_so_a_return_jump_is_possible()
    {
        int cost = C.JumpCost.Single(s => s.Item == Carbon).Count;
        foreach (var a in C.Archetypes)
        {
            var carbon = a.Deposits.Where(d => d.Item == Carbon).ToList();
            Assert.True(carbon.Count > 0, $"archetype '{a.Id}' has no carbon deposit");
            Assert.True(carbon.All(d => d.CountMin >= 1), $"archetype '{a.Id}' may spawn without its carbon deposit");
            Assert.True(carbon.Sum(d => d.AmountMin * d.CountMin) >= cost * 8, $"archetype '{a.Id}' has too little carbon for a few jumps");
        }
    }

    [Fact]
    public void Jump_cost_and_affordability_are_readable()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed);
        w.Network.EnsurePlayer(0);
        var cost = Assert.Single(w.Network.JumpCost());
        Assert.Equal(Carbon, cost.Item);
        Assert.False(w.Network.CanAffordJump(0));
        w.Network.GiveItems(0, Carbon, cost.Count - 1);
        Assert.False(w.Network.CanAffordJump(0));
        w.Network.GiveItems(0, Carbon, 1);
        Assert.True(w.Network.CanAffordJump(0));

        // Sandbox jumps are free.
        var sb = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed);
        sb.Network.EnsurePlayer(0);
        Assert.True(sb.Network.CanAffordJump(0));
    }

    [Fact]
    public void A_ship_stranded_at_a_titanium_vein_mines_carbon_and_jumps_back()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed);
        var g = w.Network.Galaxy!;
        w.Network.EnsurePlayer(0);
        w.Network.GrantResearch(JumpDrive);
        int vein = C.Archetypes.ToList().FindIndex(a => a.Id == "core:titanium_vein");
        int target = Enumerable.Range(0, g.Systems.Count).First(s => g.Systems[s].Archetype == vein);

        // Shortest lane path from home.
        var prev = new Dictionary<int, int> { [0] = -1 };
        var queue = new Queue<int>(new[] { 0 });
        while (queue.Count > 0 && !prev.ContainsKey(target))
        {
            int s = queue.Dequeue();
            foreach (int n in g.Systems[s].Lanes)
                if (prev.TryAdd(n, s)) queue.Enqueue(n);
        }
        var path = new List<int>();
        for (int s = target; s != -1; s = prev[s]) path.Add(s);
        path.Reverse();
        int hops = path.Count - 1;
        Assert.True(hops >= 1);

        int cost = w.Network.JumpCost().Single().Count;
        w.Network.GiveItems(0, Carbon, cost * hops); // exactly enough to get there
        foreach (int next in path.Skip(1))
        {
            w.Submit(0, new Jump(next));
            w.Step();
            Assert.True(w.LastResults.Single().Ok, "outbound jump rejected");
            w.Step(ExplorationRules.JumpChargeTicks);
            Assert.Equal(next, w.Network.CurrentSystem(0));
        }
        Assert.Equal(0, w.Network.HoldCount(0, Carbon));
        Assert.False(w.Network.CanAffordJump(0)); // stranded unless carbon can be mined here

        w.Submit(0, new Scan());
        w.Step();
        int dep = -1;
        for (int i = 0; i < w.Network.DepositCount; i++)
            if (g.Deposits[i].System == target && w.Network.DepositItem(i) == Carbon && w.Network.IsDepositRevealed(i)) { dep = i; break; }
        Assert.True(dep >= 0, "no carbon deposit revealed by a scan at the vein");

        var (dx, dy, dz) = w.Network.DepositPosition(dep);
        int guard = 0;
        (int, int, int) last = (0, 0, 0);
        while (!w.Network.InMineRange(0, dep) && guard++ < 20_000)
        {
            var p = w.Network.GetPlayerInfo(0);
            static int Axis(long diff) => Math.Abs(diff) > Units.CellUnits ? Math.Sign(diff) : 0;
            var intent = (Axis(dx * Units.CellUnits - p.X), Axis(dy * Units.CellUnits - p.Y), Axis(dz * Units.CellUnits - p.Z));
            if (intent != last) { w.Submit(0, new SetMovement(intent.Item1, intent.Item2, intent.Item3)); last = intent; }
            w.Step();
        }
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(dep));
        w.Step();
        Assert.True(w.LastResults.Last().Ok);
        for (int i = 0; i < 30 * 120 && !w.Network.CanAffordJump(0); i++) w.Step();
        Assert.True(w.Network.CanAffordJump(0), "could not mine enough carbon to leave");

        w.Submit(0, new Jump(path[^2]));
        w.Step();
        Assert.True(w.LastResults.Last().Ok);
        w.Step(ExplorationRules.JumpChargeTicks);
        Assert.Equal(path[^2], w.Network.CurrentSystem(0));
        w.ValidateInvariants();
    }
}
