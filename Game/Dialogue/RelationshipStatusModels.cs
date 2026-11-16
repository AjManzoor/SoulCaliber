using System;
using System.Collections.Generic;

namespace SoulCaliber.Dialogue;

/// <summary>Authored gate: the indicated current directional status must match.</summary>
public sealed record RelationshipStatusRequirement(string Direction, string StatusKey);

/// <summary>Authored outcome effect: changes a discrete Ally/Neutral/Enemy-style status.</summary>
public sealed record RelationshipStatusEffect(string Direction, string StatusKey);

/// <summary>One actual directional transition stored in the savegame, not the content master.</summary>
public sealed record AppliedRelationshipStatusChange(string FromId, string ToId,
    string Before, string After);

public interface IRelationshipStatusStore
{
    string Get(string fromCharacterId, string toCharacterId);
    IReadOnlyList<AppliedRelationshipStatusChange> Apply(string actorId, string partnerId,
        IReadOnlyList<RelationshipStatusEffect> effects);
    /// <summary>Call when starting a NEW game; do not call when loading an existing save.</summary>
    void ResetToDefaults();
}
