namespace Starfoundry.Ui;

/// <summary>
/// The short, localised reason shown when a load fails, engine-free. The files themselves (slot ids, atomic writes, rotation, migration) are
/// <see cref="SaveFolder"/>, <see cref="SaveNaming"/> and <see cref="SaveRotation"/>.
/// </summary>
public static class SaveSlots
{
    /// <summary>The <c>toast.loadFailed.*</c> key of the reason a load failed. The file is never touched because of it.</summary>
    public static string LoadFailureKey(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "toast.loadFailed.notFound",
        SaveFileException { Problem: SaveProblem.NewerFormat } => "toast.loadFailed.newer",
        InvalidDataException when e.Message.StartsWith("The save was made with other game content", StringComparison.Ordinal) => "toast.loadFailed.content",
        InvalidDataException when e.Message.StartsWith("The save belongs to the scenario", StringComparison.Ordinal) => "toast.loadFailed.scenario",
        InvalidDataException when e.Message.StartsWith("Unsupported log version", StringComparison.Ordinal) => "toast.loadFailed.newer",
        _ => "toast.loadFailed.corrupt",
    };
}
