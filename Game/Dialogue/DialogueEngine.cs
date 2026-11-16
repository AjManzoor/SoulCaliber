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
    private readonly Random _random = new();
    private readonly Dictionary<string, DialogueNode> _nodes;

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
        IInteractionEffectStore? effectStore = null)
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
        Definition = data.Interaction(interactionKey.Trim());
        Visitor = data.Character(actorCharacterId.Trim());
        Host = data.Character(partnerCharacterId.Trim());
        if (string.Equals(Visitor.Id, Host.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An interaction must have two different characters.");
        _nodes = Definition.Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
        CurrentNode = _nodes[Definition.StartNodeKey];
    }

    public void Restart() => CurrentNode = _nodes[Definition.StartNodeKey];

    public IReadOnlyList<AvailableOption> GetOptions() => CurrentNode.Options
        .Select(option =>
        {
            string lockReason = LockReason(option);
            bool locked = lockReason.Length != 0;
            // A hidden choice is absent from both the UI and the engine's selectable list.
            if (locked && option.HideWhenRequirementsUnmet) return null;
            return new AvailableOption(option, Chance(option), locked, lockReason);
        })
        .Where(option => option is not null)
        .Select(option => option!)
        .ToList();

    public DialogueResolution Choose(string optionKey)
    {
        var available = GetOptions().FirstOrDefault(o =>
            string.Equals(o.Definition.Key, optionKey, StringComparison.OrdinalIgnoreCase));
        if (available is null || available.IsLocked)
            throw new InvalidOperationException($"Option '{optionKey}' does not exist or is locked.");

        int roll = _random.Next(1, 101);
        bool succeeded = roll <= available.Chance;
        DialogueOption option = available.Definition;

        // Resolve wording/image against BEFORE-changes relationship state.
        var actorLine = _data.PlayerLine(Definition.Key, CurrentNode, option,
            Visitor.Id, Host.Id, succeeded, ActorToPartner, PartnerToActor);
        var partnerLine = _data.HostLine(Definition.Key, CurrentNode, option, Host.Id, Visitor.Id, succeeded, ActorToPartner, PartnerToActor);
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
        IReadOnlyList<AppliedRelationshipChange> changes;
        IReadOnlyList<AppliedRelationshipStatusChange> statusChanges;
        if (_effectStore is not null)
        {
            // The savegame implementation commits both effect families together.
            var committed = _effectStore.Apply(Visitor.Id, Host.Id, adjustedEffects, statusEffects);
            changes = committed.RelationshipChanges;
            statusChanges = committed.StatusChanges;
        }
        else
        {
            // Preserve compatibility with non-SQLite consumers of this pure rules engine,
            // but never silently perform a partially-committable mixed outcome.
            if (adjustedEffects.Count > 0 && statusEffects.Count > 0)
                throw new InvalidOperationException(
                    "Mixed numeric and status effects require an atomic IInteractionEffectStore.");
            changes = _relationships.Apply(Visitor.Id, Host.Id, adjustedEffects);
            statusChanges = _statuses?.Apply(Visitor.Id, Host.Id, statusEffects)
                ?? Array.Empty<AppliedRelationshipStatusChange>();
        }

        string targetReaction = succeeded
            ? option.SuccessTargetReaction
            : option.FailureTargetReaction;
        if (string.IsNullOrWhiteSpace(targetReaction))
            targetReaction = succeeded ? "RECEPTIVE" : "DISMISSIVE";

        return new DialogueResolution(option, actorLine, partnerLine,
            succeeded, roll, available.Chance, changes, statusChanges,
            succeeded ? option.SuccessNextNodeKey : option.FailureNextNodeKey,
            targetReaction);
    }

    public bool Advance(string? nextNodeKey)
    {
        if (string.IsNullOrWhiteSpace(nextNodeKey)) return false;
        CurrentNode = _nodes[nextNodeKey];
        return true;
    }

    public string Introduction() => _data.Introduction(Definition.Key, CurrentNode, Host.Id, Visitor.Id, ActorToPartner, PartnerToActor)
        .Replace("{actor}", Visitor.Name, StringComparison.OrdinalIgnoreCase)
        .Replace("{partner}", Host.Name, StringComparison.OrdinalIgnoreCase);

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
