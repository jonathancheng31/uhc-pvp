# Playtest notes

What the owner noticed while playing the dev builds, and what we did about it. Every piece of
feedback lands here, whether it came through `/playtest` or plain chat, so it survives between
sessions. Newest first.

Each entry: date, build version, the owner's words, the type (bug, feel, idea, decision), and
what we think is causing it. Fixed items move to "Done" with the version that fixed them.

## Open

- **2026-10-23, 0.4.6, feel and idea: "comboing feels kinda hard. i want comboing to be a bigger
  part of the game. or maybe im just bad at mc pvp. idk."** Our knockback, hit protection and
  w-tap rules match 1.8.9 exactly (tested), so this is 1.8.9's own difficulty, maybe made harder
  by a fighting bot that strafes away and hits back. Being looked into in depth before changing
  anything: what decides whether a combo carries on (how far each hit pushes versus how fast the
  attacker catches up, and the victim's ways out), measured in our game, plus how combo-focused
  servers tune it. Then options to try as twists.
  **What we found** (research plus a simulation with our real movement and knockback code):
  - Main cause: 1.8.9 draws other players about 3.5 ticks (175 ms) in the past even at 0 ping
    (positions sent every 2 ticks, then glided in over 3), and hits are judged on the attacker's
    screen. A victim flying away looks 1 to 1.5 blocks closer, so the attacker reaches them
    150 to 200 ms before they can hit back. We draw host bots at the present (and remote
    players at as little as 20 ms), so that head start is gone: after a hit both players get
    back in reach on the same tick and it's a click race. With 1.8.9's delay, a victim who
    lets the knockback carry them or strafes takes 4 to 6 hits; without it, 1.
  - The fighting bot walks back toward you while flying and rods you at the exact distance a
    sprint hit leaves it, so it breaks combos every time (being made difficulty-based).
  - Bug: a jump pressed 16 to 33 ms after knockback cuts it far more than 1.8.9 allows (0.73 to
    1.14 blocks of travel against 1.8.9's best of 1.65), so jump-resets are too strong.
  - Changes that don't help: shorter hit protection, more lift, more reach, the strafe twists.
    Less sideways push (sprint push 0.3 to 0.4, or 0.8 to 0.9 of all horizontal knockback)
    makes combos longer, and too far (0.7, or sprint push 0.2) takes away strafing as an out.
- **2026-10-23, 0.4.6, decision: hits and the rod first, depth over breadth.** Offered as next
  directions: a bigger kit (healing, bow), hit feel and fairness, team fights, or stranger
  mechanics. "nah lets focus specifically on the hit and rod mechanics for now first. i wanna
  polish this first." Then: "a little more depth than breadth for this project. i want quality
  not quantity." So the new experimental twists are dropped from the current batch (pushing, a
  smarter bot and sounds carry on, sounds narrowed to the combat ones), and changes to hits and
  the rod get planned with the owner first.
- **2026-10-23, 0.4.6, decision: more freedom, more experimental.** "For me im playing against
  bots but thats just for testing gameplay and changes... Feel free to take a little more
  freedom here and make improvements that are clear for now. You can have subagents or new
  sessions work on these. After those finish, then i'll play test, and then we can discuss more
  details of what directions to go in." Then: "you can begin to move away from minecraft pvp at
  this point. Take a little more freedom here and be more experimental for now." Picked from the
  polish list: a smarter fighting bot, players pushing apart, sounds, mouse sensitivity (plus
  field of view), and new experimental twists.
- **2026-10-22, 0.4.6, decision: "lets ignore match flow and other aspects for a while, we wanna
  polish gameplay."** Match flow and internet play wait; the feel pass and the twists come
  first (`design.md`, "Current status").
- **2026-10-22, 0.4.4, decision: our own twists, small changes first, then playtest.** Shortlist
  offered: combo breaker, faster strafing, timing over click speed, better hit feel.
  - Faster strafing: "interesting, basically changing some movement mechanics." Then: "faster
    sideways same top speed, add a slider so i can play test the feel." **Built in 0.4.5:** Esc
    menu, "Twists", strafe speed 100% (1.8) to 150%; sideways speed goes up on the ground, never
    faster than a straight sprint, the air stays 1.8's. Waiting on: which value feels best.
  - Click speed: "I want to get away from the twist idea you have" (saving an early click to land
    the instant protection ends), because "if clicking fast enough, you can guarantee a full
    length hit." But "the general goal of this is nice though. Remove some power from cps."
    Offered: wasted clicks cost a moment, or a click speed cap. "lets try wasted clicks."
    **Built in 0.4.6:** Twists screen, "Wasted clicks", off to 0.3 s; the crosshair turns red
    while clicks are blocked. Waiting on: whether it feels right, and which delay.
  - Then: "maybe we could try making strafing faster for changing directions not just top
    speed." **Built in 0.4.6:** "Strafe snappiness", off to 100% (switching A to D from 250 ms
    down to 50 ms). Waiting on: which value feels best.
  - Combo breaker: no. "It's very difficult to land full combos in minecraft pvp and they are
    already a very satisfying part of the pvp that i enjoy."
  - Better hit feel: later ("kinda qol and vfx that we can add to more later"). Queued in
    `design.md`, "Later".
- **0.4.0: how does the rod feel?** Range and knockback. (Spamming it: answered, see the
  0.4.2 cooldown under "Done".) Now also: does the 0.75 s cooldown feel right?
- **2026-10-21, 0.3.1, idea: faster strafing** ("we can consider changing the movement patterns
  so that strafing is a lot faster, adding more skill... that's for later"). Being playtested
  as a twist since 0.4.5 (above).
- **0.3.1: how do hits and knockback feel now?** Especially combos on a fighting bot, and
  whether hits still get eaten.

## Known gaps

Not bugs: things that are expected to feel off until they're built.

- When you hit another person over the network (not a bot on your own game), their knock back
  shows up one round trip later. Predicted knockback is planned (see `netcode.md`).
- Players don't push each other apart when they overlap.
- No slabs, stairs or stepping up blocks; no water or lava yet.

## Done

- **2026-10-22, 0.4.5, feel: "the screen shakes on hit is too much can we remove this or tone it
  down or change it or smth?"** Cause: we copied 1.8.9's hurt camera tilt (14 degrees). **Done in
  0.4.6:** it's a setting, "Camera shake when hit", 0 to 100% of Minecraft's, 25% by default;
  0 turns it off.
- **2026-10-22, 0.4.3, decision: "remove the hotbar."** **Done in 0.4.4:** the hotbar and the
  rod's slot are gone; hearts and armor sit centered at the bottom, and the rod's cooldown is a
  small bar under the crosshair that shows only right after a throw. Keys 1 and 2 (unused) are
  unbound.
- **2026-10-22, 0.4.2, idea: "add settings to adjust fps max and audio. just basic settings for
  now."** **Done in 0.4.3:** a Settings button on the main menu and in the pause menu (Esc) opens
  max FPS (30 to 360, or Unlimited) and a volume slider. Changes apply at once and are kept for
  next time.
- **2026-10-22, 0.4.1, decision: "add a cd to fishing rod so u cant just spam it. still p short
  cd tho."** Cause: the rod had no cooldown by design, so right-clicking non-stop threw a new
  bobber every click. **Done in 0.4.2:** after a throw, the rod can't be thrown again for 0.75 s
  (a bit longer than the half second of hit protection, so rod after rod can't keep someone
  pinned away). A pale cover on the rod's slot sinks away as it gets ready. Bots follow it too.
- **2026-10-22, 0.4.1, decision: "stop sprinting from changing fov."** Cause: we copied 1.8.9,
  which widens the view by up to 15% while sprinting. **Done in 0.4.2:** the view stays at 70
  degrees all the time.
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
