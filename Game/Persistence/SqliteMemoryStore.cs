using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;

namespace SoulCaliber.Dialogue;

/// <summary>The authored memory type is read from published content, never from the savegame.</summary>
public sealed record MemoryTypeInfo(string Key, string Name, string DefaultSeverity,
	string DefaultKnowledge, bool IsActive);

/// <summary>One historical incident, potentially affecting several characters.</summary>
public sealed record MemoryOccurrence(long Id, string MemoryKey, string ActorCharacterId,
	string PlayerCharacterId, string Severity, string KnowledgeLevel,
	DateTimeOffset OccurredUtc, IReadOnlyList<string> AffectedCharacterIds);

/// <summary>
/// Persistent world incidents and individual character knowledge.
/// Public Record() commits standalone events; RecordInTransaction() joins dialogue outcomes.
/// All writes go to user://soulcaliber_savegame.db; published content is read-only.
/// </summary>
public sealed class SqliteMemoryStore
{
	private readonly Dictionary<string, MemoryTypeInfo> _types =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _characters = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, KnowledgeAudienceSpec> _customTypeAudiences = new(StringComparer.OrdinalIgnoreCase);
	private readonly SqliteRelationshipStatusStore _statuses;

	public SqliteMemoryStore(SqliteRelationshipStatusStore statuses)
	{
		_statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
		using (var content = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = DatabasePaths.Content,
			Mode = SqliteOpenMode.ReadOnly,
			Pooling = false,
			ForeignKeys = true
		}.ToString()))
		{
			content.Open();
			using (var exists = content.CreateCommand())
			{
				exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='MemoryTypes'";
				if (exists.ExecuteScalar() is null)
					throw new InvalidOperationException(
						"The published content has no MemoryTypes table. Publish your Phase 13A Memory Catalogue from Studio first.");
			}
			using (var command = content.CreateCommand())
			{
				command.CommandText = "SELECT Key,Name,DefaultSeverity,DefaultKnowledge,IsActive FROM MemoryTypes";
				using var reader = command.ExecuteReader();
				while (reader.Read())
				{
					var memory = new MemoryTypeInfo(Key(reader.GetString(0)), reader.GetString(1),
						Key(reader.GetString(2)), Key(reader.GetString(3)), reader.GetInt32(4) == 1);
					_types.Add(memory.Key, memory);
				}
			}
			using (var existsAudience = content.CreateCommand())
			{
				existsAudience.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='MemoryTypeKnowledgeAudiences'";
				if (existsAudience.ExecuteScalar() is not null)
				{
					using var command = content.CreateCommand();
					command.CommandText = "SELECT MemoryKey,AudienceJson FROM MemoryTypeKnowledgeAudiences";
					using var reader = command.ExecuteReader();
					while (reader.Read())
						_customTypeAudiences.Add(Key(reader.GetString(0)), KnowledgeAudienceSpec.Parse(reader.GetString(1)));
				}
			}
			using (var command = content.CreateCommand())
			{
				command.CommandText = "SELECT Id FROM Characters";
				using var reader = command.ExecuteReader();
				while (reader.Read()) _characters.Add(Key(reader.GetString(0)));
			}
		}

		// SaveGameSchemaManager creates and validates the save tables before use.
	}

	public IReadOnlyList<MemoryTypeInfo> Types => _types.Values.OrderBy(x => x.Key).ToList();

	private static string Key(string value) =>
		(value ?? throw new ArgumentNullException(nameof(value))).Trim().ToUpperInvariant();

	private void RequireCharacter(string value)
	{
		if (!_characters.Contains(value))
			throw new InvalidOperationException($"Unknown character '{value}' in published content.");
	}

	private static SqliteConnection OpenSave()
	{
		var connection = new SqliteConnection(DatabasePaths.ConnectionString(DatabasePaths.Save));
		connection.Open();
		return connection;
	}

	private static void Add(SqliteCommand command, string parameter, object? value) =>
		command.Parameters.AddWithValue(parameter, value ?? DBNull.Value);

	/// <summary>
	/// Records exactly one incident, including its affected characters and initial knowers.
	/// Quiet: player + affected. Allies: those people + each side's current outgoing ALLY links.
    /// Global: universally known, including to future roster members; no knowledge rows needed.
    /// Severity and knowledge can override the catalogue defaults for this occurrence.
    /// </summary>
    public MemoryOccurrence Record(string memoryKey, string actorCharacterId,
        string playerCharacterId, IReadOnlyCollection<string> affectedCharacterIds,
        string? severityOverride = null, string? knowledgeOverride = null, string? customAudienceJson = null)
    {
        using var save = OpenSave();
        using var transaction = save.BeginTransaction();
        var occurrence = RecordInTransaction(save, transaction, memoryKey, actorCharacterId,
            playerCharacterId, affectedCharacterIds, severityOverride, knowledgeOverride, customAudienceJson);
        transaction.Commit();
        return occurrence;
    }

    /// <summary>
    /// Enlist one occurrence, all affected-character rows and all initial knowers
	/// in the caller's savegame transaction. Never creates a connection or commits.
	/// Allies are read through the SAME transaction, so status effects from this
	/// dialogue choice affect distribution exactly as in the previous sequential flow.
	/// </summary>
	internal MemoryOccurrence RecordInTransaction(SqliteConnection save, SqliteTransaction transaction,
		string memoryKey, string actorCharacterId, string playerCharacterId,
		IReadOnlyCollection<string> affectedCharacterIds,
		string? severityOverride = null, string? knowledgeOverride = null, string? customAudienceJson = null)
	{
		if (save is null) throw new ArgumentNullException(nameof(save));
		if (transaction is null) throw new ArgumentNullException(nameof(transaction));
		string key = Key(memoryKey), actor = Key(actorCharacterId), player = Key(playerCharacterId);
		if (!_types.TryGetValue(key, out var definition) || !definition.IsActive)
			throw new InvalidOperationException($"Unknown or inactive memory type: '{key}'.");
		RequireCharacter(actor);
		RequireCharacter(player);
		if (affectedCharacterIds is null) throw new ArgumentNullException(nameof(affectedCharacterIds));
		string[] affected = affectedCharacterIds.Select(Key)
			.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		foreach (string affectedId in affected) RequireCharacter(affectedId);

		string severity = severityOverride is null ? definition.DefaultSeverity : Key(severityOverride);
		KnowledgeAudienceSpec? customAudience = customAudienceJson is not null
			? KnowledgeAudienceSpec.Parse(customAudienceJson)
			: knowledgeOverride is null && _customTypeAudiences.TryGetValue(key, out var typeAudience)
				? typeAudience : null;
		string knowledge = customAudience is not null ? "CUSTOM"
			: knowledgeOverride is null ? definition.DefaultKnowledge : Key(knowledgeOverride);
		if (severity is not ("MAJOR" or "MINOR"))
			throw new ArgumentException("Memory severity must be MAJOR or MINOR.", nameof(severityOverride));
		if (knowledge is not ("QUIET" or "ALLIES" or "GLOBAL" or "CUSTOM"))
			throw new ArgumentException("Knowledge must be QUIET, ALLIES, GLOBAL or CUSTOM.", nameof(knowledgeOverride));

		var knowers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { player };
		knowers.UnionWith(affected);
		if (knowledge == "CUSTOM")
		{
			knowers = KnowledgeAudienceRules.Resolve(customAudience!, actor, affected, _characters,
				(source, candidate) => _statuses.GetInTransaction(save, transaction, source, candidate))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (string knower in knowers) RequireCharacter(knower);
		}
		if (knowledge == "ALLIES")
		{
			// Outgoing ALLY relationships are directional, and pending status effects
			// from this choice have already been flushed to this transaction.
			foreach (string source in new[] { player }.Concat(affected).Distinct(StringComparer.OrdinalIgnoreCase))
				foreach (string candidate in _characters)
					if (candidate != source && _statuses.GetInTransaction(save, transaction, source, candidate) == "ALLY")
						knowers.Add(candidate);
		}

		string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
		using (var insert = save.CreateCommand())
		{
			insert.Transaction = transaction;
			insert.CommandText = @"
                INSERT INTO SavedMemoryOccurrences
                    (MemoryKey,ActorCharacterId,PlayerCharacterId,Severity,KnowledgeLevel,OccurredUtc)
                VALUES($memory,$actor,$player,$severity,$knowledge,$utc)
				";
			Add(insert, "$memory", key);
			Add(insert, "$actor", actor);
			Add(insert, "$player", player);
			Add(insert, "$severity", severity);
			// Preset column stays backward compatible; custom mode is recorded
			// separately, while individual knowledge rows remain authoritative.
			Add(insert, "$knowledge", knowledge == "CUSTOM" ? "QUIET" : knowledge);
			Add(insert, "$utc", now);
			insert.ExecuteNonQuery();
		}
		long id;
		using (var getId = save.CreateCommand())
		{
			getId.Transaction = transaction;
			getId.CommandText = "SELECT last_insert_rowid()";
			id = (long)getId.ExecuteScalar()!;
		}
		if (knowledge == "CUSTOM")
		{
			using var mode = save.CreateCommand();
			mode.Transaction = transaction;
			mode.CommandText = "INSERT INTO SavedMemoryAudienceModes(MemoryId,Mode) VALUES($id,'CUSTOM')";
			Add(mode, "$id", id);
			mode.ExecuteNonQuery();
		}
		foreach (string target in affected)
		{
			using var add = save.CreateCommand();
			add.Transaction = transaction;
			add.CommandText = "INSERT INTO SavedMemoryAffectedCharacters(MemoryId,CharacterId) VALUES($id,$character)";
			Add(add, "$id", id);
			Add(add, "$character", target);
			add.ExecuteNonQuery();
		}
		if (knowledge != "GLOBAL")
			foreach (string knower in knowers)
			{
				using var add = save.CreateCommand();
				add.Transaction = transaction;
				add.CommandText = "INSERT INTO SavedMemoryKnowledge(MemoryId,CharacterId) VALUES($id,$character)";
				Add(add, "$id", id);
				Add(add, "$character", knower);
				add.ExecuteNonQuery();
			}
		return new MemoryOccurrence(id, key, actor, player, severity, knowledge,
			DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), affected);
	}

	/// <summary>Returns separate occurrences, optionally filtered by historical participants and a current knower.</summary>
	public IReadOnlyList<MemoryOccurrence> Find(string? memoryKey = null, string? actorCharacterId = null,
		string? affectedCharacterId = null, string? knownByCharacterId = null)
	{
		string? tag = memoryKey is null ? null : Key(memoryKey);
		string? actor = actorCharacterId is null ? null : Key(actorCharacterId);
		string? affected = affectedCharacterId is null ? null : Key(affectedCharacterId);
		string? knower = knownByCharacterId is null ? null : Key(knownByCharacterId);
		if (knower is not null) RequireCharacter(knower);
		using var save = OpenSave();
		var rows = new List<(long Id, string Memory, string Actor, string Player,
			string Severity, string Knowledge, string Utc)>();
		using (var query = save.CreateCommand())
		{
			query.CommandText = @"
                SELECT m.Id,m.MemoryKey,m.ActorCharacterId,m.PlayerCharacterId,
                       m.Severity,CASE WHEN am.Mode='CUSTOM' THEN 'CUSTOM' ELSE m.KnowledgeLevel END,m.OccurredUtc
                FROM SavedMemoryOccurrences m
                LEFT JOIN SavedMemoryAudienceModes am ON am.MemoryId=m.Id
                WHERE ($memory IS NULL OR m.MemoryKey=$memory)
                  AND ($actor IS NULL OR m.ActorCharacterId=$actor)
                  AND ($affected IS NULL OR EXISTS (
                    SELECT 1 FROM SavedMemoryAffectedCharacters a
                    WHERE a.MemoryId=m.Id AND a.CharacterId=$affected))
                  AND ($knower IS NULL OR m.KnowledgeLevel='GLOBAL' OR EXISTS (
                    SELECT 1 FROM SavedMemoryKnowledge k
                    WHERE k.MemoryId=m.Id AND k.CharacterId=$knower))
                ORDER BY m.Id
				";
			Add(query, "$memory", tag);
			Add(query, "$actor", actor);
			Add(query, "$affected", affected);
			Add(query, "$knower", knower);
			using var reader = query.ExecuteReader();
			while (reader.Read())
				rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
					reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
		}
		var results = new List<MemoryOccurrence>();
		foreach (var row in rows)
		{
			using var query = save.CreateCommand();
			query.CommandText = "SELECT CharacterId FROM SavedMemoryAffectedCharacters WHERE MemoryId=$id ORDER BY CharacterId";
			Add(query, "$id", row.Id);
			var affectedCharacters = new List<string>();
			using var reader = query.ExecuteReader();
			while (reader.Read()) affectedCharacters.Add(reader.GetString(0));
			results.Add(new MemoryOccurrence(row.Id, row.Memory, row.Actor, row.Player,
				row.Severity, row.Knowledge,
				DateTimeOffset.Parse(row.Utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
				affectedCharacters));
		}
		return results;
	}

	/// <summary>Load only the selected historical occurrence, not the newest incident of this type.</summary>
	public MemoryOccurrence? FindById(long occurrenceId)
	{
		if (occurrenceId <= 0) throw new ArgumentOutOfRangeException(nameof(occurrenceId));
		using var save = OpenSave();
		string key, actor, player, severity, knowledge, utc;
		using (var query = save.CreateCommand())
		{
			query.CommandText = @"
                SELECT m.MemoryKey,m.ActorCharacterId,m.PlayerCharacterId,m.Severity,
                       CASE WHEN am.Mode='CUSTOM' THEN 'CUSTOM' ELSE m.KnowledgeLevel END,
                       m.OccurredUtc
                FROM SavedMemoryOccurrences m
                LEFT JOIN SavedMemoryAudienceModes am ON am.MemoryId=m.Id
				WHERE m.Id=$id";
			Add(query, "$id", occurrenceId);
			using var reader = query.ExecuteReader();
			if (!reader.Read()) return null;
			key = reader.GetString(0); actor = reader.GetString(1);
			player = reader.GetString(2); severity = reader.GetString(3);
			knowledge = reader.GetString(4); utc = reader.GetString(5);
		}
		var affected = new List<string>();
		using (var query = save.CreateCommand())
		{
			query.CommandText = "SELECT CharacterId FROM SavedMemoryAffectedCharacters WHERE MemoryId=$id ORDER BY CharacterId";
			Add(query, "$id", occurrenceId);
			using var reader = query.ExecuteReader();
			while (reader.Read()) affected.Add(reader.GetString(0));
		}
		return new MemoryOccurrence(occurrenceId, key, actor, player, severity, knowledge,
			DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), affected);
	}

	/// <summary>Efficient COUNT for dialogue conditions; does not materialise occurrences.</summary>
	public int Count(string? memoryKey = null, string? actorCharacterId = null,
		string? affectedCharacterId = null, string? knownByCharacterId = null)
	{
		string? tag = memoryKey is null ? null : Key(memoryKey);
		string? actor = actorCharacterId is null ? null : Key(actorCharacterId);
		string? affected = affectedCharacterId is null ? null : Key(affectedCharacterId);
		string? knower = knownByCharacterId is null ? null : Key(knownByCharacterId);
		if (knower is not null) RequireCharacter(knower);
		using var save = OpenSave();
		using var query = save.CreateCommand();
		query.CommandText = @"
            SELECT COUNT(*) FROM SavedMemoryOccurrences m
            WHERE ($memory IS NULL OR m.MemoryKey=$memory)
              AND ($actor IS NULL OR m.ActorCharacterId=$actor)
              AND ($affected IS NULL OR EXISTS (
                SELECT 1 FROM SavedMemoryAffectedCharacters a
                WHERE a.MemoryId=m.Id AND a.CharacterId=$affected))
              AND ($knower IS NULL OR m.KnowledgeLevel='GLOBAL' OR EXISTS (
                SELECT 1 FROM SavedMemoryKnowledge k
                WHERE k.MemoryId=m.Id AND k.CharacterId=$knower))
			";
		Add(query, "$memory", tag);
		Add(query, "$actor", actor);
		Add(query, "$affected", affected);
		Add(query, "$knower", knower);
		return Convert.ToInt32((long)query.ExecuteScalar()!);
	}

	/// <summary>Used later by authored knowledge-sharing actions. Never removes a memory.</summary>
	public bool GrantKnowledge(long occurrenceId, string characterId)
	{
		string knower = Key(characterId);
		RequireCharacter(knower);
		using var save = OpenSave();
		using var tx = save.BeginTransaction();
		string level;
		using (var query = save.CreateCommand())
		{
			query.Transaction = tx;
			query.CommandText = "SELECT KnowledgeLevel FROM SavedMemoryOccurrences WHERE Id=$id";
			Add(query, "$id", occurrenceId);
			level = query.ExecuteScalar() as string
				?? throw new InvalidOperationException($"Memory occurrence {occurrenceId} does not exist.");
		}
		if (level == "GLOBAL") return false; // Everyone already knows.
		using var add = save.CreateCommand();
		add.Transaction = tx;
		add.CommandText = "INSERT OR IGNORE INTO SavedMemoryKnowledge(MemoryId,CharacterId) VALUES($id,$knower)";
		Add(add, "$id", occurrenceId);
		Add(add, "$knower", knower);
		bool changed = add.ExecuteNonQuery() > 0;
		tx.Commit();
		return changed;
	}
}
