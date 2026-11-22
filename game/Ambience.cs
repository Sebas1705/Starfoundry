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

const mat3 ROT = mat3(vec3(0.36, 0.48, -0.8), vec3(-0.8, 0.6, 0.0), vec3(0.48, 0.64, 0.6));

float hash13(vec3 p) {
    uvec3 q = uvec3(ivec3(floor(p + 0.5)) + ivec3(int(seed_offset * 7.0)));
    q *= uvec3(1597334673u, 3812015801u, 2798796415u);
    uint n = (q.x ^ q.y ^ q.z) * 1597334673u;
    return float(n >> 8u) * (1.0 / 16777215.0);
}

vec3 hash33(vec3 p) {
    return vec3(hash13(p), hash13(p + 17.1), hash13(p + 43.7));
}

float vnoise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
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
        p = ROT * p * 2.03 + 11.7;
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

uniform vec3 p1_dir = vec3(-0.6, 0.1, 0.5);
uniform vec3 p1_a = vec3(0.8, 0.6, 0.4);
uniform vec3 p1_b = vec3(0.5, 0.3, 0.2);
uniform vec3 p1_atm = vec3(0.6, 0.7, 1.0);
uniform float p1_size = 0.14;
uniform float p1_ring = 1.0;
uniform float p1_bands = 1.0;
uniform vec3 p2_dir = vec3(0.7, -0.1, -0.3);
uniform vec3 p2_a = vec3(0.4, 0.5, 0.6);
uniform vec3 p2_b = vec3(0.3, 0.35, 0.4);
uniform vec3 p2_atm = vec3(0.5, 0.7, 1.0);
uniform float p2_size = 0.045;
uniform float p2_bands = 0.0;
uniform vec3 far_star_dir = vec3(0.1, 0.2, 0.9);
uniform vec3 far_star_color = vec3(1.0, 0.5, 0.35);

// A lit sphere on the sky: banded or rocky surface, a day/night terminator, an atmosphere rim, a faint halo and an optional ring.
// Returns rgb in xyz and the coverage in w (the halo is added with a low coverage so it blends over the stars).
vec4 body(vec3 dir, vec3 bdir, float ang, vec3 ca, vec3 cb, vec3 atm, float bands, float ring, vec3 sdir) {
    bdir = normalize(bdir);
    float c = dot(dir, bdir);
    if (c <= 0.0) return vec4(0.0);
    vec3 t1 = normalize(cross(bdir, abs(bdir.y) > 0.95 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0)));
    vec3 t2 = cross(bdir, t1);
    vec2 p = vec2(dot(dir, t1), dot(dir, t2)) / ang;
    float r = length(p);
    vec3 col = vec3(0.0);
    float a = 0.0;
    if (r < 1.0) {
        float z = sqrt(1.0 - r * r);
        vec3 n = normalize(t1 * p.x + t2 * p.y + bdir * z);
        vec3 axis = normalize(t2 + 0.35 * t1);
        float lat = dot(n, axis);
        float warp = fbm(n * 3.0 + seed_offset) * 2.2;
        float band = sin(lat * 14.0 * bands + warp * 3.0) * 0.5 + 0.5;
        float crater = fbm(n * 6.0 + 9.0);
        vec3 albedo = mix(ca, cb, mix(crater, band, bands));
        // Fine grain: turbulence along the bands (or mottling on a rocky world) and a few darker storm spots.
        float fine = fbm(n * 11.0 + 3.0 + warp);
        albedo *= 0.8 + 0.4 * fine;
        albedo *= 1.0 - 0.3 * smoothstep(0.62, 0.72, fbm(n * 2.2 + 17.0)) * bands;
        float diff = dot(n, normalize(sdir));
        float lit = smoothstep(-0.12, 0.55, diff);
        float rim = pow(1.0 - z, 3.0) * smoothstep(-0.3, 0.5, diff);
        col = albedo * (0.06 + lit * 1.05) + atm * rim * 0.9;
        a = smoothstep(1.0, 0.985, r);
    } else {
        float h = exp(-(r - 1.0) * 9.0);
        col = atm * h * 0.22;
        a = h * 0.5;
    }
    if (ring > 0.5) {
        float ca_ = 0.94, sa_ = 0.34;
        vec2 q = vec2(p.x * ca_ - p.y * sa_, p.x * sa_ + p.y * ca_);
        q.y *= 3.6;
        float rr = length(q);
        float rband = smoothstep(1.45, 1.55, rr) * smoothstep(2.35, 2.2, rr);
        float stripes = 0.7 + 0.3 * sin(rr * 17.0 + seed_offset) * sin(rr * 41.0) + 0.12 * sin(rr * 120.0 + fbm(vec3(rr * 6.0, seed_offset, 1.0)) * 6.0);
        float hidden = (q.y < 0.0 && r < 1.0) ? 0.0 : 1.0;
        float ra = rband * stripes * 0.8 * hidden;
        vec3 rc = mix(ca, cb, 0.5) * (0.35 + 0.8 * max(dot(normalize(vec3(t1 * 0.0 + t2 * 0.4 + bdir * 0.9)), normalize(sdir)), 0.0));
        col = mix(col, rc, ra);
        a = max(a, ra);
    }
    return vec4(col, a);
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
    col += (stars(dir, 90.0, 0.32) + stars(dir, 190.0, 0.28) * 0.7 + stars(dir, 340.0, 0.3) * 0.45) * clear;
    // A dense, faint star haze inside the nebula band (the unresolved galactic plane), cut by dark dust lanes.
    float lane = smoothstep(0.35, 0.7, fbm(dir * 4.0 + 21.0));
    col += mix(vec3(0.8, 0.85, 1.0), nebula_color, 0.4) * band * lane * 0.05 * (0.5 + detail);
    col *= 1.0 - 0.45 * band * smoothstep(0.55, 0.8, fbm(dir * 5.5 + 40.0));

    // A far red/blue giant: a point with a soft halo.
    float fs = max(dot(dir, normalize(far_star_dir)), 0.0);
    col += far_star_color * (smoothstep(0.99985, 0.99995, fs) * 3.0 + pow(fs, 900.0) * 0.5 + pow(fs, 60.0) * 0.05);

    // Distant bodies, lit by the sun of the system.
    vec4 b1 = body(dir, p1_dir, p1_size, p1_a, p1_b, p1_atm, p1_bands, p1_ring, sun_dir);
    col = mix(col, b1.rgb, clamp(b1.a, 0.0, 1.0));
    vec4 b2 = body(dir, p2_dir, p2_size, p2_a, p2_b, p2_atm, p2_bands, 0.0, sun_dir);
    col = mix(col, b2.rgb, clamp(b2.a, 0.0, 1.0));

    // Sun: a bright core, a tight halo, a wide glow and a faint horizontal lens streak.
    float s = max(dot(dir, normalize(sun_dir)), 0.0);
    float disc = smoothstep(0.9985, 0.9993, s);
    float halo = pow(s, 220.0) * 0.9 + pow(s, 24.0) * 0.18 + pow(s, 6.0) * 0.05;
    vec3 sd = normalize(sun_dir);
    vec3 side = normalize(cross(sd, vec3(0.0, 1.0, 0.0)) + vec3(0.0001));
    float along = dot(dir - sd * dot(dir, sd), side);
    float streak = exp(-abs(dot(dir - sd * dot(dir, sd), cross(sd, side))) * 90.0) * exp(-abs(along) * 5.0) * step(0.0, dot(dir, sd));
    col += sun_color * (disc * 6.0 + halo + streak * 0.07);

    COLOR = col;
}
";

    private static WorldEnvironment? _env;
    private static ShaderMaterial? _skyMaterial;
    private static DirectionalLight3D? _sun, _fill;
    private static GpuParticles3D? _dust;
    private static Godot.Environment? _environment;
    private static GpuParticles3D? _streaks;
    private static StandardMaterial3D? _streakMaterial;
    private static Vector3 _tracked;

    /// <summary>Tells the ambience where the ship is (world position); its frame-to-frame speed drives the motion streaks and the haze.</summary>
    public static void Track(Vector3 shipPosition) => _tracked = shipPosition;

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
            AmbientLightEnergy = 1.35f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.05f,
            TonemapWhite = 6.0f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowStrength = 1.0f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 1.0f,
            GlowHdrScale = 2.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            // A very thin tinted haze: distant deposits and stations fall into the dark with a hint of the system colour.
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogDensity = 0.0012f,
            FogSkyAffect = 0.0f,
            FogLightEnergy = 0.8f,
            AdjustmentEnabled = true,
            AdjustmentContrast = 1.06f,
            AdjustmentSaturation = 1.08f,
        };
        _environment = environment;
        _env = new WorldEnvironment { Environment = environment };
        root.AddChild(_env);

        _sun = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = false };
        root.AddChild(_sun);
        // A faint cool fill from the opposite side: the faces turned away from the star read as dim metal, not as black cut-outs.
        _fill = new DirectionalLight3D { LightEnergy = 0.3f, ShadowEnabled = false, LightSpecular = 0.2f };
        root.AddChild(_fill);

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
        _streaks = BuildStreaks();
        root.AddChild(_streaks);
        var lastPos = Vector3.Zero;
        bool hasLast = false;
        float speed = 0f;
        void Update()
        {
            Follow();
            var cam = _dust?.GetViewport()?.GetCamera3D();
            if (cam is null || _streaks is null || !GodotObject.IsInstanceValid(_streaks)) return;
            double dt = tree.Root.GetProcessDeltaTime();
            var p = _tracked;
            if (hasLast && dt > 1e-4 && (p - lastPos).Length() < 120f * (float)dt) // a bigger step is a teleport (a jump, a respawn), not flight
            {
                var v = (p - lastPos) / (float)dt;
                speed = Mathf.Lerp(speed, v.Length(), (float)Math.Min(1.0, dt * 4.0));
                if (v.Length() > 0.5f)
                {
                    // The specks hang in the world and the ship moves through them, so streaks lie along the travel direction.
                    ((ParticleProcessMaterial)_streaks.ProcessMaterial).Direction = v.Normalized();
                }
            }
            _streaks.GlobalPosition = cam.GlobalPosition;
            lastPos = p;
            hasLast = true;
            float k = Mathf.Clamp((speed - StreakStartSpeed) / (StreakFullSpeed - StreakStartSpeed), 0f, 1f);
            _streaks.Emitting = k > 0.02f;
            _streaks.Visible = k > 0.02f;
            if (_streakMaterial is not null) _streakMaterial.AlbedoColor = new Color(0.8f, 0.88f, 1f, 0.5f * k);
            if (_environment is not null) _environment.FogDensity = 0.0012f + 0.0012f * k;
        }
        tree.ProcessFrame += Update;
        root.TreeExiting += () => tree.ProcessFrame -= Update;

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
        var baseColor = Color.FromHsv(hue, 0.45f, 0.055f);
        var nebula = Color.FromHsv((hue + 0.12f + rng.Randf() * 0.25f) % 1f, 0.55f + rng.Randf() * 0.3f, 0.7f);
        var sunColor = Color.FromHsv(0.08f + rng.Randf() * 0.1f, 0.15f + rng.Randf() * 0.3f, 1f);

        // The sun sits above the horizon (so the light comes from above) at a seed-dependent azimuth and height.
        float az = rng.Randf() * Mathf.Tau;
        float el = Mathf.DegToRad(12f + rng.Randf() * 50f);
        var sunDir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).Normalized();
        var bandNormal = new Vector3(rng.Randf() - 0.5f, rng.Randf() * 0.8f + 0.2f, rng.Randf() - 0.5f).Normalized();

        _skyMaterial.SetShaderParameter("base_color", new Vector3(baseColor.R, baseColor.G, baseColor.B));
        _skyMaterial.SetShaderParameter("nebula_color", new Vector3(nebula.R, nebula.G, nebula.B));
        _skyMaterial.SetShaderParameter("sun_color", new Vector3(sunColor.R, sunColor.G, sunColor.B));
        _skyMaterial.SetShaderParameter("sun_dir", sunDir);
        _skyMaterial.SetShaderParameter("band_normal", bandNormal);
        _skyMaterial.SetShaderParameter("seed_offset", rng.Randf() * 40f);

        // Two distant bodies (a gas giant, often ringed, and a small world) and a far giant star, away from the sun.
        Vector3 Spot(float minElDeg, float maxElDeg, Vector3 avoid, float maxDot)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = rng.Randf() * Mathf.Tau, e = Mathf.DegToRad(minElDeg + rng.Randf() * (maxElDeg - minElDeg));
                var d = new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a));
                if (d.Dot(avoid) < maxDot) return d;
            }
            return -avoid;
        }
        var g = Spot(-25f, 35f, sunDir, 0.4f);
        var gasHue = rng.Randf();
        var ga = Color.FromHsv(gasHue, 0.35f + rng.Randf() * 0.4f, 0.85f);
        var gb = Color.FromHsv((gasHue + 0.05f + rng.Randf() * 0.08f) % 1f, 0.5f + rng.Randf() * 0.3f, 0.45f);
        var gatm = Color.FromHsv((gasHue + 0.5f) % 1f, 0.3f, 1f);
        _skyMaterial.SetShaderParameter("p1_dir", g);
        _skyMaterial.SetShaderParameter("p1_a", new Vector3(ga.R, ga.G, ga.B));
        _skyMaterial.SetShaderParameter("p1_b", new Vector3(gb.R, gb.G, gb.B));
        _skyMaterial.SetShaderParameter("p1_atm", new Vector3(gatm.R, gatm.G, gatm.B));
        _skyMaterial.SetShaderParameter("p1_size", 0.05f + rng.Randf() * 0.05f);
        _skyMaterial.SetShaderParameter("p1_ring", rng.Randf() < 0.45f ? 1f : 0f);
        _skyMaterial.SetShaderParameter("p1_bands", 1f);
        var w = Spot(-30f, 40f, g, 0.8f);
        var wa = Color.FromHsv(rng.Randf(), 0.25f + rng.Randf() * 0.35f, 0.7f);
        var wb = wa.Darkened(0.5f);
        _skyMaterial.SetShaderParameter("p2_dir", w);
        _skyMaterial.SetShaderParameter("p2_a", new Vector3(wa.R, wa.G, wa.B));
        _skyMaterial.SetShaderParameter("p2_b", new Vector3(wb.R, wb.G, wb.B));
        _skyMaterial.SetShaderParameter("p2_atm", new Vector3(0.5f, 0.75f, 1f));
        _skyMaterial.SetShaderParameter("p2_size", 0.03f + rng.Randf() * 0.035f);
        _skyMaterial.SetShaderParameter("p2_bands", rng.Randf() < 0.3f ? 0.6f : 0f);
        var fsc = rng.Randf() < 0.5f ? new Color(1f, 0.45f, 0.3f) : new Color(0.55f, 0.7f, 1f);
        _skyMaterial.SetShaderParameter("far_star_dir", Spot(-10f, 50f, sunDir, 0.2f));
        _skyMaterial.SetShaderParameter("far_star_color", new Vector3(fsc.R, fsc.G, fsc.B));
        if (_environment is not null) _environment.FogLightColor = baseColor.Lerp(nebula, 0.3f).Lightened(0.1f);

        // The light travels from the sun towards the scene: its forward (-Z) axis points opposite the sun direction.
        var up = Mathf.Abs(sunDir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        _sun.Basis = Basis.LookingAt(-sunDir, up);
        _sun.LightColor = sunColor.Lerp(Colors.White, 0.4f);
        if (_fill is not null)
        {
            _fill.Basis = Basis.LookingAt(sunDir * new Vector3(0.9f, -0.7f, 0.9f) + new Vector3(0f, 0.15f, 0f), up);
            _fill.LightColor = new Color(0.62f, 0.72f, 1f);
        }
    }

    private const float StreakStartSpeed = 4f, StreakFullSpeed = 22f;

    /// <summary>Long thin specks aligned with the travel direction, faded in with speed (the dust seen as motion lines).</summary>
    private static GpuParticles3D BuildStreaks()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(26f, 18f, 26f),
            Direction = new Vector3(0f, 0f, -1f),
            Spread = 0f,
            InitialVelocityMin = 0.01f,
            InitialVelocityMax = 0.02f,
            Gravity = Vector3.Zero,
            ParticleFlagAlignY = true,
            ScaleMin = 0.6f,
            ScaleMax = 1.6f,
        };
        _streakMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            AlbedoColor = new Color(0.8f, 0.88f, 1f, 0f),
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        return new GpuParticles3D
        {
            Amount = 220,
            Lifetime = 2.5,
            Preprocess = 2.5,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.025f, 2.4f), Material = _streakMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-32f, -22f, -32f), new Vector3(64f, 44f, 64f)),
        };
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
