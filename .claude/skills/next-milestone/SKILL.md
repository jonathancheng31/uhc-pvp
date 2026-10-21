---
name: next-milestone
description: Pick up the project where the last session stopped, plan the next milestone with the owner and build it end to end. Use at the start of a new session, or when the owner says to move on.
argument-hint: "[optional: which milestone or what to focus on]"
---

# Build the next milestone

Owner's notes, if any: $ARGUMENTS

## 1. Catch up (keep it short)

- `docs/design.md`: "Current status / next up", the milestone's description, decisions and
  open questions.
- `docs/playtest-notes.md`: open items. Bugs come before new features unless the owner says otherwise.
- The feature docs the milestone touches (`docs/combat.md`, `docs/movement.md`, `docs/netcode.md`).
- `gh run list --repo jonathancheng31/uhc-pvp --limit 1`: start from a green build. If it's
  red, fix that first.

## 2. Research in helpers

Start an `mc-researcher` agent per topic, in parallel (for the fishing rod, for example: the
bobber's flight and physics, hooking and the rod hit, and how pulling and reeling work in 1.8.9).
Give each the exact question and what we already have. Use `Explore` agents for wide searches of
our own code. Keep the research in the agents; bring back only the briefs.

## 3. Plan with the owner

Tell the owner in a few plain lines what they'll be able to do when this ships, and how long
it's likely to take. If a real design choice is open (something that changes how the game
plays), ask with AskUserQuestion, recommended option first, and record the answer in
`docs/design.md`. Otherwise state the plan and start; don't wait for a go-ahead that isn't needed.

## 4. Build it

In this order, committing working steps as you go:

1. **Rules with tests.** Pure logic in `scripts/Core/`, `scripts/Movement/` or `scripts/Combat/`,
   numbers in `Tuning.cs` starting from the 1.8.9 values. Where 1.8.9 behavior is copied, extend
   `tests/McReference.cs` with an exact 20-tick version and test that ours matches it at
   Minecraft tick boundaries.
2. **Gameplay and network.** New messages follow the conventions in `CLAUDE.md` (append to
   `MsgType`, `MessageTests`, bump `Network.ProtocolVersion`). Keep the netcode plan in
   `docs/netcode.md`: the acting player's game decides, the victim's game applies.
3. **Practice bots use it too,** so the owner can practice against it alone.
4. **Smoke test covers it.** Extend `tools/smoke_test.sh` so the new feature is exercised over
   the network, including the laggy player.
5. **Looks and sound.** Check with `/playtest-shot` (first person, other players, through lag).
   Art and sound are our own.

## 5. Finish

- Docs: a feature doc for anything big (`docs/<feature>.md`), the milestone marked done, the
  status section rewritten for the next session, `CLAUDE.md` layout if files were added.
- Bump the middle version number, then `/ship`.
- Final message: what's new, how to try it, the one or two things you most want feedback on.
  Suggest a fresh session (`/next-milestone`) for the next milestone.
