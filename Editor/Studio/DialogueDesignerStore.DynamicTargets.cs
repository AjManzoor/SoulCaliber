using System.IO;

namespace SoulCaliberStudio;

public sealed record DynamicTargetRow(int OptionId, string OwnerRef, string RelationshipDirection,
    string RequiredStatusKey, string SelectionMode = "GROUP", IReadOnlyList<string>? CharacterIds = null);
public sealed record PairStatusEffectRow(int Id, int OptionId, string Outcome, string FromRef, string ToRef,
    string StatusKey, string? ReverseStatusKey)
{
    public override string ToString() => $"{Outcome}: {FromRef} → {ToRef} = {StatusKey}" +
        (ReverseStatusKey is null ? "" : $"; reverse = {ReverseStatusKey}");
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureDynamicTargetChoicesSchema()
    {
        if (!Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionDynamicTargets'") ||
            !Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionPairStatusEffects'"))
        {
            _backupSource.Backup();
            using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
                "SoulCaliberStudio.Migrations.023_DynamicTargetChoices.sql")
                ?? throw new InvalidOperationException("Missing Phase 13H-4 migration resource.");
            using var reader = new StreamReader(stream);
            using var tx = _db.BeginTransaction();
            try
            {
                using var cmd = Command(reader.ReadToEnd(),tx);
                cmd.ExecuteNonQuery();
                using(var fk=Command("PRAGMA foreign_key_check",tx))
                using(var r=fk.ExecuteReader())
                    if(r.Read()) throw new InvalidOperationException("Dynamic-target migration has invalid foreign keys.");
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }
        EnsureCustomDynamicTargetChoicesSchema();
    }

    private void EnsureCustomDynamicTargetChoicesSchema()
    {
        bool hasMode=Exists(_db,"SELECT 1 FROM pragma_table_info('OptionDynamicTargets') WHERE name='SelectionMode'");
        bool hasCharacters=Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionDynamicTargetCharacters'");
        if(hasMode && hasCharacters) return;
        if(hasMode || hasCharacters)
            throw new InvalidOperationException("Partial dynamic-target migration 024 detected. Restore a backup before proceeding.");
        _backupSource.Backup();
        using var stream=typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.024_CustomDynamicTargetLists.sql")
            ?? throw new InvalidOperationException("Missing Phase 13H-4 custom-list migration resource.");
        using var reader=new StreamReader(stream);
        using var tx=_db.BeginTransaction();
        try
        {
            using(var cmd=Command(reader.ReadToEnd(),tx)) cmd.ExecuteNonQuery();
            using(var fk=Command("PRAGMA foreign_key_check",tx))
            using(var r=fk.ExecuteReader())
                if(r.Read()) throw new InvalidOperationException("Custom-target migration has invalid foreign keys.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public DynamicTargetRow? DynamicTargets(int optionId)
    {
        string owner,direction,status,mode;
        using(var cmd=Command("SELECT OwnerRef,RelationshipDirection,RequiredStatusKey,SelectionMode FROM OptionDynamicTargets WHERE OptionId=$id"))
        {
            Add(cmd,"$id",optionId);
            using var r=cmd.ExecuteReader();
            if(!r.Read()) return null;
            owner=r.GetString(0);direction=r.GetString(1);status=r.GetString(2);mode=r.GetString(3);
        }
        var ids=new List<string>();
        using(var cmd=Command("SELECT CharacterId FROM OptionDynamicTargetCharacters WHERE OptionId=$id ORDER BY SortOrder,CharacterId"))
        {
            Add(cmd,"$id",optionId);
            using var r=cmd.ExecuteReader();
            while(r.Read()) ids.Add(r.GetString(0));
        }
        return new DynamicTargetRow(optionId,owner,direction,status,mode,ids);
    }

    public void SaveDynamicTargets(DynamicTargetRow? row, int optionId)
    {
        List<string> characterIds=[];
        if(row is not null)
        {
            if(row.SelectionMode is not ("GROUP" or "CUSTOM"))
                throw new InvalidOperationException("Choose a relationship group or a custom character list.");
            if(row.OwnerRef is not ("INITIATOR" or "RECIPIENT") ||
               row.RelationshipDirection is not ("OUTGOING" or "INCOMING"))
                throw new InvalidOperationException("Invalid relationship target group settings.");
            if(row.SelectionMode=="GROUP")
                EnsureActiveStatus(row.RequiredStatusKey);
            else
            {
                characterIds=(row.CharacterIds ?? Array.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if(characterIds.Count==0)
                    throw new InvalidOperationException("Select at least one character for the custom target list.");
                foreach(var id in characterIds)
                {
                    if(string.IsNullOrWhiteSpace(id) ||
                       !Exists(_db,"SELECT 1 FROM Characters WHERE Id=$id COLLATE NOCASE",("$id",id)))
                        throw new InvalidOperationException($"Unknown custom target character '{id}'.");
                }
            }
        }
        Write(tx =>
        {
            using(var cmd=Command(row is null ?
                "DELETE FROM OptionDynamicTargets WHERE OptionId=$id" :
                "INSERT INTO OptionDynamicTargets(OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,SelectionMode) VALUES($id,$owner,$direction,$status,$mode) ON CONFLICT(OptionId) DO UPDATE SET OwnerRef=excluded.OwnerRef,RelationshipDirection=excluded.RelationshipDirection,RequiredStatusKey=excluded.RequiredStatusKey,SelectionMode=excluded.SelectionMode",tx))
            {
                Add(cmd,"$id",optionId);
                if(row is not null)
                {
                    Add(cmd,"$owner",row.OwnerRef);Add(cmd,"$direction",row.RelationshipDirection);
                    Add(cmd,"$status",row.RequiredStatusKey);Add(cmd,"$mode",row.SelectionMode);
                }
                cmd.ExecuteNonQuery();
            }
            if(row is null) return; // Child rows cascade from the parent.
            using(var remove=Command("DELETE FROM OptionDynamicTargetCharacters WHERE OptionId=$id",tx))
            {
                Add(remove,"$id",optionId);remove.ExecuteNonQuery();
            }
            for(int i=0;i<characterIds.Count;i++)
            {
                using var add=Command("INSERT INTO OptionDynamicTargetCharacters(OptionId,CharacterId,SortOrder) VALUES($id,$character,$order)",tx);
                Add(add,"$id",optionId);Add(add,"$character",characterIds[i]);Add(add,"$order",i);
                add.ExecuteNonQuery();
            }
        });
    }

    public List<PairStatusEffectRow> PairStatusEffects(int optionId)
    {
        var result=new List<PairStatusEffectRow>();
        using var cmd=Command("SELECT Id,OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey FROM OptionPairStatusEffects WHERE OptionId=$id ORDER BY Id");
        Add(cmd,"$id",optionId);
        using var r=cmd.ExecuteReader();
        while(r.Read()) result.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6)));
        return result;
    }

    private void EnsureActiveStatus(string key)
    {
        if (!Exists(_db,"SELECT 1 FROM RelationshipStatuses WHERE Key=$key COLLATE NOCASE AND IsActive=1",("$key",key)))
            throw new InvalidOperationException($"Unknown or inactive relationship status '{key}'.");
    }
    private void EnsureCharacterRef(string value)
    {
        if (value is "INITIATOR" or "RECIPIENT" or "TARGET") return;
        if (!Exists(_db,"SELECT 1 FROM Characters WHERE Id=$id COLLATE NOCASE",("$id",value)))
            throw new InvalidOperationException($"Unknown character '{value}'.");
    }
    public int SavePairStatusEffect(PairStatusEffectRow row)
    {
        if(row.Outcome is not ("SUCCESS" or "FAILURE")) throw new InvalidOperationException("Choose success or failure.");
        EnsureCharacterRef(row.FromRef); EnsureCharacterRef(row.ToRef);
        if(row.FromRef==row.ToRef) throw new InvalidOperationException("A relationship must involve two distinct characters.");
        EnsureActiveStatus(row.StatusKey);
        if(row.ReverseStatusKey is not null) EnsureActiveStatus(row.ReverseStatusKey);
        int result=row.Id;
        Write(tx =>
        {
            using var cmd=Command(row.Id==0 ?
                "INSERT INTO OptionPairStatusEffects(OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey) VALUES($option,$outcome,$from,$to,$status,$reverse)" :
                "UPDATE OptionPairStatusEffects SET Outcome=$outcome,FromRef=$from,ToRef=$to,StatusKey=$status,ReverseStatusKey=$reverse WHERE Id=$id AND OptionId=$option",tx);
            Add(cmd,"$option",row.OptionId);Add(cmd,"$outcome",row.Outcome);
            Add(cmd,"$from",row.FromRef);Add(cmd,"$to",row.ToRef);Add(cmd,"$status",row.StatusKey);Add(cmd,"$reverse",row.ReverseStatusKey);
            if(row.Id!=0) Add(cmd,"$id",row.Id);
            Changed(cmd,row.Id!=0);
            if(row.Id==0)
            {
                using var id=Command("SELECT last_insert_rowid()",tx);
                result=Convert.ToInt32((long)id.ExecuteScalar()!);
            }
        });
        return result;
    }
    public void DeletePairStatusEffect(int id,int optionId)
    {
        Write(tx=>
        {
            using var cmd=Command("DELETE FROM OptionPairStatusEffects WHERE Id=$id AND OptionId=$option",tx);
            Add(cmd,"$id",id);Add(cmd,"$option",optionId);Changed(cmd);
        });
    }
}
