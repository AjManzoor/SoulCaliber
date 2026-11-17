"""Synthetic-schema importer smoke tests. Does not touch a real Studio database."""
from pathlib import Path
from tempfile import TemporaryDirectory
import sqlite3, subprocess, sys, json
from import_into_studio import check_db,insert,validate_pack, PACK
P=Path(__file__).parent
pack=json.loads(PACK.read_text(encoding='utf8'))
validate_pack(pack)
with TemporaryDirectory() as d:
 dbpath=Path(d)/'studio_test_master.db'
 with sqlite3.connect(dbpath) as db:
  db.execute('PRAGMA foreign_keys=ON')
  db.executescript('''
CREATE TABLE Interactions(Id INTEGER PRIMARY KEY,Key TEXT UNIQUE NOT NULL,Title TEXT NOT NULL,HostCharacterId TEXT NOT NULL,StartNodeKey TEXT NOT NULL);
CREATE TABLE Nodes(Id INTEGER PRIMARY KEY,InteractionId INTEGER NOT NULL REFERENCES Interactions(Id),Key TEXT NOT NULL,Round INTEGER,SortOrder INTEGER,HostIntroduction TEXT,UNIQUE(InteractionId,Key));
CREATE TABLE ReactionTypes(Key TEXT PRIMARY KEY COLLATE NOCASE);
CREATE TABLE CharacterSkills(Key TEXT PRIMARY KEY COLLATE NOCASE,IsActive INTEGER);
CREATE TABLE CheckProfiles(Key TEXT PRIMARY KEY COLLATE NOCASE);
CREATE TABLE RelationshipAttributes(Key TEXT PRIMARY KEY COLLATE NOCASE,IsActive INTEGER,MinValue INTEGER,MaxValue INTEGER);
CREATE TABLE RelationshipStatuses(Key TEXT PRIMARY KEY COLLATE NOCASE,IsActive INTEGER);
CREATE TABLE Options(Id INTEGER PRIMARY KEY,NodeId INTEGER NOT NULL REFERENCES Nodes(Id),Key TEXT NOT NULL,Label TEXT NOT NULL,SortOrder INTEGER,BaseSuccessChance INTEGER CHECK(BaseSuccessChance BETWEEN 0 AND 100),SuccessNextNodeKey TEXT,FailureNextNodeKey TEXT,DefaultPlayerLine TEXT NOT NULL,DefaultHostSuccessLine TEXT NOT NULL,DefaultHostFailureLine TEXT NOT NULL,SuccessTargetReaction TEXT NOT NULL REFERENCES ReactionTypes(Key),FailureTargetReaction TEXT NOT NULL REFERENCES ReactionTypes(Key),UnmetRequirementBehaviour TEXT NOT NULL DEFAULT 'LOCK' CHECK(UnmetRequirementBehaviour IN ('LOCK','HIDE')),UNIQUE(NodeId,Key));
CREATE TABLE Requirements(Id INTEGER PRIMARY KEY,OptionId INTEGER NOT NULL REFERENCES Options(Id),Direction TEXT,Attribute TEXT REFERENCES RelationshipAttributes(Key),Minimum INTEGER,Maximum INTEGER);
CREATE TABLE Effects(Id INTEGER PRIMARY KEY,OptionId INTEGER NOT NULL REFERENCES Options(Id),Outcome TEXT,Direction TEXT,Attribute TEXT REFERENCES RelationshipAttributes(Key),Delta INTEGER);
CREATE TABLE SkillChecks(OptionId INTEGER PRIMARY KEY REFERENCES Options(Id),ActorSkill TEXT REFERENCES CharacterSkills(Key),DefenderSkill TEXT REFERENCES CharacterSkills(Key),CheckMode TEXT REFERENCES CheckProfiles(Key),ActorFactor REAL,DefenderFactor REAL);
CREATE TABLE OptionCheckProfiles(OptionId INTEGER PRIMARY KEY REFERENCES Options(Id),ProfileKey TEXT REFERENCES CheckProfiles(Key));
CREATE TABLE OptionStatusRequirements(Id INTEGER PRIMARY KEY,OptionId INTEGER NOT NULL REFERENCES Options(Id),Direction TEXT,StatusKey TEXT COLLATE NOCASE REFERENCES RelationshipStatuses(Key),UNIQUE(OptionId,Direction));
CREATE TABLE OptionStatusEffects(Id INTEGER PRIMARY KEY,OptionId INTEGER NOT NULL REFERENCES Options(Id),Outcome TEXT,Direction TEXT,StatusKey TEXT COLLATE NOCASE REFERENCES RelationshipStatuses(Key),UNIQUE(OptionId,Outcome,Direction));
''')
  for skill in ['MIGHT','FINESSE','INTELLECT','CUNNING','CHARISMA','WILLPOWER']:
   db.execute('INSERT INTO CharacterSkills VALUES(?,1)',(skill,))
  for prof in ['NEUTRAL','STANDARD','INTIMIDATION','COOPERATIVE']:
   db.execute('INSERT INTO CheckProfiles VALUES(?)',(prof,))
  for stat in ['ALLY','NEUTRAL','ENEMY']:
   db.execute('INSERT INTO RelationshipStatuses VALUES(?,1)',(stat,))
  for att,lo,hi in [('RESPECT',-100,100),('FEAR',0,100),('TRUST',-100,100),('LOYALTY',-100,100),('RIVALRY',0,100),('RESENTMENT',0,100)]:
   db.execute('INSERT INTO RelationshipAttributes VALUES(?,1,?,?)',(att,lo,hi))
  for react in ['ACCEPTING','AMUSED','ANGRY','COLD','CONSIDERING','DISMISSIVE','IMPRESSED','INTERESTED','INTIMIDATED','OFFENDED','PLEASED','RECEPTIVE','REFLECTIVE','RELIEVED','SUSPICIOUS','UNIMPRESSED','UNMOVED','WARM','WARY']:
   db.execute('INSERT INTO ReactionTypes VALUES(?)',(react,))
  db.commit()
  check_db(db,pack)

 def run(*args):
  return subprocess.run([sys.executable,str(P/'import_into_studio.py'),'--db',str(dbpath),*args],capture_output=True,text=True)
 res=run()
 assert res.returncode==0,res.stderr
 assert 'DRY RUN PASSED' in res.stdout
 with sqlite3.connect(dbpath) as db:
  assert db.execute('SELECT COUNT(*) FROM Interactions').fetchone()[0]==0
 print('PASS: dry run read-only')
 # Simulate a mid-import SQL failure and verify rollback of interaction + all children.
 with sqlite3.connect(dbpath) as db:
  db.execute("CREATE TRIGGER reject_warning BEFORE INSERT ON Options WHEN NEW.Key='WARNING_BOAST' BEGIN SELECT RAISE(ABORT,'simulated insertion error'); END;")
  db.commit()
 bad=run('--apply')
 assert bad.returncode!=0 and 'simulated insertion error' in bad.stderr,bad.stderr
 with sqlite3.connect(dbpath) as db:
  assert db.execute('SELECT COUNT(*) FROM Interactions').fetchone()[0]==0
  assert db.execute('SELECT COUNT(*) FROM Nodes').fetchone()[0]==0
  assert db.execute('SELECT COUNT(*) FROM Options').fetchone()[0]==0
  db.execute('DROP TRIGGER reject_warning');db.commit()
 print('PASS: full transaction rollback on mid-import failure')
 res=run('--apply')
 assert res.returncode==0,res.stderr
 with sqlite3.connect(dbpath) as db:
  counts={table:db.execute(f'SELECT COUNT(*) FROM {table}').fetchone()[0] for table in ['Interactions','Nodes','Options','Requirements','Effects','SkillChecks','OptionCheckProfiles','OptionStatusRequirements','OptionStatusEffects']}
  assert counts=={'Interactions':1,'Nodes':15,'Options':50,'Requirements':14,'Effects':50,'SkillChecks':22,'OptionCheckProfiles':50,'OptionStatusRequirements':18,'OptionStatusEffects':7}, counts
  assert db.execute("SELECT COUNT(*) FROM Options WHERE UnmetRequirementBehaviour='HIDE'").fetchone()[0]==11
  assert db.execute('PRAGMA foreign_key_check').fetchall()==[]
 print('PASS: committed row counts, visibility, and foreign keys',counts)
 dup=run('--apply')
 assert dup.returncode!=0 and 'already exists' in dup.stderr,dup.stderr
 print('PASS: duplicate-key import refused; no overwrite')
print('ALL TESTS PASSED')
