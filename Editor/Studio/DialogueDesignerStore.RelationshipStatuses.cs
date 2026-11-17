using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

/// <summary>
/// A directional relationship status is a label (ALLY, NEUTRAL, ENEMY...), not a
/// numerical relationship attribute (TRUST, RESPECT...). The catalogue lives in
/// master content; later phases will store defaults and savegame overrides.
/// </summary>
public sealed record RelationshipStatusDefinition(
    string Key, string Name, string Description, int SortOrder, bool IsActive, bool IsDefault,
    string IconPath, string Color)
{
    public override string ToString() => $"{Name} [{Key}]" +
        (IsDefault ? " (default)" : !IsActive ? " (inactive)" : "");
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureRelationshipStatusSchema()
    {
        if (!Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='RelationshipStatuses'"))
        {
            // One-time migration only, not a backup on every catalogue edit.
            _backupSource.Backup();
            using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
                "SoulCaliberStudio.Migrations.009_RelationshipStatuses.sql")
                ?? throw new InvalidOperationException("Missing embedded Phase 12A relationship status migration.");
            using var reader = new StreamReader(stream);
            using var tx = _db.BeginTransaction();
            try
            {
                using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
                using var check = Command("PRAGMA foreign_key_check", tx);
                using var result = check.ExecuteReader();
                if (result.Read()) throw new InvalidOperationException("Relationship status migration failed foreign-key checks.");
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }

        EnsureRelationshipStatusAppearanceSchema();

        // Fail loudly on partial/manual edits instead of silently changing the
        // master database's starting-state semantics.
        using var verify = Command("SELECT COUNT(*) FROM RelationshipStatuses WHERE IsDefault=1 AND IsActive=1");
        if (Convert.ToInt64(verify.ExecuteScalar()) != 1 ||
            !Exists(_db, "SELECT 1 FROM RelationshipStatuses WHERE Key='NEUTRAL' AND IsDefault=1 AND IsActive=1"))
            throw new InvalidOperationException(
                "Relationship status catalogue must have active NEUTRAL as its only default. Repair the master database before continuing.");
    }

    private void EnsureRelationshipStatusAppearanceSchema()
    {
        bool iconColumn = Exists(_db,
            "SELECT 1 FROM pragma_table_info('RelationshipStatuses') WHERE name='IconPath'");
        bool colorColumn = Exists(_db,
            "SELECT 1 FROM pragma_table_info('RelationshipStatuses') WHERE name='Color'");
        if (iconColumn && colorColumn) return;
        if (iconColumn != colorColumn)
            throw new InvalidOperationException(
                "RelationshipStatuses has a partial appearance migration (IconPath/Color). Repair the database before continuing.");

        // Only a schema upgrade produces a safety backup. Normal catalogue saves do not.
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.012_StatusAppearance.sql")
            ?? throw new InvalidOperationException("Missing embedded relationship status appearance migration.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", tx);
            using var result = check.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Status appearance migration failed foreign-key checks.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<RelationshipStatusDefinition> StatusDefinitions(bool activeOnly = false)
    {
        using var cmd = Command("SELECT Key,Name,Description,SortOrder,IsActive,IsDefault,IconPath,Color FROM RelationshipStatuses " +
            "WHERE ($active=0 OR IsActive=1) ORDER BY SortOrder,Name,Key");
        Add(cmd, "$active", activeOnly ? 1 : 0);
        using var r = cmd.ExecuteReader();
        var result = new List<RelationshipStatusDefinition>();
        while (r.Read())
            result.Add(new(r.GetString(0), r.GetString(1), r.GetString(2),
                r.GetInt32(3), r.GetInt32(4) == 1, r.GetInt32(5) == 1, r.GetString(6), r.GetString(7)));
        return result;
    }

    public void SaveStatus(RelationshipStatusDefinition item, bool isNew)
    {
        string key = (item.Key ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, "^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Status key must start with A-Z and use uppercase letters, numbers or underscores (max 48).");
        string name = Required(item.Name, "Status name");
        if (name.Length > 120) throw new InvalidOperationException("Status name must be at most 120 characters.");
        string description = (item.Description ?? "").Trim();
        if (description.Length > 1000) throw new InvalidOperationException("Description must be at most 1000 characters.");
        string icon = (item.IconPath ?? "").Trim();
        if (icon.Length != 0 &&
            (!icon.StartsWith("res://", StringComparison.Ordinal) || icon.Contains('\\') ||
             icon[6..].Length == 0 || icon[6..].StartsWith('/') || icon[6..].Contains(':') ||
             icon[6..].Split('/').Any(p => p is "" or "." or "..")))
            throw new InvalidOperationException("Icon must be a safe Godot res:// path using forward slashes, or blank.");
        string color = (item.Color ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(color, "^#[0-9A-F]{6}$"))
            throw new InvalidOperationException("Colour must be #RRGGBB, e.g. #8AE6A8.");
        if (key == "NEUTRAL" && !item.IsActive)
            throw new InvalidOperationException("NEUTRAL is the required fallback and cannot be disabled.");
        if (item.IsDefault != (key == "NEUTRAL"))
            throw new InvalidOperationException("For Phase 12A, NEUTRAL must remain the default status.");
        // Advanced STATUS leaves are authored as JSON, so foreign keys alone
        // cannot protect them from catalogue deactivation.
        if (!item.IsActive && AdvancedRequirementsUseStatus(key))
            throw new InvalidOperationException(
                $"Cannot deactivate {key}: an advanced dialogue condition uses this status.");

        Write(tx =>
        {
            using var cmd = Command(isNew
                ? @"INSERT INTO RelationshipStatuses(Key,Name,Description,SortOrder,IsActive,IsDefault,IconPath,Color)
                    VALUES($key,$name,$description,$sort,$active,$default,$icon,$color)"
                : @"UPDATE RelationshipStatuses SET Name=$name,Description=$description,SortOrder=$sort,
                    IsActive=$active,IconPath=$icon,Color=$color WHERE Key=$key", tx);
            Add(cmd, "$key", key);
            Add(cmd, "$name", name);
            Add(cmd, "$description", description);
            Add(cmd, "$sort", item.SortOrder);
            Add(cmd, "$icon", icon);
            Add(cmd, "$color", color);
            Add(cmd, "$active", item.IsActive ? 1 : 0);
            if (isNew) Add(cmd, "$default", item.IsDefault ? 1 : 0);
            Changed(cmd, !isNew);
        });
    }

    private bool AdvancedRequirementsUseStatus(string key)
    {
        if (!Exists(_db,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionMemoryRequirements'"))
            return false;
        using var cmd = Command("SELECT ExpressionJson FROM OptionMemoryRequirements");
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            var root = SoulCaliber.Shared.MemoryConditionRules.Read(rows.GetString(0));
            if (Uses(root)) return true;
        }
        return false;

        bool Uses(SoulCaliber.Shared.MemoryConditionNode node) =>
            (node.Kind == "STATUS" &&
             string.Equals(node.StatusKey, key, StringComparison.OrdinalIgnoreCase)) ||
            (node.Children?.Any(Uses) ?? false);
    }

    public void DeleteStatus(string key)
    {
        if (string.Equals(key, "NEUTRAL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The default NEUTRAL status cannot be deleted.");
        if (AdvancedRequirementsUseStatus(key))
            throw new InvalidOperationException(
                $"Cannot delete {key}: an advanced dialogue condition uses this status.");
        // Other direct table references remain protected by foreign keys.
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM RelationshipStatuses WHERE Key=$key AND IsDefault=0", tx);
            Add(cmd, "$key", key);
            Changed(cmd, true);
        });
    }
}
