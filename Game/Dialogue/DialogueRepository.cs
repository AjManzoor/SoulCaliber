using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;

namespace SoulCaliber.Dialogue;

/// <summary>
/// Loads normalised SQLite content into the SAME domain DTOs used by DialogueEngine.
/// Neither the engine nor the Godot portrait/choice UI needs to know SQL exists.
/// Re-open the interaction scene after editing content.db externally.
/// </summary>
public sealed class DialogueRepository
{
	private readonly Dictionary<string, CharacterDefinition> _characters;
	private readonly Dictionary<string, InteractionDefinition> _interactions;
	private readonly List<CharacterLine> _lines;
    private readonly List<NodeIntroduction> _introductions;
    private readonly List<ConditionalVariant> _conditionalVariants;
    private sealed record ConditionalVariant(string InteractionKey, string NodeKey, string? OptionKey, DialogueCandidate Candidate);
    private readonly Dictionary<string, int> _interactionIds;
    private sealed record NodeIntroduction(int InteractionId, string NodeKey, string CharacterId, string? VisitorId, string Text);
	private readonly Dictionary<string, PersonalityStats> _personalities;
	private readonly Dictionary<string, OptionSkillCheck> _skillChecks;
    private readonly Dictionary<string, IReadOnlyList<ChanceFactor>> _profiles;
    private readonly Dictionary<string, string> _optionProfiles;
	private readonly PersonalityStats _defaultPersonality;
    private readonly CharacterSkillCatalogue _skillCatalogue;
    private readonly PersonalityEffectRules _traitRules;

	public DialogueRepository()
	{
		// Validate game-authored relationship keys against the content catalogue,
		// not against the removed hardcoded RelationshipAttribute enum.
		var relationshipCatalogue = RelationshipCatalogue.Load();
		using var db = new ContentDbContext();
		_characters = db.Characters.AsNoTracking().ToList().ToDictionary(c => c.Id,
			c => new CharacterDefinition
			{
				Id = c.Id, Name = c.Name, DefaultImage = c.DefaultImage,
				WinImage = c.WinImage, LoseImage = c.LoseImage
			}, StringComparer.OrdinalIgnoreCase);
		foreach (var image in db.ReactionImages.AsNoTracking())
			_characters[image.CharacterId].ReactionImages[image.ReactionKey] = image.ImagePath;

        _traitRules = LoadTraitRules();
        _skillCatalogue = CharacterSkillCatalogue.Load();
        _defaultPersonality = new PersonalityStats(_skillCatalogue);
        _personalities = new Dictionary<string, PersonalityStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in _characters.Keys)
            _personalities[character] = new PersonalityStats(_skillCatalogue);
        foreach (var row in db.CharacterSkillValues.AsNoTracking())
        {
            if (!_personalities.TryGetValue(row.CharacterId, out var stats))
                throw new InvalidOperationException($"Skill assigned to unknown character '{row.CharacterId}'.");
            stats.Set(row.SkillKey,row.Value);
        }

		var interactions = db.Interactions.AsNoTracking().ToList();
		var nodes = db.Nodes.AsNoTracking().ToList();
		var options = db.Options.AsNoTracking().ToList();
        // Read the optional presentation column separately: older published databases
        // remain compatible and retain the original SHOW AS LOCKED behaviour.
        var hideWhenUnmet = new HashSet<int>();
        using (var visibilityDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            visibilityDb.Open();
            using var exists = visibilityDb.CreateCommand();
            exists.CommandText = "SELECT 1 FROM pragma_table_info('Options') WHERE name='UnmetRequirementBehaviour'";
            if (exists.ExecuteScalar() is not null)
            {
                using var cmd = visibilityDb.CreateCommand();
                cmd.CommandText = "SELECT Id FROM Options WHERE UnmetRequirementBehaviour='HIDE'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) hideWhenUnmet.Add(reader.GetInt32(0));
            }
        }
		var requirements = db.Requirements.AsNoTracking().ToList();
		var modifiers = db.ChanceModifiers.AsNoTracking().ToList();
		var effects = db.Effects.AsNoTracking().ToList();
		var optionLookup = options.ToDictionary(o => o.Id);
		var nodeLookup = nodes.ToDictionary(n => n.Id);
		var interactionLookup = interactions.ToDictionary(i => i.Id);

		_interactions = new Dictionary<string, InteractionDefinition>(StringComparer.OrdinalIgnoreCase);
        _interactionIds = interactions.ToDictionary(x => x.Key, x => x.Id, StringComparer.OrdinalIgnoreCase);
		foreach (var interaction in interactions)
		{
			var definition = new InteractionDefinition
			{
				Key = interaction.Key,
				Title = interaction.Title,
				HostCharacterId = interaction.HostCharacterId,
				StartNodeKey = interaction.StartNodeKey
			};
			foreach (var node in nodes.Where(n => n.InteractionId == interaction.Id).OrderBy(n => n.SortOrder).ThenBy(n => n.Id))
			{
				var dialogueNode = new DialogueNode
				{
					Key = node.Key, Round = node.Round, HostIntroduction = node.HostIntroduction
				};
				foreach (var option in options.Where(o => o.NodeId == node.Id).OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
				{
					var choice = new DialogueOption
					{
						Key = option.Key,
						Label = option.Label,
						BaseSuccessChance = option.BaseSuccessChance,
                        HideWhenRequirementsUnmet = hideWhenUnmet.Contains(option.Id),
						SuccessNextNodeKey = option.SuccessNextNodeKey,
						FailureNextNodeKey = option.FailureNextNodeKey,
						DefaultPlayerLine = option.DefaultPlayerLine,
						DefaultHostSuccessLine = option.DefaultHostSuccessLine,
						DefaultHostFailureLine = option.DefaultHostFailureLine,
						SuccessTargetReaction = option.SuccessTargetReaction,
						FailureTargetReaction = option.FailureTargetReaction,
						Requirements = requirements.Where(r => r.OptionId == option.Id)
							.OrderBy(r => r.Id)
							.Select(r => new RelationshipRequirement
							{
								Direction = r.Direction, Attribute = r.Attribute,
								Minimum = r.Minimum, Maximum = r.Maximum
							}).ToList(),
						ChanceModifiers = modifiers.Where(m => m.OptionId == option.Id)
							.OrderBy(m => m.Id)
							.Select(m => new ChanceModifier
							{
								Direction = m.Direction, Attribute = m.Attribute, Factor = m.Factor
							}).ToList(),
						SuccessEffects = effects.Where(e => e.OptionId == option.Id &&
							Eq(e.Outcome, "SUCCESS")).OrderBy(e => e.Id)
							.Select(e => new RelationshipEffect
							{
								Direction = e.Direction, Attribute = e.Attribute, Delta = e.Delta
							}).ToList(),
						FailureEffects = effects.Where(e => e.OptionId == option.Id &&
							Eq(e.Outcome, "FAILURE")).OrderBy(e => e.Id)
							.Select(e => new RelationshipEffect
							{
								Direction = e.Direction, Attribute = e.Attribute, Delta = e.Delta
							}).ToList()
					};
					dialogueNode.Options.Add(choice);
				}
				definition.Nodes.Add(dialogueNode);
			}
			Validate(definition, relationshipCatalogue);
			_interactions.Add(definition.Key, definition);
		}

		// Optional Phase 5 table. Load it through a read-only SQLite connection, without
        // introducing EF migrations into the game's read-only content database.
        _introductions = new List<NodeIntroduction>();
        using (var introDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false
        }.ToString()))
        {
            introDb.Open();
            using var exists = new SqliteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='NodeIntroductions'", introDb);
            if (exists.ExecuteScalar() is not null)
            {
                using var cmd = new SqliteCommand("""
                    SELECT n.InteractionId, n.Key, x.CharacterId, x.VisitorId, x.Text
                    FROM NodeIntroductions x JOIN Nodes n ON n.Id = x.NodeId
                    ORDER BY x.Id
                    """, introDb);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    _introductions.Add(new NodeIntroduction(
                        reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4)));
                }
            }
        }

        // Phase 7 variants are optional for older content databases. Both tables are
        // read-only; relationship values come from the SAVE database via the engine.
        _conditionalVariants = new List<ConditionalVariant>();
        using (var variantDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            variantDb.Open();
            using var exists = new SqliteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='ConditionalDialogueVariants'", variantDb);
            if (exists.ExecuteScalar() is not null)
            {
                var clauses = new Dictionary<int,List<DialogueClause>>();
                using (var conditions = new SqliteCommand(
                    "SELECT VariantId, Direction, Attribute, Minimum, Maximum FROM ConditionalDialogueConditions ORDER BY Id", variantDb))
                using (var r = conditions.ExecuteReader())
                    while (r.Read())
                    {
                        int id = r.GetInt32(0);
                        if (!clauses.TryGetValue(id, out var list)) clauses[id] = list = new();
                        list.Add(new DialogueClause(r.GetString(1), r.GetString(2),
                            r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4)));
                    }
                using var variants = new SqliteCommand(@"SELECT Id, NodeId, OptionId, CharacterId,
                    VisitorId, Outcome, Priority, Text, ImagePath FROM ConditionalDialogueVariants ORDER BY Id", variantDb);
                using var vr = variants.ExecuteReader();
                while (vr.Read())
                {
                    int id = vr.GetInt32(0);
                    int? nodeId = vr.IsDBNull(1) ? null : vr.GetInt32(1);
                    int? optionId = vr.IsDBNull(2) ? null : vr.GetInt32(2);
                    var option = optionId is int oid ? optionLookup[oid] : null;
                    var node = nodeId is int nid ? nodeLookup[nid] : nodeLookup[option!.NodeId];
                    var interaction = interactionLookup[node.InteractionId];
                    var candidate = new DialogueCandidate(id,vr.IsDBNull(3) ? null : vr.GetString(3),
                        vr.IsDBNull(4) ? null : vr.GetString(4),
                        vr.IsDBNull(5) ? null : vr.GetString(5), vr.GetInt32(6),
                        vr.GetString(7),vr.GetString(8),true,
                        clauses.TryGetValue(id,out var rules) ? rules : Array.Empty<DialogueClause>());
                    _conditionalVariants.Add(new ConditionalVariant(interaction.Key,node.Key,option?.Key,candidate));
                }
            }
        }

        // Resolve relational OptionId references into character-specific lines.
		_lines = new List<CharacterLine>();
		foreach (var line in db.Lines.AsNoTracking().OrderBy(l => l.Id))
		{
			var o = optionLookup[line.OptionId];
			var n = nodeLookup[o.NodeId];
			var i = interactionLookup[n.InteractionId];
			_lines.Add(new CharacterLine
			{
				CharacterId = line.CharacterId, InteractionKey = i.Key,
				NodeKey = n.Key, OptionKey = o.Key,
				VisitorId = line.VisitorId, Outcome = line.Outcome,
				Text = line.Text, ImagePath = line.ImagePath
			});
		}

        // Phase 9: content-authored relationship formulas, read only. A missing table
        // means the game is running an old content.db; publish the new master DB.
        _profiles = new Dictionary<string, IReadOnlyList<ChanceFactor>>(StringComparer.OrdinalIgnoreCase);
        using (var profileDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            profileDb.Open();
            using var keys = new SqliteCommand("SELECT Key FROM CheckProfiles", profileDb);
            using (var reader = keys.ExecuteReader())
                while (reader.Read()) _profiles.Add(reader.GetString(0), new List<ChanceFactor>());
            var terms = _profiles.Keys.ToDictionary(k => k, _ => new List<ChanceFactor>(), StringComparer.OrdinalIgnoreCase);
            using var cmd = new SqliteCommand(
                "SELECT ProfileKey,Direction,Attribute,Factor FROM CheckProfileModifiers ORDER BY ProfileKey,Direction,Attribute", profileDb);
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    if (!terms.TryGetValue(r.GetString(0), out var list))
                        throw new InvalidOperationException($"Unknown formula profile '{r.GetString(0)}'.");
                    list.Add(new ChanceFactor(r.GetString(1), r.GetString(2), r.GetDouble(3)));
                }
            foreach (var (key, list) in terms) _profiles[key] = list;
        }
        if (!_profiles.ContainsKey("STANDARD"))
            throw new InvalidOperationException("STANDARD profile is missing. Open the master database in Studio and republish.");

        _optionProfiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var profileAssignments = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            profileAssignments.Open();
            using var cmd = new SqliteCommand(@"SELECT i.Key, n.Key, o.Key, p.ProfileKey
                FROM OptionCheckProfiles p
                JOIN Options o ON o.Id=p.OptionId
                JOIN Nodes n ON n.Id=o.NodeId
                JOIN Interactions i ON i.Id=n.InteractionId", profileAssignments);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string profileKey = reader.GetString(3);
                if (!_profiles.ContainsKey(profileKey))
                    throw new InvalidOperationException($"Unknown check profile '{profileKey}' assigned to a choice.");
                _optionProfiles.Add(CompositeKey(reader.GetString(0),reader.GetString(1),reader.GetString(2)),profileKey);
            }
        }

        // Phase 12C: discrete status effects are authored by Studio in their own
        // table. Populate each option in-memory without changing numeric Effects.
        using (var statusDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            statusDb.Open();
            using var cmd = statusDb.CreateCommand();
            cmd.CommandText = @"SELECT OptionId,Outcome,Direction,StatusKey
                FROM OptionStatusEffects ORDER BY Id";
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                if (!optionLookup.TryGetValue(r.GetInt32(0),out var optionRow))
                    throw new InvalidOperationException("A status effect references an unknown option.");
                var nodeRow=nodeLookup[optionRow.NodeId];
                var interactionRow=interactionLookup[nodeRow.InteractionId];
                var dialogueOption=_interactions[interactionRow.Key].Nodes
                    .Single(n => Eq(n.Key,nodeRow.Key)).Options
                    .Single(o => Eq(o.Key,optionRow.Key));
                var effect=new RelationshipStatusEffect(r.GetString(2),r.GetString(3));
                switch(r.GetString(1))
                {
                    case "SUCCESS":dialogueOption.SuccessStatusEffects.Add(effect);break;
                    case "FAILURE":dialogueOption.FailureStatusEffects.Add(effect);break;
                    default:throw new InvalidOperationException("Unknown status effect outcome.");
                }
            }
        }

        // Phase 13H-4: optional one-at-a-time relationship-target choices and pair status effects.
        // Old published content databases without these authoring tables remain readable.
        using (var dynamicDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=DatabasePaths.Content, Mode=SqliteOpenMode.ReadOnly,
            ForeignKeys=true, Pooling=false
        }.ToString()))
        {
            dynamicDb.Open();
            bool HasTable(string name)
            {
                using var q=dynamicDb.CreateCommand();
                q.CommandText="SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
                q.Parameters.AddWithValue("$name",name);
                return q.ExecuteScalar() is not null;
            }
            DialogueOption OptionById(int id)
            {
                if (!optionLookup.TryGetValue(id,out var row))
                    throw new InvalidOperationException("Dynamic target references unknown option.");
                var node=nodeLookup[row.NodeId];
                var interaction=interactionLookup[node.InteractionId];
                return _interactions[interaction.Key].Nodes.Single(n=>Eq(n.Key,node.Key)).Options.Single(o=>Eq(o.Key,row.Key));
            }
            if(HasTable("OptionDynamicTargets"))
            {
                // Older published content has only relationship groups. New content
                // adds CUSTOM mode with explicit, FK-backed character IDs.
                var customIds=new Dictionary<int,List<string>>();
                if(HasTable("OptionDynamicTargetCharacters"))
                {
                    using var members=dynamicDb.CreateCommand();
                    members.CommandText="SELECT OptionId,CharacterId FROM OptionDynamicTargetCharacters ORDER BY OptionId,SortOrder,CharacterId";
                    using var ids=members.ExecuteReader();
                    while(ids.Read())
                    {
                        int optionId=ids.GetInt32(0);
                        if(!customIds.TryGetValue(optionId,out var list))
                            customIds[optionId]=list=new List<string>();
                        list.Add(ids.GetString(1));
                    }
                }
                using var col=dynamicDb.CreateCommand();
                col.CommandText="SELECT 1 FROM pragma_table_info('OptionDynamicTargets') WHERE name='SelectionMode'";
                bool hasSelectionMode=col.ExecuteScalar() is not null;
                using var q=dynamicDb.CreateCommand();
                q.CommandText=hasSelectionMode
                    ? "SELECT OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,SelectionMode FROM OptionDynamicTargets"
                    : "SELECT OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,'GROUP' FROM OptionDynamicTargets";
                using var r=q.ExecuteReader();
                while(r.Read())
                {
                    int optionId=r.GetInt32(0);
                    customIds.TryGetValue(optionId,out var list);
                    OptionById(optionId).DynamicTarget=new DynamicTargetRule(
                        r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),list);
                }
            }
            if(HasTable("OptionPairStatusEffects"))
            {
                using var q=dynamicDb.CreateCommand();
                q.CommandText="SELECT OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey FROM OptionPairStatusEffects ORDER BY Id";
                using var r=q.ExecuteReader();
                while(r.Read())
                {
                    var option=OptionById(r.GetInt32(0));
                    var effect=new PairStatusEffect(r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5));
                    switch(r.GetString(1))
                    {
                        case "SUCCESS":option.SuccessPairStatusEffects.Add(effect);break;
                        case "FAILURE":option.FailurePairStatusEffects.Add(effect);break;
                        default:throw new InvalidOperationException("Invalid pair-status-effect outcome.");
                    }
                }
            }
        }

        // Phase 13C: memory creation effects attached to dialogue choices.
        using (var memoryDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=DatabasePaths.Content, Mode=SqliteOpenMode.ReadOnly,
            ForeignKeys=true, Pooling=false
        }.ToString()))
        {
            memoryDb.Open();
            using var cmd=memoryDb.CreateCommand();
            using (var existsAudience = memoryDb.CreateCommand())
            {
                existsAudience.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionMemoryEffectKnowledgeAudiences'";
                bool hasAudience = existsAudience.ExecuteScalar() is not null;
                cmd.CommandText = hasAudience
                    ? "SELECT e.OptionId,e.MemoryKey,e.Trigger,e.Affected,e.SeverityOverride,e.KnowledgeOverride,a.AudienceJson FROM OptionMemoryEffects e LEFT JOIN OptionMemoryEffectKnowledgeAudiences a ON a.OptionMemoryEffectId=e.Id ORDER BY e.Id"
                    : "SELECT OptionId,MemoryKey,Trigger,Affected,SeverityOverride,KnowledgeOverride,NULL FROM OptionMemoryEffects ORDER BY Id";
            }
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                if(!optionLookup.TryGetValue(r.GetInt32(0),out var optionRow))
                    throw new InvalidOperationException("Memory effect references an unknown choice.");
                var nodeRow=nodeLookup[optionRow.NodeId];
                var interactionRow=interactionLookup[nodeRow.InteractionId];
                _interactions[interactionRow.Key].Nodes.Single(n=>Eq(n.Key,nodeRow.Key))
                    .Options.Single(o=>Eq(o.Key,optionRow.Key)).MemoryEffects.Add(
                        new DialogueMemoryEffect(r.GetString(1),r.GetString(2),r.GetString(3),
                            r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),
                            r.IsDBNull(6)?null:r.GetString(6)));
            }
        }

        // Phase 12D: authored gates use the current status store when an option is
        // evaluated. They are independent of numeric requirements and success chance.
        using (var requirementsDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            requirementsDb.Open();
            using var cmd = requirementsDb.CreateCommand();
            cmd.CommandText = "SELECT OptionId,Direction,StatusKey FROM OptionStatusRequirements ORDER BY Id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!optionLookup.TryGetValue(reader.GetInt32(0), out var optionRow))
                    throw new InvalidOperationException("A status requirement references an unknown dialogue choice.");
                var nodeRow = nodeLookup[optionRow.NodeId];
                var interactionRow = interactionLookup[nodeRow.InteractionId];
                _interactions[interactionRow.Key].Nodes.Single(n => Eq(n.Key, nodeRow.Key))
                    .Options.Single(o => Eq(o.Key, optionRow.Key))
                    .StatusRequirements.Add(new RelationshipStatusRequirement(reader.GetString(1), reader.GetString(2)));
            }
        }

        // Phase 13D: read and validate authored memory condition trees.
        using (var memoryRequirementsDb = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true, Pooling = false
        }.ToString()))
        {
            memoryRequirementsDb.Open();
            using var table = memoryRequirementsDb.CreateCommand();
            table.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='OptionMemoryRequirements'";
            if (table.ExecuteScalar() is not null) // older published content is still loadable
            {
                var activeMemoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var keys = memoryRequirementsDb.CreateCommand())
                {
                    keys.CommandText = "SELECT Key FROM MemoryTypes WHERE IsActive=1";
                    using var keyReader = keys.ExecuteReader();
                    while (keyReader.Read()) activeMemoryKeys.Add(keyReader.GetString(0));
                }
                var activeStatusKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var statuses = memoryRequirementsDb.CreateCommand())
                {
                    statuses.CommandText = "SELECT Key FROM RelationshipStatuses WHERE IsActive=1";
                    using var statusReader = statuses.ExecuteReader();
                    while (statusReader.Read()) activeStatusKeys.Add(statusReader.GetString(0));
                }
                var activeTraitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var traits = memoryRequirementsDb.CreateCommand())
                {
                    traits.CommandText = "SELECT Key FROM PersonalityTraits WHERE IsActive=1";
                    using var rows = traits.ExecuteReader();
                    while (rows.Read()) activeTraitKeys.Add(rows.GetString(0));
                }
                using var cmd = memoryRequirementsDb.CreateCommand();
                cmd.CommandText = "SELECT OptionId,ExpressionJson FROM OptionMemoryRequirements ORDER BY OptionId";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int optionId = reader.GetInt32(0);
                    if (!optionLookup.TryGetValue(optionId, out var optionRow))
                        throw new InvalidOperationException("A memory requirement references an unknown dialogue choice.");
                    var tree = MemoryConditionRules.Read(reader.GetString(1));
                    MemoryConditionRules.Validate(tree, activeMemoryKeys.Contains, _characters.ContainsKey,
                        activeStatusKeys.Contains, activeTraitKeys.Contains);
                    var nodeRow = nodeLookup[optionRow.NodeId];
                    var interactionRow = interactionLookup[nodeRow.InteractionId];
                    _interactions[interactionRow.Key].Nodes.Single(n => Eq(n.Key, nodeRow.Key))
                        .Options.Single(o => Eq(o.Key, optionRow.Key)).MemoryRequirements = tree;
                }
            }
        }

		_skillChecks = new Dictionary<string, OptionSkillCheck>(StringComparer.OrdinalIgnoreCase);
		foreach (var check in db.SkillChecks.AsNoTracking())
		{
			var option = optionLookup[check.OptionId];
			var node = nodeLookup[option.NodeId];
			var interaction = interactionLookup[node.InteractionId];
			if (!_skillCatalogue.Get(check.ActorSkill).IsActive)
                throw new InvalidOperationException($"Inactive skill: {check.ActorSkill}");
			if (!_skillCatalogue.Get(check.DefenderSkill).IsActive)
                throw new InvalidOperationException($"Inactive skill: {check.DefenderSkill}");
            if (!_profiles.ContainsKey(check.CheckMode))
                throw new InvalidOperationException($"Unknown check profile: {check.CheckMode}");
			var skill = new OptionSkillCheck
			{
				InteractionKey = interaction.Key, NodeKey = node.Key, OptionKey = option.Key,
				ActorSkill = check.ActorSkill, DefenderSkill = check.DefenderSkill,
				CheckMode = check.CheckMode.ToUpperInvariant(),
				ActorFactor = check.ActorFactor, DefenderFactor = check.DefenderFactor
			};
			_skillChecks.Add(CompositeKey(interaction.Key, node.Key, option.Key), skill);
		}
	}

	    private static Dictionary<string,int> Values(RelationshipStats actorToPartner, RelationshipStats partnerToActor)
    {
        var values = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in actorToPartner.Catalogue.All)
        {
            values[ConditionalDialogueRules.StatKey(RelationshipDirection.ActorToPartner,attribute.Key)] = actorToPartner.Get(attribute.Key);
            values[ConditionalDialogueRules.StatKey(RelationshipDirection.PartnerToActor,attribute.Key)] = partnerToActor.Get(attribute.Key);
        }
        return values;
    }

    public string Introduction(string interactionKey, DialogueNode node, string hostId, string visitorId,
        RelationshipStats actorToPartner, RelationshipStats partnerToActor)
    {
        if (!_interactionIds.TryGetValue(interactionKey, out int interactionId))
            throw new InvalidOperationException($"Unknown interaction '{interactionKey}'");
        var candidates = _introductions.Where(x => x.InteractionId == interactionId && Eq(x.NodeKey,node.Key))
            .Select((x, i) => new DialogueCandidate(i + 1,x.CharacterId,x.VisitorId,null,0,x.Text,"",false,
                Array.Empty<DialogueClause>())).ToList();
        candidates.AddRange(_conditionalVariants.Where(x => x.OptionKey is null &&
            Eq(x.InteractionKey,interactionKey) && Eq(x.NodeKey,node.Key)).Select(x => x.Candidate));
        var selected = ConditionalDialogueRules.Resolve(candidates,hostId,visitorId,null,
            Values(actorToPartner,partnerToActor));
        return selected.Candidate?.Text ?? node.HostIntroduction;
    }

    public IReadOnlyCollection<string> CharacterIds => _characters.Keys;

    public CharacterDefinition Character(string id) => _characters.TryGetValue(id, out var value)
		? value : throw new InvalidOperationException($"Unknown character '{id}' in content.db");

	public InteractionDefinition Interaction(string key) => _interactions.TryGetValue(key, out var value)
		? value : throw new InvalidOperationException($"Unknown interaction '{key}' in content.db");

	public PersonalityStats Personality(string id) =>
		_personalities.TryGetValue(id, out var value) ? value : _defaultPersonality;

    public string OptionProfile(string interaction, string node, string option) =>
        _optionProfiles.TryGetValue(CompositeKey(interaction,node,option), out var key)
            ? key : "STANDARD";

    public IReadOnlyList<ChanceFactor> CheckProfile(string profileKey) =>
        _profiles.TryGetValue(profileKey, out var terms) ? terms :
        throw new InvalidOperationException($"Missing check profile '{profileKey}'. Republish content.db.");

	public OptionSkillCheck? SkillCheck(string interaction, string node, string option) =>
		_skillChecks.TryGetValue(CompositeKey(interaction, node, option), out var check)
			? check : null;

    public CharacterLine PlayerLine(string interactionKey, DialogueNode node,
        DialogueOption option, string playerId, string partnerId, bool succeeded,
        RelationshipStats actorToPartner, RelationshipStats partnerToActor) =>
        FindLine(interactionKey,node,option,playerId,partnerId,succeeded,actorToPartner,partnerToActor)
            ?? new CharacterLine { CharacterId=playerId, Text=option.DefaultPlayerLine };

    public CharacterLine HostLine(string interactionKey, DialogueNode node,
        DialogueOption option, string hostId, string visitorId, bool succeeded,
        RelationshipStats actorToPartner, RelationshipStats partnerToActor) =>
        FindLine(interactionKey,node,option,hostId,visitorId,succeeded,actorToPartner,partnerToActor)
            ?? new CharacterLine { CharacterId=hostId,
                Text=succeeded ? option.DefaultHostSuccessLine : option.DefaultHostFailureLine };

    private CharacterLine? FindLine(string interactionKey,DialogueNode node,DialogueOption option,
        string speaker,string other,bool succeeded,RelationshipStats actorToPartner,RelationshipStats partnerToActor)
    {
        string outcome=succeeded ? "SUCCESS":"FAILURE";
        var candidates = _lines.Where(l => Eq(l.InteractionKey,interactionKey) &&
            Eq(l.NodeKey,node.Key) && Eq(l.OptionKey,option.Key))
            .Select((l,i)=>new DialogueCandidate(i+1,l.CharacterId,l.VisitorId,l.Outcome,
                0,l.Text,l.ImagePath,false,Array.Empty<DialogueClause>())).ToList();
        candidates.AddRange(_conditionalVariants.Where(v => Eq(v.InteractionKey,interactionKey) &&
            Eq(v.NodeKey,node.Key) && Eq(v.OptionKey,option.Key)).Select(v=>v.Candidate));
        var selection=ConditionalDialogueRules.Resolve(candidates,speaker,other,outcome,
            Values(actorToPartner,partnerToActor));
        var selected=selection.Candidate;
        if (selected is null) return null;
        return new CharacterLine { CharacterId=speaker,VisitorId=selected.OtherCharacterId,
            // Conditional artwork is specific to the resolved outcome, even with ANY text.
            Outcome=selected.Conditional && !string.IsNullOrWhiteSpace(selected.ImagePath) ? outcome : selected.Outcome,
            Text=selected.Text,ImagePath=selected.ImagePath,
            InteractionKey=interactionKey,NodeKey=node.Key,OptionKey=option.Key };
    }

    // Preserve the original uppercase composite key format for lookups.
    private static string CompositeKey(string interaction, string node, string option) =>
        $"{interaction.ToUpperInvariant()}|{node.ToUpperInvariant()}|{option.ToUpperInvariant()}";

    public PersonalityAdjustment AdjustRelationshipEffect(string receiverId, string attribute,
        int baseDelta, bool succeeded) => _traitRules.Adjust(receiverId, attribute, baseDelta, succeeded);

    public bool HasTrait(string characterId, string traitKey) =>
        _traitRules.HasTrait(characterId, traitKey);

    private static PersonalityEffectRules LoadTraitRules()
    {
        using var cn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePaths.Content, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, ForeignKeys = true
        }.ToString());
        cn.Open();
        using var check = cn.CreateCommand();
        check.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='PersonalityTraits'";
        if (check.ExecuteScalar() is null)
            throw new InvalidOperationException("Phase 10B personality tables missing. Open the master database in updated SoulCaliber Studio, and republish content if using an export.");
        var assigned = new List<(string CharacterId,string TraitKey)>();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ct.CharacterId,ct.TraitKey FROM CharacterTraits ct
                JOIN PersonalityTraits t ON t.Key=ct.TraitKey WHERE t.IsActive=1";
            using var r=cmd.ExecuteReader();
            while(r.Read()) assigned.Add((r.GetString(0),r.GetString(1)));
        }
        var rules = new List<PersonalityModifierRule>();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = @"SELECT m.TraitKey,m.AttributeKey,m.Outcome,m.Change,m.Factor
                FROM PersonalityTraitModifiers m JOIN PersonalityTraits t ON t.Key=m.TraitKey
                WHERE t.IsActive=1";
            using var r=cmd.ExecuteReader();
            while(r.Read()) rules.Add(new PersonalityModifierRule(r.GetString(0),r.GetString(1),
                r.GetString(2),r.GetString(3),r.GetDouble(4)));
        }
        return new PersonalityEffectRules(assigned,rules);
    }

	private static bool Eq(string left, string right) =>
		string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	private static void Validate(InteractionDefinition interaction, RelationshipCatalogue catalogue)
	{
		var nodes = interaction.Nodes.ToDictionary(n => n.Key, StringComparer.OrdinalIgnoreCase);
		if (!nodes.ContainsKey(interaction.StartNodeKey))
			throw new InvalidOperationException($"Invalid start node '{interaction.StartNodeKey}' in {interaction.Key}");
		foreach (var node in interaction.Nodes)
		{
			if (node.Options.Select(o => o.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != node.Options.Count)
				throw new InvalidOperationException($"Duplicate choices in {interaction.Key}/{node.Key}");
			foreach (var option in node.Options)
			{
				foreach (var r in option.Requirements)
				{
					RelationshipDirection.Resolve(r.Direction, "ACTOR", "PARTNER");
					catalogue.Get(r.Attribute);
				}
				foreach (var m in option.ChanceModifiers)
				{
					RelationshipDirection.Resolve(m.Direction, "ACTOR", "PARTNER");
					catalogue.Get(m.Attribute);
				}
				foreach (var e in option.SuccessEffects.Concat(option.FailureEffects))
				{
					RelationshipDirection.Resolve(e.Direction, "ACTOR", "PARTNER");
					catalogue.Get(e.Attribute);
				}
				foreach (var next in new[] { option.SuccessNextNodeKey, option.FailureNextNodeKey })
					if (!string.IsNullOrWhiteSpace(next) && !nodes.ContainsKey(next))
						throw new InvalidOperationException($"Invalid next node {next} for {interaction.Key}/{node.Key}/{option.Key}");
			}
		}
	}
}
