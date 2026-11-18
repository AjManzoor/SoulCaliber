using SoulCaliber.Dialogue;

int passed = 0;
void Check(string label, bool assertion)
{
    if (!assertion) throw new Exception("FAIL: " + label);
    passed++;
    Console.WriteLine("PASS: " + label);
}
void MustFail(string label, Action action)
{
    try { action(); }
    catch (InvalidOperationException) { Check(label, true); return; }
    throw new Exception("FAIL: " + label);
}

// The pure selector and pair effect resolver are the SAME sources compiled into
// Godot's runtime and WPF Test Lab: this is not a second mock implementation.
string[] ids = ["LAW", "YOURI", "NOBUNOGI", "ARK", "TINA", "LAW"];
var statuses = new Dictionary<(string, string), string>
{
    [("YOURI", "NOBUNOGI")] = "ALLY",
    [("YOURI", "ARK")] = "ALLY",
    [("NOBUNOGI", "YOURI")] = "ALLY",
    [("ARK", "YOURI")] = "ENEMY"
};
string Get(string from, string to) =>
    statuses.GetValueOrDefault((from, to), "NEUTRAL");
string Character(string id) => ids.FirstOrDefault(x => x.Equals(id, StringComparison.OrdinalIgnoreCase))
    ?? throw new InvalidOperationException("Unknown character");

var group = new DynamicTargetRule("RECIPIENT", "OUTGOING", "ALLY");
IReadOnlyList<string> Eligible() => DynamicTargetRules.Find(group, "LAW", "YOURI", ids, Get);
Check("group initially shows two distinct allies", Eligible().SequenceEqual(["NOBUNOGI", "ARK"]));
Check("incoming group uses the reverse relationship",
    DynamicTargetRules.Find(new("RECIPIENT", "INCOMING", "ALLY"), "LAW", "YOURI", ids, Get)
    .SequenceEqual(["NOBUNOGI"]));
var success = new[] { new DynamicPairStatusEffect("RECIPIENT", "TARGET", "NEUTRAL", "NEUTRAL") };
var resolved = DynamicTargetRules.ResolvePairEffects(success, "LAW", "YOURI", "NOBUNOGI", Character);
Check("success resolves one chosen pair plus reverse, no group mutation", resolved.Count == 2 &&
    resolved[0] == new DynamicResolvedPairStatus("YOURI", "NOBUNOGI", "NEUTRAL") &&
    resolved[1] == new DynamicResolvedPairStatus("NOBUNOGI", "YOURI", "NEUTRAL"));
// Simulate applying the ONE prepared effect to sandbox statuses.
foreach (var change in resolved) statuses[(change.FromCharacterId, change.ToCharacterId)] = change.StatusKey;
Check("next round immediately sees changed eligibility", Eligible().SequenceEqual(["ARK"]));
Check("unselected ally stays an ally", Get("YOURI", "ARK") == "ALLY");
Check("reverse update is independent", Get("NOBUNOGI", "YOURI") == "NEUTRAL");
Check("unaffected incoming relationship stays an enemy", Get("ARK", "YOURI") == "ENEMY");

var custom = new DynamicTargetRule("RECIPIENT", "OUTGOING", "ALLY", "CUSTOM",
    ["TINA", "NOBUNOGI", "LAW", "YOURI", "MISSING", "TINA"]);
Check("custom ignores all statuses, excludes participants, duplicates and deleted IDs",
    DynamicTargetRules.Find(custom, "LAW", "YOURI", ids,
        (_, _) => throw new Exception("Custom mode must not read statuses"))
        .SequenceEqual(["TINA", "NOBUNOGI"]));
Check("empty list locks/hides choice as no eligible targets",
    DynamicTargetRules.Find(new("RECIPIENT", "OUTGOING", "ALLY", "CUSTOM", []),
        "LAW", "YOURI", ids, Get).Count == 0);
Check("empty group locks/hides choice as no eligible targets",
    DynamicTargetRules.Find(new("RECIPIENT", "OUTGOING", "LOVER"),
        "LAW", "YOURI", ids, Get).Count == 0);
Check("failure-specific effect changes only selected forward direction",
    DynamicTargetRules.ResolvePairEffects(
        [new("RECIPIENT", "TARGET", "ENEMY", null)], "LAW", "YOURI", "TINA", Character)
        .SequenceEqual([new DynamicResolvedPairStatus("YOURI", "TINA", "ENEMY")]));
Check("explicit named character is supported without a dynamic target",
    DynamicTargetRules.ResolvePairEffects(
        [new("RECIPIENT", "ARK", "ENEMY", null)], "LAW", "YOURI", null, Character)
        .SequenceEqual([new DynamicResolvedPairStatus("YOURI", "ARK", "ENEMY")]));
Check("target token formats without changing other dialogue",
    DynamicTargetRules.FormatTarget("Ask {TARGET} to leave.", "Nobunogi") == "Ask Nobunogi to leave.");
Check("previously bound target works in a later dialogue node",
    DynamicTargetRules.FormatTarget("{target} has agreed; {target} is free.", "Nobunogi") ==
    "Nobunogi has agreed; Nobunogi is free.");
Check("dialogue without target token does not require a bound selection",
    DynamicTargetRules.FormatTarget("Hello Youri.", null) == "Hello Youri.");
MustFail("unbound target token is rejected", () =>
    DynamicTargetRules.FormatTarget("Where is {target}?", null));
MustFail("missing chosen target fails before any writes", () =>
    DynamicTargetRules.ResolvePairEffects(success, "LAW", "YOURI", null, Character));
MustFail("self-relationship effects are rejected", () =>
    DynamicTargetRules.ResolvePairEffects([new("INITIATOR", "LAW", "ENEMY", null)],
        "LAW", "YOURI", null, Character));
MustFail("unknown named character is rejected", () =>
    DynamicTargetRules.ResolvePairEffects([new("INITIATOR", "UNKNOWN", "ENEMY", null)],
        "LAW", "YOURI", null, Character));
MustFail("unknown selection mode fails closed", () =>
    DynamicTargetRules.Find(new("RECIPIENT", "OUTGOING", "ALLY", "NONSENSE"),
        "LAW", "YOURI", ids, Get));
Console.WriteLine($"{passed} dynamic target simulation regression tests passed.");
