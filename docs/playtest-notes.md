# Playtest notes

What the owner noticed while playing the dev builds, and what we did about it. Every piece of
feedback lands here, whether it came through `/playtest` or plain chat, so it survives between
sessions. Newest first.

Each entry: date, build version, the owner's words, the type (bug, feel, idea, decision), and
what we think is causing it. Fixed items move to "Done" with the version that fixed them.

## Open

- **2026-10-23, 0.4.8, feel: the 175 ms head start "just was too obvious of a lag. it felt like
  i was lagging or smth. isthis how its supposed to be? How did pvp servers handle this in
  mc?"** Hence the 75 ms pick (above). 1.8.9 really does draw others about 175 ms behind at 0
  ping, but our version may show it more plainly: the hit flash and sound come at once while
  the flight starts the full head start later, and we shift the whole path back by a fixed time
  where 1.8.9 starts gliding toward each new position at once. Being researched: how 1.8 PvP
  servers and clients dealt with the delay, and the exact timeline of what an attacker sees
  after a hit in 1.8.9 compared with ours, before proposing a version that keeps the
  chaser's edge without feeling like lag.
  **What we found:** 1.8.9 keeps two delays apart. The reaction starts soon: the flash about
  25 ms after the click, the body moving at 100 ms, easing in at about a third of the real
  launch speed. The trail is long: mid-flight the body runs about 160 to 175 ms behind, up to
  about a block, and that trail is what gives combos their edge (it matters 450 to 700 ms after
  a hit). Our fixed 175 ms shifted both: an instant flash, a 175 ms pause, then a full-speed
  jolt, so it read as lag. At 75 ms our start matches 1.8.9's 100 ms, but most of the trail is
  gone. 1.8.9 also kicks the victim's arms and legs at the flash; we have no flinch. PvP
  servers (NachoSpigot, WindSpigot) mainly handled attacks the moment they arrived instead of
  on the next tick; the open-source ones kept sending positions every 2 ticks, and the glide is
  client code they couldn't change. Measured designs (0 ping, Easy-bot combo average for a
  w-tapping player): fixed 75 ms 4.3 hits; fixed 175 ms 5.9; 1.8.9's own glide (positions every
  2 ticks, a third of the way per tick, a fresh sample forced on a hit, 25 ms delay) starts
  moving at 81 ms, trails 172 ms, 6.5 hits, but fast A/D strafing looks softer (55% of the swing
  shown, 1.8.9 74%); a "sharp" variant of today's delay that snaps the victim fresh right after
  your hit and fades back before the next starts at 43 ms, 5.9 hits, but strafe turns still
  show 175 ms late and an approaching victim lurches about 0.8 blocks at the hit.
- **2026-10-23, 0.4.8, decision: the twists that feel good.** A screenshot of the Twists screen
  set to strafe speed 100%, strafe snappiness 100%, player pushing off, combo knockback 100%,
  head start 75 ms and wasted clicks off: "these feel good". So: the snappiest strafing, a
  shorter head start than 1.8.9's 175 ms, everything else plain 1.8. **Built in 0.4.9:** these
  are the defaults for every new match. On the Twists screen each value at its default says
  "(default)", "Reset to defaults" brings them back and "Plain 1.8 (all off)" switches every
  twist to 1.8.9's; the pause menu and F3 say "Defaults" when nothing's changed. A host's saved
  choice still carries over to their next match. To find out: what felt off at 175 ms. In the
  combo simulation, a 100 ms head start gave no longer combos than none against a victim who
  lets the knockback carry them (175 ms gave about 4).
- **2026-10-23, 0.4.7, to try: sounds, player pushing, the new bot, sensitivity and FOV.**
  What's new, and what to tell us after playing:
  - Sounds: the fight has its own now (swing, hit, hurt, crit, rod throw and catch, the bobber
    landing, footsteps), heard from where they happen. Try a few combos on a bot, and a fight
    with the rod. Questions: should your hit sound keep rising in pitch through a combo (it
    climbs for 5 hits)? Is a whoosh on every swing too much when clicking fast? Should your own
    footsteps stay (quiet) or go?
  - Player pushing: Esc, "Twists", "Player pushing", off to 100%. Run into a bot with it at
    100%. Questions: does a 1.1 blocks/s shove feel right (someone you walk into gets pushed
    along at that speed)? In team fights, should bodies block knockback (today someone flying
    from a hit passes through everyone)?
  - The fighting bot: Esc, set bots to fight back, then "Bot skill": Easy, Normal or Hard. It
    sees you a moment late, its aim trails, and it escapes combos as often as its skill.
    Questions: is Normal the right default? Is Hard too strong? Does it throw its rod too often?
  - Settings: mouse sensitivity and field of view, with Minecraft's numbers, so you can copy
    yours.
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
  - Offered: bring back 1.8.9's display delay, fix the jump-reset bug, and a "combo knockback"
    slider. The owner: "Sure fix the bug. sure we can do combo knockback." The delay: asked what
    it means; after the explanation (and on by default, with a slider): "sure lets implement".
  **Built in 0.4.8**, all three:
  - Head start: everyone else is drawn and hit 175 ms in the past, 1.8.9's average (measured with
    a port of its code), the same at any ping, bots included (they see you through it too). Esc,
    "Twists", "Head start": off (how 0.4.7 played) to 250 ms. In the combo simulation, a victim who
    lets the knockback carry them now takes about 4 hits, strafing 5, jumping on the hit 5 (all 1
    before). A w-tapping player's combos on the Easy bot average 6 hits (4 before).
  - Combo knockback: Esc, "Twists", "Combo knockback", 100% (1.8.9) down to 60%. One sprint hit
    carries a victim 4.9 blocks at 100%, 4.4 at 80%, 3.8 at 60% (we'd said about 4, 3.6 and 3.1:
    the same drops, bigger distances).
  - Jump resets match 1.8.9 at every 1/60 s step, and a sprinting attacker slows at most once per
    tick (several clicks in one tick slowed you several times).
  - To try: combos on Easy and Normal with the head start on and off, and whether being hit where
    you were 175 ms ago feels fair; then combo knockback at 80%. Answered by the owner's picks
    (above): a 75 ms head start and combo knockback at 100%, the defaults since 0.4.9.
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
  - Players pushing apart: **built in 0.4.7** as a twist, "Player pushing" on the Twists screen,
    off (1.8.9) to 100%. At 100% nobody runs through anyone: you stop at someone and shove them
    along, and players spawned inside each other slide apart. Lower is softer; below 60% you can
    push through. Someone flying from a hit still passes through, so combos are untouched.
    Waiting on: which value feels best (100% is the "solid" setting to try first). Answered
    (2026-10-23, from playing 0.4.8): off, the default since 0.4.9.
  - Smarter fighting bot: **built in 0.4.7**, "Bot skill" in the pause menu: Easy, Normal (the
    default) or Hard, remembered for next time. It plays like a person instead of aiming
    perfectly: it sees you a moment late, its aim trails and wanders, it w-taps, waits out your
    hit protection and escapes combos as often as its skill. Easy lets your knockback carry it,
    for practising combos.
  - Sounds: **built in 0.4.7**, the combat ones: swing, hit, hurt, crit, rod throw and catch,
    the bobber landing, footsteps. All our own, made as one family, heard from where they
    happen ([combat.md](combat.md), "Sounds").
  - Mouse sensitivity and field of view: **built in 0.4.7** in Settings, with Minecraft's
    numbers and ranges.
- **2026-10-22, 0.4.6, decision: "lets ignore match flow and other aspects for a while, we wanna
  polish gameplay."** Match flow and internet play wait; the feel pass and the twists come
  first (`design.md`, "Current status").
- **2026-10-22, 0.4.4, decision: our own twists, small changes first, then playtest.** Shortlist
  offered: combo breaker, faster strafing, timing over click speed, better hit feel.
  - Faster strafing: "interesting, basically changing some movement mechanics." Then: "faster
    sideways same top speed, add a slider so i can play test the feel." **Built in 0.4.5:** Esc
    menu, "Twists", strafe speed 100% (1.8) to 150%; sideways speed goes up on the ground, never
    faster than a straight sprint, the air stays 1.8's. Waiting on: which value feels best.
    Answered (2026-10-23): 100%, 1.8's, the default since 0.4.9.
  - Click speed: "I want to get away from the twist idea you have" (saving an early click to land
    the instant protection ends), because "if clicking fast enough, you can guarantee a full
    length hit." But "the general goal of this is nice though. Remove some power from cps."
    Offered: wasted clicks cost a moment, or a click speed cap. "lets try wasted clicks."
    **Built in 0.4.6:** Twists screen, "Wasted clicks", off to 0.3 s; the crosshair turns red
    while clicks are blocked. Waiting on: whether it feels right, and which delay. Answered
    (2026-10-23): off, the default since 0.4.9.
  - Then: "maybe we could try making strafing faster for changing directions not just top
    speed." **Built in 0.4.6:** "Strafe snappiness", off to 100% (switching A to D from 250 ms
    down to 50 ms). Waiting on: which value feels best. Answered (2026-10-23): 100%, the
    default since 0.4.9.
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
  shows up the head start (75 ms by default) plus the hit's trip to their game later; a bot on your own game,
  after just the head start, like 1.8.9 at 0 ping. Predicted knockback is planned (see
  `netcode.md`).
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
