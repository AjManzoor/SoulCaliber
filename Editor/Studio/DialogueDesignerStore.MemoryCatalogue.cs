using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

/// <summary>
/// Authored memory TYPES only. Runtime occurrences and character knowledge are
/// intentionally separate savegame responsibilities (Phase 13B and later).
/// </summary>
public sealed record MemoryTypeDefinition(
    string Key, string Name, string Description,
    string DefaultSeverity, string DefaultKnowledge, int SortOrder, bool IsActive)
{
    public override string ToString() => $"{Name} [{Key}]" + (IsActive ? "" : " (inactive)");
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureMemoryCatalogueSchema()
    {
        bool types = Exists(_db,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='MemoryTypes'");
        bool relevance = Exists(_db,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='MemoryTypePersonalityRelevance'");
        if (types != relevance)
            throw new InvalidOperationException(
                "Incomplete Phase 13A memory catalogue schema. Restore or repair your master database.");
        if (types) return;

        // One-time schema upgrade only. Routine saves do NOT produce backups.
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.015_MemoryCatalogue.sql")
            ?? throw new InvalidOperationException("Missing embedded Phase 13A memory catalogue migration.");
        using var reader = new StreamReader(stream);
        using var transaction = _db.BeginTransaction();
        try
        {
            using (var command = Command(reader.ReadToEnd(), transaction)) command.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", transaction);
            using var result = check.ExecuteReader();
            if (result.Read())
                throw new InvalidOperationException("Memory catalogue migration failed foreign-key validation.");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public IReadOnlyList<MemoryTypeDefinition> MemoryTypeDefinitions(bool activeOnly = false)
    {
        using var cmd = Command("""
            SELECT Key,Name,Description,DefaultSeverity,DefaultKnowledge,SortOrder,IsActive
            FROM MemoryTypes WHERE ($active=0 OR IsActive=1)
            ORDER BY SortOrder,Name,Key
            """);
        Add(cmd, "$active", activeOnly ? 1 : 0);
        using var reader = cmd.ExecuteReader();
        var results = new List<MemoryTypeDefinition>();
        while (reader.Read())
            results.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6) == 1));
        return results;
    }

    public IReadOnlyList<string> MemoryTypeRelevantTraits(string memoryKey)
    {
        using var cmd = Command("""
            SELECT TraitKey FROM MemoryTypePersonalityRelevance
            WHERE MemoryKey=$key ORDER BY TraitKey
            """);
        Add(cmd, "$key", memoryKey);
        using var reader = cmd.ExecuteReader();
        var results = new List<string>();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    /// <summary>Save a definition and all personality relevance links together.</summary>
    public void SaveMemoryType(MemoryTypeDefinition item, bool isNew, IReadOnlyCollection<string> relevantTraitKeys)
    {
        string key = (item.Key ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, "^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Memory key must start with A-Z and contain only A-Z, 0-9 or _ (max 48).");
        string name = Required(item.Name, "Memory name");
        if (name.Length > 120)
            throw new InvalidOperationException("Memory name must be at most 120 characters.");
        string description = (item.Description ?? "").Trim();
        if (description.Length > 1000)
            throw new InvalidOperationException("Description must be at most 1000 characters.");
        if (item.DefaultSeverity is not ("MAJOR" or "MINOR"))
            throw new InvalidOperationException("Severity must be Major or Minor.");
        if (item.DefaultKnowledge is not ("QUIET" or "ALLIES" or "GLOBAL"))
            throw new InvalidOperationException("Knowledge must be Quiet, Allies or Global.");

        var traitKeys = (relevantTraitKeys ?? Array.Empty<string>())
            .Select(k => (k ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string traitKey in traitKeys)
            if (!Exists(_db, "SELECT 1 FROM PersonalityTraits WHERE Key=$key", ("$key", traitKey)))
                throw new InvalidOperationException($"Unknown personality trait: {traitKey}.");

        Write(tx =>
        {
            using (var cmd = Command(isNew ? """
                INSERT INTO MemoryTypes
                  (Key,Name,Description,DefaultSeverity,DefaultKnowledge,SortOrder,IsActive)
                VALUES ($key,$name,$description,$severity,$knowledge,$sort,$active)
                """ : """
                UPDATE MemoryTypes SET Name=$name,Description=$description,
                  DefaultSeverity=$severity,DefaultKnowledge=$knowledge,
                  SortOrder=$sort,IsActive=$active WHERE Key=$key
                """, tx))
            {
                Add(cmd, "$key", key); Add(cmd, "$name", name);
                Add(cmd, "$description", description);
                Add(cmd, "$severity", item.DefaultSeverity);
                Add(cmd, "$knowledge", item.DefaultKnowledge);
                Add(cmd, "$sort", item.SortOrder);
                Add(cmd, "$active", item.IsActive ? 1 : 0);
                Changed(cmd, !isNew);
            }

            using (var remove = Command(
                "DELETE FROM MemoryTypePersonalityRelevance WHERE MemoryKey=$key", tx))
            {
                Add(remove, "$key", key);
                remove.ExecuteNonQuery();
            }
            foreach (string traitKey in traitKeys)
            {
                using var add = Command("""
                    INSERT INTO MemoryTypePersonalityRelevance(MemoryKey,TraitKey)
                    VALUES ($memory,$trait)
                    """, tx);
                Add(add, "$memory", key); Add(add, "$trait", traitKey);
                Changed(add);
            }
        });
    }

    public void DeleteMemoryType(string memoryKey)
    {
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM MemoryTypes WHERE Key=$key", tx);
            Add(cmd, "$key", memoryKey);
            Changed(cmd, true);
        });
    }
}
