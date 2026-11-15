using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace SoulCaliber.Dialogue;

/// <summary>
/// One-time, transactional migration from EXISTING res://Data/*.json to the
/// normalised SQLite content database. JSON is not read on later launches.
/// Keeps the original JSON files untouched for rollback and re-import.
/// </summary>
public static class LegacyJsonImporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string ReadText(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
            throw new InvalidOperationException($"Cannot read {path}: {Godot.FileAccess.GetOpenError()}");
        return file.GetAsText();
    }

    private static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(ReadText(path), Options) ??
        throw new InvalidOperationException($"Cannot deserialize {path}");

    private static T ReadOptional<T>(string path) where T : class, new() =>
        Godot.FileAccess.FileExists(path) ? Read<T>(path) : new T();

    public static void EnsureContentImported()
    {
        using var db = new ContentDbContext();
        db.Database.EnsureCreated();
        if (db.Interactions.Any()) return;  // NEVER overwrite user edits or upgraded content.
        using var transaction = db.Database.BeginTransaction();

        var characters = Read<CharactersFile>("res://Data/characters.json");
        foreach (var c in characters.Characters)
        {
            db.Characters.Add(new ContentCharacter
            {
                Id = c.Id, Name = c.Name,
                DefaultImage = c.DefaultImage, WinImage = c.WinImage, LoseImage = c.LoseImage
            });
        }
        db.SaveChanges();

        // Same precedence as the JSON version: character-specific overrides win.
        var reactionPaths = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in characters.Characters)
            reactionPaths[c.Id] = new Dictionary<string, string>(c.ReactionImages ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var catalog = ReadOptional<ReactionImageCatalog>("res://Data/reaction_images.json");
        foreach (var c in catalog.Characters)
        {
            if (!reactionPaths.TryGetValue(c.CharacterId, out var paths)) continue;
            foreach (var pair in c.ReactionImages)
                if (!paths.ContainsKey(pair.Key)) paths[pair.Key] = pair.Value;
        }
        foreach (var (characterId, paths) in reactionPaths)
            foreach (var (reactionKey, path) in paths)
                db.ReactionImages.Add(new ContentReactionImage
                {
                    CharacterId = characterId, ReactionKey = reactionKey.ToUpperInvariant(), ImagePath = path
                });

        var personality = ReadOptional<PersonalityFile>("res://Data/personality.json");
        foreach (var c in personality.Characters)
        {
            c.Stats.Validate(c.CharacterId);
            db.Personalities.Add(new ContentPersonality
            {
                CharacterId = c.CharacterId, Persuasion = c.Stats.Persuasion,
                Intimidation = c.Stats.Intimidation, Deception = c.Stats.Deception,
                Charm = c.Stats.Charm, Insight = c.Stats.Insight, Resolve = c.Stats.Resolve
            });
        }

        var definitions = new List<InteractionDefinition>();
        foreach (var name in DirAccess.GetFilesAt("res://Data"))
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            string json = ReadText("res://Data/" + name);
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("nodes", out _)) continue;
            var interaction = JsonSerializer.Deserialize<InteractionDefinition>(json, Options)
                ?? throw new InvalidOperationException($"Invalid interaction JSON: {name}");
            if (string.IsNullOrWhiteSpace(interaction.Key))
                throw new InvalidOperationException($"Interaction has no key: {name}");
            ValidateInteraction(interaction);
            definitions.Add(interaction);
        }
        if (definitions.Count == 0)
            throw new InvalidOperationException("No interaction JSON definitions found in res://Data");

        // Populate parent/child trees in one unit of work, preserving option order.
        var optionsByKey = new Dictionary<string, ContentDialogueOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var interaction in definitions)
        {
            var dbInteraction = new ContentInteraction
            {
                Key = interaction.Key, Title = interaction.Title,
                HostCharacterId = interaction.HostCharacterId, StartNodeKey = interaction.StartNodeKey
            };
            foreach (var node in interaction.Nodes)
            {
                var dbNode = new ContentDialogueNode
                {
                    Key = node.Key, SortOrder = interaction.Nodes.IndexOf(node),
                    Round = node.Round, HostIntroduction = node.HostIntroduction
                };
                foreach (var option in node.Options)
                {
                    var dbOption = new ContentDialogueOption
                    {
                        Key = option.Key, SortOrder = node.Options.IndexOf(option),
                        Label = option.Label,
                        BaseSuccessChance = option.BaseSuccessChance,
                        SuccessNextNodeKey = option.SuccessNextNodeKey,
                        FailureNextNodeKey = option.FailureNextNodeKey,
                        DefaultPlayerLine = option.DefaultPlayerLine,
                        DefaultHostSuccessLine = option.DefaultHostSuccessLine,
                        DefaultHostFailureLine = option.DefaultHostFailureLine,
                        SuccessTargetReaction = option.SuccessTargetReaction,
                        FailureTargetReaction = option.FailureTargetReaction
                    };
                    foreach (var r in option.Requirements)
                        dbOption.Requirements.Add(new ContentRequirement
                        {
                            Direction = r.Direction, Attribute = r.Attribute,
                            Minimum = r.Minimum, Maximum = r.Maximum
                        });
                    foreach (var m in option.ChanceModifiers)
                        dbOption.ChanceModifiers.Add(new ContentChanceModifier
                        {
                            Direction = m.Direction, Attribute = m.Attribute, Factor = m.Factor
                        });
                    foreach (var e in option.SuccessEffects)
                        dbOption.Effects.Add(new ContentEffect
                        {
                            Outcome = "SUCCESS", Direction = e.Direction,
                            Attribute = e.Attribute, Delta = e.Delta
                        });
                    foreach (var e in option.FailureEffects)
                        dbOption.Effects.Add(new ContentEffect
                        {
                            Outcome = "FAILURE", Direction = e.Direction,
                            Attribute = e.Attribute, Delta = e.Delta
                        });
                    dbNode.Options.Add(dbOption);
                    string optionId = CompositeKey(interaction.Key, node.Key, option.Key);
                    if (!optionsByKey.TryAdd(optionId, dbOption))
                        throw new InvalidOperationException($"Duplicate option: {optionId}");
                }
                dbInteraction.Nodes.Add(dbNode);
            }
            db.Interactions.Add(dbInteraction);
        }
        db.SaveChanges(); // assigns integer OptionIds

        var lines = ReadOptional<LinesFile>("res://Data/lines.json");
        foreach (var line in lines.Lines)
        {
            var key = CompositeKey(line.InteractionKey, line.NodeKey, line.OptionKey);
            if (!optionsByKey.TryGetValue(key, out var option))
                throw new InvalidOperationException($"lines.json points to missing choice {key}");
            db.Lines.Add(new ContentDialogueLine
            {
                OptionId = option.Id, CharacterId = line.CharacterId,
                VisitorId = line.VisitorId, Outcome = line.Outcome,
                Text = line.Text, ImagePath = line.ImagePath
            });
        }

        var checks = ReadOptional<OptionSkillsFile>("res://Data/action_skills.json");
        foreach (var check in checks.Checks)
        {
            var key = CompositeKey(check.InteractionKey, check.NodeKey, check.OptionKey);
            if (!optionsByKey.TryGetValue(key, out var option))
                throw new InvalidOperationException($"action_skills.json points to missing choice {key}");
            PersonalityStats.ValidateAbility(check.ActorSkill);
            PersonalityStats.ValidateAbility(check.DefenderSkill);
            db.SkillChecks.Add(new ContentSkillCheck
            {
                OptionId = option.Id, ActorSkill = check.ActorSkill,
                DefenderSkill = check.DefenderSkill, CheckMode = check.CheckMode,
                ActorFactor = check.ActorFactor, DefenderFactor = check.DefenderFactor
            });
        }

        db.SaveChanges();
        transaction.Commit();
        GD.Print($"SQLite content imported: {definitions.Count} interactions, " +
            $"{optionsByKey.Count} choices, {lines.Lines.Count} specialised lines.");
    }

    public static string CompositeKey(string interaction, string node, string option) =>
        $"{interaction.ToUpperInvariant()}|{node.ToUpperInvariant()}|{option.ToUpperInvariant()}";

    private static void ValidateInteraction(InteractionDefinition interaction)
    {
        var nodes = interaction.Nodes.Select(n => n.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!nodes.Contains(interaction.StartNodeKey))
            throw new InvalidOperationException($"Missing start node {interaction.StartNodeKey} on {interaction.Key}");
        if (nodes.Count != interaction.Nodes.Count)
            throw new InvalidOperationException($"Duplicate node key in {interaction.Key}");
        foreach (var node in interaction.Nodes)
        {
            if (node.Options.Select(o => o.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.Options.Count)
                throw new InvalidOperationException($"Duplicate choice key in {interaction.Key}/{node.Key}");
            foreach (var opt in node.Options)
            {
                if (opt.BaseSuccessChance < 0 || opt.BaseSuccessChance > 100)
                    throw new InvalidOperationException($"Invalid base success chance: {interaction.Key}/{node.Key}/{opt.Key}");
                foreach (var next in new[] { opt.SuccessNextNodeKey, opt.FailureNextNodeKey })
                    if (!string.IsNullOrWhiteSpace(next) && !nodes.Contains(next))
                        throw new InvalidOperationException($"Missing next node {next} in {interaction.Key}");
            }
        }
    }
}
