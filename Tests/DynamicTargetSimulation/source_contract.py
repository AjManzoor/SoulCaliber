"""Portable source/XAML wiring guards; not a replacement for Windows compilation."""
from pathlib import Path
from xml.etree import ElementTree as ET
root=Path(__file__).resolve().parents[2]
studio=root/'Editor/Studio'
game=root/'Game/Dialogue'
ET.parse(studio/'InteractionSimulatorWindow.xaml')
ET.parse(studio/'SoulCaliberStudio.csproj')
xaml=(studio/'InteractionSimulatorWindow.xaml').read_text()
assert all(needle in xaml for needle in ['TargetPickerBox','TargetPicker_SelectionChanged',
    'PairFromBox','PairToBox','SetPairStatus_Click','ResetPairStatuses_Click'])
project=(studio/'SoulCaliberStudio.csproj').read_text()
assert 'DynamicTargetRules.cs' in project
simulator=(studio/'InteractionSimulatorWindow.DynamicTargets.cs').read_text()
engine=(game/'DialogueEngine.cs').read_text()
for needle in ['DynamicTargetRules.Find(', 'DynamicTargetRules.ResolvePairEffects(']:
    assert needle in simulator, needle
    assert needle in engine, needle
assert 'Mode = SqliteOpenMode.ReadOnly' in (studio/'SimulationData.cs').read_text()
assert 'PRAGMA query_only=ON;' in (studio/'SimulationData.cs').read_text()
assert 'LoadDynamicTargetSimulationData();' in (studio/'SimulationData.cs').read_text()
assert 'UpdateTargetPicker();' in (studio/'InteractionSimulatorWindow.Statuses.cs').read_text()
assert 'PreviewPairStatusChanges' in (studio/'InteractionSimulatorWindow.xaml.cs').read_text()
assert 'DynamicTargetRules.FormatTarget(' in (studio/'InteractionSimulatorWindow.xaml.cs').read_text()
assert 'DynamicTargetRules.FormatTarget(' in engine
assert 'CurrentSimulatedStatus' in (studio/'InteractionSimulatorWindow.MemoriesEncounters.cs').read_text()
assert 'StatusPairKey(from, to)' in (studio/'InteractionSimulatorWindow.MemoriesEncounters.cs').read_text()
print('PASS: Test Lab XAML, shared runtime rules, in-memory sandbox, and read-only Studio data wiring')
