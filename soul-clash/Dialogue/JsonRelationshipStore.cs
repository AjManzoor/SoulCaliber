using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Temporary JSON save adapter; switch to SQLite later without touching the UI.
/// Owns TWO directional relationship records per pair.
/// </summary>
public sealed class JsonRelationshipStore : IRelationshipStore
{
	private const string SavePath = "user://soulcaliber_relationships_v2.json";
	private const string LegacyPath = "user://soulcaliber_relationships.json";
	private readonly Dictionary<string, RelationshipStats> _relationships =
		new(StringComparer.OrdinalIgnoreCase);

	public JsonRelationshipStore()
	{
		if (Godot.FileAccess.FileExists(SavePath))
		{
			using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
			if (file is null) return;
			try
			{
				var saved = JsonSerializer.Deserialize<Dictionary<string, RelationshipStats>>(file.GetAsText());
				if (saved is not null)
					foreach (var entry in saved)
						_relationships[entry.Key] = ClampAll(entry.Value);
			}
			catch (JsonException ex)
			{
				GD.PushWarning($"Relationship save not readable: {ex.Message}");
			}
		}
		else if (Godot.FileAccess.FileExists(LegacyPath))
		{
			// Legacy save had one 0..100 'opinion' score.
			// Trust is a pragmatic temporary mapping; the original save is retained.
			using var file = Godot.FileAccess.Open(LegacyPath, Godot.FileAccess.ModeFlags.Read);
			if (file is null) return;
			try
			{
				var old = JsonSerializer.Deserialize<Dictionary<string, int>>(file.GetAsText());
				if (old is null) return;
				foreach (var entry in old)
					_relationships[entry.Key] = new RelationshipStats
					{
						Trust = Math.Clamp((entry.Value - 50) * 2, -100, 100)
					};
				Save();
			}
			catch (JsonException ex)
			{
				GD.PushWarning($"Legacy relationship save not readable: {ex.Message}");
			}
		}
	}

	public RelationshipStats Get(string fromCharacterId, string toCharacterId) =>
		_relationships.TryGetValue(Key(fromCharacterId, toCharacterId), out var stats)
			? stats.Clone() : new RelationshipStats();

	public IReadOnlyList<AppliedRelationshipChange> Apply(
		string actorId, string partnerId, IReadOnlyList<RelationshipEffect> effects)
	{
		var changes = new List<AppliedRelationshipChange>();
		foreach (var effect in effects)
		{
			var (from, to) = RelationshipDirection.Resolve(effect.Direction, actorId, partnerId);
			var attribute = RelationshipStats.ParseAttribute(effect.Attribute);
			string key = Key(from, to);
			if (!_relationships.TryGetValue(key, out var stats))
				_relationships[key] = stats = new RelationshipStats();
			int before = stats.Get(attribute);
			stats.Set(attribute, before + effect.Delta);
			changes.Add(new AppliedRelationshipChange(from, to, attribute, before, stats.Get(attribute)));
		}

		if (effects.Count > 0) Save();
		return changes;
	}

	private static string Key(string from, string to) =>
		$"{from.ToUpperInvariant()}|{to.ToUpperInvariant()}";

	private static RelationshipStats ClampAll(RelationshipStats? values)
	{
		var safe = values ?? new RelationshipStats();
		foreach (RelationshipAttribute attr in Enum.GetValues<RelationshipAttribute>())
			safe.Set(attr, safe.Get(attr));
		return safe;
	}

	private void Save()
	{
		using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
		if (file is null)
		{
			GD.PushError($"Unable to save relationships: {Godot.FileAccess.GetOpenError()}");
			return;
		}
		file.StoreString(JsonSerializer.Serialize(_relationships, new JsonSerializerOptions { WriteIndented = true }));
	}
}
