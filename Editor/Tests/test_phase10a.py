from pathlib import Path
import sqlite3, xml.etree.ElementTree as ET, re, tempfile, shutil
root=Path('/mnt/data/SoulCaliber_Phase10A_DynamicSkills')
studio=root/'Studio'
for xaml in studio.glob('*.xaml'):
    ET.parse(xaml)
    match=re.search(r'x:Class="([^"]+)"',xaml.read_text())
    if not match: continue
    name=match.group(1).split('.')[-1]
    code='\n'.join(x.read_text() for x in studio.glob(name+'*.cs'))
    for event in re.findall(r'\b(?:Click|SelectionChanged|Loaded|Closing|TextChanged|AutoGeneratingColumn)="([A-Za-z_]\w*)"',xaml.read_text()):
        assert re.search(r'\b'+event+r'\s*\(',code), (xaml,event)
print('PASS:  Studio XAML + handlers')
# Phase9 -> Phase10A SQL migration, legacy values retained, FK safety, existing characters initialized
with sqlite3.connect(':memory:') as db:
    db.execute('PRAGMA foreign_keys=ON')
    db.executescript('''
    CREATE TABLE Characters(Id TEXT PRIMARY KEY);
    CREATE TABLE Interactions(Id INTEGER PRIMARY KEY);
    CREATE TABLE Nodes(Id INTEGER PRIMARY KEY,InteractionId INTEGER REFERENCES Interactions(Id));
    CREATE TABLE Options(Id INTEGER PRIMARY KEY,NodeId INTEGER REFERENCES Nodes(Id));
    CREATE TABLE SkillChecks(OptionId INTEGER PRIMARY KEY REFERENCES Options(Id),CheckMode TEXT);
    CREATE TABLE Requirements(Id INTEGER PRIMARY KEY,OptionId INTEGER REFERENCES Options(Id),Attribute TEXT,Minimum INTEGER,Maximum INTEGER);
    CREATE TABLE ChanceModifiers(Id INTEGER PRIMARY KEY,OptionId INTEGER REFERENCES Options(Id),Attribute TEXT);
    CREATE TABLE Effects(Id INTEGER PRIMARY KEY,OptionId INTEGER REFERENCES Options(Id),Attribute TEXT);
    CREATE TABLE ConditionalDialogueVariants(Id INTEGER PRIMARY KEY);
    CREATE TABLE ContentSchemaMigrations(Version TEXT PRIMARY KEY,AppliedUtc TEXT);
    CREATE TABLE Personalities(CharacterId TEXT PRIMARY KEY REFERENCES Characters(Id),Persuasion INTEGER, Intimidation INTEGER, Deception INTEGER, Charm INTEGER, Insight INTEGER, Resolve INTEGER);
    INSERT INTO Characters(Id) VALUES('ARK'),('NOBUNOGI');
    INSERT INTO Personalities VALUES('ARK',80,30,40,60,90,70);
    ''')
    db.executescript((studio/'Migrations/005_CheckProfiles.sql').read_text())
    db.executescript('''
    CREATE TABLE ConditionalDialogueConditions(
      Id INTEGER PRIMARY KEY,VariantId INTEGER NOT NULL REFERENCES ConditionalDialogueVariants(Id) ON DELETE CASCADE,
      Direction TEXT NOT NULL CHECK(Direction IN ('ACTOR_TO_PARTNER','PARTNER_TO_ACTOR')),
      Attribute TEXT NOT NULL CHECK(Attribute IN ('Love','Fear','Allyship','Respect','Trust','Romance')),
      Minimum INTEGER,Maximum INTEGER);
    ''')
    db.executescript((studio/'Migrations/006_DynamicRelationships.sql').read_text())
    db.executescript((studio/'Migrations/007_DynamicSkills.sql').read_text())
    skills=db.execute('SELECT Key,DefaultValue FROM CharacterSkills ORDER BY SortOrder').fetchall()
    assert skills == [('MIGHT',50),('FINESSE',50),('INTELLECT',50),('CUNNING',50),('CHARISMA',50),('WILLPOWER',50)],skills
    assert db.execute('SELECT COUNT(*) FROM CharacterSkillValues').fetchone()[0]==12
    assert db.execute('SELECT COUNT(*) FROM Personalities').fetchone()[0]==1
    assert db.execute("SELECT Persuasion FROM Personalities WHERE CharacterId='ARK'").fetchone()[0]==80
    assert db.execute("SELECT Value FROM CharacterSkillValues WHERE CharacterId='ARK' AND SkillKey='MIGHT'").fetchone()[0]==50
    db.execute("UPDATE CharacterSkillValues SET Value=90 WHERE CharacterId='ARK' AND SkillKey='MIGHT'")
    db.execute("INSERT INTO CharacterSkills(Key,Name,Description,MinValue,MaxValue,DefaultValue,SortOrder,IsActive) VALUES('LEADERSHIP','Leadership','',0,100,50,200,1)")
    db.execute("INSERT INTO CharacterSkillValues(CharacterId,SkillKey,Value) VALUES('ARK','LEADERSHIP',77)")
    try:
        db.execute("INSERT INTO CharacterSkillValues(CharacterId,SkillKey,Value) VALUES('ARK','NOTREAL',50)")
        raise AssertionError('Undefined skill accepted')
    except sqlite3.IntegrityError: pass
    assert db.execute('PRAGMA foreign_key_check').fetchall()==[]
    assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    assert 'CharacterRelationshipSensitivities' not in [t[0] for t in db.execute("SELECT name FROM sqlite_master WHERE type='table'")]
print('PASS:  Phase9 -> 10A migration, default 50, legacy kept, new skill FK, no sensitivities')
# package contents; new phase must not change relationship save
assert not (root/'GodotPatch/Dialogue/RelationshipSensitivities.cs').exists()
for file in ['Studio/CharacterStudioStore.Dynamic.cs','Studio/ContentPublisher.cs','Studio/SimulationData.cs','Studio/MainWindow.xaml']:
    assert 'CharacterRelationshipSensitivities' not in (root/file).read_text(), file
assert 'RelationshipSensitivities' not in (root/'GodotPatch/Persistence/SqliteRelationshipStore.cs').read_text()
print('PASS:  no Phase 10B features in Phase 10A')
