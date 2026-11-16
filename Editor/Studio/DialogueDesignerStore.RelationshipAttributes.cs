using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

// The same stable keys are used by master content, runtime save values, and the UI.
public sealed record RelationshipAttributeDefinition(
    string Key, string Name, string Description, int Minimum, int Maximum, int Default,
    string IconPath, string Color, int SortOrder, bool IsActive)
{
    public override string ToString() => $"{Name} [{Key}]  ({Minimum}..{Maximum})";
}

public sealed partial class DialogueDesignerStore
{
    private void EnsureDynamicRelationshipsSchema()
    {
        if (Exists(_db,"SELECT 1 FROM sqlite_master WHERE type='table' AND name='RelationshipAttributes'")) return;
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.006_DynamicRelationships.sql")
            ?? throw new InvalidOperationException("Embedded 006_DynamicRelationships.sql migration missing.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using var cmd = Command(reader.ReadToEnd(), tx);
            cmd.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", tx);
            using var result = check.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Relationship migration failed foreign-key checks. Database unchanged.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<RelationshipAttributeDefinition> AttributeDefinitions(bool activeOnly = false)
    {
        using var cmd = Command("SELECT Key,Name,Description,MinValue,MaxValue,DefaultValue,IconPath,Color,SortOrder,IsActive " +
            "FROM RelationshipAttributes WHERE ($active=0 OR IsActive=1) ORDER BY SortOrder,Name,Key");
        Add(cmd,"$active",activeOnly ? 1 : 0);
        using var r = cmd.ExecuteReader();
        var list = new List<RelationshipAttributeDefinition>();
        while(r.Read()) list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),
            r.GetInt32(4),r.GetInt32(5),r.GetString(6),r.GetString(7),r.GetInt32(8),r.GetInt32(9)==1));
        return list;
    }

    public string[] RelationshipAttributes => AttributeDefinitions(true).Select(a => a.Key).ToArray();

    public RelationshipAttributeDefinition GetAttribute(string key) =>
        AttributeDefinitions().FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Unknown relationship attribute '{key}'. Create it in Relationship Attributes first.");

    public void SaveAttribute(RelationshipAttributeDefinition item, bool isNew)
    {
        string key = (item.Key ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, "^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Key must start with a letter and contain only uppercase letters, numbers or underscores (max 48). ");
        string name = Required(item.Name,"Display name");
        if (item.Minimum < -1000 || item.Maximum > 1000 || item.Minimum >= item.Maximum ||
            item.Default < item.Minimum || item.Default > item.Maximum)
            throw new InvalidOperationException("Invalid bounds. Use -1000..1000, Min < Max, and keep Default inside the range.");
        string icon = (item.IconPath ?? "").Trim();
        if (icon.Length != 0 && (!icon.StartsWith("res://",StringComparison.Ordinal) || icon.Contains("..")))
            throw new InvalidOperationException("Icon must be an existing, safe res:// resource path, or blank.");
        string color = (item.Color ?? "").Trim();
        if (!Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidOperationException("Colour must be a six-digit hexadecimal value, e.g. #70BCE9.");
        if (!isNew && !item.IsActive && ReferencedByContent(key))
            throw new InvalidOperationException("This attribute is referenced by content. Reassign those rules before deactivating it.");
        // When narrowing ranges, reject any existing authored thresholds that would be invalidated.
        if (!isNew)
        {
            bool outside = Exists(_db,@"SELECT 1 FROM Requirements WHERE Attribute=$key AND
                ((Minimum IS NOT NULL AND (Minimum<$min OR Minimum>$max)) OR
                 (Maximum IS NOT NULL AND (Maximum<$min OR Maximum>$max)))",
                ("$key",key),("$min",item.Minimum),("$max",item.Maximum)) ||
                Exists(_db,@"SELECT 1 FROM ConditionalDialogueConditions WHERE Attribute=$key AND
                ((Minimum IS NOT NULL AND (Minimum<$min OR Minimum>$max)) OR
                 (Maximum IS NOT NULL AND (Maximum<$min OR Maximum>$max)))",
                ("$key",key),("$min",item.Minimum),("$max",item.Maximum));
            if (outside) throw new InvalidOperationException("Existing dialogue conditions lie outside the new range.");
        }
        Write(tx =>
        {
            using var cmd=Command(isNew
                ? @"INSERT INTO RelationshipAttributes(Key,Name,Description,MinValue,MaxValue,DefaultValue,IconPath,Color,SortOrder,IsActive)
                    VALUES($key,$name,$description,$min,$max,$default,$icon,$color,$sort,$active)"
                : @"UPDATE RelationshipAttributes SET Name=$name,Description=$description,MinValue=$min,MaxValue=$max,
                    DefaultValue=$default,IconPath=$icon,Color=$color,SortOrder=$sort,IsActive=$active WHERE Key=$key",tx);
            Add(cmd,"$key",key);Add(cmd,"$name",name);Add(cmd,"$description",item.Description??"");
            Add(cmd,"$min",item.Minimum);Add(cmd,"$max",item.Maximum);Add(cmd,"$default",item.Default);
            Add(cmd,"$icon",icon);Add(cmd,"$color",color);Add(cmd,"$sort",item.SortOrder);
            Add(cmd,"$active",item.IsActive?1:0);
            Changed(cmd,!isNew);
        });
    }

    private bool ReferencedByContent(string key) =>
        Exists(_db,"SELECT 1 FROM Requirements WHERE Attribute=$key LIMIT 1",("$key",key)) ||
        Exists(_db,"SELECT 1 FROM Effects WHERE Attribute=$key LIMIT 1",("$key",key)) ||
        Exists(_db,"SELECT 1 FROM ChanceModifiers WHERE Attribute=$key LIMIT 1",("$key",key)) ||
        Exists(_db,"SELECT 1 FROM ConditionalDialogueConditions WHERE Attribute=$key LIMIT 1",("$key",key)) ||
        Exists(_db,"SELECT 1 FROM CheckProfileModifiers WHERE Attribute=$key LIMIT 1",("$key",key)) ||
        Exists(_db,"SELECT 1 FROM PersonalityTraitModifiers WHERE AttributeKey=$key LIMIT 1",("$key",key));

    public void DeleteAttribute(string key)
    {
        if (ReferencedByContent(key))
            throw new InvalidOperationException("This attribute is used by a check profile, condition, choice modifier or effect. Remove those references first.");
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM RelationshipAttributes WHERE Key=$key",tx);
            Add(cmd,"$key",key);Changed(cmd,true);
        });
    }
}
