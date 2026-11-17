"""Fail if shared Studio/Godot rules silently diverge.

This is a text-parity check, not a proof that the binaries behave identically.
Keep both implementations in lockstep until moved to a shared assembly.
"""
from pathlib import Path
import difflib
import sys

root = Path(__file__).resolve().parents[2]
studio = root / "Editor" / "Studio"
godot = root / "Game" / "Dialogue"
modules = ["MemoryConditionRules.cs", "ChanceFormula.cs",
           "ConditionalDialogueRules.cs", "PersonalityEffectRules.cs"]


def canonical(text: str) -> str:
    # Known harmless qualifier introduced in Godot during the terminology patch.
    # Don't erase other differences: they should be reviewed explicitly.
    return text.replace("System.Environment.NewLine", "Environment.NewLine").replace("\r\n", "\n")


def main() -> int:
    missing = [str(path) for name in modules for path in (studio / name, godot / name) if not path.is_file()]
    if missing:
        print("FAIL missing source file(s):\n" + "\n".join(missing), file=sys.stderr)
        return 1
    failed = False
    for name in modules:
        left = canonical((studio / name).read_text(encoding="utf-8"))
        right = canonical((godot / name).read_text(encoding="utf-8"))
        if left == right:
            print("PASS shared rule parity: " + name)
        else:
            failed = True
            print("FAIL shared rule drift: " + name, file=sys.stderr)
            print("".join(difflib.unified_diff(left.splitlines(True), right.splitlines(True),
                  fromfile="Studio/" + name, tofile="Godot/" + name)), file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
