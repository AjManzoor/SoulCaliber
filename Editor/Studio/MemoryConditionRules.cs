using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SoulCaliber.Shared;

/// <summary>
/// Phase 13D authored condition tree. AND/OR/NOT combine MEMORY and STATUS leaves.
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
        Func<string, bool> activeStatusKey)
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

    public static string Describe(MemoryConditionNode node)
    {
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
        Func<string, string, string> currentStatus, out string failure)
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
