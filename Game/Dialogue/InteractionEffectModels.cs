using System.Collections.Generic;

namespace SoulCaliber.Dialogue;

/// <summary>Resolved changes from one atomic interaction-effect commit.</summary>
public sealed record AppliedInteractionEffects(
    IReadOnlyList<AppliedRelationshipChange> RelationshipChanges,
    IReadOnlyList<AppliedRelationshipStatusChange> StatusChanges);

/// <summary>
/// A rules-engine port for committing both kinds of authored outcome effects together.
/// Implementations must either commit both or leave both unchanged on failure.
/// </summary>
public interface IInteractionEffectStore
{
    AppliedInteractionEffects Apply(string actorId, string partnerId,
        IReadOnlyList<RelationshipEffect> relationshipEffects,
        IReadOnlyList<RelationshipStatusEffect> statusEffects);
}


/// <summary>A resolved memory effect, including already-resolved participant IDs.</summary>
public sealed record ResolvedMemoryEffect(string MemoryKey,
    IReadOnlyList<string> AffectedCharacterIds,
    string? SeverityOverride, string? KnowledgeOverride);

/// <summary>
/// The immutable facts needed to save ONE resolved choice, not an entire conversation.
/// No authored dialogue is changed by this persistence contract.
/// </summary>
public sealed record ResolvedChoiceCommit(
    string InteractionKey, string NodeKey, string OptionKey,
    string ActorId, string PartnerId,
    bool Succeeded, int Roll, int Chance, string TargetReaction,
    IReadOnlyList<RelationshipEffect> RelationshipEffects,
    IReadOnlyList<RelationshipStatusEffect> StatusEffects,
    IReadOnlyList<ResolvedMemoryEffect> MemoryEffects);

/// <summary>
/// Atomic game-save unit of work. All effects, memories, affected people, knowledge,
/// and the history row commit together or none do. Pure simulation implementations
/// can keep implementing the smaller IInteractionEffectStore interface.
/// </summary>
public interface IInteractionOutcomeStore : IInteractionEffectStore
{
    AppliedInteractionEffects CommitResolvedChoice(ResolvedChoiceCommit choice);
}
