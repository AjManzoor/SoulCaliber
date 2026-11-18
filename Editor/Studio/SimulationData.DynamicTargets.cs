using SoulCaliber.Dialogue;

namespace SoulCaliberStudio;

/// <summary>Read-only dynamic target and pair status effect projection for Test Lab.</summary>
public sealed partial class SimulationData
{
    private readonly Dictionary<int, DynamicTargetRule> _dynamicTargetRules = [];
    private readonly Dictionary<int, List<DynamicPairStatusEffect>> _successPairEffects = [];
    private readonly Dictionary<int, List<DynamicPairStatusEffect>> _failurePairEffects = [];

    public DynamicTargetRule? DynamicTarget(int optionId) =>
        _dynamicTargetRules.GetValueOrDefault(optionId);

    public IReadOnlyList<DynamicPairStatusEffect> PairStatusEffects(int optionId, bool success) =>
        (success ? _successPairEffects : _failurePairEffects).TryGetValue(optionId, out var effects)
            ? effects : [];

    private void LoadDynamicTargetSimulationData()
    {
        // Old master DBs without these features remain usable in the Test Lab.
        if (!HasTable("OptionDynamicTargets")) return;
        var members = new Dictionary<int, List<string>>();
        if (HasTable("OptionDynamicTargetCharacters"))
        {
            foreach (var row in Read("SELECT OptionId, CharacterId FROM OptionDynamicTargetCharacters ORDER BY OptionId, SortOrder, CharacterId",
                         r => (Id: r.GetInt32(0), Character: r.GetString(1))))
            {
                if (!members.TryGetValue(row.Id, out var ids)) members[row.Id] = ids = [];
                ids.Add(row.Character);
            }
        }
        using var column = _db.CreateCommand();
        column.CommandText = "SELECT 1 FROM pragma_table_info('OptionDynamicTargets') WHERE name='SelectionMode'";
        bool hasSelectionMode = column.ExecuteScalar() is not null;
        string query = hasSelectionMode
            ? "SELECT OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,SelectionMode FROM OptionDynamicTargets"
            : "SELECT OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,'GROUP' FROM OptionDynamicTargets";
        foreach (var rule in Read(query, r => (Id: r.GetInt32(0), Owner: r.GetString(1),
                      Direction: r.GetString(2), Status: r.GetString(3), Mode: r.GetString(4))))
        {
            members.TryGetValue(rule.Id, out var allowed);
            _dynamicTargetRules.Add(rule.Id, new DynamicTargetRule(
                rule.Owner, rule.Direction, rule.Status, rule.Mode, allowed));
        }
        if (!HasTable("OptionPairStatusEffects")) return;
        foreach (var effect in Read("SELECT OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey FROM OptionPairStatusEffects ORDER BY Id",
                     r => (OptionId: r.GetInt32(0), Outcome: r.GetString(1),
                           Effect: new DynamicPairStatusEffect(r.GetString(2), r.GetString(3),
                               r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)))))
        {
            var destination = effect.Outcome switch
            {
                "SUCCESS" => _successPairEffects,
                "FAILURE" => _failurePairEffects,
                _ => throw new InvalidOperationException("Invalid pair-status-effect outcome: " + effect.Outcome)
            };
            if (!destination.TryGetValue(effect.OptionId, out var effects))
                destination[effect.OptionId] = effects = [];
            effects.Add(effect.Effect);
        }
    }
}
