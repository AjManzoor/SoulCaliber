using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulCaliber.Shared;

// This file is identical in SoulCaliber Studio and Godot. Do not implement
// separate rule ordering in the editor and game.
public sealed record DialogueClause(string Direction, string Attribute, int? Minimum, int? Maximum);
public sealed record DialogueCandidate(int Id, string? CharacterId, string? OtherCharacterId,
    string? Outcome, int Priority, string Text, string ImagePath, bool Conditional,
    IReadOnlyList<DialogueClause> Clauses);
public sealed record DialogueSelection(DialogueCandidate? Candidate, string Source, string Explanation);

public static class ConditionalDialogueRules
{
    private static bool Eq(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static string StatKey(string direction, string attribute) =>
        direction.ToUpperInvariant() + ":" + attribute.ToUpperInvariant();

    public static DialogueSelection Resolve(IEnumerable<DialogueCandidate> candidates,
        string speaker, string other, string? outcome, IReadOnlyDictionary<string, int> stats)
    {
        var considered = candidates.Where(c => (string.IsNullOrWhiteSpace(c.CharacterId) || Eq(c.CharacterId, speaker)) &&
            (string.IsNullOrEmpty(c.OtherCharacterId) || Eq(c.OtherCharacterId, other)) &&
            (string.IsNullOrEmpty(c.Outcome) || Eq(c.Outcome, outcome)))
            .ToList();

        bool Eligible(DialogueCandidate candidate) =>
            !candidate.Conditional || candidate.Clauses.Count > 0 && candidate.Clauses.All(rule =>
                stats.TryGetValue(StatKey(rule.Direction, rule.Attribute), out int actual) &&
                (rule.Minimum is null || actual >= rule.Minimum.Value) &&
                (rule.Maximum is null || actual <= rule.Maximum.Value));

        var eligible = considered.Where(Eligible).ToList();
        // Conditional variants override ordinary lines when their conditions pass.
        // Specificity within conditional/standard groups: pair > speaker > counterpart > any.
        // Scope must beat priority: a universal high-priority rule cannot hide a pair rule.
        static int Scope(DialogueCandidate c) =>
            (string.IsNullOrWhiteSpace(c.CharacterId) ? 0 : 2) +
            (string.IsNullOrWhiteSpace(c.OtherCharacterId) ? 0 : 1);
        var selected = eligible
            .OrderByDescending(c => c.Conditional)
            .ThenByDescending(Scope)
            .ThenByDescending(c => !string.IsNullOrEmpty(c.Outcome))
            .ThenByDescending(c => c.Priority)
            .ThenByDescending(c => c.Clauses.Count)
            .ThenBy(c => c.Id)
            .FirstOrDefault();

        if (selected is null)
            return new(null, "Generic fallback",
                $"{considered.Count} matching identity/outcome candidate(s); none passed all conditions.");

        string scope = Scope(selected) switch
        {
            3 => "Pair-specific",
            2 => "Speaker-specific",
            1 => "Counterpart-specific",
            _ => "Any-character"
        };
        string source = scope + (selected.Conditional ? $" conditional variant #{selected.Id}" : " standard phrase");
        string conditions = selected.Conditional
            ? string.Join(" AND ", selected.Clauses.Select(c =>
                $"{c.Direction} {c.Attribute} " +
                (c.Minimum is null ? "" : $">={c.Minimum} ") +
                (c.Maximum is null ? "" : $"<={c.Maximum}")))
            : "No conditions";
        return new(selected, source,
            $"{source}; priority {selected.Priority}; {conditions}. " +
            $"Matched {eligible.Count} eligible candidate(s) of {considered.Count} for this speaker and outcome.");
    }
}
