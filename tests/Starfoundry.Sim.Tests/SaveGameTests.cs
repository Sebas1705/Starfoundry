using Starfoundry.Sim;
using Xunit;

namespace Starfoundry.Sim.Tests;

public class SaveGameTests
{
    private static readonly ContentDb C = TestContent.Core;
    private const ulong Seed = 20261120UL;

    /// <summary>A campaign galaxy world with threats, played a little with commands only.</summary>
    private static SimWorld Played(int extraTicks = 0)
    {
        var w = SimWorld.CreateGalaxy(C, GameMode.Campaign, Seed, threats: true);
        w.Network.EnsurePlayer(0);
        int ore = Enumerable.Range(0, w.Network.DepositCount).First(d => w.Network.DepositItem(d) == C.ItemIndex("core:iron_ore"));
        var (x, _, z) = w.Network.DepositPosition(ore);
        for (int i = 0; i < 30 * 12 && !w.Network.InMineRange(0, ore); i++)
        {
            int dx = Math.Abs(x * Units.CellUnits - w.Network.GetPlayerInfo(0).X) > 120 ? Math.Sign(x * Units.CellUnits - w.Network.GetPlayerInfo(0).X) : 0;
            int dz = Math.Abs(z * Units.CellUnits - w.Network.GetPlayerInfo(0).Z) > 120 ? Math.Sign(z * Units.CellUnits - w.Network.GetPlayerInfo(0).Z) : 0;
            w.Submit(0, new SetMovement(dx, 0, dz));
            w.Step();
        }
        w.Submit(0, new SetMovement(0, 0, 0));
        w.Submit(0, new StartMining(ore));
        w.Submit(0, new SetShipRecipe(C.RecipeIndex("core:ship_smelt")));
        w.Step(30 * 80);
        // Spend what the hold has on a belt and a failing command, so the log holds a rejection too.
        w.Submit(0, new PlaceSegment(x + 2, 0, z, Dir.PosX, 3, BeltTier.Mk1));
        w.Submit(0, new PlaceMachine(0, 0, 0, C.MachineIndex("core:research_lab"), -1)); // too expensive: rejected
        w.Step(30 * 10 + extraTicks);
        return w;
    }

    private static byte[] Save(SimWorld w, GameMode mode = GameMode.Campaign)
    {
        var ms = new MemoryStream();
        SaveGame.Write(ms, w, C, mode, Seed, threats: true, player: 0);
        return ms.ToArray();
    }

    [Fact]
    public void ALoadedGameIsTheSameWorldAndKeepsPlayingIdentically()
    {
        var original = Played();
        byte[] bytes = Save(original);

        var loaded = SaveGame.Read(new MemoryStream(bytes), C, out var info);
        Assert.Equal(original.Tick, loaded.Tick);
        Assert.Equal(original.Checksum(), loaded.Checksum());
        Assert.Equal((original.Tick, GameMode.Campaign, Seed, true, 0), (info.Ticks, info.Mode, info.GalaxySeed, info.Threats, info.Player));
        Assert.Equal(original.Log.Count, loaded.Log.Count);
        loaded.ValidateInvariants();

        // The same commands from here on lead to the same state.
        foreach (var w in new[] { original, loaded })
        {
            w.Submit(0, new Scan());
            w.Step(30 * 40);
        }
        Assert.Equal(original.Checksum(), loaded.Checksum());
    }

    [Fact]
    public void ALoadedGameCanBeSavedAgainAndLoadedAgain()
    {
        var original = Played();
        var once = SaveGame.Read(new MemoryStream(Save(original)), C, out _);
        once.Step(30 * 20);
        var twice = SaveGame.Read(new MemoryStream(Save(once)), C, out _);
        Assert.Equal(once.Checksum(), twice.Checksum());
        Assert.Equal(once.Tick, twice.Tick);
    }

    [Fact]
    public void PeekReadsTheHeaderWithoutReplaying()
    {
        var w = Played();
        var info = SaveGame.Peek(new MemoryStream(Save(w)));
        Assert.Equal(w.Tick, info.Ticks);
        Assert.Equal(w.Checksum(), info.Checksum);
        Assert.Equal(GameMode.Campaign, info.Mode);
    }

    [Fact]
    public void ASaveFromOtherContentIsRefused()
    {
        byte[] bytes = Save(Played());
        string other = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "core.json")).Replace("\"hp\": 400", "\"hp\": 401");
        var different = ContentDb.FromJson(other);
        Assert.NotEqual(C.Hash, different.Hash);
        var e = Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(bytes), different, out _));
        Assert.Contains("content", e.Message);
    }

    [Fact]
    public void ACorruptOrForeignFileIsRefusedInsteadOfLoadedWrong()
    {
        byte[] bytes = Save(Played());

        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }), C, out _));

        var wrongChecksum = (byte[])bytes.Clone();
        wrongChecksum[4 + 1 + 8] ^= 0xFF; // the first byte of the stored checksum
        var e = Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(wrongChecksum), C, out _));
        Assert.Contains("replay", e.Message);

        var wrongTicks = (byte[])bytes.Clone();
        wrongTicks[4 + 1] ^= 0x01; // one tick more or less than was played
        Assert.Throws<InvalidDataException>(() => SaveGame.Read(new MemoryStream(wrongTicks), C, out _));

        var truncated = bytes.Take(bytes.Length / 2).ToArray();
        Assert.ThrowsAny<Exception>(() => SaveGame.Read(new MemoryStream(truncated), C, out _));
    }

    [Fact]
    public void ASandboxWorldWithoutAGalaxyAlsoSavesAndLoads()
    {
        var w = new SimWorld(C, GameMode.Sandbox);
        w.Network.EnsurePlayer(0);
        w.Submit(0, new PlaceMachine(0, 0, 0, C.MachineIndex("core:extractor"), C.RecipeIndex("core:mine_iron")));
        w.Submit(0, new PlaceSegment(1, 0, 0, Dir.PosX, 3, BeltTier.Mk1));
        w.Step(600);
        var ms = new MemoryStream();
        SaveGame.Write(ms, w, C, GameMode.Sandbox, galaxySeed: 0, threats: false, player: 0);
        var loaded = SaveGame.Read(new MemoryStream(ms.ToArray()), C, out var info);
        Assert.Equal(0UL, info.GalaxySeed);
        Assert.Equal(w.Checksum(), loaded.Checksum());
    }

    [Fact]
    public void ABigBaseAfterOneAndAHalfHoursLoadsInSecondsAndSavesTheSameState()
    {
        // The scripted player's base at 90 minutes: three turrets, four walls, two repair bays, upgrades, waves fought.
        var bot = new Balance.BalanceBot(C, Seed) { ActionDelaySeconds = 15, Turrets = 3, Walls = 4, Bays = 2, ExtraPlateLine = true, Upgrades = 2, ObserveAfterSeconds = 90 * 60 };
        bot.Run(90 * 60);
        var w = bot.World;
        var ms = new MemoryStream();
        SaveGame.Write(ms, w, C, GameMode.Campaign, Seed, threats: true, player: 0);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var loaded = SaveGame.Read(new MemoryStream(ms.ToArray()), C, out _);
        watch.Stop();
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.True(w.Network.MachineCount > 20, "a real base, not an empty world");
        Assert.True(watch.Elapsed.TotalSeconds < 30, $"loading took {watch.Elapsed.TotalSeconds:F1} s for {w.Tick / (double)Units.TicksPerSecond / 60:F0} minutes");
    }

    [Fact]
    public void LoadingALongGameTakesSecondsNotMinutes()
    {
        // About an hour and a half of game time with a base running: the cost a player waits when loading.
        var w = Played(extraTicks: 30 * 60 * 90);
        byte[] bytes = Save(w);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var loaded = SaveGame.Read(new MemoryStream(bytes), C, out _);
        watch.Stop();
        Assert.Equal(w.Checksum(), loaded.Checksum());
        Assert.True(watch.Elapsed.TotalSeconds < 60, $"loading took {watch.Elapsed.TotalSeconds:F1} s");
    }
}
