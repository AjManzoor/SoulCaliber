using SoulCaliber.Dialogue;
using SoulCaliber.Shared;

namespace SoulCaliberStudio;

// All state is process-local: never opens or writes to a SQLite savegame.
public sealed class SimulatedMemory(long id, string key, string actor, string player,
    string severity, string knowledge, IEnumerable<string> affected, IEnumerable<string> knowers)
{
    public long Id { get; } = id;
    public string Key { get; } = key;
    public string Actor { get; } = actor;
    public string Player { get; } = player;
    public string Severity { get; } = severity;
    public string Knowledge { get; } = knowledge;
    public HashSet<string> Affected { get; } = new(affected, StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Knowers { get; } = new(knowers, StringComparer.OrdinalIgnoreCase);
    public bool KnownBy(string characterId) => Knowledge == "GLOBAL" || Knowers.Contains(characterId);
    public override string ToString() => $"#{Id}  {Key} · {Actor} → {string.Join(", ", Affected)} · {Knowledge}";
}

public sealed record SimulatedEncounterCompletion(string Key, string Initiator, string Recipient, long? MemoryId);
public sealed record SimulationConversationEntry(string Key, string Title, string InteractionKey,
    string Category, string Status, string Explanation, int Order,
    SimSpecialEncounter? Encounter = null, long? AnchorMemoryId = null,
    string? BoundAffectedId = null)
{
    public bool Available => Status == "Available";
    public override string ToString() => $"{Status.ToUpperInvariant()} · {Category} · {Title} [{Key}] — {Explanation}";
}

/// <summary>
/// Equivalent content filtering to the Phase 13E Godot selectors. The AND/OR/NOT
/// evaluator is the exact same pure rules source as the game, not a new approximation.
/// </summary>
public sealed class SimulationMemoryRuntime(SimulationData data)
{
    private readonly List<SimulatedMemory> _history = [];
    private readonly List<SimulatedEncounterCompletion> _completions = [];
    private long _nextId = 1;
    public IReadOnlyList<SimulatedMemory> History => _history;
    public IReadOnlyList<SimulatedEncounterCompletion> Completions => _completions;
    // Test Lab can override the current pair using its in-memory trait checkboxes.
    public Func<string, string, bool>? TraitCheckOverride { get; set; }
    private bool TraitCheck(string characterId, string traitKey) =>
        TraitCheckOverride?.Invoke(characterId, traitKey) ?? data.AssignedTraits(characterId).Contains(traitKey);

    private static string Norm(string value) => value.Trim().ToUpperInvariant();
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public SimulatedMemory Record(string key, string actor, string player,
        IReadOnlyCollection<string> affected, string? severity, string? knowledge,
        Func<string, string, string> statusFor, string? customAudienceJson = null)
    {
        var definition = data.Memories.FirstOrDefault(x => Same(x.Key, key))
            ?? throw new InvalidOperationException($"Inactive or missing memory type: {key}.");
        string finalSeverity = severity ?? definition.Severity;
        KnowledgeAudienceSpec? customAudience = customAudienceJson is not null
            ? KnowledgeAudienceSpec.Parse(customAudienceJson)
            : knowledge is null && definition.CustomAudienceJson is not null
                ? KnowledgeAudienceSpec.Parse(definition.CustomAudienceJson) : null;
        string finalKnowledge = customAudience is not null ? "CUSTOM" : knowledge ?? definition.Knowledge;
        if (finalSeverity is not ("MAJOR" or "MINOR") ||
            finalKnowledge is not ("QUIET" or "ALLIES" or "GLOBAL" or "CUSTOM"))
            throw new InvalidOperationException("Invalid memory severity or knowledge level.");
        actor = Norm(actor); player = Norm(player);
        string[] victims = affected.Select(Norm).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var characters = data.Characters.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!characters.Contains(actor) || !characters.Contains(player) || victims.Any(v => !characters.Contains(v)))
            throw new InvalidOperationException("Memory references an unknown character.");
        var knowers = new HashSet<string>(victims, StringComparer.OrdinalIgnoreCase) { player };
        if (finalKnowledge == "CUSTOM")
        {
            knowers = KnowledgeAudienceRules.Resolve(customAudience!, actor, victims, characters, statusFor)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (knowers.Any(k => !characters.Contains(k)))
                throw new InvalidOperationException("Custom knowledge distribution references an unknown character.");
        }
        if (finalKnowledge == "ALLIES")
            foreach (var from in new[] { player }.Concat(victims).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var target in characters)
                    if (!Same(from, target) && Same(statusFor(from, target), "ALLY")) knowers.Add(target);
        var item = new SimulatedMemory(_nextId++, definition.Key, actor, player,
            finalSeverity, finalKnowledge, victims, knowers);
        _history.Add(item);
        return item;
    }

    public void Grant(long id, string character)
    {
        var item = _history.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Select a memory occurrence.");
        if (!data.Characters.Any(c => Same(c.Id, character)))
            throw new InvalidOperationException("The selected knower does not exist.");
        if (item.Knowledge != "GLOBAL") item.Knowers.Add(character);
    }

    public void Remove(long id)
    {
        _history.RemoveAll(x => x.Id == id);
        _completions.RemoveAll(x => x.MemoryId == id); // mirrors ON DELETE CASCADE
    }
    public void ResetMemories()
    {
        _history.Clear();
        _completions.Clear(); // cascade of per-occurrence completions; reset all for clean test
        _nextId = 1;
    }
    public void ResetCompletions() => _completions.Clear();

    public IReadOnlyList<SimulatedMemory> Find(string? key, string? actor,
        string? affected, string? knower) => _history.Where(m =>
        (key is null || Same(m.Key, key)) &&
        (actor is null || Same(m.Actor, actor)) &&
        (affected is null || m.Affected.Contains(affected)) &&
        (knower is null || m.KnownBy(knower))).ToList();

    public int Count(string key, string? actor, string? affected, string? knower) =>
        Find(key, actor, affected, knower).Count;

    public bool Evaluate(MemoryConditionNode conditions, string initiator, string recipient,
        Func<string, string, string> statusFor, out string failure) =>
        MemoryConditionRules.Evaluate(conditions, initiator, recipient, Count, statusFor, out failure,
            data.Characters.Select(c => c.Id), TraitCheck);

    private bool HasCompletedPair(string encounter, string initiator, string recipient) =>
        _completions.Any(c => Same(c.Key, encounter) && Same(c.Initiator, initiator) &&
            Same(c.Recipient, recipient) && c.MemoryId is null);

    private HashSet<long> HandledOccurrences(string encounter, string initiator, string recipient) =>
        _completions.Where(c => Same(c.Key, encounter) && Same(c.Initiator, initiator) &&
            Same(c.Recipient, recipient) && c.MemoryId is not null)
            .Select(c => c.MemoryId!.Value).ToHashSet();

    public bool Complete(SimulationConversationEntry entry, string initiator, string recipient)
    {
        var encounter = entry.Encounter ?? throw new InvalidOperationException("Only special encounters have completion state.");
        if (!entry.Available) throw new InvalidOperationException("Only an eligible, selected encounter can be completed.");
        if (encounter.RepetitionMode == "REPEATABLE") return false;
        long? memoryId = encounter.RepetitionMode == "ONCE_PER_OCCURRENCE"
            ? entry.AnchorMemoryId : null;
        if (encounter.RepetitionMode == "ONCE_PER_OCCURRENCE" && memoryId is null)
            throw new InvalidOperationException("Missing per-occurrence memory anchor.");
        if (_completions.Any(c => Same(c.Key, encounter.Key) && Same(c.Initiator, initiator) &&
            Same(c.Recipient, recipient) && c.MemoryId == memoryId)) return false;
        _completions.Add(new SimulatedEncounterCompletion(encounter.Key, initiator, recipient, memoryId));
        return true;
    }

    public IReadOnlyList<SimulationConversationEntry> Inspect(string initiator, string recipient,
        Func<string, string, string> statusFor)
    {
        if (Same(initiator, recipient)) throw new InvalidOperationException("Select two different characters.");
        var results = new List<SimulationConversationEntry>();
        foreach (var normal in data.Conversations)
        {
            string reason = !normal.Active ? "Inactive draft in Studio"
                : normal.Recipient is not null && !Same(normal.Recipient, recipient)
                    ? $"Recipient restriction: {normal.Recipient}"
                : normal.Initiator is not null && !Same(normal.Initiator, initiator)
                    ? $"Initiator restriction: {normal.Initiator}"
                : EvaluateSafe(normal.Conditions, initiator, recipient, statusFor);
            bool eligible = reason.Length == 0;
            results.Add(new SimulationConversationEntry(normal.Key, normal.Title, normal.InteractionKey,
                "Normal", eligible ? "Available" : "Not eligible",
                eligible ? "All requirements passed" : reason, normal.Order));
        }
        foreach (var encounter in data.Encounters)
        {
            string reason = !encounter.Active ? "Inactive draft in Studio"
                : encounter.Recipient is not null && !Same(encounter.Recipient, recipient)
                    ? $"Recipient restriction: {encounter.Recipient}"
                : encounter.Initiator is not null && !Same(encounter.Initiator, initiator)
                    ? $"Initiator restriction: {encounter.Initiator}"
                : encounter.RepetitionMode == "ONCE_PER_PAIR" && HasCompletedPair(encounter.Key, initiator, recipient)
                    ? "Already completed for this character pair"
                : encounter.Conditions is null ? "Missing eligibility conditions"
                : EvaluateSafe(encounter.Conditions, initiator, recipient, statusFor);
            long? anchor = null;
            string? boundAffected = null;
            if (reason.Length == 0)
            {
                if (encounter.AnchorMemoryKey is string anchorKey && !string.IsNullOrWhiteSpace(anchorKey))
                {
                    try
                    {
                        var matched = Find(anchorKey,
                            MemoryConditionRules.Resolve(encounter.AnchorActorRef, initiator, recipient),
                            MemoryConditionRules.Resolve(encounter.AnchorAffectedRef, initiator, recipient),
                            MemoryConditionRules.Resolve(encounter.AnchorKnowerRef, initiator, recipient));
                        var handled = encounter.RepetitionMode == "ONCE_PER_OCCURRENCE"
                            ? HandledOccurrences(encounter.Key, initiator, recipient) : new HashSet<long>();
                        foreach (var memory in matched)
                        {
                            if (handled.Contains(memory.Id)) continue;
                            var affected = MemoryAwareEncounterRules.BindAffected(
                                memory.Affected.ToList(), recipient, encounter.AffectedRelationshipStatusKey,
                                statusFor);
                            if (encounter.AffectedRelationshipStatusKey is not null && affected is null)
                                continue;
                            anchor = memory.Id;
                            boundAffected = affected;
                            break;
                        }
                        if (anchor is null) reason = encounter.AffectedRelationshipStatusKey is null
                            ? "No qualifying unhandled anchored memory occurrence"
                            : $"No anchored incident with Recipient → Affected = {encounter.AffectedRelationshipStatusKey}";
                    }
                    catch (Exception ex) { reason = "Invalid anchor: " + ex.Message; }
                }
                else if (encounter.RepetitionMode == "ONCE_PER_OCCURRENCE" ||
                         encounter.AffectedRelationshipStatusKey is not null)
                    reason = "Encounter requires a configured memory anchor";
            }
            if (reason.Length == 0 && encounter.RepetitionMode is not
                ("ONCE_PER_OCCURRENCE" or "ONCE_PER_PAIR" or "REPEATABLE"))
                reason = "Invalid repetition rule";
            bool eligible = reason.Length == 0;
            results.Add(new SimulationConversationEntry(encounter.Key, encounter.Title,
                encounter.InteractionKey, encounter.LaunchMode == "AUTOMATIC" ? "Mandatory" : "Optional",
                eligible ? "Available" : "Not eligible",
                eligible ? (anchor is long id
                    ? $"Eligible occurrence #{id}" + (boundAffected is null ? "" : $" · Affected: {data.Name(boundAffected)}")
                    : "All requirements passed") : reason,
                -encounter.Priority, encounter, anchor, boundAffected));
        }
        var mandatory = results.FirstOrDefault(r => r.Category == "Mandatory" && r.Available);
        if (mandatory is not null)
            for (int i = 0; i < results.Count; i++)
                if (results[i].Available && !ReferenceEquals(results[i], mandatory))
                    results[i] = results[i] with
                    {
                        Status = "Suppressed", Explanation = $"Mandatory encounter {mandatory.Key} takes priority"
                    };
        return results;
    }

    private string EvaluateSafe(MemoryConditionNode? conditions, string actor, string partner,
        Func<string, string, string> statusFor)
    {
        if (conditions is null) return "";
        try
        {
            var memories = data.Memories.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var statuses = data.Statuses.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var characters = data.Characters.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            MemoryConditionRules.Validate(conditions, memories.Contains, characters.Contains, statuses.Contains,
                data.Traits.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase).Contains);
            return Evaluate(conditions, actor, partner, statusFor, out var failure) ? "" : "Condition failed: " + failure;
        }
        catch (Exception ex) { return "Invalid condition configuration: " + ex.Message; }
    }

    public string CheckChoiceConditions(MemoryConditionNode? tree, string actor, string partner,
        Func<string, string, string> statusFor) => EvaluateSafe(tree, actor, partner, statusFor);
}
