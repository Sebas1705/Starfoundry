using System;
using System.IO;
using System.Linq;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// Made-up saves for the audits and the headless check: a folder with every kind of row the Save/Load screen can show (a long name, a quick save, checkpoints of
/// different reasons, a victory, a scenario game, a save of other content, one of an unknown scenario, one from a newer format, a damaged file and a cut one),
/// with small generated screenshots. The payloads are not replayable (nothing here is loaded); only what the list reads is real.
/// </summary>
public static class SaveFixtures
{
    public const string GameId = "fixture-game";

    /// <summary>Empties <paramref name="folder"/> (a throwaway one) and fills it. Returns the number of files.</summary>
    public static int Populate(SaveFolder folder, ContentDb content, ScenarioCatalog catalog, string currentGameId = GameId)
    {
        if (Directory.Exists(folder.Root)) foreach (string f in Directory.GetFiles(folder.Root)) File.Delete(f);
        long now = SaveStore.Now;
        ulong hash = content.Hash;
        byte[] payload = new byte[256];
        byte[] thumb = Thumbnail(new Color(0.1f, 0.2f, 0.4f));
        int n = 0;
        void Put(string id, SaveMeta meta, byte[]? t = null) { folder.Write(id, new SaveParts(meta with { LogVersion = CommandCodec.CurrentLogVersion }, t ?? thumb, "", payload)); n++; }
        SaveMeta Base(string name, SaveKind kind, string reason, long ago, string game = GameId) => new()
        {
            Name = name, Kind = kind, Reason = reason, GameId = game, Preset = "normal", Mode = (byte)GameMode.Campaign, Seed = 123456789, Ticks = 72_000 - ago / 8, Wave = 14,
            System = "Vega-3", PlaySeconds = 5400 - ago / 3, SavedAt = now - ago, ContentHash = hash,
        };
        var scenario = catalog.All.FirstOrDefault();
        Put("base-with-a-very-long-name", Base("North station - the long name that has to fit in a row, 48 ch", SaveKind.Manual, SaveReasons.Manual, 600) with { Preset = "hard" });
        Put("quick", Base("", SaveKind.Quick, SaveReasons.QuickSave, 200));
        Put("auto-1", Base("", SaveKind.Auto, SaveReasons.Wave, 900) with { Wave = 13 });
        Put("auto-2", Base("", SaveKind.Auto, SaveReasons.Dock, 1500) with { Wave = 12, System = "Sol" });
        Put("auto-3", Base("", SaveKind.Auto, SaveReasons.Timer, 2100) with { Wave = 11 });
        Put("auto-4", Base("", SaveKind.Auto, SaveReasons.Research, 2700, "other-game") with { Seed = 987654321, Preset = "brutal", Wave = 3 });
        Put("victory-fixture", Base("", SaveKind.Victory, SaveReasons.Victory, 86_400) with { Wave = 20, Preset = "relaxed" });
        if (scenario is not null)
            Put("story-level", Base("Under siege, second try", SaveKind.Manual, SaveReasons.Manual, 4000, "scenario-game") with { ScenarioId = scenario.Id, Preset = "", ContentHash = scenario.HeaderHash(content), Seed = scenario.Seed == 0 ? 42 : scenario.Seed, Wave = 2 });
        Put("other-content", Base("Made with the previous content", SaveKind.Manual, SaveReasons.Manual, 700_000) with { ContentHash = hash ^ 0xFFUL });
        Put("lost-scenario", Base("A scenario this version lacks", SaveKind.Manual, SaveReasons.Manual, 800_000) with { ScenarioId = "99_nope" });
        Put("from-the-future", Base("Saved by a newer game", SaveKind.Manual, SaveReasons.Manual, 900_000) with { }, null);
        // The one above gets a newer log version: rewrite it with the version raised.
        folder.Write("from-the-future", folder.Read("from-the-future") with { Meta = folder.Read("from-the-future").Meta with { LogVersion = CommandCodec.CurrentLogVersion + 1 } });
        byte[] damaged = SaveFile.Pack(new SaveParts(Base("Damaged", SaveKind.Manual, SaveReasons.Manual, 1_000_000), thumb, "", payload));
        damaged[damaged.Length / 2] ^= 0x55;
        File.WriteAllBytes(folder.PathOf("damaged-file"), damaged); n++;
        File.WriteAllBytes(folder.PathOf("cut-short"), SaveFile.Pack(new SaveParts(Base("Cut", SaveKind.Manual, SaveReasons.Manual, 1_100_000), thumb, "", payload))[..40]); n++;
        return n;
    }

    /// <summary>A small JPEG: a gradient with a bright disc, enough to see that the picture is drawn in its frame.</summary>
    public static byte[] Thumbnail(Color tint)
    {
        const int w = 320, h = 180;
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = y / (float)h;
                float d = new Vector2(x - 220, y - 70).Length();
                var c = tint.Lerp(new Color(0.02f, 0.02f, 0.06f), t);
                if (d < 28) c = new Color(1f, 0.9f, 0.6f);
                image.SetPixel(x, y, c);
            }
        return image.SaveJpgToBuffer(0.7f);
    }
}
