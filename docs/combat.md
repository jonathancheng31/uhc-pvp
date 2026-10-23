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
   hitboxes count 0.1 bigger on every side, where they're drawn on your screen (the match's
   head start in the past, 175 ms by default, like 1.8.9: see [netcode.md](netcode.md)); a player
   must be closer than the first block and within 3 blocks. Pointing at a player beyond 3 blocks, or at nothing, is a **miss**, which
   starts 1.8's miss penalty (`MissPenalty`): clicks are thrown away for 10 ticks, but the
   penalty clears at the end of any 1/20 s tick the button is up. So it only eats a second
   click in the same tick as the miss, or clicks while the button stayed held (some butterfly
   and drag clicking). Pointing at a nearby block is not a miss.
3. **Attacker side.** Any click on a player: if you were sprinting, you slow to 60% but keep
   sprinting (your game does this immediately, like 1.8's client, even if the hit won't
   count). Only once per Minecraft tick: 1.8.9's first click stops sprint until the next tick,
   so more clicks in the same 1/20 s don't slow you again (before 0.4.8 every click did).
   Falling and off the ground makes it a **crit**; your own screen shows crit sparks
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
     1/8000). The copy itself keeps its old motion. Like a velocity packet, which 1.8.9 handles
     between ticks, it lands at the victim's next Minecraft tick (`PlayerMotor.Knock`; at most
     1/30 s later on our 60 ticks), built from the copy as it is then.
   - **What that feels like:** a sprint hit from someone facing the victim pushes 0.9 blocks per
     tick sideways whatever the victim was doing, as long as the victim isn't sprinting. A
     victim who sprints (holds W toward you) gets a little less from the second hit on, 0.86 to
     0.88 in our combo simulation: knocked off the ground, the server's copy takes it for a
     sprint-jump and adds 1.8.9's 0.2 sprint-jump boost toward where they face, and half of
     what's left of that cancels part of the next push. A sprint hit carries a victim who
     doesn't touch the keys about 4 blocks, out of reach for most of a second, so in play the
     next hit almost always finds them back on the ground: it lifts 0.46 (sprint hit) or 0.36
     (no sprint). Right after landing it lifts less, about 0.25, because the server's copy lands
     a tick or two after the player. (Hits every 10 ticks on a victim who doesn't touch the keys
     would lift 0.46, then 0.35, then 0.02, as `tests/CombatTests.cs` checks, but nobody can
     keep up with that on flat ground: the victim is out of reach after 10 ticks.)
   - **Jump resets.** Jumping on the tick the knockback lands takes some of it off: the jump's
     0.42 lift replaces the knockback's (so you land sooner) and, sprinting, its 0.2 forward
     boost pushes back toward the attacker. After a sprint hit, holding W, you travel 1.65 blocks
     in the next half second instead of 2.54. Jumping a tick early is worse than not jumping
     (4.34: knocked back mid-air); a tick late does nothing (2.54). 1.8.9 reads the jump key once
     per tick, so these are the only outcomes. Our jump starts the instant you press it, and
     lands on exactly one of them (`tests/JumpResetTests.cs`, every 1/60 s step): pressed between
     the knockback arriving and the end of that Minecraft tick it's on time, earlier is early,
     later is late. So the perfect window is 1/60 to 1/20 s after the knockback arrives,
     depending on where in the tick it arrives (1.8.9's is a full 1/20 s, but it starts up to
     1/20 s before the knockback, since a 1.8.9 jump waits for the next tick). Before 0.4.8 a
     jump 16 to 33 ms after the knockback cut it to 0.73 to 1.14 blocks, far more than 1.8.9
     allows, and one 16 to 33 ms before it sent you 5 blocks.
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

## Our twist: combo knockback (since 0.4.8, a match rule)

1.8.9 by default. The owner wants comboing to be a bigger part of the game. The host can turn
**combo knockback** down from 100% (1.8.9) to 60% in the Twists screen: a sprint hit's extra
sideways push (1.8.9's 0.5, `Tuning.SprintKnockbackBonus`) becomes that fraction of itself. The
base push (0.4), the lift, hits without sprint knockback and the rod stay 1.8.9's. Code:
`CombatRules.Knockback`'s `comboKnockback`; the victim's game applies it.

Why this one: what decides whether a combo carries on is how far each hit sends the victim
against how fast the attacker catches up. In our combo simulation less sideways push makes
combos longer, while more lift, shorter hit protection, more reach and the strafe twists don't.
Below about 60% the victim barely moves sideways, and strafing stops working as a way out, so
the slider stops there.

One sprint hit carries a victim who doesn't touch the keys, until they stop
(`tests/CombatTests.cs`):

| Combo knockback | Carry |
|---|---|
| 100% (1.8.9) | 4.9 blocks |
| 80% | 4.4 (11% less) |
| 60% | 3.8 (22% less) |

What it does to combos, with the head start (0.4.8, [netcode.md](netcode.md)) at off, 1.8.9's
175 ms and 250 ms. The combo simulation (`COMBO_SIM=Table tools/run_tests.sh ComboSim`, about two
minutes: two of our motors on flat ground, perfect aim, the attacker's best of holding W, w-taps, s-taps,
sprint-jumping and keeping its distance, each victim response fixed; a combo breaks when the
victim gets back in reach first or the attacker can't reach within 250 ms of protection ending;
average over 3 tick timings, 8 is the cap). "Before" is 0.4.7: no head start, and the jump-reset
bug.

| Victim, after each hit | Before | Off, 100% | Off, 80% | Off, 60% | 175 ms, 100% | 175, 80% | 175, 60% | 250, 100% | 250, 80% | 250, 60% |
|---|---|---|---|---|---|---|---|---|---|---|
| no keys | 1 | 1 | 2 | 8 | 3.7 | 4.7 | 5.3 | 6 | 6.3 | 7.7 |
| holds W (runs back in) | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 |
| holds S (backs off) | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 2 | 2.3 |
| strafes (A) | 1 | 1 | 2 | 3.3 | 5 | 6 | 6.7 | 8 | 8 | 8 |
| S+A | 1 | 1 | 1 | 1 | 1 | 1 | 4 | 1 | 2.7 | 8 |
| W+A | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 6.7 | 6 | 8 |
| switches A/D every 2 to 8 ticks | 1 | 1 | 2 | 5 to 8 | 4 | 4.7 to 5 | 5.3 | 6 | 6.3 to 8 | 7.7 to 8 |
| jumps on the hit | 1 | 1 | 2 | 8 | 4.7 | 6.3 | 8 | 7.3 | 8 | 8 |
| jumps on the hit, holding W | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 | 8 |
| holds jump | 1 | 1 | 4 | 8 | 8 | 8 | 8 | 8 | 8 | 8 |
| holds jump and W | 7 | 8 | 8 | 8 | 7 | 7 | 6.3 | 8 | 8 | 8 |

The head start does most of the work: with it, letting the knockback carry you, strafing,
switching A/D and jumping on the hit stop being ways out, as in 1.8.9. Combo knockback then
lengthens combos a little more at 80% and a lot at 60%. Holding S (backing off) still ends every
combo, and running back in still gets you comboed. Real players and the bots don't play one fixed
response, so these are the shapes, not predictions (for the bots, see "Practice bots").

## W-tapping

In 1.8 the client only tells the server when sprinting *changes*, and the server switches its
own copy off after a sprint hit without telling the client. So holding W after a sprint hit
keeps you sprinting on your screen, but your next hit gets no sprint knockback until you
really stop sprinting for a tick (let go of W, press S or sneak) and start again.
`PlayerMotor.ServerSprinting` reproduces this exactly; F3 shows "Sprint knockback ready".

## Practice bots

Bots press the same controls a player does and their hits use the same rules. Modes (pause
menu): stand still, strafe, chase (moves like fight back but never swings or rods), fight back.
They respawn 2 s after dying and heal to full after 5 s without being hit. Code:
`scripts/Gameplay/BotBrain.cs` (what it decides), `scripts/Core/BotPerception.cs` (what it sees),
`scripts/Core/BotAim.cs` (its hand on the mouse), `scripts/Core/BotSkill.cs` (what each skill
number means) and `scripts/Core/Tuning.cs` (the numbers).

### How the fighting bot plays (since 0.4.7)

Like a good player with human limits, not an aimbot. The host picks its **skill** in the pause
menu, under the bot mode: **Easy**, **Normal** (the default) or **Hard**, remembered for the next
match (`--bot-skill=easy|normal|hard` on the command line).

- **It sees you late.** It sees you the way you see it: drawn the match's head start in the past
  (175 ms by default, as 1.8.9 draws everyone; its sword and rod find you there too, as yours find
  it). On top of that comes its reaction time, the brain's part (Hard 0.08 s, Normal 0.1, Easy
  0.2), as a person's comes on top of what's on their screen. It leads how you were moving then,
  but only to make up for its reaction: you're hit where you're drawn, so that's where it aims.
  Keep moving the same way and it follows you; switch strafe direction and its aim slips until it
  catches up. Good strafing makes it miss. (Up to 0.4.7 it saw you, the host's player, where you
  really were, a head start you didn't get on it.)
- **Its aim is a hand.** It follows you smoothly, closes a gap fast but not instantly, has a top
  turning speed, always wanders a little (Hard 1.5 degrees, Easy 4) and gets knocked off when you
  hit it. Up to 0.4.6 the bot turned straight at your chest at 540 degrees a second: perfect aim.
- **It clicks like a person**, irregularly: Easy about 7 times a second, Normal 11, Hard 12.5.
  Normal and Hard click only once you're in reach, so their first click lands the moment you
  come in (which wins the race for the first hit); Easy spams as you come in.
- **W-taps** (sometimes an S-tap) after its sprint hits, so the next one knocks you back fully:
  Hard always, Normal 85% of the time, Easy 40%.
- **Patience.** After landing a hit it usually waits out your hit protection instead of clicking
  into it (each click on you while sprinting slows it to 60%), keeping just outside your reach
  meanwhile, then strikes as it ends (Hard 90% of the time, Normal 60%, Easy never).
- **Strafes unpredictably**: random lengths, shorter at higher skill, sometimes circling against
  your strafe; straight lines when closing in from afar or chasing its combo. It switches more
  often under the faster and snappier strafing twists.
- **Rods** to open a fight (as you come at it), to extend its combo (catching you in the air as
  your protection ends and you fly out of sword reach) and to stop your chase when you're comboing
  it. Each chance is taken or passed on (Hard and Normal 60% of the time, Easy 30% and only to open).
- **Escapes your combos**, each a choice it makes on each of your hits, as often and as precisely
  as its skill:

  | When your hit lands, it... | Easy | Normal | Hard |
  |---|---|---|---|
  | clicks you right back (hit select) | never | 60% | 90% |
  | jumps on the exact tick (jump reset) | never | 30% | 60% |
  | holds W against the knockback | 10% | 60% | 90% |
  | strafes off to one side | never | 30% | 50% |
  | rods you as you chase | never | yes | yes |

  The hit select and the jump reset only work on the first Minecraft tick of the knockback;
  Hard gets that tick about two times in three, Normal about half the time. **Easy lets your
  knockback carry it**, so you can practise combos on it.
- **Under the wasted-clicks twist** it times its clicks instead of spamming: it only clicks when
  it reckons your protection is over and its crosshair is well on you.
- Bots click before they move within a tick, the order Minecraft handles a tick in (a person's
  click lands between our ticks, so it's the same for them). Up to 0.4.6 bots moved first, which
  made hit selecting impossible for them: by then the knockback had carried them out of reach.

### Escaping a combo, measured

How far a sprint hit sends someone who was running at the attacker, in the half second before
the attacker's next hit can land (`tests/BotTests.cs`, `TestWaysToEscapeACombo`):

| After the hit, they... | Flight | |
|---|---|---|
| let go of the keys | 4.0 blocks | |
| hold W toward the attacker | 2.5 | air control steers against it |
| hold W and click the attacker right away (hit select) | 1.1 | the 1.8.9 attack slowdown (x0.6) cuts the knockback too |
| ... and click twice | 0.6 | |
| ... click 0.2 s late | 2.1 | |
| hold W and jump on the exact tick (jump reset) | 1.7 | the sprint-jump pushes back toward the attacker |
| ... a tick late | 2.5 | already in the air: nothing |
| ... a tick early | 4.3 | worse: hit in the air |

The old bot (0.4.6) held W always and rodded at 3.5 to 8 blocks about every 1.5 s (where a sprint
hit leaves you), which worked as a built-in combo breaker. It clicked within 4.2 blocks, but
after moving, so it only cut its knockback by accident on 3% (against a precise attacker) to 10%
(bot against bot) of the hits it took.

### Measured against the old bot and each other

`tests/BotDuel.cs` fights two bot brains without a scene, through the same rules (movement,
aiming, hit protection, knockback, rod, wasted clicks, and since 0.4.8 the head start: each sees
and hits the other 175 ms in the past), starting 10 to 14 blocks apart until one dies.
`tests/LegacyBotBrain.cs` is the 0.4.6 bot, kept as a yardstick. 300 duels each (rerun with
`BOT_REPORT=300 tools/run_tests.sh BotReport`, about a minute); the 0.4.6 bot against itself comes
out at 47 to 52%, so the duels are fair.

Duels won against the 0.4.6 bot, by twist (hits landed per duel in brackets):

| | 1.8.9 | Wasted clicks 0.15 s | Strafe 130%, snappiness 50% |
|---|---|---|---|
| Easy | 0% | 6% | 0% |
| Normal | 46% (5.1 to 5.6) | 90% | 44% |
| Hard | 73% (6.2 to 4.6) | 94% | 64% |

Hard beats Normal 73% of the time and Normal beats Easy every time. The head start makes the
first hit of an exchange worth more, which narrows close matchups (in 0.4.7, without it, Hard
beat Normal 91% of the time and the 0.4.6 bot 86%). Under wasted clicks the bot wastes about a third of its
clicks where the 0.4.6 bot's spam wastes 84%, so timing beats it. With the same eyes and hands as
the bot it faces (the 0.4.6 decisions given today's aim), today's decisions win 86% (Normal) and
91% (Hard): the decisions matter, not just the aim.

In the real game, over the network on one machine (both ways round, 8 minutes each, in 0.4.7
before the head start): Hard 25 kills to the 0.4.6 bot's 17; Normal 19 to 19.

**How long your combos get.** A scripted player who sprints straight in, w-taps every hit, never
clicks into protection and clicks the moment its crosshair is on you (`tests/ComboAttacker.cs`),
with Normal's eyes and hands:

| Against | Average combo | Combos of 4+ hits | Longest combo per fight | Who wins |
|---|---|---|---|---|
| Easy | 6.1 hits | 71% (1 in 10 reach 9) | 8.1 | player 100% |
| Normal | 2.3 | 18% | 4.1 | player 65% |
| Hard | 1.8 | 10% | 3.0 | player 36% |
| 0.4.6 bot | 2.3 | 21% | 4.1 | player 55% |

With perfect aim the same player averages 6.8 on Easy, 3.6 on Normal, 2.7 on Hard and 3.6 on
the 0.4.6 bot. A kill takes 9 hits, so 9 is the longest a combo can be. These are with 1.8.9's
head start (0.4.8); without it (0.4.7) the player averaged 4.0 on Easy, 1.7 on Normal and 1.4 on
Hard, so the head start makes every bot easier to combo, Easy most.

`tests/BotTests.cs` checks what the bot sees and how it aims, when it escapes combos at each skill,
w-tapping and patience, and (with fewer duels) the comparisons above.

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
slowdown (once per tick), the w-tap rule (including clicks into hit protection), crits needing a
fall, the miss penalty against a port of `Minecraft.runTick` over 300 random clicking patterns,
and aiming (reach, border, walls, sky, the out-of-reach quirk). `tests/JumpResetTests.cs`: a
jump pressed on every 1/60 s step from two ticks before to two ticks after a knockback, with the
knockback arriving at every point of a tick, follows one of 1.8.9's flights exactly (standing,
holding W, holding A). `tools/smoke_test.sh` has bots fight across the network, one with 120 ms
of lag.
