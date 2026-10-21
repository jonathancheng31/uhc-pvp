# Playtest notes

What the owner noticed while playing the dev builds, and what we did about it. Every piece of
feedback lands here, whether it came through `/feedback` or plain chat, so it survives between
sessions. Newest first.

Each entry: date, build version, the owner's words, the type (bug, feel, idea, decision), and
what we think is causing it. Fixed items move to "Done" with the version that fixed them.

## Open

- **2026-10-21, 0.3.0: waiting on first feedback on melee.** How hits, knockback and combos
  feel in practice mode against still, strafing and fighting bots.

## Known gaps

Not bugs: things that are expected to feel off until they're built.

- When you hit another person over the network (not a bot on your own game), their knock back
  shows up one round trip later. Predicted knockback is planned (see `netcode.md`).
- Players don't push each other apart when they overlap.
- No slabs, stairs or stepping up blocks; no water or lava yet.

## Done

Nothing yet.
