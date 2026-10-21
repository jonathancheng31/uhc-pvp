---
name: playtest
description: Log the owner's playtest feedback in docs/playtest-notes.md and act on it. Use whenever the owner reports how a build plays, a bug, or an idea, even when they don't type /playtest.
argument-hint: "<what you noticed while playing>"
---

# Playtest feedback

The owner said: $ARGUMENTS

## 1. Understand it

- Read `docs/playtest-notes.md` and the current version (`config/version` in `project.godot`).
- Work out which part of the game it's about and whether it's a **bug** (something broken),
  **feel** (works but plays wrong), an **idea** (something new), or a **decision** (a choice
  about how the game should be).
- If it could mean two quite different things, ask one short question with AskUserQuestion,
  offering the likely readings as options. Don't ask when the meaning is clear.

## 2. Log it

Add an entry at the top of "Open" in `docs/playtest-notes.md`:
the date, the build version, the owner's words (short quote), the type, and what we think is
causing it.

## 3. Act on it

- **Bugs and small feel tweaks:** fix them now. Write a test that fails before the fix when the
  problem can be tested, then `/ship`. Move the entry to "Done" with the version that fixed it.
- **Feel:** first check whether we match Minecraft 1.8.9 there (parity tests, `docs/combat.md`,
  `docs/movement.md`) and whether lag causes it (retry with `--ping`). Then either fix a mismatch,
  or, if we match 1.8.9 and the owner wants it different, treat it as a deliberate change from 1.8.9:
  record it in the decisions table in `docs/design.md`.
- **Ideas:** put them in the milestone where they fit in `docs/design.md` (or "Later") and keep
  the entry open with a pointer there.
- **Decisions:** update the decisions table or open questions in `docs/design.md`.

## 4. Reply

In a few plain lines: what you understood, what you're doing about it now, and what's queued
for later.
