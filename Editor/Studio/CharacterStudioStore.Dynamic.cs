using Microsoft.Data.Sqlite;
using System.Data;

namespace SoulCaliberStudio;

public sealed partial class CharacterStudioStore
{
    public Dictionary<string,int> GetSkills(string? characterId)
    {
        var result=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        using (var cmd=Sql("SELECT Key,DefaultValue FROM CharacterSkills WHERE IsActive=1"))
        using (var r=cmd.ExecuteReader()) while(r.Read()) result.Add(r.GetString(0),r.GetInt32(1));
        if (characterId is null) return result;
        using var values=Sql("SELECT SkillKey,Value FROM CharacterSkillValues WHERE CharacterId=$id");
        Add(values,"$id",characterId);
        using var reader=values.ExecuteReader();
        while(reader.Read())
        {
            string key=reader.GetString(0);
            if (result.ContainsKey(key)) result[key]=reader.GetInt32(1);
        }
        return result;
    }
    public string[] GetCharacterTraits(string? characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return [];
        using var cmd=Sql("SELECT TraitKey FROM CharacterTraits WHERE CharacterId=$id ORDER BY TraitKey");
        Add(cmd,"$id",characterId);
        using var r=cmd.ExecuteReader();
        var result=new List<string>();
        while(r.Read()) result.Add(r.GetString(0));
        return result.ToArray();
    }

    public void SaveCharacter(Character character, IReadOnlyDictionary<string,int> skills, string? existingId,
        IReadOnlyCollection<string> traits)
    {
        string id=Identity(character.Id,"Character ID");
        string name=(character.Name??"").Trim();
        if (name.Length==0) throw new InvalidOperationException("Character name required.");
        if (existingId is not null && !id.Equals(existingId,StringComparison.Ordinal))
            throw new InvalidOperationException("Existing character IDs cannot be changed.");
        string neutral=ResourcePath(character.DefaultImage),win=ResourcePath(character.WinImage),lose=ResourcePath(character.LoseImage);
        var bounds=new Dictionary<string,(int Min,int Max)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd=Sql("SELECT Key,MinValue,MaxValue FROM CharacterSkills WHERE IsActive=1"))
        using (var r=cmd.ExecuteReader()) while(r.Read()) bounds.Add(r.GetString(0),(r.GetInt32(1),r.GetInt32(2)));
        foreach(var (key,value) in skills)
        {
            if (!bounds.TryGetValue(key,out var limits) || value < limits.Min || value > limits.Max)
                throw new InvalidOperationException($"Invalid value {value} for skill {key}.");
        }
        if (skills.Count!=bounds.Count) throw new InvalidOperationException("Save values for every active skill.");
        var traitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd=Sql("SELECT Key FROM PersonalityTraits WHERE IsActive=1"))
        using (var r=cmd.ExecuteReader()) while(r.Read()) traitKeys.Add(r.GetString(0));
        foreach (var key in traits)
            if (!traitKeys.Contains(key)) throw new InvalidOperationException($"Unknown or inactive trait '{key}'.");
        Write(tx=>
        {
            using(var cmd=Sql(existingId is null
                ? "INSERT INTO Characters(Id,Name,DefaultImage,WinImage,LoseImage) VALUES($id,$name,$neutral,$win,$lose)"
                : "UPDATE Characters SET Name=$name,DefaultImage=$neutral,WinImage=$win,LoseImage=$lose WHERE Id=$id",tx))
            {
                Add(cmd,"$id",id);Add(cmd,"$name",name);Add(cmd,"$neutral",neutral);
                Add(cmd,"$win",win);Add(cmd,"$lose",lose);
                if (cmd.ExecuteNonQuery()!=1) throw new DBConcurrencyException("Character could not be saved.");
            }
            foreach(var (key,value) in skills)
            {
                using var cmd=Sql(@"INSERT INTO CharacterSkillValues(CharacterId,SkillKey,Value) VALUES($id,$key,$value)
                    ON CONFLICT(CharacterId,SkillKey) DO UPDATE SET Value=excluded.Value",tx);
                Add(cmd,"$id",id);Add(cmd,"$key",key);Add(cmd,"$value",value);cmd.ExecuteNonQuery();
            }
            // Trait assignments are saved atomically with character data and skills.
            using (var clear=Sql("DELETE FROM CharacterTraits WHERE CharacterId=$id",tx))
            { Add(clear,"$id",id);clear.ExecuteNonQuery(); }
            foreach (var key in traits.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var cmd=Sql("INSERT INTO CharacterTraits(CharacterId,TraitKey) VALUES($id,$trait)",tx);
                Add(cmd,"$id",id);Add(cmd,"$trait",key);cmd.ExecuteNonQuery();
            }
        });
    }
}
