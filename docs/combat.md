# Combat

Melee follows Minecraft 1.8.9's code (EntityPlayer.attackTargetEntityWithCurrentItem,
EntityLivingBase.attackEntityFrom and knockBack, EntityRenderer.getMouseOver). Numbers live in
`scripts/Core/Tuning.cs`; the rules in `scripts/Combat/`; how hits travel over the network in
[netcode.md](netcode.md). The fishing rod has its own page, [rod.md](rod.md); its hits go through
the same hit protection and knockback as sword hits.

## A hit, step by step

1. **Click.** Every click is one attack attempt (no cooldown, so clicks per second matter).
   The arm swings (6 MC ticks).
2. **Aim.** From your eyes along your view: blocks are traced up to 4.5 blocks; players'
   hitboxes count 0.1 bigger on every side; a player must be closer than the first block and
   within 3 blocks. Pointing at a player beyond 3 blocks, or at nothing, is a **miss**, which
   starts 1.8's miss penalty (`MissPenalty`): clicks are thrown away for 10 ticks, but the
   penalty clears at the end of any 1/20 s tick the button is up. So it only eats a second
   click in the same tick as the miss, or clicks while the button stayed held (some butterfly
   and drag clicking). Pointing at a nearby block is not a miss.
3. **Attacker side.** Any click on a player: if you were sprinting, you slow to 60% but keep
   sprinting (your game does this immediately, like 1.8's client, even if the hit won't
   count). Falling and off the ground makes it a **crit**; your own screen shows crit sparks
   on every critical click.
4. **Victim side.** Damage = 8 (diamond sword), x1.5 if crit, x(25 - armor)/25. With the kit's
   18 armor points that's 2.24 (3.36 crit).
   - **Hit protection** (`HitProtection`): after a full hit, for 10 MC ticks further hits only
     count if stronger than the last (a crit after a normal hit), and then only the extra damage
     applies. A rod hit counts as a full hit with no damage, so a sword hit right after it is a
     stronger hit (full damage, no knockback). A server handles clicks at its 1/20 s ticks, so "10 ticks later" means the tick
     the click falls in: in real time, 450 to 500 ms after the last full hit. We count the same
     way, on the shared host clock, from the moment of the click.
   - **Knockback, from the server's copy of the victim.** 1.8.9 doesn't start from the victim's
     real motion but from the server's copy of the player (`EntityPlayerMP`), which only
     follows the player's position reports and never sees their keys. So it doesn't move
     sideways, rests at -0.0784 vertically on the ground, and when it sees the player leave
     the ground it runs its own jump (0.42 up) and keeps falling until its copy lands.
     `PlayerMotor.ServerMotion` reproduces it on the victim's own game, from the victim's own
     state, so there's no ping in it.
   - **The formula:** halve the copy's motion (all three axes), push 0.4 away from the attacker
     and 0.4 up (capped at 0.4). A sprint hit adds 0.5 along the attacker's facing and 0.1 up.
     The result replaces the victim's motion, rounded like Minecraft's velocity packet (to
     1/8000). The copy itself keeps its old motion.
   - **What that feels like:** every hit pushes the same 0.9 blocks per tick sideways (sprint
     hit), whatever the victim was doing. The lift shrinks as a combo keeps the victim in the
     air: on a victim who doesn't touch the keys, sprint hits every 10 ticks lift 0.46, then
     0.35, then 0.02.
   - **A stronger hit** (during protection) sends the victim no knockback at all; a sprint
     push from it stays on the server's copy and adds half of what's left to the next hit.
   - **Sprint knockback is used up only by hits that count** (full or stronger), so a click
     into hit protection keeps your next hit's sprint knockback.
   - **With the player pushing twist on, a player flying from a hit passes through everyone**
     until they land, so bodies never eat knockback ([movement.md](movement.md)).
   - **Hits never stop your sprint** (a deliberate change). 1.8.9's client stops it on every
     click on a player, but restarts it before you move when the sprint key is held, which our
     always-on sprint is. So movement is identical to 1.8.9 with the key held; we only skip
     1.8.9's one-tick FOV dip, and the lost sprint that double-tap-W players had to tap back.
5. **Everyone** sees the red flash (0.5 s), hears the hurt sound (the attacker also hears their
   own hit sound; see "Sounds" below), sees crit sparks, and the victim's camera tilts: by a quarter of Minecraft's 14 degrees by default, set in Settings
   (0.4.6, after the owner found the full tilt too much; 0 turns it off).

## Our twist: wasted clicks (since 0.4.6, a match rule)

Off by default (1.8.9). The host can set a **wasted-click delay** (0 to 0.3 s) in the pause menu's
Twists screen. A click that does nothing blocks your clicks for that long: a miss, a block, a
teammate, or a player still in hit protection when the hit isn't stronger. A hit that counts,
even a stronger one (a crit) inside protection, is never wasted. Blocked clicks don't swing and
don't restart the delay; the crosshair turns red while they're blocked. It's decided on the
clicker's own game (`ClickLockout`, `Game.WasteClick`), from what it sees and its guess of the
target's hit protection.

Why: in 1.8.9 the next hit after hit protection goes to whoever clicks first after it ends, so
clicking faster wins (and butterfly or jitter clicking wins most). With the delay, clicks into
protection lock you out, so spamming makes your first real hit come later and one well-timed
click beats it. Average time from protection ending to the next hit landing, with perfect aim
(`tests/WastedClickTests.cs`):

| Delay | 6 clicks/s | 8 | 10 | 15 | One timed click |
|---|---|---|---|---|---|
| Off (1.8.9) | 92 ms | 70 | 55 | 41 | 24 |
| 0.10 s | 100 | 88 | 86 | 72 | 24 |
| 0.15 s | 142 | 107 | 108 | 99 | 24 |

At 0.15 s, clicking faster than about 8 times a second gains almost nothing. Clicking into
protection costs everyone, slow clickers too; what wins is timing. (Ruled out first: saving an
early click to land the instant protection ends, since spam would then always land the perfectly
timed hit.)

## W-tapping

In 1.8 the client only tells the server when sprinting *changes*, and the server switches its
own copy off after a sprint hit without telling the client. So holding W after a sprint hit
keeps you sprinting on your screen, but your next hit gets no sprint knockback until you
really stop sprinting for a tick (let go of W, press S or sneak) and start again.
`PlayerMotor.ServerSprinting` reproduces this exactly; F3 shows "Sprint knockback ready".

## Practice bots

`scripts/Gameplay/BotBrain.cs`. Bots press the same controls a player does and their hits use
the same rules. Modes: stand still, strafe, chase (moves like fight back but never swings), fight back (chases, sprints, strafes, sprint-jumps
to close distance, clicks about 9 times a second, w-taps after sprint hits, throws its rod at 3.5
to 8 blocks about every 1.5 seconds, turns at up to
540 degrees per second with a little wobble). They respawn 2 s after dying and heal to full
after 5 s without being hit.

## Sounds

Our own, made by `tools/gen_sounds.py` (since 0.4.7; before, only a hurt sound and the rod's
throw). Fewer sounds, each worked on: the fight comes first. The fight sounds are one family: a
sword hit is the victim's hurt sound, plus the attacker's own hit sound, plus the crit on top
when it's critical. They share one crack, one punch and one small outdoor room, so together they
read as one blow. Who hears what is decided in `scripts/Gameplay/Game.Sounds.cs`; the mix is in
`scripts/Core/SoundBank.cs`.

| Sound | When it plays | Who hears it, from where |
|---|---|---|
| Swing | An arm swing starts (at most every 0.15 s, as the animation restarts) | You: from your sword hand, quieter and thinner when nobody's in reach. Others: from the swinger's hand |
| Hit | Your sword hit counts (at the click for players on other games, like the red flash) | Only you, in your head. Each hit of a combo is a little higher, for 5 hits |
| Hurt | Anyone takes a hit, sword or rod | Everyone, from the victim. Lower and heavier on a kill |
| Crit | A critical hit counts | Everyone, from the victim, with the sparks |
| Rod throw | The bobber leaves the rod | You: from your rod hand. Others: from the thrower's |
| Bobber catch | A bobber catches someone | The thrower in their head; everyone else from the victim |
| Bobber landing | A bobber touches the ground or a wall | Everyone, from where it lands |
| Footstep | Every 1.4 blocks walked on the ground (sneaking is silent, as in Minecraft) | Everyone, from the walker's feet; yours much quieter |
| Landing | Touching down after a fall of 0.25 blocks or more | As footsteps; bigger falls are louder |

- **Never the same twice.** Each sound has 4 to 8 versions (8 for the swing, hit, hurt and
  footsteps). They play in a shuffled order, never the same one twice running, and every play
  shifts pitch (3 to 10%) and volume (1 to 3 dB) a little, mostly near the middle.
- **On the frame.** Each sound starts as its event is shown: the swing as the arm starts, a hit
  at the click, footsteps from where a player is drawn, the bobber's landing as it's drawn
  touching down. Every file is at full strength within 4 ms of its first sample. The swing's
  whoosh follows the animation: loudest in the first 15 ms, gone by the time the arm comes back.
- **Where it comes from.** Other players' sounds lean to the side they're on (straight to your
  left is about 12 dB louder in your left ear), fade by 6 dB for each doubling of distance past
  a few blocks and get duller further away, and are muffled from behind, so you can tell front
  from back.
- **Levels.** Every file is mastered to the same loudness (-14 LUFS over its loudest 100 ms,
  peaks under -1 dBFS). Measured in the game: a hurt is about -15 LUFS, your hit sits 2 to 3 dB
  under it, your swings 13 dB under, other players' footsteps 16 dB under up close (and still
  audible 12 blocks away), yours 20 dB under. A limiter on the master stops pile-ups (two
  players trading hits) from clipping. Everything follows the volume setting.
- **Checking them without listening.** `python3 tools/gen_sounds.py --report` lists each file's
  peak, loudness, length and tone. The `--record-audio=PATH` launch option records what the game
  plays, and `gen_sounds.py --analyze PATH` reports it second by second, with any clipping.
  `tests/SoundTests.cs` checks every file's level, start and end, the variation, footsteps and
  the bobber's landing time.

## Verified against Minecraft

`tests/CombatTests.cs`: damage and armor, hit protection on the tick grid, 9 hits to kill,
the knockback formula and velocity-packet rounding, knockback flights (standing, sprint hit,
victim sprinting at you, victim mid-air) identical to the 1.8.9 reference at every tick, the
server's copy of the player matching a port of `NetHandlerPlayServer.processPlayer`
(`McServerCopy`) while standing, jumping, sprint-jumping, walking and sneaking, a 3-hit combo
whose knockback matches it exactly, the stronger-hit leftover, the attacker's sprint-hit
slowdown, the w-tap rule (including clicks into hit protection), crits needing a fall, the
miss penalty against a port of `Minecraft.runTick` over 300 random clicking patterns, and
aiming (reach, border, walls, sky, the out-of-reach quirk). `tools/smoke_test.sh` has bots fight across the network, one with 120 ms of lag.
