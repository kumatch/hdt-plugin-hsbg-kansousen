r"""Check that the analysis CLI (hsbg-kansousen) has the current log contract of this plugin.

The log format belongs to this repository (docs/log-schema.md, schema/game-log.schema.json);
hsbg-kansousen keeps copies of both and reads the logs. After changing the log format, update the
copies there in the same piece of work (CLAUDE.md "ログの形を変えたとき").

What is checked:
  1. hsbg-kansousen's hsbg/game-log.schema.json == schema/game-log.schema.json (as JSON)
  2. hsbg-kansousen's docs/log-schema.md == docs/log-schema.md (line endings ignored)
  3. GameLog.CurrentSchemaVersion has an entry in docs/log-schema.md's change history

Where hsbg-kansousen is: --cli <folder>, else $HSBG_KANSOUSEN_DIR, else a folder named
hsbg-kansousen next to this repository. When it is not there, 1 and 2 are skipped with a warning
(exit 0), unless --strict is given (exit 1).

Usage (Python 3.12, standard library only; run from the repository root):
  python scripts\check_cli_sync.py [--strict] [--cli <folder>]
Exit code 1 when something differs. Paths are printed relative to this repository only.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = Path("schema/game-log.schema.json")
DOC = Path("docs/log-schema.md")
GAME_LOG_CS = Path("plugin/HsbgKansousen/Model/GameLog.cs")
CLI_SCHEMA = Path("hsbg/game-log.schema.json")
CLI_DOC = Path("docs/log-schema.md")


def _show(path: Path) -> str:
    """A path for messages: relative to this repository, never the absolute one."""
    try:
        return Path(os.path.relpath(path.resolve(), ROOT)).as_posix()
    except ValueError:  # another drive
        return "<hsbg-kansousen>/" + path.name


def _find_cli(arg: str | None) -> Path | None:
    # an explicit --cli / $HSBG_KANSOUSEN_DIR is used as given (no fallback to another folder)
    given = arg or os.environ.get("HSBG_KANSOUSEN_DIR")
    candidate = Path(given) if given else ROOT.parent / "hsbg-kansousen"
    return candidate if (candidate / CLI_SCHEMA).is_file() else None


def _text(path: Path) -> str:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n")


def check_version() -> list[str]:
    m = re.search(r'CurrentSchemaVersion\s*=\s*"(\d+\.\d+\.\d+)"', _text(ROOT / GAME_LOG_CS))
    if not m:
        return [f"{GAME_LOG_CS.as_posix()}: CurrentSchemaVersion not found"]
    if f"`{m.group(1)}`" not in _text(ROOT / DOC):
        return [
            f"{DOC.as_posix()}: no change history entry for schema {m.group(1)}"
            " (GameLog.CurrentSchemaVersion)"
        ]
    return []


def check_cli(cli: Path) -> list[str]:
    errors = []
    ours = json.loads(_text(ROOT / SCHEMA))
    theirs = json.loads(_text(cli / CLI_SCHEMA))
    if ours != theirs:
        errors.append(f"{_show(cli / CLI_SCHEMA)} differs from {SCHEMA.as_posix()}: copy it over")
    if not (cli / CLI_DOC).is_file() or _text(cli / CLI_DOC) != _text(ROOT / DOC):
        errors.append(f"{_show(cli / CLI_DOC)} differs from {DOC.as_posix()}: copy it over")
    return errors


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--cli", help="the hsbg-kansousen folder")
    ap.add_argument("--strict", action="store_true", help="fail when hsbg-kansousen is not found")
    args = ap.parse_args()

    errors = check_version()
    cli = _find_cli(args.cli)
    if cli is None:
        msg = "hsbg-kansousen not found (--cli, $HSBG_KANSOUSEN_DIR or ../hsbg-kansousen)"
        if args.strict:
            errors.append(msg)
        else:
            print(f"warning: {msg}; skipped the copy checks")
    else:
        errors += check_cli(cli)
    for e in errors:
        print(f"NG {e}")
    if errors:
        # ASCII only: the Windows console may not show Japanese
        print("Also update how hsbg-kansousen reads the logs (see CLAUDE.md on log format changes).")
        return 1
    print("check_cli_sync: ok" + ("" if cli else " (version only)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
