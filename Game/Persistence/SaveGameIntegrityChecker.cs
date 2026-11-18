using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SoulCaliber.Dialogue;

public enum SaveIntegritySeverity { Information, Warning, Error }

public sealed record SaveIntegrityFinding(SaveIntegritySeverity Severity, string Area, string Message);

/// <summary>Read-only diagnostic results. Warnings about removed authored content
/// are not proof that the SQLite save itself is corrupt.</summary>
public sealed class SaveIntegrityReport
{
    private readonly List<SaveIntegrityFinding> _findings = new();
    public IReadOnlyList<SaveIntegrityFinding> Findings => _findings;
    public int Errors => _findings.Count(x => x.Severity == SaveIntegritySeverity.Error);
    public int Warnings => _findings.Count(x => x.Severity == SaveIntegritySeverity.Warning);
    public bool HasErrors => Errors > 0;
    public int SchemaVersion { get; internal set; } = -1;
    public bool SaveOpened { get; internal set; }
    public bool ContentOpened { get; internal set; }

    internal void Add(SaveIntegritySeverity severity, string area, string message) =>
        _findings.Add(new SaveIntegrityFinding(severity, area, message));

    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine("SoulCaliber — Savegame Integrity Report");
        text.AppendLine($"Save opened: {SaveOpened} | Schema version: {SchemaVersion} | Content opened: {ContentOpened}");
        text.AppendLine($"Errors: {Errors} | Warnings: {Warnings}");
        text.AppendLine("Read-only diagnostic: no repairs, upgrades, deletes or resets were performed.");
        text.AppendLine();
        if (_findings.Count == 0)
            text.AppendLine("No issues detected by the available checks. This is not a guarantee against all logical errors.");
        else
            foreach (var finding in _findings)
                text.AppendLine($"[{finding.Severity}] {finding.Area}: {finding.Message}");
        return text.ToString();
    }
}

/// <summary>
/// Checks SQLite integrity, internal foreign keys, runtime schema shape and
/// references into the independently published Studio content database.
/// This class is strictly diagnostic: uses read-only connections and NEVER
/// calls SaveGameSchemaManager.EnsureCurrent or performs DDL/DML/repair.
/// </summary>
public static class SaveGameIntegrityChecker
{
    private const int MaxReportedPerCheck = 20;

    // Hardcoded mappings only. Neither SQLite table/column names nor SQL is
    // constructed from savegame values.
    private static readonly (string Table, string Column)[] CharacterReferences =
    {
        ("SavedRelationshipValues", "FromCharacterId"),
        ("SavedRelationshipValues", "ToCharacterId"),
        ("SavedCharacterStatusOverrides", "FromCharacterId"),
        ("SavedCharacterStatusOverrides", "ToCharacterId"),
        ("InteractionHistory", "ActorId"),
        ("InteractionHistory", "PartnerId"),
        ("RelationshipChanges", "FromCharacterId"),
        ("RelationshipChanges", "ToCharacterId"),
        ("SavedInteractionStatusChanges", "FromCharacterId"),
        ("SavedInteractionStatusChanges", "ToCharacterId"),
        ("SavedMemoryOccurrences", "ActorCharacterId"),
        ("SavedMemoryOccurrences", "PlayerCharacterId"),
        ("SavedMemoryAffectedCharacters", "CharacterId"),
        ("SavedMemoryKnowledge", "CharacterId"),
        ("SavedSpecialEncounterCompletions", "InitiatorCharacterId"),
        ("SavedSpecialEncounterCompletions", "RecipientCharacterId")
    };

    public static SaveIntegrityReport Inspect() => Inspect(DatabasePaths.Save, DatabasePaths.Content);

    public static SaveIntegrityReport Inspect(string savePath, string contentPath)
    {
        var result = new SaveIntegrityReport();
        if (string.IsNullOrWhiteSpace(savePath) || !File.Exists(savePath))
        {
            result.Add(SaveIntegritySeverity.Error, "Save database", "Savegame file does not exist; no file was created.");
            return result;
        }

        try
        {
            using var save = OpenReadOnly(savePath);
            result.SaveOpened = true;
            CheckSqliteIntegrity(save, result);
            CheckForeignKeys(save, result);
            var tables = ReadTables(save);
            CheckSchema(save, tables, result);

            if (string.IsNullOrWhiteSpace(contentPath) || !File.Exists(contentPath))
            {
                result.Add(SaveIntegritySeverity.Warning, "Authored content",
                    "Published content database was not found; cross-database reference checks were skipped.");
                return result;
            }

            try
            {
                using var content = OpenReadOnly(contentPath);
                result.ContentOpened = true;
                var contentTables = ReadTables(content);
                CheckContentReferences(save, tables, content, contentTables, result);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                result.Add(SaveIntegritySeverity.Warning, "Authored content",
                    "Could not read the published content database; cross-database checks incomplete: " + ex.Message);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            result.Add(SaveIntegritySeverity.Error, "Save database", "Could not inspect savegame: " + ex.Message);
        }
        return result;
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }

    private static HashSet<string> ReadTables(SqliteConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT GLOB 'sqlite_*'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) tables.Add(reader.GetString(0));
        return tables;
    }

    private static void CheckSqliteIntegrity(SqliteConnection save, SaveIntegrityReport report)
    {
        using var cmd = save.CreateCommand();
        // Running on demand avoids the cost of a full database scan at every launch.
        cmd.CommandText = "PRAGMA integrity_check(100);";
        using var reader = cmd.ExecuteReader();
        int failures = 0;
        while (reader.Read())
        {
            string message = reader.IsDBNull(0) ? "unknown error" : reader.GetString(0);
            if (string.Equals(message, "ok", StringComparison.OrdinalIgnoreCase)) continue;
            failures++;
            if (failures <= MaxReportedPerCheck)
                report.Add(SaveIntegritySeverity.Error, "SQLite integrity", message);
        }
        if (failures > MaxReportedPerCheck)
            report.Add(SaveIntegritySeverity.Error, "SQLite integrity", $"{failures - MaxReportedPerCheck} further errors omitted.");
    }

    private static void CheckForeignKeys(SqliteConnection save, SaveIntegrityReport report)
    {
        using var cmd = save.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        using var reader = cmd.ExecuteReader();
        int failures = 0;
        while (reader.Read())
        {
            failures++;
            if (failures <= MaxReportedPerCheck)
                report.Add(SaveIntegritySeverity.Error, "Foreign keys",
                    $"{reader.GetString(0)} row {reader.GetValue(1)} references missing parent {reader.GetString(2)} (FK {reader.GetValue(3)}).");
        }
        if (failures > MaxReportedPerCheck)
            report.Add(SaveIntegritySeverity.Error, "Foreign keys", $"{failures - MaxReportedPerCheck} further violations omitted.");
    }

    private static void CheckSchema(SqliteConnection save, HashSet<string> tables, SaveIntegrityReport report)
    {
        using (var version = save.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version;";
            report.SchemaVersion = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        if (report.SchemaVersion == 0)
            report.Add(SaveIntegritySeverity.Warning, "Schema version", "Unversioned save; migrate using the game's schema manager before playing.");
        else if (report.SchemaVersion > SaveGameSchemaManager.CurrentVersion || report.SchemaVersion < 0)
            report.Add(SaveIntegritySeverity.Error, "Schema version",
                $"Save version {report.SchemaVersion} is incompatible with this game (supports {SaveGameSchemaManager.CurrentVersion}).");
        else if (report.SchemaVersion < SaveGameSchemaManager.CurrentVersion)
            report.Add(SaveIntegritySeverity.Warning, "Schema version",
                $"Save version {report.SchemaVersion} requires migration to {SaveGameSchemaManager.CurrentVersion}.");

        foreach (var expected in SaveGameSchemaManager.RequiredColumns)
        {
            if (!tables.Contains(expected.Key))
            {
                report.Add(SaveIntegritySeverity.Error, "Save schema", "Missing table: " + expected.Key);
                continue;
            }
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = save.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{expected.Key}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
            foreach (string column in expected.Value)
                if (!columns.Contains(column))
                    report.Add(SaveIntegritySeverity.Error, "Save schema", $"{expected.Key} is missing column {column}.");
        }
    }

    private static void CheckContentReferences(SqliteConnection save, HashSet<string> saveTables,
        SqliteConnection content, HashSet<string> contentTables, SaveIntegrityReport report)
    {
        CheckReferenceGroup(save, saveTables, content, contentTables, report,
            "Characters", "Id", CharacterReferences, "authored character");
        CheckReferenceGroup(save, saveTables, content, contentTables, report,
            "MemoryTypes", "Key", new[] { ("SavedMemoryOccurrences", "MemoryKey") }, "memory type");
        CheckReferenceGroup(save, saveTables, content, contentTables, report,
            "RelationshipAttributes", "Key", new[]
            {
                ("SavedRelationshipValues", "AttributeKey"),
                ("RelationshipChanges", "Attribute")
            }, "relationship attribute");
        CheckReferenceGroup(save, saveTables, content, contentTables, report,
            "RelationshipStatuses", "Key", new[]
            {
                ("SavedCharacterStatusOverrides", "StatusKey")
            }, "relationship status");
        CheckReferenceGroup(save, saveTables, content, contentTables, report,
            "Interactions", "Key", new[] { ("InteractionHistory", "InteractionKey") }, "interaction");
        CheckReferenceGroup(save, saveTables, content, contentTables, report,
            "SpecialEncounters", "EncounterKey", new[]
            {
                ("SavedSpecialEncounterCompletions", "EncounterKey")
            }, "special encounter");
    }

    private static void CheckReferenceGroup(SqliteConnection save, HashSet<string> saveTables,
        SqliteConnection content, HashSet<string> contentTables, SaveIntegrityReport report,
        string contentTable, string contentColumn, (string Table, string Column)[] columns, string label)
    {
        if (!contentTables.Contains(contentTable))
        {
            report.Add(SaveIntegritySeverity.Warning, "Authored content",
                $"Table {contentTable} is absent; {label} reference checks were skipped.");
            return;
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = content.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"{contentColumn}\" FROM \"{contentTable}\"";
            using var rows = cmd.ExecuteReader();
            while (rows.Read())
                if (!rows.IsDBNull(0)) known.Add(rows.GetString(0).Trim());
        }

        foreach (var (table, column) in columns)
        {
            if (!saveTables.Contains(table)) continue; // already reported as missing schema
            using var cmd = save.CreateCommand();
            cmd.CommandText = $"SELECT \"{column}\", COUNT(*) FROM \"{table}\" GROUP BY \"{column}\"";
            using var rows = cmd.ExecuteReader();
            int unseen = 0;
            while (rows.Read())
            {
                string value = rows.IsDBNull(0) ? "" : rows.GetString(0);
                long count = rows.GetInt64(1);
                bool empty = string.IsNullOrWhiteSpace(value);
                if (!empty && known.Contains(value.Trim())) continue;
                unseen++;
                if (unseen > MaxReportedPerCheck) continue;
                report.Add(empty ? SaveIntegritySeverity.Error : SaveIntegritySeverity.Warning,
                    $"{table}.{column}", empty
                        ? $"Blank {label} reference ({count} record(s))."
                        : $"Unknown {label} '{value}' ({count} record(s)); content may have been changed or removed.");
            }
            if (unseen > MaxReportedPerCheck)
                report.Add(SaveIntegritySeverity.Warning, $"{table}.{column}",
                    $"{unseen - MaxReportedPerCheck} more unrecognised values omitted.");
        }
    }
}
