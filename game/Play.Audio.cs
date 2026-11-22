using System;
using Godot;
using Starfoundry.Sim;
using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The game's side of the audio: it reads the state of the simulation and tells the <see cref="AudioManager"/> what to play. Nothing here
/// changes the game. Everything is a no-op when the manager is missing (scripted runs). Per frame it drives the ship loops (engine,
/// reactor, room tone), hand mining and the music; four times a second it picks the nearest working machines for the positional hums and
/// checks the fuel; per simulation tick it turns shots and module hits into sounds.
/// </summary>
public partial class Play
{
    private const float HumRangeCells = 40f;
    private const double SlowAudioSeconds = 0.25;

    private AudioManager? _audio;
    private double _slowAudio;
    private float _engineLevel, _tension;
    private bool _audioReactor;
    private int _prevHold = -1, _prevResearched = -1;
    private long _audioLost;
    private readonly NearestSet _humNear = new(AudioManager.HumSlots);
    private readonly int[] _humOwner = CreateOwners();
    private readonly Node3D?[] _humNode = new Node3D?[AudioManager.HumSlots];

    private static int[] CreateOwners()
    {
        var a = new int[AudioManager.HumSlots];
        Array.Fill(a, -1);
        return a;
    }

    private void InitAudio(string[] args)
    {
        AudioManager.Enabled = !AudioFlags.IsSilent(args);
        _audio = AudioManager.Create(this);
    }

    private void AudioToast(ToastCategory category)
    {
        if (category == ToastCategory.Threat) _audio?.Play(Sfx.ToastThreat);
        else if (category != ToastCategory.Logistics) _audio?.Play(Sfx.ToastPing);
    }

    /// <summary>The result of a command the player sent: building sounds, the error sound for a refusal.</summary>
    private void AudioResult(CommandResult r)
    {
        if (_audio is null) return;
        if (!r.Ok)
        {
            if (r.Command is PlaceSegment or PlaceMachine or PlaceModule or RemoveModule or RemoveSegment or RemoveMachine or StartMining or SetResearch or SetShipRecipe
                or PlaceSegmentIn or PlaceMachineIn or PlaceModuleIn or RemoveModuleIn or FoundStation or Scan or Jump or AssignDrones)
                _audio.Play(Sfx.UiError);
            return;
        }
        switch (r.Command)
        {
            case PlaceMachine or PlaceMachineIn or PlaceSegment or PlaceSegmentIn: _audio.Play(Sfx.PlaceThunk); break;
            case PlaceModule or PlaceModuleIn or FoundStation: _audio.Play(Sfx.ModuleSnap); break;
            case RemoveMachine or RemoveSegment or RemoveModule or RemoveModuleIn: _audio.Play(Sfx.DemolishCrunch); break;
            case AssignDrones ad when ad.Deposit >= 0: _audio.Play(Sfx.DroneWhoosh); break;
            case Scan or Jump or SetResearch: _audio.Play(Sfx.UiConfirm); break;
        }
    }

    /// <summary>Called after every simulation tick: the shots and module hits of that tick are gone after the next one.</summary>
    private void AudioTick()
    {
        if (_audio is null) return;
        foreach (var s in Net.Shots)
        {
            var from = new Vector3(s.FromX, s.FromY, s.FromZ) / CellUnits;
            _audio.Play(s.Kind == ShotKind.Enemy ? Sfx.EnemyShot : Sfx.TurretShot, from);
        }
        int myShip = _structuresOn ? Net.ShipStructure(Player) : -1;
        foreach (var e in Net.ModuleHits)
        {
            var at = CentreOfLost(e);
            _audio.Play(e.Destroyed ? Sfx.Explosion : Sfx.ModuleHit, at);
            if (e.Structure == myShip) _audio.Play(Sfx.HullRumble);
        }
        if (Net.MachinesLost > _audioLost)
        {
            if (!_structuresOn) _audio.Play(Sfx.Explosion);
            _audioLost = Net.MachinesLost;
        }
    }

    /// <summary>Called every frame: the loops that follow the ship, hand mining, music and the machine hums.</summary>
    private void UpdateAudio(float dt)
    {
        if (_audio is null) return;
        _audio.SetLoopsPaused(_paused);
        if (!Net.HasPlayer(Player)) return;
        double now = Time.GetTicksMsec() / 1000.0;
        var pi = Net.GetPlayerInfo(Player);

        // Engine: thrust and speed set the volume and the pitch.
        bool thrusting = Net.CanFly(Player) && (pi.MoveX | pi.MoveY | pi.MoveZ | pi.ThrustX | pi.ThrustY | pi.ThrustZ) != 0;
        _engineLevel = AudioMix.Approach(_engineLevel, thrusting ? 1f : 0f, thrusting ? 1.5f : 0.9f, dt);
        float eng = ShipStruct >= 0 ? Net.ShipSpeedUnitsPerTick(Player) / (float)PlayerRules.MoveUnitsPerTick : 1f;
        float speed01 = _engineLevel * Math.Clamp(eng / 2f, 0f, 1f);
        _audio.SetLoop(Sfx.EngineLoop, AudioMix.EngineDb(_engineLevel, speed01), AudioMix.EnginePitch(speed01));
        _audio.SetLoop(Sfx.RoomTone, 0f);

        // Reactor: follows the power satisfaction and stutters when it is low.
        if (_audioReactor && ShipStruct >= 0)
        {
            var st = Net.PowerStatus(ShipStruct);
            float sat = st.SatisfactionPermille / (float)PowerRules.FullSatisfaction;
            _audio.SetLoop(Sfx.ReactorLoop, AudioMix.ReactorDb(sat, st.Burning, now) + 14f, AudioMix.ReactorPitch(sat));
        }
        else _audio.SetLoop(Sfx.ReactorLoop, AudioMix.Silent);

        // Hand mining: a buzz while it works, a pop for each item that enters the hold.
        int hold = Net.HoldTotal(Player);
        bool mining = Net.LaserFiring(Player) && hold < Net.HoldCapacity(Player);
        _audio.SetLoop(Sfx.MineLoop, mining ? 0f : AudioMix.Silent);
        if (_prevHold >= 0 && hold > _prevHold && Net.LaserFiring(Player)) _audio.Play(Sfx.MinePop);
        _prevHold = hold;

        // Research finished.
        int researched = Net.ResearchedCount;
        if (_prevResearched >= 0 && researched > _prevResearched) _audio.Play(Sfx.ResearchDone);
        _prevResearched = researched;

        // Music: calm, tense while a wave is warned or alive.
        bool threat = Net.ThreatActive && (Net.EnemyCount > 0 || Net.GetSystemThreat(0).WarningRemaining >= 0);
        _tension = AudioMix.Approach(_tension, threat ? 1f : 0f, 0.25f, dt);
        _audio.SetMusic(_tension);
        if (threat && Net.GetSystemThreat(0).WarningRemaining >= 0) _audio.Play(Sfx.WaveAlarm); // rate limited: once every few seconds
        else if (_audioWaves >= 0 && Net.WavesSpawned > _audioWaves) _audio.Play(Sfx.WaveAlarm);
        _audioWaves = Net.WavesSpawned;

        // Hum positions follow their machines every frame (a moving ship carries them); the choice of machines is made four times a second.
        for (int i = 0; i < AudioManager.HumSlots; i++)
            if (_humOwner[i] >= 0 && _humNode[i] is { } node && IsInstanceValid(node)) _audio.MoveHum(i, node.GlobalPosition);

        _slowAudio -= dt;
        if (_slowAudio > 0) return;
        _slowAudio = SlowAudioSeconds;
        SlowAudio();
    }

    private long _audioWaves = -1;
    private readonly Sfx[] _humSoundOf = new Sfx[AudioManager.HumSlots];

    /// <summary>Four times a second: the fuel warning and the choice of the eight nearest working machines for the hums.</summary>
    private void SlowAudio()
    {
        if (_audio is null) return;
        int ship = ShipStruct;
        _audioReactor = ship >= 0 && HasReactor(ship);
        if (_audioReactor)
        {
            var st = Net.PowerStatus(ship);
            if (PowerFormat.TimeLeftTicks(st, BurnInterval) < (long)PowerFormat.LowFuelSeconds * Units.TicksPerSecond) _audio.Play(Sfx.FuelBeep);
        }

        _humNear.Clear();
        var listener = _camera.GlobalPosition;
        float max2 = HumRangeCells * HumRangeCells;
        foreach (int id in _machineIds)
        {
            if (!_machineModels.TryGetValue(id, out var model) || !Net.IsMachineAlive(id)) continue;
            var m = Net.GetMachineInfo(id);
            var def = _content.Machines[m.Type];
            if (HumFor(def, m) is null) continue;
            if (NeedsPower(m.Type) && Net.PowerStatus(m.Structure).SatisfactionPermille < PowerRules.TurretMinSatisfaction) continue; // unpowered machines are silent
            float d2 = (model.GlobalPosition - listener).LengthSquared();
            if (d2 <= max2) _humNear.Offer(id, d2);
        }

        // Keep the slots of machines that are still among the nearest, free the others, then fill the free slots.
        for (int s = 0; s < AudioManager.HumSlots; s++)
            if (_humOwner[s] >= 0 && !_humNear.Contains(_humOwner[s]))
            {
                _humOwner[s] = -1;
                _humNode[s] = null;
                _audio.ClearHum(s);
            }
        for (int i = 0; i < _humNear.Count; i++)
        {
            int id = _humNear.Id(i);
            bool owned = false;
            int free = -1;
            for (int s = 0; s < AudioManager.HumSlots; s++)
            {
                if (_humOwner[s] == id) owned = true;
                else if (_humOwner[s] < 0 && free < 0) free = s;
            }
            if (owned || free < 0 || !_machineModels.TryGetValue(id, out var node)) continue;
            var m = Net.GetMachineInfo(id);
            if (HumFor(_content.Machines[m.Type], m) is not { } sound) continue;
            _humOwner[free] = id;
            _humNode[free] = node;
            _humSoundOf[free] = sound;
            _audio.SetHum(free, sound, node.GlobalPosition);
        }
    }

    /// <summary>The hum a machine makes while it works, or null when it is idle or makes none (walls, turrets, repairers).</summary>
    private Sfx? HumFor(MachineDef def, MachineInfo m)
    {
        switch (def.Role)
        {
            case MachineRole.Depot: return Sfx.MachDepot;
            case MachineRole.Unloader: return m.Crafting ? Sfx.MachDepot : null;
            case MachineRole.Lab: return Net.CurrentResearch >= 0 ? Sfx.MachHum : null;
            case MachineRole.Drill: return m.Deposit >= 0 ? Sfx.MachHum : null;
            case MachineRole.Crafter:
                if (!m.Crafting) return null;
                if (def.Id.Contains("smelter", StringComparison.Ordinal)) return Sfx.MachSmelter;
                if (def.Id.Contains("assembler", StringComparison.Ordinal)) return Sfx.MachAssembler;
                if (def.Id.Contains("cracker", StringComparison.Ordinal)) return Sfx.MachCracker;
                return Sfx.MachHum;
            default: return null;
        }
    }
}
