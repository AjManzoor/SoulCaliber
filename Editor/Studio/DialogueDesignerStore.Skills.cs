using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

public sealed record CharacterSkillDefinition(string Key,string Name,string Description,int Minimum,
    int Maximum,int Default,int SortOrder,bool IsActive)
{
    public override string ToString() => $"{Name} [{Key}] ({Minimum}..{Maximum})";
}

public sealed partial class DialogueDesignerStore
{
    private void EnsureDynamicSkillsSchema()
    {
        if (Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='CharacterSkills'"))
        {
            // A damaged/partial schema must fail loudly rather than appear successfully migrated.
            if (!Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='CharacterSkillValues'"))
                throw new InvalidOperationException("Incomplete Phase 10 schema. Restore your master database backup.");
            return;
        }
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.007_DynamicSkills.sql")
            ?? throw new InvalidOperationException("Phase 10 migration resource is missing.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd=Command(reader.ReadToEnd(),tx)) cmd.ExecuteNonQuery();
            using var check=Command("PRAGMA foreign_key_check",tx);
            using var result=check.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Phase 10 failed foreign-key checks.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<CharacterSkillDefinition> SkillDefinitions(bool activeOnly=false)
    {
        using var cmd=Command("SELECT Key,Name,Description,MinValue,MaxValue,DefaultValue,SortOrder,IsActive " +
            "FROM CharacterSkills WHERE ($active=0 OR IsActive=1) ORDER BY SortOrder,Name,Key");
        Add(cmd,"$active",activeOnly ? 1 : 0);
        using var r=cmd.ExecuteReader();
        var list=new List<CharacterSkillDefinition>();
        while(r.Read()) list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),
            r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6),r.GetInt32(7)==1));
        return list;
    }
    public string[] SkillKeys => SkillDefinitions(true).Select(s=>s.Key).ToArray();

    public void SaveSkill(CharacterSkillDefinition item, bool isNew)
    {
        string key=(item.Key??"").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key,"^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Use a permanent uppercase skill key (A-Z, 0-9, _).");
        string name=Required(item.Name,"Skill name");
        if (item.Minimum < -1000 || item.Maximum > 1000 || item.Minimum >= item.Maximum ||
            item.Default < item.Minimum || item.Default > item.Maximum)
            throw new InvalidOperationException("Invalid skill bounds or default value.");
        bool referenced = Exists(_db,"SELECT 1 FROM SkillChecks WHERE ActorSkill=$key OR DefenderSkill=$key LIMIT 1",("$key",key));
        if (!isNew && !item.IsActive && referenced)
            throw new InvalidOperationException("Skill is used by an interaction. Reassign its checks first.");
        if (!isNew && Exists(_db,"SELECT 1 FROM CharacterSkillValues WHERE SkillKey=$key AND (Value<$min OR Value>$max) LIMIT 1",
            ("$key",key),("$min",item.Minimum),("$max",item.Maximum)))
            throw new InvalidOperationException("Character skill values lie outside these new bounds. Edit them first.");
        Write(tx=>
        {
            using var cmd=Command(isNew
                ? @"INSERT INTO CharacterSkills(Key,Name,Description,MinValue,MaxValue,DefaultValue,SortOrder,IsActive)
                    VALUES($key,$name,$description,$min,$max,$default,$sort,$active)"
                : @"UPDATE CharacterSkills SET Name=$name,Description=$description,MinValue=$min,
                    MaxValue=$max,DefaultValue=$default,SortOrder=$sort,IsActive=$active WHERE Key=$key",tx);
            Add(cmd,"$key",key);Add(cmd,"$name",name);Add(cmd,"$description",item.Description??"");
            Add(cmd,"$min",item.Minimum);Add(cmd,"$max",item.Maximum);Add(cmd,"$default",item.Default);
            Add(cmd,"$sort",item.SortOrder);Add(cmd,"$active",item.IsActive?1:0);
            Changed(cmd,!isNew);
        });
    }
    public void DeleteSkill(string key)
    {
        if (Exists(_db,"SELECT 1 FROM SkillChecks WHERE ActorSkill=$key OR DefenderSkill=$key LIMIT 1",("$key",key)))
            throw new InvalidOperationException("Skill is used in choice checks. Reassign those checks first.");
        Write(tx=>
        {
            // FK RESTRICT on skill values protects deleting skills still assigned to characters.
            using var removeValues=Command("DELETE FROM CharacterSkillValues WHERE SkillKey=$key",tx);
            Add(removeValues,"$key",key);removeValues.ExecuteNonQuery();
            using var cmd=Command("DELETE FROM CharacterSkills WHERE Key=$key",tx);
            Add(cmd,"$key",key);Changed(cmd,true);
        });
    }
}
