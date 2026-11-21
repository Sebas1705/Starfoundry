namespace Starfoundry.Ui;

/// <summary>
/// Save slot naming and choice, engine-free: which slot Continue, F9 and the pause menu load, the temporary and backup names of an
/// atomic write, and the short, localised reason shown when a load fails.
/// </summary>
public static class SaveSlots
{
    public const string Quick = "quicksave";
    public const string Auto = "autosave";
    /// <summary>The autosave before the last one: a new game's first autosave moves the previous game's autosave here instead of overwriting it.</summary>
    public const string AutoPrevious = "autosave.prev";

    /// <summary>The slots that Continue, F9 and the pause menu choose the newest from.</summary>
    public static readonly string[] Loadable = { Quick, Auto };

    /// <summary>The file written first; renamed over the slot once complete.</summary>
    public static string TempName(string slot) => slot + ".tmp";

    /// <summary>The copy of the slot's previous contents kept when it is overwritten.</summary>
    public static string BackupName(string slot) => slot + ".bak";

    /// <summary>Where the slot's current contents go before it is overwritten: the autosave rotates to <see cref="AutoPrevious"/>, others to a backup.</summary>
    public static string PreviousName(string slot) => slot == Auto ? AutoPrevious : BackupName(slot);

    /// <summary>
    /// The newest of the slots that exist (modified time per slot name; null when the slot does not exist).
    /// A tie goes to the earlier slot of <see cref="Loadable"/>. Null when none exists.
    /// </summary>
    public static string? Newest(Func<string, ulong?> modifiedTime)
    {
        string? best = null;
        ulong bestTime = 0;
        foreach (string name in Loadable)
        {
            if (modifiedTime(name) is not { } t) continue;
            if (best is null || t > bestTime) { best = name; bestTime = t; }
        }
        return best;
    }

    /// <summary>The <c>toast.loadFailed.*</c> key of the reason a load failed.</summary>
    public static string LoadFailureKey(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "toast.loadFailed.notFound",
        InvalidDataException when e.Message.StartsWith("The save was made with other game content", StringComparison.Ordinal) => "toast.loadFailed.content",
        _ => "toast.loadFailed.corrupt",
    };
}
