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
   - **Hits never stop your sprint** (a deliberate change). 1.8.9's client stops it on every
     click on a player, but restarts it before you move when the sprint key is held, which our
     always-on sprint is. So movement is identical to 1.8.9 with the key held; we only skip
     1.8.9's one-tick FOV dip, and the lost sprint that double-tap-W players had to tap back.
5. **Everyone** sees the red flash (0.5 s), hears the hurt sound, sees crit sparks, and the
   victim's camera tilts: by a quarter of Minecraft's 14 degrees by default, set in Settings
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

- **It sees you late.** It aims and decides from where you were a reaction time ago (Hard 0.08 s,
  Normal 0.1, Easy 0.2), led by how you were moving then. Keep moving the same way and it follows
  you; switch strafe direction and its aim slips until it catches up. Good strafing makes it miss.
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
aiming, hit protection, knockback, rod, wasted clicks), starting 10 to 14 blocks apart until one
dies. `tests/LegacyBotBrain.cs` is the 0.4.6 bot, kept as a yardstick. 400 duels each (rerun with
`BOT_REPORT=300 tools/run_tests.sh BotReport`, about a minute); the 0.4.6 bot against itself comes
out at 48 to 51%, so the duels are fair.

Duels won against the 0.4.6 bot, by twist (its hits landed per duel in brackets):

| | 1.8.9 | Wasted clicks 0.15 s | Strafe 130%, snappiness 50% |
|---|---|---|---|
| Easy | 0% | 3% | 0% |
| Normal | 48% (6.0 to 6.3) | 98% | 28% |
| Hard | 86% (6.9 to 4.5) | 98% | 80% |

Hard beats Normal 91% of the time and Normal beats Easy every time. Under wasted clicks the bot
wastes about a quarter of its clicks where the 0.4.6 bot's spam wastes 84%, so timing beats it.
With the same eyes and hands as the bot it faces (the 0.4.6 decisions given today's aim), today's
decisions win 81% (Normal) and 98% (Hard): the decisions matter, not just the aim.

In the real game, over the network on one machine (both ways round, 8 minutes each): Hard 25
kills to the 0.4.6 bot's 17; Normal 19 to 19.

**How long your combos get.** A scripted player who sprints straight in, w-taps every hit, never
clicks into protection and clicks the moment its crosshair is on you (`tests/ComboAttacker.cs`),
with Normal's eyes and hands:

| Against | Average combo | Combos of 4+ hits | Longest combo per fight | Who wins |
|---|---|---|---|---|
| Easy | 4.0 hits | 46% (1 in 10 reach 8) | 6.8 | player 100% |
| Normal | 1.7 | 7% | 3.2 | player 89% |
| Hard | 1.4 | 1% | 2.2 | player 34% |
| 0.4.6 bot | 2.2 | 17% | 4.2 | player 92% |

With perfect aim the same player averages 5.1 on Easy, 2.8 on Normal, 1.7 on Hard and 4.6 on
the 0.4.6 bot. A kill takes 9 hits, so 9 is the longest a combo can be.

`tests/BotTests.cs` checks what the bot sees and how it aims, when it escapes combos at each skill,
w-tapping and patience, and (with fewer duels) the comparisons above.
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
