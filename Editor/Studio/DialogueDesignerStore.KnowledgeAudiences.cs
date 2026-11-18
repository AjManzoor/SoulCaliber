using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

public sealed partial class DialogueDesignerStore
{
    /// <summary>Only creates authoring tables. Existing memory presets stay unchanged.</summary>
    public void EnsureKnowledgeAudienceSchema()
    {
        bool types = Exists(_db,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='MemoryTypeKnowledgeAudiences'");
        bool effects = Exists(_db,
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionMemoryEffectKnowledgeAudiences'");
        if (types != effects)
            throw new InvalidOperationException("Incomplete Phase 13H audience schema. Restore your master database backup.");
        if (types) return;
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.021_KnowledgeAudiences.sql")
            ?? throw new InvalidOperationException("Missing Phase 13H audience migration resource.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using var cmd = Command(reader.ReadToEnd(), tx);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public KnowledgeAudienceSpec? MemoryTypeAudience(string key)
    {
        using var cmd = Command("SELECT AudienceJson FROM MemoryTypeKnowledgeAudiences WHERE MemoryKey=$key");
        Add(cmd, "$key", key);
        var json = cmd.ExecuteScalar() as string;
        return json is null ? null : KnowledgeAudienceSpec.Parse(json);
    }

    public KnowledgeAudienceSpec? MemoryEffectAudience(int effectId)
    {
        if (effectId == 0) return null;
        using var cmd = Command("SELECT AudienceJson FROM OptionMemoryEffectKnowledgeAudiences WHERE OptionMemoryEffectId=$id");
        Add(cmd, "$id", effectId);
        var json = cmd.ExecuteScalar() as string;
        return json is null ? null : KnowledgeAudienceSpec.Parse(json);
    }

    private void ValidateAudience(KnowledgeAudienceSpec? spec)
    {
        if (spec is null) return;
        foreach (var status in spec.ActorStatusKeys.Concat(spec.AffectedStatusKeys))
            if (!Exists(_db, "SELECT 1 FROM RelationshipStatuses WHERE Key=$key AND IsActive=1", ("$key", status)))
                throw new InvalidOperationException($"Unknown or inactive audience relationship status: {status}");
        foreach (var character in spec.OtherCharacterIds.Concat(spec.ExcludedCharacterIds))
            if (!Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", character)))
                throw new InvalidOperationException($"Unknown audience character: {character}");
    }

    private void SaveMemoryTypeAudienceInTransaction(string key, KnowledgeAudienceSpec? spec,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        using var cmd = Command(spec is null
            ? "DELETE FROM MemoryTypeKnowledgeAudiences WHERE MemoryKey=$key"
            : "INSERT INTO MemoryTypeKnowledgeAudiences(MemoryKey,AudienceJson) VALUES($key,$json) " +
              "ON CONFLICT(MemoryKey) DO UPDATE SET AudienceJson=excluded.AudienceJson", tx);
        Add(cmd, "$key", key);
        if (spec is not null) Add(cmd, "$json", spec.ToJson());
        cmd.ExecuteNonQuery();
    }

    private void SaveMemoryEffectAudienceInTransaction(int effectId, KnowledgeAudienceSpec? spec,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        using var cmd = Command(spec is null
            ? "DELETE FROM OptionMemoryEffectKnowledgeAudiences WHERE OptionMemoryEffectId=$id"
            : "INSERT INTO OptionMemoryEffectKnowledgeAudiences(OptionMemoryEffectId,AudienceJson) VALUES($id,$json) " +
              "ON CONFLICT(OptionMemoryEffectId) DO UPDATE SET AudienceJson=excluded.AudienceJson", tx);
        Add(cmd, "$id", effectId);
        if (spec is not null) Add(cmd, "$json", spec.ToJson());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Studio-only preview using authored starting statuses, not game-save overrides.</summary>
    public IReadOnlyList<StatusCharacter> PreviewAudience(KnowledgeAudienceSpec spec, string actor,
        IReadOnlyCollection<string> affected)
    {
        ValidateAudience(spec);
        var characters = StatusCharacters();
        var links = new Dictionary<(string Source, string Target), string>();
        using (var cmd = Command("SELECT SourceCharacterId,TargetCharacterId,StatusKey FROM CharacterDefaultStatuses"))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) links[(r.GetString(0).ToUpperInvariant(), r.GetString(1).ToUpperInvariant())] = r.GetString(2);
        string Status(string source, string target) => links.GetValueOrDefault(
            (source.ToUpperInvariant(), target.ToUpperInvariant())) ?? "NEUTRAL";
        var ids = KnowledgeAudienceRules.Resolve(spec, actor, affected,
            characters.Select(c => c.Id).ToArray(), Status).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return characters.Where(c => ids.Contains(c.Id)).ToArray();
    }
}
