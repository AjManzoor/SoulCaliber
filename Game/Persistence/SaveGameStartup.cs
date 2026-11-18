using System;
using System.IO;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Categories used for safe, actionable startup errors. These are runtime
/// load failures, not invitations to reset or automatically repair a save.
/// </summary>
public enum SaveGameStartupProblem
{
    None,
    NewerVersion,
    CorruptDatabase,
    IncompatibleSchema,
    StorageUnavailable,
    MigrationFailed,
    Unknown
}

/// <summary>Thrown by the schema manager when the reason is known.</summary>
public sealed class SaveGameSchemaException : InvalidOperationException
{
    public SaveGameStartupProblem Problem { get; }

    public SaveGameSchemaException(SaveGameStartupProblem problem, string message,
        Exception? innerException = null) : base(message, innerException)
    {
        Problem = problem;
    }
}

public sealed record SaveGameStartupResult(
    bool Success,
    bool CreatedNewSave,
    SaveGameStartupProblem Problem,
    string UserMessage,
    string TechnicalDetails);

/// <summary>
/// Single, UI-agnostic startup entry point for both Godot scenes. It calls the
/// central schema manager, never deletes/reset/repairs a save, and translates
/// typed failures into readable messages. Full technical details are logged by
/// the caller, not shown as the player-facing explanation.
/// </summary>
public static class SaveGameStartup
{
    public static SaveGameStartupResult TryInitialize() => TryInitialize(DatabasePaths.Save);

    public static SaveGameStartupResult TryInitialize(string savePath)
    {
        // Only a path that did NOT exist before the call counts as a new save.
        // An existing but blank/invalid file must NOT be treated as a new game.
        bool existed = !string.IsNullOrWhiteSpace(savePath) && File.Exists(savePath);
        try
        {
            SaveGameSchemaManager.EnsureCurrent(savePath);
            return new SaveGameStartupResult(
                true, !existed, SaveGameStartupProblem.None,
                existed ? "Savegame loaded." : "New savegame created.", string.Empty);
        }
        catch (SaveGameSchemaException ex)
        {
            return Failure(ex.Problem, ex.ToString());
        }
        catch (Exception ex)
        {
            // Unexpected errors are still failures. Do not fall back to New Game.
            return Failure(SaveGameStartupProblem.Unknown, ex.ToString());
        }
    }

    private static SaveGameStartupResult Failure(SaveGameStartupProblem problem, string details)
    {
        string message = problem switch
        {
            SaveGameStartupProblem.NewerVersion =>
                "This savegame was created by a newer version of SoulCaliber. Open it with that version. Your save was not reset.",
            SaveGameStartupProblem.CorruptDatabase =>
                "The savegame database appears damaged or is not a valid SQLite database. Gameplay has been stopped. Back up the file before attempting recovery; nothing was automatically reset.",
            SaveGameStartupProblem.IncompatibleSchema =>
                "This savegame has an incomplete or unrecognised structure. Gameplay has been stopped to protect your progress. Back up the save and inspect the diagnostic log.",
            SaveGameStartupProblem.StorageUnavailable =>
                "The savegame cannot be opened or written. Check folder permissions, disk space and whether another application is locking the file. Existing progress was not reset.",
            SaveGameStartupProblem.MigrationFailed =>
                "The savegame upgrade could not complete. Gameplay has been stopped rather than loading an incompatible save. Back up the file and inspect the diagnostic log.",
            _ =>
                "The savegame could not be loaded. Gameplay has been stopped and no automatic reset was performed. Back up the save and inspect the diagnostic log."
        };
        return new SaveGameStartupResult(false, false, problem, message, details);
    }
}
