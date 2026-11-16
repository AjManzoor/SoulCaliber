using System.Data;
using System.IO;

namespace SoulCaliberStudio;

public sealed record DialogueStatusEffectRow(int Id, int OptionId, string Outcome,
    string Direction, string StatusKey)
{
    public override string ToString() => $"{Outcome} · {Direction.Replace('_',' ')} · {StatusKey}";
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureOptionStatusEffectsSchema()
    {
        if (Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionStatusEffects'"))
            return;
        // One-time schema migration backup, never a snapshot per ordinary edit.
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.011_OptionStatusEffects.sql")
            ?? throw new InvalidOperationException("Missing Phase 12C status-effect migration resource.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            using var fk = Command("PRAGMA foreign_key_check", tx);
            using var result = fk.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Status-effect migration has invalid foreign keys.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<DialogueStatusEffectRow> StatusEffects(int optionId)
    {
        var result = new List<DialogueStatusEffectRow>();
        using var cmd = Command("""
            SELECT Id,OptionId,Outcome,Direction,StatusKey FROM OptionStatusEffects
            WHERE OptionId=$option ORDER BY Outcome,Id
            """);
        Add(cmd,"$option",optionId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(new(r.GetInt32(0), r.GetInt32(1),
            r.GetString(2), r.GetString(3), r.GetString(4)));
        return result;
    }

    public int SaveStatusEffect(DialogueStatusEffectRow row)
    {
        if (row.Outcome is not ("SUCCESS" or "FAILURE"))
            throw new InvalidOperationException("Choose SUCCESS or FAILURE.");
        if (row.Direction is not ("ACTOR_TO_PARTNER" or "PARTNER_TO_ACTOR" or "BOTH"))
            throw new InvalidOperationException("Choose a valid direction.");
        if (!Exists(_db,"SELECT 1 FROM Options WHERE Id=$id",("$id",row.OptionId)))
            throw new InvalidOperationException("The dialogue option no longer exists.");
        if (!Exists(_db,"SELECT 1 FROM RelationshipStatuses WHERE Key=$key AND IsActive=1",
                ("$key",row.StatusKey)))
            throw new InvalidOperationException("The selected relationship status is not active.");
        // BOTH and a one-way effect for the SAME outcome would give conflicting instructions.
        using (var collision = Command("""
            SELECT 1 FROM OptionStatusEffects
            WHERE OptionId=$option AND Outcome=$outcome AND Id<>$id
              AND (Direction=$direction OR Direction='BOTH' OR $direction='BOTH')
            LIMIT 1
            """))
        {
            Add(collision,"$option",row.OptionId);
            Add(collision,"$outcome",row.Outcome);
            Add(collision,"$direction",row.Direction);
            Add(collision,"$id",row.Id);
            if (collision.ExecuteScalar() is not null)
                throw new InvalidOperationException("Another status effect already targets this direction/outcome. Edit or delete it first.");
        }
        int savedId = row.Id;
        Write(tx =>
        {
            using var cmd = Command(row.Id == 0 ? """
                INSERT INTO OptionStatusEffects(OptionId,Outcome,Direction,StatusKey)
                VALUES($option,$outcome,$direction,$status)
                """ : """
                UPDATE OptionStatusEffects SET Outcome=$outcome,Direction=$direction,StatusKey=$status
                WHERE Id=$id AND OptionId=$option
                """,tx);
            Add(cmd,"$option",row.OptionId);
            Add(cmd,"$outcome",row.Outcome);
            Add(cmd,"$direction",row.Direction);
            Add(cmd,"$status",row.StatusKey);
            if (row.Id != 0) Add(cmd,"$id",row.Id);
            Changed(cmd, row.Id != 0);
            if (row.Id == 0)
            {
                using var id = Command("SELECT last_insert_rowid()",tx);
                savedId = Convert.ToInt32((long)id.ExecuteScalar()!);
            }
        });
        return savedId;
    }

    public void DeleteStatusEffect(int optionId,int effectId)
    {
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM OptionStatusEffects WHERE Id=$id AND OptionId=$option",tx);
            Add(cmd,"$id",effectId); Add(cmd,"$option",optionId);
            Changed(cmd);
        });
    }
}
