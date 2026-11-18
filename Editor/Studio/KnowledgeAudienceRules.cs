using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SoulCaliber.Shared;

/// <summary>Authoring-time distribution rules. Evaluated only when a memory is recorded.
/// Knowledge rows are a historical snapshot; later relationship changes do not revoke knowledge.</summary>
public sealed class KnowledgeAudienceSpec
{
    public bool IncludeActor { get; set; } = true;
    public bool IncludeAffected { get; set; }
    public List<string> ActorStatusKeys { get; set; } = new();
    public List<string> AffectedStatusKeys { get; set; } = new();
    public List<string> OtherCharacterIds { get; set; } = new();
    public bool ExcludeActor { get; set; }
    public bool ExcludeAffected { get; set; }
    public List<string> ExcludedCharacterIds { get; set; } = new();

    public static KnowledgeAudienceSpec Parse(string json)
    {
        var spec = JsonSerializer.Deserialize<KnowledgeAudienceSpec>(json)
            ?? throw new InvalidOperationException("Audience rules must contain an object.");
        if (spec.ActorStatusKeys is null || spec.AffectedStatusKeys is null ||
            spec.OtherCharacterIds is null || spec.ExcludedCharacterIds is null)
            throw new InvalidOperationException("Audience rule lists cannot be null.");
        foreach (var key in spec.ActorStatusKeys.Concat(spec.AffectedStatusKeys)
                     .Concat(spec.OtherCharacterIds).Concat(spec.ExcludedCharacterIds))
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Audience rules cannot contain blank identifiers.");
        return spec;
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}

public static class KnowledgeAudienceRules
{
    /// <summary>Outgoing status means source considers candidate a member of that group.
    /// Exclusions win even if the same character matches multiple inclusions.</summary>
    public static IReadOnlyCollection<string> Resolve(KnowledgeAudienceSpec spec, string actor,
        IReadOnlyCollection<string> affected, IReadOnlyCollection<string> characters,
        Func<string, string, string> outgoingStatus)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(outgoingStatus);
        var knowers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spec.IncludeActor) knowers.Add(actor);
        if (spec.IncludeAffected) knowers.UnionWith(affected);
        knowers.UnionWith(spec.OtherCharacterIds);
        var actorGroups = spec.ActorStatusKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affectedGroups = spec.AffectedStatusKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in characters)
        {
            if (!string.Equals(candidate, actor, StringComparison.OrdinalIgnoreCase) &&
                actorGroups.Contains(outgoingStatus(actor, candidate)))
                knowers.Add(candidate);
            foreach (var victim in affected)
                if (!string.Equals(candidate, victim, StringComparison.OrdinalIgnoreCase) &&
                    affectedGroups.Contains(outgoingStatus(victim, candidate)))
                { knowers.Add(candidate); break; }
        }
        if (spec.ExcludeActor) knowers.Remove(actor);
        if (spec.ExcludeAffected) knowers.ExceptWith(affected);
        knowers.ExceptWith(spec.ExcludedCharacterIds);
        return knowers.ToArray();
    }
}
