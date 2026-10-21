# Playtest notes

What the owner noticed while playing the dev builds, and what we did about it. Every piece of
feedback lands here, whether it came through `/playtest` or plain chat, so it survives between
sessions. Newest first.

Each entry: date, build version, the owner's words, the type (bug, feel, idea, decision), and
what we think is causing it. Fixed items move to "Done" with the version that fixed them.

## Open

- **0.4.0: how does the rod feel?** Range, knockback, and whether throwing it non-stop (no
  cooldown) is too strong.
- **2026-10-21, 0.3.1, idea: faster strafing** ("we can consider changing the movement patterns
  so that strafing is a lot faster, adding more skill... that's for later"). Queued in
  `design.md`, "Later".
- **0.3.1: how do hits and knockback feel now?** Especially combos on a fighting bot, and
  whether hits still get eaten.

## Known gaps

Not bugs: things that are expected to feel off until they're built.

- When you hit another person over the network (not a bot on your own game), their knock back
  shows up one round trip later. Predicted knockback is planned (see `netcode.md`).
- Players don't push each other apart when they overlap.
- No slabs, stairs or stepping up blocks; no water or lava yet.

## Done

- **2026-10-21, 0.3.1, decision: the fishing rod, our own design.** "We are heavily based on
  Minecraft, let's move away from that and start building ourselves." Right-click throws the rod
  (no slot switching, no cooldown); right-click again removes the old bobber and throws a new
  one; a hit knocks back. Rod hits count for hit protection like 1.8 (owner's choice). **Done in
  0.4.0** (`rod.md`).
- **2026-10-21, 0.3.1, decision: "make sprinting a toggle like Badlion and Lunar Client", then
  "make it so that sprinting isn't an option at all, it's just the default movement."** **Done in
  0.3.2:** moving forward always sprints; the sprint key is gone.
- **2026-10-21, 0.3.1, feel: "don't make players lose sprint after hitting a player, that's not
  correct."** Cause: 1.8.9 stops your sprint on every click on a player. With the sprint key held
  (or Lunar/Badlion toggle sprint) it comes straight back before you move, but double-tap-W
  sprinting stays off until you double-tap again, which is what our game did without the key.
  **Fixed in 0.3.2:** hits never stop your sprint (you still slow down a little per hit, and
  w-tapping still resets sprint knockback, like 1.8).
- **2026-10-21, 0.3.1, idea: "a setting for the bot so that it attacks me but doesn't actually
  swing its sword... basically running at me."** **Done in 0.3.2:** "Bots chase you (no hitting)"
  in the pause menu: moves exactly like the fighting bot but never swings.
- **2026-10-21, 0.3.0, feel: "the knockback feels a little weird... especially sprint knockups.
  Not sure if it's going too far or too high."** Cause: we built knockback from the victim's
  real motion, but 1.8.9 builds it from the server's copy of the player, which doesn't move
  sideways and keeps falling while the victim is airborne. Our combos pushed farther with each
  hit (19.5 vs 23.3 blocks over four hits) and stayed high; Minecraft's lift drops to almost
  nothing by the third hit in the air. **Fixed in 0.3.1:** the server's copy is emulated on the
  victim's game, and a 3-hit combo now matches Minecraft exactly (tested).
- **2026-10-21, 0.3.0, feel: "the hit cooldown after a player gets hit doesn't feel right... my
  hits get eaten" (hitting a practice bot).** Causes:
  - Mainly our miss delay: after clicking at nothing (or at a player just past 3 blocks), we
    blocked every click for half a second. Minecraft clears it at the end of any 1/20 s tick
    the button is up, so it rarely eats anything: at 9 clicks a second we ate about 4 clicks
    per miss, Minecraft about 0.4.
  - Clicks into hit protection used up your sprint knockback; in Minecraft only hits that count do.
  - Hit protection was about 15 ms stricter than a server's, which handles clicks on a 1/20 s grid.

  **Fixed in 0.3.1**, all three matching Minecraft (tested against ports of its code).
