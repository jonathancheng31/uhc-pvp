# Combat

Melee follows Minecraft 1.8.9's code (EntityPlayer.attackTargetEntityWithCurrentItem,
EntityLivingBase.attackEntityFrom and knockBack, EntityRenderer.getMouseOver). Numbers live in
`scripts/Core/Tuning.cs`; the rules in `scripts/Combat/`; how hits travel over the network in
[netcode.md](netcode.md).

## A hit, step by step

1. **Click.** Every click is one attack attempt (no cooldown, so clicks per second matter).
   The arm swings (6 MC ticks).
2. **Aim.** From your eyes along your view: blocks are traced up to 4.5 blocks; players'
   hitboxes count 0.1 bigger on every side; a player must be closer than the first block and
   within 3 blocks. Pointing at a player beyond 3 blocks, or at nothing, is a **miss**: 1.8's
   half-second hit delay. Pointing at a nearby block is not a miss.
3. **Attacker side.** If you were sprinting, you slow to 60% and stop sprinting (your game
   does this immediately, like 1.8's client). Falling and off the ground makes it a **crit**.
4. **Victim side.** Damage = 8 (diamond sword), x1.5 if crit, x(25 - armor)/25. With the kit's
   18 armor points that's 2.24 (3.36 crit).
   - **Hit protection:** after a full hit, for 10 MC ticks (0.5 s) further hits only count if
     stronger than the last, and then only the extra damage applies, with no base knockback.
   - **Knockback:** halve the victim's current motion (all three axes), push 0.4 away from the
     attacker and 0.4 up (capped at 0.4). A sprint hit adds 0.5 along the attacker's facing and
     0.1 up. Applied to the victim's real motion by the victim's own game, so it's the same at
     any ping.
5. **Everyone** sees the red flash (0.5 s), hears the hurt sound, sees crit sparks, and the
   victim's camera tilts.

## W-tapping

In 1.8 the client only tells the server when sprinting *changes*, and the server switches its
own copy off after a sprint hit without telling the client. So holding W and sprint after a
sprint hit keeps you sprinting on your screen, but your next hit gets no sprint knockback until
you really stop sprinting for a tick (let go of W, press S, or stop sprinting) and start again.
`PlayerMotor.ServerSprinting` reproduces this exactly; F3 shows "Sprint knockback ready".

## Practice bots

`scripts/Gameplay/BotBrain.cs`. Bots press the same controls a player does and their hits use
the same rules. Modes: stand still, strafe, fight back (chases, sprints, strafes, sprint-jumps
to close distance, clicks about 9 times a second, w-taps after sprint hits, turns at up to
540 degrees per second with a little wobble). They respawn 2 s after dying and heal to full
after 5 s without being hit.

## Verified against Minecraft

`tests/CombatTests.cs`: damage and armor, hit protection timing, 9 hits to kill, the
knockback formula, knockback flights (standing, sprint hit, victim sprinting at you, victim
mid-air) identical to the 1.8.9 reference at every tick, the attacker's sprint-hit slowdown,
the w-tap rule, crits needing a fall, and aiming (reach, border, walls, sky, the out-of-reach
quirk). `tools/smoke_test.sh` has bots fight across the network, one with 120 ms of lag.
