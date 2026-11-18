"""Contract tests for the exact CREATE/INSERT SQL in the completion store.

Run from your SoulCaliber project root: python Tests/EncounterCompletionSqliteTests/run.py
Uses an in-memory SQLite database; never touches the actual savegame.
This is SQL-only validation, not execution of Godot's C# completion store.
"""
from pathlib import Path
import sqlite3
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tests" / "Regression"))
from production_sql import savegame_extra_schema, store_command

# Since Phase 13G-5B, the schema is owned by SaveGameSchemaManager, not
# SqliteSpecialEncounterCompletionStore. Keep the SQL contract tied to the
# production code while avoiding brittle assumptions about local variable names.
schema = savegame_extra_schema(ROOT)
insert = store_command(ROOT, "SqliteSpecialEncounterCompletionStore.cs",
                       "INSERT INTO SavedSpecialEncounterCompletions")

c = sqlite3.connect(':memory:')
c.execute('PRAGMA foreign_keys=ON')
c.executescript(schema)
# Seed valid memory rows under the production NOT NULL/CHECK constraints.
# The old test used an Id-only stub, which is no longer the actual schema.
def seed_memory(memory_id):
    c.execute("""INSERT INTO SavedMemoryOccurrences
        (Id,MemoryKey,ActorCharacterId,PlayerCharacterId,Severity,KnowledgeLevel,OccurredUtc)
        VALUES(?, 'DEFEATED', 'LAW', 'LAW', 'MAJOR', 'QUIET', '2026-11-17T17:00:00Z')""",
        (memory_id,))

for memory_id in (1, 2, 3):
    seed_memory(memory_id)

def complete(key, initiator, recipient, memory=None, utc='2026-11-17T17:00:00Z'):
    return c.execute(insert, {'encounter': key, 'initiator': initiator,
                              'recipient': recipient, 'memory': memory, 'utc': utc}).rowcount

def rows():
    return c.execute('SELECT EncounterKey,InitiatorCharacterId,RecipientCharacterId,MemoryOccurrenceId '
                     'FROM SavedSpecialEncounterCompletions ORDER BY Id').fetchall()

def must_fail(fn, name):
    try:
        fn()
    except sqlite3.IntegrityError:
        print('PASS', name)
        return
    raise AssertionError(name + ': expected integrity error')

assert complete('PAIR', 'LAW', 'YOURI') == 1
assert complete('pair', 'law', 'youri') == 0
print('PASS once-per-pair uniqueness (case-insensitive)')
assert complete('PAIR', 'YOURI', 'LAW') == 1
print('PASS directional pairs are distinct')
assert complete('DEFEATED', 'LAW', 'YOURI', 1) == 1
assert complete('DEFEATED', 'LAW', 'YOURI', 1) == 0
assert complete('DEFEATED', 'LAW', 'YOURI', 2) == 1
assert [r[3] for r in rows() if r[0] == 'DEFEATED'] == [1, 2]
print('PASS exact memory anchoring and separate occurrences')
prior = len(rows())
must_fail(lambda: complete('DEFEATED', 'LAW', 'YOURI', 999), 'missing memory anchor fails')
must_fail(lambda: complete('PAIR', 'LAW', 'LAW'), 'invalid same-character pair fails')
must_fail(lambda: complete('BAD', 'LAW', 'YOURI', None, None), 'invalid completion timestamp fails')
assert len(rows()) == prior
print('PASS invalid writes never silently create or suppress completions')
c.commit()
# DELETE plus memory cleanup in one explicit reset transaction.
c.execute('BEGIN')
c.execute('DELETE FROM SavedSpecialEncounterCompletions')
c.execute('DELETE FROM SavedMemoryOccurrences')
c.commit()
assert not rows()
print('PASS new-game reset clears completion state')
# A failed transaction should not leave completed history behind.
seed_memory(4)
c.commit()
try:
    c.execute('BEGIN')
    complete('DEFEATED', 'LAW', 'YOURI', 4)
    complete('DEFEATED', 'LAW', 'YOURI', 404)
except sqlite3.IntegrityError:
    c.rollback()
assert not rows()
print('PASS transaction rollback leaves no partial completion')
print('All SQLite completion-contract checks passed')
