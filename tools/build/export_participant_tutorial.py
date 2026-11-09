"""Keep the readable tutorial script in sync with the detailed source."""
from __future__ import annotations

import argparse
from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs/participant-tutorial/script.md"
OUTPUT = ROOT / "docs/participant-tutorial/reading-script.md"


def render_reading_script() -> str:
    """Copy narration and short operator cues, leaving out production details."""
    source = SOURCE.read_text(encoding="utf-8")
    blocks = re.finditer(
        r"^(#{2,3} Part [\d.]+[^\n]*)\n(.*?)(?=^#{2,3} Part |^## Playback|\Z)",
        source,
        re.MULTILINE | re.DOTALL,
    )
    lines = [
        "# Participant tutorial — reading script",
        "",
        "Narration and action/wait cues only. Cues are not spoken. This is the normal session flow; the operator handles exceptions.",
        "",
        "This copy is kept in sync with the [detailed script](script.md).",
        "",
    ]
    count = 0
    for block in blocks:
        heading, body = block.groups()
        spoken = re.findall(r"^\*\*Spoken:\*\* (.+)$", body, re.MULTILINE)
        cues = re.findall(r"^\*\*Action / wait:\*\* (.+)$", body, re.MULTILINE)
        # Fail clearly if a new chunk is incomplete instead of silently omitting it.
        if len(spoken) != 1 or len(cues) != 1:
            raise ValueError(f"{heading}: needs exactly one Spoken and Action / wait line")
        lines.extend([heading, "", spoken[0], "", "**Cue:** " + cues[0], ""])
        count += 1
    if count == 0:
        raise ValueError("No numbered tutorial chunks found")
    return "\n".join(lines)


def check_reading_script() -> None:
    """Used by repository documentation checks to catch a stale readable copy."""
    if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != render_reading_script():
        raise ValueError(
            "Readable tutorial is out of date. Run: python tools/build/export_participant_tutorial.py"
        )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check without rewriting the readable copy")
    args = parser.parse_args()
    if args.check:
        check_reading_script()
        print("PASS: readable tutorial matches the detailed script")
    else:
        OUTPUT.write_text(render_reading_script(), encoding="utf-8", newline="\n")
        print("Updated docs/participant-tutorial/reading-script.md")


if __name__ == "__main__":
    main()
