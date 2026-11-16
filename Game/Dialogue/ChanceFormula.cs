using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulCaliber.Shared;

// This exact file is used by the WPF simulator and the Godot runtime.
// Formula coefficients are data, never arbitrary executable code.
public sealed record ChanceFactor(string Direction, string Attribute, double Factor);
public sealed record ChanceTermResult(ChanceFactor Term, int Value, double Points, bool IsProfile);
public sealed record ChanceResult(int FinalChance, double RawChance, double RelationshipPoints,
    double ActorPoints, double DefenderPoints, IReadOnlyList<ChanceTermResult> Terms);

public static class ChanceFormula
{
    public static ChanceResult Calculate(int baseChance, int? actorScore, int? defenderScore,
        double actorFactor, double defenderFactor,
        IEnumerable<ChanceFactor> profileTerms, IEnumerable<ChanceFactor> choiceTerms,
        Func<string, string, int> relationshipValue)
    {
        var terms = new List<ChanceTermResult>();
        double relationship = 0;
        foreach (var (source, isProfile) in new[] { (profileTerms, true), (choiceTerms, false) })
        foreach (var term in source)
        {
            if (!double.IsFinite(term.Factor))
                throw new InvalidOperationException("Non-finite relationship factor in success formula.");
            int value = relationshipValue(term.Direction, term.Attribute);
            double points = value * term.Factor;
            relationship += points;
            terms.Add(new ChanceTermResult(term, value, points, isProfile));
        }
        double actor = actorScore is int a ? (a - 50) * actorFactor : 0;
        double defender = defenderScore is int d ? -(d - 50) * defenderFactor : 0;
        double raw = baseChance + relationship + actor + defender;
        if (!double.IsFinite(raw))
            throw new InvalidOperationException("Non-finite result in success formula.");
        return new ChanceResult(Math.Clamp((int)Math.Round(raw), 5, 95), raw,
            relationship, actor, defender, terms);
    }

    public static string Describe(IEnumerable<ChanceFactor> terms)
    {
        var parts = terms.OrderBy(t => t.Direction).ThenBy(t => t.Attribute)
            .Select(t => $"{t.Direction}.{t.Attribute} × ({t.Factor:+0.###;-0.###;0})").ToArray();
        return parts.Length == 0 ? "0 (no relationship contributions)" : string.Join(" + ", parts);
    }

    public static string Explain(ChanceResult result, int baseChance, string profileKey,
        int? actorScore, int? defenderScore, double actorFactor, double defenderFactor)
    {
        var lines = new List<string>
        {
            $"Base chance: {baseChance}%",
            $"Profile: {profileKey}",
            $"Relationship formula: {Describe(result.Terms.Where(x => x.IsProfile).Select(x => x.Term))}"
        };
        foreach (var item in result.Terms)
            lines.Add($"{(item.IsProfile ? "Profile" : "Choice modifier")}: " +
                $"{item.Term.Direction}.{item.Term.Attribute} ({item.Value}) × ({item.Term.Factor:0.###}) " +
                $"= {item.Points:+0.##;-0.##;0} points");
        if (actorScore is int a && defenderScore is int d)
        {
            lines.Add($"Initiator skill: ({a} − 50) × {actorFactor:0.###} = {result.ActorPoints:+0.##;-0.##;0}");
            lines.Add($"Recipient skill: −({d} − 50) × {defenderFactor:0.###} = {result.DefenderPoints:+0.##;-0.##;0}");
        }
        else lines.Add("No skill check: skill contribution = 0");
        lines.Add($"Raw chance: {result.RawChance:0.###}");
        lines.Add($"Final: clamp(round({result.RawChance:0.###}), 5, 95) = {result.FinalChance}%");
        return string.Join(System.Environment.NewLine, lines);
    }
}
