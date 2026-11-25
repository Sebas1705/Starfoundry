using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>Proves the pure-integer replacements equal the double-based originals they replaced (the floating point seed was banned for determinism).</summary>
public class IntMathTests
{
    // The original implementations, kept here only as the reference.
    private static long OldISqrt(long n)
    {
        if (n <= 0) return 0;
        long x = (long)Math.Sqrt(n);
        while (x * x > n) x--;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }

    private static int OldFloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
    private static int OldCeilDiv(int a, int b) => (int)Math.Ceiling(a / (double)b);

    [Fact, Trait("Tier", "Slow")]
    public void ISqrt_matches_the_old_version_for_every_small_value()
    {
        for (long v = -5; v <= 3_000_000; v++) Assert.Equal(OldISqrt(v), IntMath.ISqrt(v));
    }

    [Fact, Trait("Tier", "Slow")]
    public void ISqrt_matches_the_old_version_around_every_square_up_to_the_sim_range()
    {
        for (long r = 0; r <= 4_000_000; r += 1)
        {
            long sq = r * r;
            foreach (long v in new[] { sq - 1, sq, sq + 1 }) Assert.Equal(OldISqrt(v), IntMath.ISqrt(v));
        }
    }

    [Fact]
    public void ISqrt_matches_the_old_version_for_random_and_edge_values()
    {
        var rng = new System.Random(12345);
        foreach (long v in new[] { long.MinValue, -1, 0, 1, 2, 3, 4, int.MaxValue, (long)int.MaxValue + 1, uint.MaxValue, 1L << 40, (1L << 52) - 1, 1L << 52, (1L << 53) + 1, long.MaxValue / 2, long.MaxValue - 1, long.MaxValue })
        {
            if (v > 3_037_000_499L * 3_037_000_499L) continue; // the old loop overflows past this; sim values stay far below
            Assert.Equal(OldISqrt(v), IntMath.ISqrt(v));
        }
        for (int i = 0; i < 1_000_000; i++)
        {
            long v = rng.NextInt64(-1_000_000, 9_000_000_000_000_000_000L >> rng.Next(0, 62));
            if (v > 3_037_000_499L * 3_037_000_499L) continue;
            Assert.Equal(OldISqrt(v), IntMath.ISqrt(v));
        }
    }

    [Fact]
    public void ISqrt_is_the_exact_floor_root_over_the_full_long_range()
    {
        var rng = new System.Random(7);
        for (int i = 0; i < 1_000_000; i++)
        {
            long v = rng.NextInt64(0, long.MaxValue) >> rng.Next(0, 63);
            long r = IntMath.ISqrt(v);
            Assert.True(r >= 0 && r * r <= v && (r + 1 > 3_037_000_499L || (r + 1) * (r + 1) > v), $"v={v} r={r}");
        }
        Assert.Equal(3_037_000_499L, IntMath.ISqrt(long.MaxValue));
    }

    [Fact, Trait("Tier", "Slow")]
    public void FloorDiv_and_CeilDiv_match_the_old_version_over_the_sim_range()
    {
        for (int b = 1; b <= 64; b++)
            for (int a = -40_000; a <= 40_000; a++)
            {
                Assert.Equal(OldFloorDiv(a, b), IntMath.FloorDiv(a, b));
                Assert.Equal(OldCeilDiv(a, b), IntMath.CeilDiv(a, b));
            }
    }

    [Fact, Trait("Tier", "Slow")]
    public void FloorDiv_and_CeilDiv_match_the_old_version_for_random_and_edge_values()
    {
        var rng = new System.Random(99);
        foreach (int a in new[] { int.MinValue, int.MinValue + 1, -1, 0, 1, int.MaxValue - 1, int.MaxValue })
            foreach (int b in new[] { 1, 2, 3, 7, 1000, int.MaxValue })
            {
                Assert.Equal(OldFloorDiv(a, b), IntMath.FloorDiv(a, b));
                Assert.Equal(OldCeilDiv(a, b), IntMath.CeilDiv(a, b));
            }
        for (int i = 0; i < 1_000_000; i++)
        {
            int a = rng.Next(int.MinValue, int.MaxValue), b = rng.Next(1, rng.Next(2) == 0 ? 100 : int.MaxValue);
            Assert.Equal(OldFloorDiv(a, b), IntMath.FloorDiv(a, b));
            Assert.Equal(OldCeilDiv(a, b), IntMath.CeilDiv(a, b));
        }
    }
}
