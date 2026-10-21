# Playtest notes

What the owner noticed while playing the dev builds, and what we did about it. Every piece of
feedback lands here, whether it came through `/playtest` or plain chat, so it survives between
sessions. Newest first.

Each entry: date, build version, the owner's words, the type (bug, feel, idea, decision), and
what we think is causing it. Fixed items move to "Done" with the version that fixed them.

## Open

- **2026-10-21, 0.3.0, feel: "the knockback feels a little weird... especially sprint knockups.
  Not sure if it's going too far or too high."** Suspected cause: we build knockback from the
  victim's real motion, while 1.8.9 builds it from the server's copy of the player, which
  barely moves sideways and keeps falling while airborne. In a combo that would make our hits
  send people farther and higher than Minecraft. Being researched.
- **2026-10-21, 0.3.0, feel: "the hit cooldown after a player gets hit doesn't feel right...
  my hits get eaten" (hitting a practice bot).** Suspected causes: the half-second miss delay
  after clicking at nothing (most 1.8.9 PvP players use clients that remove it), and our
  immunity being slightly stricter than Minecraft's, which rounds clicks to 1/20 s steps.
  Being researched.

## Known gaps

Not bugs: things that are expected to feel off until they're built.

- When you hit another person over the network (not a bot on your own game), their knock back
  shows up one round trip later. Predicted knockback is planned (see `netcode.md`).
- Players don't push each other apart when they overlap.
- No slabs, stairs or stepping up blocks; no water or lava yet.

## Done

Nothing yet.
