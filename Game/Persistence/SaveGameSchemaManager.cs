using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SoulCaliber.Dialogue;

/// <summary>
/// The ONLY owner of the runtime savegame schema. Call once before constructing
/// any save stores or using a SaveDbContext. Authored Studio content is unrelated.
/// An unversioned pre-13G-5B save is adopted only if its known complete layout
/// is verified; unrecognised files are never overwritten or reset.
/// </summary>
public static class SaveGameSchemaManager
{
    public const int CurrentVersion = 2;

    internal static readonly Dictionary<string, string[]> RequiredColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SavedRelationshipValues"] = new[] { "FromCharacterId", "ToCharacterId", "AttributeKey", "Value" },
        ["InteractionHistory"] = new[] { "Id", "OccurredUtc", "InteractionKey", "NodeKey", "OptionKey",
            "ActorId", "PartnerId", "Succeeded", "Chance", "Roll", "TargetReaction" },
        ["RelationshipChanges"] = new[] { "Id", "SavedInteractionId", "FromCharacterId", "ToCharacterId",
            "Attribute", "Before", "After" },
        ["SavedInteractionStatusChanges"] = new[] { "Id", "SavedInteractionId", "FromCharacterId", "ToCharacterId",
            "Before", "After" },
        ["SavedCharacterStatusOverrides"] = new[] { "FromCharacterId", "ToCharacterId", "StatusKey" },
        ["SavedMemoryOccurrences"] = new[] { "Id", "MemoryKey", "ActorCharacterId", "PlayerCharacterId",
            "Severity", "KnowledgeLevel", "OccurredUtc" },
        ["SavedMemoryAffectedCharacters"] = new[] { "MemoryId", "CharacterId" },
        ["SavedMemoryKnowledge"] = new[] { "MemoryId", "CharacterId" },
        ["SavedMemoryAudienceModes"] = new[] { "MemoryId", "Mode" },
        ["SavedSpecialEncounterCompletions"] = new[] { "Id", "EncounterKey", "InitiatorCharacterId",
            "RecipientCharacterId", "MemoryOccurrenceId", "CompletedUtc" }
    };

    /// <summary>
    /// Idempotent schema bootstrap/validation. Fresh databases and verified legacy
    /// databases are advanced to version 1 in ONE transaction. An older versioned
    /// database will be upgraded by ordered steps when later migrations are added.
    /// Does not touch normal gameplay rows or the published content database.
    /// </summary>
    public static void EnsureCurrent() => EnsureCurrent(DatabasePaths.Save);

    // Explicit path makes temporary-database integration tests possible without Godot.
    public static void EnsureCurrent(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
            throw new ArgumentException("A savegame file path is required.", nameof(savePath));

        // ReadWriteCreate is appropriate for the new-game case only. For existing
        // files, ReadWrite prevents a typo from silently replacing a missing save.
        bool existed = File.Exists(savePath);
        bool migrationInProgress = false;
        var builder = new SqliteConnectionStringBuilder(DatabasePaths.ConnectionString(savePath))
        {
            Mode = existed ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        };
        try
        {
            // A pre-existing zero-byte file is NOT a new save. Never adopt it
            // or turn it into a fresh database without the player's consent.
            if (existed && new FileInfo(savePath).Length == 0)
                throw new SaveGameSchemaException(SaveGameStartupProblem.IncompatibleSchema,
                    "Existing savegame is empty and cannot be safely adopted.");

            // Verify the existing file before opening it for writes. This catches
            // corrupt SQLite files that might otherwise reach schema bootstrap.
            if (existed)
                CheckExistingFileIntegrity(savePath);

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var transaction = connection.BeginTransaction();
            int version = ReadVersion(connection, transaction);
            if (version > CurrentVersion)
                throw new SaveGameSchemaException(SaveGameStartupProblem.NewerVersion,
                    $"Savegame schema version {version} is newer than this game supports ({CurrentVersion}). " +
                    "Use the newer game version; the save has not been modified.");
            if (version < 0)
                throw new SaveGameSchemaException(SaveGameStartupProblem.IncompatibleSchema,
                    $"Invalid savegame schema version {version}.");

            if (version == 0)
            {
                var objects = GetUserObjects(connection, transaction);
                if (objects.Count == 0)
                {
                    if (existed)
                        throw new SaveGameSchemaException(SaveGameStartupProblem.IncompatibleSchema,
                            "An existing savegame contains no recognised tables; refusing to initialise it as a new game.");
                    // Generate the original EF table/index SQL from the actual EF model.
                    // This avoids hand-maintaining a second copy of the EF schema.
                    using var model = new SaveDbContext();
                    Execute(connection, transaction, model.Database.GenerateCreateScript());
                    Execute(connection, transaction, ExtraSchemaSql);
                }
                else
                {
                    // A version-zero file can contain real gameplay progress. Only
                    // a complete, recognised legacy layout may be stamped as v1.
                    VerifyLayout(connection, transaction, strictLegacyTables: true);
                }
                VerifyLayout(connection, transaction, strictLegacyTables: false);
                SetVersion(connection, transaction, 1);
                version = 1;
            }

            // New numbered migrations are applied here, in ascending order, using
            // this SAME transaction. The version is changed only with the schema.
            for (int next = version + 1; next <= CurrentVersion; next++)
            {
                migrationInProgress = true;
                ApplyMigration(connection, transaction, next);
                VerifyLayout(connection, transaction, strictLegacyTables: false, requireV2: next >= 2);
                SetVersion(connection, transaction, next);
                migrationInProgress = false;
            }

            VerifyLayout(connection, transaction, strictLegacyTables: false, requireV2: version >= 2 || CurrentVersion >= 2);
            transaction.Commit();
        }
        catch (SaveGameSchemaException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            // SQLITE_CORRUPT=11, SQLITE_NOTADB=26, SQLITE_IOERR=10,
            // SQLITE_CANTOPEN=14, SQLITE_READONLY=8, SQLITE_BUSY=5,
            // SQLITE_LOCKED=6. Any other SQLite failure stops startup too.
            var reason = ex.SqliteErrorCode is 11 or 26
                ? SaveGameStartupProblem.CorruptDatabase
                : ex.SqliteErrorCode is 5 or 6 or 8 or 10 or 14
                    ? SaveGameStartupProblem.StorageUnavailable
                    : migrationInProgress
                        ? SaveGameStartupProblem.MigrationFailed
                        : SaveGameStartupProblem.IncompatibleSchema;
            throw new SaveGameSchemaException(reason,
                $"Cannot initialise savegame '{savePath}': {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SaveGameSchemaException(SaveGameStartupProblem.StorageUnavailable,
                $"Cannot access savegame '{savePath}': {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new SaveGameSchemaException(SaveGameStartupProblem.MigrationFailed,
                $"Cannot upgrade savegame '{savePath}': {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new SaveGameSchemaException(
                migrationInProgress ? SaveGameStartupProblem.MigrationFailed : SaveGameStartupProblem.IncompatibleSchema,
                $"Cannot initialise savegame '{savePath}': {ex.Message}", ex);
        }
    }

    private static void CheckExistingFileIntegrity(string savePath)
    {
        // Read-only and unpooled: this diagnostic must not create or replace
        // an invalid save, nor leave a pooled file lock behind on Windows.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = savePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check(1);";
        var response = Convert.ToString(command.ExecuteScalar());
        if (!string.Equals(response, "ok", StringComparison.OrdinalIgnoreCase))
            throw new SaveGameSchemaException(SaveGameStartupProblem.CorruptDatabase,
                "SQLite quick_check did not pass: " + (response ?? "no result"));
    }

    private static void ApplyMigration(SqliteConnection connection, SqliteTransaction transaction, int targetVersion)
    {
        // Add explicit, ordered version cases here when Phase 14 introduces new
        // saved fields. Each migration must run on this connection + transaction.
        if (targetVersion == 2)
        {
            Execute(connection, transaction, @"
                CREATE TABLE SavedMemoryAudienceModes (
                    MemoryId INTEGER PRIMARY KEY REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
                    Mode TEXT NOT NULL CHECK(Mode='CUSTOM')
                );");
            return;
        }
        throw new NotSupportedException($"Missing savegame migration to version {targetVersion}.");
    }

    private static int ReadVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static HashSet<string> GetUserObjects(SqliteConnection connection, SqliteTransaction transaction)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view','trigger') AND name NOT GLOB 'sqlite_*';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static HashSet<string> GetUserTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT GLOB 'sqlite_*';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static void VerifyLayout(SqliteConnection connection, SqliteTransaction transaction,
        bool strictLegacyTables, bool requireV2 = false)
    {
        var tables = GetUserTables(connection, transaction);
        var absent = RequiredColumns.Keys.Where(table => (requireV2 || table != "SavedMemoryAudienceModes") && !tables.Contains(table)).ToArray();
        if (absent.Length > 0)
            throw new InvalidOperationException("Incomplete savegame schema; missing tables: " + string.Join(", ", absent));
        if (strictLegacyTables)
        {
            var extra = tables.Where(table => !RequiredColumns.ContainsKey(table)).ToArray();
            if (extra.Length > 0)
                throw new InvalidOperationException("Unrecognised tables in unversioned save: " + string.Join(", ", extra));
        }

        foreach (var pair in RequiredColumns)
        {
            if (!requireV2 && pair.Key == "SavedMemoryAudienceModes") continue;
            // Names are hardcoded in RequiredColumns, never read from user input.
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $"PRAGMA table_info(\"{pair.Key}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
            var missing = pair.Value.Where(column => !columns.Contains(column)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Invalid {pair.Key} table; missing columns: {string.Join(", ", missing)}");
        }

        VerifyPrimaryKey(connection, transaction, "SavedRelationshipValues",
            "FromCharacterId", "ToCharacterId", "AttributeKey");
        VerifyPrimaryKey(connection, transaction, "SavedCharacterStatusOverrides",
            "FromCharacterId", "ToCharacterId");
        VerifyPrimaryKey(connection, transaction, "SavedMemoryAffectedCharacters", "MemoryId", "CharacterId");
        VerifyPrimaryKey(connection, transaction, "SavedMemoryKnowledge", "MemoryId", "CharacterId");

        VerifyForeignKey(connection, transaction, "RelationshipChanges", "SavedInteractionId", "InteractionHistory");
        VerifyForeignKey(connection, transaction, "SavedInteractionStatusChanges", "SavedInteractionId", "InteractionHistory");
        VerifyForeignKey(connection, transaction, "SavedMemoryAffectedCharacters", "MemoryId", "SavedMemoryOccurrences");
        VerifyForeignKey(connection, transaction, "SavedMemoryKnowledge", "MemoryId", "SavedMemoryOccurrences");
        if (requireV2)
            VerifyForeignKey(connection, transaction, "SavedMemoryAudienceModes", "MemoryId", "SavedMemoryOccurrences");
        VerifyForeignKey(connection, transaction, "SavedSpecialEncounterCompletions", "MemoryOccurrenceId", "SavedMemoryOccurrences");
        VerifyIndex(connection, transaction, "SavedSpecialEncounterCompletions", "UX_SpecialEncounter_Pair", unique: true);
        VerifyIndex(connection, transaction, "SavedSpecialEncounterCompletions", "UX_SpecialEncounter_Occurrence", unique: true);

        using var fk = connection.CreateCommand();
        fk.Transaction = transaction;
        fk.CommandText = "PRAGMA foreign_key_check;";
        using var violations = fk.ExecuteReader();
        if (violations.Read())
            throw new InvalidOperationException($"Savegame contains broken foreign key references in '{violations.GetString(0)}'.");
    }

    private static void VerifyPrimaryKey(SqliteConnection connection, SqliteTransaction transaction,
        string table, params string[] expected)
    {
        var keys = new SortedDictionary<int, string>();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int position = reader.GetInt32(5);
            if (position > 0) keys.Add(position, reader.GetString(1));
        }
        if (!keys.Values.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Savegame table {table} has an unexpected primary key.");
    }

    private static void VerifyForeignKey(SqliteConnection connection, SqliteTransaction transaction,
        string table, string from, string target)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(2), target, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reader.GetString(3), from, StringComparison.OrdinalIgnoreCase))
                return;
        throw new InvalidOperationException($"Savegame table {table} is missing its {from} foreign key.");
    }

    private static void VerifyIndex(SqliteConnection connection, SqliteTransaction transaction,
        string table, string index, bool unique)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"PRAGMA index_list(\"{table}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), index, StringComparison.OrdinalIgnoreCase) &&
                (!unique || reader.GetInt32(2) == 1))
                return;
        throw new InvalidOperationException($"Savegame is missing expected index {index}.");
    }

    // The four EF-owned tables are generated by SaveDbContext.GenerateCreateScript.
    // Everything else lives here, in dependency order, and is never created in stores.
    private const string ExtraSchemaSql = @"
        CREATE TABLE SavedCharacterStatusOverrides (
            FromCharacterId TEXT NOT NULL COLLATE NOCASE,
            ToCharacterId TEXT NOT NULL COLLATE NOCASE,
            StatusKey TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY(FromCharacterId,ToCharacterId),
            CHECK(FromCharacterId <> ToCharacterId COLLATE NOCASE)
        ) WITHOUT ROWID;
        CREATE TABLE SavedMemoryOccurrences (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MemoryKey TEXT NOT NULL COLLATE NOCASE,
            ActorCharacterId TEXT NOT NULL COLLATE NOCASE,
            PlayerCharacterId TEXT NOT NULL COLLATE NOCASE,
            Severity TEXT NOT NULL CHECK(Severity IN ('MAJOR','MINOR')),
            KnowledgeLevel TEXT NOT NULL CHECK(KnowledgeLevel IN ('QUIET','ALLIES','GLOBAL')),
            OccurredUtc TEXT NOT NULL
        );
        CREATE INDEX IX_SavedMemoryOccurrences_MemoryKeyActor
            ON SavedMemoryOccurrences(MemoryKey, ActorCharacterId);
        CREATE TABLE SavedMemoryAffectedCharacters (
            MemoryId INTEGER NOT NULL REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
            CharacterId TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY (MemoryId,CharacterId)
        ) WITHOUT ROWID;
        CREATE INDEX IX_SavedMemoryAffectedCharacters_Character
            ON SavedMemoryAffectedCharacters(CharacterId,MemoryId);
        CREATE TABLE SavedMemoryKnowledge (
            MemoryId INTEGER NOT NULL REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
            CharacterId TEXT NOT NULL COLLATE NOCASE,
            PRIMARY KEY (MemoryId,CharacterId)
        ) WITHOUT ROWID;
        CREATE INDEX IX_SavedMemoryKnowledge_Character
            ON SavedMemoryKnowledge(CharacterId,MemoryId);
        CREATE TABLE SavedSpecialEncounterCompletions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EncounterKey TEXT NOT NULL COLLATE NOCASE,
            InitiatorCharacterId TEXT NOT NULL COLLATE NOCASE,
            RecipientCharacterId TEXT NOT NULL COLLATE NOCASE,
            MemoryOccurrenceId INTEGER NULL REFERENCES SavedMemoryOccurrences(Id) ON DELETE CASCADE,
            CompletedUtc TEXT NOT NULL,
            CHECK(InitiatorCharacterId <> RecipientCharacterId COLLATE NOCASE)
        );
        CREATE UNIQUE INDEX UX_SpecialEncounter_Pair
            ON SavedSpecialEncounterCompletions
                (EncounterKey, InitiatorCharacterId, RecipientCharacterId)
            WHERE MemoryOccurrenceId IS NULL;
        CREATE UNIQUE INDEX UX_SpecialEncounter_Occurrence
            ON SavedSpecialEncounterCompletions
                (EncounterKey, InitiatorCharacterId, RecipientCharacterId, MemoryOccurrenceId)
            WHERE MemoryOccurrenceId IS NOT NULL;
    ";
}
