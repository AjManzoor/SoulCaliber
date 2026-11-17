using SoulCaliber.Shared;

namespace SoulCaliberStudio;

// Phase 13F: read-only projection of published memory, choice and conversation rules.
// The Test Lab's changing history lives only in InteractionSimulatorWindow.
public sealed record SimMemoryDefinition(string Key, string Name, string Severity, string Knowledge)
{
    public override string ToString() => $"{Name} [{Key}]";
}
public sealed record SimMemoryEffect(int OptionId, string MemoryKey, string Trigger,
    string Affected, string? SeverityOverride, string? KnowledgeOverride);
public sealed record SimConversationRule(string Key, string Title, string InteractionKey, int Order,
    bool Active, string? Initiator, string? Recipient, MemoryConditionNode? Conditions);
public sealed record SimSpecialEncounter(string Key, string Title, string InteractionKey, int Priority,
    bool Active, string LaunchMode, string RepetitionMode, string? Initiator, string? Recipient,
    MemoryConditionNode? Conditions, string? AnchorMemoryKey, string AnchorActorRef,
    string AnchorAffectedRef, string AnchorKnowerRef);

public sealed partial class SimulationData
{
    private readonly List<SimMemoryDefinition> _memories = [];
    private readonly Dictionary<int, MemoryConditionNode> _optionConditions = [];
    private readonly Dictionary<int, List<SimMemoryEffect>> _memoryEffects = [];
    private readonly List<SimConversationRule> _conversations = [];
    private readonly List<SimSpecialEncounter> _encounters = [];

    public IReadOnlyList<SimMemoryDefinition> Memories => _memories;
    public IReadOnlyList<SimConversationRule> Conversations => _conversations;
    public IReadOnlyList<SimSpecialEncounter> Encounters => _encounters;
    public MemoryConditionNode? OptionConditions(int optionId) =>
        _optionConditions.GetValueOrDefault(optionId);
    public IReadOnlyList<SimMemoryEffect> MemoryEffects(int optionId) =>
        _memoryEffects.GetValueOrDefault(optionId) ?? [];

    private void LoadMemoryEncounterSimulationData()
    {
        // A stale master database should fail clearly rather than pretending no memories exist.
        foreach (string table in new[] { "MemoryTypes", "OptionMemoryEffects", "OptionMemoryRequirements",
                     "SpecialEncounters", "ConversationAvailability" })
            if (!HasTable(table)) throw new InvalidOperationException(
                $"Phase 13F requires '{table}'. Apply Studio migrations through 020 and reopen the Test Lab.");

        _memories.AddRange(Read(
            "SELECT Key,Name,DefaultSeverity,DefaultKnowledge FROM MemoryTypes WHERE IsActive=1 ORDER BY SortOrder,Name,Key",
            r => new SimMemoryDefinition(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3))));

        foreach (var row in Read("SELECT OptionId,ExpressionJson FROM OptionMemoryRequirements",
                     r => (OptionId: r.GetInt32(0), Json: r.GetString(1))))
            _optionConditions.Add(row.OptionId, MemoryConditionRules.Read(row.Json));

        foreach (var effect in Read(
            "SELECT OptionId,MemoryKey,Trigger,Affected,SeverityOverride,KnowledgeOverride FROM OptionMemoryEffects ORDER BY Id",
            r => new SimMemoryEffect(r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5))))
        {
            if (!_memoryEffects.TryGetValue(effect.OptionId, out var group))
                _memoryEffects[effect.OptionId] = group = [];
            group.Add(effect);
        }

        _conversations.AddRange(Read(@"
            SELECT a.AvailabilityKey,a.Title,i.Key,a.DisplayOrder,a.IsActive,
                   a.InitiatorCharacterId,a.RecipientCharacterId,a.ConditionJson
            FROM ConversationAvailability a JOIN Interactions i ON i.Id=a.InteractionId
            ORDER BY a.DisplayOrder,a.Id", r => new SimConversationRule(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4) != 0,
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : MemoryConditionRules.Read(r.GetString(7)))));

        _encounters.AddRange(Read(@"
            SELECT e.EncounterKey,e.Title,i.Key,e.Priority,e.IsActive,e.LaunchMode,e.RepetitionMode,
                   e.InitiatorCharacterId,e.RecipientCharacterId,e.ConditionJson,e.AnchorMemoryKey,
                   e.AnchorActorRef,e.AnchorAffectedRef,e.AnchorKnowerRef
            FROM SpecialEncounters e JOIN Interactions i ON i.Id=e.InteractionId
            ORDER BY e.Priority DESC,e.Id", r => new SimSpecialEncounter(
                r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4) != 0,
                r.GetString(5),r.GetString(6),r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : MemoryConditionRules.Read(r.GetString(9)),
                r.IsDBNull(10) ? null : r.GetString(10),r.GetString(11),r.GetString(12),r.GetString(13))));
    }
}
