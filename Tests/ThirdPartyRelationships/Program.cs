using SoulCaliber.Shared;

int passed = 0;
void Check(string label, bool condition)
{
    if (!condition) throw new Exception("FAIL " + label);
    Console.WriteLine("PASS " + label);
    passed++;
}
void Throws(string label, Action action)
{
    try { action(); }
    catch (InvalidOperationException) { Check(label, true); return; }
    throw new Exception("FAIL (expected InvalidOperationException): " + label);
}

var characters = new[] { "LAW", "YOURI", "MIZURI", "THORN", "NOBUNOGI" }
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var statuses = new[] { "ALLY", "ENEMY", "NEUTRAL", "LOVER" }
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var memories = new[] { "BETRAYAL" }.ToHashSet(StringComparer.OrdinalIgnoreCase);
void Validate(MemoryConditionNode node) => MemoryConditionRules.Validate(node, memories.Contains,
    characters.Contains, statuses.Contains);

var current = new Dictionary<(string From, string To), string>
{
    [("YOURI", "MIZURI")] = "ALLY",
    [("MIZURI", "YOURI")] = "ENEMY",
    [("LAW", "THORN")] = "ENEMY",
    [("LAW", "MIZURI")] = "ENEMY"
};
string Status(string from, string to) => current.TryGetValue((from, to), out var status)
    ? status : "NEUTRAL";
int Count(string key, string? actor, string? affected, string? knower) => 0;
bool Evaluate(MemoryConditionNode tree, out string reason) =>
    MemoryConditionRules.Evaluate(tree, "LAW", "YOURI", Count, Status, out reason);

var toMizuri = new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "PARTNER",
    RelationshipToRef = "CHAR:MIZURI", StatusKey = "ALLY"
};
Validate(toMizuri);
Check("Recipient -> Queen Mizuri = Ally", Evaluate(toMizuri, out _));
Check("Describes named third party", MemoryConditionRules.Describe(toMizuri).Contains("MIZURI"));
var restored = MemoryConditionRules.Read(MemoryConditionRules.Write(toMizuri));
Validate(restored);
Check("Authored JSON round trips", Evaluate(restored, out _));
current[("YOURI", "MIZURI")] = "ENEMY";
Check("Current runtime status overrides earlier Ally", !Evaluate(toMizuri, out string failure) && failure.Contains("not matched"));
current[("YOURI", "MIZURI")] = "ALLY";

var reverse = new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "CHAR:MIZURI",
    RelationshipToRef = "PARTNER", StatusKey = "ENEMY"
};
Validate(reverse);
Check("Queen Mizuri -> Recipient is evaluated directionally", Evaluate(reverse, out _));
reverse.StatusKey = "ALLY";
Check("Reverse direction does not inherit Recipient -> Queen", !Evaluate(reverse, out _));

var initiator = new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "ACTOR",
    RelationshipToRef = "CHAR:THORN", StatusKey = "ENEMY"
};
Validate(initiator);
Check("Initiator -> named third party", Evaluate(initiator, out _));

var andTree = new MemoryConditionNode
{
    Kind = "AND", Children = new()
    {
        toMizuri,
        new MemoryConditionNode { Kind = "STATUS", StatusDirection = "ACTOR_TO_PARTNER", StatusKey = "NEUTRAL" }
    }
};
Validate(andTree);
Check("Works alongside legacy STATUS in AND", Evaluate(andTree, out _));
var notTree = new MemoryConditionNode { Kind = "NOT", Children = new() { reverse } };
Validate(notTree);
Check("NOT(third-party relationship) works", Evaluate(notTree, out _));
var orTree = new MemoryConditionNode { Kind = "OR", Children = new() { reverse, toMizuri } };
Validate(orTree);
Check("OR(third-party relationship) works", Evaluate(orTree, out _));

Throws("Unknown named character rejected", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "PARTNER",
    RelationshipToRef = "CHAR:MISSING", StatusKey = "ALLY"
}));
Throws("Inactive/unknown status rejected", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "PARTNER",
    RelationshipToRef = "CHAR:MIZURI", StatusKey = "RIVAL"
}));
Throws("No third party rejected", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "PARTNER",
    RelationshipToRef = "ACTOR", StatusKey = "ALLY"
}));
Throws("No participant rejected", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "CHAR:THORN",
    RelationshipToRef = "CHAR:MIZURI", StatusKey = "ALLY"
}));
Throws("ANY endpoint rejected", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "PARTNER",
    RelationshipToRef = "ANY", StatusKey = "ALLY"
}));
Throws("Leaf cannot have children", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "PARTNER",
    RelationshipToRef = "CHAR:MIZURI", StatusKey = "ALLY",
    Children = new() { new MemoryConditionNode { Kind = "STATUS", StatusKey = "ALLY" } }
}));

// Existing MEMORY-only JSON and legacy STATUS leaves retain their semantics.
var legacy = MemoryConditionRules.Read("{\"Kind\":\"STATUS\",\"StatusKey\":\"NEUTRAL\",\"StatusDirection\":\"ACTOR_TO_PARTNER\"}");
Validate(legacy);
Check("Legacy status JSON still evaluates", Evaluate(legacy, out _));
var memory = new MemoryConditionNode { Kind = "MEMORY", MemoryKey = "BETRAYAL" };
Validate(memory);
Check("Memory-only requirements still evaluate", !Evaluate(memory, out _));
Check("New leaf discoverable for fail-closed status guard", MemoryConditionRules.ContainsKind(andTree, "RELATIONSHIP"));
Console.WriteLine($"All {passed} third-party relationship cases passed.");
