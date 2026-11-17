using System.IO;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

public sealed partial class DialogueDesignerStore
{
    public void EnsureOptionMemoryRequirementsSchema()
    {
        if (Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionMemoryRequirements'")) return;
        _backupSource.Backup(); // One-time schema upgrade; never called on routine saves.
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.017_OptionMemoryRequirements.sql")
            ?? throw new InvalidOperationException("Missing embedded Phase 13D migration.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", tx);
            using var result = check.ExecuteReader();
            if (result.Read()) throw new InvalidOperationException("Memory requirements migration failed foreign-key validation.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public MemoryConditionNode? GetMemoryRequirements(int optionId)
    {
        using var cmd = Command("SELECT ExpressionJson FROM OptionMemoryRequirements WHERE OptionId=$id");
        Add(cmd, "$id", optionId);
        string? json = cmd.ExecuteScalar() as string;
        return json is null ? null : MemoryConditionRules.Read(json);
    }

    public void SaveMemoryRequirements(int optionId, MemoryConditionNode? expression)
    {
        if (!Exists(_db, "SELECT 1 FROM Options WHERE Id=$id", ("$id", optionId)))
            throw new InvalidOperationException("Dialogue choice does not exist.");
        string? json = null;
        if (expression is not null)
        {
            var active = MemoryTypeDefinitions(true).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var characters = CharacterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var statuses = StatusDefinitions(activeOnly: true).Select(x => x.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            MemoryConditionRules.Validate(expression, active.Contains, characters.Contains,
                statuses.Contains);
            json = MemoryConditionRules.Write(expression);
        }
        Write(tx =>
        {
            if (json is null)
            {
                using var remove = Command("DELETE FROM OptionMemoryRequirements WHERE OptionId=$id", tx);
                Add(remove, "$id", optionId); remove.ExecuteNonQuery();
            }
            else
            {
                using var save = Command(@"
                    INSERT INTO OptionMemoryRequirements(OptionId,ExpressionJson)
                    VALUES($id,$json)
                    ON CONFLICT(OptionId) DO UPDATE SET ExpressionJson=excluded.ExpressionJson", tx);
                Add(save, "$id", optionId); Add(save, "$json", json);
                save.ExecuteNonQuery();
            }
        });
    }

    private IReadOnlyList<string> CharacterIds()
    {
        using var cmd = Command("SELECT Id FROM Characters ORDER BY Id");
        using var r = cmd.ExecuteReader();
        var keys = new List<string>();
        while (r.Read()) keys.Add(r.GetString(0));
        return keys;
    }

    public IReadOnlyList<string> MemoryRequirementCharacterIds() => CharacterIds();
}
