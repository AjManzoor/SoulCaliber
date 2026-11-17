using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;

namespace SoulCaliber.Dialogue;

/// <summary>One selectable dialogue interaction; it is not a separate Godot scene.</summary>
public sealed record AvailableConversation(string Key, string Title, string InteractionKey,
    string Category, int SortOrder, SelectedSpecialEncounter? SpecialEncounter = null);

/// <summary>
/// An authored conversation's read-only menu diagnosis. Status is Available,
/// Not eligible, or Suppressed. Suppressed means rules pass but a higher-priority
/// mandatory encounter prevents selection.
/// </summary>
public sealed record ConversationEligibilityDetail(string Key, string Title, string InteractionKey,
    string Category, string Status, string Explanation);

public sealed record ConversationSelectionReport(
    IReadOnlyList<AvailableConversation> Available,
    IReadOnlyList<ConversationEligibilityDetail> Diagnostics,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Calculates the conversation menu and its diagnostics together from the
/// published Studio rules and current savegame. No evaluation is duplicated in
/// the menu UI. Evaluation is read-only; it cannot clear completions or memories.
/// </summary>
public sealed class ConversationAvailabilitySelector
{
    private readonly SqliteMemoryStore _memories;
    private readonly IRelationshipStatusStore _statuses;
    private readonly SpecialEncounterSelector _special;

    public ConversationAvailabilitySelector(SqliteMemoryStore memories,
        IRelationshipStatusStore statuses, SqliteSpecialEncounterCompletionStore completions)
    {
        _memories = memories;
        _statuses = statuses;
        _special = new SpecialEncounterSelector(memories, statuses, completions);
    }

    /// <summary>Existing game API; always uses the exact same evaluation as diagnostics.</summary>
    public IReadOnlyList<AvailableConversation> Select(string initiatorId, string recipientId,
        Action<string>? trace = null) => Inspect(initiatorId, recipientId, trace).Available;

    public ConversationSelectionReport Inspect(string initiatorId, string recipientId,
        Action<string>? trace = null)
    {
        var warnings = new List<string>();
        var diagnostics = new List<ConversationEligibilityDetail>();
        var available = new List<AvailableConversation>();
        var normal = new List<NormalInspection>();
        using var content = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, ForeignKeys = true
        }.ToString());
        content.Open();

        if (!HasTable(content, "ConversationAvailability"))
            warnings.Add("No ConversationAvailability table. Publish your Phase 13E-4A Studio content.");
        else
        {
            var candidates = new List<NormalCandidate>();
            using (var cmd = content.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT a.AvailabilityKey, a.Title, i.Key, a.DisplayOrder,
                           a.ConditionJson, a.IsActive, a.InitiatorCharacterId,
                           a.RecipientCharacterId
                    FROM ConversationAvailability a
                    JOIN Interactions i ON i.Id=a.InteractionId
                    ORDER BY a.DisplayOrder ASC, a.Id ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    candidates.Add(new NormalCandidate(
                        reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.GetInt32(5) == 1,
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
            if (candidates.Count == 0)
                warnings.Add("No normal conversation topics have been authored in the published database.");
            var memories = ReadKeys(content, "SELECT Key FROM MemoryTypes WHERE IsActive=1");
            var statuses = ReadKeys(content, "SELECT Key FROM RelationshipStatuses WHERE IsActive=1");
            var characters = ReadKeys(content, "SELECT Id FROM Characters");
            foreach (var item in candidates)
            {
                NormalInspection Excluded(string why) => new(item, false, why);
                if (!item.IsActive)
                {
                    normal.Add(Excluded("Inactive draft in Studio"));
                    continue;
                }
                if (item.Recipient is string requiredRecipient &&
                    !string.Equals(requiredRecipient, recipientId, StringComparison.OrdinalIgnoreCase))
                {
                    normal.Add(Excluded($"Recipient restriction: requires {requiredRecipient}, approached {recipientId}"));
                    continue;
                }
                if (item.Initiator is string requiredInitiator &&
                    !string.Equals(requiredInitiator, initiatorId, StringComparison.OrdinalIgnoreCase))
                {
                    normal.Add(Excluded($"Initiator restriction: requires {requiredInitiator}, selected {initiatorId}"));
                    continue;
                }
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.ConditionJson))
                    {
                        var tree = MemoryConditionRules.Read(item.ConditionJson);
                        MemoryConditionRules.Validate(tree, memories.Contains,
                            characters.Contains, statuses.Contains);
                        if (!MemoryConditionRules.Evaluate(tree, initiatorId, recipientId,
                            (key, actor, affected, knower) => _memories.Count(key, actor, affected, knower),
                            (from, to) => _statuses.Get(from, to), out var reason))
                        {
                            normal.Add(Excluded("Condition failed: " + reason));
                            continue;
                        }
                    }
                    normal.Add(new NormalInspection(item, true,
                        string.IsNullOrWhiteSpace(item.ConditionJson)
                            ? "No conditions; character restrictions match"
                            : "All configured conditions pass"));
                }
                catch (Exception ex)
                {
                    normal.Add(Excluded("Invalid condition configuration: " + ex.Message));
                }
            }
        }

        // Both automatic and optional encounters, including ineligible entries,
        // use the same SpecialEncounterSelector evaluation as direct launches.
        var special = _special.Inspect(initiatorId, recipientId, warning => warnings.Add(warning));
        var mandatory = special.FirstOrDefault(x => x.LaunchMode == "AUTOMATIC" && x.Eligible);
        if (mandatory is not null)
        {
            var selected = mandatory.Selection!;
            available.Add(FromSpecial(selected, "Mandatory"));
            trace?.Invoke($"Mandatory {selected.EncounterKey} takes exclusive priority.");
        }
        else
        {
            available.AddRange(normal.Where(x => x.Eligible).Select(x =>
                new AvailableConversation(x.Entry.Key, x.Entry.Title,
                    x.Entry.InteractionKey, "Normal", x.Entry.Order)));
            available.AddRange(special.Where(x => x.Eligible && x.LaunchMode == "OPTIONAL")
                .Select(x => FromSpecial(x.Selection!, "Optional")));
            available = available.OrderBy(x => x.Category == "Normal" ? 0 : 1)
                .ThenBy(x => x.SortOrder).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        foreach (var item in normal)
        {
            string status = !item.Eligible ? "Not eligible" : mandatory is null ? "Available" : "Suppressed";
            string reason = item.Eligible && mandatory is not null
                ? $"Mandatory encounter {mandatory.EncounterKey} takes priority"
                : item.Explanation;
            diagnostics.Add(new ConversationEligibilityDetail(item.Entry.Key, item.Entry.Title,
                item.Entry.InteractionKey, "Normal", status, reason));
        }
        foreach (var item in special)
        {
            bool winningMandatory = mandatory is not null && ReferenceEquals(item, mandatory);
            string category = item.LaunchMode == "AUTOMATIC" ? "Mandatory" : "Optional";
            string status = !item.Eligible ? "Not eligible"
                : mandatory is null || winningMandatory ? "Available" : "Suppressed";
            string reason = item.Eligible && status == "Suppressed"
                ? $"Mandatory encounter {mandatory!.EncounterKey} takes priority"
                : item.Explanation;
            diagnostics.Add(new ConversationEligibilityDetail(item.EncounterKey, item.Title,
                item.InteractionKey, category, status, reason));
        }
        foreach (var item in diagnostics.Where(x => x.Status == "Not eligible"))
            trace?.Invoke($"Excluded {item.Category} {item.Key}: {item.Explanation}");
        foreach (var warning in warnings)
            trace?.Invoke(warning);
        return new ConversationSelectionReport(available, diagnostics, warnings);
    }

    private static AvailableConversation FromSpecial(SelectedSpecialEncounter item, string category) =>
        new(item.EncounterKey, item.Title, item.InteractionKey, category, -item.Priority, item);

    private static bool HasTable(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
        cmd.Parameters.AddWithValue("$name", table);
        return cmd.ExecuteScalar() is not null;
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

    private sealed record NormalCandidate(string Key, string Title, string InteractionKey,
        int Order, string? ConditionJson, bool IsActive, string? Initiator, string? Recipient);
    private sealed record NormalInspection(NormalCandidate Entry, bool Eligible, string Explanation);
}
