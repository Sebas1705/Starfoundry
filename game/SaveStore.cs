using System;
using System.IO;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The player's saves folder for the running game (ADR 0047): <c>user://saves</c> opened as a <see cref="SaveFolder"/> (all the logic is engine-free), prepared
/// once per run (leftover temporary files removed, the single-file saves of older versions wrapped into slots), plus the few things that need the engine: the
/// screenshot of a save, the settings entry that remembers the slot used last, and the check of a save against the loaded content.
/// Scripted runs (shots, audits, benchmarks) write to <c>user://saves-scripted</c> instead, so they never touch the player's saves.
/// </summary>
public static class SaveStore
{
    public const string DefaultPath = "user://saves";
    public const string ScriptedPath = "user://saves-scripted";
    private const int ThumbnailWidth = 320;

    private static SaveFolder? _folder;
    private static bool _prepared, _scripted;

    /// <summary>What the first start with this folder did to old saves (for a one-time message), or null.</summary>
    public static MigrationReport? Migrated { get; private set; }

    public static SaveFolder Folder
    {
        get
        {
            if (_folder is null) UseFolder(DefaultPath);
            return _folder!;
        }
    }

    /// <summary>Points the store at another folder (a <c>user://</c> path or an absolute one) and prepares it.</summary>
    public static void UseFolder(string path)
    {
        string root = path.StartsWith("user://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
        _folder = new SaveFolder(root);
        _prepared = false;
        _scripted = path != DefaultPath; // a throwaway folder (a scripted run) never changes the player settings
        Prepare();
    }

    /// <summary>Removes leftover temporary files and wraps older saves; runs once per folder.</summary>
    public static void Prepare()
    {
        if (_prepared) return;
        _prepared = true;
        try
        {
            Folder.RemoveStaleTemp();
            var report = Folder.MigrateLegacy(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (report.Wrapped > 0 || report.Unreadable > 0)
            {
                Migrated = report;
                GD.Print($"SAVES wrapped {report.Wrapped} old save(s) into slots, {report.Unreadable} unreadable; the originals are in the legacy folder");
            }
        }
        catch (Exception e) { GD.PrintErr("SAVES could not prepare the saves folder: " + e.Message); }
    }

    /// <summary>Seconds added to the clock (the headless check moves time without waiting).</summary>
    public static long Skew;

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Skew;

    /// <summary>The slot used last (from <c>settings.cfg</c>), or null.</summary>
    public static string? LastUsed => SettingsStore.Current.LastSave is { Length: > 0 } id ? id : null;

    /// <summary>Remembers the slot just saved or loaded; the next Continue goes back to it.</summary>
    public static void Remember(string id)
    {
        if (_scripted || SettingsStore.Current.LastSave == id) return;
        SettingsStore.SetLastSave(id);
    }

    /// <summary>The content hash a replay of a game of this scenario needs, or null when the scenario is not in this game.</summary>
    public static Func<string?, ulong?> ExpectedHash(ContentDb content, ScenarioCatalog catalog) => scenarioId =>
        scenarioId is null ? content.Hash : catalog.Find(scenarioId) is { } s ? s.HeaderHash(content) : null;

    public static SaveCompat Check(SaveMeta meta, ContentDb content, ScenarioCatalog catalog) =>
        SaveCompatibility.Check(meta, ExpectedHash(content, catalog), CommandCodec.CurrentLogVersion);

    /// <summary>What a Continue should load: the slot used last, else the newest that this content can replay. Null when there is none.</summary>
    public static SaveEntry? ContinueTarget(ContentDb content, ScenarioCatalog catalog) =>
        SaveList.ContinueTarget(Folder.List(), LastUsed, m => Check(m, content, catalog) == SaveCompat.Ok);

    /// <summary>The header of a slot's save (its galaxy, scenario and rules) without replaying it, or null when it cannot be read.</summary>
    public static SaveInfo? PeekInfo(string id)
    {
        try { return SaveGame.Peek(new MemoryStream(Folder.Read(id).Payload)); }
        catch (Exception e) { GD.PrintErr($"SAVES could not read the header of {id}: {e.Message}"); return null; }
    }

    /// <summary>A <c>--continue=</c> argument: a slot id, or one of the names the older versions used (<c>quicksave</c>).</summary>
    public static string ResolveContinue(string arg) => Folder.Exists(arg) ? arg : SaveNaming.LegacyId(arg);

    /// <summary>A short screenshot of what the viewport shows (JPEG), or an empty array when the renderer has nothing to give (headless runs).</summary>
    public static byte[] CaptureThumbnail(Viewport viewport)
    {
        try
        {
            if (DisplayServer.GetName() == "headless") return Array.Empty<byte>(); // nothing is rendered: the texture of the viewport is a placeholder
            var image = viewport.GetTexture()?.GetImage();
            if (image is null || image.IsEmpty() || image.GetWidth() < 16) return Array.Empty<byte>();
            if (image.IsCompressed()) return Array.Empty<byte>();
            int h = Math.Max(1, image.GetHeight() * ThumbnailWidth / image.GetWidth());
            image.Convert(Image.Format.Rgb8);
            image.Resize(ThumbnailWidth, h, Image.Interpolation.Bilinear);
            return image.SaveJpgToBuffer(0.78f);
        }
        catch (Exception e)
        {
            GD.PrintErr("SAVES could not capture a screenshot: " + e.Message);
            return Array.Empty<byte>();
        }
    }

    /// <summary>The picture of a save as a texture, or null.</summary>
    public static Texture2D? ThumbnailTexture(byte[] jpeg)
    {
        if (jpeg.Length == 0) return null;
        var image = new Image();
        return image.LoadJpgFromBuffer(jpeg) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }
}
