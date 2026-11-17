using SoulCaliber.Dialogue;
using SoulCaliber.Shared;

// Dependency-free console regression suite. Directly compiles the six production
// rule/guard files linked by Regression.csproj; no Godot or test NuGet dependency.
internal static class Program
{
    private const string Law = "LAW";
    private const string Youri = "YOURI";
    private static readonly HashSet<string> CharacterKeys = new(StringComparer.OrdinalIgnoreCase)
        { "LAW", "YOURI", "NOBUNOGI", "THORN", "TINA" };
    private static readonly HashSet<string> MemoryKeys = new(StringComparer.OrdinalIgnoreCase)
        { "DEFEAT", "BETRAYAL", "REWARDED" };
    private static readonly HashSet<string> StatusKeys = new(StringComparer.OrdinalIgnoreCase)
        { "ALLY", "NEUTRAL", "ENEMY" };

    private static int Main()
    {
        var tests = new (string, Action)[]
        {
            ("memory: known incident matches actor, affected and knower", MemoryMatch),
            ("memory: ANY knower is world fact, not awareness", WorldFact),
            ("memory: specific character knowledge and actor filter", SpecificKnowledge),
            ("memory: count boundaries inclusive", CountBounds),
            ("memory: absence (count zero) is supported", MemoryAbsence),
            ("conditions: nested AND/OR/NOT", NestedConditions),
            ("conditions: NOT passes only when inner fails", NotSemantics),
            ("conditions: fails with explanatory reason", FailureReason),
            ("conditions: initiator-to-recipient directional status", ForwardStatus),
            ("conditions: recipient-to-initiator directional status", ReverseStatus),
            ("conditions: BOTH requires both directions", BothStatuses),
            ("conditions: invalid unknown memory key rejected", InvalidMemory),
            ("conditions: invalid status direction rejected", InvalidDirection),
            ("conditions: malformed NOT rejected", InvalidNot),
            ("conditions: max lower than min rejected", InvalidRange),
            ("conditions: unknown character reference rejected", InvalidCharacter),
            ("conditions: nested tree over depth limit rejected", DepthLimit),
            ("conditions: JSON round-trip retains interpretation", JsonRoundTrip),
            ("chance: relationship and skill contributions", ChanceArithmetic),
            ("chance: no skill means no skill contribution", ChanceNoSkill),
            ("chance: clamp to 5..95", ChanceClamp),
            ("chance: invalid non-finite factors rejected", ChanceNonFinite),
            ("personality: success gain multiplier", TraitGain),
            ("personality: outcome-specific beats generic rule", TraitSpecificity),
            ("personality: stacked multipliers capped at four", TraitCap),
            ("personality: no trait and zero delta unchanged", TraitIdentity),
            ("variant: conditional variant beats unconditional", VariantConditional),
            ("variant: pair scope beats high priority generic", VariantSpecificity),
            ("variant: failed condition returns generic fallback", VariantFallback),
            ("variant: clause bounds use AND", VariantBounds),
            ("choice: repeated callback after commit cannot re-save", ChoiceOnce),
            ("choice: save failure reuses prepared result", ChoiceRetry),
            ("choice: failed save cannot switch option", ChoiceCannotSwitch),
            ("choice: nested submission is rejected", ChoiceReentrancy),
            ("choice: advancing allows legitimate next execution", ChoiceNextRound),
            ("completion: finish is idempotent", CompletionOnce),
            ("completion: failed write allows retry", CompletionRetry),
            ("completion: no completion until finished", CompletionNotOnLaunch)
        };
        int passed = 0;
        foreach (var (name, test) in tests)
        {
            try { test(); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); }
        }
        Console.WriteLine($"{passed}/{tests.Length} regression tests passed.");
        return passed == tests.Length ? 0 : 1;
    }

    private static MemoryConditionNode Mem(string key = "DEFEAT", string actor = "ANY",
        string affected = "ANY", string knower = "ANY", int min = 1, int? max = null) =>
        new() { Kind = "MEMORY", MemoryKey = key, ActorRef = actor, AffectedRef = affected,
            KnowerRef = knower, MinimumCount = min, MaximumCount = max };
    private static MemoryConditionNode Status(string direction, string key = "ALLY") =>
        new() { Kind = "STATUS", StatusDirection = direction, StatusKey = key };
    private static MemoryConditionNode Op(string kind, params MemoryConditionNode[] children) =>
        new() { Kind = kind, Children = children.ToList() };

    private sealed record Incident(string Key, string Actor, string[] Affected, string[] Knowers);
    private static readonly Incident[] Incidents =
    {
        new("DEFEAT", "LAW", new[] {"YOURI"}, new[] {"LAW", "YOURI"}),
        new("DEFEAT", "THORN", new[] {"NOBUNOGI"}, new[] {"THORN", "NOBUNOGI", "TINA"}),
        new("BETRAYAL", "LAW", new[] {"YOURI"}, new[] {"LAW", "YOURI"})
    };
    private static readonly Dictionary<(string From, string To), string> CurrentStatuses = new()
    {
        [("LAW", "YOURI")] = "ALLY", [("YOURI", "LAW")] = "ENEMY"
    };
    private static int Count(string key, string? actor, string? affected, string? knower) =>
        Incidents.Count(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase) &&
            (actor is null || string.Equals(x.Actor, actor, StringComparison.OrdinalIgnoreCase)) &&
            (affected is null || x.Affected.Contains(affected, StringComparer.OrdinalIgnoreCase)) &&
            (knower is null || x.Knowers.Contains(knower, StringComparer.OrdinalIgnoreCase)));
    private static string Current(string from, string to) =>
        CurrentStatuses.TryGetValue((from, to), out string? status) ? status : "NEUTRAL";
    private static bool Eval(MemoryConditionNode node, out string reason) =>
        MemoryConditionRules.Evaluate(node, Law, Youri, Count, Current, out reason);
    private static bool Eval(MemoryConditionNode node) => Eval(node, out _);
    private static void Validate(MemoryConditionNode node) =>
        MemoryConditionRules.Validate(node, MemoryKeys.Contains, CharacterKeys.Contains, StatusKeys.Contains);
    private static void Ensure(bool actual, string description) { if (!actual) throw new Exception(description); }
    private static void Eq<T>(T expected, T actual) where T : notnull =>
        Ensure(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void MemoryMatch() => Ensure(Eval(Mem(actor: "ACTOR", affected: "PARTNER", knower: "PARTNER")), "Defeat not found");
    private static void WorldFact() => Ensure(Eval(Mem(actor: "CHAR:THORN", affected: "CHAR:NOBUNOGI", knower: "ANY")), "World fact should match without awareness");
    private static void SpecificKnowledge()
    {
        Ensure(Eval(Mem(actor: "CHAR:THORN", affected: "CHAR:NOBUNOGI", knower: "CHAR:TINA")), "Tina should know");
        Ensure(!Eval(Mem(actor: "CHAR:THORN", affected: "CHAR:NOBUNOGI", knower: "ACTOR")), "Law does not know Thorn's defeat");
    }
    private static void CountBounds()
    {
        Ensure(Eval(Mem(min: 2, max: 2)), "Two defeats should qualify");
        Ensure(!Eval(Mem(min: 3)), "Three defeats incorrectly qualified");
        Ensure(!Eval(Mem(min: 0, max: 1)), "Max should exclude two defeats");
    }
    private static void MemoryAbsence() => Ensure(Eval(Mem("REWARDED", min: 0, max: 0)), "No REWARDED memory exists");
    private static void NestedConditions()
    {
        var tree = Op("AND", Mem(actor: "ACTOR", affected: "PARTNER"),
            Op("OR", Status("ACTOR_TO_PARTNER"), Mem("REWARDED")),
            Op("NOT", Mem("REWARDED")));
        Validate(tree);
        Ensure(Eval(tree), "Nested AND/OR/NOT should pass");
    }
    private static void NotSemantics()
    {
        Ensure(!Eval(Op("NOT", Mem("DEFEAT"))), "NOT existing memory should fail");
        Ensure(Eval(Op("NOT", Mem("REWARDED"))), "NOT missing memory should pass");
    }
    private static void FailureReason()
    {
        Ensure(!Eval(Op("OR", Mem("REWARDED"), Status("PARTNER_TO_ACTOR")), out var reason), "Expected failed OR");
        Ensure(reason.Contains("None of the alternatives", StringComparison.Ordinal), "No useful reason");
    }
    private static void ForwardStatus() => Ensure(Eval(Status("ACTOR_TO_PARTNER")), "Law->Youri should be Ally");
    private static void ReverseStatus() => Ensure(Eval(Status("PARTNER_TO_ACTOR", "ENEMY")), "Youri->Law should be Enemy");
    private static void BothStatuses() => Ensure(!Eval(Status("BOTH")), "Both-direction Ally must fail");
    private static void InvalidMemory() => Throws<InvalidOperationException>(() => Validate(Mem("UNKNOWN")));
    private static void InvalidDirection() => Throws<InvalidOperationException>(() => Validate(Status("SIDEWAYS")));
    private static void InvalidNot() => Throws<InvalidOperationException>(() => Validate(Op("NOT", Mem(), Mem())));
    private static void InvalidRange() => Throws<InvalidOperationException>(() => Validate(Mem(min: 2, max: 1)));
    private static void InvalidCharacter() => Throws<InvalidOperationException>(() => Validate(Mem(knower: "CHAR:UNKNOWN")));
    private static void DepthLimit()
    {
        MemoryConditionNode root = Mem();
        for (int i = 0; i < 12; i++) root = Op("NOT", root);
        Throws<InvalidOperationException>(() => Validate(root));
    }
    private static void JsonRoundTrip()
    {
        var original = Op("AND", Mem(actor: "ACTOR", affected: "PARTNER"), Status("PARTNER_TO_ACTOR", "ENEMY"));
        var restored = MemoryConditionRules.Read(MemoryConditionRules.Write(original));
        Validate(restored);
        Ensure(Eval(restored) && MemoryConditionRules.Describe(restored.Children[0]).Contains("Recipient", StringComparison.Ordinal),
            "JSON should preserve semantics and readable terminology");
    }

    private static ChanceResult Chance(int baseChance = 50, int? actor = 70, int? defender = 60,
        IEnumerable<ChanceFactor>? profile = null, IEnumerable<ChanceFactor>? choice = null) =>
        ChanceFormula.Calculate(baseChance, actor, defender, 0.5, 0.5,
            profile ?? Array.Empty<ChanceFactor>(), choice ?? Array.Empty<ChanceFactor>(),
            (_, attribute) => attribute == "TRUST" ? 40 : 0);
    private static void ChanceArithmetic()
    {
        var result = Chance(profile: new[] {new ChanceFactor("A_TO_B", "TRUST", 0.25)});
        Eq(65, result.FinalChance); // base 50 + trust 10 + actor 10 - defender 5
        Eq(10d, result.RelationshipPoints);
    }
    private static void ChanceNoSkill()
    {
        var result = Chance(actor: null, defender: null);
        Eq(50, result.FinalChance);
        Eq(0d, result.ActorPoints);
        Eq(0d, result.DefenderPoints);
    }
    private static void ChanceClamp()
    {
        Eq(95, Chance(baseChance: 200).FinalChance);
        Eq(5, Chance(baseChance: -200).FinalChance);
    }
    private static void ChanceNonFinite() => Throws<InvalidOperationException>(() =>
        Chance(profile: new[] {new ChanceFactor("A", "TRUST", double.NaN)}));

    private static PersonalityEffectRules Traits(params PersonalityModifierRule[] modifiers) =>
        new(new[] { (CharacterId: "YOURI", TraitKey: "PROUD"), (CharacterId: "YOURI", TraitKey: "BRAVE") }, modifiers);
    private static void TraitGain()
    {
        var rules = Traits(new PersonalityModifierRule("PROUD", "RESPECT", "SUCCESS", "GAIN", 1.5));
        Eq(15, rules.Adjust("YOURI", "RESPECT", 10, true).AdjustedDelta);
        Eq(10, rules.Adjust("YOURI", "RESPECT", 10, false).AdjustedDelta);
    }
    private static void TraitSpecificity()
    {
        var rules = Traits(new PersonalityModifierRule("PROUD", "RESPECT", "BOTH", "GAIN", 2),
            new PersonalityModifierRule("PROUD", "RESPECT", "SUCCESS", "GAIN", 3));
        Eq(30, rules.Adjust("YOURI", "RESPECT", 10, true).AdjustedDelta);
    }
    private static void TraitCap()
    {
        var rules = Traits(new PersonalityModifierRule("PROUD", "FEAR", "BOTH", "LOSS", 3),
            new PersonalityModifierRule("BRAVE", "FEAR", "BOTH", "LOSS", 3));
        Eq(-40, rules.Adjust("YOURI", "FEAR", -10, false).AdjustedDelta);
    }
    private static void TraitIdentity()
    {
        Eq(8, Traits().Adjust("LAW", "RESPECT", 8, true).AdjustedDelta);
        Eq(0, Traits().Adjust("YOURI", "FEAR", 0, true).AdjustedDelta);
    }

    private static DialogueCandidate Phrase(int id, int priority, bool conditional = false,
        string? speaker = null, string? other = null, params DialogueClause[] clauses) =>
        new(id, speaker, other, null, priority, "Phrase " + id, "", conditional, clauses);
    private static Dictionary<string, int> Stats(int trust) =>
        new() { [ConditionalDialogueRules.StatKey("ACTOR_TO_PARTNER", "TRUST")] = trust };
    private static readonly DialogueClause TrustAtLeast50 = new("ACTOR_TO_PARTNER", "TRUST", 50, null);
    private static void VariantConditional()
    {
        var pick = ConditionalDialogueRules.Resolve(new[] {Phrase(1, 100), Phrase(2, 1, true, clauses: new[]{TrustAtLeast50})}, Law, Youri, null, Stats(60));
        Eq(2, pick.Candidate!.Id);
    }
    private static void VariantSpecificity()
    {
        var pick = ConditionalDialogueRules.Resolve(new[] { Phrase(1, 100), Phrase(2, 1, speaker: Law, other: Youri) }, Law, Youri, null, Stats(0));
        Eq(2, pick.Candidate!.Id);
    }
    private static void VariantFallback()
    {
        var pick = ConditionalDialogueRules.Resolve(new[] {Phrase(1, 1, true, clauses: new[]{TrustAtLeast50}), Phrase(2, 0)}, Law, Youri, null, Stats(40));
        Eq(2, pick.Candidate!.Id);
    }
    private static void VariantBounds()
    {
        var pick = ConditionalDialogueRules.Resolve(new[] {Phrase(1, 1, true, clauses: new[]{TrustAtLeast50,
            new DialogueClause("ACTOR_TO_PARTNER", "TRUST", null, 60)})}, Law, Youri, null, Stats(70));
        Ensure(pick.Candidate is null, "All clauses must pass");
    }

    private static void ChoiceOnce()
    {
        var guard = new ChoiceResolutionGuard<int, string>(); int persisted = 0;
        var first = guard.Execute("START", "INTIMIDATE", () => 77, n => { persisted++; return $"roll={n}"; });
        var duplicate = guard.Execute("START", "INTIMIDATE", () => 10, n => { persisted++; return $"roll={n}"; });
        Eq("roll=77", first); Eq(first, duplicate); Eq(1, persisted);
    }
    private static void ChoiceRetry()
    {
        var guard = new ChoiceResolutionGuard<int, string>(); int prepared = 0, attempts = 0;
        Throws<IOException>(() => guard.Execute("A", "B", () => { prepared++; return 72; },
            n => { attempts++; throw new IOException($"failure at {n}"); }));
        Eq("B", guard.PendingOptionKey!);
        var value = guard.Execute("A", "B", () => { prepared++; return 5; }, n => { attempts++; return $"roll={n}"; });
        Eq("roll=72", value); Eq(1, prepared); Eq(2, attempts);
    }
    private static void ChoiceCannotSwitch()
    {
        var guard = new ChoiceResolutionGuard<int, string>();
        Throws<IOException>(() => guard.Execute("A", "B", () => 1, n => throw new IOException()));
        Throws<InvalidOperationException>(() => guard.Execute("A", "C", () => 2, n => "wrong"));
    }
    private static void ChoiceReentrancy()
    {
        var guard = new ChoiceResolutionGuard<int, string>();
        guard.Execute("A", "B", () => 1, n =>
        {
            Throws<InvalidOperationException>(() => guard.Execute("A", "B", () => 2, _ => "nested"));
            return "ok";
        });
        Ensure(guard.HasCommittedChoice, "Should have committed");
    }
    private static void ChoiceNextRound()
    {
        var guard = new ChoiceResolutionGuard<int, string>(); int persisted = 0;
        guard.Execute("A", "B", () => 1, n => { persisted++; return "first"; });
        guard.Clear();
        guard.Execute("A", "B", () => 1, n => { persisted++; return "second"; });
        Eq(2, persisted);
    }
    private static void CompletionOnce()
    {
        var guard = new SpecialEncounterCompletionGuard(); int persisted = 0;
        Ensure(guard.Finish(() => persisted++), "first finish expected");
        Ensure(!guard.Finish(() => persisted++), "second finish should be suppressed");
        Eq(1, persisted);
    }
    private static void CompletionRetry()
    {
        var guard = new SpecialEncounterCompletionGuard(); int attempts = 0;
        Throws<IOException>(() => guard.Finish(() => { attempts++; throw new IOException(); }));
        Ensure(!guard.HasFinished, "failed save should not finish");
        guard.Finish(() => attempts++);
        Eq(2, attempts); Ensure(guard.HasFinished, "retry should finish");
    }
    private static void CompletionNotOnLaunch() => Ensure(!new SpecialEncounterCompletionGuard().HasFinished, "Launch alone shouldn't complete");
}
