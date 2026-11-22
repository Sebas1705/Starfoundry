using System;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// Space ambience: a procedural sky (stars, a soft nebula band, a bright sun disc) whose hue comes from the system seed,
/// a directional light aligned with the sun disc, sky ambient light, soft glow, tonemapping, and a small cloud of drifting dust
/// that follows the camera so movement has a sense of speed and depth.
/// </summary>
public static class Ambience
{
    private const string SkyShader = @"
shader_type sky;

uniform vec3 base_color = vec3(0.02, 0.03, 0.07);
uniform vec3 nebula_color = vec3(0.35, 0.18, 0.55);
uniform vec3 band_normal = vec3(0.0, 1.0, 0.0);
uniform vec3 sun_dir = vec3(0.4, 0.6, -0.7);
uniform vec3 sun_color = vec3(1.0, 0.92, 0.75);
uniform float seed_offset = 0.0;

float hash13(vec3 p) {
    p = fract(p * 0.1031 + seed_offset);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

vec3 hash33(vec3 p) {
    return vec3(hash13(p), hash13(p + 17.1), hash13(p + 43.7));
}

float vnoise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = hash13(i);
    float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
    return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
               mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

float fbm(vec3 p) {
    float a = 0.5;
    float s = 0.0;
    for (int i = 0; i < 5; i++) {
        s += a * vnoise(p);
        p = p * 2.03 + 11.7;
        a *= 0.5;
    }
    return s;
}

// One layer of point stars: a random point per cell, a tiny soft dot, a few bright ones.
vec3 stars(vec3 dir, float density, float size) {
    vec3 p = dir * density;
    vec3 cell = floor(p);
    vec3 r = hash33(cell);
    vec3 centre = cell + 0.2 + 0.6 * hash33(cell + 5.3);
    float present = step(0.93, r.x);
    float d = length(p - centre);
    float dot_ = smoothstep(size, 0.0, d);
    float temp = r.y;
    vec3 tint = mix(vec3(1.0, 0.8, 0.6), vec3(0.7, 0.85, 1.0), temp);
    return tint * dot_ * present * (0.4 + 1.6 * r.z * r.z);
}

void sky() {
    vec3 dir = normalize(EYEDIR);
    float up = dir.y * 0.5 + 0.5;
    vec3 col = base_color * (0.55 + 0.9 * up);

    // A soft band of nebula, thickest along the great circle around band_normal, broken up by noise.
    float lat = dot(dir, normalize(band_normal));
    float band = exp(-lat * lat * 9.0);
    float clouds = fbm(dir * 2.6 + seed_offset * 7.0);
    float detail = fbm(dir * 7.0 + 3.1);
    float neb = band * smoothstep(0.30, 0.85, clouds) * (0.6 + 0.8 * detail);
    col += nebula_color * neb * 0.55;
    col += nebula_color.zxy * band * smoothstep(0.5, 1.0, detail) * 0.18;

    // Stars: two layers, dimmed where the nebula is bright.
    float clear = 1.0 - 0.6 * neb;
    col += (stars(dir, 90.0, 0.32) + stars(dir, 190.0, 0.28) * 0.7) * clear;

    // Sun: a bright core, a tight halo and a wide glow.
    float s = max(dot(dir, normalize(sun_dir)), 0.0);
    float disc = smoothstep(0.9985, 0.9993, s);
    float halo = pow(s, 220.0) * 0.9 + pow(s, 24.0) * 0.18;
    col += sun_color * (disc * 6.0 + halo);

    COLOR = col;
}
";

    private static WorldEnvironment? _env;
    private static ShaderMaterial? _skyMaterial;
    private static DirectionalLight3D? _sun;
    private static GpuParticles3D? _dust;
    private static ulong _currentSeed = ulong.MaxValue;

    /// <summary>A stable seed for a system of a galaxy (so each system gets its own sky).</summary>
    public static ulong SystemSeed(ulong galaxySeed, int system) => Mix(galaxySeed ^ ((ulong)(system + 1) * 0x9E3779B97F4A7C15UL));

    /// <summary>Builds the environment, sun light and dust under <paramref name="root"/> and tints them for the seed.</summary>
    public static void Build(Node3D root, ulong systemSeed)
    {
        _skyMaterial = new ShaderMaterial { Shader = new Shader { Code = SkyShader } };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMaterial, RadianceSize = Sky.RadianceSizeEnum.Size128 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1.1f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.5f,
            GlowStrength = 0.9f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.2f,
        };
        _env = new WorldEnvironment { Environment = environment };
        root.AddChild(_env);

        _sun = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = false };
        root.AddChild(_sun);

        _dust = BuildDust();
        root.AddChild(_dust);

        // The dust cloud follows the camera so the specks stay around the viewer while they stay put in the world.
        var tree = root.GetTree();
        void Follow()
        {
            if (_dust is null || !GodotObject.IsInstanceValid(_dust)) return;
            var cam = _dust.GetViewport()?.GetCamera3D();
            if (cam is not null) _dust.GlobalPosition = cam.GlobalPosition;
        }
        tree.ProcessFrame += Follow;
        root.TreeExiting += () => tree.ProcessFrame -= Follow;

        _currentSeed = ulong.MaxValue;
        Retint(systemSeed);
    }

    /// <summary>Gives the sky, the sun and the dust the colours of another system.</summary>
    public static void Retint(ulong systemSeed)
    {
        if (_skyMaterial is null || _sun is null || systemSeed == _currentSeed) return;
        _currentSeed = systemSeed;
        var rng = new RandomNumberGenerator { Seed = systemSeed };

        float hue = rng.Randf();
        var baseColor = Color.FromHsv(hue, 0.55f, 0.11f);
        var nebula = Color.FromHsv((hue + 0.12f + rng.Randf() * 0.25f) % 1f, 0.6f + rng.Randf() * 0.3f, 0.75f);
        var sunColor = Color.FromHsv(0.08f + rng.Randf() * 0.1f, 0.15f + rng.Randf() * 0.3f, 1f);

        // The sun sits above the horizon (so the light comes from above) at a seed-dependent azimuth and height.
        float az = rng.Randf() * Mathf.Tau;
        float el = Mathf.DegToRad(25f + rng.Randf() * 45f);
        var sunDir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).Normalized();
        var bandNormal = new Vector3(rng.Randf() - 0.5f, rng.Randf() * 0.8f + 0.2f, rng.Randf() - 0.5f).Normalized();

        _skyMaterial.SetShaderParameter("base_color", new Vector3(baseColor.R, baseColor.G, baseColor.B));
        _skyMaterial.SetShaderParameter("nebula_color", new Vector3(nebula.R, nebula.G, nebula.B));
        _skyMaterial.SetShaderParameter("sun_color", new Vector3(sunColor.R, sunColor.G, sunColor.B));
        _skyMaterial.SetShaderParameter("sun_dir", sunDir);
        _skyMaterial.SetShaderParameter("band_normal", bandNormal);
        _skyMaterial.SetShaderParameter("seed_offset", rng.Randf() * 40f);

        // The light travels from the sun towards the scene: its forward (-Z) axis points opposite the sun direction.
        var up = Mathf.Abs(sunDir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        _sun.Basis = Basis.LookingAt(-sunDir, up);
        _sun.LightColor = sunColor.Lerp(Colors.White, 0.4f);
    }

    private static GpuParticles3D BuildDust()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(45f, 30f, 45f),
            Direction = new Vector3(0.3f, -0.1f, 0.2f),
            Spread = 180f,
            InitialVelocityMin = 0.1f,
            InitialVelocityMax = 0.5f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.5f,
            ScaleMax = 1.4f,
        };
        // A soft round dot: a plain quad showed up as a small light square when a speck drifted close to the camera.
        var dot = new Gradient();
        dot.SetColor(0, new Color(1f, 1f, 1f, 1f));
        dot.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var dotTex = new GradientTexture2D { Gradient = dot, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 32, Height = 32 };
        var mesh = new QuadMesh { Size = new Vector2(0.09f, 0.09f) };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoTexture = dotTex,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.75f, 0.82f, 1f, 0.55f),
            VertexColorUseAsAlbedo = false,
            NoDepthTest = false,
        };
        return new GpuParticles3D
        {
            Amount = 350,
            Lifetime = 14.0,
            Preprocess = 14.0,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-50f, -35f, -50f), new Vector3(100f, 70f, 100f)),
        };
    }

    private static ulong Mix(ulong x)
    {
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return x;
    }
}
