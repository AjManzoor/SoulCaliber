"""Source wiring/schema smoke test; NOT a substitute for compiling/running WPF on Windows."""
from pathlib import Path
import re, sqlite3, xml.etree.ElementTree as ET, tempfile

ROOT=Path(__file__).resolve().parents[1]
STUDIO=ROOT/'Studio'
for xaml in STUDIO.glob('*.xaml'):
    ET.parse(xaml)
    class_match = re.search(r'x:Class="([^"]+)"',xaml.read_text(encoding='utf-8'))
    if not class_match: continue
    name=class_match.group(1).split('.')[-1]
    # Classes with partial files are split into multiple codebehind files.
    source='\n'.join(f.read_text(encoding='utf-8') for f in STUDIO.glob(f'{name}*.cs'))
    xml=xaml.read_text(encoding='utf-8')
    for callback in re.findall(r'\b(?:Click|SelectionChanged|Loaded|Closing|TextChanged|AutoGeneratingColumn)="([A-Za-z_][A-Za-z_0-9]*)"',xml):
        assert re.search(r'\b'+callback+r'\s*\(',source), f'{xaml.name}: missing event handler {callback}'

sim=(STUDIO/'SimulationData.cs').read_text()
assert 'Mode = SqliteOpenMode.ReadOnly' in sim
assert 'PRAGMA query_only=ON' in sim
assert 'DataSource = Path.GetFullPath(databasePath)' in sim
assert 'ConditionalDialogueRules.Resolve(' in sim

with sqlite3.connect(':memory:') as db:
    db.executescript('''
    PRAGMA foreign_keys=ON;
    CREATE TABLE Characters(Id TEXT PRIMARY KEY,Name TEXT NOT NULL);
    CREATE TABLE Personalities(CharacterId TEXT PRIMARY KEY, Persuasion INTEGER, Intimidation INTEGER, Deception INTEGER, Charm INTEGER, Insight INTEGER, Resolve INTEGER);
    CREATE TABLE Interactions(Id INTEGER PRIMARY KEY,Key TEXT,Title TEXT,StartNodeKey TEXT);
    CREATE TABLE Nodes(Id INTEGER PRIMARY KEY,InteractionId INTEGER,Key TEXT,Round INTEGER,SortOrder INTEGER,HostIntroduction TEXT);
    CREATE TABLE Options(Id INTEGER PRIMARY KEY,NodeId INTEGER,Key TEXT,Label TEXT,SortOrder INTEGER,BaseSuccessChance INTEGER,SuccessNextNodeKey TEXT,FailureNextNodeKey TEXT,DefaultPlayerLine TEXT,DefaultHostSuccessLine TEXT,DefaultHostFailureLine TEXT,SuccessTargetReaction TEXT,FailureTargetReaction TEXT);
    CREATE TABLE Requirements(OptionId INTEGER,Direction TEXT,Attribute TEXT,Minimum INTEGER,Maximum INTEGER);
    CREATE TABLE ChanceModifiers(OptionId INTEGER,Direction TEXT,Attribute TEXT,Factor REAL);
    CREATE TABLE Effects(Id INTEGER PRIMARY KEY,OptionId INTEGER,Outcome TEXT,Direction TEXT,Attribute TEXT,Delta INTEGER);
    CREATE TABLE SkillChecks(OptionId INTEGER PRIMARY KEY,ActorSkill TEXT,DefenderSkill TEXT,CheckMode TEXT,ActorFactor REAL,DefenderFactor REAL);
    CREATE TABLE Lines(Id INTEGER PRIMARY KEY,OptionId INTEGER,CharacterId TEXT,VisitorId TEXT,Outcome TEXT,Text TEXT,ImagePath TEXT);
    CREATE TABLE NodeIntroductions(Id INTEGER PRIMARY KEY,NodeId INTEGER,CharacterId TEXT,VisitorId TEXT,Text TEXT);
    CREATE TABLE ConditionalDialogueVariants(Id INTEGER PRIMARY KEY,NodeId INTEGER,OptionId INTEGER,CharacterId TEXT,VisitorId TEXT,Outcome TEXT,Priority INTEGER,Text TEXT,ImagePath TEXT);
    CREATE TABLE ConditionalDialogueConditions(Id INTEGER PRIMARY KEY,VariantId INTEGER,Direction TEXT,Attribute TEXT,Minimum INTEGER,Maximum INTEGER);
    INSERT INTO Characters VALUES('ARK','Ark'),('NOBUNOGI','Nobunogi');
    INSERT INTO Interactions VALUES(1,'INFORMATION_GATHERING','Information Gathering','OPENING');
    INSERT INTO Nodes VALUES(1,1,'OPENING',1,0,'What do you wish to know?');
    INSERT INTO Nodes VALUES(2,1,'FOLLOWUP',2,1,'Anything else?');
    INSERT INTO Options VALUES(1,1,'ASK','Ask directly',0,60,'FOLLOWUP',NULL,'Tell me.','Very well.','No.', 'RECEPTIVE','DISMISSIVE');
    INSERT INTO Effects VALUES(1,1,'SUCCESS','PARTNER_TO_ACTOR','Trust',10);
    INSERT INTO ConditionalDialogueVariants VALUES(1,1,NULL,NULL,NULL,NULL,10,'You have earned my trust.','');
    INSERT INTO ConditionalDialogueConditions VALUES(1,1,'PARTNER_TO_ACTOR','Trust',70,NULL);
    ''')
    queries = [
      'SELECT Id, Name FROM Characters ORDER BY Name',
      'SELECT CharacterId, Persuasion, Intimidation, Deception, Charm, Insight, Resolve FROM Personalities',
      'SELECT Id, Key, Title, StartNodeKey FROM Interactions ORDER BY Title',
      'SELECT Id, InteractionId, Key, Round, HostIntroduction FROM Nodes ORDER BY SortOrder, Id',
      'SELECT Id, NodeId, Key, Label, BaseSuccessChance, SuccessNextNodeKey, FailureNextNodeKey, DefaultPlayerLine, DefaultHostSuccessLine, DefaultHostFailureLine, SuccessTargetReaction, FailureTargetReaction FROM Options ORDER BY SortOrder, Id',
      'SELECT OptionId, Direction, Attribute, Minimum, Maximum FROM Requirements',
      'SELECT OptionId, Direction, Attribute, Factor FROM ChanceModifiers',
      'SELECT OptionId, Outcome, Direction, Attribute, Delta FROM Effects ORDER BY Id',
      'SELECT OptionId, ActorSkill, DefenderSkill, CheckMode, ActorFactor, DefenderFactor FROM SkillChecks',
      'SELECT Id, OptionId, CharacterId, VisitorId, Outcome, Text, ImagePath FROM Lines ORDER BY Id',
      'SELECT Id, NodeId, CharacterId, VisitorId, Text FROM NodeIntroductions ORDER BY Id',
      'SELECT VariantId, Direction, Attribute, Minimum, Maximum FROM ConditionalDialogueConditions ORDER BY Id',
      'SELECT Id, NodeId, OptionId, CharacterId, VisitorId, Outcome, Priority, Text, ImagePath FROM ConditionalDialogueVariants ORDER BY Id'
    ]
    for query in queries: db.execute(query).fetchall()
    c=db.execute('SELECT Minimum FROM ConditionalDialogueConditions').fetchone()[0]
    assert 0 < c and 80 >= c
    next_node=db.execute('SELECT SuccessNextNodeKey FROM Options WHERE Key="ASK"').fetchone()[0]
    assert next_node == 'FOLLOWUP'
    effect=db.execute('SELECT Delta FROM Effects WHERE Outcome="SUCCESS"').fetchone()[0]
    assert effect == 10
print('OK: XAML, event handlers, read-only guards and sample SQLite schema queries')
