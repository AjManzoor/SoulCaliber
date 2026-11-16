using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;

namespace SoulCaliberStudio;

/// <summary>A character's starting status towards another character, not a numeric attribute.</summary>
public sealed record StatusCharacter(string Id, string Name)
{
    public override string ToString() => $"{Name} [{Id}]";
}

/// <summary>One pair viewed from a selected character's perspective.</summary>
public sealed record CharacterStartingStatusPair(
    string OtherCharacterId, string OtherCharacterName,
    string OutgoingStatusKey, string IncomingStatusKey);

public sealed partial class DialogueDesignerStore
{
    public void EnsureCharacterDefaultStatusesSchema()
    {
        if (Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='CharacterDefaultStatuses'"))
            return;

        // Schema changes receive a one-time safety snapshot. Ordinary status edits do not.
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.010_CharacterDefaultStatuses.sql")
            ?? throw new InvalidOperationException("Missing embedded Phase 12B starting-status migration.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var migration = Command(reader.ReadToEnd(), tx)) migration.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", tx);
            using var result = check.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Starting-status migration failed foreign-key checks.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<StatusCharacter> StatusCharacters()
    {
        var result = new List<StatusCharacter>();
        using var cmd = Command("SELECT Id,Name FROM Characters ORDER BY Name COLLATE NOCASE,Id");
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new(r.GetString(0),r.GetString(1)));
        return result;
    }

    public IReadOnlyList<CharacterStartingStatusPair> StartingStatusPairs(string selectedCharacterId)
    {
        var characters = StatusCharacters();
        if (!characters.Any(c => string.Equals(c.Id, selectedCharacterId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an existing character.");

        var outwards = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var inwards = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = Command("""
            SELECT SourceCharacterId, TargetCharacterId, StatusKey
            FROM CharacterDefaultStatuses
            WHERE SourceCharacterId=$id COLLATE NOCASE OR TargetCharacterId=$id COLLATE NOCASE
            """))
        {
            Add(cmd, "$id", selectedCharacterId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string source = r.GetString(0);
                string target = r.GetString(1);
                string status = r.GetString(2);
                if (string.Equals(source, selectedCharacterId, StringComparison.OrdinalIgnoreCase))
                    outwards[target] = status;
                else
                    inwards[source] = status;
            }
        }
        return characters
            .Where(c => !string.Equals(c.Id, selectedCharacterId, StringComparison.OrdinalIgnoreCase))
            .Select(c => new CharacterStartingStatusPair(c.Id, c.Name,
                outwards.GetValueOrDefault(c.Id, "NEUTRAL"),
                inwards.GetValueOrDefault(c.Id, "NEUTRAL")))
            .ToList();
    }

    public void SaveStartingStatusPairs(string selectedCharacterId,
        IReadOnlyList<CharacterStartingStatusPair> pairs)
    {
        var characters = StatusCharacters();
        if (!characters.Any(c => string.Equals(c.Id, selectedCharacterId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected character no longer exists.");

        var otherIds = characters.Where(c => !string.Equals(c.Id, selectedCharacterId, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairIds = pairs.Select(p => p.OtherCharacterId).ToList();
        if (pairIds.Count != otherIds.Count ||
            pairIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != pairIds.Count ||
            pairIds.Any(id => !otherIds.Contains(id)))
            throw new InvalidOperationException("The roster changed while editing. Reopen starting relationships and try again.");

        var activeStatuses = StatusDefinitions(activeOnly: true).Select(s => s.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            if (!activeStatuses.Contains(pair.OutgoingStatusKey) || !activeStatuses.Contains(pair.IncomingStatusKey))
                throw new InvalidOperationException($"One of the selected statuses for {pair.OtherCharacterName} is no longer active.");
        }

        // One atomic write for the entire selected character, both directions.
        // No gameplay state and no per-edit backups are involved.
        using var tx = _db.BeginTransaction();
        try
        {
            using var insert = Command("""
                INSERT INTO CharacterDefaultStatuses(SourceCharacterId,TargetCharacterId,StatusKey)
                VALUES($from,$to,$status)
                ON CONFLICT(SourceCharacterId,TargetCharacterId)
                DO UPDATE SET StatusKey=excluded.StatusKey
                """,tx);
            using var delete = Command("""
                DELETE FROM CharacterDefaultStatuses
                WHERE SourceCharacterId=$from COLLATE NOCASE AND TargetCharacterId=$to COLLATE NOCASE
                """,tx);
            foreach (var pair in pairs)
            {
                SaveDirection(selectedCharacterId, pair.OtherCharacterId, pair.OutgoingStatusKey);
                SaveDirection(pair.OtherCharacterId, selectedCharacterId, pair.IncomingStatusKey);
            }
            tx.Commit();

            void SaveDirection(string from, string to, string status)
            {
                if (string.Equals(status, "NEUTRAL", StringComparison.OrdinalIgnoreCase))
                {
                    delete.Parameters.Clear();
                    Add(delete, "$from", from); Add(delete, "$to", to);
                    delete.ExecuteNonQuery();
                }
                else
                {
                    insert.Parameters.Clear();
                    Add(insert, "$from", from); Add(insert, "$to", to);
                    Add(insert, "$status", status);
                    insert.ExecuteNonQuery();
                }
            }
        }
        catch { tx.Rollback(); throw; }
    }
}
