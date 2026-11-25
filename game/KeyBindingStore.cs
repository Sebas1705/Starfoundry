using Starfoundry.Ui;

namespace Starfoundry.Game;

/// <summary>
/// The live key overrides of the running game: one <see cref="KeyBindingEditor"/> that feeds <see cref="InputActions"/> (the key handler, the key
/// panel and the F1 help) and saves into <c>settings.cfg</c> through <see cref="SettingsStore"/> after every change.
/// </summary>
public static class KeyBindingStore
{
    public static KeyBindingEditor Editor { get; } = CreateEditor();

    private static bool _syncing;

    private static KeyBindingEditor CreateEditor()
    {
        var editor = new KeyBindingEditor(live: true);
        editor.Changed += () =>
        {
            if (_syncing) return;
            SettingsStore.Set(SettingsStore.Current with { Bindings = new KeyOverrides(new System.Collections.Generic.Dictionary<string, string[]>(editor.Overrides)) });
        };
        return editor;
    }

    /// <summary>Takes the overrides of freshly loaded or reset settings (called by <see cref="SettingsStore"/>).</summary>
    public static void Sync(GameSettings s)
    {
        _syncing = true;
        try { Editor.Load(s.Bindings); Editor.Apply(); }
        finally { _syncing = false; }
    }
}
