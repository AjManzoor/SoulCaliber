using Godot;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;

namespace SoulCaliber.Dialogue;

// Content is editable independently of the savegame. String IDs survive additional
// characters and interaction types without depending on hardcoded enum ordinals.
public sealed class ContentCharacter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DefaultImage { get; set; } = "";
    public string WinImage { get; set; } = "";
    public string LoseImage { get; set; } = "";
}
public sealed class ContentReactionImage
{
    public string CharacterId { get; set; } = "";
    public string ReactionKey { get; set; } = "";
    public string ImagePath { get; set; } = "";
}
public sealed class ContentPersonality
{
    public string CharacterId { get; set; } = "";
    public int Persuasion { get; set; } = 50;
    public int Intimidation { get; set; } = 50;
    public int Deception { get; set; } = 50;
    public int Charm { get; set; } = 50;
    public int Insight { get; set; } = 50;
    public int Resolve { get; set; } = 50;
}
public sealed class ContentInteraction
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string HostCharacterId { get; set; } = "";
    public string StartNodeKey { get; set; } = "";
    public List<ContentDialogueNode> Nodes { get; set; } = new();
}
public sealed class ContentDialogueNode
{
    public int Id { get; set; }
    public int InteractionId { get; set; }
    public int SortOrder { get; set; }
    public string Key { get; set; } = "";
    public int Round { get; set; }
    public string HostIntroduction { get; set; } = "";
    public List<ContentDialogueOption> Options { get; set; } = new();
}
public sealed class ContentDialogueOption
{
    public int Id { get; set; }
    public int NodeId { get; set; }
    public int SortOrder { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int BaseSuccessChance { get; set; } = 50;
    public string? SuccessNextNodeKey { get; set; }
    public string? FailureNextNodeKey { get; set; }
    public string DefaultPlayerLine { get; set; } = "";
    public string DefaultHostSuccessLine { get; set; } = "";
    public string DefaultHostFailureLine { get; set; } = "";
    public string SuccessTargetReaction { get; set; } = "RECEPTIVE";
    public string FailureTargetReaction { get; set; } = "DISMISSIVE";
    public List<ContentRequirement> Requirements { get; set; } = new();
    public List<ContentChanceModifier> ChanceModifiers { get; set; } = new();
    public List<ContentEffect> Effects { get; set; } = new();
}
public sealed class ContentRequirement
{
    public int Id { get; set; }
    public int OptionId { get; set; }
    public string Direction { get; set; } = "";
    public string Attribute { get; set; } = "";
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
}
public sealed class ContentChanceModifier
{
    public int Id { get; set; }
    public int OptionId { get; set; }
    public string Direction { get; set; } = "";
    public string Attribute { get; set; } = "";
    public double Factor { get; set; }
}
public sealed class ContentEffect
{
    public int Id { get; set; }
    public int OptionId { get; set; }
    public string Outcome { get; set; } = "SUCCESS";
    public string Direction { get; set; } = "";
    public string Attribute { get; set; } = "";
    public int Delta { get; set; }
}
public sealed class ContentDialogueLine
{
    public int Id { get; set; }
    public int OptionId { get; set; }
    public string CharacterId { get; set; } = "";
    public string? VisitorId { get; set; }
    public string? Outcome { get; set; }
    public string Text { get; set; } = "";
    public string ImagePath { get; set; } = "";
}
public sealed class ContentSkillCheck
{
    public int OptionId { get; set; }
    public string ActorSkill { get; set; } = "";
    public string DefenderSkill { get; set; } = "";
    public string CheckMode { get; set; } = "STANDARD";
    public double ActorFactor { get; set; } = 0.5;
    public double DefenderFactor { get; set; } = 0.5;
}

public static class DatabasePaths
{
    private const string ContentPathSetting =
        "soulcaliber/content_database_path";

    public static string Content
    {
        get
        {
            string configuredPath =
                ProjectSettings.HasSetting(ContentPathSetting)
                    ? ProjectSettings
                        .GetSetting(ContentPathSetting)
                        .AsString()
                    : "user://soulcaliber_content.db";

            string fullPath =
                configuredPath.StartsWith("user://") ||
                configuredPath.StartsWith("res://")
                    ? ProjectSettings.GlobalizePath(configuredPath)
                    : Path.GetFullPath(configuredPath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "Content database could not be found.",
                    fullPath);
            }

            return fullPath;
        }
    }

    public static string Save =>
        ProjectSettings.GlobalizePath(
            "user://soulcaliber_savegame.db");

    public static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true
        }.ToString();
}

public sealed class ContentDbContext : DbContext
{
    public DbSet<ContentCharacter> Characters => Set<ContentCharacter>();
    public DbSet<ContentReactionImage> ReactionImages => Set<ContentReactionImage>();
    public DbSet<ContentPersonality> Personalities => Set<ContentPersonality>();
    public DbSet<ContentInteraction> Interactions => Set<ContentInteraction>();
    public DbSet<ContentDialogueNode> Nodes => Set<ContentDialogueNode>();
    public DbSet<ContentDialogueOption> Options => Set<ContentDialogueOption>();
    public DbSet<ContentRequirement> Requirements => Set<ContentRequirement>();
    public DbSet<ContentChanceModifier> ChanceModifiers => Set<ContentChanceModifier>();
    public DbSet<ContentEffect> Effects => Set<ContentEffect>();
    public DbSet<ContentDialogueLine> Lines => Set<ContentDialogueLine>();
    public DbSet<ContentSkillCheck> SkillChecks => Set<ContentSkillCheck>();

    protected override void OnConfiguring(
        DbContextOptionsBuilder builder)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true
        }.ToString();

        builder.UseSqlite(connectionString);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ContentCharacter>().HasKey(x => x.Id);
        builder.Entity<ContentReactionImage>().HasKey(x => new { x.CharacterId, x.ReactionKey });
        builder.Entity<ContentReactionImage>().HasOne<ContentCharacter>().WithMany()
            .HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentPersonality>().HasKey(x => x.CharacterId);
        builder.Entity<ContentPersonality>().HasOne<ContentCharacter>().WithMany()
            .HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentInteraction>().HasKey(x => x.Id);
        builder.Entity<ContentInteraction>().HasIndex(x => x.Key).IsUnique();
        builder.Entity<ContentInteraction>().HasMany(x => x.Nodes).WithOne()
            .HasForeignKey(x => x.InteractionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentDialogueNode>().HasIndex(x => new { x.InteractionId, x.Key }).IsUnique();
        builder.Entity<ContentDialogueNode>().HasMany(x => x.Options).WithOne()
            .HasForeignKey(x => x.NodeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentDialogueOption>().HasIndex(x => new { x.NodeId, x.Key }).IsUnique();
        builder.Entity<ContentDialogueOption>().HasMany(x => x.Requirements).WithOne()
            .HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentDialogueOption>().HasMany(x => x.ChanceModifiers).WithOne()
            .HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentDialogueOption>().HasMany(x => x.Effects).WithOne()
            .HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentDialogueLine>().HasOne<ContentDialogueOption>().WithMany()
            .HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ContentDialogueLine>().HasOne<ContentCharacter>().WithMany()
            .HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ContentDialogueLine>().HasOne<ContentCharacter>().WithMany()
            .HasForeignKey(x => x.VisitorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ContentDialogueLine>().HasIndex(x => new { x.OptionId, x.CharacterId });
        builder.Entity<ContentSkillCheck>().HasKey(x => x.OptionId);
        builder.Entity<ContentSkillCheck>().HasOne<ContentDialogueOption>().WithOne()
            .HasForeignKey<ContentSkillCheck>(x => x.OptionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SavedRelationship
{
    public string FromCharacterId { get; set; } = "";
    public string ToCharacterId { get; set; } = "";
    public int Love { get; set; }
    public int Fear { get; set; }
    public int Allyship { get; set; }
    public int Respect { get; set; }
    public int Trust { get; set; }
    public int Romance { get; set; }
}
public sealed class SavedInteraction
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string InteractionKey { get; set; } = "";
    public string NodeKey { get; set; } = "";
    public string OptionKey { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string PartnerId { get; set; } = "";
    public bool Succeeded { get; set; }
    public int Chance { get; set; }
    public int Roll { get; set; }
    public string TargetReaction { get; set; } = "";
    public List<SavedRelationshipChange> Changes { get; set; } = new();
}
public sealed class SavedRelationshipChange
{
    public long Id { get; set; }
    public long SavedInteractionId { get; set; }
    public string FromCharacterId { get; set; } = "";
    public string ToCharacterId { get; set; } = "";
    public string Attribute { get; set; } = "";
    public int Before { get; set; }
    public int After { get; set; }
}
public sealed class SaveDbContext : DbContext
{
    public DbSet<SavedRelationship> Relationships => Set<SavedRelationship>();
    public DbSet<SavedInteraction> InteractionHistory => Set<SavedInteraction>();
    public DbSet<SavedRelationshipChange> RelationshipChanges => Set<SavedRelationshipChange>();

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseSqlite(DatabasePaths.ConnectionString(DatabasePaths.Save));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<SavedRelationship>().HasKey(x => new { x.FromCharacterId, x.ToCharacterId });
        builder.Entity<SavedInteraction>().HasKey(x => x.Id);
        builder.Entity<SavedInteraction>().HasMany(x => x.Changes).WithOne()
            .HasForeignKey(x => x.SavedInteractionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SavedInteraction>().HasIndex(x => x.OccurredUtc);
    }
}
