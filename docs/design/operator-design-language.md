# Design Language Brief

**For: any AI model building interfaces or systems for me.**
**Read this before you write a single line of UI.**

This document describes how I want things to look, feel, and behave. It is not a
style guide you skim and then ignore — it is the brief. If something you're
about to build conflicts with a rule here, the rule wins unless I've explicitly
overridden it in the current conversation.

I care more about *feel* than about implementation. You can pick the stack. You
cannot pick the interaction model.

---

## 0. The one-paragraph version

Dark, dense, quiet chrome, bright content. Everything is directly manipulable —
if it's on screen and it represents a thing, I should be able to grab it, move
it, and have it stay there. Show me one decision at a time with a persistent
live view of the result. Never hide a feature behind a hover or a right-click.
Never truncate text. Never move the viewport out from under me. Motion exists to
explain what just changed, not to decorate. Build like it's a tool I'll use
every day for a year, not a landing page I'll look at once.

---

## 1. First principles

These generate everything else. When a specific rule doesn't cover your case,
derive from these.

### 1.1 Direct manipulation over forms
The default failure mode of AI-designed software is *the form*: a stack of
labeled fields, a Save button, a list view. I don't want that. If an object has
a position, let me drag it. If a list has an order, let me drag to reorder it.
If a value has a range, give me something to pull. Forms are the fallback for
data that genuinely has no spatial or physical analogue — not the starting
point.

The mental reference isn't enterprise software. It's a **character creator in a
good RPG** (Skyrim, Cyberpunk 2077, The Sims), a **DAW**, a **node editor**, or
a **game launcher**. Those are interfaces people use for hours voluntarily.

### 1.2 One thing at a time, with the whole always visible
Stacking every option on screen at once buries the user. But hiding everything
behind navigation loses the thread. The resolution is almost always
**master–detail**: a persistent index of everything on one side, one focused
decision in the middle, and the live result on the other side. I can see where I
am, what's left, and what I've made — simultaneously — while only being asked to
think about one thing.

### 1.3 Space is memory
Where I put something is information. Layouts persist. Per-surface, per-mode,
per-context. If I drag a tile to the top-left because that's where my brain
expects it, it is *never* allowed to migrate on reload, on resize, or because
the system decided to re-sort. Auto-arrangement is a command I invoke, not a
behavior that happens to me.

### 1.4 The interface should recede
Chrome is quiet, small, low-contrast, and tucked into corners. Content is
bright, large, and centered. A settings gear belongs in the bottom corner at 60%
opacity, not in a persistent top nav bar. Toolbars that are always visible
should be earning their pixels every second.

### 1.5 Predictable beats clever
When there's a choice between a smart behavior and a boring one I can model in
my head, pick boring. A system I can predict is a system I can move fast in. If
you want the clever behavior, make it an explicit action with a name.

### 1.6 Design first, wire later
When building something real, get the look and the interaction completely right
against stubbed data before connecting anything to the outside world. Isolate
every external call into one clearly marked block so the design layer stays
untouched when the plumbing lands. I would rather have a beautiful, fully
interactive thing that does nothing yet than a functional thing I hate touching.

---

## 2. Visual language

### 2.1 Palette

Dark by default. Always. Not "supports dark mode" — designed dark, and if a
light theme exists it's the afterthought.

The reference feel is **Claude's interface and Zen Browser**: soft dark neutrals
with real depth, not flat black, not the near-black-with-one-acid-accent look
that every AI-generated dark UI defaults to.

My canonical token set, which you should use unless the project calls for its
own identity:

| Token | Value | Role |
|---|---|---|
| `bg` | `#16181d` | App background, the floor |
| `panel` | `#1d2027` | Raised surfaces, tiles, cards |
| `panel2` | `#23272f` | Hover states, inputs, the layer above panel |
| `border` | `#2c313b` | Hairlines, separators, tile edges |
| `text` | `#e7e9ee` | Primary text |
| `muted` | `#8b919e` | Secondary text, labels, inactive |
| `accent` | `#00b3ff` | Cloud9 blue — selection, focus, active, primary action |

Rules for the accent:
- **One accent.** Blue carries selection, focus, active state, and primary
  actions. It does not become decoration. If everything is accented, nothing is.
- Accent at full saturation is for *state*, not for surfaces. Large blue fills
  are wrong; blue borders, blue glows, blue text, blue underlines, blue-tinted
  panel backgrounds at ~8–12% are right.
- Semantic colors (destructive red, warning amber, success green) exist but stay
  muted and desaturated enough to live in the dark palette without screaming.

**Object color is allowed and encouraged.** When items represent real things
(apps, tools, categories), give each its own identity color and use it on the
icon, the tile edge, or a glow. That's how a wall of tiles becomes scannable at
a glance instead of a grey grid.

### 2.2 Depth

Depth comes from **layering and borders**, not from drop shadows. The stack is
`bg → panel → panel2`, each step slightly lighter, each with a hairline border.
Shadows, when used at all, are large, soft, and very dark — they suggest
elevation, they don't outline the element.

Blur/glass is welcome for surfaces that float *over* content: mode overlays,
settings panels, popovers, status pills. It should feel like frosted glass with
depth behind it, not a semi-transparent rectangle.

Corner radius: generous but not pill-shaped. Tiles and panels are noticeably
rounded (think 10–16px), inputs and small controls less so. Do not put the same
radius on everything regardless of size — a large panel and a small chip should
not share a value.

### 2.3 Typography

- One family, maybe two. A clean geometric or neutral sans for everything, and a
  mono face only where the content is genuinely code, paths, IDs, or tabular
  numbers. Mono as a "style choice" for small labels is a tell — don't.
- **Sentence case everywhere.** Not Title Case. Not ALL CAPS tracked-out
  eyebrow labels above every section. Those are the single clearest sign a model
  reached for its defaults.
- Real hierarchy through size and weight, not through color alone. Three or four
  steps in the scale is plenty; if you need a fifth, your structure is wrong.

### 2.4 Text is never truncated

This is a hard rule and I will notice immediately.

`text-overflow: ellipsis` and `white-space: nowrap` are banned for any content
the user created or selected. Names, tags, labels, file paths, options — they
**wrap**. If wrapping breaks the layout, the layout is wrong: make the container
flexible, let the row grow, use a line clamp at 2–3 lines if you truly must, but
never cut a name off at one line and call it done. A truncated label is an
unreadable label, and I can't pick between two options I can't read.

Corollary: don't design layouts that only look right with short placeholder
content. Test with the longest realistic string.

### 2.5 Density

Dense, but breathing. This is a tool, not a marketing page — I want a lot on
screen. But dense means *tight, consistent spacing on a rhythm*, not cramped.
Pick a spacing scale and hold it. Whitespace between groups, not inside them.

Avoid the SaaS-card kit: content chopped into identical rounded boxes with
identical padding and identical soft grey shadows, arranged in a 3-column grid.
Card everything and you've encoded nothing.

### 2.6 Icons

Consistent line-icon set, one weight, one size per context. Icons carry meaning
paired with color; they don't replace labels in primary navigation. An icon-only
button is fine for a universally understood action in a compact space (close,
settings, drag handle) and wrong for anything a user has to learn.

---

## 3. Layout archetypes

Pick the one that matches the shape of the problem. Don't invent a fourth
without a reason.

### 3.1 The three-pane workbench

For anything where the user is composing, configuring, or building up a result
from many decisions.

```
┌──────────────┬────────────────────────────────┬──────────────┐
│  RAIL        │  STAGE                         │  LIVE RESULT │
│              │                                │              │
│  Section ▾   │   One attribute, big.          │  ┌────────┐  │
│   item · val │                                │  │preview │  │
│   item · val │   ┌──────┐ ┌──────┐ ┌──────┐   │  └────────┘  │
│   item · val │   │option│ │option│ │option│   │              │
│  Section ▸   │   └──────┘ └──────┘ └──────┘   │  entry       │
│  Section ▸   │                                │  entry       │
│              │   ┌──────┐ ┌──────┐            │  entry       │
│  + add item  │   │option│ │option│            │              │
│              │   └──────┘ └──────┘            │  [ copy ]    │
└──────────────┴────────────────────────────────┴──────────────┘
  where I am           what I'm deciding          what I've made
  + current state      (one thing, readable)      (always visible)
```

Rules:
- The rail shows **every** section and every item, collapsed by section, with
  each item's **current value inline**. I should be able to read my whole
  configuration off the rail without clicking anything.
- The stage shows exactly one item's options, as large tappable cards with full
  readable labels. Moving to the next item is one click, never a
  back-out-and-drill-in round trip.
- The result panel updates live on every change. Never behind a "Preview"
  button.
- The result panel has a sensible default order derived from the structure, plus
  manual override (drag to pin). Automatic ordering that I can't correct is
  worse than no ordering.

### 3.2 The freeform canvas

For collections of objects whose arrangement is meaningful — launchers,
boards, maps, workspaces.

```
┌────────────────────────────────────────────────────────────┐
│                                                            │
│     ( mode )        ( mode )          ( mode )             │
│                                                            │
│    ┌────┐ ┌────┐          ┌────┐                           │
│    │tile│ │tile│          │tile│      ┌────┐               │
│    └────┘ └────┘          └────┘      │tile│               │
│                                       └────┘               │
│               ┌────┐ ┌────┐                                │
│               │tile│ │tile│                                │
│               └────┘ └────┘                                │
│                                                            │
│   ▸ sequence strip                                    ⚙    │
└────────────────────────────────────────────────────────────┘
```

Rules:
- **Infinite in screen space.** Dragging toward an edge pans; there is no wall.
- **Snap to grid by default, hold Shift to break free.** Snapping should feel
  magnetic and smooth, not teleporting.
- Objects are resizable in place where size means something.
- Layout saves per surface. Each mode/context owns its own arrangement.
- Chrome (settings, status, secondary strips) lives in the corners and edges,
  small and dim until hovered.

### 3.3 The focused overlay

For a single decision or a management surface that isn't the main task: settings,
a picker, a confirmation. Full-height panel sliding in from an edge, or a
centered card over a blurred backdrop. Tabbed inside if it manages multiple
object types. Dismissable with Escape and by clicking outside — always both.

---

## 4. Interaction model

### 4.1 Everything is grabbable
Drag to move. Drag to reorder. Drag to reparent. Drag out to remove (with
confirmation). If an element sits in an ordered list and there's no drag handle,
you've made a form. Keyboard equivalents should exist, but the mouse path comes
first.

Drag feedback: the dragged object lifts (scale up slightly, shadow grows), the
drop target indicates itself clearly, and the rest of the layout animates to
make room *before* I release — so I can see the result of the drop while
deciding.

### 4.2 Nothing important is hidden
If a feature exists, there is a visible affordance for it. Not on hover. Not in
a right-click menu. Not in an overflow popover three levels deep.

If I have to ask you "how do I add a new one of these," the design has failed
and the fix is a visible `+ add` control in the place I looked for it. Hover
reveals and context menus are *accelerators for things that are already
visible*, never the only path.

### 4.3 Preserve my place
Any action that changes the underlying data must leave me looking at somewhere
sensible. Delete an item → move to the neighboring item, don't jump to the top.
Save a setting → stay on that panel. Add an object → focus it, don't reset the
scroll.

A correct operation that snaps the viewport somewhere unexpected **reads as a
bug**, and I will report it as one. Losing my place is the same as losing my
work.

### 4.4 Destructive actions must feel safe
Scattered ✕ buttons that instantly delete are hostile — one mis-click on a
crowded screen and something's gone. Preferred pattern: an explicit **removal
mode** that arms deletion across the surface, makes the targets obviously
armed, and disarms after use. Alternatively: undo. Alternatively: confirm on
anything irreversible.

Whatever you pick, delete and drag must never share a gesture, and a delete
control must never sit where a drag handle or a select target is.

### 4.5 Context awareness
The system should notice what I'm doing and surface what's related, quietly.
Launch one thing → related things highlight. Select an object → its relevant
tools appear near it. This should be a subtle highlight or a soft reveal, never
a modal, never a notification, never a jump.

### 4.6 Escape hatches
Every clever composed behavior needs a manual override next to it. If the system
combines things automatically, give me a control that splits them back into
their raw parts. If it sorts automatically, let me pin. If it names things
automatically, let me rename. Smart defaults with a visible escape hatch is the
pattern; smart defaults with no way out is the anti-pattern.

### 4.7 Extensibility without code
For any system with categories, tags, presets, or rules — I should be able to
create, edit, reorder, and delete those definitions from inside the UI. Building
a system where the interesting structure is hardcoded means every change routes
through you, and that's a worse tool.

---

## 5. Motion

Motion's job is to **explain what changed**. Every animation should answer a
question: where did that go, where did this come from, what's related to what.

- **Direct while held, eased when released.** The object under my pointer must
  track it immediately, keeping the original grab point. Use restrained springs
  or short ease-out transitions for surrounding reflow and settling, not to add
  lag between my hand and the thing I'm moving. Continuous playback and progress
  should reflect real time, not an easing curve.
- **Named transitions that carry meaning:** an object animating to center and
  scaling up when it becomes the focus; layout reflowing smoothly when items
  rearrange; a panel sliding in from the edge it lives on; a list growing to
  make room before a drop.
- **Fast.** 150–250ms for most state changes, up to ~400ms for a big spatial
  transition. If I notice I'm waiting, it's too slow.
- **Don't animate on load.** Staggered fade-and-slide-up entrances on every
  section is the single most obvious generated-page tell. The interface is
  already there when I open it.
- **Don't animate everything.** Quiet color and border transitions can clarify
  hover, focus, and selection. Routine hovering must not make cards float, zoom,
  or shift their neighbors. Decorative ambient movement and pulsing glows add
  noise. Spend motion on the action and the things it actually affects.
- Respect reduced-motion preferences: remove nonessential travel, scaling, and
  spring effects while retaining clear focus, selection, destination feedback,
  and direct tracking of an object the user is moving.

### 5.1 Show the consequence before I commit

Dragging must feel like moving the actual object through a stable space. A
dimmed object left at its source and a thin insertion marker are not enough.
Keep a readable representation attached to the pointer, distinguish it with
restrained elevation, and smoothly move neighboring objects aside as the
candidate destination changes.

The reserved space must match the object's actual footprint, including wrapped
text and variable row heights. I should be able to predict the final arrangement
before releasing. Drop commits that arrangement without a flash, duplicate,
teleport, or unrelated reflow. Escape or cancellation restores the prior order
and leaves selection and focus somewhere sensible.

Use stable target thresholds so small pointer movements do not make the
destination flicker back and forth. Near an edge, scroll only the relevant
surface and continue scrolling while I hold there; do not require me to keep
wiggling the pointer. Keep the object attached to the same grab point as the
surface scrolls. Keyboard reordering must produce the same order and clear
destination feedback.

### 5.2 Make editing feel continuous

Controls, labels, summaries, and the live result should tell the same story
throughout an interaction. Valid value changes update together as I drag, scrub,
step, or type. Do not make a slider feel live while its numeric equivalent waits
for a separate confirmation without a task-specific reason.

Let me finish typing an incomplete value, such as a minus sign or an empty
field, without overwriting my input or moving the caret. Keep the last valid
result until the draft is valid. Update affected content in place; do not
recreate entire panels on every keystroke if that risks focus loss, flicker,
scroll movement, or delayed feedback.

Changing a label or selection must not restart a preview or erase my position
in a workflow. If a substantive edit invalidates playback or a result, preserve
as much context as possible and make any necessary pause or reset explicit.

### 5.3 Motion must accept the next action

Never make me wait for an animation to finish before I can interact again.
Repeated input should retarget motion from its current visible position, not
queue animations, replay an entrance, or snap back to an old starting point.
Selection, focus, and active playback are distinct states and must remain
visually distinguishable while things move.

Keep motion distances modest and timing consistent across related controls.
Put durations, easing, spring settings, and drag thresholds in one clearly
named configuration or token block so they are easy to tune. The goal is a
quiet, cohesive tool: obvious cause and effect, no hesitation, and no extra
visual work for the user.

### 5.4 Verify the feel, not just the presence of transitions

Test rapid reversals, repeated clicks, long labels, unequal item sizes, long
lists, edge scrolling, cancellation, and reduced motion. A transition declaration
is not evidence that an interaction feels fluid. Check the running interface
with realistic content and distinguish observed behavior from conclusions drawn
only from source code. When live testing is unavailable, state that limitation.

---

## 6. State, feedback, and edges

### 6.1 Feedback is legible or it doesn't exist
Every action produces a visible result within a frame or two. If the result
happens off-screen, say so — a brief, quiet toast in a consistent corner. If
something takes real time, show real progress, not an indeterminate spinner
where a determinate bar is possible.

### 6.2 Loading
Skeletons that match the shape of the incoming content, not centered spinners on
a blank page. If a surface has known structure, render the structure immediately
and fill it in.

### 6.3 Empty states
An empty screen is an instruction, not a shrug. Show what goes here and give the
control that creates the first one, right there in the middle of the space.
"No items yet" alone is a failure.

### 6.4 Errors
State plainly what happened and what to do about it. No apologies, no
personality, no exclamation marks. The error appears next to the thing that
failed, not in a global banner, and the failed input keeps my data so I can fix
rather than retype.

---

## 7. Words in the interface

Copy is design content. The same taste applies.

- **Sentence case. Plain verbs. No filler.**
- Buttons say exactly what happens: "Save changes," "Create mode," "Delete
  sequence." Never "Submit," never "OK" where a real verb fits.
- An action keeps the same name through the whole flow. "Publish" produces
  "Published."
- Name things the way I'd describe them, not the way the code models them.
- **No emoji in the UI.** No exclamation marks. No cheerful microcopy, no
  "Oops!", no "Let's get started!", no personality voice in system messages. Dry
  and useful.
- Labels describe; they don't sell. Tooltips explain the non-obvious; they don't
  repeat the label.

---

## 8. Anti-patterns — things I will send back

1. Text truncated with an ellipsis.
2. A feature reachable only by hover, right-click, or a nested overflow menu.
3. The viewport jumping to the top after an action.
4. A form where direct manipulation was possible.
5. Every option displayed at once in stacked sections, with no focused stage.
6. Light theme by default.
7. ALL-CAPS tracked-out eyebrow labels above headings.
8. Identical rounded cards in a 3-column grid with identical soft grey shadows.
9. Staggered fade-in-on-scroll animations.
10. A persistent top navigation bar on something that isn't a website.
11. Layouts that reset, re-sort, or re-flow without me asking.
12. Instant irreversible delete from a small ✕.
13. Emoji or exclamation marks in system copy.
14. Gradient washes used as decoration.
15. `→` appended to button and link text.
16. A "Preview" or "Generate" button where the result could have been live.
17. Hardcoded categories/presets I can't edit from the UI.
18. Placeholder-looking content shipped as real content.
19. Drag feedback that marks a destination but leaves the object visually behind.
20. Reordering that jumps or flickers instead of showing the resulting layout.
21. Animations that lag behind input, block the next action, or queue up.
22. Small edits that rebuild the workspace, lose the caret, or restart a preview.

---

## 9. How I want you to work

- **Ask clarifying questions at the end of a delivery, not before it.** Build
  your best interpretation first, then list the two or three judgment calls you
  made and what the alternatives were. I'll correct you fast. Don't stall on a
  questionnaire.
- **State your assumptions explicitly** when you make them.
- **Push back if I'm wrong.** If I ask for something that will produce a worse
  interface, say so and explain why, then build what I asked for if I hold.
- **Iterate on the design in one place.** Don't scatter a project across files
  when a single self-contained deliverable will do — I'd rather have one file I
  can drop somewhere and run.
- **Surprises are welcome.** If you see something useful I didn't ask for that
  fits the design language, build it and tell me afterward.
- **Test before you hand it over.** Especially: long strings, empty states,
  many items, and the actual delete/reorder paths. A demo that only works with
  the seed data isn't done.

---

## 10. Pre-delivery checklist

Before you show me anything, verify:

- [ ] Dark theme, one accent, layered surfaces with hairline borders
- [ ] No text is truncated anywhere, tested with long strings
- [ ] Every feature has a visible affordance
- [ ] Ordered lists are drag-reorderable
- [ ] Positions and layout persist
- [ ] Delete is safe and reversible or confirmed
- [ ] No action moves the viewport unexpectedly
- [ ] A live result/preview is visible while editing
- [ ] Motion explains change; nothing animates on load
- [ ] Dragged objects track the grab point; neighbors preview the final placement
- [ ] Drop, cancellation, and edge scrolling work with long and unequal-size items
- [ ] Live editing preserves focus, caret, scroll, and relevant preview position
- [ ] Rapid input retargets motion without waiting, flicker, or queued animations
- [ ] Reduced motion preserves all interaction feedback and functionality
- [ ] Empty, loading, and error states all exist and all say something useful
- [ ] Copy is sentence case, dry, no emoji, verbs on buttons
- [ ] Keyboard: Escape closes, Tab focus is visible
- [ ] It looks like a tool someone made on purpose, not a template
