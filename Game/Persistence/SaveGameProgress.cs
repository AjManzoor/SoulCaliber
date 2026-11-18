using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SoulCaliber.Dialogue;

/// <summary>
/// A single EXPLICIT New Game / Reset Progress operation. Ordinary Godot launch,
/// scene reload and application exit never reset saved state. The authored
/// content database is never touched, and no automatic backup is created.
/// </summary>
public static class SaveGameProgress
{
    public static void ResetAll()
    {
        // Validate / migrate before reset. New Game never resets the schema version.
        SaveGameSchemaManager.EnsureCurrent();

        using var connection = new SqliteConnection(DatabasePaths.ConnectionString(DatabasePaths.Save));
        connection.Open();
        using var transaction = connection.BeginTransaction();
        // Children of interactions/memories are removed through ON DELETE CASCADE.
        // All operations are in one transaction: a failure leaves progress unchanged.
        foreach (string table in new[]
        {
            "SavedRelationshipValues",
            "SavedCharacterStatusOverrides",
            "InteractionHistory",
            "SavedSpecialEncounterCompletions",
            "SavedMemoryOccurrences"
        })
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"DELETE FROM {table}";
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
