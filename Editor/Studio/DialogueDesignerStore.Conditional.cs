using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;
using System.IO;

namespace SoulCaliberStudio;

public sealed partial class DialogueDesignerStore
{
    public sealed record ConditionalVariantRow(int Id, int? NodeId, int? OptionId,
        string? CharacterId, string? VisitorId, string? Outcome, int Priority,
        string Text, string ImagePath, IReadOnlyList<DialogueClause> Conditions)
    {
        public override string ToString() =>
            $"{(NodeId.HasValue ? "INTRO" : "REPLY")} {CharacterId ?? "ANY SPEAKER"}" +
            (VisitorId is null ? " (any counterpart)" : " with " + VisitorId) +
            (Outcome is null ? "" : " " + Outcome) + $"  [P{Priority}] {Text}";
    }

    private void EnsureConditionalDialogueSchema()
    {
        using var check = Command("SELECT 1 FROM sqlite_master WHERE type='table' AND name='ConditionalDialogueVariants'");
        if (check.ExecuteScalar() is not null)
        {
            using var other = Command("SELECT 1 FROM sqlite_master WHERE type='table' AND name='ConditionalDialogueConditions'");
            if (other.ExecuteScalar() is null)
                throw new InvalidOperationException("Conditional dialogue schema is incomplete. Restore from a backup before opening the editor.");
            return;
        }
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.003_ConditionalDialogue.sql")
            ?? throw new InvalidOperationException("Embedded migration 003_ConditionalDialogue.sql is missing.");
        using var sr = new StreamReader(stream);
        using var transaction = _db.BeginTransaction();
        try
        {
            using var command = Command(sr.ReadToEnd(), transaction);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private void EnsureUniversalConditionalDialogueSchema()
    {
        // SQLite cannot DROP NOT NULL from a column. Rebuild only once, preserving IDs,
        // clauses, references and all existing dialogue. No gameplay/savegame tables change.
        using var info = Command("PRAGMA table_info(ConditionalDialogueVariants)");
        bool found = false;
        bool required = false;
        using (var reader = info.ExecuteReader())
        {
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(1), "CharacterId", StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                required = reader.GetInt32(3) != 0;
                break;
            }
        }
        if (!found) throw new InvalidOperationException("Conditional dialogue table is missing CharacterId.");
        if (!required) return; // Already upgraded.

        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.004_UniversalConditionalDialogue.sql")
            ?? throw new InvalidOperationException("Embedded migration 004_UniversalConditionalDialogue.sql is missing.");
        using var sr = new StreamReader(stream);

        // Disabling foreign keys must happen before BEGIN. Verify references before COMMIT.
        // There is no other writer on this authoring connection during startup.
        using (var disable = Command("PRAGMA foreign_keys=OFF;")) disable.ExecuteNonQuery();
        try
        {
            using var transaction = _db.BeginTransaction();
            try
            {
                using (var command = Command(sr.ReadToEnd(), transaction)) command.ExecuteNonQuery();
                using (var foreignKeys = Command("PRAGMA foreign_key_check;", transaction))
                using (var violations = foreignKeys.ExecuteReader())
                    if (violations.Read())
                        throw new InvalidOperationException("Foreign-key check failed during universal dialogue migration.");
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }
        finally
        {
            using var enable = Command("PRAGMA foreign_keys=ON;");
            enable.ExecuteNonQuery();
        }
    }

    public List<ConditionalVariantRow> ConditionalVariants(int? nodeId, int? optionId)
    {
        if ((nodeId is null) == (optionId is null))
            throw new InvalidOperationException("Supply exactly one dialogue node or option ID.");
        using var cmd = Command(@"SELECT Id, NodeId, OptionId, CharacterId, VisitorId, Outcome, Priority, Text, ImagePath
            FROM ConditionalDialogueVariants
            WHERE ($node IS NOT NULL AND NodeId=$node) OR ($option IS NOT NULL AND OptionId=$option)
            ORDER BY CharacterId, VisitorId, Outcome, Priority DESC, Id");
        Add(cmd,"$node",nodeId); Add(cmd,"$option",optionId);
        var rows = new List<ConditionalVariantRow>();
        using (var reader = cmd.ExecuteReader())
        {
            while(reader.Read()) rows.Add(new ConditionalVariantRow(reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt32(6),
                reader.GetString(7), reader.GetString(8), Array.Empty<DialogueClause>()));
        }
        return rows.Select(r => r with { Conditions = ConditionalConditions(r.Id) }).ToList();
    }

    private List<DialogueClause> ConditionalConditions(int id)
    {
        using var command = Command(@"SELECT Direction, Attribute, Minimum, Maximum
            FROM ConditionalDialogueConditions WHERE VariantId=$id ORDER BY Id");
        Add(command,"$id",id);
        using var reader = command.ExecuteReader();
        var result = new List<DialogueClause>();
        while(reader.Read()) result.Add(new DialogueClause(reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        return result;
    }

    public int SaveConditionalVariant(ConditionalVariantRow row)
    {
        if ((row.NodeId is null) == (row.OptionId is null))
            throw new InvalidOperationException("Select an introduction node or an option.");
        string? character = NullIfBlank(row.CharacterId); // NULL = any speaker
        string? other = NullIfBlank(row.VisitorId);
        string? outcome = NullIfBlank(row.Outcome)?.ToUpperInvariant();
        if (row.NodeId is not null && outcome is not null ||
            outcome is not null && outcome is not ("SUCCESS" or "FAILURE"))
            throw new InvalidOperationException("Outcome can only be SUCCESS/FAILURE/ANY for a reply.");
        if (row.Priority is < 0 or > 1000) throw new InvalidOperationException("Priority must be 0–1000.");
        string value = Required(row.Text, "Conditional dialogue text");
        string path = (row.ImagePath ?? "").Trim();
        if (row.NodeId is not null && path.Length > 0)
            throw new InvalidOperationException("Introductions cannot have a portrait override.");
        if (path.Length > 0 && (!path.StartsWith("res://", StringComparison.Ordinal) || path.Contains("..")))
            throw new InvalidOperationException("Image path must be a safe res:// path.");
        if (character is not null && !Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", character)) ||
            other is not null && !Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", other)))
            throw new InvalidOperationException("Select characters that exist in the roster.");
        if (character is not null && other is not null && string.Equals(character, other, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A character cannot speak to themselves.");
        if (row.NodeId is int node && !Exists(_db, "SELECT 1 FROM Nodes WHERE Id=$id", ("$id", node)) ||
            row.OptionId is int option && !Exists(_db, "SELECT 1 FROM Options WHERE Id=$id", ("$id", option)))
            throw new InvalidOperationException("The selected node or choice no longer exists.");
        if (row.Conditions.Count == 0) throw new InvalidOperationException("A conditional variant needs at least one condition.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var condition in row.Conditions)
        {
            string direction = CheckDirection(condition.Direction);
            string attribute = CheckAttribute(condition.Attribute);
            if (!seen.Add(direction + ":" + attribute))
                throw new InvalidOperationException("Combine conditions on the same attribute into one range.");
            if (condition.Minimum is null && condition.Maximum is null)
                throw new InvalidOperationException("Each condition needs a minimum or maximum.");
            var definition = GetAttribute(attribute);
            int minimum = definition.Minimum;
            int maximum = definition.Maximum;
            if (condition.Minimum is int lo && (lo < minimum || lo > maximum) ||
                condition.Maximum is int hi && (hi < minimum || hi > maximum) ||
                condition.Minimum is int l && condition.Maximum is int h && l > h)
                throw new InvalidOperationException($"Invalid bounds for {direction} / {attribute}.");
        }
        if (row.Id > 0 && !Exists(_db, @"SELECT 1 FROM ConditionalDialogueVariants
            WHERE Id=$id AND IFNULL(NodeId,0)=IFNULL($node,0) AND IFNULL(OptionId,0)=IFNULL($option,0)
            AND IFNULL(CharacterId,'')=IFNULL($character,'') AND IFNULL(VisitorId,'')=IFNULL($visitor,'')
            AND IFNULL(Outcome,'')=IFNULL($outcome,'')",
            ("$id",row.Id),("$node",row.NodeId),("$option",row.OptionId),
            ("$character",character),("$visitor",other),("$outcome",outcome)))
            throw new InvalidOperationException("Existing variant identity cannot change; use New variant.");

        int id = row.Id;
        Write(tx =>
        {
            using var command = Command(row.Id == 0
                ? @"INSERT INTO ConditionalDialogueVariants(NodeId,OptionId,CharacterId,VisitorId,Outcome,Priority,Text,ImagePath)
                    VALUES($node,$option,$character,$visitor,$outcome,$priority,$text,$path)"
                : @"UPDATE ConditionalDialogueVariants SET Priority=$priority,Text=$text,ImagePath=$path WHERE Id=$id",tx);
            Add(command,"$id",row.Id); Add(command,"$node",row.NodeId); Add(command,"$option",row.OptionId);
            Add(command,"$character",character); Add(command,"$visitor",other); Add(command,"$outcome",outcome);
            Add(command,"$priority",row.Priority); Add(command,"$text",value); Add(command,"$path",path);
            Changed(command,row.Id != 0);
            if (row.Id == 0)
            {
                using var last = Command("SELECT last_insert_rowid()",tx);
                id=Convert.ToInt32((long)last.ExecuteScalar()!);
            }
            using(var delete=Command("DELETE FROM ConditionalDialogueConditions WHERE VariantId=$id",tx))
            { Add(delete,"$id",id); delete.ExecuteNonQuery(); }
            foreach(var c in row.Conditions)
            {
                using var add=Command(@"INSERT INTO ConditionalDialogueConditions
                    (VariantId,Direction,Attribute,Minimum,Maximum) VALUES($id,$direction,$attribute,$min,$max)",tx);
                Add(add,"$id",id);Add(add,"$direction",c.Direction);Add(add,"$attribute",c.Attribute);
                Add(add,"$min",c.Minimum);Add(add,"$max",c.Maximum);
                add.ExecuteNonQuery();
            }
        });
        return id;
    }

    public void DeleteConditionalVariant(int id)
    {
        Write(tx =>
        {
            using var command=Command("DELETE FROM ConditionalDialogueVariants WHERE Id=$id",tx);
            Add(command,"$id",id); Changed(command,true);
        });
    }

    private static DialogueCandidate ToCandidate(ConditionalVariantRow r) =>
        new(r.Id,r.CharacterId,r.VisitorId,r.Outcome,r.Priority,r.Text,r.ImagePath,true,r.Conditions);

    public DialogueSelection PreviewConditionalIntroduction(int nodeId,string speaker,string visitor,
        IReadOnlyDictionary<string,int> stats)
    {
        var candidates=ConditionalVariants(nodeId,null).Select(ToCandidate).ToList();
        candidates.AddRange(Introductions(nodeId).Select(i => new DialogueCandidate(
            i.Id,i.CharacterId,i.VisitorId,null,0,i.Text,"",false,Array.Empty<DialogueClause>())));
        var match=ConditionalDialogueRules.Resolve(candidates,speaker,visitor,null,stats);
        if (match.Candidate is not null) return match;
        using var cmd=Command("SELECT HostIntroduction FROM Nodes WHERE Id=$id");
        Add(cmd,"$id",nodeId);
        return new DialogueSelection(new DialogueCandidate(0,speaker,null,null,0,
            Convert.ToString(cmd.ExecuteScalar()) ?? "","",false,Array.Empty<DialogueClause>()),
            "Generic introduction", match.Explanation);
    }

    public DialogueSelection PreviewConditionalReply(int optionId,string speaker,string other,
        bool succeeded,IReadOnlyDictionary<string,int> stats,string fallbackText)
    {
        string outcome=succeeded ? "SUCCESS":"FAILURE";
        var candidates=ConditionalVariants(null,optionId).Select(ToCandidate).ToList();
        candidates.AddRange(Lines(optionId).Select(l=>new DialogueCandidate(l.Id,l.CharacterId,l.VisitorId,l.Outcome,
            0,l.Text,l.ImagePath,false,Array.Empty<DialogueClause>())));
        var match=ConditionalDialogueRules.Resolve(candidates,speaker,other,outcome,stats);
        if(match.Candidate is not null) return match;
        return new DialogueSelection(new DialogueCandidate(0,speaker,null,outcome,0,
            fallbackText,"",false,Array.Empty<DialogueClause>()),"Generic choice fallback",match.Explanation);
    }
}
