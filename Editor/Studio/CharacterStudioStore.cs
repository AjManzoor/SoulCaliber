using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;

namespace SoulCaliberStudio;

/// <summary>
/// Character, skill and reaction-image authoring using the existing Godot
/// content schema. Does not create/migrate tables or modify the savegame.
/// Modifications use SQLite transactions; backups are not made for routine saves.
/// </summary>
public sealed partial class CharacterStudioStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly DatabaseEditor _backups;

    public sealed record Character(string Id, string Name, string DefaultImage, string WinImage, string LoseImage)
    {
        public override string ToString() => $"{Name}  [{Id}]";
    }

    public sealed record Reaction(string CharacterId, string ReactionKey, string ImagePath)
    {
        public override string ToString() => $"{ReactionKey}    {ImagePath}";
    }

    public CharacterStudioStore(string databasePath, DatabaseEditor backups)
    {
        _backups = backups;
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Master database not found", databasePath);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true
        }.ToString());
        _db.Open();
        using var cmd = Sql("PRAGMA busy_timeout=5000;");
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Sql(string statement, SqliteTransaction? transaction = null) => new(statement, _db, transaction);

    private static void Add(SqliteCommand cmd, string key, object? value) =>
        cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);

    private static string Identity(string? raw, string kind)
    {
        string value = (raw ?? "").Trim().ToUpperInvariant();
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException($"{kind} must contain only A-Z, 0-9 and underscores.");
        return value;
    }

    private static string ResourcePath(string? raw)
    {
        string path = (raw ?? "").Trim().Replace('\\', '/');
        if (path.Length == 0) return "";
        if (!path.StartsWith("res://", StringComparison.Ordinal) || path.Length <= 6)
            throw new InvalidOperationException($"Artwork path must start with res:// — received '{path}'.");
        string relative = path[6..];
        if (relative.StartsWith('/') || relative.Contains(':') ||
            relative.Split('/').Any(s => s is "" or "." or ".."))
            throw new InvalidOperationException($"Unsafe artwork path: '{path}'.");
        return path;
    }

    private static int Stat(int value, string name)
    {
        if (value is < 0 or > 100) throw new InvalidOperationException($"{name} must be between 0 and 100.");
        return value;
    }

    private void Write(Action<SqliteTransaction> change)
    {
        using var tx = _db.BeginTransaction();
        try { change(tx); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    public List<Character> Characters()
    {
        using var cmd = Sql("SELECT Id, Name, DefaultImage, WinImage, LoseImage FROM Characters ORDER BY Name, Id;");
        using var reader = cmd.ExecuteReader();
        var result = new List<Character>();
        while (reader.Read())
            result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));
        return result;
    }

    public List<Reaction> GetReactions(string characterId)
    {
        using var cmd = Sql("SELECT CharacterId, ReactionKey, ImagePath FROM ReactionImages WHERE CharacterId=$id ORDER BY ReactionKey;");
        Add(cmd, "$id", characterId);
        using var reader = cmd.ExecuteReader();
        var result = new List<Reaction>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public void DeleteCharacter(string id)
    {
        id = Identity(id, "Character ID");
        using (var blockers = Sql("""
            SELECT
              (SELECT COUNT(*) FROM Lines WHERE CharacterId=$id OR VisitorId=$id) +
              (SELECT COUNT(*) FROM NodeIntroductions WHERE CharacterId=$id OR VisitorId=$id);
            """))
        {
            Add(blockers, "$id", id);
            long references = (long)blockers.ExecuteScalar()!;
            if (references > 0)
                throw new InvalidOperationException($"Character '{id}' is referenced by {references} interaction/phrase record(s). Reassign or delete those first. Nothing has been deleted.");
        }

        Write(tx =>
        {
            // Explicitly remove authored portrait and skill rows, even though
            // the imported schema also specifies ON DELETE CASCADE for these tables.
            foreach (string table in new[] { "ReactionImages", "CharacterSkillValues", "CharacterTraits", "Personalities" })
            {
                using var child = Sql($"DELETE FROM {table} WHERE CharacterId=$id;", tx);
                Add(child, "$id", id);
                child.ExecuteNonQuery();
            }
            using var cmd = Sql("DELETE FROM Characters WHERE Id=$id;", tx);
            Add(cmd, "$id", id);
            if (cmd.ExecuteNonQuery() != 1) throw new DBConcurrencyException("Character no longer exists.");
        });
    }

    public void SaveReaction(string characterId, string key, string imagePath, bool existing)
    {
        string character = Identity(characterId, "Character ID");
        string reaction = Identity(key, "Reaction key");
        string path = ResourcePath(imagePath);
        if (path.Length == 0) throw new InvalidOperationException("Select an artwork path before saving a reaction.");
        using (var check = Sql("SELECT 1 FROM ReactionTypes WHERE Key=$key LIMIT 1"))
        {
            Add(check, "$key", reaction);
            if (check.ExecuteScalar() is null)
                throw new InvalidOperationException($"Reaction '{reaction}' is not in the catalogue. Add it via Manage reaction types.");
        }
        Write(tx =>
        {
            using var cmd = Sql(existing
                ? "UPDATE ReactionImages SET ImagePath=$path WHERE CharacterId=$char AND ReactionKey=$key;"
                : "INSERT INTO ReactionImages(CharacterId,ReactionKey,ImagePath) VALUES($char,$key,$path);", tx);
            Add(cmd, "$char", character); Add(cmd, "$key", reaction); Add(cmd, "$path", path);
            if (cmd.ExecuteNonQuery() != 1) throw new DBConcurrencyException("Reaction could not be saved. Reload and retry.");
        });
    }

    public void DeleteReaction(string characterId, string reactionKey)
    {
        Write(tx =>
        {
            using var cmd = Sql("DELETE FROM ReactionImages WHERE CharacterId=$char AND ReactionKey=$key;", tx);
            Add(cmd, "$char", characterId); Add(cmd, "$key", reactionKey);
            if (cmd.ExecuteNonQuery() != 1) throw new DBConcurrencyException("Reaction no longer exists.");
        });
    }

    public void Dispose() => _db.Dispose();
}
