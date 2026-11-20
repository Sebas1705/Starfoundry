using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class ThreatTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly ThreatRules Rules = C.Threat!;
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Assembler = C.MachineIndex("core:assembler");
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Drill = C.MachineIndex("core:drill");
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int DrillIron = C.RecipeIndex("core:drill_iron");
    private static readonly int Plate = C.RecipeIndex("core:iron_plate");
    private static readonly int Ammo = C.ItemIndex("core:ammo");
    private static readonly int Ingot = C.ItemIndex("core:iron_ingot");

    private const ulong Seed = 20261120UL;

    private static SimWorld World(GameMode mode = GameMode.Sandbox, bool threats = true) =>
        SimWorld.CreateGalaxy(C, mode, Seed, threats);

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

    /// <summary>Machines with a total signature of 14, above the threshold of 8: smelter 3 + assembler 3 + lab 4 + two drills 4.</summary>
    private static void BuildLoudBase(SimWorld w, int originX = 0)
    {
        Ok(w, new PlaceMachine(originX, 0, 0, Smelter, SmeltIron));
        Ok(w, new PlaceMachine(originX, 0, 3, Assembler, Plate));
        Ok(w, new PlaceMachine(originX, 0, 6, Lab, -1));
        Ok(w, new PlaceMachine(originX, 0, 9, Drill, DrillIron));
        Ok(w, new PlaceMachine(originX, 0, 12, Drill, DrillIron));
    }

    private static void StepUntil(SimWorld w, Func<bool> done, int limit = 20_000)
    {
        for (int i = 0; i < limit && !done(); i++) w.Step();
        Assert.True(done(), "the condition was not reached in time");
    }

    // ---- switching threats on --------------------------------------------------------------------

    [Fact]
    public void ThreatsAreOffUnlessSwitchedOnAndNeedAGalaxy()
    {
        var quiet = World(threats: false);
        BuildLoudBase(quiet);
        quiet.Step(5000);
        Assert.False(quiet.Network.ThreatActive);
        Assert.Equal(0, quiet.Network.GetSystemThreat(0).Alert);
        Assert.Equal(0, quiet.Network.EnemyCount);
        quiet.ValidateInvariants();

        var noGalaxy = new SimWorld(new BeltNetwork(C, GameMode.Sandbox, threats: true));
        Assert.False(noGalaxy.Network.ThreatActive);
        Assert.NotEqual(World(threats: true).Checksum(), World(threats: false).Checksum());
    }

    // ---- signature and alert ---------------------------------------------------------------------

    [Fact]
    public void SignatureAddsUpPerSystem()
    {
        var w = World();
        Assert.Equal(0, w.Network.SystemSignature(0));
        BuildLoudBase(w);
        Assert.Equal(3 + 3 + 4 + 2 + 2, w.Network.SystemSignature(0));
        Ok(w, new PlaceMachine(0, 0, 15, Turret, -1)); // turrets are quiet
        Assert.Equal(14, w.Network.SystemSignature(0));

        // A machine in another system counts there, not at home.
        var g = w.Network.Galaxy!;
        var other = g.Systems[g.Systems[0].Lanes[0]];
        Ok(w, new PlaceMachine(other.X, other.Y, other.Z, Smelter, SmeltIron));
        Assert.Equal(14, w.Network.SystemSignature(0));
        Assert.Equal(3, w.Network.SystemSignature(other.Id));

        // A machine in deep space counts nowhere.
        Ok(w, new PlaceMachine(0, 3000, 0, Smelter, SmeltIron));
        Assert.Equal(14, w.Network.SystemSignature(0));
    }

    [Fact]
    public void AlertRisesByTheExcessSignatureEveryTickAndDecaysBelowTheThreshold()
    {
        var w = World();
        Ok(w, new PlaceMachine(0, 0, 0, Smelter, SmeltIron));
        Ok(w, new PlaceMachine(0, 0, 3, Assembler, Plate)); // signature 6, below the threshold of 8
        w.Step(500);
        Assert.Equal(0, w.Network.GetSystemThreat(0).Alert);

        int lab = Ok(w, new PlaceMachine(0, 0, 6, Lab, -1)); // 10: two above the threshold
        int before = w.Network.GetSystemThreat(0).Alert;
        w.Step(100);
        Assert.Equal(before + 100 * (10 - Rules.SignatureThreshold), w.Network.GetSystemThreat(0).Alert);

        Ok(w, new RemoveMachine(lab));
        int peak = w.Network.GetSystemThreat(0).Alert;
        w.Step(50);
        Assert.Equal(peak - 50 * Rules.DecayPerTick, w.Network.GetSystemThreat(0).Alert);
        w.ValidateInvariants();
    }

    [Fact]
    public void AFullAlertWarnsThenSpawnsAWaveAndResetsTheMeter()
    {
        var w = World();
        BuildLoudBase(w);
        StepUntil(w, () => w.Network.GetSystemThreat(0).WarningRemaining >= 0, limit: Rules.AlertMax + 1000); // the meter rises by at least 1 a tick
        var info = w.Network.GetSystemThreat(0);
        Assert.Equal(Rules.AlertMax, info.Alert);
        Assert.Equal(Rules.WarningTicks - 1, info.WarningRemaining); // the countdown starts in the tick the meter fills
        Assert.Equal(0, w.Network.EnemyCount);

        w.Step(info.WarningRemaining - 1);
        Assert.Equal(0, w.Network.EnemyCount); // still counting down
        w.Step();
        Assert.Equal(2, w.Network.EnemyCount);  // wave 0: two scouts
        Assert.Equal(1, w.Network.WavesSpawned);
        info = w.Network.GetSystemThreat(0);
        Assert.Equal((Rules.AlertAfterWave, 1, -1), (info.Alert, info.Wave, info.WarningRemaining));
        w.ValidateInvariants();
    }

    [Fact]
    public void WavesGrowAndEnemiesAppearAtTheNodeAroundTheSystem()
    {
        var w = World();
        Assert.Throws<InvalidOperationException>(() => World(threats: false).Network.ScheduleWave(0));

        var counts = new List<int>();
        for (int wave = 0; wave < 3; wave++)
        {
            long before = w.Network.EnemiesSpawned;
            w.Network.ScheduleWave(0);
            w.Step(Rules.WarningTicks);
            counts.Add((int)(w.Network.EnemiesSpawned - before));
        }
        Assert.Equal(new[] { 2, 4, 6 }, counts); // scouts: 2, 3, 4  and strikers: 0, 1, 2

        // They start about 70 cells from the anchor (plus a cell of jitter), in the system.
        foreach (var e in w.Network.Enemies())
        {
            double dx = e.X / (double)Units.CellUnits, dz = e.Z / (double)Units.CellUnits;
            Assert.InRange(Math.Sqrt(dx * dx + dz * dz), Rules.NodeDistanceCells - 3, Rules.NodeDistanceCells + 3);
            Assert.Equal(0, e.System);
        }
        w.ValidateInvariants();
    }

    [Fact]
    public void TheEnemyCapLimitsWaves()
    {
        var w = World();
        for (int i = 0; i < 8; i++)
        {
            w.Network.ScheduleWave(0);
            w.Step(Rules.WarningTicks);
        }
        Assert.Equal(Rules.MaxEnemies, w.Network.EnemyCount);
        w.ValidateInvariants();
    }

    [Fact]
    public void WavesDoNotStackWhileOneIsActive()
    {
        var w = World();
        BuildLoudBase(w);
        w.Network.ScheduleWave(0);
        w.Step(Rules.WarningTicks);
        Assert.True(w.Network.EnemyCount > 0);
        int alert = w.Network.GetSystemThreat(0).Alert;
        w.Step(300);
        Assert.Equal(alert, w.Network.GetSystemThreat(0).Alert); // frozen while enemies are alive and nothing killed them yet
    }

    // ---- enemies ---------------------------------------------------------------------------------

    [Fact]
    public void EnemiesFlyToTheLoudestMachineAndDestroyItAndTheCellIsFree()
    {
        var w = World();
        int quiet = Ok(w, new PlaceMachine(20, 0, 0, Drill, DrillIron));  // signature 2
        int loud = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));             // signature 4: the target
        w.Network.ScheduleWave(0);

        StepUntil(w, () => !w.Network.IsMachineAlive(loud));
        Assert.True(w.Network.IsMachineAlive(quiet), "the quieter machine is attacked only after the louder one");
        Assert.Equal(1, w.Network.MachinesLost);
        w.ValidateInvariants(); // includes the cell index: the destroyed machine's cell must have been freed

        // Remove the enemies' reason to stay: the next target is the other machine.
        StepUntil(w, () => !w.Network.IsMachineAlive(quiet));
        Assert.Equal(2, w.Network.MachinesLost);
        Assert.Equal(Reject.None, Run(w, new PlaceMachine(0, 0, 0, Lab, -1)).Reject); // the cell is free again
    }

    [Fact]
    public void ADestroyedMachineLosesItsContentsAndItsBelts()
    {
        var w = World();
        int drill = Ok(w, new PlaceMachine(0, 0, 0, Drill, DrillIron));
        int belt = Ok(w, new PlaceSegment(1, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        int smelter = Ok(w, new PlaceMachine(4, 0, 0, Smelter, SmeltIron));
        Ok(w, new ConnectFromMachine(drill, belt));
        Ok(w, new ConnectToMachine(belt, smelter));
        w.Network.AddDeposit(C.ItemIndex("core:iron_ore"), 1000, 2, 0, 3);
        Ok(w, new LinkDeposit(drill, w.Network.DepositCount - 1));
        w.Step(300); // the smelter is the loudest and holds some ore
        long destroyed = w.Network.Destroyed;
        int held = w.Network.GetMachineInfo(smelter).InputItems;

        w.Network.ScheduleWave(0);
        StepUntil(w, () => !w.Network.IsMachineAlive(smelter));
        Assert.True(w.Network.Destroyed >= destroyed + held);
        Assert.False(w.Network.FeedsAnyMachine(belt));
        w.ValidateInvariants();
    }

    [Fact]
    public void EnemiesAttackAShipThatComesCloseAndTheShipIsRebuiltAtTheAnchor()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        w.Network.GiveItems(0, Ingot, 12);
        Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        w.Network.ScheduleWave(0);

        StepUntil(w, () => w.Network.ShipsLost > 0);
        var info = w.Network.GetPlayerInfo(0);
        Assert.Equal(0, info.Hull);
        Assert.True(info.RespawnRemaining > 0);
        Assert.Equal((0, 0, 0), (info.X, info.Y, info.Z));
        Assert.Equal(12, w.Network.HoldCount(0, Ingot)); // the hold survives

        w.Step(Rules.RespawnTicks);
        info = w.Network.GetPlayerInfo(0);
        Assert.Equal(Rules.ShipHull, info.Hull);
        Assert.Equal(0, info.RespawnRemaining);
        w.ValidateInvariants();
    }

    // ---- turrets ---------------------------------------------------------------------------------

    /// <summary>A belt that carries ammo from a (sandbox) source into a turret.</summary>
    private static int AmmoFedTurret(SimWorld w, int x, int z)
    {
        int turret = Ok(w, new PlaceMachine(x, 0, z, Turret, -1));
        int belt = Ok(w, new PlaceSegment(x - 3, 0, z, Dir.PosX, 3, BeltTier.Mk2));
        Ok(w, new SetSource(belt, new ushort[] { (ushort)Ammo }));
        Ok(w, new ConnectToMachine(belt, turret));
        return turret;
    }

    [Fact]
    public void TurretsFillWithAmmoFromBeltsAndRefuseAnythingElse()
    {
        var w = World();
        int turret = AmmoFedTurret(w, 8, 0);
        w.Step(600);
        Assert.Equal(C.Machines[Turret].Turret!.AmmoCap, w.Network.MachineInCount(turret, Ammo));
        w.ValidateInvariants();

        var jam = World();
        int t2 = Ok(jam, new PlaceMachine(8, 0, 0, Turret, -1));
        int belt = Ok(jam, new PlaceSegment(5, 0, 0, Dir.PosX, 3, BeltTier.Mk2));
        Ok(jam, new SetSource(belt, new ushort[] { (ushort)Ingot }));
        Ok(jam, new ConnectToMachine(belt, t2));
        jam.Step(600);
        Assert.Equal(0, jam.Network.GetMachineInfo(t2).InputItems);
        Assert.Equal(13, jam.Network.CountOn(belt)); // the belt is jammed with ingots
    }

    [Fact]
    public void TurretsWithAmmoKillEnemiesBeforeTheyDoMuchDamage()
    {
        var w = World();
        int lab = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        int t1 = AmmoFedTurret(w, 6, 0);
        AmmoFedTurret(w, 6, 8);
        w.Step(600);

        w.Network.ScheduleWave(0);
        StepUntil(w, () => w.Network.EnemiesKilled == w.Network.EnemiesSpawned && w.Network.EnemiesSpawned > 0);

        Assert.True(w.Network.IsMachineAlive(lab), "two fed turrets protect the lab");
        Assert.True(w.Network.AmmoFired >= 6, "every scout takes at least three shots");
        Assert.Equal(0, w.Network.EnemyCount);
        Assert.True(w.Network.GetMachineInfo(t1).Hp > 0);
        w.ValidateInvariants();
    }

    [Fact]
    public void TurretsWithoutAmmoOrOutOfRangeDoNothing()
    {
        var empty = World();
        Ok(empty, new PlaceMachine(0, 0, 0, Lab, -1));
        Ok(empty, new PlaceMachine(6, 0, 0, Turret, -1)); // no belt, so no ammo
        empty.Network.ScheduleWave(0);
        StepUntil(empty, () => empty.Network.MachinesLost >= 1);
        Assert.Equal(0, empty.Network.AmmoFired);

        var far = World();
        Ok(far, new PlaceMachine(0, 0, 0, Lab, -1));
        AmmoFedTurret(far, 0, 60); // more than 18 cells from the lab and from where the scouts stop
        far.Step(600);
        far.Network.ScheduleWave(0);
        StepUntil(far, () => far.Network.MachinesLost >= 1);
        Assert.Equal(0, far.Network.AmmoFired);
    }

    [Fact]
    public void ATurretFiresNoFasterThanItsInterval()
    {
        var w = World();
        Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        AmmoFedTurret(w, 6, 0);
        w.Step(600);
        w.Network.ScheduleWave(0);

        var shotTicks = new List<long>();
        for (int i = 0; i < 3000; i++)
        {
            w.Step();
            if (w.Network.Shots.Any(s => s.Kind == ShotKind.Turret)) shotTicks.Add(w.Tick);
        }
        Assert.True(shotTicks.Count >= 4);
        int interval = C.Machines[Turret].Turret!.IntervalTicks;
        for (int i = 1; i < shotTicks.Count; i++) Assert.True(shotTicks[i] - shotTicks[i - 1] >= interval);
    }

    // ---- the ship's weapon -------------------------------------------------------------------------

    [Fact]
    public void TheShipWeaponKillsEnemiesInRangeWhileOn()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        Ok(w, new SetWeapons(true));
        Assert.True(w.Network.GetPlayerInfo(0).Armed);
        w.Network.ScheduleWave(0);

        StepUntil(w, () => w.Network.EnemiesKilled > 0);
        Assert.True(w.Network.EnemiesKilled >= 1);

        Ok(w, new SetWeapons(false));
        Assert.False(w.Network.GetPlayerInfo(0).Armed);
        long killed = w.Network.EnemiesKilled;
        bool shot = false;
        for (int i = 0; i < 200; i++) { w.Step(); shot |= w.Network.Shots.Any(s => s.Kind == ShotKind.Ship); }
        Assert.False(shot);
        Assert.True(w.Network.EnemiesKilled >= killed);
        w.ValidateInvariants();
    }

    // ---- codec, header and replay --------------------------------------------------------------------

    [Fact]
    public void CommandsHeadersAndReplaysCoverCombat()
    {
        Assert.Equal(new SetWeapons(true), CommandCodec.FromBytes(CommandCodec.ToBytes(new SetWeapons(true))));

        // Setup that is not a command (here: forcing the first wave) belongs to the starting world, so replays recreate it.
        SimWorld Start(GameMode mode = GameMode.Sandbox, ulong seed = Seed, bool threats = true)
        {
            var s = SimWorld.CreateGalaxy(C, mode, seed, threats);
            s.Network.EnsurePlayer(0);
            s.Network.ScheduleWave(0);
            return s;
        }

        var w = Start();
        Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        AmmoFedTurret(w, 6, 0);
        Ok(w, new SetWeapons(true));
        w.Step(2500);
        Assert.True(w.Network.WavesSpawned > 0);
        Assert.True(w.Network.EnemiesKilled > 0);

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, GameMode.Sandbox, Seed, threats: true);
        ms.Position = 0;
        var log = CommandCodec.ReadLog(ms, out _, out var mode, out ulong seed, out bool threats);
        Assert.Equal((GameMode.Sandbox, Seed, true), (mode, seed, threats));

        var replay = SimWorld.Replay(log, w.Tick, initial: Start(mode, seed, threats));
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();

        // A replay that forgets the setup diverges: the starting world matters.
        var wrong = SimWorld.Replay(log, w.Tick, initial: SimWorld.CreateGalaxy(C, mode, seed, threats));
        Assert.NotEqual(w.Checksum(), wrong.Checksum());
    }

    // ---- fuzz --------------------------------------------------------------------------------------

    private static SimWorld StormWorld(GameMode mode)
    {
        var w = SimWorld.CreateGalaxy(C, mode, Seed, threats: true);
        w.Network.EnsurePlayer(0);
        w.Network.ScheduleWave(0); // a first wave arrives whatever the players do
        if (mode == GameMode.Campaign)
        {
            for (int p = 0; p < 3; p++)
            {
                w.Network.GiveItems(p, Ingot, 120);
                w.Network.GiveItems(p, C.ItemIndex("core:iron_plate"), 40);
            }
        }
        return w;
    }

    private static Command RandomCombatCommand(Random rng, SimWorld w)
    {
        switch (rng.Next(0, 6))
        {
            case 0: return new SetWeapons(rng.Next(0, 2) == 0);
            case 1: return new PlaceMachine(rng.Next(0, 8), 0, rng.Next(0, 8), Turret, -1);
            case 2: return new PlaceMachine(rng.Next(0, 8), 0, rng.Next(0, 8), rng.Next(0, 2) == 0 ? Lab : Smelter, rng.Next(0, 2) == 0 ? -1 : SmeltIron);
            case 3: return new RemoveMachine(rng.Next(0, Math.Max(1, w.Network.MachineIdLimit)));
            case 4: return new SetMovement(rng.Next(-1, 2), 0, rng.Next(-1, 2));
            default: return new SetMovement(0, 0, 0);
        }
    }

    [Theory]
    [InlineData(GameMode.Sandbox, 41)]
    [InlineData(GameMode.Sandbox, 42)]
    [InlineData(GameMode.Sandbox, 43)]
    [InlineData(GameMode.Campaign, 44)]
    [InlineData(GameMode.Campaign, 45)]
    public void RandomCombatStormKeepsInvariantsAndReplaysIdentically(GameMode mode, int seed)
    {
        const int ticks = 4000;
        var rng = new Random(seed);
        var w = StormWorld(mode);

        for (int t = 0; t < ticks; t++)
        {
            int n = rng.Next(0, 3);
            for (int i = 0; i < n; i++)
            {
                int who = rng.Next(0, 3);
                int pick = rng.Next(0, 10);
                Command cmd = pick < 5 ? RandomCombatCommand(rng, w)
                    : pick < 7 ? MachineTests.RandomMachineCommand(rng, w)
                    : CommandTests.RandomCommand(rng, w);
                w.Submit(who, cmd);
            }
            w.Step();
            w.ValidateInvariants(); // includes the cell index after machines are destroyed
        }

        Assert.True(w.Network.WavesSpawned >= 1, "no wave arrived");
        Assert.True(w.Network.EnemiesSpawned >= 2);
        Assert.True(w.Network.EnemiesKilled + w.Network.MachinesLost + w.Network.ShipsLost > 0, "the wave had no effect at all");

        var ms = new MemoryStream();
        CommandCodec.WriteLog(ms, w.Log, C.Hash, mode, Seed, threats: true);
        ms.Position = 0;
        var restored = CommandCodec.ReadLog(ms, out _, out var m, out ulong s, out bool threats);
        Assert.Equal((mode, Seed, true), (m, s, threats));

        var replay = SimWorld.Replay(restored, ticks, initial: StormWorld(mode));
        Assert.Equal(w.Checksum(), replay.Checksum());
        replay.ValidateInvariants();
    }
}
