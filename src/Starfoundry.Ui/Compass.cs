using System.Text;

namespace Starfoundry.Ui;

/// <summary>
/// The heading readout of the flight HUD. North is the world's -Z axis (the way the cockpit looks at yaw 0) and east is +X,
/// so the letters match the axes of the map. Engine-free.
/// </summary>
public static class Compass
{
    private static readonly string[] Letters = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    /// <summary>Heading in degrees, 0 = north, 90 = east, in [0, 360), from the horizontal part of a view direction.</summary>
    public static float HeadingDegrees(float forwardX, float forwardZ)
    {
        if (MathF.Abs(forwardX) < 1e-6f && MathF.Abs(forwardZ) < 1e-6f) return 0f; // looking straight up or down: no heading
        float deg = MathF.Atan2(forwardX, -forwardZ) * 180f / MathF.PI;
        deg %= 360f;
        return deg < 0f ? deg + 360f : deg;
    }

    /// <summary>The nearest of the eight points (N, NE, E, ...) for a heading.</summary>
    public static string Letter(float degrees)
    {
        int i = (int)MathF.Round((((degrees % 360f) + 360f) % 360f) / 45f) % 8;
        return Letters[i];
    }

    /// <summary>
    /// A strip of text around the heading: a mark every 15 degrees, a letter on every 45, the middle slot being where you look.
    /// <paramref name="slotsEachSide"/> slots to each side, so the strip spans 15 * (2n + 1) degrees.
    /// </summary>
    public static string Strip(float degrees, int slotsEachSide = 4)
    {
        int centre = (int)MathF.Round(degrees / 15f);
        var sb = new StringBuilder();
        for (int s = -slotsEachSide; s <= slotsEachSide; s++)
        {
            int slot = ((centre + s) % 24 + 24) % 24; // 24 slots of 15 degrees
            string token = slot % 3 == 0 ? Letters[slot / 3] : "·";
            if (s == 0) token = "[" + token + "]";
            int pad = Math.Max(0, 4 - token.Length); // fixed width, so the strip does not jitter as it turns
            sb.Append(' ', pad / 2).Append(token).Append(' ', pad - pad / 2);
        }
        return sb.ToString();
    }
}
