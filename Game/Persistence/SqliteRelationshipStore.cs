using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace SoulCaliber.Dialogue;

/// <summary>SQLite savegame: one directional row per (from, to, attribute).</summary>
public sealed class SqliteRelationshipStore : IRelationshipStore
{
    private readonly RelationshipCatalogue _catalogue;
    public SqliteRelationshipStore()
    {
        _catalogue = RelationshipCatalogue.Load();
        using var db = new SaveDbContext();
        db.Database.EnsureCreated();
        // Fail explicitly if an old six-column savegame was not actually deleted.
        using var connection = new SqliteConnection(DatabasePaths.ConnectionString(DatabasePaths.Save));
        connection.Open();
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='SavedRelationshipValues'";
        if (check.ExecuteScalar() is null)
            throw new InvalidOperationException("Legacy savegame schema found. Back up/delete the old soulcaliber_savegame.db and its -wal/-shm companions while the game is closed.");
    }

    public RelationshipStats Get(string fromCharacterId, string toCharacterId)
    {
        string from = fromCharacterId.ToUpperInvariant();
        string to = toCharacterId.ToUpperInvariant();
        using var db = new SaveDbContext();
        var rows = db.RelationshipValues.AsNoTracking()
            .Where(x => x.FromCharacterId == from && x.ToCharacterId == to).ToList();
        var values = new RelationshipStats(_catalogue);
        foreach (var row in rows)
        {
            // A save may contain values for now-deleted catalogue entries. Ignore only
            // these historical keys; current content never receives invented values.
            if (_catalogue.All.Any(a => a.Key.Equals(row.AttributeKey,StringComparison.OrdinalIgnoreCase)))
                values.Set(row.AttributeKey,row.Value);
        }
        return values;
    }

    public IReadOnlyList<AppliedRelationshipChange> Apply(
        string actorId, string partnerId, IReadOnlyList<RelationshipEffect> effects)
    {
        if (effects.Count == 0) return Array.Empty<AppliedRelationshipChange>();
        using var db = new SaveDbContext();
        using var tx = db.Database.BeginTransaction();
        var changes = ApplyInTransaction(db, actorId, partnerId, effects);
        tx.Commit();
        return changes;
    }

    /// <summary>
    /// Applies numerical changes within a caller-owned transaction. Does not commit.
    /// Used by the combined dialogue-effect writer to avoid partial outcomes.
    /// </summary>
    internal IReadOnlyList<AppliedRelationshipChange> ApplyInTransaction(
        SaveDbContext db, string actorId, string partnerId,
        IReadOnlyList<RelationshipEffect> effects)
    {
        var changes = new List<AppliedRelationshipChange>();
        foreach (var effect in effects)
        {
            var (fromId, toId) = RelationshipDirection.Resolve(effect.Direction, actorId, partnerId);
            string from = fromId.ToUpperInvariant(), to = toId.ToUpperInvariant();
            var definition = _catalogue.Get(effect.Attribute);
            if (!definition.IsActive)
                throw new InvalidOperationException($"Inactive relationship attribute used by an effect: {definition.Key}");
            var row = db.RelationshipValues.Local.FirstOrDefault(x =>
                x.FromCharacterId == from && x.ToCharacterId == to && x.AttributeKey == definition.Key)
                ?? db.RelationshipValues.Find(from, to, definition.Key);
            if (row is null)
            {
                row = new SavedRelationshipValue
                {
                    FromCharacterId = from, ToCharacterId = to,
                    AttributeKey = definition.Key, Value = definition.Default
                };
                db.RelationshipValues.Add(row);
            }
            int before = Math.Clamp(row.Value, definition.Minimum, definition.Maximum);
            int after = Math.Clamp((int)Math.Clamp((long)before + effect.Delta,
                definition.Minimum, definition.Maximum), definition.Minimum, definition.Maximum);
            row.Value = after;
            changes.Add(new AppliedRelationshipChange(from, to, definition.Key, before, after));
        }
        if (changes.Count > 0)
            db.SaveChanges(); // Flush within the caller's transaction, never commit here.
        return changes;
    }
}

public sealed class SqliteInteractionHistoryStore
{
    /// <summary>Flush a single resolved choice history entry inside the caller's transaction.</summary>
    internal void RecordInTransaction(SaveDbContext db, ResolvedChoiceCommit choice,
        IReadOnlyList<AppliedRelationshipChange> changes,
        IReadOnlyList<AppliedRelationshipStatusChange> statusChanges)
    {
        var row = new SavedInteraction
        {
            OccurredUtc = DateTime.UtcNow,
            InteractionKey = choice.InteractionKey,
            NodeKey = choice.NodeKey,
            OptionKey = choice.OptionKey,
            ActorId = choice.ActorId,
            PartnerId = choice.PartnerId,
            Succeeded = choice.Succeeded,
            Roll = choice.Roll,
            Chance = choice.Chance,
            TargetReaction = choice.TargetReaction
        };
        foreach (var change in changes)
            row.Changes.Add(new SavedRelationshipChange
            {
                FromCharacterId = change.FromId, ToCharacterId = change.ToId,
                Attribute = change.Attribute, Before = change.Before, After = change.After
            });
        foreach (var change in statusChanges)
            row.StatusChanges.Add(new SavedInteractionStatusChange
            {
                FromCharacterId = change.FromId, ToCharacterId = change.ToId,
                Before = change.Before, After = change.After
            });
        db.InteractionHistory.Add(row);
        db.SaveChanges(); // Flush only. Caller owns the commit/rollback.
    }

    public void Record(DialogueEngine engine,DialogueResolution result)
    {
        using var db = new SaveDbContext();
        db.Database.EnsureCreated();
        var row = new SavedInteraction
        {
            OccurredUtc=DateTime.UtcNow, InteractionKey=engine.Definition.Key,
            NodeKey=engine.CurrentNode.Key, OptionKey=result.Choice.Key,
            ActorId=engine.Visitor.Id, PartnerId=engine.Host.Id,
            Succeeded=result.Succeeded, Roll=result.Roll, Chance=result.Chance,
            TargetReaction=result.TargetReaction
        };
        foreach (var change in result.Changes)
            row.Changes.Add(new SavedRelationshipChange
            {
                FromCharacterId=change.FromId, ToCharacterId=change.ToId,
                Attribute=change.Attribute, Before=change.Before, After=change.After
            });
        foreach (var change in result.StatusChanges)
            row.StatusChanges.Add(new SavedInteractionStatusChange
            {
                FromCharacterId=change.FromId, ToCharacterId=change.ToId,
                Before=change.Before, After=change.After
            });
        db.InteractionHistory.Add(row);
        db.SaveChanges();
    }
}
