"""Contract tests for the exact CREATE/INSERT SQL in the completion store.

Run from your SoulCaliber project root: python Tests/EncounterCompletionSqliteTests/run.py
Uses an in-memory SQLite database; never touches the actual savegame.
This is SQL-only validation, not execution of Godot's C# completion store.
"""
from pathlib import Path
import re
import sqlite3

source = Path(__file__).resolve().parents[2] / 'Game' / 'Persistence' / 'SqliteSpecialEncounterCompletionStore.cs'
text = source.read_text(encoding='utf-8')

def extract(pattern):
    match = re.search(pattern, text, flags=re.S)
    if match is None:
        raise RuntimeError(f'SQL was not found in {source.name}: {pattern}')
    return match.group(1)

schema = extract(r'command\.CommandText\s*=\s*@"(\s*CREATE TABLE IF NOT EXISTS SavedSpecialEncounterCompletions.*?);\s*";')
insert = extract(r'command\.CommandText\s*=\s*@"(\s*INSERT INTO SavedSpecialEncounterCompletions.*?ON CONFLICT DO NOTHING)";')

c = sqlite3.connect(':memory:')
c.execute('PRAGMA foreign_keys=ON')
c.executescript('CREATE TABLE SavedMemoryOccurrences (Id INTEGER PRIMARY KEY AUTOINCREMENT);')
c.executescript(schema)
c.executemany('INSERT INTO SavedMemoryOccurrences(Id) VALUES(?)', [(1,), (2,), (3,)])

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
c.execute('INSERT INTO SavedMemoryOccurrences(Id) VALUES (4)')
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
