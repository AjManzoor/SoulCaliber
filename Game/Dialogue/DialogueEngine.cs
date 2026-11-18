using System;
using System.Collections.Generic;
using System.Linq;
using SoulCaliber.Shared;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Godot-independent simulation rules. A different Godot scene, game map, or
/// SQLite repository can reuse this engine without changing its dialogue tree.
/// </summary>
public sealed class DialogueEngine
{
    private readonly DialogueRepository _data;
    private readonly IRelationshipStore _relationships;
    private readonly IRelationshipStatusStore? _statuses;
    private readonly IInteractionEffectStore? _effectStore;
    private readonly SqliteMemoryStore? _memories;
    private readonly Random _random = new();
    private readonly Dictionary<string, DialogueNode> _nodes;
    private readonly ChoiceResolutionGuard<PreparedChoice, DialogueResolution> _choiceExecution = new();

    // This is the resolved fact of an attempted choice; it is calculated ONCE,
    // before any SQLite write. A failed commit retries this same prepared result.
    private sealed record PreparedChoice(
        DialogueOption Option, CharacterLine ActorLine, CharacterLine RecipientLine,
        string? SelectedTargetId,
        bool Succeeded, int Roll, int Chance,
        IReadOnlyList<RelationshipEffect> RelationshipEffects,
        IReadOnlyList<RelationshipStatusEffect> StatusEffects,
        IReadOnlyList<ResolvedPairStatusEffect> PairStatusEffects,
        IReadOnlyList<ResolvedMemoryEffect> MemoryEffects,
        string TargetReaction, string? NextNodeKey);

    private string? _memoryActorName;
    private string? _boundAffectedName;
    private string? _selectedTargetId;
    private string? _selectedTargetName;
    public string? SelectedTargetId => _selectedTargetId;
    public string CharacterName(string characterId) => _data.Character(characterId).Name;

    /// <summary>Finds ONE selectable character per matching effective directional status.</summary>
    public IReadOnlyList<CharacterDefinition> DynamicTargetsForOption(string optionKey)
    {
        var option=CurrentNode.Options.FirstOrDefault(o=>
            string.Equals(o.Key,optionKey,StringComparison.OrdinalIgnoreCase));
        if(option?.DynamicTarget is not DynamicTargetRule rule ||
           (rule.SelectionMode=="GROUP" && _statuses is null))
            return Array.Empty<CharacterDefinition>();
        return DynamicTargetRules.Find(rule,Visitor.Id,Host.Id,_data.CharacterIds,
                (from,to)=>_statuses?.Get(from,to) ?? "NEUTRAL")
            .Select(_data.Character)
            .OrderBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).ToList();
    }


    /// <summary>Bind only the incident selected by the Special Encounter selector.</summary>
    public void BindMemoryOccurrence(MemoryOccurrence occurrence, string? boundAffectedCharacterId)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (_choiceExecution.PendingOptionKey is not null || _choiceExecution.HasCommittedChoice)
            throw new InvalidOperationException("Memory context cannot change during choice resolution.");
        _memoryActorName = _data.Character(occurrence.ActorCharacterId).Name;
        _boundAffectedName = boundAffectedCharacterId is null ? null :
            occurrence.AffectedCharacterIds.Any(x => string.Equals(x, boundAffectedCharacterId,
                StringComparison.OrdinalIgnoreCase))
                ? _data.Character(boundAffectedCharacterId).Name
                : throw new InvalidOperationException("The bound affected character is not part of this incident.");
    }

    public string FormatDialogueText(string? text, string? targetName = null)
    {
        string formatted=MemoryDialogueFormatter.Format(text,Visitor.Name,Host.Name,_memoryActorName,_boundAffectedName);
        formatted = DynamicTargetRules.FormatTarget(formatted, targetName ?? _selectedTargetName);
        return formatted;
    }

    private CharacterLine FormatLine(CharacterLine line, string? targetName = null) => new()
    {
        CharacterId = line.CharacterId,
        InteractionKey = line.InteractionKey,
        NodeKey = line.NodeKey,
        OptionKey = line.OptionKey,
        VisitorId = line.VisitorId,
        Outcome = line.Outcome,
        Text = FormatDialogueText(line.Text,targetName),
        ImagePath = line.ImagePath
    };

    public string? PendingChoiceKey => _choiceExecution.PendingOptionKey;
    public bool HasCommittedChoice => _choiceExecution.HasCommittedChoice;

    public InteractionDefinition Definition { get; }
    public CharacterDefinition Visitor { get; }  // left / actor / makes choices
    public CharacterDefinition Host { get; }     // right / partner / responds
    public DialogueNode CurrentNode { get; private set; }

    public RelationshipStats ActorToPartner => _relationships.Get(Visitor.Id, Host.Id);
    public string ActorToPartnerStatus => _statuses?.Get(Visitor.Id, Host.Id) ?? "NEUTRAL";
    public string PartnerToActorStatus => _statuses?.Get(Host.Id, Visitor.Id) ?? "NEUTRAL";
    public RelationshipStats PartnerToActor => _relationships.Get(Host.Id, Visitor.Id);
    public PersonalityStats ActorPersonality => _data.Personality(Visitor.Id);
    public PersonalityStats PartnerPersonality => _data.Personality(Host.Id);

    // All three IDs are supplied by the caller. No participant is stored on an
    // interaction definition, and no fallback to the legacy HostCharacterId exists.
    public DialogueEngine(DialogueRepository data, IRelationshipStore relationships,
        string interactionKey, string actorCharacterId, string partnerCharacterId,
        IRelationshipStatusStore? statusStore = null,
        IInteractionEffectStore? effectStore = null,
        SqliteMemoryStore? memoryStore = null)
    {
        if (string.IsNullOrWhiteSpace(interactionKey))
            throw new ArgumentException("Interaction key is required.", nameof(interactionKey));
        if (string.IsNullOrWhiteSpace(actorCharacterId))
            throw new ArgumentException("Actor character ID is required.", nameof(actorCharacterId));
        if (string.IsNullOrWhiteSpace(partnerCharacterId))
            throw new ArgumentException("Partner character ID is required. Set PartnerCharacterId on the scene or pass it through InteractionLauncher.", nameof(partnerCharacterId));

        _data = data;
        _relationships = relationships;
        _statuses = statusStore;
        _effectStore = effectStore;
        _memories = memoryStore;
        Definition = data.Interaction(interactionKey.Trim());
        Visitor = data.Character(actorCharacterId.Trim());
        Host = data.Character(partnerCharacterId.Trim());
        if (string.Equals(Visitor.Id, Host.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An interaction must have two different characters.");
        _nodes = Definition.Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
        CurrentNode = _nodes[Definition.StartNodeKey];
    }

    public void Restart()
    {
        _choiceExecution.Clear();
        CurrentNode = _nodes[Definition.StartNodeKey];
        _selectedTargetId=null;
        _selectedTargetName=null;
    }

    public IReadOnlyList<AvailableOption> GetOptions() => CurrentNode.Options
        .Select(option =>
        {
            string lockReason = LockReason(option);
            if(lockReason.Length==0 && option.DynamicTarget is not null && DynamicTargetsForOption(option.Key).Count==0)
                lockReason="No characters currently match this target selection rule";
            bool locked = lockReason.Length != 0;
            // A hidden choice is absent from both the UI and the engine's selectable list.
            if (locked && option.HideWhenRequirementsUnmet) return null;
            return new AvailableOption(option, Chance(option), locked, lockReason);
        })
        .Where(option => option is not null)
        .Select(option => option!)
        .ToList();

    public DialogueResolution Choose(string optionKey) =>
        _choiceExecution.Execute(CurrentNode.Key, optionKey,
            () => PrepareChoice(optionKey), CommitChoice);

    private PreparedChoice PrepareChoice(string selectedKey)
    {
        // Composite selection key comes from a runtime-generated list, never the authoring database.
        int separator=selectedKey.IndexOf("::",StringComparison.Ordinal);
        string optionKey=separator<0 ? selectedKey : selectedKey[..separator];
        string? targetId=separator<0 ? null : selectedKey[(separator+2)..];
        var available = GetOptions().FirstOrDefault(o =>
            string.Equals(o.Definition.Key, optionKey, StringComparison.OrdinalIgnoreCase));
        if (available is null || available.IsLocked)
            throw new InvalidOperationException($"Option '{optionKey}' does not exist or is locked.");

        int roll = _random.Next(1, 101);
        bool succeeded = roll <= available.Chance;
        DialogueOption option = available.Definition;
        if(option.DynamicTarget is not null)
        {
            if(string.IsNullOrWhiteSpace(targetId) || !DynamicTargetsForOption(optionKey)
                .Any(x=>string.Equals(x.Id,targetId,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The selected relationship target is no longer eligible.");
        }
        else if(targetId is not null)
            throw new InvalidOperationException("This option does not offer dynamic target selection.");
        string? effectiveTargetId=targetId??_selectedTargetId;
        string? effectiveTargetName=effectiveTargetId is null ? null : _data.Character(effectiveTargetId).Name;

        // Resolve wording/image against BEFORE-changes relationship state.
        var actorLine = FormatLine(_data.PlayerLine(Definition.Key, CurrentNode, option,
            Visitor.Id, Host.Id, succeeded, ActorToPartner, PartnerToActor),effectiveTargetName);
        var partnerLine = FormatLine(_data.HostLine(Definition.Key, CurrentNode, option, Host.Id, Visitor.Id, succeeded, ActorToPartner, PartnerToActor),effectiveTargetName);
        var effects = succeeded ? option.SuccessEffects : option.FailureEffects;
        // Traits belong to the character whose outgoing relationship changes.
        // Apply them after the roll, before persistence; preserve pre-effect dialogue selection.
        var adjustedEffects = effects.Select(effect =>
        {
            var (receiverId, _) = RelationshipDirection.Resolve(effect.Direction, Visitor.Id, Host.Id);
            var adjustment = _data.AdjustRelationshipEffect(receiverId, effect.Attribute, effect.Delta, succeeded);
            return new RelationshipEffect
            {
                Direction = effect.Direction, Attribute = effect.Attribute, Delta = adjustment.AdjustedDelta
            };
        }).ToList();
        // Distinct discrete status changes are NOT personality-adjusted and do not
        // influence the roll. Author them per SUCCESS or FAILURE in Studio.
        var statusEffects = succeeded ? option.SuccessStatusEffects : option.FailureStatusEffects;
        if (statusEffects.Count > 0 && _statuses is null)
            throw new InvalidOperationException("Relationship status effects require an IRelationshipStatusStore.");
        // Resolve all pair targets BEFORE writing; never mutate an entire relationship group.
        var pairEffects = succeeded ? option.SuccessPairStatusEffects : option.FailurePairStatusEffects;
        var resolvedPairs = DynamicTargetRules.ResolvePairEffects(
            pairEffects.Select(e => new DynamicPairStatusEffect(
                e.FromRef, e.ToRef, e.StatusKey, e.ReverseStatusKey)),
            Visitor.Id, Host.Id, effectiveTargetId, id => _data.Character(id).Id)
            .Select(e => new ResolvedPairStatusEffect(
                e.FromCharacterId, e.ToCharacterId, e.StatusKey)).ToList();
        if(resolvedPairs.Count>0 && _statuses is null)
            throw new InvalidOperationException("Third-party status effects require a status store.");

        // Resolve memory targets and triggers BEFORE writing anything. The unit of
        // work saves every fired occurrence after status changes in one transaction.
        var memoryEffects = new List<ResolvedMemoryEffect>();
        foreach (var memory in option.MemoryEffects)
        {
            bool fires = memory.Trigger switch
            {
                "SELECTED" or "BOTH" => true,
                "SUCCESS" => succeeded,
                "FAILURE" => !succeeded,
                _ => throw new InvalidOperationException($"Invalid memory trigger '{memory.Trigger}'.")
            };
            if (!fires) continue;
            string[] affected = memory.Affected switch
            {
                "ACTOR" => new[] { Visitor.Id },
                "PARTNER" => new[] { Host.Id },
                "BOTH" => new[] { Visitor.Id, Host.Id },
                _ => throw new InvalidOperationException($"Invalid affected target '{memory.Affected}'.")
            };
            memoryEffects.Add(new ResolvedMemoryEffect(memory.MemoryKey, affected,
                memory.SeverityOverride, memory.KnowledgeOverride, memory.CustomAudienceJson));
        }

        string targetReaction = succeeded
            ? option.SuccessTargetReaction
            : option.FailureTargetReaction;
        if (string.IsNullOrWhiteSpace(targetReaction))
            targetReaction = succeeded ? "RECEPTIVE" : "DISMISSIVE";

        return new PreparedChoice(option, actorLine, partnerLine, targetId, succeeded, roll,
            available.Chance, adjustedEffects, statusEffects.ToList(), resolvedPairs, memoryEffects,
            targetReaction, succeeded ? option.SuccessNextNodeKey : option.FailureNextNodeKey);
    }

    private DialogueResolution CommitChoice(PreparedChoice prepared)
    {
        IReadOnlyList<AppliedRelationshipChange> changes;
        IReadOnlyList<AppliedRelationshipStatusChange> statusChanges;
        if (_effectStore is IInteractionOutcomeStore outcomeStore)
        {
            // Includes interaction history even when the option has no effects.
            var committed = outcomeStore.CommitResolvedChoice(new ResolvedChoiceCommit(
                Definition.Key, CurrentNode.Key, prepared.Option.Key,
                Visitor.Id, Host.Id, prepared.Succeeded, prepared.Roll, prepared.Chance, prepared.TargetReaction,
                prepared.RelationshipEffects, prepared.StatusEffects, prepared.MemoryEffects,
                prepared.PairStatusEffects));
            changes = committed.RelationshipChanges;
            statusChanges = committed.StatusChanges;
        }
        else
        {
            // Preserve pure/non-SQLite consumer support without permitting partially
            // committed memories or mixed numeric/status effects.
            if (prepared.MemoryEffects.Count > 0 || prepared.PairStatusEffects.Count > 0)
                throw new InvalidOperationException("Memory and third-party status effects require an atomic IInteractionOutcomeStore.");
            if (_effectStore is not null)
            {
                var committed = _effectStore.Apply(Visitor.Id, Host.Id, prepared.RelationshipEffects, prepared.StatusEffects);
                changes = committed.RelationshipChanges;
                statusChanges = committed.StatusChanges;
            }
            else
            {
                if (prepared.RelationshipEffects.Count > 0 && prepared.StatusEffects.Count > 0)
                    throw new InvalidOperationException(
                        "Mixed numeric and status effects require an atomic IInteractionEffectStore.");
                changes = _relationships.Apply(Visitor.Id, Host.Id, prepared.RelationshipEffects);
                statusChanges = _statuses?.Apply(Visitor.Id, Host.Id, prepared.StatusEffects)
                    ?? Array.Empty<AppliedRelationshipStatusChange>();
            }
        }

        // Bind after successful persistence only. The next dialogue node can reuse {target}.
        if(prepared.SelectedTargetId is not null)
        {
            _selectedTargetId=prepared.SelectedTargetId;
            _selectedTargetName=_data.Character(prepared.SelectedTargetId).Name;
        }
        return new DialogueResolution(prepared.Option, prepared.ActorLine, prepared.RecipientLine,
            prepared.Succeeded, prepared.Roll, prepared.Chance, changes, statusChanges,
            prepared.NextNodeKey, prepared.TargetReaction);
    }

    public bool Advance(string? nextNodeKey)
    {
        if (string.IsNullOrWhiteSpace(nextNodeKey)) return false;
        var nextNode = _nodes[nextNodeKey];
        _choiceExecution.Clear();
        CurrentNode = nextNode;
        return true;
    }

    public string Introduction() => FormatDialogueText(_data.Introduction(
        Definition.Key, CurrentNode, Host.Id, Visitor.Id, ActorToPartner, PartnerToActor));

    private string LockReason(DialogueOption option)
    {
        foreach (var req in option.Requirements)
        {
            int value = GetDirectionalStats(req.Direction)
                .Get(req.Attribute);
            if (req.Minimum is int min && value < min)
                return $"{req.Attribute} ≥ {min}";
            if (req.Maximum is int max && value > max)
                return $"{req.Attribute} ≤ {max}";
        }
        foreach (var req in option.StatusRequirements)
        {
            if (_statuses is null)
                return "Relationship status unavailable"; // Fail closed; never accidentally unlock.
            string expected = req.StatusKey;
            switch (req.Direction.ToUpperInvariant())
            {
                case "ACTOR_TO_PARTNER":
                    if (!string.Equals(ActorToPartnerStatus, expected, StringComparison.OrdinalIgnoreCase))
                        return $"{Visitor.Name} → {Host.Name} must be {expected}";
                    break;
                case "PARTNER_TO_ACTOR":
                    if (!string.Equals(PartnerToActorStatus, expected, StringComparison.OrdinalIgnoreCase))
                        return $"{Host.Name} → {Visitor.Name} must be {expected}";
                    break;
                case "BOTH":
                    if (!string.Equals(ActorToPartnerStatus, expected, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(PartnerToActorStatus, expected, StringComparison.OrdinalIgnoreCase))
                        return $"Both directions must be {expected}";
                    break;
                default:
                    throw new InvalidOperationException($"Invalid status-requirement direction '{req.Direction}'.");
            }
        }
        if (option.MemoryRequirements is not null)
        {
            // A missing service must fail closed, including NOT checks: otherwise
            // missing memory history could accidentally unlock a secret choice.
            if (_memories is null && MemoryConditionRules.ContainsKind(option.MemoryRequirements, "MEMORY"))
                return "Memory history unavailable";
            if (_statuses is null && (MemoryConditionRules.ContainsKind(option.MemoryRequirements, "STATUS") ||
                MemoryConditionRules.ContainsKind(option.MemoryRequirements, "RELATIONSHIP") ||
                MemoryConditionRules.ContainsKind(option.MemoryRequirements, "RELATIONSHIP_COUNT") ||
                MemoryConditionRules.ContainsKind(option.MemoryRequirements, "RELATIONSHIP_GROUP")))
                return "Relationship status unavailable";
            if (!MemoryConditionRules.Evaluate(option.MemoryRequirements, Visitor.Id, Host.Id,
                (memory, actor, affected, knower) => _memories!.Count(memory, actor, affected, knower),
                (from, to) => _statuses!.Get(from, to),
                out string conditionFailure, _data.CharacterIds, _data.HasTrait))
                return conditionFailure;
        }
        return "";
    }

    // A skill check belongs to a specific interaction/node/option, not to the UI.
    // If there is no check, the existing probability calculation is unchanged.
    public string SkillLabel(DialogueOption option)
    {
        var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
        return check is null ? "" : check.ActorSkill;
    }

    public string ChanceBreakdown(DialogueOption option)
    {
        var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
        string profileKey = _data.OptionProfile(Definition.Key, CurrentNode.Key, option.Key);
        ChanceResult chance = CalculateChance(option);
        int? actorScore = check is null ? null : ActorPersonality.Get(check.ActorSkill);
        int? defenderScore = check is null ? null : PartnerPersonality.Get(check.DefenderSkill);
        return ChanceFormula.Explain(chance, option.BaseSuccessChance, profileKey,
            actorScore, defenderScore, check?.ActorFactor ?? 0, check?.DefenderFactor ?? 0);
    }

    private int Chance(DialogueOption option) => CalculateChance(option).FinalChance;

    private ChanceResult CalculateChance(DialogueOption option)
    {
        var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
        string profileKey = _data.OptionProfile(Definition.Key, CurrentNode.Key, option.Key);
        var profile = _data.CheckProfile(profileKey);
        var terms = option.ChanceModifiers.Select(m =>
            new ChanceFactor(m.Direction, m.Attribute, m.Factor));
        return ChanceFormula.Calculate(option.BaseSuccessChance,
            check is null ? null : ActorPersonality.Get(check.ActorSkill),
            check is null ? null : PartnerPersonality.Get(check.DefenderSkill),
            check?.ActorFactor ?? 0, check?.DefenderFactor ?? 0,
            profile, terms,
            (direction, attribute) => GetDirectionalStats(direction)
                .Get(attribute));
    }

    private RelationshipStats GetDirectionalStats(string direction) =>
        direction.ToUpperInvariant() switch
        {
            RelationshipDirection.ActorToPartner => ActorToPartner,
            RelationshipDirection.PartnerToActor => PartnerToActor,
            _ => throw new InvalidOperationException($"Unknown relationship direction '{direction}'.")
        };
}
