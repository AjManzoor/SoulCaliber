using SoulCaliber.Dialogue;

int passed=0;
void Check(string description, bool condition)
{
    if(!condition) throw new Exception("FAILED: "+description);
    Console.WriteLine("PASS "+description);passed++;
}
var statuses=new Dictionary<(string,string),string>
{
    [("YOURI","NOBUNOGI")]="ALLY",[("YOURI","ARK")]="ALLY",[("YOURI","TINA")]="NEUTRAL",
    [("NOBUNOGI","YOURI")]="ALLY",[("ARK","YOURI")]="ENEMY",
    [("LAW","THORN")]="ENEMY"
};
string Lookup(string from,string to)=> statuses.TryGetValue((from,to),out var v)?v:"NEUTRAL";
string[] characters=["LAW","YOURI","NOBUNOGI","ARK","TINA","LAW","THORN"];
var outgoing=new DynamicTargetRule("RECIPIENT","OUTGOING","ALLY");
var targets=DynamicTargetRules.Find(outgoing,"LAW","YOURI",characters,Lookup);
Check("Only recipient's actual allies appear",targets.SequenceEqual(["NOBUNOGI","ARK"]));
Check("No duplicate or participant entries",targets.Count==2);
var incoming=DynamicTargetRules.Find(new("RECIPIENT","INCOMING","ALLY"),"LAW","YOURI",characters,Lookup);
Check("Incoming direction is independent",incoming.SequenceEqual(["NOBUNOGI"]));
statuses[("YOURI","NOBUNOGI")]="NEUTRAL";
Check("A changed effective status immediately removes a target",
    DynamicTargetRules.Find(outgoing,"LAW","YOURI",characters,Lookup).SequenceEqual(["ARK"]));
Check("Initiator group is independently selectable",
    DynamicTargetRules.Find(new("INITIATOR","OUTGOING","ENEMY"),"LAW","YOURI",characters,Lookup)
    .SequenceEqual(["THORN"]));
Check("No match returns no choices",
    DynamicTargetRules.Find(new("RECIPIENT","OUTGOING","LOVER"),"LAW","YOURI",characters,Lookup).Count==0);
Check("Status names are case insensitive",
    DynamicTargetRules.Find(new("RECIPIENT","OUTGOING","ally"),"LAW","YOURI",characters,Lookup).Count==1);
try { DynamicTargetRules.Find(new("NONE","OUTGOING","ALLY"),"LAW","YOURI",characters,Lookup); throw new Exception("Invalid owner should fail"); }
catch(InvalidOperationException){Check("Invalid owner fails closed",true);}
// Custom selections ignore ALL relationship status checks and cannot choose the
// conversation participants, even if included in the authored character list.
var explicitList=new DynamicTargetRule("RECIPIENT","OUTGOING","ALLY","CUSTOM",
    ["TINA","NOBUNOGI","LAW","YOURI","MISSING","TINA"]);
var customTargets=DynamicTargetRules.Find(explicitList,"LAW","YOURI",characters,
    (_,_)=>throw new Exception("Custom lists must not read relationship statuses"));
Check("Custom list ignores relationship status and retains authored order",
    customTargets.SequenceEqual(["TINA","NOBUNOGI"]));
Check("Only authored targets are eligible, never participants or deleted characters",
    customTargets.Count==2);
statuses[("YOURI","TINA")]="ENEMY";
Check("Changing a custom target's status does not remove the character",
    DynamicTargetRules.Find(explicitList,"LAW","YOURI",characters,Lookup)
        .SequenceEqual(["TINA","NOBUNOGI"]));
Check("Empty custom list has no selectable targets",
    DynamicTargetRules.Find(new("RECIPIENT","OUTGOING","ENEMY","CUSTOM",[]),
        "LAW","YOURI",characters,Lookup).Count==0);
try { DynamicTargetRules.Find(new("RECIPIENT","OUTGOING","ALLY","INVALID"),"LAW","YOURI",characters,Lookup); throw new Exception("Unknown selection mode should fail"); }
catch(InvalidOperationException){Check("Invalid selection mode fails closed",true);}
Console.WriteLine($"{passed} tests passed.");
