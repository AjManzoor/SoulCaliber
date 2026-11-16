using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

public sealed record PersonalityTraitDefinition(string Key,string Name,string Description,int SortOrder,bool IsActive)
{
    public override string ToString() => $"{Name} [{Key}]" + (IsActive ? "" : " (inactive)");
}
public sealed record PersonalityTraitModifier(int Id,string TraitKey,string AttributeKey,
    string Outcome,string Change,double Factor)
{
    public override string ToString() => $"{AttributeKey} · {Outcome} · {Change} ×{Factor:0.##}";
}

public sealed partial class DialogueDesignerStore
{
    private void EnsurePersonalityTraitsSchema()
    {
        if (Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='PersonalityTraits'"))
        {
            if (!Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='CharacterTraits'") ||
                !Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='PersonalityTraitModifiers'"))
                throw new InvalidOperationException("Incomplete Phase 10B personality schema. Restore a Studio backup.");
            return;
        }
        _backupSource.Backup();
        using var stream=typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.008_PersonalityTraits.sql")
            ?? throw new InvalidOperationException("Missing embedded Phase 10B migration.");
        using var reader=new StreamReader(stream);
        using var tx=_db.BeginTransaction();
        try
        {
            using (var cmd=Command(reader.ReadToEnd(),tx)) cmd.ExecuteNonQuery();
            using var check=Command("PRAGMA foreign_key_check",tx);
            using var r=check.ExecuteReader();
            if (r.Read()) throw new InvalidOperationException("Personality migration failed foreign-key validation.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<PersonalityTraitDefinition> TraitDefinitions()
    {
        using var cmd=Command("SELECT Key,Name,Description,SortOrder,IsActive FROM PersonalityTraits ORDER BY SortOrder,Name,Key");
        using var r=cmd.ExecuteReader();
        var result=new List<PersonalityTraitDefinition>();
        while(r.Read()) result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4)==1));
        return result;
    }
    public IReadOnlyList<PersonalityTraitModifier> TraitModifiers(string traitKey)
    {
        using var cmd=Command("SELECT Id,TraitKey,AttributeKey,Outcome,Change,Factor FROM PersonalityTraitModifiers WHERE TraitKey=$key ORDER BY AttributeKey,Outcome,Change");
        Add(cmd,"$key",traitKey);
        using var r=cmd.ExecuteReader();
        var result=new List<PersonalityTraitModifier>();
        while(r.Read()) result.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDouble(5)));
        return result;
    }
    public void SaveTrait(PersonalityTraitDefinition trait,bool isNew)
    {
        string key=(trait.Key??"").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key,"^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Trait key must be uppercase letters, numbers and underscores, starting with a letter.");
        string name=Required(trait.Name,"Trait name");
        if (!isNew && !trait.IsActive && Exists(_db,
            "SELECT 1 FROM CharacterTraits WHERE TraitKey=$key LIMIT 1",("$key",key)))
            throw new InvalidOperationException("Remove this trait from all characters before deactivating it.");
        Write(tx=>
        {
            using var cmd=Command(isNew
                ? "INSERT INTO PersonalityTraits(Key,Name,Description,SortOrder,IsActive) VALUES($key,$name,$description,$sort,$active)"
                : "UPDATE PersonalityTraits SET Name=$name,Description=$description,SortOrder=$sort,IsActive=$active WHERE Key=$key",tx);
            Add(cmd,"$key",key);Add(cmd,"$name",name);Add(cmd,"$description",trait.Description??"");
            Add(cmd,"$sort",trait.SortOrder);Add(cmd,"$active",trait.IsActive?1:0);
            Changed(cmd,!isNew);
        });
    }
    public void DeleteTrait(string key)
    {
        if (Exists(_db,"SELECT 1 FROM CharacterTraits WHERE TraitKey=$key LIMIT 1",("$key",key)))
            throw new InvalidOperationException("This trait is assigned to characters. Remove those tags before deleting it.");
        Write(tx=>
        {
            using var cmd=Command("DELETE FROM PersonalityTraits WHERE Key=$key",tx);
            Add(cmd,"$key",key);Changed(cmd,true);
        });
    }
    public void SaveTraitModifier(PersonalityTraitModifier item)
    {
        if (item.Factor < 0 || item.Factor > 3 || double.IsNaN(item.Factor) || double.IsInfinity(item.Factor))
            throw new InvalidOperationException("Multiplier must be between 0 and 3.");
        if (item.Outcome is not ("BOTH" or "SUCCESS" or "FAILURE") ||
            item.Change is not ("BOTH" or "GAIN" or "LOSS"))
            throw new InvalidOperationException("Choose valid outcome and change settings.");
        if (!Exists(_db,"SELECT 1 FROM RelationshipAttributes WHERE Key=$key AND IsActive=1",("$key",item.AttributeKey)))
            throw new InvalidOperationException("Choose an active relationship attribute.");
        Write(tx=>
        {
            using var cmd=Command(item.Id==0
                ? @"INSERT INTO PersonalityTraitModifiers(TraitKey,AttributeKey,Outcome,Change,Factor)
                    VALUES($trait,$attribute,$outcome,$change,$factor)"
                : @"UPDATE PersonalityTraitModifiers SET AttributeKey=$attribute,Outcome=$outcome,
                    Change=$change,Factor=$factor WHERE Id=$id AND TraitKey=$trait",tx);
            Add(cmd,"$id",item.Id);Add(cmd,"$trait",item.TraitKey);Add(cmd,"$attribute",item.AttributeKey);
            Add(cmd,"$outcome",item.Outcome);Add(cmd,"$change",item.Change);Add(cmd,"$factor",item.Factor);
            Changed(cmd,item.Id!=0);
        });
    }
    public void DeleteTraitModifier(int id)
    {
        Write(tx=>
        {
            using var cmd=Command("DELETE FROM PersonalityTraitModifiers WHERE Id=$id",tx);
            Add(cmd,"$id",id);Changed(cmd,true);
        });
    }
}
