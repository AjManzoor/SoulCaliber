using System.IO;

namespace SoulCaliberStudio;

/// <summary>Exact required status for a dialogue choice, checked before the roll.</summary>
public sealed record DialogueStatusRequirementRow(int Id, int OptionId, string Direction, string StatusKey)
{
    public override string ToString() => $"{Direction.Replace('_', ' ')} must be {StatusKey}";
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureOptionStatusRequirementsSchema()
    {
        if (Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionStatusRequirements'"))
            return;
        _backupSource.Backup(); // One-time schema migration only; ordinary editing does not make backups.
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.013_OptionStatusRequirements.sql")
            ?? throw new InvalidOperationException("Missing Phase 12D status-requirements migration resource.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            using var fk = Command("PRAGMA foreign_key_check", tx);
            using var result = fk.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Status-requirements migration has invalid foreign keys.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<DialogueStatusRequirementRow> StatusRequirements(int optionId)
    {
        var rows = new List<DialogueStatusRequirementRow>();
        using var cmd = Command("SELECT Id,OptionId,Direction,StatusKey FROM OptionStatusRequirements WHERE OptionId=$option ORDER BY Id");
        Add(cmd, "$option", optionId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    public int SaveStatusRequirement(DialogueStatusRequirementRow row)
    {
        if (row.Direction is not ("ACTOR_TO_PARTNER" or "PARTNER_TO_ACTOR" or "BOTH"))
            throw new InvalidOperationException("Choose a valid direction.");
        if (!Exists(_db, "SELECT 1 FROM Options WHERE Id=$id", ("$id", row.OptionId)))
            throw new InvalidOperationException("The dialogue choice no longer exists.");
        if (!Exists(_db, "SELECT 1 FROM RelationshipStatuses WHERE Key=$key AND IsActive=1", ("$key", row.StatusKey)))
            throw new InvalidOperationException("Select an active relationship status.");
        // Requiring contradictory statuses in an overlapping direction would permanently lock the choice.
        using (var conflict = Command("""
            SELECT 1 FROM OptionStatusRequirements
            WHERE OptionId=$option AND Id<>$id
              AND (Direction=$direction OR Direction='BOTH' OR $direction='BOTH')
            LIMIT 1
            """))
        {
            Add(conflict, "$option", row.OptionId);
            Add(conflict, "$id", row.Id);
            Add(conflict, "$direction", row.Direction);
            if (conflict.ExecuteScalar() is not null)
                throw new InvalidOperationException("Another requirement already covers this direction. Edit it instead.");
        }
        int savedId = row.Id;
        Write(tx =>
        {
            using var cmd = Command(row.Id == 0
                ? "INSERT INTO OptionStatusRequirements(OptionId,Direction,StatusKey) VALUES($option,$direction,$status)"
                : "UPDATE OptionStatusRequirements SET Direction=$direction,StatusKey=$status WHERE Id=$id AND OptionId=$option", tx);
            Add(cmd, "$option", row.OptionId);
            Add(cmd, "$direction", row.Direction);
            Add(cmd, "$status", row.StatusKey);
            if (row.Id != 0) Add(cmd, "$id", row.Id);
            Changed(cmd, row.Id != 0);
            if (row.Id == 0)
            {
                using var getId = Command("SELECT last_insert_rowid()", tx);
                savedId = Convert.ToInt32((long)getId.ExecuteScalar()!);
            }
        });
        return savedId;
    }

    public void DeleteStatusRequirement(int optionId, int requirementId)
    {
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM OptionStatusRequirements WHERE Id=$id AND OptionId=$option", tx);
            Add(cmd, "$id", requirementId);
            Add(cmd, "$option", optionId);
            Changed(cmd, true);
        });
    }
}
