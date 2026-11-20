using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class UpgradeTests
{
    private static readonly ContentDb C = TestContent.Core;
    private static readonly int Lab = C.MachineIndex("core:research_lab");
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int Bay = C.MachineIndex("core:repair_bay");
    private static readonly int Extractor = C.MachineIndex("core:extractor");
    private static readonly int Smelter = C.MachineIndex("core:smelter");
    private static readonly int Assembler = C.MachineIndex("core:assembler");
    private static readonly int MineIron = C.RecipeIndex("core:mine_iron");
    private static readonly int SmeltIron = C.RecipeIndex("core:smelt_iron");
    private static readonly int MakeAmmo = C.RecipeIndex("core:make_ammo");
    private static readonly int Calibration = C.ResearchIndex("core:turret_calibration");
    private static readonly int Plating = C.ResearchIndex("core:reinforced_plating");
    private static readonly int Loader = C.ResearchIndex("core:rapid_loader");
    private static readonly int FieldRepair = C.ResearchIndex("core:field_repair");

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

    // ---- content --------------------------------------------------------------------------------

    [Fact]
    public void TheDefenceNodesCarryTheirBonusesAndNeedTheirPredecessors()
    {
        Assert.Equal(new[] { new Bonus(BonusKind.TurretDamage, 25) }, C.Research[Calibration].Bonuses);
        Assert.Equal(new[] { new Bonus(BonusKind.MachineHp, 50) }, C.Research[Plating].Bonuses);
        Assert.Equal(new[] { new Bonus(BonusKind.TurretRate, 25) }, C.Research[Loader].Bonuses);
        Assert.Equal(new[] { new Bonus(BonusKind.RepairHeal, 100) }, C.Research[FieldRepair].Bonuses);
        Assert.Contains(Calibration, C.Research[Loader].Requires);
        Assert.Contains(Plating, C.Research[FieldRepair].Requires);
    }

    private const string Head = "{\"schema\":1,\"items\":[{\"id\":\"t:a\"}],\"machines\":[{\"id\":\"t:m\"}],\"recipes\":[],\"research\":[";

    private static string Node(string bonuses) =>
        Head + "{\"id\":\"t:r\",\"cost\":[{\"item\":\"t:a\",\"count\":1}],\"requires\":[],\"unlocks\":[]," + bonuses + "}]}";

    [Fact]
    public void BonusOnlyNodesLoadAndBadBonusesAreRejected()
    {
        var c = ContentDb.FromJson(Node("\"bonuses\":[{\"kind\":\"machine_hp\",\"percent\":10}]"));
        Assert.Equal(new Bonus(BonusKind.MachineHp, 10), Assert.Single(c.Research[0].Bonuses));
        Assert.NotEqual(c.Hash, ContentDb.FromJson(Node("\"bonuses\":[{\"kind\":\"machine_hp\",\"percent\":11}]")).Hash);

        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Node("\"bonuses\":[]")));                                  // unlocks nothing
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Node("\"bonuses\":[{\"kind\":\"luck\",\"percent\":10}]")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Node("\"bonuses\":[{\"kind\":\"machine_hp\",\"percent\":0}]")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Node("\"bonuses\":[{\"kind\":\"turret_rate\",\"percent\":76}]")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Node(
            "\"bonuses\":[{\"kind\":\"machine_hp\",\"percent\":5},{\"kind\":\"machine_hp\",\"percent\":6}]")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(Node("\"bonuses\":[{\"kind\":\"machine_hp\",\"percent\":5,\"x\":1}]")));
    }

    // ---- effects --------------------------------------------------------------------------------

    [Fact]
    public void BonusesAddUpAndFeedTheFormulas()
    {
        var w = World();
        var n = w.Network;
        var turret = C.Machines[Turret].Turret!;
        var repairer = C.Machines[Bay].Repairer!;
        Assert.Equal(turret.Damage, n.TurretDamage(turret));
        Assert.Equal(turret.IntervalTicks, n.TurretInterval(turret));
        Assert.Equal(C.Machines[Lab].Hp, n.MaxHp(Lab));
        Assert.Equal(repairer.Heal, n.RepairHeal(repairer));

        n.GrantResearch(FieldRepair); // also grants reinforced plating, which it requires
        n.GrantResearch(Loader);      // also grants calibration
        Assert.Equal(25, n.BonusPercent(BonusKind.TurretDamage));
        Assert.Equal(25, n.BonusPercent(BonusKind.TurretRate));
        Assert.Equal(50, n.BonusPercent(BonusKind.MachineHp));
        Assert.Equal(100, n.BonusPercent(BonusKind.RepairHeal));
        Assert.Equal(turret.Damage * 125 / 100, n.TurretDamage(turret));
        Assert.Equal(turret.IntervalTicks * 75 / 100, n.TurretInterval(turret));
        Assert.Equal(C.Machines[Lab].Hp * 150 / 100, n.MaxHp(Lab));
        Assert.Equal(repairer.Heal * 2, n.RepairHeal(repairer));
        w.ValidateInvariants();
    }

    [Fact]
    public void MachinesStandingWhenPlatingArrivesKeepTheirShareOfHitPoints()
    {
        var w = World();
        int full = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        int hurt = Ok(w, new PlaceMachine(0, 0, 3, Lab, -1));
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && w.Network.GetMachineInfo(full).Hp >= C.Machines[Lab].Hp && w.Network.GetMachineInfo(hurt).Hp >= C.Machines[Lab].Hp; i++) w.Step();
        int damagedId = w.Network.GetMachineInfo(full).Hp < C.Machines[Lab].Hp ? full : hurt;
        int untouched = damagedId == full ? hurt : full;
        int before = w.Network.GetMachineInfo(damagedId).Hp;
        int baseMax = C.Machines[Lab].Hp;

        w.Network.GrantResearch(Plating);
        int newMax = baseMax * 150 / 100;
        Assert.Equal(newMax, w.Network.MaxHp(Lab));
        Assert.Equal(newMax, w.Network.GetMachineInfo(untouched).Hp); // a full machine stays full
        int expected = (int)Math.Clamp((long)before * newMax / baseMax, 1, newMax);
        Assert.True(Math.Abs(w.Network.GetMachineInfo(damagedId).Hp - expected) <= 4, "the share is kept (enemies may land a hit in the same tick)");
        w.ValidateInvariants();

        // New machines start at the higher maximum.
        int fresh = Ok(w, new PlaceMachine(0, 0, 9, Lab, -1));
        Assert.Equal(newMax, w.Network.GetMachineInfo(fresh).Hp);
    }

    [Fact]
    public void RepairsUseTheHigherMaximum()
    {
        var w = World();
        int lab = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        w.Network.GrantResearch(Plating);
        Assert.Empty(w.Network.RepairCost(lab));                // full at 180 of 180
        w.Submit(0, new Repair(lab));
        w.Step();
        Assert.Equal(Reject.NotDamaged, w.LastResults.Single().Reject);
    }

    [Fact]
    public void BonusesSurviveARolledBackBatch()
    {
        var w = World();
        w.Network.GrantResearch(Plating);
        int lab = Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
        // The second command fails, so the whole batch is rolled back through a clone of the network.
        w.Submit(0, new Batch(new Command[] { new PlaceMachine(0, 0, 3, Lab, -1), new PlaceMachine(0, 0, 0, Lab, -1) }));
        w.Step();
        Assert.False(w.LastResults.Single().Ok);
        Assert.Equal(50, w.Network.BonusPercent(BonusKind.MachineHp));
        Assert.Equal(C.Machines[Lab].Hp * 150 / 100, w.Network.GetMachineInfo(lab).Hp);
        w.ValidateInvariants();
    }

    /// <summary>Ticks a fed turret needs to kill the whole first wave, from the moment the wave arrives.</summary>
    private static int TicksToClearTheFirstWave(bool upgraded)
    {
        var w = World();
        if (upgraded) w.Network.GrantResearch(Loader);
        int ex = Ok(w, new PlaceMachine(0, 0, 30, Extractor, MineIron));
        int b1 = Ok(w, new PlaceSegment(1, 0, 30, Dir.PosX, 3, BeltTier.Mk1));
        int sm = Ok(w, new PlaceMachine(4, 0, 30, Smelter, SmeltIron));
        int b2 = Ok(w, new PlaceSegment(5, 0, 30, Dir.PosX, 3, BeltTier.Mk1));
        int asm = Ok(w, new PlaceMachine(8, 0, 30, Assembler, MakeAmmo));
        int b3 = Ok(w, new PlaceSegment(9, 0, 30, Dir.PosX, 3, BeltTier.Mk1));
        int tur = Ok(w, new PlaceMachine(12, 0, 30, Turret, -1));
        Ok(w, new ConnectFromMachine(ex, b1)); Ok(w, new ConnectToMachine(b1, sm));
        Ok(w, new ConnectFromMachine(sm, b2)); Ok(w, new ConnectToMachine(b2, asm));
        Ok(w, new ConnectFromMachine(asm, b3)); Ok(w, new ConnectToMachine(b3, tur));
        for (int i = 0; i < 900; i++) w.Step(); // let it fill with ammo
        Assert.True(w.Network.GetMachineInfo(tur).InputItems > 0);

        w.Network.ScheduleWave(0);
        w.Step(C.Threat!.WarningTicks);
        int ticks = 0;
        while (w.Network.EnemyCount > 0 && ticks < 30_000) { w.Step(); ticks++; }
        Assert.Equal(0, w.Network.EnemyCount);
        return ticks;
    }

    [Fact]
    public void UpgradedTurretsClearAWaveSooner()
    {
        Assert.True(TicksToClearTheFirstWave(upgraded: true) < TicksToClearTheFirstWave(upgraded: false));
    }

    private static readonly int Armor = C.ResearchIndex("core:ship_armor");
    private static readonly int Cannon = C.ResearchIndex("core:ship_cannon");

    [Fact]
    public void TheShipNodesCarryTheirBonusesAndNeedTheirPredecessors()
    {
        Assert.Equal(new[] { new Bonus(BonusKind.ShipHull, 50) }, C.Research[Armor].Bonuses);
        Assert.Equal(new[] { new Bonus(BonusKind.ShipDamage, 50) }, C.Research[Cannon].Bonuses);
        Assert.Contains(Plating, C.Research[Armor].Requires);
        Assert.Contains(Calibration, C.Research[Cannon].Requires);
    }

    [Fact]
    public void ShipBonusesRaiseTheHullAndTheWeaponAndAreNamedInTheContent()
    {
        var n = World().Network;
        n.EnsurePlayer(0);
        int hull = C.Threat!.ShipHull, gun = C.Threat.ShipWeaponDamage;
        Assert.Equal(hull, n.ShipMaxHull);
        Assert.Equal(gun, n.ShipWeaponDamage);

        n.GrantResearch(Armor);
        n.GrantResearch(Cannon);
        Assert.Equal(hull * 150 / 100, n.ShipMaxHull);
        Assert.Equal(gun * 150 / 100, n.ShipWeaponDamage);
        Assert.Equal(n.ShipMaxHull, n.GetPlayerInfo(0).Hull); // a ship at full hull stays at full hull
    }

    [Fact]
    public void AHurtShipKeepsItsShareOfHullWhenArmourArrives()
    {
        var w = World();
        w.Network.EnsurePlayer(0);
        Ok(w, new PlaceMachine(5, 0, 0, Lab, -1)); // bait: the enemies come to it and meet the ship on the way
        w.Network.ScheduleWave(0);
        for (int i = 0; i < 20_000 && w.Network.GetPlayerInfo(0).Hull >= C.Threat!.ShipHull; i++) w.Step();
        int before = w.Network.GetPlayerInfo(0).Hull;
        Assert.InRange(before, 1, C.Threat!.ShipHull - 1);

        w.Network.GrantResearch(Armor);
        int max = w.Network.ShipMaxHull;
        int after = w.Network.GetPlayerInfo(0).Hull;
        Assert.True(Math.Abs(after - (int)((long)before * max / C.Threat.ShipHull)) <= 12, "the share is kept (enemies may land hits in the same tick)");
        w.ValidateInvariants();

    }

    private static readonly int Overclock = C.ResearchIndex("core:overclock");
    private static readonly int Plate = C.RecipeIndex("core:iron_plate");
    private static readonly int PlateItem = C.ItemIndex("core:iron_plate");

    [Fact]
    public void RepeatableNodesCarryTheirLevelsAndGrowth()
    {
        var def = C.Research[Overclock];
        Assert.Equal((8, 35), (def.MaxLevels, def.CostGrowthPercent));
        Assert.Equal(1, C.Research[Calibration].MaxLevels);
        foreach (var id in new[] { "core:alloy_refinement", "core:nanite_repair" })
            Assert.True(C.Research[C.ResearchIndex(id)].MaxLevels > 1, id);
    }

    private const string RepHead = """{"schema":1,"items":[{"id":"t:a"}],"machines":[{"id":"t:m"}],"recipes":[],"research":[""";

    private static string RepNode(string extra) =>
        RepHead + """{"id":"t:r","cost":[{"item":"t:a","count":10}],"requires":[],"unlocks":[],""" + extra + "}]}";

    [Fact]
    public void RepeatableContentIsValidated()
    {
        const string bonus = """ "bonuses":[{"kind":"machine_hp","percent":5}]""";
        var c = ContentDb.FromJson(RepNode(bonus + """,  "repeatable":{"max_levels":3,"cost_growth_percent":50}"""));
        Assert.Equal((3, 50), (c.Research[0].MaxLevels, c.Research[0].CostGrowthPercent));
        Assert.NotEqual(c.Hash, ContentDb.FromJson(RepNode(bonus + """,  "repeatable":{"max_levels":4,"cost_growth_percent":50}""")).Hash);

        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(RepNode(bonus + """,  "repeatable":{"max_levels":1,"cost_growth_percent":50}""")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(RepNode(bonus + """,  "repeatable":{"max_levels":3,"cost_growth_percent":0}""")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(RepNode(bonus + """,  "repeatable":{"max_levels":3}""")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(RepNode(bonus + """,  "repeatable":{"max_levels":3,"cost_growth_percent":50,"x":1}""")));
        Assert.Throws<InvalidDataException>(() => ContentDb.FromJson(
            RepNode("""  "abilities":["jump"],"repeatable":{"max_levels":3,"cost_growth_percent":50}"""))); // nothing to repeat
    }

    [Fact]
    public void EachLevelCostsTheGrowthPercentMoreRoundedUpAndTheBonusIsPerLevel()
    {
        var n = World().Network;
        int baseCost = C.Research[Overclock].Cost.Single().Count;
        Assert.Equal(baseCost, n.ResearchCost(Overclock).Single().Count);

        int expected = baseCost;
        for (int level = 1; level <= 7; level++)
        {
            n.GrantResearch(Overclock, level);
            Assert.Equal(level, n.ResearchLevel(Overclock));
            expected = (int)(((long)expected * 135 + 99) / 100);
            Assert.Equal(expected, n.ResearchCost(Overclock).Single().Count);
            Assert.Equal(25 + 8 * level, n.BonusPercent(BonusKind.TurretDamage)); // calibration's 25 plus 8 for each level
            Assert.True(n.CanResearch(Overclock));
        }
        n.GrantResearch(Overclock, 99); // clamped to the maximum
        Assert.Equal(8, n.ResearchLevel(Overclock));
        Assert.False(n.CanResearch(Overclock), "the last level is done");
        Assert.Equal(25 + 8 * 8, n.BonusPercent(BonusKind.TurretDamage));
        Assert.Equal(expected, n.ResearchCost(Overclock).Single().Count == expected ? expected : -1); // a finished node shows its last level cost
    }

    [Fact]
    public void ALabResearchesLevelAfterLevelAndEachCostsMore()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: false); // no waves to wreck the chain over the hours this takes
        w.Network.GrantResearch(Calibration);
        int ex = Ok(w, new PlaceMachine(0, 0, 0, Extractor, MineIron));
        int b1 = Ok(w, new PlaceSegment(1, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        int sm = Ok(w, new PlaceMachine(4, 0, 0, Smelter, SmeltIron));
        int b2 = Ok(w, new PlaceSegment(5, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        int asm = Ok(w, new PlaceMachine(8, 0, 0, Assembler, Plate));
        int b3 = Ok(w, new PlaceSegment(9, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        int lab = Ok(w, new PlaceMachine(12, 0, 0, Lab, -1));
        Ok(w, new ConnectFromMachine(ex, b1)); Ok(w, new ConnectToMachine(b1, sm));
        Ok(w, new ConnectFromMachine(sm, b2)); Ok(w, new ConnectToMachine(b2, asm));
        Ok(w, new ConnectFromMachine(asm, b3)); Ok(w, new ConnectToMachine(b3, lab));

        int first = w.Network.ResearchCost(Overclock).Single().Count;
        Ok(w, new SetResearch(Overclock));
        for (int i = 0; i < 160_000 && w.Network.ResearchLevel(Overclock) < 1; i++) w.Step();
        Assert.Equal(1, w.Network.ResearchLevel(Overclock));
        Assert.Equal(-1, w.Network.CurrentResearch);
        Assert.Equal(0, w.Network.ResearchProgress(Overclock, PlateItem)); // the next level starts from nothing
        Assert.Equal(33, w.Network.BonusPercent(BonusKind.TurretDamage));  // 25 + 8

        int second = w.Network.ResearchCost(Overclock).Single().Count;
        Assert.True(second > first);
        Ok(w, new SetResearch(Overclock));                                   // allowed again
        for (int i = 0; i < 260_000 && w.Network.ResearchLevel(Overclock) < 2; i++) w.Step();
        Assert.Equal(2, w.Network.ResearchLevel(Overclock));
        Assert.Equal(41, w.Network.BonusPercent(BonusKind.TurretDamage));
        w.ValidateInvariants();
    }

    [Fact]
    public void LevelsSurviveARolledBackBatchAndAreDeterministic()
    {
        ulong Run()
        {
            var w = World();
            w.Network.GrantResearch(Overclock, 3);
            Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
            w.Submit(0, new Batch(new Command[] { new PlaceMachine(0, 0, 3, Lab, -1), new PlaceMachine(0, 0, 0, Lab, -1) }));
            w.Step();
            Assert.False(w.LastResults.Single().Ok);
            Assert.Equal(3, w.Network.ResearchLevel(Overclock));
            Assert.Equal(25 + 24, w.Network.BonusPercent(BonusKind.TurretDamage));
            for (int i = 0; i < 300; i++) w.Step();
            w.ValidateInvariants();
            return w.Checksum();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void UpgradesAreDeterministic()
    {
        ulong Run()
        {
            var w = World();
            w.Network.GrantResearch(FieldRepair);
            w.Network.GrantResearch(Loader);
            Ok(w, new PlaceMachine(0, 0, 0, Lab, -1));
            w.Network.ScheduleWave(0);
            for (int i = 0; i < 1200; i++) w.Step();
            return w.Checksum();
        }
        Assert.Equal(Run(), Run());
    }
}
