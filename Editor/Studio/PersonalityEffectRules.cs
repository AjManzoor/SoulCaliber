using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SoulCaliber.Shared;

// Content-defined, reusable character personality traits. No Godot/WPF dependencies.
public sealed record PersonalityModifierRule(string TraitKey, string AttributeKey,
    string Outcome, string Change, double Factor);
public sealed record PersonalityAdjustment(int BaseDelta, int AdjustedDelta,
    double Multiplier, string Explanation);

public sealed class PersonalityEffectRules
{
    private readonly Dictionary<string, string[]> _assigned;
    private readonly Dictionary<string, PersonalityModifierRule[]> _modifiers;

    public PersonalityEffectRules(IEnumerable<(string CharacterId, string TraitKey)> assignments,
        IEnumerable<PersonalityModifierRule> modifiers)
    {
        _assigned = assignments.GroupBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TraitKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        _modifiers = modifiers.GroupBy(x => x.TraitKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public bool HasTrait(string characterId, string traitKey) =>
        _assigned.TryGetValue(characterId, out var traits) &&
        traits.Contains(traitKey, StringComparer.OrdinalIgnoreCase);

    public PersonalityAdjustment Adjust(string receivingCharacterId, string attributeKey,
        int baseDelta, bool succeeded)
    {
        if (baseDelta == 0) return new(baseDelta, 0, 1, "zero base delta");
        string outcome = succeeded ? "SUCCESS" : "FAILURE";
        string sign = baseDelta > 0 ? "GAIN" : "LOSS";
        double multiplier = 1;
        var reasons = new List<string>();
        if (_assigned.TryGetValue(receivingCharacterId, out var traits))
        foreach (string trait in traits)
        {
            if (!_modifiers.TryGetValue(trait, out var rules)) continue;
            // Within one trait the most specific matching rule wins: e.g. SUCCESS/GAIN
            // overrides BOTH/GAIN, rather than unexpectedly applying a trait twice.
            var matched = rules.Where(rule =>
                rule.AttributeKey.Equals(attributeKey, StringComparison.OrdinalIgnoreCase) &&
                (rule.Outcome == "BOTH" || rule.Outcome == outcome) &&
                (rule.Change == "BOTH" || rule.Change == sign))
                .OrderByDescending(rule => (rule.Outcome == outcome ? 2 : 0) +
                    (rule.Change == sign ? 1 : 0))
                .FirstOrDefault();
            if (matched is null) continue;
            multiplier *= matched.Factor;
            reasons.Add($"{trait} ×{matched.Factor.ToString("0.##", CultureInfo.InvariantCulture)}");
        }
        // Limit stacked traits without restricting individual authored factors (0..3).
        multiplier = Math.Min(4, multiplier);
        double scaled = Math.Round(baseDelta * multiplier, MidpointRounding.AwayFromZero);
        int delta = (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
        return new(baseDelta, delta, multiplier,
            reasons.Count == 0 ? "no matching traits (×1)" : string.Join(", ", reasons));
    }
}
