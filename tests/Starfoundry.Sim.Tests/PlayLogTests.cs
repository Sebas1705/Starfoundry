using Starfoundry.Sim;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class PlayLogTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261120UL;

    private static SimWorld World(GameMode mode = GameMode.Campaign, bool threats = false) => SimWorld.CreateGalaxy(C, mode, Seed, threats);

    [Fact]
    public void GuidedStepsAreLoggedOnceWithTheirTimeAndTheirName()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var log = new PlayLog(C);
        Assert.Empty(log.Update(w.Network, 0)); // nothing done at the start

        // Fly towards the iron deposit and mine: the first steps tick off.
        int ore = Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == C.ItemIndex("core:iron_ore"));
        var (x, y, z) = w.Network.DepositPosition(ore);
        for (int i = 0; i < 30 * 15 && !w.Network.InMineRange(0, ore); i++)
        {
            int dx = Math.Sign(x * Units.CellUnits - w.Network.GetPlayerInfo(0).X), dz = Math.Sign(z * Units.CellUnits - w.Network.GetPlayerInfo(0).Z);
            w.Submit(0, new SetMovement(Math.Abs(x * Units.CellUnits - w.Network.GetPlayerInfo(0).X) > 120 ? dx : 0, 0, Math.Abs(z * Units.CellUnits - w.Network.GetPlayerInfo(0).Z) > 120 ? dz : 0));
            w.Step();
        }
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(ore));
        w.Step(30 * 10);

        var fresh = log.Update(w.Network, 0);
        Assert.Contains(fresh, e => e is { Kind: "Step", Text: "Fly" });
        Assert.Contains(fresh, e => e is { Kind: "Step", Text: "Mine" });
        Assert.Empty(log.Update(w.Network, 0)); // each step is logged once only
        Assert.True(log.Entries.Select(e => e.Seconds).SequenceEqual(log.Entries.Select(e => e.Seconds).OrderBy(s => s)), "entries are in time order");
    }

    [Fact]
    public void ResearchAndItsLevelsAreLogged()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var log = new PlayLog(C);
        w.Network.GrantResearch(C.ResearchIndex("core:overclock"), 2);
        var fresh = log.Update(w.Network, 0);
        Assert.Contains(fresh, e => e is { Kind: "Researched", Text: "core:logistics" } or { Kind: "Researched", Text: "core:turret_calibration" });
        Assert.Contains(fresh, e => e is { Kind: "Researched", Text: "core:overclock level 2" });

        w.Network.GrantResearch(C.ResearchIndex("core:overclock"), 3);
        Assert.Contains(log.Update(w.Network, 0), e => e is { Kind: "Researched", Text: "core:overclock level 3" });
    }

    [Fact]
    public void WavesLossesAndJumpsAreLogged()
    {
        var w = World(GameMode.Sandbox, threats: true);
        w.Network.EnsurePlayer(0);
        var log = new PlayLog(C);

        w.Submit(0, new PlaceMachine(0, 0, 0, C.MachineIndex("core:research_lab"), -1));
        w.Step();
        w.Network.ScheduleWave(0);
        w.Step(1);
        Assert.Contains(log.Update(w.Network, 0), e => e is { Kind: "Wave", Text: "first warning" });

        w.Step(C.Threat!.WarningTicks);
        Assert.Contains(log.Update(w.Network, 0), e => e.Kind == "Wave" && e.Text.StartsWith("wave 1 arrived"));

        for (int i = 0; i < 8; i++) { w.Network.ScheduleWave(0); w.Step(C.Threat.WarningTicks); }
        for (int i = 0; i < 20_000 && w.Network.MachinesLost == 0; i++) w.Step();
        Assert.Contains(log.Update(w.Network, 0), e => e.Kind == "Loss" && e.Text.Contains("machine"));

        var g = w.Network.Galaxy!;
        // The ship may have been rebuilt; send a fresh world's ship to a neighbour for the jump line.
        var calm = World(GameMode.Sandbox);
        calm.Network.EnsurePlayer(0);
        var jumpLog = new PlayLog(C);
        calm.Submit(0, new Jump(g.Systems[0].Lanes[0]));
        calm.Step(ExplorationRules.JumpChargeTicks + 5);
        Assert.Contains(jumpLog.Update(calm.Network, 0), e => e.Kind == "Jump");
    }

    [Fact]
    public void TheTextHasOneLinePerEntryAndASummary()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var log = new PlayLog(C);
        w.Network.GrantResearch(C.ResearchIndex("core:logistics"));
        log.Update(w.Network, 0);
        string text = log.ToText();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal(log.Entries.Count + 1, lines.Count);
        Assert.StartsWith("0:00", lines[0]);
        Assert.Contains(lines, l => l.Contains("Researched"));
        Assert.StartsWith("--", lines[^1]);
        Assert.Equal("1:05", PlayLog.Format(65.9));
    }

    [Fact]
    public void AFullHoldIsCountedInSeconds()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        var log = new PlayLog(C);
        w.Network.GiveItems(0, C.ItemIndex("core:iron_ingot"), PlayerRules.HoldCapacity);
        for (int i = 0; i < 4; i++) { w.Step(Units.TicksPerSecond); log.Update(w.Network, 0); }
        Assert.InRange(log.HoldFullSeconds, 3, 5);
    }
}
