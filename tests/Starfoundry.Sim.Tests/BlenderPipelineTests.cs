using System.Numerics;
using System.Text;
using System.Text.Json;
using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>
/// The Blender pilot (docs/prototypes/blender-pipeline.md): <c>tools/blender/shell.json</c> carries the cockpit dimensions to the Blender generator and must
/// match <see cref="CockpitShell"/>; the exported <c>.glb</c> must exist and carry the node names the game maps to its materials.
/// Run with <c>STARFOUNDRY_UPDATE_SHELL=1</c> to rewrite shell.json after changing the numbers.
/// </summary>
public class BlenderPipelineTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starfoundry.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void ShellJsonIsFresh()
    {
        string path = Path.Combine(RepoRoot(), CockpitShell.JsonPath.Replace('/', Path.DirectorySeparatorChar));
        string expected = CockpitShell.ToJson();
        if (Environment.GetEnvironmentVariable("STARFOUNDRY_UPDATE_SHELL") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }
        Assert.True(File.Exists(path), "tools/blender/shell.json is missing: run the tests with STARFOUNDRY_UPDATE_SHELL=1");
        string actual = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.True(actual == expected, "tools/blender/shell.json is stale: set STARFOUNDRY_UPDATE_SHELL=1, run the BlenderPipelineTests and commit the result");
    }

    [Fact]
    public void ShellJsonIsDeterministic() => Assert.Equal(CockpitShell.ToJson(), CockpitShell.ToJson());

    [Fact]
    public void StarterCockpitHasPodInFrontAndGunOnTop()
    {
        int m = CockpitShell.StarterMask();
        Assert.NotEqual(0, m & HullProfile.PodFront);
        Assert.NotEqual(0, m & HullProfile.Up);
        Assert.NotEqual(0, m & HullProfile.Rear);
        Assert.Equal(0, m & (HullProfile.Left | HullProfile.Right | HullProfile.Down));
        Assert.True(CockpitShell.IsStarterShell(m));
        Assert.False(CockpitShell.IsStarterShell(HullProfile.Rear)); // a nose cockpit is a different shell
    }

    [Fact]
    public void ScreensStayInsideTheConsoleFootprint()
    {
        // every screen corner, after the tilt and the squeeze, lies within the solid footprint of the console (the screens sit on the desk the body cannot enter)
        foreach (var s in CockpitShell.Screens)
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var p = CockpitShell.DeckPoint(new Vector3(s.X + sx * s.Width / 2f, CockpitShell.SurfaceHeight, s.Z + sz * s.Depth / 2f));
                Assert.InRange(p.X, HullProfile.ConsoleX0, HullProfile.ConsoleX1);
                Assert.InRange(p.Z, HullProfile.ConsoleZ0, HullProfile.ConsoleZ1);
            }
    }

    [Fact]
    public void SeatedEyeIsUnchanged() => Assert.Equal(new Vector3(4f, 2.7f, 6.6f), SeatLocator.EyeOffset);

    // ---- the exported glTF ---------------------------------------------------------------------------------------

    public const string GlbPath = "game/assets/blender/cockpit_wide.glb";

    /// <summary>The names the game maps to materials and behaviour (see BlenderCockpit.cs): the seat group, the hull and structure parts, the glass and the emissive marks.</summary>
    public static readonly string[] RequiredNodes =
    {
        "Seat", "Seat_Hull", "Seat_Struct", "Console_Hull", "Console_Struct", "Frames_Struct", "Details_Hull", "Details_Struct", "Glass", "Glow_Amber", "Glow_Green", "Glow_Cyan", "Glow_Red",
    };

    private static JsonDocument ReadGlbJson()
    {
        string path = Path.Combine(RepoRoot(), GlbPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{GlbPath} is missing: run tools/blender/build.ps1");
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        Assert.Equal(0x46546C67u, br.ReadUInt32()); // "glTF"
        Assert.Equal(2u, br.ReadUInt32());
        br.ReadUInt32(); // total length
        uint len = br.ReadUInt32();
        Assert.Equal(0x4E4F534Au, br.ReadUInt32()); // "JSON"
        return JsonDocument.Parse(br.ReadBytes((int)len));
    }

    [Fact]
    public void GlbHasTheNamedNodes()
    {
        using var doc = ReadGlbJson();
        var names = doc.RootElement.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()).ToHashSet();
        foreach (string n in RequiredNodes) Assert.Contains(n, names);
    }

    [Fact]
    public void GlbMeshesCarryUvsColoursAndTangents()
    {
        using var doc = ReadGlbJson();
        foreach (var mesh in doc.RootElement.GetProperty("meshes").EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var a = prim.GetProperty("attributes");
                string name = mesh.GetProperty("name").GetString() ?? "?";
                Assert.True(a.TryGetProperty("POSITION", out _) && a.TryGetProperty("NORMAL", out _), name + ": positions and normals");
                if (name.StartsWith("Glow", StringComparison.Ordinal) || name == "Glass") continue;
                Assert.True(a.TryGetProperty("TEXCOORD_0", out _), name + ": UVs");
                Assert.True(a.TryGetProperty("COLOR_0", out _), name + ": vertex colours");
                Assert.True(a.TryGetProperty("TANGENT", out _), name + ": tangents");
            }
    }

    [Fact]
    public void GlbStaysWithinTheModuleBox()
    {
        using var doc = ReadGlbJson();
        var accessors = doc.RootElement.GetProperty("accessors");
        foreach (var mesh in doc.RootElement.GetProperty("meshes").EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var acc = accessors[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                var min = acc.GetProperty("min").EnumerateArray().Select(e => e.GetSingle()).ToArray();
                var max = acc.GetProperty("max").EnumerateArray().Select(e => e.GetSingle()).ToArray();
                // the exporter works in Y up; the module is 8 x 4 x 8 with its minimum corner at the origin (a hair of tolerance for the window frames in the wall)
                Assert.InRange(min[0], -0.05f, 8.05f); Assert.InRange(max[0], -0.05f, 8.05f);
                Assert.InRange(min[1], -0.05f, 4.05f); Assert.InRange(max[1], -0.05f, 4.05f);
                Assert.InRange(min[2], -0.05f, 8.05f); Assert.InRange(max[2], -0.05f, 8.05f);
            }
    }

    [Fact]
    public void GlbTriangleCountIsModest()
    {
        using var doc = ReadGlbJson();
        var accessors = doc.RootElement.GetProperty("accessors");
        long tris = 0;
        foreach (var mesh in doc.RootElement.GetProperty("meshes").EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
                tris += accessors[prim.GetProperty("indices").GetInt32()].GetProperty("count").GetInt64() / 3;
        Assert.InRange(tris, 500, 40000);
    }
}
