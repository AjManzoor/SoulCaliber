using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;

namespace SoulCaliber.Dialogue;

/// <summary>A selected special encounter and its optional per-occurrence memory anchor.</summary>
public sealed record SelectedSpecialEncounter(
    string EncounterKey, string Title, string InteractionKey, int Priority,
    string RepetitionMode, long? AnchorMemoryOccurrenceId,
    // Snapshot the directional participants at selection time. The scene must
    // not complete an encounter against a different pair later on.
    string InitiatorCharacterId, string RecipientCharacterId);

/// <summary>
/// Read-only eligibility information for an authored special encounter.
/// Selection and diagnostics deliberately use the same evaluation path.
/// </summary>
public sealed record SpecialEncounterInspection(
    string EncounterKey, string Title, string InteractionKey, string LaunchMode,
    int Priority, bool Eligible, string Explanation,
    SelectedSpecialEncounter? Selection = null);

public sealed class SpecialEncounterSelector
{
    private readonly SqliteMemoryStore _memories;
    private readonly IRelationshipStatusStore _statuses;
    private readonly SqliteSpecialEncounterCompletionStore _completions;

    public SpecialEncounterSelector(SqliteMemoryStore memories, IRelationshipStatusStore statuses,
        SqliteSpecialEncounterCompletionStore completions)
    {
        _memories = memories ?? throw new ArgumentNullException(nameof(memories));
        _statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
        _completions = completions ?? throw new ArgumentNullException(nameof(completions));
    }

    /// <summary>For direct scene launches, choose only the top automatic encounter.</summary>
    public SelectedSpecialEncounter? Select(string initiatorId, string recipientId,
        Action<string>? trace = null) => FindEligible(initiatorId, recipientId, "AUTOMATIC", trace).FirstOrDefault();

    /// <summary>Eligible encounters sorted by descending priority, then authored Id.</summary>
    public IReadOnlyList<SelectedSpecialEncounter> FindEligible(string initiatorId,
        string recipientId, string launchMode, Action<string>? trace = null)
    {
        if (launchMode is not ("AUTOMATIC" or "OPTIONAL"))
            throw new ArgumentException("Unknown encounter launch mode.", nameof(launchMode));

        var inspections = Inspect(initiatorId, recipientId, trace);
        foreach (var item in inspections.Where(x => x.LaunchMode == launchMode))
            trace?.Invoke(item.Eligible
                ? $"Eligible {item.EncounterKey}: {item.Explanation}"
                : $"Skipped {item.EncounterKey}: {item.Explanation}");
        return inspections.Where(x => x.LaunchMode == launchMode && x.Eligible)
            .Select(x => x.Selection!).ToList();
    }

    /// <summary>
    /// Inspect ALL authored special encounters, including inactive and wrong-character
    /// entries, without altering eligibility or encounter completion state.
    /// </summary>
    public IReadOnlyList<SpecialEncounterInspection> Inspect(string initiatorId,
        string recipientId, Action<string>? trace = null)
    {
        if (string.IsNullOrWhiteSpace(initiatorId) || string.IsNullOrWhiteSpace(recipientId))
            throw new ArgumentException("Both participants are required to select an encounter.");
        if (string.Equals(initiatorId, recipientId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An encounter requires two different characters.");

        initiatorId = initiatorId.Trim();
        recipientId = recipientId.Trim();
        using var content = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, ForeignKeys = true
        }.ToString());
        content.Open();
        using (var exists = content.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='SpecialEncounters'";
            if (exists.ExecuteScalar() is null)
            {
                trace?.Invoke("No SpecialEncounters table in published content. Publish from Studio.");
                return Array.Empty<SpecialEncounterInspection>();
            }
        }

        var candidates = new List<Candidate>();
        using (var cmd = content.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT e.Id, e.EncounterKey, e.Title, i.Key, e.Priority,
                       e.RepetitionMode, e.ConditionJson, e.AnchorMemoryKey,
                       e.AnchorActorRef, e.AnchorAffectedRef, e.AnchorKnowerRef,
                       e.LaunchMode, e.IsActive, e.RecipientCharacterId, e.InitiatorCharacterId
                FROM SpecialEncounters e
                JOIN Interactions i ON i.Id=e.InteractionId
                ORDER BY e.Priority DESC, e.Id ASC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                candidates.Add(new Candidate(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt32(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8), reader.GetString(9), reader.GetString(10),
                    reader.GetString(11), reader.GetInt32(12) == 1,
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        // Load validation catalogues once, only after closing the row reader.
        var memoryKeys = ReadKeys(content, "SELECT Key FROM MemoryTypes WHERE IsActive=1");
        var statusKeys = ReadKeys(content, "SELECT Key FROM RelationshipStatuses WHERE IsActive=1");
        var characterKeys = ReadKeys(content, "SELECT Id FROM Characters");
        var result = new List<SpecialEncounterInspection>();
        foreach (var encounter in candidates)
        {
            SpecialEncounterInspection Excluded(string reason) =>
                new(encounter.Key, encounter.Title, encounter.InteractionKey,
                    encounter.LaunchMode, encounter.Priority, false, reason);

            if (!encounter.IsActive)
            {
                result.Add(Excluded("Inactive draft in Studio"));
                continue;
            }
            if (encounter.RecipientCharacterId is string specifiedRecipient &&
                !string.Equals(specifiedRecipient, recipientId, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(Excluded($"Recipient restriction: requires {specifiedRecipient}, approached {recipientId}"));
                continue;
            }
            if (encounter.InitiatorCharacterId is string specifiedInitiator &&
                !string.Equals(specifiedInitiator, initiatorId, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(Excluded($"Initiator restriction: requires {specifiedInitiator}, selected {initiatorId}"));
                continue;
            }
            try
            {
                if (encounter.RepetitionMode == "ONCE_PER_PAIR" &&
                    _completions.HasCompletedPair(encounter.Key, initiatorId, recipientId))
                {
                    result.Add(Excluded("Already completed once for this character pair"));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(encounter.ConditionJson))
                {
                    result.Add(Excluded("Missing eligibility conditions"));
                    continue;
                }
                var tree = MemoryConditionRules.Read(encounter.ConditionJson);
                MemoryConditionRules.Validate(tree, memoryKeys.Contains,
                    characterKeys.Contains, statusKeys.Contains);
                if (!MemoryConditionRules.Evaluate(tree, initiatorId, recipientId,
                    (key, actor, affected, knower) => _memories.Count(key, actor, affected, knower),
                    (from, to) => _statuses.Get(from, to), out string reason))
                {
                    result.Add(Excluded("Condition failed: " + reason));
                    continue;
                }

                long? anchorId = null;
                if (encounter.RepetitionMode == "ONCE_PER_OCCURRENCE")
                {
                    if (string.IsNullOrWhiteSpace(encounter.AnchorMemoryKey))
                    {
                        result.Add(Excluded("No per-occurrence memory anchor configured"));
                        continue;
                    }
                    var matching = _memories.Find(encounter.AnchorMemoryKey,
                        MemoryConditionRules.Resolve(encounter.AnchorActorRef, initiatorId, recipientId),
                        MemoryConditionRules.Resolve(encounter.AnchorAffectedRef, initiatorId, recipientId),
                        MemoryConditionRules.Resolve(encounter.AnchorKnowerRef, initiatorId, recipientId));
                    var handled = _completions.CompletedOccurrenceIds(encounter.Key, initiatorId, recipientId);
                    anchorId = matching.FirstOrDefault(m => !handled.Contains(m.Id))?.Id;
                    if (anchorId is null)
                    {
                        result.Add(Excluded("No unhandled matching memory occurrence (all handled or none found)"));
                        continue;
                    }
                }
                else if (encounter.RepetitionMode is not ("ONCE_PER_PAIR" or "REPEATABLE"))
                {
                    result.Add(Excluded("Unknown repetition rule: " + encounter.RepetitionMode));
                    continue;
                }

                var selected = new SelectedSpecialEncounter(encounter.Key, encounter.Title,
                    encounter.InteractionKey, encounter.Priority, encounter.RepetitionMode, anchorId,
                    initiatorId, recipientId);
                result.Add(new SpecialEncounterInspection(encounter.Key, encounter.Title,
                    encounter.InteractionKey, encounter.LaunchMode, encounter.Priority, true,
                    anchorId is long memoryId
                        ? $"All requirements pass; unhandled memory occurrence #{memoryId}"
                        : "All requirements pass; not previously completed under its repetition rule",
                    selected));
            }
            catch (Exception ex)
            {
                // Corrupt/stale conditions always fail closed, including NOT branches.
                result.Add(Excluded("Invalid encounter configuration: " + ex.Message));
            }
        }
        return result;
    }

    private static HashSet<string> ReadKeys(SqliteConnection connection, string sql)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) keys.Add(reader.GetString(0));
        return keys;
    }

    private sealed record Candidate(long Id, string Key, string Title,
        string InteractionKey, int Priority, string RepetitionMode,
        string? ConditionJson, string? AnchorMemoryKey,
        string AnchorActorRef, string AnchorAffectedRef, string AnchorKnowerRef,
        string LaunchMode, bool IsActive, string? RecipientCharacterId, string? InitiatorCharacterId);
}
