namespace Starfoundry.Ui;

/// <summary>How healthy something is, for colouring a bar.</summary>
public enum HealthBand
{
    Good,
    Worn,
    Critical,
}

/// <summary>The state of a build-tool cost line.</summary>
public enum ToolState
{
    Free,
    Affordable,
    Short,
    Locked,
}

/// <summary>Small engine-free decisions of the HUD that are worth a test.</summary>
public static class HudFormat
{
    /// <summary>Good above 60 percent, worn above 30, critical at or below; an empty maximum counts as critical.</summary>
    public static HealthBand Band(int value, int max)
    {
        if (max <= 0) return HealthBand.Critical;
        float f = Math.Clamp(value / (float)max, 0f, 1f);
        return f > 0.6f ? HealthBand.Good : f > 0.3f ? HealthBand.Worn : HealthBand.Critical;
    }

    public static ToolState Tool(bool locked, bool hasCost, bool canPay) =>
        locked ? ToolState.Locked : !hasCost ? ToolState.Free : canPay ? ToolState.Affordable : ToolState.Short;
}
