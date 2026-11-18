using System.IO;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

public sealed record DialogueMemoryEffectRow(int Id, int OptionId, string MemoryKey,
    string Trigger, string Affected, string? SeverityOverride, string? KnowledgeOverride)
{
    public override string ToString() => $"{Trigger} · {MemoryKey} · {DisplayAffected(Affected)}";
    private static string DisplayAffected(string affected) => affected switch
    {
        "ACTOR" => "Initiator",
        "PARTNER" => "Recipient",
        "BOTH" => "Both",
        _ => affected
    };
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureOptionMemoryEffectsSchema()
    {
        if (Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionMemoryEffects'")) return;
        _backupSource.Backup(); // one-time migration, never per-save
        using var stream=typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.016_OptionMemoryEffects.sql")
            ?? throw new InvalidOperationException("Missing memory-effect migration resource.");
        using var reader=new StreamReader(stream);
        using var tx=_db.BeginTransaction();
        try { using var cmd=Command(reader.ReadToEnd(),tx);cmd.ExecuteNonQuery();tx.Commit(); }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<DialogueMemoryEffectRow> MemoryEffects(int optionId)
    {
        using var cmd=Command("SELECT Id,OptionId,MemoryKey,Trigger,Affected,SeverityOverride,KnowledgeOverride FROM OptionMemoryEffects WHERE OptionId=$option ORDER BY Id");
        Add(cmd,"$option",optionId);
        using var reader=cmd.ExecuteReader();
        var rows=new List<DialogueMemoryEffectRow>();
        while(reader.Read()) rows.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
            reader.IsDBNull(5)?null:reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6)));
        return rows;
    }

    public int SaveMemoryEffect(DialogueMemoryEffectRow item, KnowledgeAudienceSpec? customAudience = null)
    {
        if (!Exists(_db,"SELECT 1 FROM Options WHERE Id=$id",("$id",item.OptionId)))
            throw new InvalidOperationException("Dialogue choice no longer exists.");
        if (!Exists(_db,"SELECT 1 FROM MemoryTypes WHERE Key=$key AND IsActive=1",("$key",item.MemoryKey)))
            throw new InvalidOperationException("Select an active memory type.");
        if (item.Trigger is not ("SELECTED" or "SUCCESS" or "FAILURE" or "BOTH")) throw new InvalidOperationException("Invalid memory trigger.");
        if (item.Affected is not ("ACTOR" or "PARTNER" or "BOTH")) throw new InvalidOperationException("Invalid affected character setting.");
        if (item.SeverityOverride is not (null or "MAJOR" or "MINOR")) throw new InvalidOperationException("Invalid severity override.");
        if (item.KnowledgeOverride is not (null or "QUIET" or "ALLIES" or "GLOBAL")) throw new InvalidOperationException("Invalid knowledge override.");
        ValidateAudience(customAudience);
        int savedId=item.Id;
        Write(tx => {
            using var cmd=Command(item.Id==0 ?
                "INSERT INTO OptionMemoryEffects(OptionId,MemoryKey,Trigger,Affected,SeverityOverride,KnowledgeOverride) VALUES($option,$key,$trigger,$affected,$severity,$knowledge)" :
                "UPDATE OptionMemoryEffects SET MemoryKey=$key,Trigger=$trigger,Affected=$affected,SeverityOverride=$severity,KnowledgeOverride=$knowledge WHERE Id=$id AND OptionId=$option",tx);
            Add(cmd,"$option",item.OptionId);Add(cmd,"$key",item.MemoryKey);Add(cmd,"$trigger",item.Trigger);
            Add(cmd,"$affected",item.Affected);Add(cmd,"$severity",item.SeverityOverride);Add(cmd,"$knowledge",item.KnowledgeOverride);
            if(item.Id!=0) Add(cmd,"$id",item.Id);
            Changed(cmd,item.Id!=0);
            if(item.Id==0) {using var get=Command("SELECT last_insert_rowid()",tx);savedId=Convert.ToInt32((long)get.ExecuteScalar()!);}
            SaveMemoryEffectAudienceInTransaction(savedId, customAudience, tx);
        });
        return savedId;
    }

    public void DeleteMemoryEffect(int optionId,int effectId) => Write(tx => {
        using var cmd=Command("DELETE FROM OptionMemoryEffects WHERE Id=$id AND OptionId=$option",tx);
        Add(cmd,"$id",effectId);Add(cmd,"$option",optionId);Changed(cmd);
    });
}
