using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;

namespace SoulCaliberStudio;

/// <summary>
/// Character, personality and reaction-image authoring using the existing Godot
/// content schema. Does not create/migrate tables or modify the savegame.
/// Every modification is backed up before a single SQLite transaction.
/// </summary>
public sealed class CharacterStudioStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly DatabaseEditor _backups;

    public sealed record Character(string Id, string Name, string DefaultImage, string WinImage, string LoseImage)
    {
        public override string ToString() => $"{Name}  [{Id}]";
    }

    public sealed record Personality(int Persuasion, int Intimidation, int Deception, int Charm, int Insight, int Resolve)
    {
        public static Personality Default => new(50, 50, 50, 50, 50, 50);
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
        _backups.Backup(); // Uses SQLite backup API so WAL changes are included.
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

    public Personality GetPersonality(string characterId)
    {
        using var cmd = Sql("""
            SELECT Persuasion, Intimidation, Deception, Charm, Insight, Resolve
            FROM Personalities WHERE CharacterId=$id;
            """);
        Add(cmd, "$id", characterId);
        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5))
            : Personality.Default;
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

    public void SaveCharacter(Character character, Personality personality, string? existingId)
    {
        string id = Identity(character.Id, "Character ID");
        string name = character.Name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Character name is required.");
        if (existingId is not null && !string.Equals(existingId, id, StringComparison.Ordinal))
            throw new InvalidOperationException("An existing character ID cannot be changed. Create another character instead.");
        string neutral = ResourcePath(character.DefaultImage);
        string success = ResourcePath(character.WinImage);
        string failure = ResourcePath(character.LoseImage);
        var stats = new[] { personality.Persuasion, personality.Intimidation, personality.Deception,
            personality.Charm, personality.Insight, personality.Resolve };
        var names = new[] { "Persuasion", "Intimidation", "Deception", "Charm", "Insight", "Resolve" };
        for (int i = 0; i < stats.Length; i++) Stat(stats[i], names[i]);

        Write(tx =>
        {
            using (var cmd = Sql(existingId is null
                ? "INSERT INTO Characters(Id,Name,DefaultImage,WinImage,LoseImage) VALUES($id,$name,$neutral,$win,$lose);"
                : "UPDATE Characters SET Name=$name,DefaultImage=$neutral,WinImage=$win,LoseImage=$lose WHERE Id=$id;", tx))
            {
                Add(cmd, "$id", id); Add(cmd, "$name", name);
                Add(cmd, "$neutral", neutral); Add(cmd, "$win", success); Add(cmd, "$lose", failure);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new DBConcurrencyException("Character could not be saved. Reload the roster and retry.");
            }
            using var persona = Sql("""
                INSERT INTO Personalities(CharacterId,Persuasion,Intimidation,Deception,Charm,Insight,Resolve)
                VALUES($id,$p,$i,$d,$c,$ins,$r)
                ON CONFLICT(CharacterId) DO UPDATE SET
                  Persuasion=excluded.Persuasion, Intimidation=excluded.Intimidation,
                  Deception=excluded.Deception, Charm=excluded.Charm,
                  Insight=excluded.Insight, Resolve=excluded.Resolve;
                """, tx);
            Add(persona, "$id", id); Add(persona, "$p", personality.Persuasion);
            Add(persona, "$i", personality.Intimidation); Add(persona, "$d", personality.Deception);
            Add(persona, "$c", personality.Charm); Add(persona, "$ins", personality.Insight);
            Add(persona, "$r", personality.Resolve);
            persona.ExecuteNonQuery();
        });
    }

    public void DeleteCharacter(string id)
    {
        id = Identity(id, "Character ID");
        using (var blockers = Sql("""
            SELECT
              (SELECT COUNT(*) FROM Interactions WHERE HostCharacterId=$id) +
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
            // Explicitly remove authored portrait and personality rows, even though
            // the imported schema also specifies ON DELETE CASCADE for these tables.
            foreach (string table in new[] { "ReactionImages", "Personalities" })
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
