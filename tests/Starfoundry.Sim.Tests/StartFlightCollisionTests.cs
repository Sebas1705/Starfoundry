using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// A gentle flight from the start of a new campaign never hurts the starter ship. Rocks are static, so the closing speed of a hit is at most the speed of the ship:
/// below the safe speed (1.25 cells/s) a bump is harmless whatever module touches the rock, the dock module on the -X flank included. A "-1 to Dock" collision seen
/// at a low speed on the HUD was a hit at more than the safe speed whose velocity the rock then took away (the HUD shows the speed after the hit).
/// </summary>
public class StartFlightCollisionTests
{
    private static readonly ContentDb Shipped = TestContent.CoreFloor;

    private static (int X, int Y, int Z) Toward(int fx, int fy, int fz, int thrust)
    {
        double n = Math.Sqrt((double)fx * fx + (double)fy * fy + (double)fz * fz);
        return n == 0 ? (0, 0, 0) : ((int)(fx / n * thrust), (int)(fy / n * thrust), (int)(fz / n * thrust));
    }

    private static List<(int X, int Y, int Z)> HomeRocks(SimWorld w)
    {
        var rocks = new List<(int, int, int)>();
        for (int d = 0; d < w.Network.DepositCount; d++)
        {
            var p = w.Network.DepositPosition(d);
            if (Math.Abs(p.X) < 90 && Math.Abs(p.Y) < 90 && Math.Abs(p.Z) < 90 && w.Network.GetDepositInfo(d).Revealed) rocks.Add(p);
        }
        return rocks;
    }

    [Fact]
    public void A_slow_flight_into_the_rocks_near_home_does_no_damage_for_every_preset_and_seed()
    {
        var catalog = TestContent.ScenariosFloor;
        int bumps = 0, flights = 0;
        foreach (var preset in new[] { "relaxed", "normal", "hard", "brutal" })
            foreach (ulong seed in new ulong[] { 1, 7919, 20261126 })
                foreach (int thrust in new[] { 100, 175 }) // about 0.7 and 1.2 cells/s with the starter ship
                {
                    var probe = SimWorld.CreateScenario(Shipped, catalog, "campaign", seed, WorldRules.Preset(preset));
                    // Straight at each rock of the home area, and along the six axes.
                    var aims = HomeRocks(probe).Concat(new (int X, int Y, int Z)[] { (0, 0, -1), (0, 0, 1), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0) }).ToList();
                    foreach (var aim in aims)
                    {
                        var w = SimWorld.CreateScenario(Shipped, catalog, "campaign", seed, WorldRules.Preset(preset));
                        var v = Toward(aim.X, aim.Y, aim.Z, thrust);
                        w.Submit(0, new SetThrust(v.X, v.Y, v.Z));
                        flights++;
                        for (int t = 0; t < 90 * Units.TicksPerSecond; t++)
                        {
                            w.Step();
                            Assert.Empty(w.Network.ModuleHits);
                            Assert.DoesNotContain(w.Network.ObstacleEvents, e => e.Kind == ObstacleEventKind.Impact);
                            bumps += w.Network.ObstacleEvents.Count(e => e.Kind == ObstacleEventKind.Bump);
                        }
                    }
                }
        Assert.True(flights > 100);
        Assert.True(bumps > 0, "the flights must reach the rocks, or the test proves nothing");
    }

    [Fact]
    public void A_hit_above_the_safe_speed_is_damage_and_the_rock_takes_the_speed_away()
    {
        var catalog = TestContent.ScenariosFloor;
        int safe = Shipped.World!.SafeSpeedUnits;
        bool hurt = false;
        for (ulong seed = 1; seed <= 10 && !hurt; seed++)
        {
            var probe = SimWorld.CreateScenario(Shipped, catalog, "campaign", seed, WorldRules.Preset("normal"));
            foreach (var rock in HomeRocks(probe))
            {
                if (hurt) break;
                var w = SimWorld.CreateScenario(Shipped, catalog, "campaign", seed, WorldRules.Preset("normal"));
                var v = Toward(rock.X, rock.Y, rock.Z, 400);
                w.Submit(0, new SetThrust(v.X, v.Y, v.Z));
                for (int t = 0; t < 60 * Units.TicksPerSecond && !hurt; t++)
                {
                    w.Step();
                    foreach (var e in w.Network.ObstacleEvents.Where(e => e.Kind == ObstacleEventKind.Impact))
                    {
                        hurt = true;
                        Assert.True(e.Speed > safe);
                        Assert.True(e.Damage >= 1);
                        var after = w.Network.GetPlayerInfo(0);
                        long speed = (long)Math.Sqrt((double)after.VelX * after.VelX + (double)after.VelY * after.VelY + (double)after.VelZ * after.VelZ);
                        Assert.True(speed < (long)e.Speed * 1000, "the rock takes the velocity into it away: the HUD reads the speed after the hit");
                    }
                }
            }
        }
        Assert.True(hurt, "a fast flight into a rock must hurt, or the test proves nothing");
    }
}
