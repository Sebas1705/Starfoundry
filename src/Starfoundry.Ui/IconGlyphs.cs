namespace Starfoundry.Ui;

/// <summary>
/// The drawings of the icon set: one SVG fragment per glyph on a 24 by 24 grid, stroke only (round caps and joins) with an optional
/// translucent fill (<c>{F}</c>) for the main mass, so every icon shares one weight and reads at 16 to 32 pixels. The fragments are
/// white: the game tints them. Pure data, no engine: <see cref="IconCatalog"/> says which id uses which glyph.
/// </summary>
public static class IconGlyphs
{
    /// <summary>Stroke width of every glyph on the 24 unit grid.</summary>
    public const float Stroke = 1.75f;

    private const string Fill = "fill=\"#fff\" fill-opacity=\".3\"";
    private const string Solid = "fill=\"#fff\" stroke=\"none\"";

    /// <summary>The whole SVG document of a glyph at a pixel size (the viewBox stays 24 so it scales cleanly).</summary>
    public static string Document(string glyph, int pixels)
    {
        string body = Body(glyph);
        return System.FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pixels}\" height=\"{pixels}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"#fff\" stroke-width=\"{Stroke:0.##}\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>");
    }

    public static bool Has(string glyph) => Table.ContainsKey(glyph);

    public static IEnumerable<string> All => Table.Keys;

    public static string Body(string glyph) => (Table.TryGetValue(glyph, out var b) ? b : Table["generic"]).Replace("{F}", Fill).Replace("{S}", Solid);

    private static string Chevrons(int n)
    {
        var sb = new System.Text.StringBuilder("<path d=\"M3 7.5h18M3 16.5h18\"/>");
        float gap = 5.2f, start = 12f - (n - 1) * gap / 2f;
        for (int i = 0; i < n; i++)
        {
            float x = start + i * gap;
            sb.Append(System.FormattableString.Invariant($"<path d=\"M{x - 1.4f:0.#} 10l2.8 2-2.8 2\"/>"));
        }
        return sb.ToString();
    }

    private static readonly Dictionary<string, string> Table = new()
    {
        ["generic"] = "<rect x=\"4\" y=\"4\" width=\"16\" height=\"16\" rx=\"3.5\"/><path d=\"M9.6 9.6a2.5 2.5 0 1 1 3.5 2.3c-.7.4-1.1.9-1.1 1.7\"/><circle cx=\"12\" cy=\"16.6\" r=\".6\" {S}/>",

        // ---- materials (shape says the form, the game tints the material) ----
        ["ore"] = "<path d=\"M4.5 14.5l2.7-6.8 5-2.7 5.6 3.2 1.7 6.3-4 4.2H8.6z\" {F}/><path d=\"M7.2 7.7l4.8 3.6 5.8-.8M12 11.3l-3.2 7.3\"/>",
        ["ingot"] = "<path d=\"M7 8.5h10l3.5 5H3.5z\" {F}/><path d=\"M3.5 13.5h17v4.2h-17zM8.5 11h7\"/>",
        ["plate"] = "<path d=\"M3.5 9.5L12 5.5l8.5 4L12 13.5z\" {F}/><path d=\"M3.5 9.5v3L12 16.5l8.5-4v-3M12 13.5v3\"/>",
        ["plateStack"] = "<path d=\"M3.5 7.5L12 4l8.5 3.5L12 11z\" {F}/><path d=\"M3.5 11.5L12 15l8.5-3.5M3.5 15.5L12 19l8.5-3.5\"/>",
        ["lump"] = "<path d=\"M5 17l-.8-5 3.8-3.4 4.2.8 3.6-2.4 3.4 3.4-.6 5.2-4.4 2.6z\" {F}/><path d=\"M9 12.4l2.2 1.8M14.6 10.2l-1 3.6\"/>",
        ["ammo"] = "<path d=\"M6 20v-7.5c0-2 .9-3.6 2.5-5 1.6 1.4 2.5 3 2.5 5V20zM6 16.8h5\" {F}/><path d=\"M13 20v-7.5c0-2 .9-3.6 2.5-5 1.6 1.4 2.5 3 2.5 5V20zM13 16.8h5\"/>",
        ["gem"] = "<path d=\"M12 3l6.5 5.5L12 21 5.5 8.5z\" {F}/><path d=\"M5.5 8.5h13M12 3l-2.6 5.5L12 21l2.6-12.5z\"/>",
        ["crystal"] = "<path d=\"M8.5 21V9.5L12 4l3.5 5.5V21z\" {F}/><path d=\"M8.5 9.5h7M12 4v5.5M12 9.5V21M5 21v-5.5l2-3M19 21v-5.5l-2-3\"/>",
        ["drop"] = "<path d=\"M12 3.5c3.2 3.7 5.7 6.5 5.7 9.8a5.7 5.7 0 0 1-11.4 0c0-3.3 2.5-6.1 5.7-9.8z\" {F}/><path d=\"M9.6 13.4v3.2M9.6 15h2.8M12.4 13.4v3.2\"/>",
        ["wafer"] = "<path d=\"M8.2 20.3a8.6 8.6 0 1 1 7.6 0z\" {F}/><path d=\"M8.5 6.5v8M12 5v11M15.5 6.5v8M5.8 10.5h12.4\"/>",
        ["chip"] = "<rect x=\"6\" y=\"6\" width=\"12\" height=\"12\" rx=\"2\" {F}/><rect x=\"9.5\" y=\"9.5\" width=\"5\" height=\"5\" rx=\".8\"/><path d=\"M9 3.5v2.5M15 3.5v2.5M9 18v2.5M15 18v2.5M3.5 9H6M3.5 15H6M18 9h2.5M18 15h2.5\"/>",
        ["alloy"] = "<path d=\"M7 8.5h10l3.5 5H3.5z\" {F}/><path d=\"M3.5 13.5h17v4.2h-17z\"/><path d=\"M7.5 15.6h2M11 15.6h2M14.5 15.6h2\"/>",
        ["cell"] = "<rect x=\"7\" y=\"6\" width=\"10\" height=\"14.5\" rx=\"2.2\" {F}/><path d=\"M10 3.5h4M12.6 9.5l-2.7 4.2h4.2l-2.7 4.2\"/>",
        ["gear"] = "<circle cx=\"12\" cy=\"12\" r=\"5.8\" {F}/><circle cx=\"12\" cy=\"12\" r=\"2.3\"/><path d=\"M12 3.2v3M12 17.8v3M3.2 12h3M17.8 12h3M5.8 5.8l2.1 2.1M16.1 16.1l2.1 2.1M18.2 5.8l-2.1 2.1M7.9 16.1l-2.1 2.1\"/>",
        ["leaf"] = "<path d=\"M5 19c0-8 5-13.5 14.5-14.5C19.500 14 14.500 19 6 19z\" {F}/><path d=\"M5 19c3-4.200 6-7.200 10-9.500\"/>",
        ["atom"] = "<ellipse cx=\"12\" cy=\"12\" rx=\"9\" ry=\"3.600\" transform=\"rotate(60 12 12)\"/><ellipse cx=\"12\" cy=\"12\" rx=\"9\" ry=\"3.600\" transform=\"rotate(-60 12 12)\"/><ellipse cx=\"12\" cy=\"12\" rx=\"9\" ry=\"3.600\"/><circle cx=\"12\" cy=\"12\" r=\"1.600\" {S}/>",
        ["sprout"] = "<path d=\"M12 21v-9\"/><path d=\"M12 12c0-4.200-2.600-6.200-7.200-6.200 0 4.200 2.600 6.200 7.200 6.200z\" {F}/><path d=\"M12 14.500c0-3.400 2.200-5.600 7-5.600 0 3.600-2.400 5.600-7 5.600z\" {F}/>",

        // ---- modules ----
        ["core"] = "<path d=\"M12 3l7.800 4.500v9L12 21l-7.800-4.500v-9z\" {F}/><circle cx=\"12\" cy=\"12\" r=\"2.800\"/>",
        ["cockpit"] = "<path d=\"M4 16.500c0-4.800 3.500-8.500 8-8.500s8 3.700 8 8.500z\" {F}/><path d=\"M4 16.500V19h16v-2.500M9 13c.8-1.400 2-2.200 3-2.200s2.200.8 3 2.200\"/>",
        ["engine"] = "<path d=\"M7 3.500h10l-1.600 9h-6.800z\" {F}/><path d=\"M9 15.500c0 2 1 3.500 3 5.500 2-2 3-3.500 3-5.500\"/>",
        ["reactor"] = "<circle cx=\"12\" cy=\"12\" r=\"8.500\"/><circle cx=\"12\" cy=\"12\" r=\"2.400\" {F}/><path d=\"M12 9.600V3.800M14.100 13.200l5 2.900M9.900 13.200l-5 2.900\"/>",
        ["reactor2"] = "<circle cx=\"12\" cy=\"11\" r=\"7.500\"/><circle cx=\"12\" cy=\"11\" r=\"2.200\" {F}/><path d=\"M12 8.800V4.200M13.900 12.100l4 2.300M10.100 12.100l-4 2.300\"/><path d=\"M8.800 21h2.800M12.400 21h2.800\"/>",
        ["cargo"] = "<path d=\"M3.500 7.500L12 3.500l8.500 4v9L12 20.500l-8.500-4z\" {F}/><path d=\"M3.500 7.500L12 11.500l8.500-4M12 11.500v9\"/>",
        ["hull"] = "<rect x=\"3.500\" y=\"5\" width=\"17\" height=\"14\" rx=\"2.200\" {F}/><path d=\"M3.500 12h17M12 5v14\"/><circle cx=\"7.500\" cy=\"8.500\" r=\".5\" {S}/><circle cx=\"16.500\" cy=\"15.500\" r=\".5\" {S}/>",
        ["drone"] = "<circle cx=\"12\" cy=\"12\" r=\"3\" {F}/><path d=\"M9.800 9.800L6.500 6.500M14.200 9.800l3.300-3.300M9.800 14.200l-3.300 3.300M14.200 14.200l3.300 3.300\"/><circle cx=\"5\" cy=\"5\" r=\"2.200\"/><circle cx=\"19\" cy=\"5\" r=\"2.200\"/><circle cx=\"5\" cy=\"19\" r=\"2.200\"/><circle cx=\"19\" cy=\"19\" r=\"2.200\"/>",
        ["shield"] = "<path d=\"M12 3l7.500 3v5.500c0 4.500-3 8-7.500 9.500-4.500-1.500-7.500-5-7.500-9.500V6z\" {F}/>",
        ["shieldCheck"] = "<path d=\"M12 3l7.500 3v5.500c0 4.500-3 8-7.500 9.500-4.500-1.500-7.500-5-7.500-9.500V6z\" {F}/><path d=\"M8.700 12l2.300 2.300 4.300-4.600\"/>",
        ["dish"] = "<path d=\"M5 19a9 9 0 0 1 9-9\" /><path d=\"M8.500 12.500a6 6 0 0 0 7 7\" {F}/><path d=\"M14 5.500a7.500 7.500 0 0 1 4.500 4.500M14.500 2.500A11 11 0 0 1 21.500 9.500\"/><path d=\"M10.500 17.500l-4 3\"/>",
        ["dock"] = "<circle cx=\"12\" cy=\"12\" r=\"8\" {F}/><path d=\"M12 5.500V3M12 21v-2.500M5.500 12H3M21 12h-2.500\"/><path d=\"M8.500 12h6.500M12.500 9.200L15.300 12l-2.800 2.800\"/>",

        // ---- machines ----
        ["drill"] = "<path d=\"M8 3.500h8v4H8z\" {F}/><path d=\"M9 7.500h6L12 21z\" {F}/><path d=\"M10.200 11.500l3.200 1.200M11 14.800l2.200.8\"/>",
        ["furnace"] = "<path d=\"M4.500 20.500V9.500L12 4l7.500 5.500v11z\" {F}/><path d=\"M12 19.500c-2 0-3.200-1.300-3.200-2.900 0-1.900 1.600-2.600 1.900-4.400 1.400 1 3.200 2.400 3.200 4.500 0 1.600-.9 2.800-1.900 2.800z\"/>",
        ["arm"] = "<circle cx=\"6.500\" cy=\"18\" r=\"2.600\" {F}/><path d=\"M7.800 15.800L12 8.500l4.500 4\"/><circle cx=\"12\" cy=\"7.200\" r=\"2\" {F}/><path d=\"M16.500 12.500L20 9.500M16.500 12.500l3.500 3\"/>",
        ["flask"] = "<path d=\"M9.500 3.500h5M10.500 3.500v6L4.800 19a1.600 1.600 0 0 0 1.400 2.300h11.600a1.600 1.600 0 0 0 1.400-2.300L13.500 9.500v-6\"/><path d=\"M7.500 15h9l2.300 3.900a1.500 1.500 0 0 1-1.300 2.200h-11a1.500 1.500 0 0 1-1.300-2.200z\" {F}/>",
        ["depot"] = "<path d=\"M3.500 10L12 4l8.500 6v10.500h-17z\" {F}/><path d=\"M8 20.500v-6.500h8v6.500M8 17.200h8\"/>",
        ["turret"] = "<path d=\"M5 20.500h14M7 20.500v-4h10v4\" {F}/><circle cx=\"12\" cy=\"12.500\" r=\"3.500\" {F}/><path d=\"M12 9V3.500\"/>",
        ["wall"] = "<rect x=\"3.500\" y=\"5\" width=\"17\" height=\"14\" rx=\"1.500\" {F}/><path d=\"M3.500 9.700h17M3.500 14.300h17M9 5v4.700M15 9.700v4.600M9 14.300V19\"/>",
        ["repairBay"] = "<rect x=\"3.500\" y=\"3.500\" width=\"17\" height=\"17\" rx=\"4\" {F}/><path d=\"M12 8v8M8 12h8\"/>",
        ["port"] = "<rect x=\"3.500\" y=\"6\" width=\"10\" height=\"12\" rx=\"2\" {F}/><path d=\"M13.500 12h7M17.500 9l3 3-3 3\"/>",
        ["molecule"] = "<circle cx=\"6.800\" cy=\"12\" r=\"3.500\" {F}/><circle cx=\"17.200\" cy=\"12\" r=\"3.500\" {F}/><path d=\"M10.300 12h3.400M12 3.500v4M12 16.500v4\"/>",
        ["bench"] = "<path d=\"M3.500 13h17M5.500 13v7.500M18.500 13v7.500\"/><circle cx=\"12\" cy=\"8\" r=\"3\" {F}/><path d=\"M12 3v1.500M12 11.500V13M7 8h1.500M15.500 8H17\"/>",
        ["greenhouse"] = "<path d=\"M3.500 20.500a8.500 8.500 0 0 1 17 0z\" {F}/><path d=\"M12 20.500v-9M12 15.500c0-2 1.200-3.200 3.500-3.200 0 2-1.200 3.200-3.500 3.200z\"/>",
        ["station"] = "<path d=\"M12 3.500l6.500 3.800v7.400L12 18.500l-6.500-3.800V7.300z\" {F}/><path d=\"M12 18.500v3M5.500 14.700L3 16.200M18.500 14.700l2.500 1.500\"/>",

        // ---- tools and actions ----
        ["build"] = "<rect x=\"3.500\" y=\"3.500\" width=\"7\" height=\"7\" rx=\"1.500\" {F}/><rect x=\"13.500\" y=\"3.500\" width=\"7\" height=\"7\" rx=\"1.500\"/><rect x=\"3.500\" y=\"13.500\" width=\"7\" height=\"7\" rx=\"1.500\"/><path d=\"M17 13.500v7M13.500 17h7\"/>",
        ["trash"] = "<path d=\"M4.500 7h15M9.500 7V4.500h5V7M6.500 7l1 13h9l1-13\" {F}/><path d=\"M10 11v6M14 11v6\"/>",
        ["laser"] = "<path d=\"M3.500 20.500L12.500 11.500\"/><path d=\"M13.500 5l4.500-1 2.500 3.500-1 4.500-4.500 1-3-3z\" {F}/><path d=\"M4 14.500v2M7 11.500h2\"/>",
        ["crosshair"] = "<circle cx=\"12\" cy=\"12\" r=\"6.500\"/><circle cx=\"12\" cy=\"12\" r=\"1.500\" {S}/><path d=\"M12 2.500v4M12 17.500v4M2.500 12h4M17.500 12h4\"/>",
        ["radar"] = "<circle cx=\"12\" cy=\"12\" r=\"8.500\"/><circle cx=\"12\" cy=\"12\" r=\"4.500\"/><path d=\"M12 12l6-6\"/><circle cx=\"12\" cy=\"12\" r=\"1.200\" {S}/>",
        ["starmap"] = "<circle cx=\"6\" cy=\"17\" r=\"2.300\" {F}/><circle cx=\"12\" cy=\"6.500\" r=\"2.300\" {F}/><circle cx=\"18.500\" cy=\"15\" r=\"2.300\" {F}/><path d=\"M7.200 14.900l3.700-6.300M13.600 8.400l3.900 4.800M8.300 16.700l7.800-1.200\"/>",
        ["undock"] = "<path d=\"M12 20.500V8.500M7 13l5-5 5 5M4 4h16\"/>",
        ["jump"] = "<path d=\"M5 5l7 7-7 7M12 5l7 7-7 7\"/>",
        ["wrench"] = "<path d=\"M14.800 4a4.600 4.600 0 0 0-4.300 6.200L4.200 16.500a2 2 0 0 0 2.800 2.800l6.300-6.300a4.600 4.600 0 0 0 6.200-4.300l-3 2.700-2.700-.5-.6-2.700z\" {F}/>",
        ["seat"] = "<path d=\"M8 3.500v10h9.500\"/><path d=\"M8 13.500l-1.500 7M17.500 13.500l1.500 7M8 9.500H5.500\" {F}/>",
        ["walk"] = "<circle cx=\"12\" cy=\"4.800\" r=\"2.200\" {F}/><path d=\"M12 8.500v6M12 10.500l-4 3M12 10.500l4 3M12 14.500l-3 6M12 14.500l3 6\"/>",
        ["eye"] = "<path d=\"M2.500 12S6 5.500 12 5.500 21.500 12 21.500 12 18 18.500 12 18.500 2.500 12 2.500 12z\"/><circle cx=\"12\" cy=\"12\" r=\"3\" {F}/>",
        ["help"] = "<circle cx=\"12\" cy=\"12\" r=\"8.500\"/><path d=\"M9.600 9.600a2.500 2.500 0 1 1 3.500 2.300c-.7.400-1.100.900-1.100 1.700\"/><circle cx=\"12\" cy=\"16.800\" r=\".6\" {S}/>",
        ["info"] = "<circle cx=\"12\" cy=\"12\" r=\"8.500\"/><path d=\"M12 11v5.500\"/><circle cx=\"12\" cy=\"7.800\" r=\".7\" {S}/>",
        ["globe"] = "<circle cx=\"12\" cy=\"12\" r=\"8.500\"/><ellipse cx=\"12\" cy=\"12\" rx=\"3.800\" ry=\"8.500\"/><path d=\"M3.500 12h17\"/>",
        ["bolt"] = "<path d=\"M13.500 2.500L5 13.500h6l-1 8 8.500-11h-6z\" {F}/>",
        ["pause"] = "<path d=\"M8.500 5v14M15.500 5v14\"/>",
        ["play"] = "<path d=\"M7 4.500l12 7.500-12 7.500z\" {F}/>",
        ["sliders"] = "<path d=\"M4 7h9M17 7h3M4 17h3M11 17h9M4 12h5M13 12h7\"/><circle cx=\"15\" cy=\"7\" r=\"2\" {F}/><circle cx=\"9\" cy=\"17\" r=\"2\" {F}/><circle cx=\"11\" cy=\"12\" r=\"2\" {F}/>",
        ["save"] = "<path d=\"M5 4.500h11l3 3v12H5z\" {F}/><path d=\"M8 4.500v5h7v-5M8 19.500v-5h8v5\"/>",
        ["load"] = "<path d=\"M3.500 7.500v11h17v-9h-8l-2-2.500h-7z\" {F}/><path d=\"M12 17v-5M9.800 14l2.200-2.200 2.200 2.200\"/>",
        ["rotate"] = "<path d=\"M20 12a8 8 0 1 1-2.500-5.800M20 4v4.500h-4.500\"/>",
        ["layers"] = "<path d=\"M12 3.500l8.500 4.500-8.500 4.500L3.500 8z\" {F}/><path d=\"M3.500 12l8.500 4.500 8.500-4.500M3.500 16l8.500 4.500 8.500-4.500\"/>",
        ["unload"] = "<path d=\"M4 15v5h16v-5M12 4v11M8 11l4 4 4-4\"/>",
        ["upload"] = "<path d=\"M4 15v5h16v-5M12 15V4M8 8l4-4 4 4\"/>",
        ["cross"] = "<path d=\"M6 6l12 12M18 6L6 18\"/>",
        ["check"] = "<path d=\"M5 12.500l4.500 4.500L19 7\"/>",
        ["lock"] = "<rect x=\"5.500\" y=\"10.500\" width=\"13\" height=\"10\" rx=\"2.200\" {F}/><path d=\"M8.500 10.500V8a3.500 3.500 0 0 1 7 0v2.500\"/><circle cx=\"12\" cy=\"15.300\" r=\".8\" {S}/>",
        ["cursor"] = "<path d=\"M6 3.500l12 8.200-5.200 1.300-2.700 5.500z\" {F}/>",
        ["magnifier"] = "<circle cx=\"10.500\" cy=\"10.500\" r=\"6\" {F}/><path d=\"M15 15l5.500 5.500\"/>",
        ["cycle"] = "<path d=\"M4.500 10a8 8 0 0 1 14-3l1.500 1.500M19.500 14a8 8 0 0 1-14 3L4 15.500M20 4v4.500h-4.500M4 20v-4.500h4.500\"/>",
        ["tag"] = "<path d=\"M3.500 12.500V4.500h8l9 9-8 8z\" {F}/><circle cx=\"8\" cy=\"9\" r=\"1.200\" {S}/>",
        ["tabs"] = "<rect x=\"3.500\" y=\"7.500\" width=\"17\" height=\"12\" rx=\"2\"/><path d=\"M3.500 11.500h17M3.500 7.500v-2h6v2\" {F}/>",
        ["flag"] = "<path d=\"M6 21V4M6 5h11l-2.500 3.500L17 12H6\" {F}/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"8.500\"/><path d=\"M12 7v5l3.500 2\"/>",
        ["star"] = "<path d=\"M12 3l2.600 5.700 6.200.700-4.600 4.200 1.300 6.100L12 16.600 6.500 19.700l1.300-6.100L3.200 9.400l6.200-.7z\" {F}/>",
        ["sun"] = "<circle cx=\"12\" cy=\"12\" r=\"4.200\" {F}/><path d=\"M12 2.500v3M12 18.500v3M2.500 12h3M18.500 12h3M5.300 5.300l2.100 2.100M16.600 16.600l2.100 2.100M18.700 5.300l-2.100 2.100M7.400 16.600l-2.100 2.100\"/>",
        ["pulsar"] = "<circle cx=\"12\" cy=\"12\" r=\"2.600\" {F}/><path d=\"M12 2.500v6M12 15.500v6M5 5l4.200 4.200M14.800 14.800L19 19\"/>",
        ["binary"] = "<circle cx=\"8\" cy=\"12\" r=\"4\" {F}/><circle cx=\"17\" cy=\"12\" r=\"2.600\" {F}/>",

        // ---- status ----
        ["ship"] = "<path d=\"M12 3l5 9.500V19l-2.500-1.800h-5L7 19v-6.500z\" {F}/><path d=\"M12 8.500v4.500\"/>",
        ["gauge"] = "<path d=\"M4 17.500a8.500 8.500 0 1 1 16 0\"/><path d=\"M12 17.500l4.200-5.500\"/><circle cx=\"12\" cy=\"17.500\" r=\"1.300\" {S}/>",
        ["warning"] = "<path d=\"M12 4l9 16H3z\" {F}/><path d=\"M12 10v4.500\"/><circle cx=\"12\" cy=\"17.300\" r=\".7\" {S}/>",
        ["alarm"] = "<path d=\"M6 19v-6a6 6 0 0 1 12 0v6M4 19h16\" {F}/><path d=\"M12 3v2M4.500 6l1.500 1.300M19.500 6L18 7.300\"/>",
        ["hostile"] = "<path d=\"M12 20.500L4 5l8 4 8-4z\" {F}/><path d=\"M12 9v5\"/>",
        ["gate"] = "<path d=\"M5 21V10a7 7 0 0 1 14 0v11\" {F}/><path d=\"M9 21v-9.500a3 3 0 0 1 6 0V21\"/>",
        ["waves"] = "<path d=\"M3 8c3-3 6 3 9 0s6 3 9 0M3 14c3-3 6 3 9 0s6 3 9 0M3 20c3-3 6 3 9 0s6 3 9 0\"/>",
        ["heart"] = "<path d=\"M12 20.500S4 15.500 4 9.800A4.300 4.300 0 0 1 12 7.500a4.300 4.300 0 0 1 8 2.300c0 5.700-8 10.700-8 10.700z\" {F}/>",
        ["plus"] = "<path d=\"M12 5v14M5 12h14\"/>",

        // ---- enemies and obstacles ----
        ["scout"] = "<path d=\"M12 4l5 14-5-3-5 3z\" {F}/>",
        ["striker"] = "<path d=\"M12 3l4 8 5 7-9-3-9 3 5-7z\" {F}/>",
        ["brute"] = "<path d=\"M12 3.500l7 3.500v7l-7 6.500-7-6.500V7z\" {F}/><path d=\"M8.500 9h7M9 13h6\"/>",
        ["bulwark"] = "<path d=\"M12 3l8 3.500v6c0 4-3.500 7-8 8.500-4.500-1.500-8-4.500-8-8.500v-6z\" {F}/><path d=\"M12 8v8M8 12h8\"/>",
        ["asteroid"] = "<path d=\"M5 10l3-5.500 6 .5 4.500 4L19 16l-5 4.500-6-1L4.500 15z\" {F}/><circle cx=\"10\" cy=\"10.500\" r=\"1.200\"/><circle cx=\"14.500\" cy=\"15\" r=\".9\"/>",
        ["mine"] = "<circle cx=\"12\" cy=\"12\" r=\"4.800\" {F}/><path d=\"M12 2.500v3.200M12 18.300v3.200M2.500 12h3.200M18.300 12h3.200M5.300 5.300l2.300 2.300M16.400 16.400l2.300 2.300M18.700 5.300l-2.300 2.300M7.600 16.400l-2.300 2.300\"/>",
        ["cloud"] = "<path d=\"M7 18.500a4.500 4.500 0 0 1-.5-8.900A5.500 5.500 0 0 1 17 8.500a5 5 0 0 1 .5 10z\" {F}/>",
        ["wreck"] = "<path d=\"M3.500 14l5-1.500 2-4.500 3 3.500 6-1-3.500 5 .5 4.500-5-2.500-3 3z\" {F}/>",
        ["debris"] = "<path d=\"M4 8l4-3 2.500 3-3 3zM13 5.500l5.500 1-1.500 5zM9 15l5-1 1.500 4.500-6 1.500z\" {F}/>",

        // ---- belts (the number of chevrons says the tier) ----
        ["belt1"] = Chevrons(1),
        ["belt2"] = Chevrons(2),
        ["belt3"] = Chevrons(3),
        ["belt"] = "<path d=\"M3 7.500h18M3 16.500h18M9 9.500l3 2.500-3 2.500M14.500 9.500l3 2.500-3 2.500\"/>",
    };
}
