using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SoulCaliber.Shared;

/// <summary>
/// AND/OR/NOT tree combining MEMORY, STATUS, RELATIONSHIP, RELATIONSHIP_COUNT, RELATIONSHIP_GROUP and TRAIT leaves.
/// Keep this pure rules file identical
/// in Studio and Godot until a shared assembly is introduced.
/// </summary>
public sealed class MemoryConditionNode
{
    public string Kind { get; set; } = "AND";
    public List<MemoryConditionNode> Children { get; set; } = new();
    public string? MemoryKey { get; set; }
    public string ActorRef { get; set; } = "ANY";
    public string AffectedRef { get; set; } = "ANY";
    public string KnowerRef { get; set; } = "ANY";
    public int MinimumCount { get; set; } = 1;
    public int? MaximumCount { get; set; }
    // STATUS leaves use these fields. Existing MEMORY-only JSON remains valid.
    public string StatusDirection { get; set; } = "PARTNER_TO_ACTOR";
    public string? StatusKey { get; set; }
    // RELATIONSHIP leaf: a conversation participant and a specifically authored
    // third character. Explicit direction is preserved (e.g. Recipient -> Queen).
    // Legacy MEMORY/STATUS JSON requires no migration.
    public string RelationshipFromRef { get; set; } = "PARTNER";
    public string RelationshipToRef { get; set; } = "ANY";
    // RELATIONSHIP_COUNT: status relationships from/to one participant and a set of
    // distinct published characters. Self is never counted; excluding the other
    // conversation participant is optional. Older JSON keeps its existing meaning.
    public string CountOwnerRef { get; set; } = "ACTOR";
    public string CountDirection { get; set; } = "OUTGOING";
    public bool CountExcludeOtherParticipant { get; set; }
    // RELATIONSHIP_GROUP: count distinct third characters X for which both
    // source-owner <-> X and match-owner <-> that SAME X satisfy their statuses.
    // Participants are deliberately excluded from X; use STATUS for the pair.
    public string GroupSourceOwnerRef { get; set; } = "ACTOR";
    public string GroupSourceDirection { get; set; } = "OUTGOING";
    public string? GroupSourceStatusKey { get; set; }
    public string GroupMatchOwnerRef { get; set; } = "PARTNER";
    public string GroupMatchDirection { get; set; } = "OUTGOING";
    public string? GroupMatchStatusKey { get; set; }
    // TRAIT: check an active authored personality trait on the Initiator or Recipient.
    // This is a present-tense character property, not a memory or status.
    public string TraitCharacterRef { get; set; } = "PARTNER";
    public string? TraitKey { get; set; }
}

public static class MemoryConditionRules
{
    public const int MaximumDepth = 12;
    public const int MaximumNodes = 128;

    public static MemoryConditionNode Read(string json) =>
        JsonSerializer.Deserialize<MemoryConditionNode>(json)
        ?? throw new InvalidOperationException("Memory condition cannot be empty.");

    public static string Write(MemoryConditionNode tree) =>
        JsonSerializer.Serialize(tree, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Prevents invalid/empty/oversized trees from being published or run.</summary>
    public static void Validate(MemoryConditionNode root,
        Func<string, bool> activeMemoryKey, Func<string, bool> knownCharacter,
        Func<string, bool> activeStatusKey, Func<string, bool>? activeTraitKey = null)
    {
        if (root is null) throw new InvalidOperationException("Memory condition tree is missing.");
        int total = 0;
        void Walk(MemoryConditionNode node, int depth)
        {
            if (++total > MaximumNodes || depth > MaximumDepth)
                throw new InvalidOperationException("Memory conditions exceed the limit of 128 nodes / 12 levels.");
            if (node is null) throw new InvalidOperationException("A memory condition is null.");
            switch (node.Kind)
            {
                case "AND":
                case "OR":
                    if (node.Children is null || node.Children.Count == 0)
                        throw new InvalidOperationException($"{node.Kind} needs at least one condition.");
                    foreach (var child in node.Children) Walk(child, depth + 1);
                    break;
                case "NOT":
                    if (node.Children is null || node.Children.Count != 1)
                        throw new InvalidOperationException("NOT must contain exactly one condition.");
                    Walk(node.Children[0], depth + 1);
                    break;
                case "STATUS":
                    if (node.Children is { Count: > 0 })
                        throw new InvalidOperationException("A status check cannot contain child conditions.");
                    if (string.IsNullOrWhiteSpace(node.StatusKey) || !activeStatusKey(node.StatusKey))
                        throw new InvalidOperationException($"Unknown or inactive relationship status: {node.StatusKey ?? "(none)"}.");
                    if (node.StatusDirection is not ("ACTOR_TO_PARTNER" or "PARTNER_TO_ACTOR" or "BOTH"))
                        throw new InvalidOperationException($"Invalid status direction: {node.StatusDirection}.");
                    break;
                case "RELATIONSHIP":
                    if (node.Children is { Count: > 0 })
                        throw new InvalidOperationException("A third-party relationship check cannot contain children.");
                    if (string.IsNullOrWhiteSpace(node.StatusKey) || !activeStatusKey(node.StatusKey))
                        throw new InvalidOperationException($"Unknown or inactive relationship status: {node.StatusKey ?? "(none)"}.");
                    // Phase 13H-3A deliberately supports one conversation participant
                    // and one named third character; arbitrary groups come in 3C.
                    CheckRequiredRelationshipRef(node.RelationshipFromRef, knownCharacter);
                    CheckRequiredRelationshipRef(node.RelationshipToRef, knownCharacter);
                    bool fromParticipant = node.RelationshipFromRef is "ACTOR" or "PARTNER";
                    bool toParticipant = node.RelationshipToRef is "ACTOR" or "PARTNER";
                    bool fromNamed = node.RelationshipFromRef.StartsWith("CHAR:", StringComparison.Ordinal);
                    bool toNamed = node.RelationshipToRef.StartsWith("CHAR:", StringComparison.Ordinal);
                    if (!((fromParticipant && toNamed) || (fromNamed && toParticipant)))
                        throw new InvalidOperationException(
                            "Choose one Initiator/Recipient reference and one specific third-party character. Direction matters.");
                    break;
                case "RELATIONSHIP_COUNT":
                    if (node.Children is { Count: > 0 })
                        throw new InvalidOperationException("A relationship count cannot contain children.");
                    if (string.IsNullOrWhiteSpace(node.StatusKey) || !activeStatusKey(node.StatusKey))
                        throw new InvalidOperationException($"Unknown or inactive relationship status: {node.StatusKey ?? "(none)"}.");
                    if (node.CountOwnerRef is not ("ACTOR" or "PARTNER"))
                        throw new InvalidOperationException("Relationship count must target Initiator or Recipient.");
                    if (node.CountDirection is not ("OUTGOING" or "INCOMING"))
                        throw new InvalidOperationException("Relationship count direction must be OUTGOING or INCOMING.");
                    if (node.MinimumCount < 0 || node.MaximumCount is < 0 ||
                        (node.MaximumCount is int upper && upper < node.MinimumCount))
                        throw new InvalidOperationException("Invalid relationship count minimum/maximum.");
                    break;
                case "RELATIONSHIP_GROUP":
                    if (node.Children is { Count: > 0 })
                        throw new InvalidOperationException("A relationship group check cannot contain children.");
                    if (string.IsNullOrWhiteSpace(node.GroupSourceStatusKey) ||
                        !activeStatusKey(node.GroupSourceStatusKey))
                        throw new InvalidOperationException("Unknown or inactive source relationship status: " +
                            (node.GroupSourceStatusKey ?? "(none)"));
                    if (string.IsNullOrWhiteSpace(node.GroupMatchStatusKey) ||
                        !activeStatusKey(node.GroupMatchStatusKey))
                        throw new InvalidOperationException("Unknown or inactive matching relationship status: " +
                            (node.GroupMatchStatusKey ?? "(none)"));
                    if (node.GroupSourceOwnerRef is not ("ACTOR" or "PARTNER") ||
                        node.GroupMatchOwnerRef is not ("ACTOR" or "PARTNER"))
                        throw new InvalidOperationException("Group checks must refer to Initiator or Recipient.");
                    if (node.GroupSourceDirection is not ("OUTGOING" or "INCOMING") ||
                        node.GroupMatchDirection is not ("OUTGOING" or "INCOMING"))
                        throw new InvalidOperationException("Group relationship directions must be OUTGOING or INCOMING.");
                    if (node.MinimumCount < 0 || node.MaximumCount is < 0 ||
                        (node.MaximumCount is int groupMax && groupMax < node.MinimumCount))
                        throw new InvalidOperationException("Invalid group match minimum/maximum.");
                    break;
                case "TRAIT":
                    if (node.Children is { Count: > 0 })
                        throw new InvalidOperationException("A character trait check cannot contain children.");
                    if (node.TraitCharacterRef is not ("ACTOR" or "PARTNER"))
                        throw new InvalidOperationException("Trait checks must refer to Initiator or Recipient.");
                    // Never accept a trait rule unless the actual active catalogue was provided.
                    if (activeTraitKey is null)
                        throw new InvalidOperationException("Trait catalogue is unavailable.");
                    if (string.IsNullOrWhiteSpace(node.TraitKey) || !activeTraitKey(node.TraitKey))
                        throw new InvalidOperationException("Unknown or inactive personality trait: " +
                            (node.TraitKey ?? "(none)"));
                    break;
                case "MEMORY":
                    if (node.Children is { Count: > 0 })
                        throw new InvalidOperationException("A memory check cannot contain child conditions.");
                    if (string.IsNullOrWhiteSpace(node.MemoryKey) || !activeMemoryKey(node.MemoryKey))
                        throw new InvalidOperationException($"Unknown or inactive memory tag: {node.MemoryKey ?? "(none)"}.");
                    if (node.MinimumCount < 0 || node.MaximumCount is < 0 ||
                        (node.MaximumCount is int max && max < node.MinimumCount))
                        throw new InvalidOperationException("Invalid minimum/maximum occurrence count.");
                    CheckReference(node.ActorRef, knownCharacter, "actor");
                    CheckReference(node.AffectedRef, knownCharacter, "affected");
                    CheckReference(node.KnowerRef, knownCharacter, "knower");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown condition operator: {node.Kind}.");
            }
        }
        Walk(root, 1);
    }

    private static void CheckReference(string? reference, Func<string, bool> knownCharacter, string field)
    {
        if (reference is "ANY" or "ACTOR" or "PARTNER") return;
        if (reference is null || !reference.StartsWith("CHAR:", StringComparison.Ordinal)
            || reference.Length <= 5 || !knownCharacter(reference[5..]))
            throw new InvalidOperationException($"Invalid {field} character reference: {reference ?? "(null)"}.");
    }

    private static void CheckRequiredRelationshipRef(string? reference,
        Func<string, bool> knownCharacter)
    {
        if (reference is "ACTOR" or "PARTNER") return;
        if (reference is null || !reference.StartsWith("CHAR:", StringComparison.Ordinal) ||
            reference.Length <= 5 || !knownCharacter(reference[5..]))
            throw new InvalidOperationException(
                $"Invalid third-party relationship character reference: {reference ?? "(null)"}.");
    }

    public static string Describe(MemoryConditionNode node)
    {
        if (node.Kind == "TRAIT")
            return $"{DisplayReference(node.TraitCharacterRef)} has trait {node.TraitKey ?? "?"}";
        if (node.Kind == "RELATIONSHIP")
            return $"{DisplayReference(node.RelationshipFromRef)} → {DisplayReference(node.RelationshipToRef)} is {node.StatusKey}";
        if (node.Kind == "RELATIONSHIP_GROUP")
        {
            string RelationshipText(string owner, string direction, string status) =>
                direction == "INCOMING"
                    ? $"X → {DisplayReference(owner)} = {status}"
                    : $"{DisplayReference(owner)} → X = {status}";
            string range = node.MaximumCount is int maximum
                ? $"count {node.MinimumCount}..{maximum}" : $"count >= {node.MinimumCount}";
            return $"Same third character X: {RelationshipText(node.GroupSourceOwnerRef, node.GroupSourceDirection, node.GroupSourceStatusKey ?? "?")} AND " +
                RelationshipText(node.GroupMatchOwnerRef, node.GroupMatchDirection, node.GroupMatchStatusKey ?? "?") +
                $" | {range}";
        }
        if (node.Kind == "RELATIONSHIP_COUNT")
        {
            var owner = DisplayReference(node.CountOwnerRef);
            var direction = node.CountDirection == "INCOMING" ? $"Others → {owner}" : $"{owner} → Others";
            var range = node.MaximumCount is int maximum
                ? $"count {node.MinimumCount}..{maximum}" : $"count >= {node.MinimumCount}";
            return $"{direction} is {node.StatusKey} | {range}" +
                (node.CountExcludeOtherParticipant ? " | exclude other participant" : "");
        }
        if (node.Kind == "STATUS")
        {
            string direction = node.StatusDirection switch
            {
                "ACTOR_TO_PARTNER" => "Initiator → Recipient",
                "PARTNER_TO_ACTOR" => "Recipient → Initiator",
                "BOTH" => "Both directions",
                _ => node.StatusDirection
            };
            return $"{direction} is {node.StatusKey}";
        }
        if (node.Kind != "MEMORY") return node.Kind;
        string count = node.MaximumCount is int max
            ? $"count {node.MinimumCount}..{max}"
            : $"count >= {node.MinimumCount}";
        return $"{node.MemoryKey} | Actor: {DisplayReference(node.ActorRef)} | Affected: {DisplayReference(node.AffectedRef)} | Knower: {DisplayReference(node.KnowerRef, true)} | {count}";
    }

    // Human-readable labels only; authored JSON and evaluation retain ACTOR/PARTNER tokens.
    private static string DisplayReference(string reference, bool isKnower = false) => reference switch
    {
        "ANY" => isKnower ? "Knowledge not required" : "Any",
        "ACTOR" => "Initiator",
        "PARTNER" => "Recipient",
        _ when reference?.StartsWith("CHAR:", StringComparison.Ordinal) == true => "Character " + reference[5..],
        _ => reference ?? "(unknown)"
    };

    /// <summary>
    /// Evaluates the same tree in Godot and future Studio sandboxes.
    /// ANY knower deliberately checks world history, not character knowledge.
    /// Status checks read the CURRENT directional relationship, not Studio defaults.
    /// </summary>
    public static bool Evaluate(MemoryConditionNode root, string actorId, string partnerId,
        Func<string, string?, string?, string?, int> occurrenceCount,
        Func<string, string, string> currentStatus, out string failure,
        IEnumerable<string>? characterRoster = null,
        Func<string, string, bool>? hasTrait = null)
    {
        (bool Pass, string Reason) Walk(MemoryConditionNode node)
        {
            switch (node.Kind)
            {
                case "AND":
                    foreach (var child in node.Children)
                    {
                        var result = Walk(child);
                        if (!result.Pass) return result;
                    }
                    return (true, "");
                case "OR":
                    var reasons = new List<string>();
                    foreach (var child in node.Children)
                    {
                        var result = Walk(child);
                        if (result.Pass) return (true, "");
                        reasons.Add(result.Reason);
                    }
                    return (false, "None of the alternatives passed: " +
                        string.Join("; ", reasons.Take(3)));
                case "NOT":
                    var inner = Walk(node.Children[0]);
                    return inner.Pass
                        ? (false, "NOT condition failed: " + Describe(node.Children[0]))
                        : (true, "");
                case "STATUS":
                    bool actorMatches = string.Equals(currentStatus(actorId, partnerId),
                        node.StatusKey, StringComparison.OrdinalIgnoreCase);
                    bool partnerMatches = string.Equals(currentStatus(partnerId, actorId),
                        node.StatusKey, StringComparison.OrdinalIgnoreCase);
                    bool statusPasses = node.StatusDirection switch
                    {
                        "ACTOR_TO_PARTNER" => actorMatches,
                        "PARTNER_TO_ACTOR" => partnerMatches,
                        "BOTH" => actorMatches && partnerMatches,
                        _ => throw new InvalidOperationException("Unknown relationship-status direction.")
                    };
                    return statusPasses ? (true, "") : (false, Describe(node) + " (not matched)");
                case "RELATIONSHIP":
                    string from = Resolve(node.RelationshipFromRef, actorId, partnerId)
                        ?? throw new InvalidOperationException("Third-party relationship source is missing.");
                    string to = Resolve(node.RelationshipToRef, actorId, partnerId)
                        ?? throw new InvalidOperationException("Third-party relationship target is missing.");
                    bool matches = !string.Equals(from, to, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(currentStatus(from, to), node.StatusKey, StringComparison.OrdinalIgnoreCase);
                    return matches ? (true, "") : (false, Describe(node) + " (not matched)");
                case "RELATIONSHIP_COUNT":
                    // A complete roster is essential: treating unavailable roster data
                    // as zero could make NOT/maximum-count conditions pass incorrectly.
                    if (characterRoster is null)
                        throw new InvalidOperationException("Relationship count requires the published character roster.");
                    string owner = Resolve(node.CountOwnerRef, actorId, partnerId)
                        ?? throw new InvalidOperationException("Relationship count owner is missing.");
                    string otherParticipant = node.CountOwnerRef == "ACTOR" ? partnerId : actorId;
                    var distinctCharacters = characterRoster
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (!distinctCharacters.Contains(actorId, StringComparer.OrdinalIgnoreCase) ||
                        !distinctCharacters.Contains(partnerId, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The character roster must include both conversation participants.");
                    int matching = distinctCharacters
                        .Count(x => !string.Equals(x, owner, StringComparison.OrdinalIgnoreCase) &&
                            (!node.CountExcludeOtherParticipant ||
                             !string.Equals(x, otherParticipant, StringComparison.OrdinalIgnoreCase)) &&
                            string.Equals(node.CountDirection == "INCOMING"
                                ? currentStatus(x, owner) : currentStatus(owner, x),
                                node.StatusKey, StringComparison.OrdinalIgnoreCase));
                    bool countPasses = matching >= node.MinimumCount &&
                        (node.MaximumCount is null || matching <= node.MaximumCount.Value);
                    return countPasses ? (true, "")
                        : (false, $"{Describe(node)} (found {matching})");
                case "RELATIONSHIP_GROUP":
                    // Like RELATIONSHIP_COUNT, complete roster data is required,
                    // even for NOT(group) or maximum=0; never silently use zero.
                    if (characterRoster is null)
                        throw new InvalidOperationException("Relationship group check requires the published character roster.");
                    string sourceOwner = Resolve(node.GroupSourceOwnerRef, actorId, partnerId)
                        ?? throw new InvalidOperationException("Relationship group source owner is missing.");
                    string matchOwner = Resolve(node.GroupMatchOwnerRef, actorId, partnerId)
                        ?? throw new InvalidOperationException("Relationship group match owner is missing.");
                    var groupCandidates = characterRoster
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (!groupCandidates.Contains(actorId, StringComparer.OrdinalIgnoreCase) ||
                        !groupCandidates.Contains(partnerId, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The character roster must include both conversation participants.");
                    int groupMatches = 0;
                    foreach (string x in groupCandidates)
                    {
                        // X must be a *third* character, not either participant.
                        if (string.Equals(x, actorId, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x, partnerId, StringComparison.OrdinalIgnoreCase))
                            continue;
                        bool sourceMatches = string.Equals(
                            node.GroupSourceDirection == "INCOMING"
                                ? currentStatus(x, sourceOwner) : currentStatus(sourceOwner, x),
                            node.GroupSourceStatusKey, StringComparison.OrdinalIgnoreCase);
                        if (!sourceMatches) continue;
                        bool targetMatches = string.Equals(
                            node.GroupMatchDirection == "INCOMING"
                                ? currentStatus(x, matchOwner) : currentStatus(matchOwner, x),
                            node.GroupMatchStatusKey, StringComparison.OrdinalIgnoreCase);
                        if (targetMatches) groupMatches++;
                    }
                    bool groupPasses = groupMatches >= node.MinimumCount &&
                        (node.MaximumCount is null || groupMatches <= node.MaximumCount.Value);
                    return groupPasses ? (true, "") : (false, $"{Describe(node)} (found {groupMatches})");
                case "TRAIT":
                    // Treat unavailable trait assignments as an error, NOT a failed
                    // condition, because NOT(TRAIT) would otherwise succeed.
                    if (hasTrait is null)
                        throw new InvalidOperationException("Trait assignments are unavailable.");
                    string traitCharacter = Resolve(node.TraitCharacterRef, actorId, partnerId)
                        ?? throw new InvalidOperationException("Trait character reference is missing.");
                    bool traitMatches = hasTrait(traitCharacter, node.TraitKey
                        ?? throw new InvalidOperationException("Trait key is missing."));
                    return traitMatches ? (true, "") : (false, Describe(node) + " (not assigned)");
                case "MEMORY":
                    int count = occurrenceCount(node.MemoryKey!, Resolve(node.ActorRef, actorId, partnerId),
                        Resolve(node.AffectedRef, actorId, partnerId),
                        Resolve(node.KnowerRef, actorId, partnerId));
                    bool passes = count >= node.MinimumCount &&
                        (node.MaximumCount is null || count <= node.MaximumCount.Value);
                    return passes ? (true, "")
                        : (false, $"{Describe(node)} (found {count})");
                default:
                    throw new InvalidOperationException($"Unknown condition operator: {node.Kind}.");
            }
        }
        var result = Walk(root);
        failure = result.Reason;
        return result.Pass;
    }

    // The caller must fail closed if a required persistence service is unavailable.
    // In particular, NOT(MEMORY) must not be allowed to succeed with no memory store.
    public static bool ContainsKind(MemoryConditionNode root, string kind) =>
        root.Kind == kind || (root.Children?.Any(child => ContainsKind(child, kind)) ?? false);

    public static string? Resolve(string reference, string actorId, string partnerId) =>
        reference switch
        {
            "ANY" => null,
            "ACTOR" => actorId,
            "PARTNER" => partnerId,
            _ when reference.StartsWith("CHAR:", StringComparison.Ordinal) => reference[5..],
            _ => throw new InvalidOperationException($"Invalid character reference: {reference}.")
        };
}
