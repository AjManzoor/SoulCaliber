from pathlib import Path
from xml.etree import ElementTree as ET
root=Path(__file__).resolve().parents[2]
ET.parse(root/'Editor/Studio/ChoiceMechanicsWindow.xaml')
ET.parse(root/'Editor/Studio/SoulCaliberStudio.csproj')
files={p.name:p.read_text() for p in (root/'Game/Dialogue').glob('*.cs')}
scene=(root/'Game/Scenes/InteractionScene.cs').read_text()
store=(root/'Game/Persistence/SqliteInteractionEffectStore.cs').read_text()
assert 'ShowDynamicTargetMenu' in scene and 'Choose(selection)' in scene
assert 'DynamicTargetRules.Find' in files['DialogueEngine.cs']
assert 'PairStatusEffects' in files['InteractionEffectModels.cs']
assert 'choice.PairStatusEffects' in store
assert 'ApplyInTransaction' in store
assert 'EditDynamicTargets_Click' in (root/'Editor/Studio/ChoiceMechanicsWindow.xaml.cs').read_text()
assert 'EnsureDynamicTargetChoicesSchema' in (root/'Editor/Studio/MainWindow.Testing.cs').read_text()
assert 'OptionPairStatusEffects' in (root/'Editor/Studio/ContentPublisher.cs').read_text()
print('PASS: source contract and XAML/XML parsing')
