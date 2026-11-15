using System;
using System.Collections.Generic;

namespace SoulCaliber.Dialogue;

public sealed class CharacterDefinition
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string DefaultImage { get; set; } = "";
	// Generic outcome portraits. These are optional so existing character data keeps working.
	// A character can be the actor OR partner, and each role uses the same images.
	public string WinImage { get; set; } = "";
	public string LoseImage { get; set; } = "";
	// Reaction portraits for when this character is the TARGET of an action.
	// Keys are descriptive states such as INTIMIDATED, DISMISSIVE, PLEASED.
	// Unlike WinImage/LoseImage, these do not imply the target made a roll.
	public Dictionary<string, string> ReactionImages { get; set; } = new();

	public string ReactionImage(string reactionKey)
	{
		if (ReactionImages is null) return "";
		foreach (var pair in ReactionImages)
			if (string.Equals(pair.Key, reactionKey, StringComparison.OrdinalIgnoreCase))
				return pair.Value;
		return "";
	}
}

/// <summary>
/// Optional reusable reaction-portrait library in Data/reaction_images.json.
/// Each character's reactions work with ANY interaction that emits the same key.
/// Existing characters.json reactionImages entries take precedence.
/// </summary>
public sealed class ReactionImageCatalog
{
	public List<CharacterReactionImages> Characters { get; set; } = new();
}

public sealed class CharacterReactionImages
{
	public string CharacterId { get; set; } = "";
	public Dictionary<string, string> ReactionImages { get; set; } = new();
}

public sealed class InteractionDefinition
{
	public string Key { get; set; } = "";
	public string Title { get; set; } = "";
	// Optional default only. The Godot scene can override it with ANY character ID.
	public string HostCharacterId { get; set; } = "";
	public string StartNodeKey { get; set; } = "";
	public List<DialogueNode> Nodes { get; set; } = new();
}

public sealed class DialogueNode
{
	public string Key { get; set; } = "";
	public int Round { get; set; }
	public string HostIntroduction { get; set; } = "";
	public List<DialogueOption> Options { get; set; } = new();
}

public sealed class DialogueOption
{
	public string Key { get; set; } = "";
	public string Label { get; set; } = "";
	public int BaseSuccessChance { get; set; } = 50;

	// Rules and effects are DATA. Add or remove attributes without adding scene code.
	public List<RelationshipRequirement> Requirements { get; set; } = new();
	public List<ChanceModifier> ChanceModifiers { get; set; } = new();
	public List<RelationshipEffect> SuccessEffects { get; set; } = new();
	public List<RelationshipEffect> FailureEffects { get; set; } = new();

	public string? SuccessNextNodeKey { get; set; }
	public string? FailureNextNodeKey { get; set; }
	public string DefaultPlayerLine { get; set; } = "";
	public string DefaultHostSuccessLine { get; set; } = "";
	public string DefaultHostFailureLine { get; set; } = "";
	// The partner REACTS to the player's result; they do not win/lose a roll.
	// Defaults keep existing interaction JSON working without edits.
	public string SuccessTargetReaction { get; set; } = "RECEPTIVE";
	public string FailureTargetReaction { get; set; } = "DISMISSIVE";
}

public sealed class CharacterLine
{
	public string CharacterId { get; set; } = "";
	public string InteractionKey { get; set; } = "";
	public string NodeKey { get; set; } = "";
	public string OptionKey { get; set; } = "";
	// For the partner's answer, an optional actor ID and outcome give specific reactions.
	public string? VisitorId { get; set; }
	public string? Outcome { get; set; }
	public string Text { get; set; } = "";
	public string ImagePath { get; set; } = "";
}

public sealed class LinesFile
{
	public List<CharacterLine> Lines { get; set; } = new();
}

public sealed class CharactersFile
{
	public List<CharacterDefinition> Characters { get; set; } = new();
}

public sealed record AvailableOption(DialogueOption Definition, int Chance, bool IsLocked, string LockReason);

public sealed record DialogueResolution(
	DialogueOption Choice,
	CharacterLine PlayerLine,
	CharacterLine HostLine,
	bool Succeeded,
	int Roll,
	int Chance,
	IReadOnlyList<AppliedRelationshipChange> Changes,
	string? NextNodeKey,
	string TargetReaction);
