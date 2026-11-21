using Starfoundry.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// Wave balance of structure worlds (docs/prototypes/defence.md): how far an ammo-fed battery and the ship cannon get through the
/// waves, measured on a ship of cockpit, workshop, defence module and engine with no repair bay and a two minute pause between waves.
/// </summary>
public class WaveBalanceTests
{
    private const int Tps = Units.TicksPerSecond;
    private const ulong Seed = 20261121UL;

    private static readonly ContentDb C = TestContent.CoreWithoutStarterShip;
    private static readonly int Turret = C.MachineIndex("core:turret");
    private static readonly int Ammo = C.ItemIndex("core:ammo");
    private static readonly int Hydrogen = C.ItemIndex("core:hydrogen");

    private static string _lastModules = "";
    private readonly ITestOutputHelper _out;
    public WaveBalanceTests(ITestOutputHelper output) => _out = output;

    private static int Ok(SimWorld w, Command c)
    {
        w.Submit(0, c);
        w.Step();
        var r = w.LastResults.Single();
        Assert.True(r.Ok, $"{c} was rejected: {r.Reject}");
        return r.CreatedMachines.Count > 0 ? r.CreatedMachines[0] : r.Created.Count > 0 ? r.Created[0] : -1;
    }

    /// <summary>One wave in the series: whether the base held (all enemies dead, no module lost) and the structure's hit points after it.</summary>
    private readonly record struct WaveResult(int Wave, bool Held, int ModulesLost, int HpPermille) { public int Left { get; init; } public bool ShipLost { get; init; } }

    /// <summary>Plays waves 0 to <paramref name="last"/> in sequence against a base and returns one result per wave fought (stops at the first failure).</summary>
    private static List<WaveResult> Fight(int turrets, bool cannon, int last, int defenceModules = 1, ContentDb? content = null, params string[] research)
    {
        var c = content ?? C;
        var w = SimWorld.CreateGalaxy(c, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        // A compact cross: the defence module in the middle, the workshop with the ammo belts on one side, cockpit and engine on the
        // other two, so every module lies within reach of turrets standing in the defence module.
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Workshop);
        for (int i = 0; i < defenceModules; i++) w.Network.AddStructureModuleUnchecked(s, 1, i, 0, ModuleKind.Defence);
        w.Network.AddStructureModuleUnchecked(s, 1, 0, 1, ModuleKind.Cockpit);
        w.Network.AddStructureModuleUnchecked(s, 1, 0, -1, ModuleKind.Engine);
        w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor);
        w.Network.GiveItems(0, Hydrogen, 60);
        foreach (var r in research) w.Network.GrantResearch(c.ResearchIndex(r));
        int turret = c.MachineIndex("core:turret"), ammo = c.ItemIndex("core:ammo");
        for (int i = 0; i < turrets; i++)
        {
            int belt = Ok(w, new PlaceSegmentIn(s, 4, 1, i, Dir.PosX, 4, BeltTier.Mk2));
            Ok(w, new SetSource(belt, new ushort[] { (ushort)ammo }));
            int t = Ok(w, new PlaceMachineIn(s, 8, 1, i, turret, -1));
            Ok(w, new ConnectToMachine(belt, t));
        }
        if (cannon) w.Submit(0, new SetWeapons(true));
        w.Step(Tps);
        var results = new List<WaveResult>();
        for (int wave = 0; wave <= last; wave++)
        {
            long lost = w.Network.ModulesLost;
            w.Network.ScheduleWave(0);
            for (int i = 0; i < 30_000 && (w.Network.EnemyCount > 0 || w.Network.WavesSpawned <= wave); i++) w.Step();
            bool cleared = w.Network.EnemyCount == 0;
            bool lostShip = w.Network.ShipLost(0);
            bool held = cleared && w.Network.ModulesLost == lost && !lostShip;
            results.Add(new WaveResult(wave, held, (int)(w.Network.ModulesLost - lost), lostShip ? 0 : w.Network.StructureHpFraction(s)) { Left = w.Network.EnemyCount, ShipLost = lostShip });
            if (!held) { _lastModules = string.Join(",", w.Network.GetStructure(s).Modules.Values); break; }
            w.Step(Tps * 120);
        }
        _ = Turret; _ = Ammo; _ = Hydrogen;
        return results;
    }

    private static string Row(List<WaveResult> r) =>
        string.Join("  ", r.Select(x => $"w{x.Wave}:{(x.Held ? "held" : "FELL")}({x.HpPermille}{(x.Held ? "" : $", left {x.Left}, modules lost {x.ModulesLost}, ship lost {x.ShipLost}")})"));

    /// <summary>The first wave that got through, or -1 if every wave in the series held.</summary>
    private static int FirstFall(List<WaveResult> r) => r.Any(x => !x.Held) ? r.First(x => !x.Held).Wave : -1;

    /// <summary>The core content (no starter ship) with a change applied to its JSON, for comparisons against the shipped numbers.</summary>
    private static ContentDb Variant(Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")))!.AsObject();
        root.Remove("structure_templates");
        change(root);
        return ContentDb.FromJson(root.ToJsonString());
    }

    [Fact]
    public void Measure_the_wave_table()
    {
        var plainCannon = Variant(r => r["threat"]!.AsObject().Remove("structure_ship_damage_percent"));
        foreach (bool cannon in new[] { false, true })
            for (int n = 0; n <= 4; n++)
                foreach (var research in new[] { Array.Empty<string>(), new[] { "core:turret_calibration" } })
                {
                    var row = Row(Fight(n, cannon, 8, 1, null, research));
                    _out.WriteLine($"cannon {(cannon ? "on " : "off")} {n} turrets {(research.Length == 0 ? "no research " : "calibrated   ")}: {row} [{_lastModules}]");
                }
        foreach (int n in new[] { 0, 1, 2 })
            _out.WriteLine($"cannon on, damage 100 percent, {n} turrets: {Row(Fight(n, true, 8, 1, plainCannon))} [{_lastModules}]");
    }

    [Fact]
    public void Two_turrets_and_the_ship_cannon_hold_waves_0_to_6()
    {
        var r = Fight(2, cannon: true, last: 6);
        Assert.Equal(7, r.Count);
        Assert.All(r, x => Assert.True(x.Held, $"wave {x.Wave} got through ({x.ModulesLost} modules lost)"));
    }

    [Fact]
    public void One_turret_is_not_enough_from_wave_3()
    {
        // Without the cannon one turret holds the scouts and the first strikers; the brute of wave 3 starts to hurt and the base falls
        // within two waves. A player has to have built defence by then.
        var r = Fight(1, cannon: false, last: 8);
        Assert.InRange(FirstFall(r), 3, 5);
        Assert.True(r[3].HpPermille < 1000, "wave 3 (the first brute) should leave a mark");
        Assert.True(r.Take(3).All(x => x.Held), "waves 0 to 2 are survivable with one turret");
    }

    [Fact]
    public void Undefended_base_falls_by_wave_2()
    {
        Assert.InRange(FirstFall(Fight(0, cannon: false, last: 8)), 0, 2);
        // Firing the cannon by hand holds the early waves but cannot carry a base without turrets for long.
        Assert.InRange(FirstFall(Fight(0, cannon: true, last: 8)), 4, 7);
    }

    [Fact]
    public void Ship_cannon_is_stronger_in_structure_worlds_only()
    {
        var core = TestContent.Core;
        var legacy = SimWorld.CreateGalaxy(core, GameMode.Sandbox, Seed, threats: true);
        var structure = SimWorld.CreateGalaxy(core, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true);
        legacy.Network.EnsurePlayer(0);
        structure.Network.EnsurePlayer(0);
        Assert.Equal(6, legacy.Network.ShipWeaponDamage);
        Assert.Equal(9, structure.Network.ShipWeaponDamage);
        structure.Network.GrantResearch(core.ResearchIndex("core:ship_cannon")); // the research bonus stacks on top
        Assert.Equal(13, structure.Network.ShipWeaponDamage);
    }

    [Fact]
    public void Defence_module_regenerates_faster_than_other_modules()
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Sandbox, Seed, threats: true, stationRules: false, structures: true);
        w.Network.EnsurePlayer(0);
        int s = w.Network.CreateStructure(0, StructureKind.Ship, 0, 0, 0);
        w.Network.AddStructureModuleUnchecked(s, 0, 0, 0, ModuleKind.Workshop);
        w.Network.AddStructureModuleUnchecked(s, 1, 0, 0, ModuleKind.Defence);
        w.Network.AddStructureModuleUnchecked(s, 20, 0, 0, ModuleKind.Reactor);
        w.Network.GiveItems(0, Hydrogen, 20);
        w.Network.HitModule(s, 0, 0, 0, 50);
        w.Network.HitModule(s, 1, 0, 0, 50);
        int w0 = w.Network.ModuleHp(s, 0, 0, 0), d0 = w.Network.ModuleHp(s, 1, 0, 0);
        w.Step(Tps * 60);
        Assert.Equal(5, C.Structures!.RegenHp(ModuleKind.Defence));
        Assert.Equal(6, w.Network.ModuleHp(s, 0, 0, 0) - w0); // 1 hit point every 10 s
        Assert.Equal(30, w.Network.ModuleHp(s, 1, 0, 0) - d0); // 5 every 10 s
    }

}
