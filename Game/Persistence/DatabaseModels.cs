#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
public sealed class ContentCharacterSkillValue
{
    public string CharacterId { get; set; } = "";
    public string SkillKey { get; set; } = "";
    public int Value { get; set; }
}
public sealed class ContentReactionImage
{
    public string CharacterId { get; set; } = "";
    public string ReactionKey { get; set; } = "";
    public string ImagePath { get; set; } = "";
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
    // ProjectSettings creates a real operating-system path from Godot's portable user://.
    // No res:// database is modified, which also works in exported desktop builds.
    public static string Content
    {
        get
        {
            // You can keep a custom Content getter from your current project instead.
            // The authoring copy lives in Documents; exported builds use the EXE sidecar.
            string master = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "SoulCaliber", "Content", "soulcaliber_master.db");
            string sidecar = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
                "Content", "content.db");
            if (OS.HasFeature("editor") && File.Exists(master)) return master;
            if (File.Exists(sidecar)) return sidecar;
            if (File.Exists(master)) return master; // developer fallback, never needed in exports
            return ProjectSettings.GlobalizePath("user://soulcaliber_content.db");
        }
    }
    public static string Save => ProjectSettings.GlobalizePath("user://soulcaliber_savegame.db");
    public static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
}

public sealed class ContentDbContext : DbContext
{
    public DbSet<ContentCharacter> Characters => Set<ContentCharacter>();
    public DbSet<ContentReactionImage> ReactionImages => Set<ContentReactionImage>();

    public DbSet<ContentInteraction> Interactions => Set<ContentInteraction>();
    public DbSet<ContentDialogueNode> Nodes => Set<ContentDialogueNode>();
    public DbSet<ContentDialogueOption> Options => Set<ContentDialogueOption>();
    public DbSet<ContentRequirement> Requirements => Set<ContentRequirement>();
    public DbSet<ContentChanceModifier> ChanceModifiers => Set<ContentChanceModifier>();
    public DbSet<ContentEffect> Effects => Set<ContentEffect>();
    public DbSet<ContentDialogueLine> Lines => Set<ContentDialogueLine>();
    public DbSet<ContentSkillCheck> SkillChecks => Set<ContentSkillCheck>();
    public DbSet<ContentCharacterSkillValue> CharacterSkillValues => Set<ContentCharacterSkillValue>();

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseSqlite(DatabasePaths.ConnectionString(DatabasePaths.Content));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<ContentCharacter>().HasKey(x => x.Id);
        builder.Entity<ContentCharacterSkillValue>().HasKey(x => new { x.CharacterId, x.SkillKey });
        builder.Entity<ContentCharacterSkillValue>().ToTable("CharacterSkillValues");
        builder.Entity<ContentReactionImage>().HasKey(x => new { x.CharacterId, x.ReactionKey });
        builder.Entity<ContentReactionImage>().HasOne<ContentCharacter>().WithMany()
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

public sealed class SavedRelationshipValue
{
    public string FromCharacterId { get; set; } = "";
    public string ToCharacterId { get; set; } = "";
    public string AttributeKey { get; set; } = "";
    public int Value { get; set; }
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
    public DbSet<SavedRelationshipValue> RelationshipValues => Set<SavedRelationshipValue>();
    public DbSet<SavedInteraction> InteractionHistory => Set<SavedInteraction>();
    public DbSet<SavedRelationshipChange> RelationshipChanges => Set<SavedRelationshipChange>();

    protected override void OnConfiguring(DbContextOptionsBuilder builder) =>
        builder.UseSqlite(DatabasePaths.ConnectionString(DatabasePaths.Save));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<SavedRelationshipValue>().HasKey(x => new { x.FromCharacterId, x.ToCharacterId, x.AttributeKey });
        builder.Entity<SavedRelationshipValue>().ToTable("SavedRelationshipValues");
        builder.Entity<SavedInteraction>().HasKey(x => x.Id);
        builder.Entity<SavedInteraction>().HasMany(x => x.Changes).WithOne()
            .HasForeignKey(x => x.SavedInteractionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SavedInteraction>().HasIndex(x => x.OccurredUtc);
    }
}
