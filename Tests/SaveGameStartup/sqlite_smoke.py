"""Standalone SQLite smoke checks; does not execute the Godot/.NET implementation.
Run the accompanying .NET test runner on Windows for actual production-code tests.
"""
import sqlite3
import tempfile
import unittest
from pathlib import Path


class SQLiteStartupSmokeTests(unittest.TestCase):
    def test_newer_schema_can_be_identified_without_write(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "newer.db"
            with sqlite3.connect(path) as db:
                db.execute("CREATE TABLE Progress (Id INTEGER PRIMARY KEY)")
                db.execute("INSERT INTO Progress VALUES (1)")
                db.execute("PRAGMA user_version=999")
            before = path.read_bytes()
            with sqlite3.connect(f"file:{path}?mode=ro", uri=True) as db:
                self.assertEqual(db.execute("PRAGMA quick_check(1)").fetchone()[0], "ok")
                self.assertEqual(db.execute("PRAGMA user_version").fetchone()[0], 999)
            self.assertEqual(path.read_bytes(), before)

    def test_invalid_database_fails_readonly_check_without_write(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "broken.db"
            path.write_bytes(b"not a sqlite database")
            before = path.read_bytes()
            with self.assertRaises(sqlite3.DatabaseError):
                with sqlite3.connect(f"file:{path}?mode=ro", uri=True) as db:
                    db.execute("PRAGMA quick_check(1)").fetchone()
            self.assertEqual(path.read_bytes(), before)

    def test_missing_save_readonly_check_never_creates_file(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "missing.db"
            with self.assertRaises(sqlite3.OperationalError):
                sqlite3.connect(f"file:{path}?mode=ro", uri=True)
            self.assertFalse(path.exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
