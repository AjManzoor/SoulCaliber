"""Read the actual SQL embedded in the runtime C# source for SQLite contract tests.

SaveGameSchemaManager is the sole owner of savegame CREATE TABLE statements.
The stores own their INSERT and SELECT commands. A missing/moved SQL statement
is a test error (not something we silently replace with hand-written SQL).
"""

from pathlib import Path
import re

# Support C# verbatim strings, including escaped double-quotes inside them.
_VERBATIM = r'@"(?P<sql>(?:[^"]|"")*)"'


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def _extract_one(source: str, expression: str, label: str) -> str:
    found = re.search(expression, source, re.DOTALL)
    if found is None:
        raise AssertionError(f"Cannot extract {label} from production source")
    return found.group("sql").replace('""', '"').strip()


def savegame_extra_schema(root: Path) -> str:
    path = root / "Game" / "Persistence" / "SaveGameSchemaManager.cs"
    return _extract_one(
        _read(path), r"\bExtraSchemaSql\s*=\s*" + _VERBATIM,
        f"savegame schema in {path}",
    )


def savegame_audience_v2_migration(root: Path) -> str:
    path = root / "Game" / "Persistence" / "SaveGameSchemaManager.cs"
    source = _read(path)
    pattern = (r"\bExecute\s*\(\s*connection\s*,\s*transaction\s*,\s*" +
               _VERBATIM + r"\s*\)")
    matches = [m.group("sql").replace('""', '"').strip()
               for m in re.finditer(pattern, source, re.DOTALL)]
    matches = [sql for sql in matches if re.search(
        r"\bCREATE\s+TABLE\s+SavedMemoryAudienceModes\b", sql, re.IGNORECASE)]
    if len(matches) != 1:
        raise AssertionError(f"Expected one production SavedMemoryAudienceModes migration in {path}; found {len(matches)}")
    return matches[0]


def store_command(root: Path, file_name: str, statement_prefix: str) -> str:
    path = root / "Game" / "Persistence" / file_name
    source = _read(path)
    pattern = r"\b[A-Za-z_]\w*\.CommandText\s*=\s*" + _VERBATIM + r"\s*;"

    def normalise(sql: str) -> str:
        return " ".join(sql.split()).casefold()

    prefix = normalise(statement_prefix)
    matches = [m.group("sql").replace('""', '"').strip()
               for m in re.finditer(pattern, source, re.DOTALL)
               if normalise(m.group("sql")).startswith(prefix)]
    if len(matches) != 1:
        raise AssertionError(f"Expected one {statement_prefix!r} command in {path}; found {len(matches)}")
    return matches[0]
