using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--load-bench</c>: how much the thrust throttle shrinks a save. A scripted pilot steers the starter ship by "mouse" for 30 minutes of
/// game time at 60 frames a second (the view turns all the time, W is held, now and then a strafe, a straight stretch and a stop), once
/// with the old sending rule (every change, at most 30 a second), once with <see cref="ThrustThrottle"/> and once parked (no thrust at all, the floor). Each run saves, reloads
/// and prints the command counts, the save size and the load time. The simulation is untouched; only what the game asks of it differs.
/// </summary>
public partial class Play
{
    private const int BenchMinutes = 30;

    private void RunLoadBench()
    {
        for (int pass = 0; pass < 2; pass++) // the first pass only warms the JIT up; its numbers are not printed
        foreach (var rule in new[] { "parked", "old", "throttled" })
        {
            var world = SimWorld.CreateGalaxy(_content, GameMode.Campaign, ScriptedSeed, threats: false, stationRules: false, structures: true);
            world.Network.EnsurePlayer(Player);
            bool throttled = rule == "throttled";
            var throttle = new ThrustThrottle();
            var last = (X: 0, Y: 0, Z: 0);
            double lastAt = double.NegativeInfinity;
            int sent = 0;
            long ticks = (long)BenchMinutes * 60 * Units.TicksPerSecond;
            var clock = Stopwatch.StartNew();
            for (long tick = 0; tick < ticks; tick++)
            {
                for (int frame = 0; frame < 2; frame++) // 60 frames a second against 30 ticks a second
                {
                    double now = (tick + frame * 0.5) / Units.TicksPerSecond;
                    (int X, int Y, int Z) wanted = rule == "parked" ? (0, 0, 0) : BenchThrust(now);
                    if (throttled)
                    {
                        if (throttle.Offer(wanted, now) is { } t) { world.Submit(Player, new SetThrust(t.X, t.Y, t.Z)); sent++; }
                    }
                    else if (wanted != last && (wanted == (0, 0, 0) || now - lastAt >= 1.0 / 30.0)) // the old rule
                    {
                        last = wanted;
                        lastAt = now;
                        world.Submit(Player, new SetThrust(wanted.X, wanted.Y, wanted.Z));
                        sent++;
                    }
                }
                world.Step();
            }
            double playSeconds = clock.Elapsed.TotalSeconds;

            var ms = new MemoryStream();
            SaveGame.Write(ms, world, _content, GameMode.Campaign, ScriptedSeed, threats: false, Player, stationRules: false, structures: true);
            byte[] bytes = ms.ToArray();
            int logged = world.Log.Count;
            int thrustLogged = world.Log.Count(c => c.Command is SetThrust);

            var load = Stopwatch.StartNew();
            var loaded = SaveGame.Read(new MemoryStream(bytes), _content, out var info);
            load.Stop();
            bool same = loaded.Checksum() == world.Checksum() && loaded.Tick == world.Tick;
            if (pass == 1) GD.Print($"LOADBENCH rule={rule} game_minutes={BenchMinutes} " +
                     $"thrust_commands_sent={sent} thrust_commands_logged={thrustLogged} log_commands={logged} save_bytes={bytes.Length} load_ms={load.Elapsed.TotalMilliseconds:F0} " +
                     $"play_run_seconds={playSeconds:F1} loaded_matches={same} ticks={info.Ticks}");
        }
    }

    /// <summary>
    /// What the pilot asks for at a time (seconds): a 60-second routine repeated. 0-5 s stopped, 5-40 s W held while the view turns and
    /// pitches (with a strafe for 6 s in every 12), 40-50 s a straight stretch, 50-55 s the view turns while braking (S), 55-60 s stopped.
    /// </summary>
    private static (int X, int Y, int Z) BenchThrust(double seconds)
    {
        double t = seconds % 60.0;
        if (t < 5.0 || t >= 55.0) return (0, 0, 0);
        double yaw = 0.35 * seconds, pitch = 0.3 * Math.Sin(0.2 * seconds);
        if (t >= 40.0 && t < 50.0) { yaw = 0.35 * (seconds - (t - 40.0)); pitch = 0.1; } // a straight stretch: the view stays put
        float forward = t >= 50.0 ? -1f : 1f;
        float strafe = t >= 5.0 && t < 40.0 && (int)(t / 6.0) % 2 == 1 ? 1f : 0f;
        var fwd = new Vector3((float)(-Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(-Math.Cos(yaw) * Math.Cos(pitch)));
        var right = new Vector3((float)Math.Cos(yaw), 0f, (float)-Math.Sin(yaw));
        return ToThrust(fwd * forward + right * strafe);
    }
}
