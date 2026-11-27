namespace Starfoundry.Ui;

/// <summary>
/// The graphics quality setting (client only; it never reaches the simulation). Low is for old GPUs and the Compatibility renderer, Medium is the
/// default, High adds a second shadow cascade, MSAA 4x and ambient occlusion. See docs/prototypes/lighting-pass.md.
/// </summary>
public enum GraphicsQuality { Low, Medium, High }

public static class GraphicsQualities
{
    public const GraphicsQuality Default = GraphicsQuality.Medium;

    public static string Id(GraphicsQuality q) => q switch { GraphicsQuality.Low => "low", GraphicsQuality.High => "high", _ => "medium" };

    public static bool TryParse(string? text, out GraphicsQuality q)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "low": q = GraphicsQuality.Low; return true;
            case "medium": q = GraphicsQuality.Medium; return true;
            case "high": q = GraphicsQuality.High; return true;
            default: q = Default; return false;
        }
    }

    public static GraphicsQuality Next(GraphicsQuality q) => q switch { GraphicsQuality.Low => GraphicsQuality.Medium, GraphicsQuality.Medium => GraphicsQuality.High, _ => GraphicsQuality.Low };

    /// <summary>What a level turns on. Pure data, so the numbers are testable without the engine.</summary>
    public static QualityProfile ProfileOf(GraphicsQuality q) => q switch
    {
        GraphicsQuality.Low => new QualityProfile(Shadows: false, ShadowAtlas: 1024, ShadowDistance: 0f, ShadowSplits: 1, Msaa: 0, Ssao: false, AmbientEnergy: 0.9f, FogSunScatter: 0f, InteriorExtras: false, SunGlare: false),
        GraphicsQuality.High => new QualityProfile(Shadows: true, ShadowAtlas: 4096, ShadowDistance: 300f, ShadowSplits: 2, Msaa: 4, Ssao: true, AmbientEnergy: 0.6f, FogSunScatter: 0.2f, InteriorExtras: true, SunGlare: true),
        _ => new QualityProfile(Shadows: true, ShadowAtlas: 2048, ShadowDistance: 170f, ShadowSplits: 1, Msaa: 2, Ssao: false, AmbientEnergy: 0.6f, FogSunScatter: 0.12f, InteriorExtras: true, SunGlare: true),
    };
}

/// <summary>The renderer switches of one <see cref="GraphicsQuality"/>. Distances are client units (cells); <paramref name="Msaa"/> is 0, 2 or 4 samples.</summary>
public readonly record struct QualityProfile(bool Shadows, int ShadowAtlas, float ShadowDistance, int ShadowSplits, int Msaa, bool Ssao, float AmbientEnergy, float FogSunScatter, bool InteriorExtras, bool SunGlare);

/// <summary>
/// The key light of a star type: the colour and strength of its sun, whether a second star shines (binary) and whether it flickers (pulsar).
/// A client-only table keyed by the star id of <c>world.star_types</c>, so the content hash and the simulation are untouched; ids it does not know
/// (a mod's star) fall back to the yellow dwarf. Values are *proposal*, tuned by eye (docs/prototypes/lighting-pass.md).
/// </summary>
public readonly record struct StarLight(float R, float G, float B, float Energy, bool Binary = false, float SecondR = 0f, float SecondG = 0f, float SecondB = 0f, float PulseDepth = 0f)
{
    public static readonly StarLight YellowDwarf = new(1.00f, 0.95f, 0.82f, 1.25f);

    public static StarLight For(string? starId) => starId switch
    {
        "core:yellow_dwarf" => YellowDwarf,
        "core:red_dwarf" => new StarLight(1.00f, 0.60f, 0.40f, 1.0f),
        "core:orange_star" => new StarLight(1.00f, 0.78f, 0.55f, 1.15f),
        "core:blue_giant" => new StarLight(0.62f, 0.76f, 1.00f, 1.6f),
        "core:binary_star" => new StarLight(1.00f, 0.88f, 0.70f, 0.85f, Binary: true, SecondR: 0.70f, SecondG: 0.80f, SecondB: 1.00f),
        "core:pulsar" => new StarLight(0.78f, 0.88f, 1.00f, 1.35f, PulseDepth: 0.08f),
        _ => YellowDwarf,
    };
}
