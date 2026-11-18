using SoulCaliber.Shared;

var knownMemories = new HashSet<string>(["BETRAYAL"], StringComparer.OrdinalIgnoreCase);
var knownStatuses = new HashSet<string>(["ALLY", "LOVER", "NEUTRAL"], StringComparer.OrdinalIgnoreCase);
var knownTraits = new HashSet<string>(["MONOGAMOUS", "JEALOUS"], StringComparer.OrdinalIgnoreCase);
var characters = new[] { "LAW", "YOURI", "ARK", "THORN" };
var traits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "YOURI|MONOGAMOUS", "LAW|JEALOUS" };
var relationships = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["LAW|YOURI"] = "LOVER",
    ["LAW|ARK"] = "LOVER",
    ["YOURI|LAW"] = "LOVER"
};
int passed = 0;
void Check(string name, bool ok)
{
    if (!ok) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}
void Reject(string name, Action action)
{
    try { action(); }
    catch (InvalidOperationException) { Check(name, true); return; }
    throw new Exception("FAILED (expected invalid operation): " + name);
}
string Status(string a, string b) => relationships.GetValueOrDefault(a + "|" + b, "NEUTRAL");
int MemoryCount(string key, string? actor, string? affected, string? knower) => 0;
bool HasTrait(string character, string trait) => traits.Contains(character + "|" + trait);
void Validate(MemoryConditionNode root) => MemoryConditionRules.Validate(root, knownMemories.Contains,
    c => characters.Contains(c, StringComparer.OrdinalIgnoreCase), knownStatuses.Contains, knownTraits.Contains);
bool Eval(MemoryConditionNode root, out string reason) => MemoryConditionRules.Evaluate(root,
    "LAW", "YOURI", MemoryCount, Status, out reason, characters, HasTrait);
MemoryConditionNode Trait(string character = "PARTNER", string key = "MONOGAMOUS") =>
    new() { Kind = "TRAIT", TraitCharacterRef = character, TraitKey = key };
MemoryConditionNode MonogamyEncounter() => new()
{
    Kind = "AND",
    Children =
    [
        Trait(),
        new MemoryConditionNode { Kind = "STATUS", StatusDirection = "PARTNER_TO_ACTOR", StatusKey = "LOVER" },
        new MemoryConditionNode { Kind = "RELATIONSHIP_COUNT", CountOwnerRef = "ACTOR", CountDirection = "OUTGOING",
            CountExcludeOtherParticipant = true, StatusKey = "LOVER", MinimumCount = 1 }
    ]
};

var trait = Trait();
Validate(trait);
Check("Recipient Monogamous passes", Eval(trait, out _));
Check("Initiator not Monogamous", !Eval(Trait("ACTOR"), out var explanation)
    && explanation.Contains("not assigned"));
Check("Initiator Jealous passes", Eval(Trait("ACTOR", "JEALOUS"), out _));
var monogamy = MonogamyEncounter();
Validate(monogamy);
Check("Monogamy + Lover + another Lover passes", Eval(monogamy, out _));
relationships.Remove("LAW|ARK");
Check("Another Lover count is required", !Eval(monogamy, out _));
relationships["LAW|ARK"] = "LOVER";
traits.Remove("YOURI|MONOGAMOUS");
Check("Monogamous assignment required", !Eval(monogamy, out _));
Check("NOT works on missing trait", Eval(new MemoryConditionNode { Kind = "NOT", Children = [trait] }, out _));
traits.Add("YOURI|MONOGAMOUS");
Check("NOT fails if Recipient is monogamous", !Eval(new MemoryConditionNode { Kind = "NOT", Children = [trait] }, out _));
Check("OR with another valid leaf passes", Eval(new MemoryConditionNode
    { Kind = "OR", Children = [Trait("ACTOR"), Trait("ACTOR", "JEALOUS")] }, out _));
var json = MemoryConditionRules.Write(monogamy);
Check("JSON round-trip", Eval(MemoryConditionRules.Read(json), out _));
Check("Summary uses Initiator/Recipient", MemoryConditionRules.Describe(trait).Contains("Recipient has trait MONOGAMOUS"));
Check("Leaf discoverable", MemoryConditionRules.ContainsKind(monogamy, "TRAIT"));
Check("Old status leaf remains valid", Eval(new MemoryConditionNode
    { Kind = "STATUS", StatusDirection = "ACTOR_TO_PARTNER", StatusKey = "LOVER" }, out _));
Reject("Inactive or unknown trait refused", () => Validate(Trait(key: "UNKNOWN")));
Reject("Blank trait refused", () => Validate(Trait(key: "")));
Reject("Unknown subject refused", () => Validate(Trait("ANY")));
Reject("Named third character not supported in 3D", () => Validate(Trait("CHAR:ARK")));
Reject("Leaf cannot have children", () => Validate(new MemoryConditionNode
    { Kind = "TRAIT", TraitKey = "MONOGAMOUS", Children = [Trait()] }));
Reject("Validation needs real trait catalogue", () => MemoryConditionRules.Validate(trait,
    knownMemories.Contains, _ => true, knownStatuses.Contains));
Reject("Evaluation needs actual trait assignments", () => MemoryConditionRules.Evaluate(trait,
    "LAW", "YOURI", MemoryCount, Status, out _, characters));
Reject("NOT cannot bypass missing assignments", () => MemoryConditionRules.Evaluate(
    new MemoryConditionNode { Kind = "NOT", Children = [trait] },
    "LAW", "YOURI", MemoryCount, Status, out _, characters));
Console.WriteLine($"All {passed} character-trait regression cases passed.");
