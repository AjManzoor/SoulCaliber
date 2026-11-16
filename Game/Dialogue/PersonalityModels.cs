using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace SoulCaliber.Dialogue;

public sealed record CharacterSkillDefinition(string Key, string Name, int Minimum, int Maximum,
    int Default, bool IsActive);

/// <summary>Game-defined skills, read from content.db. The stable key, not the display name,
/// is used by every check. Skills are independent of relationship attributes.</summary>
public sealed class CharacterSkillCatalogue
{
    private readonly Dictionary<string, CharacterSkillDefinition> _byKey;
    public IReadOnlyList<CharacterSkillDefinition> All { get; }
    public CharacterSkillCatalogue(IEnumerable<CharacterSkillDefinition> definitions)
    {
        var list=definitions.ToList();
        _byKey=list.ToDictionary(x=>x.Key,StringComparer.OrdinalIgnoreCase);
        All=list.AsReadOnly();
        if (!list.Any(x=>x.IsActive)) throw new InvalidOperationException("No active character skills are defined.");
    }
    public CharacterSkillDefinition Get(string key) => _byKey.TryGetValue(key??"",out var item) ? item :
        throw new InvalidOperationException($"Undefined character skill '{key}'.");

    public static CharacterSkillCatalogue Load()
    {
        using var cn=new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource=DatabasePaths.Content,Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());
        cn.Open();
        using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT Key,Name,MinValue,MaxValue,DefaultValue,IsActive FROM CharacterSkills ORDER BY SortOrder,Key";
        using var r=cmd.ExecuteReader();
        var list=new List<CharacterSkillDefinition>();
        while(r.Read()) list.Add(new(r.GetString(0),r.GetString(1),r.GetInt32(2),
            r.GetInt32(3),r.GetInt32(4),r.GetInt32(5)==1));
        return new CharacterSkillCatalogue(list);
    }
}

/// <summary>Temporary legacy class name retained for compatibility with existing Godot code. Stores dynamic skill keys, not personality sensitivities.</summary>
public sealed class PersonalityStats
{
    private readonly CharacterSkillCatalogue _catalogue;
    private readonly Dictionary<string,int> _values=new(StringComparer.OrdinalIgnoreCase);
    public PersonalityStats(CharacterSkillCatalogue catalogue) => _catalogue=catalogue;
    public int Get(string key)
    {
        var definition=_catalogue.Get(key);
        return _values.TryGetValue(definition.Key,out int value) ? value : definition.Default;
    }
    public void Set(string key,int value)
    {
        var definition=_catalogue.Get(key);
        if (!definition.IsActive || value < definition.Minimum || value > definition.Maximum)
            throw new InvalidOperationException($"Invalid value {value} for character skill '{key}'.");
        _values[definition.Key]=value;
    }
    public void Validate(string characterId)
    {
        foreach(var (key,value) in _values)
        {
            var definition=_catalogue.Get(key);
            if (!definition.IsActive || value < definition.Minimum || value > definition.Maximum)
                throw new InvalidOperationException($"Invalid skill '{key}' for '{characterId}'.");
        }
    }
}

public sealed class OptionSkillCheck
{
    public string InteractionKey { get; set; } = "";
    public string NodeKey { get; set; } = "";
    public string OptionKey { get; set; } = "";
    public string ActorSkill { get; set; } = "";
    public string DefenderSkill { get; set; } = "";
    public string CheckMode { get; set; } = "STANDARD";
    public double ActorFactor { get; set; } = 0.5;
    public double DefenderFactor { get; set; } = 0.5;
}
