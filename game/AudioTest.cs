using System;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// <c>--audio-ab</c>: plays every one-shot effect through the real audio device, first the synthesised version and then, where the
/// table (<c>sfx_map.json</c>) has a file, the recorded one, with a line printed before each (<c>AB UiClick synthetic</c>), for listening
/// and comparing; then quits. It needs a window and speakers (not headless).
/// </summary>
public partial class AudioAb : Node
{
    private readonly System.Collections.Generic.List<(Sfx Id, bool Recorded)> _queue = new();
    private AudioManager? _audio;
    private double _wait = 1.5;
    private int _next;

    public override void _Ready()
    {
        _audio = AudioManager.Create(this);
        foreach (var spec in SoundCatalog.All)
        {
            if (spec.Loop) continue;
            _queue.Add((spec.Id, false));
            if (AudioManager.HasRecorded(spec.Id)) _queue.Add((spec.Id, true));
        }
        GD.Print($"AUDIO-AB {_queue.Count} sounds to play (synthesised first, then recorded where a file is mapped). Listen and note what is wrong.");
    }

    public override void _Process(double delta)
    {
        if (_audio is null) return;
        _wait -= delta;
        if (_wait > 0) return;
        if (_next >= _queue.Count) { GD.Print("AUDIO-AB done"); GetTree().Quit(); return; }
        var (id, rec) = _queue[_next++];
        _audio.ForceRecorded = rec;
        GD.Print($"AB {id} {(rec ? "recorded" : "synthetic")}");
        _audio.Play(id);
        _wait = 1.3;
    }
}

/// <summary>
/// <c>--audio-test</c> (optionally <c>--audio-out=dir</c>): renders every sound to a WAV file (default <c>user://audio_test/</c>) and
/// prints its length, peak and RMS in dBFS, and for loops whether the seam is smooth, then quits. The exit code is 1 when a sound
/// clips (peak above 0.95) or a loop has a seam. Nobody can listen in a headless run; this is how the levels are inspected.
/// </summary>
public static class AudioTest
{
    /// <summary>Runs the test when the flag is present and quits the tree; returns true when it did.</summary>
    public static bool Handle(SceneTree tree, string[] args)
    {
        if (args.Contains("--audio-ab"))
        {
            AudioManager.Enabled = true;
            tree.Root.AddChild(new AudioAb { Name = "AudioAb" });
            return true;
        }
        if (!args.Contains("--audio-test")) return false;
        string? outArg = args.FirstOrDefault(a => a.StartsWith("--audio-out="))?["--audio-out=".Length..];
        string dir = outArg ?? ProjectSettings.GlobalizePath("user://audio_test");
        int code = Run(dir);
        tree.Quit(code);
        return true;
    }

    private static string Db(float linear) => linear <= 1e-6f ? "-inf" : (20f * MathF.Log10(linear)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.AppendLine($"AUDIO-TEST output: {dir}");
        sb.AppendLine($"{"sound",-16} {"bus",-8} {"s",6} {"peak",6} {"peak dB",8} {"rms dB",8}  loop");
        int bad = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var spec in SoundCatalog.All)
        {
            var b = SoundBank.Render(spec.Id);
            var st = SoundStats.Measure(b);
            File.WriteAllBytes(Path.Combine(dir, spec.Id + ".wav"), WavFile.Encode(b));
            string loop = !spec.Loop ? "-" : st.Seamless ? $"seamless (seam {st.SeamStep:0.0000} <= step {st.MaxStep:0.0000})" : $"SEAM {st.SeamStep:0.0000} > step {st.MaxStep:0.0000}";
            string flag = "";
            if (st.Peak > 0.95f) { flag = "  CLIPS"; bad++; }
            if (spec.Loop && !st.Seamless) { flag += "  SEAM"; bad++; }
            sb.AppendLine($"{spec.Id,-16} {spec.Bus,-8} {st.Seconds,6:0.00} {st.Peak,6:0.000} {Db(st.Peak),8} {Db(st.Rms),8}  {loop}{flag}");
        }
        sb.AppendLine($"AUDIO-TEST rendered {SoundCatalog.All.Count} sounds in {watch.ElapsedMilliseconds} ms; problems: {bad}");
        GD.Print(sb.ToString());
        return bad == 0 ? 0 : 1;
    }
}
