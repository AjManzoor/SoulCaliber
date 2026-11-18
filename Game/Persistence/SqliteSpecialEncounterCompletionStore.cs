using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Savegame-only technical state for special dialogue completion. Memories are
/// historical facts and are never deleted or altered when a conversation ends.
/// Once-per-pair records have no memory id; once-per-occurrence records identify
/// the specific incident the completed conversation was about.
/// </summary>
public sealed class SqliteSpecialEncounterCompletionStore
{
    public SqliteSpecialEncounterCompletionStore()
    {
        // SaveGameSchemaManager owns the completion table and its unique indexes.
    }

    private static SqliteConnection OpenSave()
    {
        var builder = new SqliteConnectionStringBuilder(DatabasePaths.ConnectionString(DatabasePaths.Save))
        {
            ForeignKeys = true
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    private static string Key(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} is required.", field);
        return value.Trim().ToUpperInvariant();
    }

    private static (string Encounter, string Initiator, string Recipient) Pair(
        string encounterKey, string initiatorId, string recipientId)
    {
        string encounter = Key(encounterKey, nameof(encounterKey));
        string initiator = Key(initiatorId, nameof(initiatorId));
        string recipient = Key(recipientId, nameof(recipientId));
        if (initiator == recipient)
            throw new ArgumentException("A special encounter requires different characters.");
        return (encounter, initiator, recipient);
    }

    /// <summary>Whether a one-time encounter finished for this directional pair.</summary>
    public bool HasCompletedPair(string encounterKey, string initiatorId, string recipientId)
    {
        var (encounter, initiator, recipient) = Pair(encounterKey, initiatorId, recipientId);
        using var save = OpenSave();
        using var command = save.CreateCommand();
        command.CommandText = @"
            SELECT 1 FROM SavedSpecialEncounterCompletions
            WHERE EncounterKey=$encounter AND InitiatorCharacterId=$initiator
              AND RecipientCharacterId=$recipient AND MemoryOccurrenceId IS NULL
            LIMIT 1";
        command.Parameters.AddWithValue("$encounter", encounter);
        command.Parameters.AddWithValue("$initiator", initiator);
        command.Parameters.AddWithValue("$recipient", recipient);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Completed incident IDs for a particular encounter and directional pair.</summary>
    public HashSet<long> CompletedOccurrenceIds(string encounterKey, string initiatorId, string recipientId)
    {
        var (encounter, initiator, recipient) = Pair(encounterKey, initiatorId, recipientId);
        using var save = OpenSave();
        using var command = save.CreateCommand();
        command.CommandText = @"
            SELECT MemoryOccurrenceId FROM SavedSpecialEncounterCompletions
            WHERE EncounterKey=$encounter AND InitiatorCharacterId=$initiator
              AND RecipientCharacterId=$recipient AND MemoryOccurrenceId IS NOT NULL";
        command.Parameters.AddWithValue("$encounter", encounter);
        command.Parameters.AddWithValue("$initiator", initiator);
        command.Parameters.AddWithValue("$recipient", recipient);
        var completed = new HashSet<long>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) completed.Add(reader.GetInt64(0));
        return completed;
    }

    /// <summary>
    /// Idempotently records completion after a dialogue reaches its terminal node.
    /// REPEATABLE encounters intentionally have no completion gate. Returns true
    /// if a new completion row was stored, or false if already recorded/repeatable.
    /// </summary>
    public bool RecordCompletion(SelectedSpecialEncounter encounter,
        string initiatorId, string recipientId)
    {
        if (encounter is null) throw new ArgumentNullException(nameof(encounter));
        var (key, initiator, recipient) = Pair(encounter.EncounterKey, initiatorId, recipientId);
        // Selection is bound to a particular directional pair. If a stale menu
        // item or a scene configuration changes participants after selection,
        // refuse to complete the wrong encounter rather than recording it.
        if (!string.Equals(Key(encounter.InitiatorCharacterId, nameof(encounter.InitiatorCharacterId)),
                initiator, StringComparison.Ordinal) ||
            !string.Equals(Key(encounter.RecipientCharacterId, nameof(encounter.RecipientCharacterId)),
                recipient, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Special encounter participants changed after selection; completion not saved.");

        if (encounter.RepetitionMode == "REPEATABLE") return false;
        if (encounter.RepetitionMode is not ("ONCE_PER_PAIR" or "ONCE_PER_OCCURRENCE"))
            throw new InvalidOperationException("Unknown encounter repetition mode: " + encounter.RepetitionMode);
        if (encounter.RepetitionMode == "ONCE_PER_OCCURRENCE" && encounter.AnchorMemoryOccurrenceId is null)
            throw new InvalidOperationException("Per-occurrence completion requires an anchor memory id.");
        // A pair-limited encounter may be ABOUT a memory. The memory is a
        // dialogue binding, not the completion key for this repetition mode.
        long? completionMemoryId = encounter.RepetitionMode == "ONCE_PER_OCCURRENCE"
            ? encounter.AnchorMemoryOccurrenceId : null;
        if (encounter.AnchorMemoryOccurrenceId is long occurrenceId && occurrenceId <= 0)
            throw new InvalidOperationException("Invalid special encounter anchor memory id.");

        using var save = OpenSave();
        using var command = save.CreateCommand();
        command.CommandText = @"
            INSERT INTO SavedSpecialEncounterCompletions
                (EncounterKey, InitiatorCharacterId, RecipientCharacterId,
                 MemoryOccurrenceId, CompletedUtc)
            VALUES ($encounter, $initiator, $recipient, $memory, $utc)
            ON CONFLICT DO NOTHING";
        // A duplicate completion is harmless, but invalid CHECK / FK / NOT NULL
        // values must throw. INSERT OR IGNORE would hide some of those errors.
        command.Parameters.AddWithValue("$encounter", key);
        command.Parameters.AddWithValue("$initiator", initiator);
        command.Parameters.AddWithValue("$recipient", recipient);
        command.Parameters.AddWithValue("$memory", (object?)completionMemoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        if (command.ExecuteNonQuery() > 0) return true;

        // A conflict is only an acceptable no-op when THIS exact completion is
        // already stored. This is a read-only verification, not a new anchor
        // search: the original selected occurrence must never be substituted.
        using var existing = save.CreateCommand();
        existing.CommandText = @"
            SELECT 1 FROM SavedSpecialEncounterCompletions
            WHERE EncounterKey=$encounter AND InitiatorCharacterId=$initiator
              AND RecipientCharacterId=$recipient AND MemoryOccurrenceId IS $memory
            LIMIT 1";
        existing.Parameters.AddWithValue("$encounter", key);
        existing.Parameters.AddWithValue("$initiator", initiator);
        existing.Parameters.AddWithValue("$recipient", recipient);
        existing.Parameters.AddWithValue("$memory", (object?)completionMemoryId ?? DBNull.Value);
        if (existing.ExecuteScalar() is null)
            throw new InvalidOperationException("Completion write was ignored without a matching existing record.");
        return false;
    }
}
