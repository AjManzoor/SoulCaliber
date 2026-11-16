using SoulCaliber.Shared;
using System.Text;

namespace SoulCaliberStudio;

/// <summary>
/// Read-only audit for one choice. Uses the same ChanceFormula and
/// PersonalityEffectRules implementations as the Godot dialogue engine.
/// Predicts BOTH outcomes without writing a savegame or changing simulation values.
/// </summary>
public sealed class InteractionAuditService(SimulationData data)
{
    public InteractionChoiceAudit Build(SimChoice choice, string actorId, string partnerId,
        IReadOnlyDictionary<string, int> current, PersonalityEffectRules traitRules)
    {
        int Value(string direction, string attribute)
        {
            string key = ConditionalDialogueRules.StatKey(direction, attribute);
            return current.TryGetValue(key, out int value) ? value :
                throw new InvalidOperationException($"Relationship '{direction}/{attribute}' is unavailable.");
        }

        var skill = data.Skill(choice.Id);
        int? actorSkill = skill is null ? null : data.Personality(actorId).Get(skill.ActorSkill);
        int? partnerSkill = skill is null ? null : data.Personality(partnerId).Get(skill.DefenderSkill);
        string profileKey = data.ProfileForOption(choice.Id);
        var chance = ChanceFormula.Calculate(choice.BaseChance, actorSkill, partnerSkill,
            skill?.ActorFactor ?? 0, skill?.DefenderFactor ?? 0, data.Profile(profileKey),
            data.Modifiers(choice.Id).Select(t => new ChanceFactor(t.Direction, t.Attribute, t.Factor)), Value);

        var definitions = data.Attributes.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

        PredictedOutcome Predict(bool success)
        {
            // A separate copy for each outcome ensures this preview never modifies
            // either the current relationship grid or the other outcome's prediction.
            var working = current.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            var steps = new List<PredictedEffect>();
            foreach (var effect in data.Effects(choice.Id, success))
            {
                bool isActor = effect.Direction.Equals("ACTOR_TO_PARTNER", StringComparison.OrdinalIgnoreCase);
                if (!isActor && !effect.Direction.Equals("PARTNER_TO_ACTOR", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Unknown effect direction '{effect.Direction}'.");
                if (!definitions.TryGetValue(effect.Attribute, out var definition))
                    throw new InvalidOperationException($"Unknown relationship attribute '{effect.Attribute}'.");
                string key = ConditionalDialogueRules.StatKey(effect.Direction, effect.Attribute);
                if (!working.TryGetValue(key, out int before))
                    throw new InvalidOperationException($"Relationship '{key}' is missing.");
                string receiverId = isActor ? actorId : partnerId;
                var personality = traitRules.Adjust(receiverId, effect.Attribute, effect.Delta, success);
                int after = (int)Math.Clamp((long)before + personality.AdjustedDelta,
                    definition.Minimum, definition.Maximum);
                working[key] = after;
                steps.Add(new PredictedEffect(effect.Direction, effect.Attribute, receiverId,
                    before, effect.Delta, personality.AdjustedDelta, personality.Multiplier,
                    after - before, after, personality.Explanation));
            }
            return new PredictedOutcome(success, steps);
        }

        return new InteractionChoiceAudit(data, choice, actorId, partnerId, profileKey,
            skill, actorSkill, partnerSkill, chance, Predict(true), Predict(false));
    }
}

public sealed record PredictedEffect(string Direction, string Attribute, string ReceiverId,
    int Before, int BaseDelta, int AdjustedDelta, double Multiplier,
    int AppliedDelta, int After, string TraitExplanation)
{
    public bool WasClamped => AppliedDelta != AdjustedDelta;
}

public sealed record PredictedOutcome(bool Success, IReadOnlyList<PredictedEffect> Effects);

public sealed class InteractionChoiceAudit(
    SimulationData data, SimChoice choice, string actorId, string partnerId,
    string profileKey, SimSkillCheck? skill, int? actorSkill, int? partnerSkill,
    ChanceResult chance, PredictedOutcome success, PredictedOutcome failure)
{
    public ChanceResult Chance { get; } = chance;
    public string ChanceBreakdown => ChanceFormula.Explain(chance, choice.BaseChance, profileKey,
        actorSkill, partnerSkill, skill?.ActorFactor ?? 0, skill?.DefenderFactor ?? 0);
    public PredictedOutcome Success { get; } = success;
    public PredictedOutcome Failure { get; } = failure;

    public string Describe(string? lockedReason = null, bool? resolvedSuccess = null,
        int? roll = null, string? testMode = null)
    {
        var output = new StringBuilder();
        static string Points(double value) => value.ToString("+0.##;-0.##;0");
        static string Delta(int value) => value.ToString("+0;-0;0");
        static void Heading(StringBuilder sb, string label) =>
            sb.AppendLine().AppendLine(label).AppendLine(new string('─', 56));

        Heading(output, "1  PLAYER CHOICE");
        output.AppendLine($"{data.Name(actorId)} → {data.Name(partnerId)}");
        output.AppendLine($"{choice.Label} [{choice.Key}]");
        output.AppendLine(lockedReason is null
            ? "Available: player can attempt this action"
            : $"LOCKED: {lockedReason}");
        output.AppendLine("Personality traits do not restrict player choices.");

        Heading(output, "2  CHARACTER SKILLS — CAPABILITY");
        if (skill is null)
            output.AppendLine("No skill check defined; skill contribution is zero.");
        else
        {
            output.AppendLine($"Initiator {data.Name(actorId)}: {skill.ActorSkill} {actorSkill}");
            output.AppendLine($"  ({actorSkill} − 50) × {skill.ActorFactor:0.###} = {Points(chance.ActorPoints)} points");
            output.AppendLine($"Recipient {data.Name(partnerId)}: {skill.DefenderSkill} {partnerSkill}");
            output.AppendLine($"  −({partnerSkill} − 50) × {skill.DefenderFactor:0.###} = {Points(chance.DefenderPoints)} points");
        }

        Heading(output, "3  EXISTING RELATIONSHIPS — SUCCESS MODIFIERS");
        output.AppendLine($"Check profile: {profileKey}");
        var profileTerms = chance.Terms.Where(t => t.IsProfile).ToList();
        var choiceTerms = chance.Terms.Where(t => !t.IsProfile).ToList();
        if (profileTerms.Count == 0) output.AppendLine("Profile has no relationship terms (0 points).");
        foreach (var term in profileTerms)
            output.AppendLine($"  {Label(term.Term.Direction)}.{term.Term.Attribute}: " +
                $"{term.Value} × {term.Term.Factor:0.###} = {Points(term.Points)} points");
        output.AppendLine("Choice-specific relationship modifiers:");
        if (choiceTerms.Count == 0) output.AppendLine("  None (0 points)");
        foreach (var term in choiceTerms)
            output.AppendLine($"  {Label(term.Term.Direction)}.{term.Term.Attribute}: " +
                $"{term.Value} × {term.Term.Factor:0.###} = {Points(term.Points)} points");
        output.AppendLine($"Total relationship contribution: {Points(chance.RelationshipPoints)} points");
        output.AppendLine("These factors affect SUCCESS PROBABILITY, not relationship changes.");

        Heading(output, "4  FINAL PROBABILITY AND OUTCOME");
        output.AppendLine($"Base: {choice.BaseChance}%");
        output.AppendLine($"Skills: {Points(chance.ActorPoints + chance.DefenderPoints)} points");
        output.AppendLine($"Relationships (profile + choice): {Points(chance.RelationshipPoints)} points");
        output.AppendLine($"Raw: {chance.RawChance:0.##}%");
        output.AppendLine($"Rounded and clamped to 5–95%: {chance.FinalChance}%");
        if (Math.Abs(chance.RawChance - chance.FinalChance) > 1.01)
            output.AppendLine("NOTE: Final chance has hit a 5–95% limit; check large formula factors.");
        if (resolvedSuccess is bool outcome)
            output.AppendLine($"{testMode}: roll {roll}/100 → {(outcome ? "SUCCESS" : "FAILURE")}");
        else output.AppendLine("Not rolled yet. Both outcome previews follow.");

        Heading(output, "5  AUTHORED RELATIONSHIP EFFECTS — BASE VALUES");
        DescribeBase(output, success);
        DescribeBase(output, failure);

        Heading(output, "6  PERSONALITY → ACTUAL RELATIONSHIP CHANGES");
        DescribeAdjusted(output, success, resolvedSuccess);
        DescribeAdjusted(output, failure, resolvedSuccess);
        output.AppendLine().AppendLine("The test uses temporary character traits and in-memory relationships only.");
        return output.ToString();

        void DescribeBase(StringBuilder sb, PredictedOutcome prediction)
        {
            sb.AppendLine(prediction.Success ? "On SUCCESS:" : "On FAILURE:");
            if (prediction.Effects.Count == 0) sb.AppendLine("  No relationship effects authored.");
            foreach (var effect in prediction.Effects)
                sb.AppendLine($"  {Label(effect.Direction)}.{effect.Attribute}: {Delta(effect.BaseDelta)}");
        }

        void DescribeAdjusted(StringBuilder sb, PredictedOutcome prediction, bool? chosen)
        {
            string state = prediction.Success ? "SUCCESS" : "FAILURE";
            sb.AppendLine(chosen is bool actual && actual == prediction.Success ?
                $"On {state} (ACTUAL):" : $"On {state} (preview):");
            if (prediction.Effects.Count == 0) sb.AppendLine("  No relationship changes.");
            foreach (var effect in prediction.Effects)
            {
                sb.AppendLine($"  {data.Name(effect.ReceiverId)}'s {effect.Attribute} toward " +
                    $"{data.Name(effect.ReceiverId.Equals(actorId, StringComparison.OrdinalIgnoreCase) ? partnerId : actorId)}");
                sb.AppendLine($"    {Delta(effect.BaseDelta)} × {effect.Multiplier:0.###} " +
                    $"= {Delta(effect.AdjustedDelta)} | {effect.TraitExplanation}");
                sb.AppendLine($"    {effect.Before} → {effect.After} (applied {Delta(effect.AppliedDelta)})" +
                    (effect.WasClamped ? " [CLAMPED to attribute range]" : ""));
            }
        }
    }

    private static string Label(string direction) =>
        direction.Equals("ACTOR_TO_PARTNER", StringComparison.OrdinalIgnoreCase)
            ? "Initiator → Recipient"
            : "Recipient → Initiator";
}
