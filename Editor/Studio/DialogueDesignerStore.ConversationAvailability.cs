using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

/// <summary>
/// Studio-authored list of normal conversations. These definitions are not savegame state,
/// and do not alter the dialogue or special-encounter definitions they reference.
/// </summary>
public sealed record ConversationAvailabilityDefinition(
    int Id, string Key, string Title, int InteractionId,
    string? RecipientCharacterId, string? InitiatorCharacterId,
    int DisplayOrder, bool IsActive)
{
    public override string ToString() => $"{Title} [{Key}]" + (IsActive ? "" : " (inactive)");
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureConversationAvailabilitySchema()
    {
        if (Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='ConversationAvailability'"))
            return;
        // Only the one-time schema upgrade uses a safety backup. Routine edits do not.
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.020_ConversationAvailability.sql")
            ?? throw new InvalidOperationException("Missing embedded 020_ConversationAvailability.sql migration.");
        using var reader = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", tx);
            using var rows = check.ExecuteReader();
            if (rows.Read()) throw new InvalidOperationException("Conversation availability migration failed foreign-key checks.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<ConversationAvailabilityDefinition> ConversationAvailability()
    {
        using var cmd = Command(@"
            SELECT Id, AvailabilityKey, Title, InteractionId, RecipientCharacterId,
                   InitiatorCharacterId, DisplayOrder, IsActive
            FROM ConversationAvailability
            ORDER BY DisplayOrder, Title COLLATE NOCASE, Id");
        using var r = cmd.ExecuteReader();
        var items = new List<ConversationAvailabilityDefinition>();
        while (r.Read())
            items.Add(new ConversationAvailabilityDefinition(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.GetInt32(7) == 1));
        return items;
    }

    public MemoryConditionNode? GetConversationAvailabilityConditions(int availabilityId)
    {
        using var cmd = Command("SELECT ConditionJson FROM ConversationAvailability WHERE Id=$id");
        Add(cmd, "$id", availabilityId);
        string? json = cmd.ExecuteScalar() as string;
        return json is null ? null : MemoryConditionRules.Read(json);
    }

    public bool ConversationAvailabilityHasConditions(int availabilityId)
    {
        using var cmd = Command(
            "SELECT 1 FROM ConversationAvailability WHERE Id=$id AND ConditionJson IS NOT NULL");
        Add(cmd, "$id", availabilityId);
        return cmd.ExecuteScalar() is not null;
    }

    public void SaveConversationAvailabilityConditions(int availabilityId, MemoryConditionNode? tree)
    {
        if (!Exists(_db, "SELECT 1 FROM ConversationAvailability WHERE Id=$id", ("$id", availabilityId)))
            throw new InvalidOperationException("Save the conversation entry before editing its conditions.");
        if (tree is not null)
        {
            var memoryKeys = MemoryTypeDefinitions(true).Select(m => m.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var characters = CharacterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var statusKeys = StatusDefinitions(true).Select(s => s.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            MemoryConditionRules.Validate(tree, memoryKeys.Contains, characters.Contains, statusKeys.Contains,
                TraitDefinitions().Where(t => t.IsActive).Select(t => t.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase).Contains);
        }
        string? json = tree is null ? null : MemoryConditionRules.Write(tree);
        Write(tx =>
        {
            using var cmd = Command(
                "UPDATE ConversationAvailability SET ConditionJson=$json WHERE Id=$id", tx);
            Add(cmd, "$id", availabilityId);
            Add(cmd, "$json", json);
            Changed(cmd, update: true);
        });
    }

    public int SaveConversationAvailability(ConversationAvailabilityDefinition item)
    {
        string key = (item.Key ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, "^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Availability key must start with A-Z, using A-Z, 0-9 or _ (max 48).");
        string title = Required(item.Title, "Conversation title");
        if (title.Length > 120) throw new InvalidOperationException("Conversation title is too long (max 120).");
        if (!Exists(_db, "SELECT 1 FROM Interactions WHERE Id=$id", ("$id", item.InteractionId)))
            throw new InvalidOperationException("Choose an existing dialogue interaction.");
        string? recipient = NullIfBlank(item.RecipientCharacterId);
        string? initiator = NullIfBlank(item.InitiatorCharacterId);
        foreach (string? character in new[] { recipient, initiator })
            if (character is not null && !Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", character)))
                throw new InvalidOperationException($"Unknown character: {character}.");
        if (recipient is not null && initiator is not null &&
            string.Equals(recipient, initiator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The initiator and recipient cannot be the same character.");
        if (item.DisplayOrder is < 0 or > 10000)
            throw new InvalidOperationException("Display order must be between 0 and 10000.");
        // Validate existing tree on activation so a previously authored inactive draft
        // cannot be activated with dangling memory/status references.
        if (item.IsActive && item.Id != 0)
        {
            var tree = GetConversationAvailabilityConditions(item.Id);
            if (tree is not null)
            {
                var memories = MemoryTypeDefinitions(true).Select(m => m.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var characters = CharacterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
                var statuses = StatusDefinitions(true).Select(s => s.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                MemoryConditionRules.Validate(tree, memories.Contains, characters.Contains, statuses.Contains,
                    TraitDefinitions().Where(t => t.IsActive).Select(t => t.Key)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase).Contains);
            }
        }
        int resultId = item.Id;
        Write(tx =>
        {
            bool isNew = item.Id == 0;
            using var cmd = Command(isNew ? @"
                INSERT INTO ConversationAvailability
                    (AvailabilityKey,Title,InteractionId,RecipientCharacterId,
                     InitiatorCharacterId,DisplayOrder,IsActive)
                VALUES($key,$title,$interaction,$recipient,$initiator,$order,$active)" : @"
                UPDATE ConversationAvailability SET
                    Title=$title,InteractionId=$interaction,RecipientCharacterId=$recipient,
                    InitiatorCharacterId=$initiator,DisplayOrder=$order,IsActive=$active
                WHERE Id=$id AND AvailabilityKey=$key", tx);
            Add(cmd, "$id", item.Id); Add(cmd, "$key", key); Add(cmd, "$title", title);
            Add(cmd, "$interaction", item.InteractionId); Add(cmd, "$recipient", recipient);
            Add(cmd, "$initiator", initiator); Add(cmd, "$order", item.DisplayOrder);
            Add(cmd, "$active", item.IsActive ? 1 : 0);
            Changed(cmd, update: !isNew);
            if (isNew)
            {
                using var last = Command("SELECT last_insert_rowid()", tx);
                resultId = Convert.ToInt32((long)last.ExecuteScalar()!);
            }
        });
        return resultId;
    }

    public void DeleteConversationAvailability(int id)
    {
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM ConversationAvailability WHERE Id=$id", tx);
            Add(cmd, "$id", id);
            Changed(cmd, update: true);
        });
    }
}
