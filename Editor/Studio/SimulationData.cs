using Microsoft.Data.Sqlite;
using SoulCaliber.Shared;
using System.IO;

namespace SoulCaliberStudio;

// Read-only, self-contained projection of the SAME content tables used by Godot.
// No connection to soulcaliber_savegame.db and no write commands anywhere in this class.
public sealed partial class SimulationData : IDisposable
{
    private readonly SqliteConnection _db;
    public IReadOnlyList<RelationshipAttributeDefinition> Attributes { get; }
    public IReadOnlyList<SimCharacter> Characters { get; }
    public IReadOnlyList<SimInteraction> Interactions { get; }
    private readonly Dictionary<string, SimPersonality> _personalities;
    private readonly Dictionary<string,int> _skillDefaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly PersonalityEffectRules _traitRules;
    private readonly List<(string CharacterId, string TraitKey)> _originalTraitAssignments;
    private readonly List<PersonalityModifierRule> _traitModifiers;
    public IReadOnlyList<SimTraitDefinition> Traits { get; }
    private readonly Dictionary<int, SimSkillCheck> _checks;
    private readonly Dictionary<int, string> _optionProfiles;
    private readonly Dictionary<string, IReadOnlyList<ChanceFactor>> _profiles;
    private readonly List<SimIntroduction> _introductions;
    private readonly List<SimPhrase> _phrases;
    private readonly List<SimVariant> _variants;
    private readonly Dictionary<int, List<SimRequirement>> _requirements;
    private readonly Dictionary<int, List<SimModifier>> _modifiers;
    private readonly Dictionary<int, List<SimEffect>> _effects;

    public SimulationData(string databasePath)
    {
        if (!File.Exists(databasePath)) throw new FileNotFoundException("Master content database not found.", databasePath);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, ForeignKeys = true
        }.ToString());
        _db.Open();
        using (var cmd = new SqliteCommand("PRAGMA query_only=ON;", _db)) cmd.ExecuteNonQuery();

        Attributes = Read(@"SELECT Key,Name,Description,MinValue,MaxValue,DefaultValue,IconPath,Color,SortOrder,IsActive
            FROM RelationshipAttributes ORDER BY SortOrder,Name,Key", r => new RelationshipAttributeDefinition(
                r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),
                r.GetString(6),r.GetString(7),r.GetInt32(8),r.GetInt32(9) == 1));
        Traits = Read("SELECT Key,Name FROM PersonalityTraits WHERE IsActive=1 ORDER BY SortOrder,Name,Key",
            r => new SimTraitDefinition(r.GetString(0), r.GetString(1)));
        var assignedTraits = Read(@"SELECT ct.CharacterId,ct.TraitKey FROM CharacterTraits ct
            JOIN PersonalityTraits t ON t.Key=ct.TraitKey WHERE t.IsActive=1",
            r => (CharacterId:r.GetString(0), TraitKey:r.GetString(1)));
        var traitModifiers = Read(@"SELECT m.TraitKey,m.AttributeKey,m.Outcome,m.Change,m.Factor
            FROM PersonalityTraitModifiers m JOIN PersonalityTraits t ON t.Key=m.TraitKey WHERE t.IsActive=1",
            r => new PersonalityModifierRule(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4)));
        _originalTraitAssignments = assignedTraits;
        _traitModifiers = traitModifiers;
        _traitRules = new PersonalityEffectRules(assignedTraits,traitModifiers);
        Characters = Read("SELECT Id, Name FROM Characters ORDER BY Name",
            r => new SimCharacter(r.GetString(0), r.GetString(1)));
        var definitions = Read("SELECT Key,DefaultValue FROM CharacterSkills WHERE IsActive=1",
            r => (Key:r.GetString(0), Default:r.GetInt32(1)));
        foreach(var definition in definitions) _skillDefaults[definition.Key] = definition.Default;
        var skillValues=Read("SELECT CharacterId,SkillKey,Value FROM CharacterSkillValues",
            r => (CharacterId:r.GetString(0),Key:r.GetString(1),Value:r.GetInt32(2)));
        _personalities = Characters.ToDictionary(c=>c.Id,
            c=>new SimPersonality(_skillDefaults, skillValues.Where(x=>x.CharacterId.Equals(c.Id,StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var interactions = Read("SELECT Id, Key, Title, StartNodeKey FROM Interactions ORDER BY Title",
            r => new SimInteraction(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        var nodes = Read("SELECT Id, InteractionId, Key, Round, HostIntroduction FROM Nodes ORDER BY SortOrder, Id",
            r => new SimNode(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), r.GetString(4)));
        var options = Read(@"SELECT Id, NodeId, Key, Label, BaseSuccessChance, SuccessNextNodeKey, FailureNextNodeKey,
            DefaultPlayerLine, DefaultHostSuccessLine, DefaultHostFailureLine,
            SuccessTargetReaction, FailureTargetReaction FROM Options ORDER BY SortOrder, Id",
            r => new SimChoice(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                S(r, 5), S(r, 6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10), r.GetString(11)));
        foreach (var node in nodes) node.Options.AddRange(options.Where(o => o.NodeId == node.Id));
        foreach (var interaction in interactions) interaction.Nodes.AddRange(nodes.Where(n => n.InteractionId == interaction.Id));
        Interactions = interactions;

        _requirements = Read("SELECT OptionId, Direction, Attribute, Minimum, Maximum FROM Requirements",
            r => new SimRequirement(r.GetInt32(0), r.GetString(1), r.GetString(2), N(r, 3), N(r, 4)))
            .GroupBy(x => x.OptionId).ToDictionary(g => g.Key, g => g.ToList());
        _modifiers = Read("SELECT OptionId, Direction, Attribute, Factor FROM ChanceModifiers",
            r => new SimModifier(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3)))
            .GroupBy(x => x.OptionId).ToDictionary(g => g.Key, g => g.ToList());
        _effects = Read("SELECT OptionId, Outcome, Direction, Attribute, Delta FROM Effects ORDER BY Id",
            r => new SimEffect(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4)))
            .GroupBy(x => x.OptionId).ToDictionary(g => g.Key, g => g.ToList());
        _profiles = Read("SELECT Key FROM CheckProfiles", r => r.GetString(0))
            .ToDictionary(key => key,
                key => (IReadOnlyList<ChanceFactor>)new List<ChanceFactor>(), StringComparer.OrdinalIgnoreCase);
        var groupedTerms = Read("SELECT ProfileKey,Direction,Attribute,Factor FROM CheckProfileModifiers ORDER BY ProfileKey,Direction,Attribute",
            r => (Key: r.GetString(0), Term: new ChanceFactor(r.GetString(1),r.GetString(2),r.GetDouble(3))))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groupedTerms) _profiles[group.Key] = group.Select(x => x.Term).ToList();
        if (!_profiles.ContainsKey("STANDARD"))
            throw new InvalidOperationException("Missing STANDARD check profile. Reopen master database in Studio.");
        _optionProfiles = Read("SELECT OptionId,ProfileKey FROM OptionCheckProfiles",
            r => (OptionId: r.GetInt32(0), Key: r.GetString(1)))
            .ToDictionary(x => x.OptionId, x => x.Key);
        _checks = Read("SELECT OptionId, ActorSkill, DefenderSkill, CheckMode, ActorFactor, DefenderFactor FROM SkillChecks",
            r => new SimSkillCheck(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDouble(4), r.GetDouble(5)))
            .ToDictionary(x => x.OptionId);
        _phrases = Read("SELECT Id, OptionId, CharacterId, VisitorId, Outcome, Text, ImagePath FROM Lines ORDER BY Id",
            r => new SimPhrase(r.GetInt32(0), r.GetInt32(1), r.GetString(2), S(r, 3), S(r, 4), r.GetString(5), r.GetString(6)));
        _introductions = HasTable("NodeIntroductions")
            ? Read("SELECT Id, NodeId, CharacterId, VisitorId, Text FROM NodeIntroductions ORDER BY Id",
                r => new SimIntroduction(r.GetInt32(0), r.GetInt32(1), r.GetString(2), S(r, 3), r.GetString(4)))
            : new List<SimIntroduction>();

        _variants = new List<SimVariant>();
        if (HasTable("ConditionalDialogueVariants") && HasTable("ConditionalDialogueConditions"))
        {
            var clauses = Read("SELECT VariantId, Direction, Attribute, Minimum, Maximum FROM ConditionalDialogueConditions ORDER BY Id",
                r => (Id: r.GetInt32(0), Clause: new DialogueClause(r.GetString(1), r.GetString(2), N(r, 3), N(r, 4))))
                .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<DialogueClause>)g.Select(x => x.Clause).ToArray());
            _variants = Read(@"SELECT Id, NodeId, OptionId, CharacterId, VisitorId, Outcome, Priority, Text, ImagePath
                FROM ConditionalDialogueVariants ORDER BY Id", r =>
            {
                int id = r.GetInt32(0);
                var candidate = new DialogueCandidate(id, S(r, 3), S(r, 4), S(r, 5), r.GetInt32(6),
                    r.GetString(7), r.GetString(8), true,
                    clauses.TryGetValue(id, out var list) ? list : Array.Empty<DialogueClause>());
                return new SimVariant(N(r, 1), N(r, 2), candidate);
            });
        }

        // Phase 12E: master defaults, status requirements/effects and choice visibility.
        // Read-only like the rest of the Interaction Test Lab projection.
        LoadStatusSimulationData();
        LoadDynamicTargetSimulationData();
        LoadMemoryEncounterSimulationData();
    }

    private static string? S(SqliteDataReader r, int index) => r.IsDBNull(index) ? null : r.GetString(index);
    private static int? N(SqliteDataReader r, int index) => r.IsDBNull(index) ? null : r.GetInt32(index);
    private bool HasTable(string name)
    {
        using var cmd = new SqliteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name", _db);
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() is not null;
    }
    private List<T> Read<T>(string sql, Func<SqliteDataReader, T> build)
    {
        using var command = new SqliteCommand(sql, _db);
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(build(reader));
        return result;
    }

    public SimPersonality Personality(string characterId) =>
        _personalities.TryGetValue(characterId, out var value) ? value : new SimPersonality(_skillDefaults, new Dictionary<string,int>());
    public List<SimRequirement> Requirements(int optionId) => _requirements.GetValueOrDefault(optionId) ?? [];
    public List<SimModifier> Modifiers(int optionId) => _modifiers.GetValueOrDefault(optionId) ?? [];
    public List<SimEffect> Effects(int optionId, bool success) =>
        (_effects.GetValueOrDefault(optionId) ?? []).Where(x => string.Equals(x.Outcome, success ? "SUCCESS" : "FAILURE", StringComparison.OrdinalIgnoreCase)).ToList();
    public SimSkillCheck? Skill(int optionId) => _checks.GetValueOrDefault(optionId);
    public string ProfileForOption(int optionId) => _optionProfiles.GetValueOrDefault(optionId) ?? "STANDARD";
    public IReadOnlyList<ChanceFactor> Profile(string profileKey) =>
        _profiles.TryGetValue(profileKey, out var terms) ? terms :
        throw new InvalidOperationException($"Unknown check profile '{profileKey}'.");
    public SimInteraction Interaction(string key) => Interactions.First(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    public string Name(string id) => Characters.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

    public SimulationLine Intro(SimNode node, string speaker, string other, IReadOnlyDictionary<string, int> stats) =>
        Select(_introductions.Where(x => x.NodeId == node.Id).Select(x => new DialogueCandidate(x.Id,
                x.Speaker, x.Other, null, 0, x.Text, "", false, Array.Empty<DialogueClause>()))
            .Concat(_variants.Where(x => x.NodeId == node.Id).Select(x => x.Candidate)),
            speaker, other, null, stats, node.GenericIntroduction, "Generic node introduction");

    public IReadOnlySet<string> AssignedTraits(string characterId) =>
        _originalTraitAssignments.Where(a => a.CharacterId.Equals(characterId,StringComparison.OrdinalIgnoreCase))
            .Select(a => a.TraitKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public PersonalityEffectRules CreateSandboxTraitRules(IEnumerable<(string CharacterId,string TraitKey)> assignments) =>
        new(assignments, _traitModifiers);

    public PersonalityAdjustment AdjustRelationshipEffect(string receiverId,string attribute,
        int baseDelta,bool succeeded) => _traitRules.Adjust(receiverId,attribute,baseDelta,succeeded);

    public SimulationLine Line(SimChoice choice, string speaker, string other, bool success,
        IReadOnlyDictionary<string, int> stats, string fallback) =>
        Select(_phrases.Where(x => x.OptionId == choice.Id).Select(x => new DialogueCandidate(x.Id,
                x.Speaker, x.Other, x.Outcome, 0, x.Text, x.ImagePath, false, Array.Empty<DialogueClause>()))
            .Concat(_variants.Where(x => x.OptionId == choice.Id).Select(x => x.Candidate)),
            speaker, other, success ? "SUCCESS" : "FAILURE", stats, fallback, "Generic choice phrase");

    private static SimulationLine Select(IEnumerable<DialogueCandidate> candidates, string speaker, string other,
        string? outcome, IReadOnlyDictionary<string, int> stats, string fallback, string fallbackSource)
    {
        var list = candidates.ToList();
        var resolution = ConditionalDialogueRules.Resolve(list, speaker, other, outcome, stats);
        string text = resolution.Candidate?.Text ?? fallback;
        string source = resolution.Candidate is null ? fallbackSource : resolution.Source;
        var messages = new List<string>
        {
            $"Speaker: {speaker}; counterpart: {other}; outcome: {outcome ?? "INTRO"}",
            $"Selected: {source} | {resolution.Explanation}"
        };
        foreach (var candidate in list)
        {
            var reasons = new List<string>();
            if (candidate.CharacterId is { Length: > 0 } id && !id.Equals(speaker, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"speaker must be {id}");
            if (candidate.OtherCharacterId is { Length: > 0 } otherId && !otherId.Equals(other, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"counterpart must be {otherId}");
            if (candidate.Outcome is { Length: > 0 } expected && !expected.Equals(outcome, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"outcome must be {expected}");
            if (candidate.Conditional && candidate.Clauses.Count == 0) reasons.Add("conditional rule has no conditions");
            foreach (var condition in candidate.Clauses)
            {
                string key = ConditionalDialogueRules.StatKey(condition.Direction, condition.Attribute);
                if (!stats.TryGetValue(key, out int actual)) reasons.Add($"{key}: stat unavailable");
                else if (condition.Minimum is int minimum && actual < minimum || condition.Maximum is int maximum && actual > maximum)
                    reasons.Add($"{key} is {actual}; requires " +
                        (condition.Minimum is int min ? $">={min} " : "") +
                        (condition.Maximum is int max ? $"<={max}" : ""));
            }
            string label = candidate.Conditional ? "CONDITIONAL" : "STANDARD";
            messages.Add($"{(reasons.Count == 0 ? "ELIGIBLE" : "SKIPPED ")} | {label} #{candidate.Id} | " +
                $"{candidate.CharacterId ?? "ANY"} → {candidate.OtherCharacterId ?? "ANY"} | priority {candidate.Priority} | " +
                (reasons.Count == 0 ? "all checks passed" : string.Join("; ", reasons)));
        }
        if (list.Count == 0) messages.Add("No overrides registered; using original generic text.");
        return new SimulationLine(text, source, resolution.Candidate?.ImagePath ?? "", string.Join(Environment.NewLine, messages));
    }

    public string ValidateInteraction(SimInteraction interaction)
    {
        var warnings = new List<string>();
        var nodes = interaction.Nodes.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        if (!nodes.ContainsKey(interaction.StartNodeKey))
            warnings.Add($"ERROR: Start node '{interaction.StartNodeKey}' is missing.");
        foreach (var node in interaction.Nodes)
        foreach (var choice in node.Options)
        foreach (var destination in new[] { choice.SuccessNext, choice.FailureNext })
            if (!string.IsNullOrWhiteSpace(destination) && !nodes.ContainsKey(destination))
                warnings.Add($"ERROR: {node.Key}/{choice.Key} points to missing node '{destination}'.");
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        if (nodes.ContainsKey(interaction.StartNodeKey)) queue.Enqueue(interaction.StartNodeKey);
        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            if (!reachable.Add(key)) continue;
            foreach (var choice in nodes[key].Options)
            foreach (var next in new[] { choice.SuccessNext, choice.FailureNext })
                if (!string.IsNullOrWhiteSpace(next) && nodes.ContainsKey(next) && !reachable.Contains(next)) queue.Enqueue(next);
        }
        foreach (var node in interaction.Nodes.Where(x => !reachable.Contains(x.Key)))
            warnings.Add($"WARNING: Node '{node.Key}' cannot be reached from the start node through defined branches.");
        foreach (var node in interaction.Nodes.Where(x => x.Options.Count == 0))
            warnings.Add($"WARNING: Node '{node.Key}' has no choices.");
        foreach (var variant in _variants.Where(x => x.NodeId is int id && interaction.Nodes.Any(n => n.Id == id) ||
             x.OptionId is int choiceId && interaction.Nodes.SelectMany(n => n.Options).Any(o => o.Id == choiceId)))
        {
            if (variant.Candidate.Clauses.Count == 0)
                warnings.Add($"WARNING: Conditional variant #{variant.Candidate.Id} has no conditions.");
            foreach (var group in variant.Candidate.Clauses.GroupBy(x => ConditionalDialogueRules.StatKey(x.Direction, x.Attribute)))
            {
                var definition = Attributes.FirstOrDefault(a =>
                    a.Key.Equals(group.First().Attribute,StringComparison.OrdinalIgnoreCase));
                if (definition is null)
                { warnings.Add($"ERROR: Unknown relationship attribute in {group.Key}."); continue; }
                int min = group.Max(x => x.Minimum ?? definition.Minimum);
                int max = group.Min(x => x.Maximum ?? definition.Maximum);
                if (min > max) warnings.Add($"ERROR: Conditional variant #{variant.Candidate.Id} has contradictory {group.Key} bounds ({min}..{max}).");
            }
        }
        var variantIds = interaction.Nodes.Select(n => n.Id).ToHashSet();
        var choiceIds = interaction.Nodes.SelectMany(n => n.Options).Select(o => o.Id).ToHashSet();
        string Signature(SimVariant variant)
        {
            var c = variant.Candidate;
            string conditions = string.Join("|", c.Clauses.Select(x =>
                $"{x.Direction}:{x.Attribute}:{x.Minimum}:{x.Maximum}").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            return $"{variant.NodeId}:{variant.OptionId}:{c.CharacterId?.ToUpperInvariant()}:" +
                   $"{c.OtherCharacterId?.ToUpperInvariant()}:{c.Outcome?.ToUpperInvariant()}:{c.Priority}:{conditions}";
        }
        foreach (var group in _variants.Where(x => x.NodeId is int node && variantIds.Contains(node) ||
                  x.OptionId is int option && choiceIds.Contains(option)).GroupBy(Signature))
        {
            if (group.Count() > 1)
                warnings.Add($"WARNING: Conditional variants [{string.Join(", ", group.Select(x => x.Candidate.Id))}] have identical scope, outcome, priority and conditions. One will shadow the others.");
        }
        foreach (var node in interaction.Nodes)
        foreach (var choice in node.Options)
        {
            foreach (var group in Requirements(choice.Id).GroupBy(x => x.Direction.ToUpperInvariant() + ":" + x.Attribute.ToUpperInvariant()))
            {
                var definition = Attributes.FirstOrDefault(a =>
                    a.Key.Equals(group.First().Attribute,StringComparison.OrdinalIgnoreCase));
                if (definition is null)
                { warnings.Add($"ERROR: Unknown relationship attribute in {group.Key}."); continue; }
                int min = group.Max(x => x.Minimum ?? definition.Minimum);
                int max = group.Min(x => x.Maximum ?? definition.Maximum);
                if (min > max)
                    warnings.Add($"ERROR: Choice {node.Key}/{choice.Key} can never unlock: {group.Key} requires {min}..{max}.");
            }
        }
        return warnings.Count == 0 ? $"No structural issues detected in {interaction.Key}." :
            string.Join(Environment.NewLine, warnings);
    }

    public void Dispose() => _db.Dispose();
}

public sealed record SimCharacter(string Id, string Name)
{
    public override string ToString() => $"{Name} [{Id}]";
}
public sealed class SimPersonality
{
    private readonly IReadOnlyDictionary<string,int> _defaults;
    private readonly IReadOnlyDictionary<string,int> _values;
    public SimPersonality(IReadOnlyDictionary<string,int> defaults,IReadOnlyDictionary<string,int> values)
    { _defaults=defaults;_values=values; }
    public int Get(string key) => _values.TryGetValue(key,out var value) ? value :
        _defaults.TryGetValue(key,out var fallback) ? fallback :
        throw new InvalidOperationException($"Undefined skill '{key}'.");
}

public sealed class SimInteraction(int id, string key, string title, string startNodeKey)
{
    public int Id { get; } = id;
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string StartNodeKey { get; } = startNodeKey;
    public List<SimNode> Nodes { get; } = [];
    public override string ToString() => $"{Title} [{Key}]";
}
public sealed class SimNode(int id, int interactionId, string key, int round, string genericIntroduction)
{
    public int Id { get; } = id;
    public int InteractionId { get; } = interactionId;
    public string Key { get; } = key;
    public int Round { get; } = round;
    public string GenericIntroduction { get; } = genericIntroduction;
    public List<SimChoice> Options { get; } = [];
}
public sealed record SimChoice(int Id, int NodeId, string Key, string Label, int BaseChance,
    string? SuccessNext, string? FailureNext, string GenericActorLine, string GenericHostSuccess,
    string GenericHostFailure, string SuccessReaction, string FailureReaction);
public sealed record SimRequirement(int OptionId, string Direction, string Attribute, int? Minimum, int? Maximum);
public sealed record SimModifier(int OptionId, string Direction, string Attribute, double Factor);
public sealed record SimEffect(int OptionId, string Outcome, string Direction, string Attribute, int Delta);
public sealed record SimSkillCheck(int OptionId, string ActorSkill, string DefenderSkill, string CheckMode, double ActorFactor, double DefenderFactor);
public sealed record SimIntroduction(int Id, int NodeId, string Speaker, string? Other, string Text);
public sealed record SimPhrase(int Id, int OptionId, string Speaker, string? Other, string? Outcome, string Text, string ImagePath);
public sealed record SimVariant(int? NodeId, int? OptionId, DialogueCandidate Candidate);
public sealed record SimulationLine(string Text, string Source, string ImagePath, string Inspector);

public sealed record SimTraitDefinition(string Key, string Name);
