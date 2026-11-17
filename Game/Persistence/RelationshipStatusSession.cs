using System;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Compatibility wrapper for callers of the old Phase 12C session helper.
/// Opening the game must NEVER delete saved progress. Use SaveGameProgress.ResetAll
/// only for an explicit New Game / Reset Progress action.
/// </summary>
[Obsolete("Use SaveGameProgress.ResetAll() for explicit resets; opening a session keeps saved progress.")]
public static class RelationshipStatusSession
{
    // No longer deletes status overrides on first scene load (the old data-loss bug).
    public static void EnsureFreshPrototypeSession(SqliteRelationshipStatusStore store) =>
        _ = store ?? throw new ArgumentNullException(nameof(store));

    // Preserve the method for any old caller, but reset ALL runtime progress together.
    public static void StartNewGame(SqliteRelationshipStatusStore store)
    {
        _ = store ?? throw new ArgumentNullException(nameof(store));
        SaveGameProgress.ResetAll();
    }

    public static void KeepSavedStatusesForLoad() { } // Persistence is now the default.
}
