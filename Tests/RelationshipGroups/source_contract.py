"""Fast, dependency-free source contract tests (run from project root)."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
def read(path): return (root/path).read_text(encoding='utf-8-sig')
rules = read('Game/Dialogue/MemoryConditionRules.cs')
assert rules == read('Editor/Studio/MemoryConditionRules.cs'), 'Shared evaluator diverged'
for term in ['case "RELATIONSHIP_GROUP":', 'GroupSourceOwnerRef', 'GroupMatchOwnerRef',
             'GroupSourceDirection', 'GroupMatchDirection',
             'GroupSourceStatusKey', 'GroupMatchStatusKey',
             'Distinct(StringComparer.OrdinalIgnoreCase)',
             'Relationship group check requires the published character roster.',
             'string.Equals(x, actorId, StringComparison.OrdinalIgnoreCase)',
             'string.Equals(x, partnerId, StringComparison.OrdinalIgnoreCase)']:
    assert term in rules, term
for path in ('Game/Dialogue/DialogueEngine.cs','Game/Dialogue/SpecialEncounterSelector.cs',
             'Game/Dialogue/ConversationAvailabilitySelector.cs','Editor/Studio/SimulationMemoryRuntime.cs'):
    content=read(path)
    assert 'MemoryConditionRules.Evaluate(' in content, path
    assert ('_data.CharacterIds' in content or 'characterKeys))' in content
            or 'characters))' in content or 'data.Characters.Select(c => c.Id)' in content), path+' missing roster'
assert 'ContainsKind(option.MemoryRequirements, "RELATIONSHIP_GROUP")' in read('Game/Dialogue/DialogueEngine.cs')
editor=read('Editor/Studio/MemoryRequirementsWindow.cs')
for term in ['Add group matching', 'NewRelationshipGroup()', 'GroupSourceOwnerRef',
             'GroupMatchOwnerRef', '_groupMinimum', '_groupMaximum', 'RELATIONSHIP_GROUP']:
    assert term in editor, term
print('PASS: shared evaluator parity; all four callers supply roster; Studio editor and fail-closed guard wired')
