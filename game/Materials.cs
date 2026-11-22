using System;
using System.Collections.Generic;
using Godot;

namespace Starfoundry.Game;

/// <summary>
/// The procedural material library. Every material is a <see cref="StandardMaterial3D"/> with object-space triplanar mapping, so meshes
/// need no UVs and the texture density is set in world units by <c>tile</c> (units covered by one texture repeat; 1 unit = 1 cell). The
/// vertex colour (and the MultiMesh instance colour) still multiplies the albedo, so the established colour coding reads first and the
/// texture only adds panels, wear and grain. Textures are generated once, on first use, with fixed seeds, at most 512 px with mipmaps
/// and anisotropic filtering. Materials are cached; ask for them by name, never new them per object.
/// </summary>
public static class Materials
{
    private static readonly Dictionary<string, TexSet> Sets = new();
    private static readonly Dictionary<string, StandardMaterial3D> Mats = new();

    private static TexSet Set(string key, Func<Surf> make, float normalStrength, bool emission = false)
    {
        if (!Sets.TryGetValue(key, out var t)) Sets[key] = t = make().Bake(normalStrength, emission);
        return t;
    }

    /// <summary>A triplanar material from a texture set. <paramref name="rough"/> and <paramref name="metal"/> multiply the (relative) maps.</summary>
    private static StandardMaterial3D Build(TexSet t, float tile, float rough, float metal, float normal, bool vertexColour = true)
    {
        float s = 1f / tile;
        var m = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = vertexColour,
            AlbedoColor = Colors.White,
            AlbedoTexture = t.Albedo,
            RoughnessTexture = t.Orm,
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            Roughness = rough,
            MetallicTexture = t.Orm,
            MetallicTextureChannel = BaseMaterial3D.TextureChannel.Blue,
            Metallic = metal,
            NormalEnabled = true,
            NormalTexture = t.Normal,
            NormalScale = normal,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(s, s, s),
            Uv1TriplanarSharpness = 4f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        return m;
    }

    private static StandardMaterial3D Cached(string key, Func<StandardMaterial3D> make)
    {
        if (!Mats.TryGetValue(key, out var m)) Mats[key] = m = make();
        return m;
    }

    private static float Bucket(float scale) => MathF.Pow(1.25f, MathF.Round(MathF.Log(MathF.Max(scale, 0.01f)) / MathF.Log(1.25f)));

    // ---- plating and metal ------------------------------------------------------------------------

    /// <summary>Plating for the big structures (ship and station modules): panels of about a unit, seams, rivets, wear.</summary>
    public static StandardMaterial3D Hull() => Cached("hull", () =>
        Build(Set("hull", () => Gen.HullPlating(512, 4, 1101, 3.2f), 3.0f), 4f, 0.82f, 0.30f, 0.9f));

    /// <summary>The same plating at machine scale (a panel is a quarter to half a unit): machines, drones, enemies, the player ship.</summary>
    public static StandardMaterial3D Machine() => Cached("machine", () =>
        Build(Set("machine", () => Gen.HullPlating(512, 2, 2202, 5.0f), 3.0f), 1f, 0.80f, 0.32f, 0.9f));

    /// <summary>Dark structural metal for trusses, rails, masts and frames: brushed, bolted, grooved.</summary>
    public static StandardMaterial3D Structural() => Cached("struct", () =>
        Build(Set("struct", () => Gen.Structural(256, 3303), 2.0f), 2f, 0.65f, 0.75f, 0.8f));

    /// <summary>Structural metal at item scale (ingots, plates, chips on a belt).</summary>
    public static StandardMaterial3D ItemMetal() => Cached("itemmetal", () =>
        Build(Set("struct", () => Gen.Structural(256, 3303), 2.0f), 0.5f, 0.55f, 0.6f, 0.6f));

    /// <summary>Burnt metal for nozzles, barrels and mine shells.</summary>
    public static StandardMaterial3D Burnt() => Cached("burnt", () =>
        Build(Set("burnt", () => Gen.Burnt(256, 4404), 2.5f), 1.5f, 0.75f, 0.8f, 0.9f));

    /// <summary>Cockpit glass: the existing dark-blue tint, emissive glow kept, with sheen bands and smudges on top.</summary>
    public static StandardMaterial3D Glass() => Cached("glass", () =>
    {
        var m = Build(Set("glass", () => Gen.Glass(256, 5505), 1.0f), 3f, 0.30f, 0.5f, 0.4f, false);
        m.AlbedoColor = new Color(0.10f, 0.20f, 0.36f) * 1.15f;
        m.EmissionEnabled = true;
        m.Emission = new Color(0.15f, 0.40f, 0.95f);
        m.EmissionEnergyMultiplier = 0.4f;
        return m;
    });

    // ---- lights -------------------------------------------------------------------------------------

    /// <summary>An emissive light with a soft panel mask (grid lines and a little noise). Own colour and energy; no vertex colour.</summary>
    public static StandardMaterial3D Light(Color c, float energy, bool grill = false) => Cached($"light.{grill}.{c.ToHtml()}.{energy}", () =>
    {
        var t = grill ? Set("grill", () => Gen.Grill(128, 6606), 1.5f, true) : Set("lightpanel", () => Gen.LightPanel(128, 6607), 1.0f, true);
        var m = Build(t, grill ? 0.5f : 0.5f, 0.6f, 0.2f, 0.6f, false);
        m.AlbedoColor = c * 0.45f;
        m.EmissionEnabled = true;
        m.Emission = c;
        m.EmissionEnergyMultiplier = energy;
        m.EmissionTexture = t.Emission;
        m.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
        return m;
    });

    // ---- natural ------------------------------------------------------------------------------------

    /// <summary>Rock. <paramref name="nodeScale"/> is the scale of the node the mesh hangs from, so the texture keeps the same density in world units whatever the size of the rock.</summary>
    public static StandardMaterial3D Rock(float nodeScale = 1f, float tile = 3f)
    {
        float b = Bucket(nodeScale);
        return Cached($"rock.{b:F3}.{tile}", () =>
            Build(Set("rock", () => Gen.Rock(512, 7707), 4.5f), tile / b, 1.0f, 0f, 1.0f));
    }

    /// <summary>Wreck and debris plating: rust over steel.</summary>
    public static StandardMaterial3D Rust(float nodeScale = 1f)
    {
        float b = Bucket(nodeScale);
        return Cached($"rust.{b:F3}", () =>
            Build(Set("rust", () => Gen.RustedPlating(512, 8808), 3.0f), 3f / b, 0.9f, 0.5f, 1.0f));
    }

    /// <summary>Ore chunks: veined, flecked. <paramref name="rough"/> and <paramref name="metal"/> come from the item's look.</summary>
    public static StandardMaterial3D Ore(float rough, float metal) => Cached($"ore.{rough:F2}.{metal:F2}", () =>
        Build(Set("ore", () => Gen.Ore(512, 9909), 3.5f), 0.8f, Math.Min(1f, rough / 0.8f), Math.Min(1f, metal / 0.7f), 1.0f));

    /// <summary>Ice, optionally see-through (alpha comes from the vertex colour).</summary>
    public static StandardMaterial3D Ice() => Cached("ice", () =>
        Build(Set("ice", () => Gen.Ice(256, 1010), 2.5f), 1.2f, 0.9f, 0f, 0.8f));

    /// <summary>Crystal facets with an emission mask, for quartz, gems and unstable crystals.</summary>
    public static StandardMaterial3D Crystal(float rough, float metal) => Cached($"crystal.{rough:F2}.{metal:F2}", () =>
    {
        var t = Set("crystal", () => Gen.Crystal(256, 1111), 2.0f, true);
        var m = Build(t, 0.7f, Math.Min(1f, rough / 0.5f), metal, 0.8f);
        m.EmissionTexture = t.Emission;
        return m;
    });

    // ---- logistics ----------------------------------------------------------------------------------

    private static StandardMaterial3D? _belt;
    private static bool _beltHooked;

    /// <summary>
    /// The belt running surface: cleats and chevrons along the tile's local +X. The texture creeps along the flow (a light uniform offset
    /// updated once per rendered frame, shared by every belt), so a belt looks like it moves. It does not know whether items are moving.
    /// </summary>
    public static StandardMaterial3D Belt()
    {
        if (_belt is not null) return _belt;
        _belt = Build(Set("belt", () => Gen.Belt(256, 1212), 2.0f), 1f, 0.9f, 0.4f, 0.8f);
        _belt.Uv1Offset = new Vector3(0f, 0f, 0.5f); // the tile is centred on v = 0.5
        if (!_beltHooked)
        {
            _beltHooked = true;
            RenderingServer.Singleton.FramePreDraw += () =>
            {
                double t = Time.GetTicksMsec() / 1000.0;
                _belt!.Uv1Offset = new Vector3(-(float)((t * BeltCreep) % 1.0), 0f, 0.5f);
            };
        }
        return _belt;
    }

    /// <summary>Texture repeats per second of the belt surface (a repeat is one cell).</summary>
    public const double BeltCreep = 0.7;

    /// <summary>A tinted copy of a library material for places that cannot use the instance colour (the gallery's spheres and boxes).</summary>
    public static StandardMaterial3D Tinted(StandardMaterial3D src, Color c)
    {
        var m = (StandardMaterial3D)src.Duplicate();
        m.AlbedoColor = c;
        return m;
    }
}
