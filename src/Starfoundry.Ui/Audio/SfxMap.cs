using System.Text.Json;

namespace Starfoundry.Ui;

/// <summary>How one effect is played from recorded files: the variants (one is picked at random), a gain in dB and a pitch jitter (plus or minus, 0.05 is five percent).</summary>
public sealed record SfxFileEntry(IReadOnlyList<string> Files, float GainDb, float PitchJitter);

/// <summary>
/// The table that maps our effect ids to recorded OGG files (<c>game/assets/audio/sfx_map.json</c>, ADR 0037). An effect with no entry,
/// or whose file is missing, plays the synthesised version. Shape:
/// <c>{ "UiClick": { "files": ["kenney/interface/click_001.ogg"], "gainDb": -4, "pitchJitter": 0.03 } }</c>, file names relative to
/// <c>game/assets/audio</c>.
/// </summary>
public sealed class SfxMap
{
    public static readonly SfxMap Empty = new(new Dictionary<string, SfxFileEntry>());

    public IReadOnlyDictionary<string, SfxFileEntry> Entries { get; }

    private SfxMap(IReadOnlyDictionary<string, SfxFileEntry> entries) => Entries = entries;

    public static SfxMap Parse(string json)
    {
        var dict = new Dictionary<string, SfxFileEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("files", out var fs) || fs.ValueKind != JsonValueKind.Array) continue;
                var files = fs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
                if (files.Count == 0) continue;
                float gain = p.Value.TryGetProperty("gainDb", out var g) && g.TryGetSingle(out float gv) ? Math.Clamp(gv, -40f, 12f) : 0f;
                float jit = p.Value.TryGetProperty("pitchJitter", out var j) && j.TryGetSingle(out float jv) ? Math.Clamp(jv, 0f, 0.5f) : 0.05f;
                dict[p.Name] = new SfxFileEntry(files, gain, jit);
            }
        }
        catch (JsonException) { return Empty; }
        return new SfxMap(dict);
    }

    public bool TryGet(Sfx id, out SfxFileEntry entry) => Entries.TryGetValue(id.ToString(), out entry!);
}
