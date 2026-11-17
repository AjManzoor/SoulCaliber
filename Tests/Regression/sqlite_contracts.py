"""In-memory contract tests using SQL extracted from the real game stores.

This runs real production CREATE/INSERT/SELECT SQL on Python's SQLite, but DOES
NOT instantiate or execute the C# EF Core/Godot save services. C# integration
and knowledge-distribution behaviour still require local .NET/Godot tests.
"""
from pathlib import Path
import re
import sqlite3
import sys

ROOT = Path(__file__).resolve().parents[2]
MEMORY = ROOT / "Game" / "Persistence" / "SqliteMemoryStore.cs"
COMPLETION = ROOT / "Game" / "Persistence" / "SqliteSpecialEncounterCompletionStore.cs"


def extract(source: str, expression: str) -> str:
    match = re.search(expression, source, re.S)
    if match is None:
        raise AssertionError("Production SQL pattern missing: " + expression)
    return match.group(1)


def must_fail(action, label):
    try:
        action()
    except sqlite3.IntegrityError:
        print("PASS " + label)
        return
    raise AssertionError("Expected integrity failure: " + label)


def main():
    mem_text = MEMORY.read_text(encoding="utf-8")
    complete_text = COMPLETION.read_text(encoding="utf-8")
    mem_schema = extract(mem_text, r'create\.CommandText\s*=\s*@"(\s*CREATE TABLE IF NOT EXISTS SavedMemoryOccurrences.*?);\s*";')
    complete_schema = extract(complete_text, r'command\.CommandText\s*=\s*@"(\s*CREATE TABLE IF NOT EXISTS SavedSpecialEncounterCompletions.*?);\s*";')
    mem_insert = extract(mem_text, r'insert\.CommandText\s*=\s*@"(\s*INSERT INTO SavedMemoryOccurrences.*?\))\s*";')
    complete_insert = extract(complete_text, r'command\.CommandText\s*=\s*@"(\s*INSERT INTO SavedSpecialEncounterCompletions.*?ON CONFLICT DO NOTHING)";')
    mem_find = extract(mem_text, r'query\.CommandText\s*=\s*@"(\s*SELECT m\.Id,m\.MemoryKey.*?ORDER BY m\.Id\s*)";')

    connection = sqlite3.connect(":memory:", isolation_level=None)
    connection.execute("PRAGMA foreign_keys=ON")
    connection.executescript(mem_schema)
    connection.executescript(complete_schema)

    def add_memory(tag="DEFEAT", actor="LAW", player="LAW", severity="MAJOR", level="QUIET"):
        cursor = connection.execute(mem_insert, {"memory": tag, "actor": actor,
            "player": player, "severity": severity, "knowledge": level,
            "utc": "2026-11-17T00:00:00Z"})
        return cursor.lastrowid

    def affect(memory_id, character):
        connection.execute("INSERT INTO SavedMemoryAffectedCharacters(MemoryId,CharacterId) VALUES (?,?)",
                           (memory_id, character))

    def know(memory_id, character):
        connection.execute("INSERT INTO SavedMemoryKnowledge(MemoryId,CharacterId) VALUES (?,?)",
                           (memory_id, character))

    def find(tag=None, actor=None, affected=None, knower=None):
        return connection.execute(mem_find, {"memory": tag, "actor": actor,
            "affected": affected, "knower": knower}).fetchall()

    quiet = add_memory()
    affect(quiet, "YOURI")
    know(quiet, "LAW")
    know(quiet, "YOURI")
    assert len(find(tag="DEFEAT", actor="LAW", affected="YOURI", knower="YOURI")) == 1
    assert len(find(tag="DEFEAT", actor="LAW", affected="YOURI", knower="NOBUNOGI")) == 0
    assert len(find(tag="DEFEAT", actor="LAW", affected="YOURI", knower=None)) == 1
    print("PASS exact memory actor/affected/knower filters and ANY knowledge")

    global_id = add_memory(actor="THORN", level="GLOBAL")
    affect(global_id, "NOBUNOGI")
    assert len(find(tag="DEFEAT", actor="THORN", knower="TINA")) == 1
    assert connection.execute("SELECT COUNT(*) FROM SavedMemoryKnowledge WHERE MemoryId=?", (global_id,)).fetchone()[0] == 0
    print("PASS GLOBAL is knowable without explicit knowledge rows")

    distinct = add_memory()
    affect(distinct, "YOURI")
    know(distinct, "YOURI")
    assert len(find(tag="DEFEAT", actor="LAW", affected="YOURI")) == 2
    print("PASS repeated incidents remain separate occurrences")

    must_fail(lambda: affect(quiet, "youri"), "duplicate affected character prevented case-insensitively")
    must_fail(lambda: know(quiet, "lAw"), "duplicate knower prevented case-insensitively")
    must_fail(lambda: add_memory(severity="UNKNOWN"), "invalid severity rejected")
    must_fail(lambda: add_memory(level="UNKNOWN"), "invalid knowledge level rejected")

    def complete(key, initiator, recipient, memory=None):
        return connection.execute(complete_insert, {"encounter": key, "initiator": initiator,
            "recipient": recipient, "memory": memory, "utc": "2026-11-17T00:00:00Z"}).rowcount

    assert complete("DEFEATED", "LAW", "YOURI", quiet) == 1
    assert complete("defeated", "law", "youri", quiet) == 0
    assert complete("DEFEATED", "LAW", "YOURI", distinct) == 1
    assert complete("DEFEATED", "YOURI", "LAW", quiet) == 1
    print("PASS completion uniqueness, directional pairs and distinct anchored memories")
    must_fail(lambda: complete("DEFEATED", "LAW", "YOURI", 999999), "orphaned completion anchor rejected")

    # Real production memory and completion tables are connected through FK cascade.
    connection.execute("DELETE FROM SavedMemoryOccurrences WHERE Id=?", (quiet,))
    assert connection.execute("SELECT COUNT(*) FROM SavedMemoryAffectedCharacters WHERE MemoryId=?", (quiet,)).fetchone()[0] == 0
    assert connection.execute("SELECT COUNT(*) FROM SavedMemoryKnowledge WHERE MemoryId=?", (quiet,)).fetchone()[0] == 0
    assert connection.execute("SELECT COUNT(*) FROM SavedSpecialEncounterCompletions WHERE MemoryOccurrenceId=?", (quiet,)).fetchone()[0] == 0
    print("PASS memory deletion cascades to affected/knowledge/completion rows")

    # Failure injection using the genuine insert SQL. These checks validate the
    # transaction boundary in SQLite, not EF's CommitResolvedChoice implementation.
    initial = connection.execute("SELECT COUNT(*) FROM SavedMemoryOccurrences").fetchone()[0]
    connection.execute("BEGIN")
    try:
        temporary = add_memory(tag="BETRAYAL")
        affect(temporary, "YOURI")
        know(temporary, "LAW")
        add_memory(level="INVALID")
    except sqlite3.IntegrityError:
        connection.execute("ROLLBACK")
    else:
        raise AssertionError("Failure injection did not fail")
    assert connection.execute("SELECT COUNT(*) FROM SavedMemoryOccurrences").fetchone()[0] == initial
    assert len(find(tag="BETRAYAL")) == 0
    print("PASS SQLite transaction rolls back partial memory and knowledge inserts")

    assert connection.execute("PRAGMA foreign_key_check").fetchall() == []
    print("PASS foreign-key integrity check")
    print("All SQLite production-SQL contract tests passed.")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("FAIL " + str(exc), file=sys.stderr)
        raise
