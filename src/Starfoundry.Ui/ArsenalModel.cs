namespace Starfoundry.Ui;

/// <summary>The two kinds of weapon a ship carries: the mining laser and the gun.</summary>
public enum ArsenalKind { Laser, Gun }

/// <summary>What a slot of the arsenal is doing (the first one that applies wins, in the order of <see cref="ArsenalModel.StateOf"/>).</summary>
public enum SlotState { Ready, Firing, Cooling, Blocked, LowPower, NoPower, Dry }

/// <summary>One kind of weapon module the ship has fitted: <paramref name="Mark"/> is 1 or 2 (3 for the heavy cannon), <paramref name="Count"/> how many of them.</summary>
public readonly record struct FittedWeapon(ArsenalKind Kind, int Mark, int Count);

/// <summary>One slot of the arsenal: a weapon kind and mark, with the number of modules that share it.</summary>
public readonly record struct ArsenalSlot(ArsenalKind Kind, int Mark, int Count)
{
    /// <summary>The weapon group the slot belongs to: every laser acts together and every gun acts together (ADR 0036), whatever the mark.</summary>
    public WeaponGroup Group => Kind == ArsenalKind.Laser ? WeaponGroup.Laser : WeaponGroup.Guns;

    /// <summary>The icon id of <see cref="IconCatalog"/>.</summary>
    public string Icon => Kind == ArsenalKind.Laser ? (Mark >= 2 ? "module:mining_laser_mk2" : "module:mining_laser") : (Mark >= 3 ? "module:gun_heavy" : Mark >= 2 ? "module:gun_mk2" : "module:gun");

    /// <summary>The Loc key of the name ("Mining laser Mk2").</summary>
    public string NameKey => "arsenal.name." + (Kind == ArsenalKind.Laser ? "laser" : "gun") + (Mark >= 3 && Kind == ArsenalKind.Gun ? "3" : Mark >= 2 ? "2" : "1");
}

/// <summary>What the client knows about a weapon kind right now. The simulation does not expose a gun cooldown, so <see cref="Cooldown"/> stays 0 unless a caller has one.</summary>
/// <param name="Firing">The trigger is held and the weapon acts (the beam is on, the guns are armed).</param>
/// <param name="Dry">A gun without the ammo the content asks for.</param>
/// <param name="PowerLostPermille">Share of the power the ship lacks, in thousandths (0 = full power, 1000 = none); a lack, so that the default value is the healthy one.</param>
/// <param name="Blocked">The weapon cannot act at the aim: a laser with nothing in reach.</param>
/// <param name="Cooldown">Share of the cooldown left, 0 to 1.</param>
/// <param name="UsesAmmo">The content asks for ammo; <paramref name="Ammo"/> is the rounds in the hold.</param>
/// <param name="HasDeposit">Mining laser: a deposit is aimed at or being mined.</param>
/// <param name="MineTenths">Mining laser: items per second in tenths at the deposit aimed at (0 when none).</param>
/// <param name="DepositLeft">Mining laser: how much is left in that deposit.</param>
/// <param name="Slowed">Mining laser: slowed by a full hold.</param>
public readonly record struct ArsenalLive(bool Firing = false, bool Dry = false, int PowerLostPermille = 0, bool Blocked = false, float Cooldown = 0f, bool UsesAmmo = false, int Ammo = 0, bool HasDeposit = false, int MineTenths = 0, int DepositLeft = 0, bool Slowed = false);

/// <summary>
/// The arsenal of the ship (docs/prototypes/arsenal.md): one slot per kind and mark of weapon module fitted, the slot in hand, its state and the animation of the
/// selection. Engine-free: the HUD bar and the cockpit weapons screen both draw from it. Selecting a slot takes its weapon group in hand; two slots of the same group
/// (a Mk1 and a Mk2 laser) act together, so the one not in hand is only shown as "also acts".
/// </summary>
public sealed class ArsenalModel
{
    public const int MaxSlots = 9;

    private readonly List<ArsenalSlot> _slots = new();
    private ArsenalLive _laser, _gun;

    /// <summary>Where the selection marker is drawn, in slots (it eases toward <see cref="Selected"/>).</summary>
    public float Cursor { get; private set; }

    public IReadOnlyList<ArsenalSlot> Slots => _slots;

    /// <summary>The slot in hand, or -1 when nothing is fitted.</summary>
    public int Selected { get; private set; } = -1;

    public bool IsEmpty => _slots.Count == 0;

    /// <summary>The group of the slot in hand, or null for an empty arsenal.</summary>
    public WeaponGroup? Group => Selected >= 0 ? _slots[Selected].Group : null;

    /// <summary>Replaces the fitted weapons (any order, duplicates are merged): lasers first, then guns, Mk1 before Mk2. The slot in hand stays when its kind is still fitted.</summary>
    public void Fit(IEnumerable<FittedWeapon> weapons)
    {
        var previous = Selected >= 0 ? _slots[Selected] : (ArsenalSlot?)null;
        var merged = new Dictionary<(ArsenalKind, int), int>();
        foreach (var w in weapons)
        {
            if (w.Count <= 0) continue;
            var key = (w.Kind, w.Kind == ArsenalKind.Gun && w.Mark >= 3 ? 3 : w.Mark >= 2 ? 2 : 1);
            merged[key] = merged.GetValueOrDefault(key) + w.Count;
        }
        var next = merged.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2).Take(MaxSlots).Select(p => new ArsenalSlot(p.Key.Item1, p.Key.Item2, p.Value)).ToList();
        if (next.SequenceEqual(_slots)) return;
        _slots.Clear();
        _slots.AddRange(next);
        if (_slots.Count == 0) { Selected = -1; Cursor = 0f; return; }
        int keep = -1;
        if (previous is { } p)
        {
            keep = _slots.FindIndex(s => s.Kind == p.Kind && s.Mark == p.Mark);
            if (keep < 0) keep = _slots.FindIndex(s => s.Kind == p.Kind);
        }
        Selected = keep >= 0 ? keep : 0;
        Cursor = Math.Clamp(Cursor, 0f, _slots.Count - 1);
    }

    /// <summary>Takes a slot in hand. False when there is no such slot or it already is in hand.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _slots.Count || index == Selected) return false;
        Selected = index;
        return true;
    }

    /// <summary>Takes the next (+1) or previous (-1) slot in hand, wrapping around. False when there is nothing to switch to.</summary>
    public bool Step(int direction)
    {
        if (_slots.Count < 2) return false;
        int n = _slots.Count;
        Selected = ((Selected + (direction >= 0 ? 1 : -1)) % n + n) % n;
        return true;
    }

    /// <summary>Follows the group in hand when something else changed it (the cockpit button): keeps the slot when it already is of that group, else takes the first one of the group.</summary>
    public bool SelectGroup(WeaponGroup group)
    {
        if (Group == group) return false;
        int i = _slots.FindIndex(s => s.Group == group);
        return i >= 0 && Select(i);
    }

    /// <summary>The slot a key picks: <c>Key1</c> to <c>Key9</c> are slots 0 to 8. -1 for any other key or when the arsenal has no such slot.</summary>
    public int IndexOfKey(string key)
    {
        if (key.Length != 4 || !key.StartsWith("Key") || key[3] is < '1' or > '9') return -1;
        int i = key[3] - '1';
        return i < _slots.Count ? i : -1;
    }

    /// <summary>The number key of a slot ("1" to "9").</summary>
    public static string KeyHint(int index) => (index + 1).ToString();

    public void SetLive(ArsenalKind kind, ArsenalLive live)
    {
        if (kind == ArsenalKind.Laser) _laser = live; else _gun = live;
    }

    public ArsenalLive LiveOf(ArsenalKind kind) => kind == ArsenalKind.Laser ? _laser : _gun;

    /// <summary>True while any weapon fires.</summary>
    public bool AnyFiring => _laser.Firing || _gun.Firing;

    /// <summary>The state of a slot, from what its kind is doing.</summary>
    public SlotState StateOf(int index)
    {
        var live = LiveOf(_slots[index].Kind);
        if (live.PowerLostPermille >= 1000) return SlotState.NoPower;
        if (live.Dry) return SlotState.Dry;
        if (live.Blocked) return SlotState.Blocked;
        if (live.Firing) return SlotState.Firing;
        if (live.Cooldown > 0.001f) return SlotState.Cooling;
        if (live.PowerLostPermille > 0) return SlotState.LowPower;
        return SlotState.Ready;
    }

    /// <summary>The fill of a slot's state ring, 0 to 1: the share of the cooldown done while cooling, empty with no power or ammo, full otherwise.</summary>
    public float RingOf(int index)
    {
        var state = StateOf(index);
        return state == SlotState.Cooling ? 1f - Math.Clamp(LiveOf(_slots[index].Kind).Cooldown, 0f, 1f) : state is SlotState.NoPower or SlotState.Dry ? 0f : 1f;
    }

    /// <summary>Moves the selection marker toward the slot in hand: a short ease (about a tenth of a second), independent of the frame rate.</summary>
    public void Animate(float dt)
    {
        if (Selected < 0) return;
        float next = UiMotion.Approach(Cursor, Selected, dt, 22f);
        Cursor = MathF.Abs(next - Selected) < 0.005f ? Selected : next;
    }

    /// <summary>The Loc key of the one-word state ("ready", "firing", "dry"...).</summary>
    public static string StateKey(SlotState state) => "arsenal.state." + state.ToString().ToLowerInvariant();

    /// <summary>The second line of a slot, in the current language: the mining readout for a laser, the volley state and ammo for a gun.</summary>
    public string Detail(int index)
    {
        var slot = _slots[index];
        var live = LiveOf(slot.Kind);
        var state = StateOf(index);
        if (slot.Kind == ArsenalKind.Laser)
        {
            if (state is SlotState.NoPower or SlotState.LowPower or SlotState.Blocked) return Loc.T(StateKey(state));
            if (!live.HasDeposit) return Loc.T("arsenal.noDeposit");
            string rate = Loc.T("arsenal.rate", (live.MineTenths / 10f).ToString("0.0", Loc.Culture));
            return live.Slowed ? rate + " · " + Loc.T("arsenal.slowed") : rate + " · " + Loc.T("arsenal.left", live.DepositLeft);
        }
        string text = Loc.T(StateKey(state));
        return live.UsesAmmo && state != SlotState.Dry ? text + " · " + Loc.T("arsenal.ammo", live.Ammo) : text;
    }

    /// <summary>The tooltip of a slot: name and count, state or readout, and the key.</summary>
    public string Tooltip(int index)
    {
        var slot = _slots[index];
        string name = Loc.T(slot.NameKey) + (slot.Count > 1 ? " x" + slot.Count : "");
        return name + "\n" + Detail(index) + "\n" + Loc.T("arsenal.keyHint", KeyHint(index));
    }
}

/// <summary>What decides whether the bar is on screen.</summary>
/// <param name="Seated">The player sits in the ship (a weapon cannot be used on foot).</param>
/// <param name="BuildTool">A build tool is in hand: the number keys pick build entries, so the bar steps aside.</param>
/// <param name="ThreatNear">A wave or an enemy is near.</param>
/// <param name="ComputerOpen">The ship computer is open and the mouse is free.</param>
/// <param name="Mode">The HUD mode (F3).</param>
/// <param name="Detail">The HUD detail level (Shift+F3).</param>
public readonly record struct ArsenalContext(bool Seated, bool BuildTool, bool ThreatNear, bool ComputerOpen, HudMode Mode, HudDetail Detail);

/// <summary>
/// When the bar shows: always while a threat is near, the computer is open, or the player just switched or fired; otherwise it fades after <see cref="IdleSeconds"/> of
/// calm flight. It never shows with a build tool in hand, on foot, in the cockpit-only and minimal HUD modes, or with nothing fitted.
/// </summary>
public sealed class ArsenalVisibility
{
    public const float IdleSeconds = 4f;

    /// <summary>The idle seconds so far.</summary>
    public float Idle { get; private set; }

    public float Alpha { get; private set; }

    /// <summary>Just the icon and the count of the slot in hand (HUD detail level Minimal).</summary>
    public bool Compact { get; private set; }

    /// <summary>True when the context allows the bar at all (before the idle fade).</summary>
    public static bool Allowed(ArsenalContext c, bool anyFitted) =>
        anyFitted && c.Seated && !c.BuildTool && HudModes.Layers(c.Mode, c.Seated).Cards;

    /// <summary>A switch, a shot or a click restarts the idle time.</summary>
    public void Touch() => Idle = 0f;

    /// <summary>Advances the idle clock and the fade; <paramref name="used"/> is true while a weapon fires.</summary>
    public float Tick(float dt, ArsenalContext c, bool anyFitted, bool used)
    {
        Compact = c.Detail == HudDetail.Minimal;
        bool allowed = Allowed(c, anyFitted);
        if (used || c.ThreatNear || c.ComputerOpen) Idle = 0f; else Idle += dt;
        float target = allowed && Idle < IdleSeconds ? 1f : 0f;
        float rate = target > Alpha ? 14f : allowed ? 3.5f : 10f; // an idle fade is slow, a context that forbids the bar takes it away at once
        Alpha = Math.Clamp(UiMotion.Approach(Alpha, target + (target > Alpha ? 0.05f : -0.05f), dt, rate), 0f, 1f);
        if (MathF.Abs(Alpha - target) < 0.01f) Alpha = target;
        return Alpha;
    }
}
