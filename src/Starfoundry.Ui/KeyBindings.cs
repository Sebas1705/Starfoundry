namespace Starfoundry.Ui;

/// <summary>The key overrides held by <see cref="GameSettings"/>: a read-only map with value equality, so two settings with the same keys are equal.</summary>
public sealed class KeyOverrides : System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>
{
    public static readonly KeyOverrides None = new(new Dictionary<string, string[]>());

    public KeyOverrides(IDictionary<string, string[]> map) : base(map) { }

    public override bool Equals(object? obj) => obj is KeyOverrides o && Count == o.Count && this.All(p => o.TryGetValue(p.Key, out var v) && v.SequenceEqual(p.Value));

    public override int GetHashCode() => Count;
}

/// <summary>One binding slot of an action: the action id and the index in its key list ("move" has four: W, A, S, D).</summary>
public readonly record struct KeySlot(string ActionId, int Slot);

public enum AssignKind
{
    /// <summary>The key was free: it can be assigned.</summary>
    Ok,
    /// <summary>The slot already has that key.</summary>
    NoChange,
    /// <summary>Esc, F1 and Tab belong to the game and cannot be bound to anything.</summary>
    Reserved,
    /// <summary>Not a key the game can read (a mouse input, a modifier chord, an unknown name).</summary>
    Invalid,
    /// <summary>The action itself cannot be rebound (Esc, F1, the ship computer key, mouse and number-row sequences).</summary>
    Locked,
    /// <summary>Another slot that is active at the same time already uses the key.</summary>
    Conflict,
}

/// <summary>What assigning a key to a slot would do. <see cref="CanSwap"/> is true when exactly one other slot holds the key and exchanging the two keys leaves no collision.</summary>
public sealed record AssignResult(AssignKind Kind, IReadOnlyList<KeySlot> Conflicts, bool CanSwap)
{
    public bool Applied => Kind is AssignKind.Ok or AssignKind.NoChange;
    public static AssignResult Of(AssignKind kind) => new(kind, Array.Empty<KeySlot>(), false);
}

/// <summary>
/// The rules of rebinding, engine-free: which keys and actions can change, and which slots collide. A key collides with another slot when
/// both actions can be active together (<see cref="InputActions.ActiveSets"/>), so flying and walking actions may share a key but a
/// flying action and a global one may not. The rebinding screen, the settings file and the tests all go through here.
/// </summary>
public static class KeyRules
{
    /// <summary>A slot with no key. It never matches a press and its cap is drawn as a dash.</summary>
    public const string Unbound = "None";

    /// <summary>The prefix of a binding in settings.cfg: <c>bind.scan=J</c>.</summary>
    public const string Prefix = "bind.";

    /// <summary>Keys no action can use: Esc (cancel and back), F1 (help) and Tab (the ship computer, read before the focus system can take it).</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string> { "Escape", "F1", "Tab" };

    /// <summary>Actions the player cannot rebind: the keys that must always work, so no setting can strand someone outside the menus.</summary>
    public static readonly IReadOnlySet<string> LockedActions = new HashSet<string> { "cancel", "help", "computer" };

    /// <summary>Actions that may be left without a key (Clear). The rest are needed to fly, walk, build or use the computer.</summary>
    public static readonly IReadOnlySet<string> OptionalActions = new HashSet<string>
    {
        "look", "assist", "cruise", "scan", "unload", "load", "weaponNext", "rotate", "recipe", "kind", "openBuild", "openInventory", "openMap",
        "mapReset", "hudDetail", "quickSave", "quickLoad", "fullscreen",
    };

    private static readonly HashSet<string> Named = new()
    {
        "Space", "Shift", "Alt", "Enter", "Up", "Down", "Left", "Right", "Comma", "Period", "Minus", "Equal", "Semicolon", "Slash", "Backslash",
        "Apostrophe", "Quoteleft", "Bracketleft", "Bracketright", "Home", "End", "Pageup", "Pagedown", "Insert", "Delete", "Backspace",
    };

    /// <summary>True for a key the game can read and bind: a letter, a number-row digit, F1 to F12 or one of the named keys. Reserved keys are known but not assignable.</summary>
    public static bool IsKnown(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key.Length == 1) return key[0] is >= 'A' and <= 'Z';
        if (key.Length == 4 && key.StartsWith("Key", StringComparison.Ordinal)) return char.IsAsciiDigit(key[3]);
        if (key[0] == 'F' && int.TryParse(key.AsSpan(1), out int f)) return f is >= 1 and <= 12 && key == "F" + f;
        return Named.Contains(key) || Reserved.Contains(key);
    }

    public static bool IsReserved(string key) => Reserved.Contains(key);

    public static bool IsAssignable(string? key) => IsKnown(key) && !Reserved.Contains(key!);

    private static bool IsDigitRun(InputAction a) => a.Keys.Length > 2 && a.Keys.All(k => k.StartsWith("Key", StringComparison.Ordinal));

    /// <summary>An action can be rebound unless it is a locked one, uses a mouse input (the "@" pseudo keys) or is a row of number keys (1-4, 1-9 pick by position).</summary>
    public static bool IsRebindable(InputAction a) =>
        !LockedActions.Contains(a.Id) && !a.Keys.Any(k => k.StartsWith('@')) && !IsDigitRun(a);

    public static bool IsOptional(string actionId) => OptionalActions.Contains(actionId);

    /// <summary>True when two actions can be active at the same time, so they must not share a key.</summary>
    public static bool CoActive(InputAction a, InputAction b)
    {
        foreach (var set in InputActions.ActiveSets())
            if ((a.Contexts & set) != 0 && (b.Contexts & set) != 0) return true;
        return false;
    }

    /// <summary>Every slot other than <paramref name="slot"/> that holds <paramref name="key"/> and is active together with it.</summary>
    public static List<KeySlot> ConflictsOf(KeySlot slot, string key, Func<string, string[]> keysOf)
    {
        var list = new List<KeySlot>();
        if (key == Unbound) return list;
        var me = InputActions.Get(slot.ActionId);
        foreach (var other in InputActions.All)
        {
            if (other.Id != me.Id && !CoActive(me, other)) continue;
            var keys = keysOf(other.Id);
            for (int i = 0; i < keys.Length; i++)
                if (keys[i] == key && !(other.Id == me.Id && i == slot.Slot)) list.Add(new KeySlot(other.Id, i));
        }
        return list;
    }

    /// <summary>The cap text of a slot: the key as the player knows it, or a dash when unbound.</summary>
    public static string CapOf(string key) => key == Unbound ? "–" : InputActions.Cap(key);
}

/// <summary>
/// The player's key overrides while the rebinding screen edits them: only the actions that differ from the defaults are kept. Every change is
/// pushed to <see cref="InputActions"/> so the key handler, the key panel and the F1 help show the new keys at once, and <see cref="ToPairs"/>
/// gives the settings.cfg form (<c>bind.actionId=KEY1,KEY2</c>, one line per changed action; an unbound slot is written <c>None</c>).
/// </summary>
public sealed class KeyBindingEditor
{
    private readonly Dictionary<string, string[]> _map = new(StringComparer.Ordinal);

    private readonly bool _live;

    /// <param name="live">True in the game: every change is pushed to <see cref="InputActions"/>. Tests leave it off so they never touch the shared table.</param>
    public KeyBindingEditor(bool live = false) => _live = live;

    public KeyBindingEditor(IEnumerable<KeyValuePair<string, string[]>> overrides, bool live = false) { _live = live; Load(overrides); }

    /// <summary>Raised after any change, once <see cref="InputActions"/> has the new keys.</summary>
    public event Action? Changed;

    public IReadOnlyDictionary<string, string[]> Overrides => _map;

    public string[] KeysOf(string id) => _map.TryGetValue(id, out var k) ? k : InputActions.Get(id).Keys;

    public string KeyAt(string id, int slot) => KeysOf(id)[slot];

    public bool IsCustom(string id) => _map.ContainsKey(id);

    /// <summary>What assigning <paramref name="key"/> to the slot would do, without changing anything.</summary>
    public AssignResult Preview(string id, int slot, string key)
    {
        if (!InputActions.Has(id) || slot < 0 || slot >= InputActions.Get(id).Keys.Length) return AssignResult.Of(AssignKind.Invalid);
        var action = InputActions.Get(id);
        if (!KeyRules.IsRebindable(action)) return AssignResult.Of(AssignKind.Locked);
        if (KeyRules.IsReserved(key)) return AssignResult.Of(AssignKind.Reserved);
        if (!KeyRules.IsKnown(key)) return AssignResult.Of(AssignKind.Invalid);
        if (KeyAt(id, slot) == key) return AssignResult.Of(AssignKind.NoChange);
        var conflicts = KeyRules.ConflictsOf(new KeySlot(id, slot), key, KeysOf);
        if (conflicts.Count == 0) return AssignResult.Of(AssignKind.Ok);
        return new AssignResult(AssignKind.Conflict, conflicts, CanSwapWith(new KeySlot(id, slot), key, conflicts));
    }

    private bool CanSwapWith(KeySlot slot, string key, List<KeySlot> conflicts)
    {
        if (conflicts.Count != 1) return false;
        var other = conflicts[0];
        if (!KeyRules.IsRebindable(InputActions.Get(other.ActionId))) return false;
        string mine = KeyAt(slot.ActionId, slot.Slot);
        if (mine == KeyRules.Unbound && !KeyRules.IsOptional(other.ActionId)) return false;
        string[] Edit(string id)
        {
            var keys = (string[])KeysOf(id).Clone();
            if (id == slot.ActionId) keys[slot.Slot] = key;
            if (id == other.ActionId) keys[other.Slot] = mine;
            return keys;
        }
        return KeyRules.ConflictsOf(slot, key, Edit).Count == 0 && KeyRules.ConflictsOf(other, mine, Edit).Count == 0;
    }

    /// <summary>Assigns the key when it is free. Anything else leaves the bindings as they were and says why.</summary>
    public AssignResult Assign(string id, int slot, string key)
    {
        var r = Preview(id, slot, key);
        if (r.Kind == AssignKind.Ok) Set(id, slot, key);
        return r;
    }

    /// <summary>Gives the key to the slot and the slot's old key to the one that had it. False when the swap is not allowed (see <see cref="AssignResult.CanSwap"/>).</summary>
    public bool Swap(string id, int slot, string key)
    {
        var r = Preview(id, slot, key);
        if (r.Kind != AssignKind.Conflict || !r.CanSwap) return false;
        var other = r.Conflicts[0];
        string mine = KeyAt(id, slot);
        Set(id, slot, key, notify: false);
        Set(other.ActionId, other.Slot, mine);
        return true;
    }

    /// <summary>Leaves a slot of an optional action without a key.</summary>
    public bool Clear(string id, int slot)
    {
        if (!InputActions.Has(id) || !KeyRules.IsOptional(id) || slot < 0 || slot >= InputActions.Get(id).Keys.Length) return false;
        if (KeyAt(id, slot) == KeyRules.Unbound) return false;
        Set(id, slot, KeyRules.Unbound);
        return true;
    }

    /// <summary>Back to the default keys of one action. If another action took one of those keys meanwhile, that action goes back to its defaults too, so a reset never leaves a collision.</summary>
    public void Reset(string id)
    {
        if (!_map.Remove(id)) return;
        FixCollisions();
        Push();
    }

    public void ResetAll()
    {
        if (_map.Count == 0) return;
        _map.Clear();
        Push();
    }

    private void Set(string id, int slot, string key, bool notify = true)
    {
        var keys = (string[])KeysOf(id).Clone();
        keys[slot] = key;
        if (keys.SequenceEqual(InputActions.Get(id).Keys)) _map.Remove(id); else _map[id] = keys;
        if (notify) Push();
    }

    private void Push()
    {
        if (_live) InputActions.SetOverrides(_map);
        Changed?.Invoke();
    }

    /// <summary>Puts the current overrides in force without raising <see cref="Changed"/> (used at start-up).</summary>
    public void Apply() { if (_live) InputActions.SetOverrides(_map); }

    // ---- settings.cfg --------------------------------------------------------------------------------

    /// <summary>The settings pairs of the overrides, ordered by action id: <c>bind.scan=J</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToPairs() =>
        _map.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, string>(KeyRules.Prefix + p.Key, string.Join(',', p.Value))).ToList();

    /// <summary>
    /// Loads overrides tolerantly: unknown actions, locked actions, unknown keys and a wrong key count are ignored (a missing slot keeps its
    /// default), and if the result still has two actions on one key, the overridden actions involved go back to their defaults, so a
    /// hand-edited file can never leave the game with a collision.
    /// </summary>
    public void Load(IEnumerable<KeyValuePair<string, string[]>> overrides)
    {
        _map.Clear();
        foreach (var (id, keys) in overrides)
        {
            if (!InputActions.Has(id) || !KeyRules.IsRebindable(InputActions.Get(id))) continue;
            var def = InputActions.Get(id).Keys;
            var merged = (string[])def.Clone();
            for (int i = 0; i < merged.Length && i < keys.Length; i++)
            {
                string k = keys[i].Trim();
                if (k == KeyRules.Unbound ? KeyRules.IsOptional(id) : KeyRules.IsAssignable(k)) merged[i] = k;
            }
            var bound = merged.Where(k => k != KeyRules.Unbound).ToArray();
            if (bound.Distinct().Count() != bound.Length) continue; // the same key twice in one action
            if (!merged.SequenceEqual(def)) _map[id] = merged;
        }
        FixCollisions();
    }

    /// <summary>The overrides of the <c>bind.*</c> pairs of a settings file (anything else is skipped).</summary>
    public static Dictionary<string, string[]> ParsePairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (rawKey, value) in pairs)
        {
            string name = rawKey.Trim();
            if (!name.StartsWith(KeyRules.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string id = name[KeyRules.Prefix.Length..];
            var real = InputActions.All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (real is null) continue;
            map[real.Id] = value.Trim().Trim('"').Split(',', StringSplitOptions.TrimEntries);
        }
        return map;
    }

    private void FixCollisions()
    {
        for (int guard = 0; guard < 128; guard++)
        {
            string? bad = null;
            foreach (var a in InputActions.All)
            {
                var keys = KeysOf(a.Id);
                for (int i = 0; i < keys.Length && bad is null; i++)
                    foreach (var c in KeyRules.ConflictsOf(new KeySlot(a.Id, i), keys[i], KeysOf))
                    {
                        bad = _map.ContainsKey(a.Id) ? a.Id : _map.ContainsKey(c.ActionId) ? c.ActionId : null;
                        if (bad is not null) break;
                    }
                if (bad is not null) break;
            }
            if (bad is null) return;
            _map.Remove(bad);
        }
    }
}
