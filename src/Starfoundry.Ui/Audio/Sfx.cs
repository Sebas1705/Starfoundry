namespace Starfoundry.Ui;

/// <summary>Every sound of the game. Each one is synthesised from code (see <see cref="SoundBank"/>); there are no audio assets.</summary>
public enum Sfx
{
    // UI
    UiClick, UiConfirm, UiError, ToastPing, ToastThreat, ResearchDone,
    // Ship ambience (loops, except the fuel beep)
    EngineLoop, ReactorLoop, RoomTone, FuelBeep,
    // Machine hum loops (positional)
    MachSmelter, MachAssembler, MachCracker, MachDepot, MachHum,
    // Mining and drones
    MineLoop, MinePop, DroneWhoosh,
    // Building
    PlaceThunk, DemolishCrunch, ModuleSnap,
    // Threat
    WaveAlarm, EnemyShot, TurretShot, ModuleHit, Explosion, HullRumble,
    // Obstacles and boost (ADR 0028)
    RockThud, MineBeep, SalvageChime, BoostWhoosh,
    // Music (loops)
    MusicCalm, MusicTense,
}

/// <summary>The audio buses of the game; every one sends to Master.</summary>
public enum AudioBus { Sfx, Ambience, Ui }

/// <summary>
/// How a sound is mixed: its bus, whether it loops, the least time between two plays (the rate limit), the gain in dB applied by the
/// player (the buffers are normalised to <see cref="Peak"/>, so this is the loudness balance between sounds) and the peak it is rendered at.
/// </summary>
public sealed record SoundSpec(Sfx Id, AudioBus Bus, bool Loop, double MinInterval, float GainDb, float Peak = 0.85f);

/// <summary>The table of <see cref="SoundSpec"/>s, in <see cref="Sfx"/> order.</summary>
public static class SoundCatalog
{
    private static readonly SoundSpec[] Specs =
    {
        new(Sfx.UiClick, AudioBus.Ui, false, 0.04, -10f),
        new(Sfx.UiConfirm, AudioBus.Ui, false, 0.08, -9f),
        new(Sfx.UiError, AudioBus.Ui, false, 0.20, -9f),
        new(Sfx.ToastPing, AudioBus.Ui, false, 0.15, -12f),
        new(Sfx.ToastThreat, AudioBus.Ui, false, 0.50, -8f),
        new(Sfx.ResearchDone, AudioBus.Ui, false, 1.00, -9f),

        new(Sfx.EngineLoop, AudioBus.Ambience, true, 0, -12f),
        new(Sfx.ReactorLoop, AudioBus.Ambience, true, 0, -16f),
        new(Sfx.RoomTone, AudioBus.Ambience, true, 0, -26f),
        new(Sfx.FuelBeep, AudioBus.Ui, false, 3.00, -10f),

        new(Sfx.MachSmelter, AudioBus.Sfx, true, 0, -10f),
        new(Sfx.MachAssembler, AudioBus.Sfx, true, 0, -12f),
        new(Sfx.MachCracker, AudioBus.Sfx, true, 0, -14f),
        new(Sfx.MachDepot, AudioBus.Sfx, true, 0, -14f),
        new(Sfx.MachHum, AudioBus.Sfx, true, 0, -14f),

        new(Sfx.MineLoop, AudioBus.Sfx, true, 0, -16f),
        new(Sfx.MinePop, AudioBus.Sfx, false, 0.10, -12f),
        new(Sfx.DroneWhoosh, AudioBus.Sfx, false, 0.50, -14f),

        new(Sfx.PlaceThunk, AudioBus.Sfx, false, 0.05, -8f),
        new(Sfx.DemolishCrunch, AudioBus.Sfx, false, 0.08, -8f),
        new(Sfx.ModuleSnap, AudioBus.Sfx, false, 0.08, -9f),

        new(Sfx.WaveAlarm, AudioBus.Sfx, false, 4.00, -8f),
        new(Sfx.EnemyShot, AudioBus.Sfx, false, 0.06, -14f),
        new(Sfx.TurretShot, AudioBus.Sfx, false, 0.05, -12f),
        new(Sfx.ModuleHit, AudioBus.Sfx, false, 0.12, -8f),
        new(Sfx.Explosion, AudioBus.Sfx, false, 0.25, -5f),
        new(Sfx.HullRumble, AudioBus.Sfx, false, 0.50, -7f),

        new(Sfx.RockThud, AudioBus.Sfx, false, 0.10, -6f),
        new(Sfx.MineBeep, AudioBus.Ui, false, 0.20, -11f),
        new(Sfx.SalvageChime, AudioBus.Ui, false, 0.50, -10f),
        new(Sfx.BoostWhoosh, AudioBus.Sfx, false, 1.00, -12f),

        new(Sfx.MusicCalm, AudioBus.Ambience, true, 0, -14f, 0.8f),
        new(Sfx.MusicTense, AudioBus.Ambience, true, 0, -14f, 0.8f),
    };

    public static IReadOnlyList<SoundSpec> All => Specs;

    public static SoundSpec Get(Sfx id) => Specs[(int)id];
}
