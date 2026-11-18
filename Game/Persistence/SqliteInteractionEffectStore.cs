using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Uses a single savegame connection and SQLite transaction for numerical
/// relationships, statuses, memory occurrences, knowledge and interaction history.
/// Never opens or writes the master content database.
/// </summary>
public sealed class SqliteInteractionEffectStore : IInteractionOutcomeStore
{
    private readonly SqliteRelationshipStore _relationships;
    private readonly SqliteRelationshipStatusStore _statuses;
    private readonly SqliteMemoryStore? _memories;
    private readonly SqliteInteractionHistoryStore _history = new();

    public SqliteInteractionEffectStore(SqliteRelationshipStore relationships,
        SqliteRelationshipStatusStore statuses, SqliteMemoryStore? memories = null)
    {
        _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        _statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
        _memories = memories;
    }

    /// <summary>
    /// One resolved dialogue choice = one SQLite unit of work. Every SQL statement,
    /// including EF change tracking, raw status SQL, memory and knowledge rows,
    /// and history records uses the SAME connection/transaction. Never writes content.
    /// </summary>
    public AppliedInteractionEffects CommitResolvedChoice(ResolvedChoiceCommit choice)
    {
        if (choice is null) throw new ArgumentNullException(nameof(choice));
        if (choice.MemoryEffects.Count > 0 && _memories is null)
            throw new InvalidOperationException("Authored memory effects require a memory store in the outcome unit of work.");

        using var db = new SaveDbContext();
        using var tx = db.Database.BeginTransaction();
        var relationshipChanges = _relationships.ApplyInTransaction(db,
            choice.ActorId, choice.PartnerId, choice.RelationshipEffects);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var sqliteTx = (SqliteTransaction)tx.GetDbTransaction();
        var statusChanges = _statuses.ApplyInTransaction(connection, sqliteTx,
            choice.ActorId, choice.PartnerId, choice.StatusEffects, choice.PairStatusEffects);

        // These must be AFTER status effects to preserve existing ally-knowledge ordering.
        foreach (var memory in choice.MemoryEffects)
            _memories!.RecordInTransaction(connection, sqliteTx, memory.MemoryKey,
                choice.ActorId, choice.ActorId, memory.AffectedCharacterIds,
                memory.SeverityOverride, memory.KnowledgeOverride, memory.CustomAudienceJson);

        // Also record choices without stat changes: history is part of this same commit.
        _history.RecordInTransaction(db, choice, relationshipChanges, statusChanges);
        tx.Commit();
        return new AppliedInteractionEffects(relationshipChanges, statusChanges);
    }

    public AppliedInteractionEffects Apply(string actorId, string partnerId,
        IReadOnlyList<RelationshipEffect> relationshipEffects,
        IReadOnlyList<RelationshipStatusEffect> statusEffects)
    {
        if (relationshipEffects.Count == 0 && statusEffects.Count == 0)
            return new AppliedInteractionEffects(
                Array.Empty<AppliedRelationshipChange>(),
                Array.Empty<AppliedRelationshipStatusChange>());

        using var db = new SaveDbContext();
        // EF and the raw status SQL must be enlisted in this SAME transaction.
        using var tx = db.Database.BeginTransaction();
        var relationshipChanges = _relationships.ApplyInTransaction(db,
            actorId, partnerId, relationshipEffects);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var sqliteTx = (SqliteTransaction)tx.GetDbTransaction();
        var statusChanges = _statuses.ApplyInTransaction(connection, sqliteTx,
            actorId, partnerId, statusEffects);
        // Any exception above disposes an uncommitted tx, rolling back both effects.
        tx.Commit();
        return new AppliedInteractionEffects(relationshipChanges, statusChanges);
    }
}
