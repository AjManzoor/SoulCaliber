using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;

namespace SoulCaliberStudio;

// Purpose-built dialogue CRUD layer; preserves the existing SQLite schema and keys.
public sealed partial class DialogueDesignerStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly DatabaseEditor _backupSource;

    public DialogueDesignerStore(string databasePath, DatabaseEditor backupSource)
    {
        _backupSource = backupSource;
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Content database not found", databasePath);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true
        }.ToString());
        _db.Open();
        using var cmd = Command("PRAGMA busy_timeout=5000;");
        cmd.ExecuteNonQuery();
        EnsureIntroductionSchema();
    }

    private void EnsureIntroductionSchema()
    {
        using var table = Command("SELECT 1 FROM sqlite_master WHERE type='table' AND name='NodeIntroductions'");
        if (table.ExecuteScalar() is not null) return;

        // The editor migrates its AUTHORING database, never the read-only published snapshot.
        _backupSource.Backup();
        using var tx = _db.BeginTransaction();
        try
        {
            using var create = Command("""
                CREATE TABLE NodeIntroductions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NodeId INTEGER NOT NULL REFERENCES Nodes(Id) ON DELETE CASCADE,
                    CharacterId TEXT NOT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
                    VisitorId TEXT NULL REFERENCES Characters(Id) ON DELETE RESTRICT,
                    Text TEXT NOT NULL CHECK(length(trim(Text)) > 0)
                );
                CREATE UNIQUE INDEX UX_NodeIntro_Character
                    ON NodeIntroductions(NodeId, CharacterId COLLATE NOCASE) WHERE VisitorId IS NULL;
                CREATE UNIQUE INDEX UX_NodeIntro_Pair
                    ON NodeIntroductions(NodeId, CharacterId COLLATE NOCASE, VisitorId COLLATE NOCASE) WHERE VisitorId IS NOT NULL;
                """, tx);
            create.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public sealed record Interaction(int Id, string Key, string Title, string HostCharacterId, string StartNodeKey)
    {
        public override string ToString() => $"{Title}  [{Key}]";
    }
    public sealed record Node(int Id, int InteractionId, string Key, int Round, int SortOrder, string HostIntroduction)
    {
        public override string ToString() => $"{Round}. {Key}";
    }
    public sealed record Option(int Id, int NodeId, string Key, string Label, int SortOrder, int BaseSuccessChance,
        string? SuccessNextNodeKey, string? FailureNextNodeKey, string DefaultPlayerLine,
        string DefaultHostSuccessLine, string DefaultHostFailureLine, string SuccessTargetReaction, string FailureTargetReaction)
    {
        public override string ToString() => $"{SortOrder + 1}. {Label}  [{Key}]";
    }
    public sealed record Introduction(int Id, int NodeId, string CharacterId, string? VisitorId, string Text)
    {
        public override string ToString() => $"{CharacterId}" +
            (VisitorId is null ? " (any visitor)" : $" with {VisitorId}") + $": {Text}";
    }
    public sealed record IntroPreview(string Text, string Source);

    public sealed record Line(int Id, int OptionId, string CharacterId, string? VisitorId, string? Outcome, string Text, string ImagePath)
    {
        public override string ToString() => $"{CharacterId}{(string.IsNullOrWhiteSpace(VisitorId) ? "" : " with " + VisitorId)}" +
            $" [{(string.IsNullOrWhiteSpace(Outcome) ? "ANY" : Outcome)}]: {Text}";
    }

    private SqliteCommand Command(string sql, SqliteTransaction? tx = null) => new(sql, _db, tx);
    private static void Add(SqliteCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{name} is required.");
        return value.Trim();
    }
    private static string Text(string? value) => value ?? "";
    private static bool ValidKey(string value) => value.All(c => char.IsLetterOrDigit(c) || c == '_');
    private static string Key(string? value, string field)
    {
        string key = Required(value, field);
        if (!ValidKey(key)) throw new InvalidOperationException($"{field} may only contain letters, numbers and underscores.");
        return key;
    }
    private static void Changed(SqliteCommand cmd, bool update = false)
    {
        if (cmd.ExecuteNonQuery() != 1) throw new DBConcurrencyException(update ? "Record changed by another editor. Reload and retry." : "Database update failed.");
    }
    private void Write(Action<SqliteTransaction> work)
    {
        // Snapshot before writes, including any WAL data; the backup is independent of this transaction.
        _backupSource.Backup();
        using var tx = _db.BeginTransaction();
        try { work(tx); tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }
    private static bool Exists(SqliteConnection db, string sql, params (string Key, object? Value)[] args)
    {
        using var cmd = new SqliteCommand(sql, db);
        foreach (var (key, value) in args) Add(cmd, key, value);
        return cmd.ExecuteScalar() != null;
    }

    public List<string> Characters()
    {
        using var cmd = Command("SELECT Id FROM Characters ORDER BY Name, Id");
        using var r = cmd.ExecuteReader();
        var result = new List<string>();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }
    public List<Interaction> Interactions()
    {
        using var cmd = Command("SELECT Id, Key, Title, HostCharacterId, StartNodeKey FROM Interactions ORDER BY Title, Id");
        using var r = cmd.ExecuteReader();
        var items = new List<Interaction>();
        while (r.Read()) items.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return items;
    }
    public List<Node> Nodes(int interactionId)
    {
        using var cmd = Command("SELECT Id, InteractionId, Key, Round, SortOrder, HostIntroduction FROM Nodes WHERE InteractionId=$id ORDER BY SortOrder, Id");
        Add(cmd, "$id", interactionId);
        using var r = cmd.ExecuteReader();
        var items = new List<Node>();
        while (r.Read()) items.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5)));
        return items;
    }
    public List<Introduction> Introductions(int nodeId)
    {
        using var cmd = Command("SELECT Id, NodeId, CharacterId, VisitorId, Text FROM NodeIntroductions WHERE NodeId=$node ORDER BY CharacterId, VisitorId, Id");
        Add(cmd, "$node", nodeId);
        using var r = cmd.ExecuteReader();
        var results = new List<Introduction>();
        while (r.Read()) results.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4)));
        return results;
    }

    public int SaveIntroduction(Introduction intro)
    {
        string speaker = Required(intro.CharacterId, "Speaking character");
        string? visitor = NullIfBlank(intro.VisitorId);
        string value = Required(intro.Text, "Introduction text");
        if (!Exists(_db, "SELECT 1 FROM Nodes WHERE Id=$id", ("$id", intro.NodeId)))
            throw new InvalidOperationException("Select an existing dialogue node first.");
        if (!Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", speaker)))
            throw new InvalidOperationException("Speaking character is not in the roster.");
        if (visitor is not null && !Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", visitor)))
            throw new InvalidOperationException("Other character is not in the roster.");
        if (visitor is not null && string.Equals(speaker, visitor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An introduction cannot address the same character as its speaker.");
        // Match the existing phrase editor: identities are immutable once created.
        if (intro.Id > 0 && !Exists(_db, """
            SELECT 1 FROM NodeIntroductions WHERE Id=$id AND NodeId=$node AND CharacterId=$speaker
              AND (VisitorId=$visitor OR (VisitorId IS NULL AND $visitor IS NULL))
            """, ("$id", intro.Id), ("$node", intro.NodeId), ("$speaker", speaker), ("$visitor", visitor)))
            throw new InvalidOperationException("The introduction's speaker and other character cannot change. Create a new introduction instead.");
        if (intro.Id == 0 && Exists(_db, """
            SELECT 1 FROM NodeIntroductions WHERE NodeId=$node AND CharacterId=$speaker
              AND (VisitorId=$visitor OR (VisitorId IS NULL AND $visitor IS NULL))
            """, ("$node", intro.NodeId), ("$speaker", speaker), ("$visitor", visitor)))
            throw new InvalidOperationException("An introduction already exists for this dialogue round, speaker and other character.");
        int id = intro.Id;
        Write(tx =>
        {
            using var cmd = Command(intro.Id == 0
                ? "INSERT INTO NodeIntroductions(NodeId,CharacterId,VisitorId,Text) VALUES($node,$speaker,$visitor,$text)"
                : "UPDATE NodeIntroductions SET Text=$text WHERE Id=$id AND NodeId=$node", tx);
            Add(cmd, "$id", intro.Id); Add(cmd, "$node", intro.NodeId);
            Add(cmd, "$speaker", speaker); Add(cmd, "$visitor", visitor); Add(cmd, "$text", value);
            Changed(cmd, intro.Id != 0);
            if (intro.Id == 0)
            {
                using var last = Command("SELECT last_insert_rowid()", tx);
                id = Convert.ToInt32((long)last.ExecuteScalar()!);
            }
        });
        return id;
    }

    public void DeleteIntroduction(int id) => Delete("DELETE FROM NodeIntroductions WHERE Id=$id", id);

    public IntroPreview ResolveIntroduction(int nodeId, string speakerId, string visitorId)
    {
        var node = Introductions(nodeId);
        var selected = node.Where(x => string.Equals(x.CharacterId, speakerId, StringComparison.OrdinalIgnoreCase) &&
            (x.VisitorId is null || string.Equals(x.VisitorId, visitorId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.VisitorId is not null).ThenBy(x => x.Id).FirstOrDefault();
        if (selected is not null)
            return new IntroPreview(selected.Text, selected.VisitorId is null ? "Character-specific" : "Pair-specific");
        using var cmd = Command("SELECT HostIntroduction FROM Nodes WHERE Id=$node");
        Add(cmd, "$node", nodeId);
        return new IntroPreview((string?)cmd.ExecuteScalar() ?? "", "Generic fallback");
    }

    public string CharacterName(string id)
    {
        using var cmd = Command("SELECT Name FROM Characters WHERE Id=$id");
        Add(cmd, "$id", id);
        return (string?)cmd.ExecuteScalar() ?? id;
    }

    // Phase 5 priority: pair-specific beats speaker-only; within each scope,
    // outcome-specific beats ANY. Generic option fields are the final fallback.
    public (string Text, string Source) ResolveLine(int optionId, string characterId, string otherId,
        bool succeeded, string fallback)
    {
        var outcome = succeeded ? "SUCCESS" : "FAILURE";
        var selected = Lines(optionId).Where(l =>
                string.Equals(l.CharacterId, characterId, StringComparison.OrdinalIgnoreCase) &&
                (l.VisitorId is null || string.Equals(l.VisitorId, otherId, StringComparison.OrdinalIgnoreCase)) &&
                (l.Outcome is null || string.Equals(l.Outcome, outcome, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(l => string.Equals(l.VisitorId, otherId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(l => string.Equals(l.Outcome, outcome, StringComparison.OrdinalIgnoreCase))
            .ThenBy(l => l.Id).FirstOrDefault();
        return selected is null ? (fallback, "Generic fallback") :
            (selected.Text, selected.VisitorId is not null ? "Character/pair phrase" : "Character phrase");
    }

    public List<Option> Options(int nodeId)
    {
        using var cmd = Command("""
            SELECT Id, NodeId, Key, Label, SortOrder, BaseSuccessChance, SuccessNextNodeKey, FailureNextNodeKey,
                   DefaultPlayerLine, DefaultHostSuccessLine, DefaultHostFailureLine, SuccessTargetReaction, FailureTargetReaction
            FROM Options WHERE NodeId=$id ORDER BY SortOrder, Id
            """);
        Add(cmd, "$id", nodeId);
        using var r = cmd.ExecuteReader();
        var items = new List<Option>();
        while (r.Read()) items.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5),
            r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7),
            r.GetString(8), r.GetString(9), r.GetString(10), r.GetString(11), r.GetString(12)));
        return items;
    }
    public List<Line> Lines(int optionId)
    {
        using var cmd = Command("SELECT Id, OptionId, CharacterId, VisitorId, Outcome, Text, ImagePath FROM Lines WHERE OptionId=$id ORDER BY CharacterId, VisitorId, Outcome, Id");
        Add(cmd, "$id", optionId);
        using var r = cmd.ExecuteReader();
        var items = new List<Line>();
        while (r.Read()) items.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6)));
        return items;
    }

    public int SaveInteraction(Interaction item)
    {
        string key = Key(item.Key, "Interaction key");
        string title = Required(item.Title, "Interaction title");
        string host = Required(item.HostCharacterId, "Host character");
        string start = Text(item.StartNodeKey).Trim();
        if (start.Length > 0) Key(start, "Starting node");
        int id = item.Id;
        if (item.Id > 0 && !Exists(_db, "SELECT 1 FROM Interactions WHERE Id=$id AND Key=$key", ("$id", item.Id), ("$key", key)))
            throw new InvalidOperationException("Existing interaction keys are immutable. Reload and edit only the other fields.");
        if (start.Length > 0 && item.Id > 0 && !Exists(_db, "SELECT 1 FROM Nodes WHERE InteractionId=$id AND Key=$key", ("$id", item.Id), ("$key", start)))
            throw new InvalidOperationException("Starting node does not exist in this interaction.");
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? "INSERT INTO Interactions(Key, Title, HostCharacterId, StartNodeKey) VALUES($key,$title,$host,$start)"
                : "UPDATE Interactions SET Title=$title, HostCharacterId=$host, StartNodeKey=$start WHERE Id=$id", tx);
            Add(cmd, "$key", key); Add(cmd, "$title", title); Add(cmd, "$host", host); Add(cmd, "$start", start); Add(cmd, "$id", item.Id);
            Changed(cmd, item.Id != 0);
            if (item.Id == 0) { using var last = Command("SELECT last_insert_rowid()", tx); id = Convert.ToInt32((long)last.ExecuteScalar()!); }
        });
        return id;
    }

    public int SaveNode(Node item)
    {
        string key = Key(item.Key, "Node key");
        if (item.Round < 1 || item.SortOrder < 0) throw new InvalidOperationException("Round must be 1 or higher; order must be zero or higher.");
        int id = item.Id;
        if (item.Id > 0 && !Exists(_db, "SELECT 1 FROM Nodes WHERE Id=$id AND InteractionId=$parent AND Key=$key", ("$id", item.Id), ("$parent", item.InteractionId), ("$key", key)))
            throw new InvalidOperationException("Existing node keys and parents cannot change.");
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? "INSERT INTO Nodes(InteractionId, Key, Round, SortOrder, HostIntroduction) VALUES($parent,$key,$round,$sort,$intro)"
                : "UPDATE Nodes SET Round=$round, SortOrder=$sort, HostIntroduction=$intro WHERE Id=$id", tx);
            Add(cmd, "$parent", item.InteractionId); Add(cmd, "$key", key); Add(cmd, "$round", item.Round);
            Add(cmd, "$sort", item.SortOrder); Add(cmd, "$intro", Text(item.HostIntroduction)); Add(cmd, "$id", item.Id);
            Changed(cmd, item.Id != 0);
            if (item.Id == 0) { using var last = Command("SELECT last_insert_rowid()", tx); id = Convert.ToInt32((long)last.ExecuteScalar()!); }
        });
        return id;
    }

    public int SaveOption(Option item, int interactionId)
    {
        string key = Key(item.Key, "Option key");
        if (string.IsNullOrWhiteSpace(item.Label)) throw new InvalidOperationException("Choice label is required.");
        if (item.BaseSuccessChance < 0 || item.BaseSuccessChance > 100) throw new InvalidOperationException("Base chance must be 0–100.");
        if (item.SortOrder < 0) throw new InvalidOperationException("Sort order must be zero or higher.");
        var success = NullIfBlank(item.SuccessNextNodeKey);
        var failure = NullIfBlank(item.FailureNextNodeKey);
        foreach (var next in new[] { success, failure })
        {
            if (next is not null && !Exists(_db, "SELECT 1 FROM Nodes WHERE InteractionId=$id AND Key=$key", ("$id", interactionId), ("$key", next)))
                throw new InvalidOperationException($"Next dialogue node '{next}' does not exist in this interaction.");
        }
        int id = item.Id;
        if (item.Id > 0 && !Exists(_db, "SELECT 1 FROM Options WHERE Id=$id AND NodeId=$node AND Key=$key", ("$id", item.Id), ("$node", item.NodeId), ("$key", key)))
            throw new InvalidOperationException("Existing choice keys and parent nodes cannot change.");
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? """
                  INSERT INTO Options(NodeId, Key, Label, SortOrder, BaseSuccessChance, SuccessNextNodeKey, FailureNextNodeKey,
                         DefaultPlayerLine, DefaultHostSuccessLine, DefaultHostFailureLine, SuccessTargetReaction, FailureTargetReaction)
                  VALUES($node,$key,$label,$sort,$chance,$success,$failure,$player,$hostSuccess,$hostFailure,$reactionSuccess,$reactionFailure)
                  """
                : """
                  UPDATE Options SET Label=$label, SortOrder=$sort, BaseSuccessChance=$chance, SuccessNextNodeKey=$success,
                    FailureNextNodeKey=$failure, DefaultPlayerLine=$player, DefaultHostSuccessLine=$hostSuccess,
                    DefaultHostFailureLine=$hostFailure, SuccessTargetReaction=$reactionSuccess, FailureTargetReaction=$reactionFailure
                  WHERE Id=$id
                  """, tx);
            Add(cmd, "$node", item.NodeId); Add(cmd, "$key", key); Add(cmd, "$id", item.Id);
            Add(cmd, "$label", item.Label.Trim()); Add(cmd, "$sort", item.SortOrder); Add(cmd, "$chance", item.BaseSuccessChance);
            Add(cmd, "$success", success); Add(cmd, "$failure", failure);
            Add(cmd, "$player", Text(item.DefaultPlayerLine)); Add(cmd, "$hostSuccess", Text(item.DefaultHostSuccessLine));
            Add(cmd, "$hostFailure", Text(item.DefaultHostFailureLine));
            Add(cmd, "$reactionSuccess", Required(item.SuccessTargetReaction, "Success target reaction").ToUpperInvariant());
            Add(cmd, "$reactionFailure", Required(item.FailureTargetReaction, "Failure target reaction").ToUpperInvariant());
            Changed(cmd, item.Id != 0);
            if (item.Id == 0) { using var last = Command("SELECT last_insert_rowid()", tx); id = Convert.ToInt32((long)last.ExecuteScalar()!); }
        });
        return id;
    }

    public int SaveLine(Line item)
    {
        string character = Required(item.CharacterId, "Speaking character");
        string? visitor = NullIfBlank(item.VisitorId);
        string? outcome = NullIfBlank(item.Outcome)?.ToUpperInvariant();
        if (outcome is not null && outcome is not ("SUCCESS" or "FAILURE"))
            throw new InvalidOperationException("Outcome must be ANY, SUCCESS or FAILURE.");
        // A phrase's identity must remain fixed once created. Changing the speaker on
        // an existing row would silently replace Nobunogi's dialogue with Youri's.
        // Users must create a separate row via New phrase for another speaker/pair/outcome.
        if (item.Id > 0 && !Exists(_db, """
            SELECT 1 FROM Lines
            WHERE Id=$id AND OptionId=$option AND CharacterId=$character
              AND (VisitorId=$visitor OR (VisitorId IS NULL AND $visitor IS NULL))
              AND (Outcome=$outcome OR (Outcome IS NULL AND $outcome IS NULL))
            """, ("$id", item.Id), ("$option", item.OptionId),
            ("$character", character), ("$visitor", visitor), ("$outcome", outcome)))
            throw new InvalidOperationException(
                "An existing phrase's speaker, other character and outcome cannot be changed. Click 'New phrase' to add another character-specific response.");

        if (item.Id == 0 && Exists(_db, """
            SELECT 1 FROM Lines
            WHERE OptionId=$option AND CharacterId=$character
              AND (VisitorId=$visitor OR (VisitorId IS NULL AND $visitor IS NULL))
              AND (Outcome=$outcome OR (Outcome IS NULL AND $outcome IS NULL))
            """, ("$option", item.OptionId), ("$character", character),
            ("$visitor", visitor), ("$outcome", outcome)))
            throw new InvalidOperationException(
                "A phrase already exists for this choice, speaker, other character and outcome. Select it in the list to edit its text.");

        int id = item.Id;
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? "INSERT INTO Lines(OptionId, CharacterId, VisitorId, Outcome, Text, ImagePath) VALUES($option,$character,$visitor,$outcome,$text,$image)"
                : "UPDATE Lines SET CharacterId=$character, VisitorId=$visitor, Outcome=$outcome, Text=$text, ImagePath=$image WHERE Id=$id AND OptionId=$option", tx);
            Add(cmd, "$id", item.Id); Add(cmd, "$option", item.OptionId); Add(cmd, "$character", character);
            Add(cmd, "$visitor", visitor); Add(cmd, "$outcome", outcome); Add(cmd, "$text", Text(item.Text)); Add(cmd, "$image", Text(item.ImagePath));
            Changed(cmd, item.Id != 0);
            if (item.Id == 0) { using var last = Command("SELECT last_insert_rowid()", tx); id = Convert.ToInt32((long)last.ExecuteScalar()!); }
        });
        return id;
    }

    public void DeleteInteraction(int id)
    {
        if (Exists(_db, "SELECT 1 FROM Nodes WHERE InteractionId=$id", ("$id", id)))
            throw new InvalidOperationException("Delete this interaction's nodes first.");
        Delete("DELETE FROM Interactions WHERE Id=$id", id);
    }
    public void DeleteNode(int id, int interactionId, string key)
    {
        if (Exists(_db, "SELECT 1 FROM Options WHERE NodeId=$id", ("$id", id)))
            throw new InvalidOperationException("Delete this node's choices first.");
        if (Exists(_db, "SELECT 1 FROM NodeIntroductions WHERE NodeId=$id", ("$id", id)))
            throw new InvalidOperationException("Delete this node's character introductions first.");
        if (Exists(_db, "SELECT 1 FROM Interactions WHERE Id=$id AND StartNodeKey=$key", ("$id", interactionId), ("$key", key)))
            throw new InvalidOperationException("Set a different starting node first.");
        if (Exists(_db, """
            SELECT 1 FROM Options o JOIN Nodes n ON n.Id=o.NodeId
            WHERE n.InteractionId=$id AND (o.SuccessNextNodeKey=$key OR o.FailureNextNodeKey=$key)
            """, ("$id", interactionId), ("$key", key)))
            throw new InvalidOperationException("Another choice branches to this node; update its next-node keys first.");
        Delete("DELETE FROM Nodes WHERE Id=$id", id);
    }
    public void DeleteOption(int id)
    {
        foreach (var table in new[] { "Lines", "Requirements", "ChanceModifiers", "Effects", "SkillChecks" })
            if (Exists(_db, $"SELECT 1 FROM {table} WHERE OptionId=$id", ("$id", id)))
                throw new InvalidOperationException($"This choice has dependent {table}. Remove those records first (Advanced tables).");
        Delete("DELETE FROM Options WHERE Id=$id", id);
    }
    public void DeleteLine(int id) => Delete("DELETE FROM Lines WHERE Id=$id", id);
    private void Delete(string sql, int id) => Write(tx =>
    {
        using var cmd = Command(sql, tx);
        Add(cmd, "$id", id);
        Changed(cmd, true);
    });

    public string ValidateInteraction(int id)
    {
        var issues = new List<string>();
        using var cmd = Command("SELECT Key, StartNodeKey FROM Interactions WHERE Id=$id");
        Add(cmd, "$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return "Interaction no longer exists.";
        string start = r.GetString(1);
        r.Close();
        var nodes = Nodes(id);
        var nodeKeys = nodes.Select(n => n.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!nodeKeys.Contains(start)) issues.Add($"Starting node '{start}' is missing.");
        foreach (var node in nodes)
        {
            var options = Options(node.Id);
            if (options.Count == 0) issues.Add($"Node {node.Key} has no choices.");
            foreach (var option in options)
            {
                foreach (string? target in new[] { option.SuccessNextNodeKey, option.FailureNextNodeKey })
                    if (!string.IsNullOrWhiteSpace(target) && !nodeKeys.Contains(target))
                        issues.Add($"{node.Key}/{option.Key}: missing next node '{target}'.");
            }
        }
        return issues.Count == 0 ? "No structural dialogue issues found." : string.Join(Environment.NewLine, issues);
    }
    public void Dispose() => _db.Dispose();
}
