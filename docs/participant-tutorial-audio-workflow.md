# Participant tutorial audio workflow

User-agreed workflow, October 30, 2026. Follow this for future participant tutorial narration work.

Current stage, November 2, 2026: drafting now covers Parts 1–11. The user requested audio for Parts 2 and 3 only: the selected takes are now downloaded and linked in the script. Preserve the selected Part 1 files. Do not generate Parts 4–11 until requested.

## Source files and delivery

- Read `participant-tutorial-script-outline.md` for context. Keep detailed script edits in the working script; revise the outline only when the user requests or authorizes a sequence change, as with the head-tracker, holding, and gun-tracker sequence.
- Edit narration and visual directions in `participant-tutorial-script.md`.
- Keep [the reading script](participant-tutorial-reading-script.md) synchronized using the process below. It contains narration and ordinary action/wait cues, without scene-building directions or audio-production details.
- Keep exact generation prompts and generation references in the matching voice-prompts document. Part 1 uses `participant-tutorial-part-1-voice-prompts.md`.
- Use Eryn - Genuine, Friendly and Natural, voice ID `kdnRe2koJdOK4Ovxn2DI`, and `eleven_v4`, unless the user changes these choices. Confirm voice identity through the voice tool when needed; do not substitute another Eryn variant.
- Keep delivery consistent across chunks. Current prompts use `[warmly] [calmly]`, natural punctuation, and occasional `[short pause]` tags. Preserve the spoken wording unless a revision is requested. Do not put screen directions or production notes into speech prompts.

## Keep the readable and detailed scripts together

The detailed script is the source of truth for both versions. Every numbered chunk has one **Spoken** line and one concise **Action / wait** line. Edit those lines there, keeping the concise cue consistent with the detailed transition, then run from the repository root:

```text
py -3 tools/build/export_participant_tutorial.py
```

The export updates `docs/participant-tutorial-reading-script.md` with only headings, narration, and human-readable cues. It does not generate audio or run the tutorial. Do not edit the exported copy separately; apply requested wording changes to the detailed script and regenerate it in the same change.

Verify synchronization with `py -3 tools/build/export_participant_tutorial.py --check`. Repository documentation checks also reject an out-of-date reading script. On systems without the Windows Python launcher, use `python` instead of `py -3`. Keep the script, outline, audio mapping, and reading copy aligned when adding, removing, or renumbering chunks.

## Generate and select

1. Generate **exactly two takes per narration node**: explicitly set `generations_count: 2`. A node represents one numbered spoken chunk, such as Part 1.1. On a revision, regenerate only the requested chunk.
2. Present the pair in a stable order and identify them as **Top (take 1)** and **Bottom (take 2)**. If the player uses a pager instead of a vertical pair, explain that take 1 is Top and take 2 is Bottom. Record the corresponding generation IDs and node ID in the voice-prompts document so a selection can be resolved later.
3. The user chooses by saying, for example, “Keep the top one for 1.1.” Treat top/bottom as referring to that particular pair, not to the order across unrelated nodes or old revisions. Do not guess if the selected chunk or pair is ambiguous; resolve the identity before downloading.
4. A choice authorizes downloading and organizing that selected take. Do not generate another take, change the voice, or ask for approval again. Leave unselected takes in ElevenLabs unless the user asks to delete them.
5. Follow the plugin's generation-view instructions. The view tracks completion itself; do not poll when instructed not to. A generation-start response does not prove that audio is finished.

## Save the selected audio

Download the completed take's original audio file into this repository, under:

```text
docs/participant-tutorial/audio/part-01/
```

Use the chunk number and a short, meaningful description. Part 1's planned filenames are:

| Script chunk | Filename stem |
| --- | --- |
| Part 1 | `01-welcome` |
| Part 1.1 | `01-01-guidance-and-questions` |
| Part 1.2 | `01-02-ready-for-setup` |

Keep the actual source extension: for example, `01-welcome.mp3` for an MP3, or `01-welcome.wav` for a WAV. Do not rename one audio format as another or transcode without a reason. For later parts, use `part-02/`, `part-03/`, and the same numbering convention.

If downloading creates a temporary local file first, move it into the final folder and verify the destination. Prefer a direct download to the final destination when practical. Check that the file is nonempty and recognizable as audio; check its duration or playback when supported. Do not mark it saved based only on a remote link or a successful generation request.

If replacing a previously selected local take, preserve the prior file in an `archive/` subfolder using the filename stem plus its generation ID before updating the active file. Keep the active filename stable for agents implementing the tutorial. Do not erase prior selections or unrelated files.

## Label the script and record the selection

Immediately below the matching **Spoken** paragraph, maintain an **Audio** line containing the status and a clickable repository-relative link to the saved file. Example for an actual saved MP3:

```markdown
**Audio:** Selected — [01-welcome.mp3](participant-tutorial/audio/part-01/01-welcome.mp3). Use for this spoken chunk only.
```

Before a selection is downloaded, label it **Pending selection** or **Selected; download pending** as appropriate. Do not create a link that suggests a nonexistent file is ready. Pending script entries list a planned filename stem without an extension until the audio format is known.

In the voice-prompts document, record the chosen Top/Bottom take, generation ID, node ID, exact prompt, voice, model, and final repository-relative file path. Store stable IDs rather than temporary signed download URLs or account-switch links. Keep prior revisions clearly labeled as superseded.

When handing off to an implementation agent, the script's Audio link is the authoritative mapping between a spoken chunk and its selected file. Play the file once for that chunk; keep operator-controlled waits and transitions in the player rather than adding long silence to the recording. Screen directions stay in the script and are not spoken.

## Current state

Earlier test generations used four takes per initial node and single takes for welcome revisions. They remain historical tests; this two-take policy applies to future generations. On October 30, 2026, the user selected the top take for Parts 1, 1.1, and 1.2. The latest warm welcome and first takes of the other two chunks are downloaded in `participant-tutorial/audio/part-01/`, linked from the script, and identified in the Part 1 voice-prompts document. Do not regenerate existing clips merely to normalize their historical take counts.

November 2, 2026: Parts 2 and 3 now have per-chunk take/session/node mappings in `participant-tutorial-part-2-voice-prompts.md` and `participant-tutorial-part-3-voice-prompts.md`. The user selected Bottom, Bottom, Bottom, Bottom, Top, Top, Bottom in chunk order 2, 2.1, 2.2, 3, 3.1, 3.2, 3.3. These seven files are downloaded, verified, and linked from the script and the corresponding voice-prompts documents.
