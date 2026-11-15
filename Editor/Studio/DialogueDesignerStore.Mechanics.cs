using Microsoft.Data.Sqlite;

namespace SoulCaliberStudio;

// Phase 6: these are the EXISTING content tables already read by Godot.
// There is deliberately no schema migration or new gameplay logic here.
public sealed partial class DialogueDesignerStore
{
    public static readonly string[] RelationshipAttributes =
        ["Love", "Fear", "Allyship", "Respect", "Trust", "Romance"];

    public static readonly string[] Directions =
        ["PARTNER_TO_ACTOR", "ACTOR_TO_PARTNER"];

    public static readonly string[] Abilities =
        ["Persuasion", "Intimidation", "Deception", "Charm", "Insight", "Resolve"];

    public static readonly string[] CheckModes = ["STANDARD", "INTIMIDATION"];

    public sealed record RelationshipEffectRow(int Id, int OptionId, string Outcome,
        string Direction, string Attribute, int Delta)
    {
        public override string ToString() => $"{Outcome,-7}  {DirectionLabel(Direction)}: {Attribute} {Delta:+#;-#;0}";
    }

    public sealed record RelationshipRequirementRow(int Id, int OptionId, string Direction,
        string Attribute, int? Minimum, int? Maximum)
    {
        public override string ToString() =>
            $"{DirectionLabel(Direction)}: {Attribute} " +
            (Minimum is null ? "" : $"≥ {Minimum} ") +
            (Maximum is null ? "" : $"≤ {Maximum}");
    }

    public sealed record ChanceModifierRow(int Id, int OptionId, string Direction,
        string Attribute, double Factor)
    {
        public override string ToString() =>
            $"{DirectionLabel(Direction)}: {Attribute} × {Factor:+0.###;-0.###;0} points";
    }

    public sealed record SkillCheckRow(int OptionId, string ActorSkill, string DefenderSkill,
        string CheckMode, double ActorFactor, double DefenderFactor);

    private static string DirectionLabel(string direction) => direction switch
    {
        "PARTNER_TO_ACTOR" => "Recipient → Initiator",
        "ACTOR_TO_PARTNER" => "Initiator → Recipient",
        _ => direction
    };

    private static string CheckDirection(string direction)
    {
        if (!Directions.Contains(direction, StringComparer.Ordinal))
            throw new InvalidOperationException("Select a valid relationship direction.");
        return direction;
    }

    private static string CheckAttribute(string attribute)
    {
        var canonical = RelationshipAttributes.FirstOrDefault(x =>
            string.Equals(x, attribute, StringComparison.OrdinalIgnoreCase));
        return canonical ?? throw new InvalidOperationException("Select a valid relationship attribute.");
    }

    private static string CheckOutcome(string outcome)
    {
        if (outcome is not ("SUCCESS" or "FAILURE"))
            throw new InvalidOperationException("Select SUCCESS or FAILURE.");
        return outcome;
    }

    private static void CheckNumber(double value, string name)
    {
        if (!double.IsFinite(value)) throw new InvalidOperationException($"{name} must be finite.");
    }

    private void CheckOptionId(int optionId)
    {
        if (!Exists(_db, "SELECT 1 FROM Options WHERE Id=$id", ("$id", optionId)))
            throw new InvalidOperationException("This choice no longer exists. Reload the editor.");
    }

    public List<RelationshipEffectRow> RelationshipEffects(int optionId)
    {
        using var cmd = Command("SELECT Id, OptionId, Outcome, Direction, Attribute, Delta FROM Effects WHERE OptionId=$id ORDER BY Outcome, Id");
        Add(cmd, "$id", optionId);
        using var r = cmd.ExecuteReader();
        var rows = new List<RelationshipEffectRow>();
        while (r.Read()) rows.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetInt32(5)));
        return rows;
    }

    public int SaveRelationshipEffect(RelationshipEffectRow item)
    {
        CheckOptionId(item.OptionId);
        string outcome = CheckOutcome(item.Outcome);
        string direction = CheckDirection(item.Direction);
        string attribute = CheckAttribute(item.Attribute);
        if (Exists(_db, """
            SELECT 1 FROM Effects WHERE OptionId=$option AND Outcome=$outcome
              AND Direction=$direction AND Attribute=$attribute AND Id<>$id
            """, ("$option", item.OptionId), ("$outcome", outcome),
            ("$direction", direction), ("$attribute", attribute), ("$id", item.Id)))
            throw new InvalidOperationException(
                "An effect already exists for this outcome, direction and attribute. Select that record and edit its amount instead.");
        int id = item.Id;
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? "INSERT INTO Effects(OptionId, Outcome, Direction, Attribute, Delta) VALUES($option,$outcome,$direction,$attribute,$delta)"
                : "UPDATE Effects SET Outcome=$outcome, Direction=$direction, Attribute=$attribute, Delta=$delta WHERE Id=$id AND OptionId=$option", tx);
            Add(cmd, "$id", item.Id); Add(cmd, "$option", item.OptionId); Add(cmd, "$outcome", outcome);
            Add(cmd, "$direction", direction); Add(cmd, "$attribute", attribute); Add(cmd, "$delta", item.Delta);
            Changed(cmd, item.Id != 0);
            if (item.Id == 0)
            {
                using var last = Command("SELECT last_insert_rowid()", tx);
                id = Convert.ToInt32((long)last.ExecuteScalar()!);
            }
        });
        return id;
    }

    public void DeleteRelationshipEffect(int id, int optionId) =>
        DeleteMechanic("DELETE FROM Effects WHERE Id=$id AND OptionId=$option", id, optionId);

    public List<RelationshipRequirementRow> RelationshipRequirements(int optionId)
    {
        using var cmd = Command("SELECT Id, OptionId, Direction, Attribute, Minimum, Maximum FROM Requirements WHERE OptionId=$id ORDER BY Id");
        Add(cmd, "$id", optionId);
        using var r = cmd.ExecuteReader();
        var rows = new List<RelationshipRequirementRow>();
        while (r.Read()) rows.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
            r.IsDBNull(4) ? null : r.GetInt32(4), r.IsDBNull(5) ? null : r.GetInt32(5)));
        return rows;
    }

    public int SaveRelationshipRequirement(RelationshipRequirementRow item)
    {
        CheckOptionId(item.OptionId);
        string direction = CheckDirection(item.Direction);
        string attribute = CheckAttribute(item.Attribute);
        if (item.Minimum is null && item.Maximum is null)
            throw new InvalidOperationException("Set at least a minimum or maximum relationship value.");
        int lowerBound = attribute is "Fear" or "Romance" ? 0 : -100;
        if (item.Minimum is int min && (min < lowerBound || min > 100) ||
            item.Maximum is int max && (max < lowerBound || max > 100))
            throw new InvalidOperationException($"{attribute} must be between {lowerBound} and 100.");
        if (item.Minimum is int lo && item.Maximum is int hi && lo > hi)
            throw new InvalidOperationException("Minimum cannot exceed maximum.");
        int id = item.Id;
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? "INSERT INTO Requirements(OptionId, Direction, Attribute, Minimum, Maximum) VALUES($option,$direction,$attribute,$min,$max)"
                : "UPDATE Requirements SET Direction=$direction, Attribute=$attribute, Minimum=$min, Maximum=$max WHERE Id=$id AND OptionId=$option", tx);
            Add(cmd, "$id", item.Id); Add(cmd, "$option", item.OptionId); Add(cmd, "$direction", direction);
            Add(cmd, "$attribute", attribute); Add(cmd, "$min", item.Minimum); Add(cmd, "$max", item.Maximum);
            Changed(cmd, item.Id != 0);
            if (item.Id == 0)
            {
                using var last = Command("SELECT last_insert_rowid()", tx);
                id = Convert.ToInt32((long)last.ExecuteScalar()!);
            }
        });
        return id;
    }

    public void DeleteRelationshipRequirement(int id, int optionId) =>
        DeleteMechanic("DELETE FROM Requirements WHERE Id=$id AND OptionId=$option", id, optionId);

    public List<ChanceModifierRow> ChanceModifiers(int optionId)
    {
        using var cmd = Command("SELECT Id, OptionId, Direction, Attribute, Factor FROM ChanceModifiers WHERE OptionId=$id ORDER BY Id");
        Add(cmd, "$id", optionId);
        using var r = cmd.ExecuteReader();
        var rows = new List<ChanceModifierRow>();
        while (r.Read()) rows.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetDouble(4)));
        return rows;
    }

    public int SaveChanceModifier(ChanceModifierRow item)
    {
        CheckOptionId(item.OptionId);
        string direction = CheckDirection(item.Direction);
        string attribute = CheckAttribute(item.Attribute);
        CheckNumber(item.Factor, "Probability factor");
        int id = item.Id;
        Write(tx =>
        {
            using var cmd = Command(item.Id == 0
                ? "INSERT INTO ChanceModifiers(OptionId, Direction, Attribute, Factor) VALUES($option,$direction,$attribute,$factor)"
                : "UPDATE ChanceModifiers SET Direction=$direction, Attribute=$attribute, Factor=$factor WHERE Id=$id AND OptionId=$option", tx);
            Add(cmd, "$id", item.Id); Add(cmd, "$option", item.OptionId);
            Add(cmd, "$direction", direction); Add(cmd, "$attribute", attribute); Add(cmd, "$factor", item.Factor);
            Changed(cmd, item.Id != 0);
            if (item.Id == 0)
            {
                using var last = Command("SELECT last_insert_rowid()", tx);
                id = Convert.ToInt32((long)last.ExecuteScalar()!);
            }
        });
        return id;
    }

    public void DeleteChanceModifier(int id, int optionId) =>
        DeleteMechanic("DELETE FROM ChanceModifiers WHERE Id=$id AND OptionId=$option", id, optionId);

    public SkillCheckRow? GetSkillCheck(int optionId)
    {
        using var cmd = Command("SELECT OptionId, ActorSkill, DefenderSkill, CheckMode, ActorFactor, DefenderFactor FROM SkillChecks WHERE OptionId=$id");
        Add(cmd, "$id", optionId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new(r.GetInt32(0), r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetDouble(4), r.GetDouble(5)) : null;
    }

    public void SaveSkillCheck(SkillCheckRow item)
    {
        CheckOptionId(item.OptionId);
        string actor = Abilities.FirstOrDefault(x => string.Equals(x, item.ActorSkill, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select an initiator skill.");
        string defender = Abilities.FirstOrDefault(x => string.Equals(x, item.DefenderSkill, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Select a recipient skill.");
        if (!CheckModes.Contains(item.CheckMode, StringComparer.Ordinal))
            throw new InvalidOperationException("Select STANDARD or INTIMIDATION mode.");
        CheckNumber(item.ActorFactor, "Initiator factor");
        CheckNumber(item.DefenderFactor, "Recipient factor");
        if (item.ActorFactor < 0 || item.DefenderFactor < 0)
            throw new InvalidOperationException("Skill factors cannot be negative.");
        Write(tx =>
        {
            using var cmd = Command("""
                INSERT INTO SkillChecks(OptionId, ActorSkill, DefenderSkill, CheckMode, ActorFactor, DefenderFactor)
                VALUES($option, $actor, $defender, $mode, $actorFactor, $defenderFactor)
                ON CONFLICT(OptionId) DO UPDATE SET
                    ActorSkill=excluded.ActorSkill, DefenderSkill=excluded.DefenderSkill,
                    CheckMode=excluded.CheckMode, ActorFactor=excluded.ActorFactor,
                    DefenderFactor=excluded.DefenderFactor
                """, tx);
            Add(cmd, "$option", item.OptionId); Add(cmd, "$actor", actor); Add(cmd, "$defender", defender);
            Add(cmd, "$mode", item.CheckMode); Add(cmd, "$actorFactor", item.ActorFactor); Add(cmd, "$defenderFactor", item.DefenderFactor);
            Changed(cmd);
        });
    }

    public void DeleteSkillCheck(int optionId)
    {
        CheckOptionId(optionId);
        Write(tx =>
        {
            using var cmd = Command("DELETE FROM SkillChecks WHERE OptionId=$option", tx);
            Add(cmd, "$option", optionId);
            Changed(cmd, true);
        });
    }

    private void DeleteMechanic(string sql, int id, int optionId)
    {
        CheckOptionId(optionId);
        Write(tx =>
        {
            using var cmd = Command(sql, tx);
            Add(cmd, "$id", id); Add(cmd, "$option", optionId);
            Changed(cmd, true);
        });
    }

    public string ChoiceMechanicsSummary(int optionId)
    {
        int effects = RelationshipEffects(optionId).Count;
        int requirements = RelationshipRequirements(optionId).Count;
        int modifiers = ChanceModifiers(optionId).Count;
        bool skill = GetSkillCheck(optionId) is not null;
        return $"Effects: {effects} · Requirements: {requirements} · Chance modifiers: {modifiers} · " +
               (skill ? "Skill check configured" : "No skill check (base chance + relationships)");
    }
}
