using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulCaliber.Dialogue;

/// <summary>Authored query for one selectable relationship target.</summary>
public sealed record DynamicTargetRule(
    string OwnerRef, string RelationshipDirection, string RequiredStatusKey,
    string SelectionMode = "GROUP", IReadOnlyList<string>? CustomCharacterIds = null);

/// <summary>One authored directional status effect for a named or dynamically selected pair.</summary>
public sealed record DynamicPairStatusEffect(string FromRef, string ToRef, string StatusKey, string? ReverseStatusKey);

/// <summary>Resolved character IDs for one status update, without any database mutation.</summary>
public sealed record DynamicResolvedPairStatus(string FromCharacterId, string ToCharacterId, string StatusKey);

/// <summary>
/// Pure directional group selection. Excludes the two conversation participants,
/// counts distinct character IDs, and never changes relationships itself.
/// </summary>
public static class DynamicTargetRules
{
    /// <summary>Substitutes the bound third-party name or fails when unbound.</summary>
    public static string FormatTarget(string text, string? targetName)
    {
        if (!text.Contains("{target}", StringComparison.OrdinalIgnoreCase)) return text;
        if (string.IsNullOrWhiteSpace(targetName))
            throw new InvalidOperationException("{target} requires selecting a relationship target first.");
        return text.Replace("{target}", targetName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve explicit directional effects before a roll is committed. This pure
    /// resolver is shared by Godot and Studio's read-only Test Lab. No group is
    /// modified in bulk, and a missing or invalid target fails closed.
    /// </summary>
    public static IReadOnlyList<DynamicResolvedPairStatus> ResolvePairEffects(
        IEnumerable<DynamicPairStatusEffect> effects, string initiator, string recipient,
        string? selectedTarget, Func<string, string> resolveCharacterId)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(resolveCharacterId);
        string Resolve(string reference) => reference.ToUpperInvariant() switch
        {
            "INITIATOR" => initiator,
            "RECIPIENT" => recipient,
            "TARGET" => selectedTarget ?? throw new InvalidOperationException(
                "This status effect requires a selected dynamic target."),
            _ => resolveCharacterId(reference) // Also rejects unknown authored IDs.
        };
        var result = new List<DynamicResolvedPairStatus>();
        foreach (var effect in effects)
        {
            string from = Resolve(effect.FromRef), to = Resolve(effect.ToRef);
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Status effect cannot target a self-relationship.");
            result.Add(new DynamicResolvedPairStatus(from, to, effect.StatusKey));
            if (effect.ReverseStatusKey is not null)
                result.Add(new DynamicResolvedPairStatus(to, from, effect.ReverseStatusKey));
        }
        return result;
    }

    public static IReadOnlyList<string> Find(
        DynamicTargetRule rule, string initiator, string recipient,
        IEnumerable<string> characterIds, Func<string,string,string> effectiveStatus)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(characterIds);
        ArgumentNullException.ThrowIfNull(effectiveStatus);
        // An authored list is independent of current relationship statuses. A target
        // is still a *third party*, not one of the two conversation participants.
        if(rule.SelectionMode=="CUSTOM")
        {
            var allowed=new HashSet<string>(characterIds,StringComparer.OrdinalIgnoreCase);
            return (rule.CustomCharacterIds ?? Array.Empty<string>())
                .Where(id=>!string.IsNullOrWhiteSpace(id) && allowed.Contains(id) &&
                           !string.Equals(id,initiator,StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(id,recipient,StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if(rule.SelectionMode!="GROUP")
            throw new InvalidOperationException("Invalid relationship target selection mode.");
        string owner=rule.OwnerRef.ToUpperInvariant() switch
        {
            "INITIATOR"=>initiator,
            "RECIPIENT"=>recipient,
            _=>throw new InvalidOperationException("Invalid relationship target owner.")
        };
        if(rule.RelationshipDirection is not ("OUTGOING" or "INCOMING"))
            throw new InvalidOperationException("Invalid relationship target direction.");
        if(string.IsNullOrWhiteSpace(rule.RequiredStatusKey))
            throw new InvalidOperationException("Missing target-group relationship status.");
        return characterIds
            .Where(id=>!string.Equals(id,initiator,StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(id,recipient,StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id=>string.Equals(rule.RelationshipDirection=="OUTGOING"
                ? effectiveStatus(owner,id) : effectiveStatus(id,owner),
                rule.RequiredStatusKey,StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
