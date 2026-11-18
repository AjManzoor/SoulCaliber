from pathlib import Path
import re
root = Path(__file__).resolve().parents[2]
repo = root / 'Game/Dialogue/MemoryConditionRules.cs'
editor = root / 'Editor/Studio/MemoryConditionRules.cs'
assert repo.read_bytes() == editor.read_bytes(), 'Studio/Godot rules diverged'
text = repo.read_text()
for fragment in ['case "TRAIT":', 'activeTraitKey is null', 'hasTrait is null', 'TraitCharacterRef', 'TraitKey']:
    assert fragment in text, fragment
checks = {
 'Game/Dialogue/DialogueEngine.cs':'_data.HasTrait',
 'Game/Dialogue/DialogueRepository.cs':'activeTraitKeys.Contains',
 'Game/Dialogue/SpecialEncounterSelector.cs':'traitLookup.Has',
 'Game/Dialogue/ConversationAvailabilitySelector.cs':'traitLookup.Has',
 'Editor/Studio/SimulationMemoryRuntime.cs':'TraitCheckOverride',
 'Editor/Studio/MemoryRequirementsWindow.cs':'Add character trait',
 'Editor/Studio/ContentPublisher.cs':'traitMap.TryGetValue',
}
for rel, fragment in checks.items():
    assert fragment in (root/rel).read_text(), rel
print('PASS Studio/Godot parity, condition paths, editor, publisher, and Test Lab hooks')
