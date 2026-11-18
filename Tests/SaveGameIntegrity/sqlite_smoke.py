"""Independent Python SQLite contract checks for Phase 13G-5C.

These checks validate SQLite behaviour and source wiring, NOT compilation or
execution of the new C# integrity checker. Run Program.cs on Windows for that.
"""
import re
import sqlite3
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHECKER = (ROOT/'Game/Persistence/SaveGameIntegrityChecker.cs').read_text(encoding='utf-8')
MANAGER = (ROOT/'Game/Persistence/SaveGameSchemaManager.cs').read_text(encoding='utf-8')
SCENE = (ROOT/'Game/Scenes/ConversationSelectionScene.cs').read_text(encoding='utf-8')
PROGRAM = (ROOT/'Tests/SaveGameIntegrity/Program.cs').read_text(encoding='utf-8')
# Reuse the representative EF fixture from the actual .NET test runner.
SAVE = PROGRAM.split('Exec(save, @"', 1)[1].split('\n            ");', 1)[0]
CONTENT = PROGRAM.split('Exec(content, @"', 1)[1].split('\n            ");', 1)[0]


class Checks(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.save = Path(self.tmp.name)/'save.db'
        self.content = Path(self.tmp.name)/'content.db'
        for path, sql in ((self.save, SAVE), (self.content, CONTENT)):
            db = sqlite3.connect(path)
            db.executescript(sql)
            db.close()

    @staticmethod
    def readonly(path):
        return sqlite3.connect(path.as_uri() + '?mode=ro', uri=True)

    def test_readonly_can_inspect_real_fixture(self):
        with self.readonly(self.save) as db:
            self.assertEqual(db.execute('PRAGMA integrity_check(100)').fetchall(), [('ok',)])
            self.assertEqual(db.execute('PRAGMA foreign_key_check').fetchall(), [])
            self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0], 1)
        with self.readonly(self.content) as db:
            self.assertEqual(db.execute('SELECT Id FROM Characters ORDER BY Id').fetchall(), [('LAW',), ('YOURI',)])

    def test_readonly_rejects_mutations(self):
        with self.readonly(self.save) as db:
            with self.assertRaises(sqlite3.OperationalError):
                db.execute("INSERT INTO SavedRelationshipValues VALUES ('X','Y','TRUST',1)")

    def test_orphan_fk_is_visible_even_when_not_enforced_on_writer(self):
        with sqlite3.connect(self.save) as writer:
            writer.execute('PRAGMA foreign_keys=OFF')
            writer.execute("INSERT INTO SavedMemoryKnowledge VALUES (999,'LAW')")
        with self.readonly(self.save) as db:
            violations = db.execute('PRAGMA foreign_key_check').fetchall()
            self.assertEqual(len(violations), 1)
            self.assertEqual(violations[0][0], 'SavedMemoryKnowledge')

    def test_global_memory_needs_no_knowledge_rows(self):
        with self.readonly(self.save) as db:
            self.assertEqual(db.execute('SELECT KnowledgeLevel FROM SavedMemoryOccurrences').fetchone()[0], 'GLOBAL')
            self.assertEqual(db.execute('SELECT COUNT(*) FROM SavedMemoryKnowledge').fetchone()[0], 0)

    def test_missing_character_can_be_detected_by_content(self):
        with sqlite3.connect(self.save) as writer:
            writer.execute("INSERT INTO SavedMemoryKnowledge VALUES (10, 'MISSING')")
        with self.readonly(self.content) as content, self.readonly(self.save) as save:
            chars = {r[0] for r in content.execute('SELECT Id FROM Characters')}
            unknown = [r[0] for r in save.execute('SELECT CharacterId FROM SavedMemoryKnowledge') if r[0] not in chars]
            self.assertEqual(unknown, ['MISSING'])

    def test_version_check_does_not_migrate(self):
        with sqlite3.connect(self.save) as writer:
            writer.execute('PRAGMA user_version=999')
        before = self.save.read_bytes()
        with self.readonly(self.save) as db:
            self.assertEqual(db.execute('PRAGMA user_version').fetchone()[0], 999)
        self.assertEqual(self.save.read_bytes(), before)

    def test_source_is_readonly_and_no_autorepair(self):
        self.assertIn('Mode = SqliteOpenMode.ReadOnly', CHECKER)
        self.assertNotIn('EnsureCurrent(', CHECKER.replace('SaveGameSchemaManager.EnsureCurrent or', ''))
        self.assertIn('PRAGMA foreign_key_check;', CHECKER)
        self.assertIn('PRAGMA integrity_check(100);', CHECKER)
        self.assertIn('SaveGameIntegrityChecker.Inspect()', SCENE)
        self.assertIn('internal static readonly Dictionary<string, string[]> RequiredColumns', MANAGER)

if __name__ == '__main__':
    unittest.main(verbosity=2)
