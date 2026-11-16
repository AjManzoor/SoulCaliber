"""Non-Windows preflight checks. Not a substitute for a WPF compile or Godot runtime test."""
from pathlib import Path
from zipfile import ZipFile
import re
import sqlite3
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
studio = root / 'Studio'

# Every WPF XAML file should remain well-formed, and every event handler must exist
# in one of the class's partial declarations.
all_source = '\n'.join(x.read_text(encoding='utf-8') for x in studio.glob('*.cs'))
for file in studio.glob('*.xaml'):
    ET.parse(file)
    for handler in re.findall(
        r'\b(?:Click|SelectionChanged|TextChanged|Checked|Unchecked|Loaded|Closing)="([A-Za-z_][A-Za-z0-9_]*)"',
        file.read_text(encoding='utf-8'),
    ):
        assert re.search(r'\b' + re.escape(handler) + r'\s*\(', all_source), (file.name, handler)
print('PASS: all XAML parses and event handlers resolve')

# New UI uses the exact shared probability/trait calculation files from Phase 10B.
with ZipFile('/mnt/data/SoulCaliberStudio_Phase10B_Full.zip') as package:
    for name in ['ChanceFormula.cs', 'PersonalityEffectRules.cs']:
        assert (studio / name).read_bytes() == package.read('Studio/' + name)
print('PASS: original chance/trait rules unchanged')

# Both paths, preview and application, use the audit's predicted relationship effects.
source = (studio / 'InteractionSimulatorWindow.xaml.cs').read_text()
audit = (studio / 'InteractionAudit.cs').read_text()
data = (studio / 'SimulationData.cs').read_text()
assert 'var projected = succeeded ? audit.Success : audit.Failure;' in source
assert 'row.ActorToPartner = effect.After;' in source
assert 'row.PartnerToActor = effect.After;' in source
assert 'new Dictionary<string, int>(current' not in audit
assert 'working[key] = after;' in audit  # sequential effects on same value
assert 'PersonalityEffectRules traitRules' in audit
assert 'ChanceFormula.Calculate(' in audit
assert 'SqliteOpenMode.ReadOnly' in data and 'PRAGMA query_only=ON' in data
assert 'ActorTraitsList' in (studio/'InteractionSimulatorWindow.xaml').read_text()
assert 'PartnerTraitsList' in (studio/'InteractionSimulatorWindow.xaml').read_text()
print('PASS: preview/application share formula and projections, content is read-only')

# Catalogues and traits are present in the Phase 10B migration. Use a disposable DB.
connection = sqlite3.connect(':memory:')
connection.execute('PRAGMA foreign_keys = ON')
connection.executescript('''
CREATE TABLE Characters(Id TEXT PRIMARY KEY, Name TEXT NOT NULL);
CREATE TABLE RelationshipAttributes(Key TEXT PRIMARY KEY,Name TEXT NOT NULL,IsActive INTEGER NOT NULL);
CREATE TABLE ContentSchemaMigrations(Version TEXT PRIMARY KEY,AppliedUtc TEXT);
INSERT INTO Characters VALUES ('ARK','Ark'),('NOBUNOGI','Nobunogi');
INSERT INTO RelationshipAttributes VALUES('FEAR','Fear',1),('TRUST','Trust',1),('RESENTMENT','Resentment',1);
''')
connection.executescript((studio / 'Migrations' / '008_PersonalityTraits.sql').read_text())
connection.execute("INSERT INTO CharacterTraits VALUES ('NOBUNOGI','BRAVE')")
connection.execute("INSERT INTO CharacterTraits VALUES ('NOBUNOGI','VINDICTIVE')")
assert connection.execute('PRAGMA foreign_key_check').fetchall() == []
assert connection.execute("SELECT Factor FROM PersonalityTraitModifiers WHERE TraitKey='BRAVE'").fetchone()[0] == 0.5
assert connection.execute("SELECT Factor FROM PersonalityTraitModifiers WHERE TraitKey='VINDICTIVE'").fetchone()[0] == 1.5
print('PASS: Phase 10B trait table and sample assignments remain valid')

# Regression examples derived from unchanged rules: midpoint rounding AWAY from zero.
from decimal import Decimal, ROUND_HALF_UP

def adjusted(base, factors):
    from functools import reduce
    from operator import mul
    factor = min(4.0, reduce(mul, factors, 1.0))
    return int((Decimal(str(base)) * Decimal(str(factor))).quantize(Decimal('1'), rounding=ROUND_HALF_UP))

assert adjusted(20, [.5]) == 10        # Brave
assert adjusted(20, [1.75]) == 35      # Timid
assert adjusted(20, [.5, 1.75]) == 18  # Brave + Timid
assert adjusted(5, [.5]) == 3          # half-step rounding
assert adjusted(-5, [1.5]) == -8       # sign-aware rounding
assert adjusted(20, [3, 3]) == 80      # x4 stacking cap
assert max(0, min(100, 95 + adjusted(20, [.5]))) == 100  # final clamp
print('PASS: independent arithmetic fixtures for stacking, rounding, and clamping')
