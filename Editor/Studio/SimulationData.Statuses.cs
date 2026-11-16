using Microsoft.Data.Sqlite;

namespace SoulCaliberStudio;

public sealed record SimStatus(string Key, string Name)
{
    public override string ToString() => $"{Name} [{Key}]";
}

public sealed record SimStatusRequirement(int OptionId, string Direction, string StatusKey);
public sealed record SimStatusEffect(int OptionId, string Outcome, string Direction, string StatusKey);

public sealed partial class SimulationData
{
    private readonly List<SimStatus> _statusDefinitions = [];
    private readonly Dictionary<(string From, string To), string> _startingStatuses = new();
    private readonly Dictionary<int, List<SimStatusRequirement>> _statusRequirements = new();
    private readonly Dictionary<int, List<SimStatusEffect>> _statusEffects = new();
    private readonly Dictionary<int, bool> _hiddenChoices = new();

    public IReadOnlyList<SimStatus> Statuses => _statusDefinitions;

    private void LoadStatusSimulationData()
    {
        foreach (var table in new[] { "RelationshipStatuses", "CharacterDefaultStatuses", "OptionStatusRequirements", "OptionStatusEffects" })
            if (!HasTable(table))
                throw new InvalidOperationException($"Phase 12E requires the '{table}' table. Open the master database in the latest Studio, apply Phase 12D migrations, and reopen the Test Lab.");

        // A new status can be added in Studio without changing simulation code.
        _statusDefinitions.AddRange(Read(
            "SELECT Key, Name FROM RelationshipStatuses WHERE IsActive=1 ORDER BY SortOrder,Name,Key",
            r => new SimStatus(r.GetString(0), r.GetString(1))));
        if (!_statusDefinitions.Any(x => x.Key.Equals("NEUTRAL", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("An active NEUTRAL relationship status is required.");

        foreach (var row in Read(
            "SELECT SourceCharacterId,TargetCharacterId,StatusKey FROM CharacterDefaultStatuses",
            r => (From: r.GetString(0), To: r.GetString(1), Status: r.GetString(2))))
        {
            var pair = (row.From.ToUpperInvariant(), row.To.ToUpperInvariant());
            _startingStatuses.Add(pair, row.Status);
        }

        foreach (var requirement in Read(
            "SELECT OptionId,Direction,StatusKey FROM OptionStatusRequirements ORDER BY Id",
            r => new SimStatusRequirement(r.GetInt32(0), r.GetString(1), r.GetString(2))))
        {
            if (!_statusRequirements.TryGetValue(requirement.OptionId, out var rows))
                _statusRequirements[requirement.OptionId] = rows = [];
            rows.Add(requirement);
        }

        foreach (var effect in Read(
            "SELECT OptionId,Outcome,Direction,StatusKey FROM OptionStatusEffects ORDER BY Id",
            r => new SimStatusEffect(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3))))
        {
            if (!_statusEffects.TryGetValue(effect.OptionId, out var rows))
                _statusEffects[effect.OptionId] = rows = [];
            rows.Add(effect);
        }

        // Fail clearly on old published content rather than silently showing locked options
        // where Godot would hide them.
        using (var col = new SqliteCommand(
            "SELECT 1 FROM pragma_table_info('Options') WHERE name='UnmetRequirementBehaviour'", _db))
            if (col.ExecuteScalar() is null)
                throw new InvalidOperationException("Phase 12D choice-visibility migration is missing. Reopen the master database in Studio.");

        foreach (var row in Read("SELECT Id,UnmetRequirementBehaviour FROM Options",
                     r => (Id: r.GetInt32(0), Mode: r.GetString(1))))
            _hiddenChoices.Add(row.Id, row.Mode.Equals("HIDE", StringComparison.OrdinalIgnoreCase));
    }

    public string StartingStatus(string fromCharacterId, string toCharacterId) =>
        _startingStatuses.TryGetValue((fromCharacterId.ToUpperInvariant(), toCharacterId.ToUpperInvariant()), out var status)
            ? status : "NEUTRAL";

    public IReadOnlyList<SimStatusRequirement> StatusRequirements(int choiceId) =>
        _statusRequirements.TryGetValue(choiceId, out var rows) ? rows : [];

    public IReadOnlyList<SimStatusEffect> StatusEffects(int choiceId, bool success) =>
        _statusEffects.TryGetValue(choiceId, out var rows)
            ? rows.Where(x => x.Outcome.Equals(success ? "SUCCESS" : "FAILURE", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

    public bool HideWhenUnmet(int choiceId) => _hiddenChoices.GetValueOrDefault(choiceId);
    public string StatusName(string key) =>
        _statusDefinitions.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;
}
