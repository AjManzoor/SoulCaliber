using System.Data;

namespace SoulCaliberStudio;

public sealed partial class DialogueDesignerStore
{
    public sealed record ReactionType(string Key, string DisplayName, string Description, int SortOrder)
    {
        public override string ToString() => $"{DisplayName}  [{Key}]";
    }

    public List<ReactionType> ReactionTypes()
    {
        using var cmd = Command("SELECT Key, DisplayName, Description, SortOrder FROM ReactionTypes ORDER BY SortOrder, DisplayName, Key;");
        using var reader = cmd.ExecuteReader();
        var result = new List<ReactionType>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return result;
    }

    public void SaveReactionType(ReactionType value, string? existingKey)
    {
        string key = Key(value.Key, "Reaction key").ToUpperInvariant();
        string name = Required(value.DisplayName, "Display name");
        if (value.SortOrder < 0) throw new InvalidOperationException("Sort order must be zero or higher.");
        if (existingKey is not null && !string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Existing reaction keys are permanent. Create a new type to change the key.");
        Write(tx =>
        {
            using var cmd = Command(existingKey is null
                ? "INSERT INTO ReactionTypes(Key,DisplayName,Description,SortOrder) VALUES($key,$name,$description,$order);"
                : "UPDATE ReactionTypes SET DisplayName=$name, Description=$description, SortOrder=$order WHERE Key=$key;", tx);
            Add(cmd,"$key",key); Add(cmd,"$name",name); Add(cmd,"$description",value.Description ?? ""); Add(cmd,"$order",value.SortOrder);
            Changed(cmd, existingKey is not null);
        });
    }

    public void DeleteReactionType(string key)
    {
        var references = ReactionTypeUsage(key);
        if (references.OptionCount + references.PortraitCount > 0)
            throw new InvalidOperationException($"Reaction '{key}' is referenced by {references.OptionCount} choice(s) and {references.PortraitCount} character portrait(s). Reassign those before deleting it.");
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM ReactionTypes WHERE Key=$key", tx);
            Add(cmd,"$key", key);
            Changed(cmd, true);
        });
    }

    public (long OptionCount, long PortraitCount) ReactionTypeUsage(string key)
    {
        using var cmd = Command("""
            SELECT
              (SELECT COUNT(*) FROM Options WHERE SuccessTargetReaction=$key OR FailureTargetReaction=$key),
              (SELECT COUNT(*) FROM ReactionImages WHERE ReactionKey=$key)
            """);
        Add(cmd,"$key",key);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }
}
