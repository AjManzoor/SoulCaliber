using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;
using System.IO;
using System.Text.RegularExpressions;

namespace SoulCaliberStudio;

/// <summary>
/// Phase 13E-1 authored definition ONLY. Godot selection and encounter completion
/// are deliberately not implemented here; they are runtime/savegame concerns.
/// </summary>
public sealed record SpecialEncounterDefinition(
    int Id, string Key, string Title, int InteractionId,
    string? RecipientCharacterId, string? InitiatorCharacterId,
    string LaunchMode, string RepetitionMode, int Priority, bool IsActive,
    string? AnchorMemoryKey, string AnchorActorRef,
    string AnchorAffectedRef, string AnchorKnowerRef)
{
    public override string ToString() => $"{Title} [{Key}]" + (IsActive ? "" : " (draft/inactive)");
}

public sealed partial class DialogueDesignerStore
{
    public void EnsureSpecialEncountersSchema()
    {
        if (!Exists(_db, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='SpecialEncounters'"))
        {
            // Existing one-time schema safety backup; routine encounter saves do not back up.
            _backupSource.Backup();
            using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
                "SoulCaliberStudio.Migrations.018_SpecialEncounters.sql")
                ?? throw new InvalidOperationException("Missing embedded 018_SpecialEncounters.sql migration.");
            using var reader = new StreamReader(stream);
            using var tx = _db.BeginTransaction();
            try
            {
                using (var cmd = Command(reader.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
                using var check = Command("PRAGMA foreign_key_check", tx);
                using var rows = check.ExecuteReader();
                if (rows.Read()) throw new InvalidOperationException("Special encounter migration failed foreign-key checks.");
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }
        EnsureOptionalEncounterRecipientSchema();
    }

    private void EnsureOptionalEncounterRecipientSchema()
    {
        bool? recipientRequired = null;
        using (var cmd = Command("PRAGMA table_info(SpecialEncounters)"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                if (string.Equals(reader.GetString(1), "RecipientCharacterId", StringComparison.OrdinalIgnoreCase))
                {
                    recipientRequired = reader.GetInt32(3) != 0;
                    break;
                }
        }
        if (recipientRequired is null)
            throw new InvalidOperationException("SpecialEncounters is missing its recipient column.");
        if (recipientRequired == false) return; // Already migrated: no backup or writes.

        // SQLite cannot DROP NOT NULL in place. Rebuild in one transaction, preserving rows.
        _backupSource.Backup();
        using var stream = typeof(DialogueDesignerStore).Assembly.GetManifestResourceStream(
            "SoulCaliberStudio.Migrations.019_SpecialEncounterAnyRecipient.sql")
            ?? throw new InvalidOperationException("Missing embedded 019_SpecialEncounterAnyRecipient.sql migration.");
        using var script = new StreamReader(stream);
        using var tx = _db.BeginTransaction();
        try
        {
            using (var cmd = Command(script.ReadToEnd(), tx)) cmd.ExecuteNonQuery();
            using var check = Command("PRAGMA foreign_key_check", tx);
            using var rows = check.ExecuteReader();
            if (rows.Read()) throw new InvalidOperationException("Optional recipient migration failed foreign-key checks.");
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public IReadOnlyList<SpecialEncounterDefinition> SpecialEncounters()
    {
        using var cmd = Command(@"
            SELECT Id, EncounterKey, Title, InteractionId, RecipientCharacterId,
                InitiatorCharacterId, LaunchMode, RepetitionMode, Priority, IsActive,
                AnchorMemoryKey, AnchorActorRef, AnchorAffectedRef, AnchorKnowerRef
            FROM SpecialEncounters ORDER BY Priority DESC, Title, Id");
        using var reader = cmd.ExecuteReader();
        var result = new List<SpecialEncounterDefinition>();
        while (reader.Read())
            result.Add(new(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetInt32(8), reader.GetInt32(9) == 1,
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11), reader.GetString(12), reader.GetString(13)));
        return result;
    }

    public MemoryConditionNode? GetSpecialEncounterConditions(int encounterId)
    {
        using var cmd = Command("SELECT ConditionJson FROM SpecialEncounters WHERE Id=$id");
        Add(cmd, "$id", encounterId);
        string? json = cmd.ExecuteScalar() as string;
        return json is null ? null : MemoryConditionRules.Read(json);
    }

    public bool SpecialEncounterHasConditions(int encounterId)
    {
        using var cmd = Command("SELECT 1 FROM SpecialEncounters WHERE Id=$id AND ConditionJson IS NOT NULL");
        Add(cmd, "$id", encounterId);
        return cmd.ExecuteScalar() is not null;
    }

    private void ValidateSpecialEncounterTree(MemoryConditionNode tree)
    {
        var activeMemories = MemoryTypeDefinitions(true).Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownCharacters = CharacterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeStatuses = StatusDefinitions(true).Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        MemoryConditionRules.Validate(tree, activeMemories.Contains,
            knownCharacters.Contains, activeStatuses.Contains);
    }

    public void SaveSpecialEncounterConditions(int encounterId, MemoryConditionNode? conditions)
    {
        if (!Exists(_db, "SELECT 1 FROM SpecialEncounters WHERE Id=$id", ("$id", encounterId)))
            throw new InvalidOperationException("Save the special encounter first.");
        if (conditions is not null) ValidateSpecialEncounterTree(conditions);
        string? json = conditions is null ? null : MemoryConditionRules.Write(conditions);
        Write(tx =>
        {
            using var cmd = Command(
                "UPDATE SpecialEncounters SET ConditionJson=$json WHERE Id=$id", tx);
            Add(cmd, "$json", json); Add(cmd, "$id", encounterId);
            Changed(cmd, update: true);
        });
    }

    public int SaveSpecialEncounter(SpecialEncounterDefinition item)
    {
        string key = (item.Key ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(key, "^[A-Z][A-Z0-9_]{0,47}$"))
            throw new InvalidOperationException("Encounter key must start with A-Z and contain only A-Z, 0-9 or _ (max 48).");
        string title = Required(item.Title, "Encounter title");
        if (title.Length > 120) throw new InvalidOperationException("Encounter title is too long (max 120).");
        if (!Exists(_db, "SELECT 1 FROM Interactions WHERE Id=$id", ("$id", item.InteractionId)))
            throw new InvalidOperationException("Choose an existing dialogue interaction.");
        string? recipient = NullIfBlank(item.RecipientCharacterId);
        string? initiator = NullIfBlank(item.InitiatorCharacterId);
        if (recipient is not null && !Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", recipient)))
            throw new InvalidOperationException("Recipient is not in the character roster.");
        if (initiator is not null && !Exists(_db, "SELECT 1 FROM Characters WHERE Id=$id", ("$id", initiator)))
            throw new InvalidOperationException("Initiator is not in the character roster.");
        if (recipient is not null && initiator is not null &&
            string.Equals(initiator, recipient, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The encounter initiator and recipient cannot be the same character.");
        if (item.LaunchMode is not ("AUTOMATIC" or "OPTIONAL"))
            throw new InvalidOperationException("Choose Automatic or Optional launch.");
        if (item.RepetitionMode is not ("ONCE_PER_PAIR" or "ONCE_PER_OCCURRENCE" or "REPEATABLE"))
            throw new InvalidOperationException("Invalid repetition mode.");
        if (item.Priority is < 0 or > 1000)
            throw new InvalidOperationException("Priority must be between 0 and 1000.");
        string? anchorKey = NullIfBlank(item.AnchorMemoryKey);
        if (item.RepetitionMode == "ONCE_PER_OCCURRENCE")
        {
            if (anchorKey is null) throw new InvalidOperationException("Once per occurrence requires an anchor memory type.");
            if (!Exists(_db, "SELECT 1 FROM MemoryTypes WHERE Key=$key AND IsActive=1", ("$key", anchorKey)))
                throw new InvalidOperationException("Anchor memory type must be active.");
        }
        else if (anchorKey is not null)
            throw new InvalidOperationException("Only per-occurrence encounters can specify an anchor memory.");
        string actorRef = item.RepetitionMode == "ONCE_PER_OCCURRENCE" ? item.AnchorActorRef : "ANY";
        string affectedRef = item.RepetitionMode == "ONCE_PER_OCCURRENCE" ? item.AnchorAffectedRef : "ANY";
        string knowerRef = item.RepetitionMode == "ONCE_PER_OCCURRENCE" ? item.AnchorKnowerRef : "ANY";
        var validCharacterIds = CharacterIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string reference in new[] { actorRef, affectedRef, knowerRef })
        {
            if (reference is "ANY" or "ACTOR" or "PARTNER") continue;
            if (!reference.StartsWith("CHAR:", StringComparison.Ordinal) || !validCharacterIds.Contains(reference[5..]))
                throw new InvalidOperationException($"Invalid anchor character reference: {reference}.");
        }
        // Keep keys immutable once used: they become stable runtime completion identifiers.
        if (item.Id != 0 && !Exists(_db,
            "SELECT 1 FROM SpecialEncounters WHERE Id=$id AND EncounterKey=$key",
            ("$id", item.Id), ("$key", key)))
            throw new InvalidOperationException("Encounter keys cannot be changed after creation.");
        if (item.IsActive)
        {
            if (item.Id == 0 || !SpecialEncounterHasConditions(item.Id))
                throw new InvalidOperationException(
                    "Create this as an inactive draft, add AND/OR/NOT eligibility conditions, then activate it.");
            var conditions = GetSpecialEncounterConditions(item.Id)!;
            ValidateSpecialEncounterTree(conditions);
        }
        int savedId = item.Id;
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0 ? @"
                INSERT INTO SpecialEncounters
                    (EncounterKey,Title,InteractionId,RecipientCharacterId,InitiatorCharacterId,
                     LaunchMode,RepetitionMode,Priority,IsActive,AnchorMemoryKey,
                     AnchorActorRef,AnchorAffectedRef,AnchorKnowerRef)
                VALUES($key,$title,$interaction,$recipient,$initiator,$launch,$repeat,$priority,
                       $active,$anchor,$actor,$affected,$knower)" : @"
                UPDATE SpecialEncounters SET Title=$title,InteractionId=$interaction,
                    RecipientCharacterId=$recipient,InitiatorCharacterId=$initiator,
                    LaunchMode=$launch,RepetitionMode=$repeat,Priority=$priority,
                    IsActive=$active,AnchorMemoryKey=$anchor,AnchorActorRef=$actor,
                    AnchorAffectedRef=$affected,AnchorKnowerRef=$knower
                WHERE Id=$id AND EncounterKey=$key", tx);
            Add(cmd, "$id", item.Id); Add(cmd, "$key", key); Add(cmd, "$title", title);
            Add(cmd, "$interaction", item.InteractionId);
            Add(cmd, "$recipient", recipient); Add(cmd, "$initiator", initiator);
            Add(cmd, "$launch", item.LaunchMode); Add(cmd, "$repeat", item.RepetitionMode);
            Add(cmd, "$priority", item.Priority); Add(cmd, "$active", item.IsActive ? 1 : 0);
            Add(cmd, "$anchor", anchorKey); Add(cmd, "$actor", actorRef);
            Add(cmd, "$affected", affectedRef); Add(cmd, "$knower", knowerRef);
            Changed(cmd, item.Id != 0);
            if (item.Id == 0)
            {
                using var last = Command("SELECT last_insert_rowid()", tx);
                savedId = Convert.ToInt32((long)last.ExecuteScalar()!);
            }
        });
        return savedId;
    }

    public void DeleteSpecialEncounter(int id)
    {
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM SpecialEncounters WHERE Id=$id", tx);
            Add(cmd, "$id", id);
            Changed(cmd, update: true);
        });
    }
}
