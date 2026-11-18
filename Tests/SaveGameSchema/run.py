"""Phase 13G-5B SQLite schema-contract tests.

These tests exercise the central manager's authored SQLite DDL and the SQLite
transactional invariants on temp databases. They cannot execute the Godot C#
manager itself; run the Godot smoke tests in README as well.
"""
from __future__ import annotations

import re
import sqlite3
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / 'Game/Persistence/SaveGameSchemaManager.cs').read_text(encoding='utf-8')
MATCH = re.search(r'private const string ExtraSchemaSql = @"(.*?)";', SOURCE, re.S)
assert MATCH, 'Cannot find central save schema SQL'
EXTRA_SQL = MATCH.group(1)

# Representative EF Core SaveDbContext.CreateScript layout as understood from
# the current EF model. Production generates this script from the model itself.
EF_SCHEMA = '''
CREATE TABLE "InteractionHistory" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_InteractionHistory" PRIMARY KEY AUTOINCREMENT,
  "OccurredUtc" TEXT NOT NULL, "InteractionKey" TEXT NOT NULL,
  "NodeKey" TEXT NOT NULL, "OptionKey" TEXT NOT NULL,
  "ActorId" TEXT NOT NULL, "PartnerId" TEXT NOT NULL,
  "Succeeded" INTEGER NOT NULL, "Chance" INTEGER NOT NULL,
  "Roll" INTEGER NOT NULL, "TargetReaction" TEXT NOT NULL
);
CREATE TABLE "SavedRelationshipValues" (
  "FromCharacterId" TEXT NOT NULL, "ToCharacterId" TEXT NOT NULL,
  "AttributeKey" TEXT NOT NULL, "Value" INTEGER NOT NULL,
  CONSTRAINT "PK_SavedRelationshipValues" PRIMARY KEY
    ("FromCharacterId", "ToCharacterId", "AttributeKey")
);
CREATE TABLE "RelationshipChanges" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_RelationshipChanges" PRIMARY KEY AUTOINCREMENT,
  "SavedInteractionId" INTEGER NOT NULL, "FromCharacterId" TEXT NOT NULL,
  "ToCharacterId" TEXT NOT NULL, "Attribute" TEXT NOT NULL,
  "Before" INTEGER NOT NULL, "After" INTEGER NOT NULL,
  FOREIGN KEY("SavedInteractionId") REFERENCES "InteractionHistory"("Id") ON DELETE CASCADE
);
CREATE TABLE "SavedInteractionStatusChanges" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_SavedInteractionStatusChanges" PRIMARY KEY AUTOINCREMENT,
  "SavedInteractionId" INTEGER NOT NULL, "FromCharacterId" TEXT NOT NULL,
  "ToCharacterId" TEXT NOT NULL, "Before" TEXT NOT NULL, "After" TEXT NOT NULL,
  FOREIGN KEY("SavedInteractionId") REFERENCES "InteractionHistory"("Id") ON DELETE CASCADE
);
CREATE INDEX "IX_InteractionHistory_OccurredUtc" ON "InteractionHistory"("OccurredUtc");
CREATE INDEX "IX_RelationshipChanges_SavedInteractionId" ON "RelationshipChanges"("SavedInteractionId");
CREATE INDEX "IX_SavedInteractionStatusChanges_SavedInteractionId" ON "SavedInteractionStatusChanges"("SavedInteractionId");
'''


def open_db(path):
    conn = sqlite3.connect(path)
    conn.execute('PRAGMA foreign_keys=ON')
    return conn


def create_legacy(conn):
    conn.executescript(EF_SCHEMA + EXTRA_SQL)
    assert conn.execute('PRAGMA user_version').fetchone()[0] == 0


def validate_layout(conn):
    required = {
        'InteractionHistory', 'SavedRelationshipValues', 'RelationshipChanges',
        'SavedInteractionStatusChanges', 'SavedCharacterStatusOverrides',
        'SavedMemoryOccurrences', 'SavedMemoryAffectedCharacters',
        'SavedMemoryKnowledge', 'SavedSpecialEncounterCompletions'
    }
    found = {row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    assert required <= found, f'missing tables: {required - found}'
    for table, col in [('SavedRelationshipValues','AttributeKey'),
                       ('RelationshipChanges','SavedInteractionId'),
                       ('SavedSpecialEncounterCompletions','MemoryOccurrenceId')]:
        assert col in {r[1] for r in conn.execute(f'PRAGMA table_info({table})')}
    assert not conn.execute('PRAGMA foreign_key_check').fetchall()
    idx = {r[1]:r[2] for r in conn.execute('PRAGMA index_list(SavedSpecialEncounterCompletions)')}
    assert idx['UX_SpecialEncounter_Pair'] == 1
    assert idx['UX_SpecialEncounter_Occurrence'] == 1


class SchemaContractTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name)/'save.db'
        self.conn = open_db(self.path)
        self.addCleanup(self.conn.close)

    def test_new_schema_has_all_tables_and_constraints(self):
        create_legacy(self.conn)
        validate_layout(self.conn)
        self.conn.execute('PRAGMA user_version = 1')
        self.assertEqual(self.conn.execute('PRAGMA user_version').fetchone()[0], 1)

    def test_existing_legacy_rows_survive_version_adoption(self):
        create_legacy(self.conn)
        self.conn.execute("INSERT INTO SavedRelationshipValues VALUES ('LAW','YOURI','TRUST',61)")
        self.conn.execute("INSERT INTO SavedMemoryOccurrences (MemoryKey,ActorCharacterId,PlayerCharacterId,Severity,KnowledgeLevel,OccurredUtc) VALUES ('DEFEAT','LAW','LAW','MAJOR','QUIET','2026-11-17')")
        memory_id = self.conn.execute('SELECT Id FROM SavedMemoryOccurrences').fetchone()[0]
        self.conn.execute('INSERT INTO SavedMemoryAffectedCharacters VALUES (?,?)', (memory_id,'YOURI'))
        self.conn.execute('INSERT INTO SavedMemoryKnowledge VALUES (?,?)', (memory_id,'YOURI'))
        self.conn.execute("INSERT INTO SavedSpecialEncounterCompletions (EncounterKey,InitiatorCharacterId,RecipientCharacterId,MemoryOccurrenceId,CompletedUtc) VALUES ('DEFEATED','LAW','YOURI',?,'2026-11-17')", (memory_id,))
        self.conn.commit()
        self.conn.execute('BEGIN')
        validate_layout(self.conn)
        self.conn.execute('PRAGMA user_version = 1')
        self.conn.commit()
        self.assertEqual(self.conn.execute('SELECT Value FROM SavedRelationshipValues').fetchone()[0],61)
        self.assertEqual(self.conn.execute('SELECT COUNT(*) FROM SavedMemoryKnowledge').fetchone()[0],1)
        self.assertEqual(self.conn.execute('SELECT COUNT(*) FROM SavedSpecialEncounterCompletions').fetchone()[0],1)

    def test_ddl_and_version_roll_back_together(self):
        self.conn.execute('BEGIN')
        self.conn.execute('CREATE TABLE StagedMigration (Id INTEGER PRIMARY KEY)')
        self.conn.execute('PRAGMA user_version = 1')
        try:
            self.conn.execute('CREATE TABLE StagedMigration (Id INTEGER)')
        except sqlite3.OperationalError:
            self.conn.rollback()
        self.assertEqual(self.conn.execute('PRAGMA user_version').fetchone()[0],0)
        self.assertIsNone(self.conn.execute("SELECT name FROM sqlite_master WHERE name='StagedMigration'").fetchone())

    def test_partial_database_must_not_be_stamped(self):
        self.conn.execute('CREATE TABLE SavedRelationshipValues (AttributeKey TEXT)')
        self.conn.commit()
        with self.assertRaises(AssertionError):
            validate_layout(self.conn)
        self.assertEqual(self.conn.execute('PRAGMA user_version').fetchone()[0],0)
        self.assertTrue(self.conn.execute("SELECT 1 FROM sqlite_master WHERE name='SavedRelationshipValues'").fetchone())

    def test_unique_pair_and_occurrence_constraints(self):
        create_legacy(self.conn)
        self.conn.execute("INSERT INTO SavedSpecialEncounterCompletions (EncounterKey,InitiatorCharacterId,RecipientCharacterId,CompletedUtc) VALUES ('X','LAW','YOURI','now')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.conn.execute("INSERT INTO SavedSpecialEncounterCompletions (EncounterKey,InitiatorCharacterId,RecipientCharacterId,CompletedUtc) VALUES ('X','LAW','YOURI','now')")

    def test_separate_occurrence_completions_remain_independent(self):
        create_legacy(self.conn)
        for _ in range(2):
            self.conn.execute("INSERT INTO SavedMemoryOccurrences (MemoryKey,ActorCharacterId,PlayerCharacterId,Severity,KnowledgeLevel,OccurredUtc) VALUES ('DEFEAT','LAW','LAW','MAJOR','QUIET','2026-11-17')")
        rows = [r[0] for r in self.conn.execute('SELECT Id FROM SavedMemoryOccurrences ORDER BY Id')]
        for memory_id in rows:
            self.conn.execute("INSERT INTO SavedSpecialEncounterCompletions (EncounterKey,InitiatorCharacterId,RecipientCharacterId,MemoryOccurrenceId,CompletedUtc) VALUES ('DEFEATED','LAW','YOURI',?,'now')",(memory_id,))
        with self.assertRaises(sqlite3.IntegrityError):
            self.conn.execute("INSERT INTO SavedSpecialEncounterCompletions (EncounterKey,InitiatorCharacterId,RecipientCharacterId,MemoryOccurrenceId,CompletedUtc) VALUES ('DEFEATED','LAW','YOURI',?,'now')",(rows[0],))
        self.assertEqual(self.conn.execute('SELECT COUNT(*) FROM SavedSpecialEncounterCompletions').fetchone()[0],2)

    def test_reset_clears_progress_but_does_not_downgrade(self):
        create_legacy(self.conn)
        self.conn.execute('PRAGMA user_version = 1')
        self.conn.execute("INSERT INTO SavedRelationshipValues VALUES ('LAW','YOURI','TRUST',61)")
        self.conn.execute("INSERT INTO SavedMemoryOccurrences (MemoryKey,ActorCharacterId,PlayerCharacterId,Severity,KnowledgeLevel,OccurredUtc) VALUES ('DEFEAT','LAW','LAW','MAJOR','QUIET','now')")
        self.conn.commit()
        self.conn.execute('BEGIN')
        for table in ['SavedRelationshipValues','SavedCharacterStatusOverrides',
                      'InteractionHistory','SavedSpecialEncounterCompletions','SavedMemoryOccurrences']:
            self.conn.execute(f'DELETE FROM {table}')
        self.conn.commit()
        self.assertEqual(self.conn.execute('PRAGMA user_version').fetchone()[0],1)
        self.assertEqual(self.conn.execute('SELECT COUNT(*) FROM SavedRelationshipValues').fetchone()[0],0)
        self.assertEqual(self.conn.execute('SELECT COUNT(*) FROM SavedMemoryOccurrences').fetchone()[0],0)
        validate_layout(self.conn)

    def test_schema_creation_is_centralised(self):
        for name in ['SqliteRelationshipStore.cs','SqliteRelationshipStatusStore.cs',
                     'SqliteMemoryStore.cs','SqliteSpecialEncounterCompletionStore.cs',
                     'SaveGameProgress.cs']:
            text = (ROOT/'Game/Persistence'/name).read_text(encoding='utf-8')
            self.assertNotIn('EnsureCreated()', text, name)
            self.assertNotIn('CREATE TABLE ', text, name)
        for name in ['InteractionScene.cs','ConversationSelectionScene.cs']:
            text = (ROOT/'Game/Scenes'/name).read_text(encoding='utf-8')
            self.assertIn('SaveGameSchemaManager.EnsureCurrent()', text)

if __name__ == '__main__':
    unittest.main(verbosity=2)
