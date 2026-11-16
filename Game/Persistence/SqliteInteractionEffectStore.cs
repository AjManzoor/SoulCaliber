using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Uses a single savegame connection and SQLite transaction for both numerical
/// relationship attributes and discrete Ally/Enemy/custom status overrides.
/// Never opens or writes the master content database.
/// </summary>
public sealed class SqliteInteractionEffectStore : IInteractionEffectStore
{
    private readonly SqliteRelationshipStore _relationships;
    private readonly SqliteRelationshipStatusStore _statuses;

    public SqliteInteractionEffectStore(SqliteRelationshipStore relationships,
        SqliteRelationshipStatusStore statuses)
    {
        _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
        _statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
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
