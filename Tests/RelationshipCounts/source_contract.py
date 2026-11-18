"""Dependency-free source checks. C# behaviour tests require dotnet run."""
from pathlib import Path
base = Path(__file__).resolve().parents[2]
read = lambda name: (base / name).read_text(encoding="utf-8-sig")
assert read("Editor/Studio/MemoryConditionRules.cs") == read("Game/Dialogue/MemoryConditionRules.cs"), 'Studio/Godot evaluator drift'
rules = read("Game/Dialogue/MemoryConditionRules.cs")
for text in ["case \"RELATIONSHIP_COUNT\":", "CountExcludeOtherParticipant", "Distinct(StringComparer.OrdinalIgnoreCase)", "characterRoster is null", "CountDirection"]:
    assert text in rules, text
for name in ["Game/Dialogue/DialogueEngine.cs", "Game/Dialogue/SpecialEncounterSelector.cs", "Game/Dialogue/ConversationAvailabilitySelector.cs", "Editor/Studio/SimulationMemoryRuntime.cs"]:
    s = read(name)
    assert "MemoryConditionRules.Evaluate" in s, name
    assert ('CharacterIds' in s or 'characterKeys))' in s or 'characters))' in s or 'data.Characters.Select(c => c.Id)' in s), name + ' roster absent'
editor = read("Editor/Studio/MemoryRequirementsWindow.cs")
for name in ["Add relationship count", "CountOwnerRef", "CountDirection", "_countMinimum", "_countMaximum", "CountExcludeOtherParticipant"]:
    assert name in editor, name
print('PASS: Studio/Godot parity; four evaluation paths provide roster; Studio editor supports count leaf')
