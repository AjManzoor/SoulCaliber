"""Phase 13H-4 authoring migration tests. Run with Python, no SQLite file is changed."""
import sqlite3
from pathlib import Path

root = Path(__file__).resolve().parents[2]
migration = (root / "Editor/Studio/Migrations/023_DynamicTargetChoices.sql").read_text()

def database():
    con = sqlite3.connect(":memory:")
    con.execute("PRAGMA foreign_keys=ON")
    con.executescript("""
        CREATE TABLE Options(Id INTEGER PRIMARY KEY);
        CREATE TABLE RelationshipStatuses(Key TEXT PRIMARY KEY COLLATE NOCASE,IsActive INTEGER NOT NULL);
        CREATE TABLE Characters(Id TEXT PRIMARY KEY);
        CREATE TABLE ContentSchemaMigrations(Version TEXT PRIMARY KEY,AppliedUtc TEXT NOT NULL);
        INSERT INTO Options VALUES(7);
        INSERT INTO RelationshipStatuses VALUES('ALLY',1),('NEUTRAL',1),('ENEMY',1);
        INSERT INTO Characters VALUES('LAW'),('YOURI'),('NOBUNOGI');
    """)
    con.executescript(migration)
    return con

c=database()
c.execute("INSERT INTO OptionDynamicTargets VALUES(7,'RECIPIENT','OUTGOING','ALLY')")
c.execute("INSERT INTO OptionPairStatusEffects(OptionId,Outcome,FromRef,ToRef,StatusKey,ReverseStatusKey) VALUES(7,'SUCCESS','RECIPIENT','TARGET','NEUTRAL','NEUTRAL')")
assert c.execute("SELECT count(*) FROM OptionPairStatusEffects").fetchone()[0] == 1
assert c.execute("PRAGMA foreign_key_check").fetchall() == []
print("PASS: dynamic target and mutual status effect persisted")
try:
    c.execute("INSERT INTO OptionDynamicTargets VALUES(8,'RECIPIENT','OUTGOING','ALLY')")
    raise AssertionError("invalid option accepted")
except sqlite3.IntegrityError: pass
print("PASS: unknown option rejected")
try:
    c.execute("INSERT INTO OptionPairStatusEffects(OptionId,Outcome,FromRef,ToRef,StatusKey) VALUES(7,'SUCCESS','RECIPIENT','TARGET','NOT_A_STATUS')")
    raise AssertionError("unknown status accepted")
except sqlite3.IntegrityError: pass
print("PASS: unknown status rejected")
try:
    c.execute("INSERT INTO OptionPairStatusEffects(OptionId,Outcome,FromRef,ToRef,StatusKey) VALUES(7,'OTHER','RECIPIENT','TARGET','ALLY')")
    raise AssertionError("invalid outcome accepted")
except sqlite3.IntegrityError: pass
print("PASS: invalid outcome rejected")
c.execute("DELETE FROM Options WHERE Id=7")
assert c.execute("SELECT COUNT(*) FROM OptionDynamicTargets").fetchone()[0] == 0
assert c.execute("SELECT COUNT(*) FROM OptionPairStatusEffects").fetchone()[0] == 0
print("PASS: deleting an authored option removes its dynamic rule and effects")
assert c.execute("SELECT Version FROM ContentSchemaMigrations").fetchone()[0]=="023_DynamicTargetChoices"
print("PASS: schema migration recorded")
