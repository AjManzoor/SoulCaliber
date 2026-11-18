using SoulCaliber.Shared;

int passed = 0;
void Check(string label, bool result)
{
    if (!result) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
    passed++;
}
void Reject(string label, Action operation)
{
    try { operation(); }
    catch (InvalidOperationException)
    {
        Check(label, true);
        return;
    }
    throw new Exception("FAIL: expected InvalidOperationException: " + label);
}

string[] roster = ["LAW", "YOURI", "ARK", "THORN", "MIZURI", "NOBUNOGI", "TINA", "ARK"];
var characters = roster.ToHashSet(StringComparer.OrdinalIgnoreCase);
var statuses = new[] { "ALLY", "ENEMY", "LOVER", "NEUTRAL", "RIVAL" }.ToHashSet();
var memoryTypes = new[] { "BETRAYAL" }.ToHashSet();
var relationships = new Dictionary<(string From, string To), string>
{
    [("LAW", "ARK")] = "ALLY", [("LAW", "THORN")] = "ALLY",
    [("LAW", "NOBUNOGI")] = "ALLY", [("LAW", "LAW")] = "ALLY",
    [("LAW", "YOURI")] = "LOVER", [("LAW", "MIZURI")] = "LOVER",
    [("YOURI", "LAW")] = "LOVER", [("ARK", "LAW")] = "ALLY",
    [("NOBUNOGI", "LAW")] = "ALLY", [("YOURI", "TINA")] = "RIVAL"
};
string Status(string from, string to) => relationships.TryGetValue((from, to), out var status) ? status : "NEUTRAL";
int MemoryCount(string key, string? actor, string? affected, string? knower) => 0;
void Validate(MemoryConditionNode node) => MemoryConditionRules.Validate(node, memoryTypes.Contains,
    characters.Contains, statuses.Contains);
bool Evaluate(MemoryConditionNode tree, out string explanation) =>
    MemoryConditionRules.Evaluate(tree, "LAW", "YOURI", MemoryCount, Status, out explanation, roster);
MemoryConditionNode Count(string key, int min, int? max = null, string owner = "ACTOR",
    string direction = "OUTGOING", bool excludeOther = false) => new()
    {
        Kind = "RELATIONSHIP_COUNT", StatusKey = key, MinimumCount = min,
        MaximumCount = max, CountOwnerRef = owner,
        CountDirection = direction, CountExcludeOtherParticipant = excludeOther
    };

var threeAllies = Count("ALLY", 3);
Validate(threeAllies);
Check("Three allies qualify", Evaluate(threeAllies, out _));
Check("Self and duplicate character are not counted", !Evaluate(Count("ALLY", 4), out string fail)
    && fail.Contains("found 3"));
Check("Exactly three passes", Evaluate(Count("ALLY", 3, 3), out _));
Check("At most two fails", !Evaluate(Count("ALLY", 0, 2), out _));
Check("Zero rivals passes", Evaluate(Count("RIVAL", 0, 0), out _));
Check("Status updated by gameplay is used", SetAndTest("LAW", "ARK", "ENEMY", () =>
    !Evaluate(threeAllies, out _)));
Check("Reverting status brings condition back", Evaluate(threeAllies, out _));

// 'Other lovers' excludes the current recipient but still counts other lovers.
var twoLovers = Count("LOVER", 2);
var otherLover = Count("LOVER", 1, owner: "ACTOR", excludeOther: true);
Check("Initiator has two Lovers including Recipient", Evaluate(twoLovers, out _));
Check("Initiator has another Lover excluding Recipient", Evaluate(otherLover, out _));
Check("Other-Lover count is exactly one", Evaluate(Count("LOVER", 1, 1,
    excludeOther: true), out _));
Check("Excluding Recipient prevents counting both Lovers", !Evaluate(Count("LOVER", 2,
    excludeOther: true), out _));
Check("Recipient has one Lover (Initiator)", Evaluate(Count("LOVER", 1,
    owner: "PARTNER"), out _));
Check("Recipient has no other Lover excluding Initiator", Evaluate(Count("LOVER", 0, 0,
    owner: "PARTNER", excludeOther: true), out _));

Check("Incoming count uses reverse direction", Evaluate(Count("ALLY", 2, 2,
    direction: "INCOMING"), out _));
Check("Outgoing count does not use incoming relationships", !Evaluate(Count("ALLY", 2,
    owner: "PARTNER"), out _));
Check("Recipient's Rival is counted", Evaluate(Count("RIVAL", 1, owner: "PARTNER"), out _));

var conditions = new MemoryConditionNode
{
    Kind = "AND", Children = [
        otherLover,
        new MemoryConditionNode { Kind = "STATUS", StatusKey = "LOVER", StatusDirection = "PARTNER_TO_ACTOR" }
    ]
};
Validate(conditions);
Check("AND relationship count + legacy status", Evaluate(conditions, out _));
var none = new MemoryConditionNode { Kind = "NOT", Children = [threeAllies] };
Validate(none);
Check("NOT reverses count result", !Evaluate(none, out _));
Check("OR supports count conditions", Evaluate(new MemoryConditionNode { Kind = "OR", Children = [
    Count("ALLY", 10), otherLover ] }, out _));
Check("Count can be serialized/deserialized", Evaluate(MemoryConditionRules.Read(
    MemoryConditionRules.Write(conditions)), out _));
Check("Count appears in condition diagnostics", MemoryConditionRules.Describe(otherLover).Contains("exclude other participant"));
Check("Condition tree locates relationship count", MemoryConditionRules.ContainsKind(conditions, "RELATIONSHIP_COUNT"));

Reject("Unknown status rejected", () => Validate(Count("UNKNOWN", 1)));
Reject("Invalid negative minimum rejected", () => Validate(Count("ALLY", -1)));
Reject("Invalid maximum rejected", () => Validate(Count("ALLY", 3, 2)));
Reject("Invalid owner rejected", () => Validate(Count("ALLY", 1, owner: "CHAR:ARK")));
Reject("Invalid direction rejected", () => Validate(Count("ALLY", 1, direction: "BOTH")));
Reject("Leaf children rejected", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP_COUNT", StatusKey = "ALLY", Children = [Count("ALLY", 1)]
}));
Reject("Missing roster fails closed", () => MemoryConditionRules.Evaluate(otherLover,
    "LAW", "YOURI", MemoryCount, Status, out _));
Reject("NOT(count) cannot pass with missing roster", () => MemoryConditionRules.Evaluate(
    new MemoryConditionNode { Kind = "NOT", Children = [Count("ALLY", 3)] },
    "LAW", "YOURI", MemoryCount, Status, out _));
Reject("Incomplete roster fails closed even for maximum zero", () => MemoryConditionRules.Evaluate(
    Count("ALLY", 0, 0), "LAW", "YOURI", MemoryCount, Status, out _,
    new[] { "LAW", "ARK" }));

var legacy = MemoryConditionRules.Read("{\"Kind\":\"STATUS\",\"StatusKey\":\"LOVER\",\"StatusDirection\":\"PARTNER_TO_ACTOR\"}");
Validate(legacy);
Check("Legacy status JSON stays compatible", Evaluate(legacy, out _));
var previousThirdParty = new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "ACTOR", RelationshipToRef = "CHAR:THORN", StatusKey = "ALLY"
};
Validate(previousThirdParty);
Check("Phase 3A named relationship still evaluates", Evaluate(previousThirdParty, out _));

Console.WriteLine($"All {passed} relationship count cases passed.");

bool SetAndTest(string from, string to, string tempStatus, Func<bool> test)
{
    var key = (from, to);
    string original = relationships[key];
    try { relationships[key] = tempStatus; return test(); }
    finally { relationships[key] = original; }
}
