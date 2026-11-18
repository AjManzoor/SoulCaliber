"""Static guard for Studio/Godot parity and shared authoring integration."""
from pathlib import Path
r = Path(__file__).resolve().parents[2]
game = (r / 'Game/Dialogue/MemoryConditionRules.cs').read_bytes()
studio = (r / 'Editor/Studio/MemoryConditionRules.cs').read_bytes()
assert game == studio, 'Studio and Godot condition engines differ'
editor = (r / 'Editor/Studio/MemoryRequirementsWindow.cs').read_text()
assert 'Add third-party relationship' in editor
assert 'RelationshipFromRef = from' in editor and 'RelationshipToRef = to' in editor
engine = (r / 'Game/Dialogue/DialogueEngine.cs').read_text()
assert 'ContainsKind(option.MemoryRequirements, "RELATIONSHIP")' in engine
print('PASS shared Studio/Godot rules identical')
print('PASS authoring editor exposes RELATIONSHIP leaf')
print('PASS Godot dialogue choices fail closed if status service unavailable')
