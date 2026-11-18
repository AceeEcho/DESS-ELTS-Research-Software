"""Keep the readable tutorial script in sync with the detailed source."""
from __future__ import annotations

import argparse
import json
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


RESOURCE_DIR = ROOT / "unity/Assets/ELTS/Operator/Resources"
CAPTIONS = RESOURCE_DIR / "ELTS/ParticipantTutorialCaptions.json"


def render_captions():
    """Use the exact spoken text for each selected recording, including substeps."""
    clips = json.loads((RESOURCE_DIR / "ELTS/ParticipantTutorialAudio.json").read_text(encoding="utf-8"))
    source = SOURCE.read_text(encoding="utf-8")
    blocks = re.split(r"(?m)^#{2,3} Part ", source)[1:]
    captions = {}
    for block in blocks:
        number = re.match(r"[\d.]+", block).group().rstrip(".")
        key = "-".join(part.zfill(2) for part in number.split("."))
        spoken = re.search(r"(?m)^\*\*Spoken:\*\* (.+)$", block)
        audio = re.search(r"(?m)^\*\*Audio:\*\* .*?\]\(([^)]+\.mp3)\)", block)
        if key in captions or not spoken or not audio or key not in clips:
            raise ValueError(f"Missing or duplicate caption or selected clip for {key}")
        linked = (SOURCE.parent / audio.group(1)).resolve()
        if not linked.is_file() or linked != (RESOURCE_DIR / (clips[key] + ".mp3")).resolve():
            raise ValueError(f"Script and selected audio disagree for {key}")
        captions[key] = spoken.group(1).strip().strip('“”')
    if captions.keys() != clips.keys():
        raise ValueError("The narration manifest and detailed script contain different steps.")
    return json.dumps(captions, ensure_ascii=False, indent=2) + "\n"


def check_reading_script() -> None:
    """Used by repository documentation checks to catch a stale readable copy."""
    if not CAPTIONS.is_file() or CAPTIONS.read_text(encoding="utf-8") != render_captions():
        raise ValueError("Tutorial captions need updating. Run tools/build/export_participant_tutorial.py")
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
        CAPTIONS.write_text(render_captions(), encoding="utf-8", newline="\n")
        OUTPUT.write_text(render_reading_script(), encoding="utf-8", newline="\n")
        print("Updated docs/participant-tutorial/reading-script.md")


if __name__ == "__main__":
    main()
