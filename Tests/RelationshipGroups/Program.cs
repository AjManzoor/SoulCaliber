using SoulCaliber.Shared;

int passed = 0;
void Check(string description, bool value)
{
    if (!value) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
    passed++;
}
void Reject(string description, Action action)
{
    try { action(); }
    catch (InvalidOperationException) { Check(description, true); return; }
    throw new Exception("FAIL: expected InvalidOperationException: " + description);
}

string[] roster = ["LAW", "YOURI", "THORN", "ARK", "MIZURI", "TINA", "ark"];
var characters = roster.ToHashSet(StringComparer.OrdinalIgnoreCase);
var statuses = new[] { "ALLY", "ENEMY", "NEUTRAL", "LOVER", "RIVAL" }.ToHashSet();
var memories = new[] { "BETRAYAL" }.ToHashSet();
var relationships = new Dictionary<(string, string), string>
{
    [("LAW", "THORN")] = "ENEMY",
    [("LAW", "ARK")] = "ENEMY",
    [("LAW", "MIZURI")] = "ENEMY",
    [("YOURI", "THORN")] = "ALLY",
    [("YOURI", "ARK")] = "ALLY",
    [("YOURI", "TINA")] = "ALLY",
    [("YOURI", "LAW")] = "ALLY",
    [("LAW", "YOURI")] = "ENEMY"
};
string Status(string from, string to) => relationships.TryGetValue((from, to), out var status) ? status : "NEUTRAL";
int MemoryCount(string key, string? actor, string? affected, string? knower) => 0;
void Validate(MemoryConditionNode node) => MemoryConditionRules.Validate(node, memories.Contains,
    characters.Contains, statuses.Contains);
bool Eval(MemoryConditionNode node, out string reason) => MemoryConditionRules.Evaluate(node,
    "LAW", "YOURI", MemoryCount, Status, out reason, roster);
MemoryConditionNode Group(int min = 1, int? max = null,
    string sourceOwner = "ACTOR", string sourceDirection = "OUTGOING", string sourceStatus = "ENEMY",
    string matchOwner = "PARTNER", string matchDirection = "OUTGOING", string matchStatus = "ALLY") => new()
    {
        Kind = "RELATIONSHIP_GROUP", MinimumCount = min, MaximumCount = max,
        GroupSourceOwnerRef = sourceOwner, GroupSourceDirection = sourceDirection,
        GroupSourceStatusKey = sourceStatus,
        GroupMatchOwnerRef = matchOwner, GroupMatchDirection = matchDirection,
        GroupMatchStatusKey = matchStatus
    };

var common = Group();
Validate(common);
Check("Friends with my enemies: same third character matches", Eval(common, out _));
Check("Two distinct shared targets qualify", Eval(Group(2, 2), out _));
Check("Duplicate roster IDs do not increase count", !Eval(Group(3, 3), out string insufficient)
    && insufficient.Contains("found 2"));
Check("Maximum 1 fails with 2 matches", !Eval(Group(0, 1), out _));
Check("Minimum 0 and max 2 accepts two", Eval(Group(0, 2), out _));
Check("Not ALL of my enemies need be allied to Recipient", Eval(common, out _));

// The relationships below each match a different X: they must not combine.
relationships[("YOURI", "THORN")] = "NEUTRAL";
relationships[("YOURI", "ARK")] = "NEUTRAL";
relationships[("YOURI", "TINA")] = "ALLY";
Check("Different third-party targets cannot be joined", !Eval(common, out _));
Check("Zero-match rule succeeds with maximum zero", Eval(Group(0, 0), out _));
Check("NOT of a failed group succeeds", Eval(new MemoryConditionNode
{
    Kind = "NOT", Children = [common]
}, out _));
relationships[("YOURI", "THORN")] = "ALLY";
relationships[("YOURI", "ARK")] = "ALLY";
Check("Savegame status changes immediately affect group", Eval(common, out _));

relationships[("THORN", "LAW")] = "ENEMY";
relationships[("ARK", "LAW")] = "ENEMY";
relationships[("THORN", "YOURI")] = "ALLY";
relationships[("ARK", "YOURI")] = "ALLY";
Check("Incoming-to-source and incoming-to-match directions", Eval(Group(2, 2,
    sourceDirection: "INCOMING", matchDirection: "INCOMING"), out _));
Check("Mixed incoming/outgoing directions", Eval(Group(2, 2,
    sourceDirection: "INCOMING", matchDirection: "OUTGOING"), out _));
Check("Owner refs may swap participants", Eval(Group(2, 2,
    sourceOwner: "PARTNER", sourceStatus: "ALLY", matchOwner: "ACTOR", matchStatus: "ENEMY"), out _));

// The two conversation participants must not be counted as an X.
relationships[("LAW", "YOURI")] = "ENEMY";
relationships[("YOURI", "LAW")] = "ALLY";
Check("Participants are never eligible third-party X", !Eval(Group(3), out _));

var and = new MemoryConditionNode
{
    Kind = "AND", Children = [common,
        new MemoryConditionNode { Kind = "RELATIONSHIP_COUNT", StatusKey = "ENEMY", MinimumCount = 3 }]
};
Validate(and);
Check("Group and count compose with AND", Eval(and, out _));
Check("OR with group and false condition", Eval(new MemoryConditionNode
{
    Kind = "OR", Children = [Group(4), common]
}, out _));
Check("Group tree serialization round-trips", Eval(MemoryConditionRules.Read(
    MemoryConditionRules.Write(and)), out _));
Check("Group is discoverable in condition trees", MemoryConditionRules.ContainsKind(and, "RELATIONSHIP_GROUP"));
Check("Diagnostic describes shared third-party target", MemoryConditionRules.Describe(common).Contains("Same third character X"));

Reject("Missing roster fails closed", () => MemoryConditionRules.Evaluate(common, "LAW", "YOURI",
    MemoryCount, Status, out _));
Reject("NOT(group) with missing roster fails closed", () => MemoryConditionRules.Evaluate(
    new MemoryConditionNode { Kind = "NOT", Children = [common] }, "LAW", "YOURI",
    MemoryCount, Status, out _));
Reject("Roster missing Recipient fails closed", () => MemoryConditionRules.Evaluate(common, "LAW", "YOURI",
    MemoryCount, Status, out _, ["LAW", "ARK", "THORN"]));
Reject("Unknown source status rejected", () => Validate(Group(sourceStatus: "UNKNOWN")));
Reject("Unknown matching status rejected", () => Validate(Group(matchStatus: "UNKNOWN")));
Reject("Invalid source owner rejected", () => Validate(Group(sourceOwner: "CHAR:THORN")));
Reject("Invalid matching owner rejected", () => Validate(Group(matchOwner: "CHAR:THORN")));
Reject("Invalid source direction rejected", () => Validate(Group(sourceDirection: "BOTH")));
Reject("Invalid match direction rejected", () => Validate(Group(matchDirection: "SIDEWAYS")));
Reject("Negative minimum rejected", () => Validate(Group(-1)));
Reject("Maximum below minimum rejected", () => Validate(Group(3, 2)));
Reject("Group node cannot contain children", () => Validate(new MemoryConditionNode
{
    Kind = "RELATIONSHIP_GROUP", GroupSourceStatusKey = "ENEMY", GroupMatchStatusKey = "ALLY",
    Children = [Group()]
}));

var legacy = MemoryConditionRules.Read("{\"Kind\":\"STATUS\",\"StatusKey\":\"ALLY\",\"StatusDirection\":\"PARTNER_TO_ACTOR\"}");
Validate(legacy);
Check("Older status rules remain valid", Eval(legacy, out _));
var oldThird = new MemoryConditionNode
{
    Kind = "RELATIONSHIP", RelationshipFromRef = "ACTOR", RelationshipToRef = "CHAR:THORN", StatusKey = "ENEMY"
};
Validate(oldThird);
Check("Third-party named relationship is unchanged", Eval(oldThird, out _));

Console.WriteLine($"All {passed} relationship group tests passed.");
