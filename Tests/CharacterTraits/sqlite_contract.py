"""Contract for the published-content query used by PublishedTraitLookup.
Runs on a temporary database; never touches the user's content or savegame.
"""
import sqlite3
from pathlib import Path
import tempfile
with tempfile.TemporaryDirectory(prefix='soulcaliber-traits-') as folder:
    path = Path(folder)/'content.db'
    db = sqlite3.connect(path)
    db.executescript('''
        CREATE TABLE PersonalityTraits(Key TEXT PRIMARY KEY, IsActive INTEGER NOT NULL);
        CREATE TABLE CharacterTraits(CharacterId TEXT NOT NULL, TraitKey TEXT NOT NULL);
        INSERT INTO PersonalityTraits VALUES ('MONOGAMOUS', 1), ('JEALOUS', 1), ('OBSOLETE', 0);
        INSERT INTO CharacterTraits VALUES ('YOURI','MONOGAMOUS'),('LAW','JEALOUS'),('ARK','OBSOLETE');
    ''')
    active = {row[0] for row in db.execute('SELECT Key FROM PersonalityTraits WHERE IsActive=1')}
    assigned = set(db.execute('''SELECT ct.CharacterId,ct.TraitKey FROM CharacterTraits ct
        JOIN PersonalityTraits t ON t.Key=ct.TraitKey WHERE t.IsActive=1'''))
    assert active == {'MONOGAMOUS', 'JEALOUS'}
    assert assigned == {('YOURI','MONOGAMOUS'), ('LAW','JEALOUS')}
    assert ('ARK','OBSOLETE') not in assigned
    db.close()
print('PASS active published trait catalogue and character assignments contract')
