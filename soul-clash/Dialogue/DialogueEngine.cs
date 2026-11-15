using System;
using System.Collections.Generic;
using System.Linq;
using SoulCaliber.Shared;

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

	// All three IDs are supplied by the caller. No participant is stored on an
	// interaction definition, and no fallback to the legacy HostCharacterId exists.
	public DialogueEngine(DialogueRepository data, IRelationshipStore relationships,
		string interactionKey, string actorCharacterId, string partnerCharacterId)
	{
		if (string.IsNullOrWhiteSpace(interactionKey))
			throw new ArgumentException("Interaction key is required.", nameof(interactionKey));
		if (string.IsNullOrWhiteSpace(actorCharacterId))
			throw new ArgumentException("Actor character ID is required.", nameof(actorCharacterId));
		if (string.IsNullOrWhiteSpace(partnerCharacterId))
			throw new ArgumentException("Partner character ID is required. Set PartnerCharacterId on the scene or pass it through InteractionLauncher.", nameof(partnerCharacterId));

		_data = data;
		_relationships = relationships;
		Definition = data.Interaction(interactionKey.Trim());
		Visitor = data.Character(actorCharacterId.Trim());
		Host = data.Character(partnerCharacterId.Trim());
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
			Visitor.Id, Host.Id, succeeded, ActorToPartner, PartnerToActor);
		var partnerLine = _data.HostLine(Definition.Key, CurrentNode, option, Host.Id, Visitor.Id, succeeded, ActorToPartner, PartnerToActor);
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

	public string Introduction() => _data.Introduction(Definition.Key, CurrentNode, Host.Id, Visitor.Id, ActorToPartner, PartnerToActor)
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
		var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
		string profileKey = _data.OptionProfile(Definition.Key, CurrentNode.Key, option.Key);
		ChanceResult chance = CalculateChance(option);
		int? actorScore = check is null ? null : ActorPersonality.Get(check.ActorSkill);
		int? defenderScore = check is null ? null : PartnerPersonality.Get(check.DefenderSkill);
		return ChanceFormula.Explain(chance, option.BaseSuccessChance, profileKey,
			actorScore, defenderScore, check?.ActorFactor ?? 0, check?.DefenderFactor ?? 0);
	}

	private int Chance(DialogueOption option) => CalculateChance(option).FinalChance;

	private ChanceResult CalculateChance(DialogueOption option)
	{
		var check = _data.SkillCheck(Definition.Key, CurrentNode.Key, option.Key);
		string profileKey = _data.OptionProfile(Definition.Key, CurrentNode.Key, option.Key);
		var profile = _data.CheckProfile(profileKey);
		var terms = option.ChanceModifiers.Select(m =>
			new ChanceFactor(m.Direction, m.Attribute, m.Factor));
		return ChanceFormula.Calculate(option.BaseSuccessChance,
			check is null ? null : ActorPersonality.Get(check.ActorSkill),
			check is null ? null : PartnerPersonality.Get(check.DefenderSkill),
			check?.ActorFactor ?? 0, check?.DefenderFactor ?? 0,
			profile, terms,
			(direction, attribute) => GetDirectionalStats(direction)
				.Get(RelationshipStats.ParseAttribute(attribute)));
	}

	private RelationshipStats GetDirectionalStats(string direction) =>
		direction.ToUpperInvariant() switch
		{
			RelationshipDirection.ActorToPartner => ActorToPartner,
			RelationshipDirection.PartnerToActor => PartnerToActor,
			_ => throw new InvalidOperationException($"Unknown relationship direction '{direction}'.")
		};
}
