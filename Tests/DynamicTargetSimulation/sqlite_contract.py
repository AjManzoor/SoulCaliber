"""SQLite 023/024 round-trip and Test Lab read-query compatibility (in-memory only)."""
from pathlib import Path
import sqlite3

root=Path(__file__).resolve().parents[2]
conn=sqlite3.connect(':memory:')
conn.execute('PRAGMA foreign_keys=ON')
conn.executescript('''
 CREATE TABLE Options(Id INTEGER PRIMARY KEY);
 CREATE TABLE RelationshipStatuses(Key TEXT PRIMARY KEY, IsActive INTEGER);
 CREATE TABLE Characters(Id TEXT PRIMARY KEY COLLATE NOCASE);
 CREATE TABLE ContentSchemaMigrations(Version TEXT PRIMARY KEY,AppliedUtc TEXT);
 INSERT INTO Options VALUES(1),(2);
 INSERT INTO RelationshipStatuses VALUES('ALLY',1),('NEUTRAL',1),('ENEMY',1);
 INSERT INTO Characters VALUES('LAW'),('YOURI'),('ARK'),('NOBUNOGI');
''')
conn.executescript((root/'Editor/Studio/Migrations/023_DynamicTargetChoices.sql').read_text())
conn.executescript((root/'Editor/Studio/Migrations/024_CustomDynamicTargetLists.sql').read_text())
conn.execute("INSERT INTO OptionDynamicTargets(OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey) VALUES(1,'RECIPIENT','OUTGOING','ALLY')")
conn.execute("INSERT INTO OptionDynamicTargets(OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,SelectionMode) VALUES(2,'RECIPIENT','OUTGOING','ENEMY','CUSTOM')")
conn.execute('INSERT INTO OptionDynamicTargetCharacters(OptionId,CharacterId,SortOrder) VALUES(2,\'NOBUNOGI\',0),(2,\'ARK\',1)')
conn.execute("INSERT INTO OptionPairStatusEffects(OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey) VALUES(1,'SUCCESS','RECIPIENT','TARGET','NEUTRAL','NEUTRAL')")
conn.execute("INSERT INTO OptionPairStatusEffects(OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey) VALUES(2,'FAILURE','RECIPIENT','TARGET','ENEMY',NULL)")
assert conn.execute('SELECT OptionId,OwnerRef,RelationshipDirection,RequiredStatusKey,SelectionMode FROM OptionDynamicTargets ORDER BY OptionId').fetchall()==[
 (1,'RECIPIENT','OUTGOING','ALLY','GROUP'),(2,'RECIPIENT','OUTGOING','ENEMY','CUSTOM')]
assert conn.execute('SELECT OptionId,CharacterId FROM OptionDynamicTargetCharacters ORDER BY OptionId,SortOrder,CharacterId').fetchall()==[(2,'NOBUNOGI'),(2,'ARK')]
assert conn.execute('SELECT OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey FROM OptionPairStatusEffects ORDER BY Id').fetchall()==[
 (1,'SUCCESS','RECIPIENT','TARGET','NEUTRAL','NEUTRAL'),(2,'FAILURE','RECIPIENT','TARGET','ENEMY',None)]
assert conn.execute('PRAGMA foreign_key_check').fetchall()==[]
print('PASS: Studio and Godot projection queries share stable 023/024 schemas')
conn.execute('DELETE FROM Options WHERE Id=2')
assert not conn.execute('SELECT 1 FROM OptionDynamicTargetCharacters').fetchall()
assert conn.execute('SELECT count(*) FROM OptionPairStatusEffects').fetchone()[0]==1
print('PASS: deleting custom choice cascades only its own selected targets/effects')
