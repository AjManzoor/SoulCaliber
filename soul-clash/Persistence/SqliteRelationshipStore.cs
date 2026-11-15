using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Replaces JsonRelationshipStore with two directional SQL rows per pair.
/// Migrates existing JSON data only when savegame.db is empty; never deletes the backup.
/// </summary>
public sealed class SqliteRelationshipStore : IRelationshipStore
{
    private const string OldSave = "user://soulcaliber_relationships_v2.json";
    private const string VeryOldSave = "user://soulcaliber_relationships.json";

    public SqliteRelationshipStore()
    {
        using var db = new SaveDbContext();
        db.Database.EnsureCreated();
        if (db.Relationships.Any()) return;

        if (Godot.FileAccess.FileExists(OldSave))
        {
            using var file = Godot.FileAccess.Open(OldSave, Godot.FileAccess.ModeFlags.Read);
            var saved = JsonSerializer.Deserialize<Dictionary<string, RelationshipStats>>(file.GetAsText());
            if (saved is not null)
            {
                foreach (var (pair, values) in saved)
                    ImportPair(db, pair, values);
                db.SaveChanges();
                GD.Print($"Migrated {saved.Count} directional relationships from existing JSON save.");
            }
        }
        else if (Godot.FileAccess.FileExists(VeryOldSave))
        {
            using var file = Godot.FileAccess.Open(VeryOldSave, Godot.FileAccess.ModeFlags.Read);
            var saved = JsonSerializer.Deserialize<Dictionary<string, int>>(file.GetAsText());
            if (saved is not null)
            {
                foreach (var (pair, opinion) in saved)
                    ImportPair(db, pair, new RelationshipStats
                    {
                        Trust = Math.Clamp((opinion - 50) * 2, -100, 100)
                    });
                db.SaveChanges();
            }
        }
    }

    public RelationshipStats Get(string fromCharacterId, string toCharacterId)
    {
        using var db = new SaveDbContext();
        var row = db.Relationships.AsNoTracking().FirstOrDefault(r =>
            r.FromCharacterId == fromCharacterId.ToUpperInvariant() &&
            r.ToCharacterId == toCharacterId.ToUpperInvariant());
        return row is null ? new RelationshipStats() : ToStats(row);
    }

    public IReadOnlyList<AppliedRelationshipChange> Apply(
        string actorId, string partnerId, IReadOnlyList<RelationshipEffect> effects)
    {
        if (effects.Count == 0) return Array.Empty<AppliedRelationshipChange>();
        using var db = new SaveDbContext();
        using var tx = db.Database.BeginTransaction();
        var changes = new List<AppliedRelationshipChange>();
        foreach (var effect in effects)
        {
            var (from, to) = RelationshipDirection.Resolve(effect.Direction, actorId, partnerId);
            from = from.ToUpperInvariant();
            to = to.ToUpperInvariant();
            var attribute = RelationshipStats.ParseAttribute(effect.Attribute);
            var row = db.Relationships.Find(from, to);
            if (row is null)
            {
                row = new SavedRelationship { FromCharacterId = from, ToCharacterId = to };
                db.Relationships.Add(row);
            }
            var stats = ToStats(row);
            int before = stats.Get(attribute);
            stats.Set(attribute, before + effect.Delta);
            FromStats(row, stats);
            changes.Add(new AppliedRelationshipChange(from, to, attribute, before, stats.Get(attribute)));
        }
        db.SaveChanges();
        tx.Commit();
        return changes;
    }

    private static void ImportPair(SaveDbContext db, string pair, RelationshipStats source)
    {
        var ids = pair.Split('|', 2);
        if (ids.Length != 2 || string.IsNullOrWhiteSpace(ids[0]) || string.IsNullOrWhiteSpace(ids[1]))
            throw new InvalidOperationException($"Invalid legacy relationship key: {pair}");
        var row = new SavedRelationship
        {
            FromCharacterId = ids[0].ToUpperInvariant(),
            ToCharacterId = ids[1].ToUpperInvariant()
        };
        foreach (RelationshipAttribute attribute in Enum.GetValues<RelationshipAttribute>())
            source.Set(attribute, source.Get(attribute));
        FromStats(row, source);
        db.Relationships.Add(row);
    }

    private static RelationshipStats ToStats(SavedRelationship row) => new()
    {
        Love = row.Love, Fear = row.Fear, Allyship = row.Allyship,
        Respect = row.Respect, Trust = row.Trust, Romance = row.Romance
    };

    private static void FromStats(SavedRelationship row, RelationshipStats stats)
    {
        row.Love = stats.Love; row.Fear = stats.Fear;
        row.Allyship = stats.Allyship; row.Respect = stats.Respect;
        row.Trust = stats.Trust; row.Romance = stats.Romance;
    }
}

/// <summary>
/// The UI calls this after a committed dialogue roll. History is append-only and
/// never invents an outcome for the non-acting character.
/// </summary>
public sealed class SqliteInteractionHistoryStore
{
    public void Record(DialogueEngine engine, DialogueResolution result)
    {
        using var db = new SaveDbContext();
        db.Database.EnsureCreated();
        var row = new SavedInteraction
        {
            OccurredUtc = DateTime.UtcNow,
            InteractionKey = engine.Definition.Key,
            NodeKey = engine.CurrentNode.Key,
            OptionKey = result.Choice.Key,
            ActorId = engine.Visitor.Id,
            PartnerId = engine.Host.Id,
            Succeeded = result.Succeeded,
            Roll = result.Roll,
            Chance = result.Chance,
            TargetReaction = result.TargetReaction
        };
        foreach (var change in result.Changes)
            row.Changes.Add(new SavedRelationshipChange
            {
                FromCharacterId = change.FromId,
                ToCharacterId = change.ToId,
                Attribute = change.Attribute.ToString(),
                Before = change.Before,
                After = change.After
            });
        db.InteractionHistory.Add(row);
        db.SaveChanges();
    }
}
