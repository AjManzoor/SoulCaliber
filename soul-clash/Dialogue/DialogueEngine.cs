using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Godot-independent simulation rules. A different Godot scene, game map, or
/// SQLite repository can reuse this engine without changing its dialogue tree.
/// </summary>
public sealed class DialogueEngine
{
	private readonly DialogueRepository _data;
	private readonly IRelationshipStore _relationships;
	private readonly Random _random = new();
	private readonly Dictionary<string, DialogueNode> _nodes;

	public InteractionDefinition Definition { get; }
	public CharacterDefinition Visitor { get; }  // left / actor / makes choices
	public CharacterDefinition Host { get; }     // right / partner / responds
	public DialogueNode CurrentNode { get; private set; }

	public RelationshipStats ActorToPartner => _relationships.Get(Visitor.Id, Host.Id);
	public RelationshipStats PartnerToActor => _relationships.Get(Host.Id, Visitor.Id);
	public PersonalityStats ActorPersonality => _data.Personality(Visitor.Id);
	public PersonalityStats PartnerPersonality => _data.Personality(Host.Id);

	public DialogueEngine(DialogueRepository data, IRelationshipStore relationships,
		string interactionKey, string actorCharacterId, string? partnerCharacterId = null)
	{
		_data = data;
		_relationships = relationships;
		Definition = data.Interaction(interactionKey);
		Visitor = data.Character(actorCharacterId);
		string partnerId = string.IsNullOrWhiteSpace(partnerCharacterId)
			? Definition.HostCharacterId : partnerCharacterId;
		if (string.IsNullOrWhiteSpace(partnerId))
			throw new InvalidOperationException($"Interaction '{interactionKey}' has no default partner. Set Partner Character Id in the Inspector.");
		Host = data.Character(partnerId);
		if (string.Equals(Visitor.Id, Host.Id, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("An interaction must have two different characters.");
		_nodes = Definition.Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
		CurrentNode = _nodes[Definition.StartNodeKey];
	}

	public void Restart() => CurrentNode = _nodes[Definition.StartNodeKey];

	public IReadOnlyList<AvailableOption> GetOptions() => CurrentNode.Options
		.Select(option =>
		{
			string lockReason = LockReason(option);
			return new AvailableOption(option, Chance(option), lockReason.Length != 0, lockReason);
		}).ToList();

	public DialogueResolution Choose(string optionKey)
	{
		var available = GetOptions().FirstOrDefault(o =>
			string.Equals(o.Definition.Key, optionKey, StringComparison.OrdinalIgnoreCase));
		if (available is null || available.IsLocked)
			throw new InvalidOperationException($"Option '{optionKey}' does not exist or is locked.");

		int roll = _random.Next(1, 101);
		bool succeeded = roll <= available.Chance;
		DialogueOption option = available.Definition;

		// Resolve wording/image against BEFORE-changes relationship state.
		var actorLine = _data.PlayerLine(Definition.Key, CurrentNode, option,
			Visitor.Id, Host.Id, succeeded);
		var partnerLine = _data.HostLine(Definition.Key, CurrentNode, option, Host.Id, Visitor.Id, succeeded);
		var effects = succeeded ? option.SuccessEffects : option.FailureEffects;
		var changes = _relationships.Apply(Visitor.Id, Host.Id, effects);

		string targetReaction = succeeded
			? option.SuccessTargetReaction
			: option.FailureTargetReaction;
		if (string.IsNullOrWhiteSpace(targetReaction))
			targetReaction = succeeded ? "RECEPTIVE" : "DISMISSIVE";

		return new DialogueResolution(option, actorLine, partnerLine,
			succeeded, roll, available.Chance, changes,
			succeeded ? option.SuccessNextNodeKey : option.FailureNextNodeKey,
			targetReaction);
	}

	public bool Advance(string? nextNodeKey)
	{
		if (string.IsNullOrWhiteSpace(nextNodeKey)) return false;
		CurrentNode = _nodes[nextNodeKey];
		return true;
	}

	public string Introduction() => _data.Introduction(Definition.Key, CurrentNode, Host.Id, Visitor.Id)
		.Replace("{actor}", Visitor.Name, StringComparison.OrdinalIgnoreCase)
		.Replace("{partner}", Host.Name, StringComparison.OrdinalIgnoreCase);

	private string LockReason(DialogueOption option)
	{
		foreach (var req in option.Requirements)
		{
			int value = GetDirectionalStats(req.Direction)
				.Get(RelationshipStats.ParseAttribute(req.Attribute));
			if (req.Minimum is int min && value < min)
				return $"{req.Attribute} ≥ {min}";
			if (req.Maximum is int max && value > max)
				return $"{req.Attribute} ≤ {max}";
		}
		return "";
	}

	// A skill check belongs to a specific interaction/node/option, not to the UI.
	// If there is no check, the existing probability calculation is unchanged.
	public string SkillLabel(DialogueOption option)
	{
		var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
		return check is null ? "" : check.ActorSkill;
	}

	public string ChanceBreakdown(DialogueOption option)
	{
		var (chance, relationship, actorEffect, defenderEffect, check) = CalculateChance(option);
		string mode = check?.CheckMode ?? "STANDARD";
		string details = $"Check: {mode}\n" +
			$"Base: {option.BaseSuccessChance}%\n" +
			$"Relationships: {relationship:+0.0;-0.0;0.0} points";
		if (mode == "INTIMIDATION")
			details += " (target Fear +0.10, target Trust -0.05)";
		if (check is not null)
		{
			int actorScore = ActorPersonality.Get(check.ActorSkill);
			int defenderScore = PartnerPersonality.Get(check.DefenderSkill);
			details += $"\n{Visitor.Name} {check.ActorSkill}: {actorScore}/100 ({actorEffect:+0.0;-0.0;0.0})" +
				$"\n{Host.Name} {check.DefenderSkill}: {defenderScore}/100 ({defenderEffect:+0.0;-0.0;0.0})";
		}
		return $"{details}\nFinal chance: {chance}% (capped at 5–95%)";
	}

	private int Chance(DialogueOption option) => CalculateChance(option).chance;

	private (int chance, double relationship, double actorEffect,
		double defenderEffect, OptionSkillCheck? check) CalculateChance(DialogueOption option)
	{
		var partnerView = PartnerToActor;
		var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
		bool intimidating = check is not null && check.CheckMode == "INTIMIDATION";

		// Intimidation is not strengthened by the target liking/respecting you.
		// Their existing fear makes them more susceptible; their trust makes them
		// less likely to believe a threat. All relationships are directional.
		double relationship = intimidating
			? partnerView.Fear * 0.10 - partnerView.Trust * 0.05
			: partnerView.Trust * 0.20 + partnerView.Respect * 0.10;
		foreach (var modifier in option.ChanceModifiers)
		{
			var attribute = RelationshipStats.ParseAttribute(modifier.Attribute);
			relationship += GetDirectionalStats(modifier.Direction).Get(attribute) * modifier.Factor;
		}

		double actorEffect = 0, defenderEffect = 0;
		if (check is not null)
		{
			// With equal 0.5 factors, the ability contribution is
			// 0.5 x (actor skill - defender skill). That means a powerful
			// defender can CANCEL a powerful actor's intimidation.
            actorEffect = (ActorPersonality.Get(check.ActorSkill) - 50) * check.ActorFactor;
            defenderEffect = -(PartnerPersonality.Get(check.DefenderSkill) - 50) * check.DefenderFactor;
        }
        double total = option.BaseSuccessChance + relationship + actorEffect + defenderEffect;
        return (Math.Clamp((int)Math.Round(total), 5, 95),
            relationship, actorEffect, defenderEffect, check);
    }

    private RelationshipStats GetDirectionalStats(string direction) =>
        direction.ToUpperInvariant() switch
        {
            RelationshipDirection.ActorToPartner => ActorToPartner,
            RelationshipDirection.PartnerToActor => PartnerToActor,
			_ => throw new InvalidOperationException($"Unknown relationship direction '{direction}'.")
        };
}
