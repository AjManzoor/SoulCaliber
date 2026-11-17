using System;
using Godot;

/// <summary>
/// Instantiate a configured interaction. The caller must add it to the scene tree
/// AFTER configuration, because InteractionScene creates its engine in _Ready().
/// Use this from a future menu, overworld or scripted event.
/// </summary>
public static class InteractionLauncher
{
	public static InteractionScene Create(string actorId, string partnerId, string interactionKey)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			throw new ArgumentException("Actor ID is required.", nameof(actorId));
		if (string.IsNullOrWhiteSpace(partnerId))
			throw new ArgumentException("Partner ID is required.", nameof(partnerId));
		if (string.IsNullOrWhiteSpace(interactionKey))
			throw new ArgumentException("Interaction key is required.", nameof(interactionKey));
		if (string.Equals(actorId, partnerId, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("An interaction requires two different characters.");

		var packed = GD.Load<PackedScene>("res://Scenes/InteractionScene.tscn")
			?? throw new InvalidOperationException("InteractionScene.tscn could not be loaded.");
		var scene = packed.Instantiate<InteractionScene>();
		scene.VisitorCharacterId = actorId.Trim();
		scene.PartnerCharacterId = partnerId.Trim();
		scene.InteractionKey = interactionKey.Trim();
		// Explicitly requested dialogue is a direct/test launch, not an approach.
		scene.EnableSpecialEncounterSelection = false;
		return scene;
	}

	/// <summary>
	/// Begin a normal character approach. The authored interaction is the fallback;
	/// an eligible automatic special encounter can replace it at _Ready().
	/// Add the returned scene to the tree AFTER setting its properties.
	/// </summary>
	public static InteractionScene CreateForApproach(string actorId, string partnerId,
		string normalInteractionKey)
	{
		var scene = Create(actorId, partnerId, normalInteractionKey);
		scene.EnableSpecialEncounterSelection = true;
		return scene;
	}
}
