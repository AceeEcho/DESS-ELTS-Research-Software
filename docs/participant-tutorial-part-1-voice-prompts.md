# Participant tutorial — Part 1 voice prompts

Voice: Eryn - Genuine, Friendly and Natural (`kdnRe2koJdOK4Ovxn2DI`).
Model: `eleven_v4`.
Date: October 30, 2026.

Going forward, follow [the audio workflow](participant-tutorial-audio-workflow.md): exactly two takes per node, Top (take 1) and Bottom (take 2). After the user chooses, download the selected audio and link its organized local file in the script. The user selected the top take for all three chunks on October 30, 2026; the files below are saved locally.

Three separate clips, with four takes requested per clip. Generation was submitted through the ElevenLabs plugin; completion and playback are shown in its generation views.

Delivery follows the model's prompting guide: short spoken sentences, natural punctuation, sparse delivery tags, and a brief pause between thoughts. Tags direct the voice; they are not caption text. Narration wording matches the working script. No speed change, whispered delivery, sound effects, or music was requested.

## Part 1 — Welcome

```text
[warmly] [calmly] Welcome to the ELTS Project Research Study. Thank you for being a participant. Before the study begins, we'll guide you through equipment setup and practice.
```

Welcome revision: restored `[warmly] [calmly]` to match Parts 1.1 and 1.2, replacing the conversational/confident tags. Keep normal sentence pauses and a clear speaking tone. One replacement take requested; Parts 1.1 and 1.2 retain their existing generations.

## Part 1.1 — Guidance and questions

```text
[warmly] [calmly] We'll explain each step as you go. [short pause] If you have a question or need an instruction repeated, please ask the operator.
```

## Part 1.2 — Ready for setup

```text
[warmly] [calmly] When you're ready, let the operator know. [short pause] We'll begin with equipment setup.
```

## Generation references

Flow: `xup5jSg7uW0BxA3jqKBp`.

| Chunk | Speech node |
| --- | --- |
| 1 (warm welcome) | `imA5id64N659sp3NGt6s` |
| 1.1 | `HpiJuuMILk84PxZydIyu` |
| 1.2 | `CiWBZ0miomSBaxmz1CGn` |

Original welcome node, retained for comparison: `ARpySyPlqUnlVSvCRGmi`.

## Selected audio files

All selections use Eryn - Genuine, Friendly and Natural (`kdnRe2koJdOK4Ovxn2DI`) and `eleven_v4`. Exact prompts are recorded above. File paths below are relative to the repository root. Downloads preserve the original MP3 bytes.

| Chunk | User selection | Selected generation ID | Saved audio file |
| --- | --- | --- | --- |
| 1 | Top; latest warm welcome revision (single take) | `YkjT5umFZVD62JNcQz7o` | `docs/participant-tutorial/audio/part-01/01-welcome.mp3` |
| 1.1 | Top; take 1 of original four | `nreNqUoUjc2vMkj44onM` | `docs/participant-tutorial/audio/part-01/01-01-guidance-and-questions.mp3` |
| 1.2 | Top; take 1 of original four | `iNRTQhXL7rHtJ5Huef9Z` | `docs/participant-tutorial/audio/part-01/01-02-ready-for-setup.mp3` |

The original welcome and its intermediate conversational/confident revision are superseded. Unselected remote takes are retained. Do not substitute an older welcome for the selected warm revision.

Listen before choosing final takes. Generated delivery has not been independently auditioned here. Keep the operator-controlled gaps and transitions in the tutorial player rather than encoding them as long silences in the voice clips.
