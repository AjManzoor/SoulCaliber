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
