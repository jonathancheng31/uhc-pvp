# Netcode plan

How the game handles lag. Short version: keep Minecraft's approach (each player's game
controls its own movement) and fix the two things that make lag feel unfair there:
ghost hits and inconsistent knockback.

## The problem

You always see other players slightly in the past: half your ping, plus processing,
plus a small buffer for smooth motion. Nothing removes this; the design choice is who
it's unfair to.

Minecraft 1.8.9 (checked against the decompiled 1.8.9 server code):

- **Ghosting.** The client says "I hit them" and the server accepts it if the target is
  within **6 blocks** (`NetHandlerPlayServer.processUseEntity`, squared distance < 36),
  even though reach is 3. It never checks what the attacker saw. A laggy player swings
  at where you *were* and the 3 blocks of slack let it count.
- **Inconsistent knockback.** The server computes knockback from its own copy of the
  victim (`EntityLivingBase.knockBack` halves that copy's motion first), then sends the
  victim a velocity packet to apply. The copy only follows the victim's position reports,
  so its idea of when the victim left the ground, which decides the lift, is late by the
  victim's ping. The knockback you get depends on both players' connections.
- **A built-in display delay, which turns out to matter.** Even at 0 ping 1.8.9 shows other
  players about 175 ms in the past (100 to 250 ms depending on how the ticks line up): the
  server sends positions every 2 ticks, the client glides each one in over 3 ticks, and a click
  is tested against the glide as of the last tick. Since hits are judged on the attacker's
  screen, whoever is chasing after a hit sees the victim closer than they are and gets back in
  reach first. That head start is a big part of why 1.8.9 combos work. Up to 0.4.7 we drew others
  as fresh as possible, which took it away: after a hit both players got back in reach on the same
  tick and it was a click race (see "The head start" below).

## Our approach

| Part | How it works |
|---|---|
| Movement | Each player's game owns its own movement, like 1.8. Always smooth, never corrected |
| Hits | The attacker's game decides hits from exactly what was on their screen |
| Reach | Exactly `Tuning.Reach` (3.0) plus a tiny tolerance. No 6-block slack |
| Head start | Everyone else is drawn, and hit, a fixed time in the past: 75 ms by default (the owner's pick, since 0.4.9; 1.8.9's 175 ms is the reference), a match rule from 0 to 250 ms. Network delay counts toward it, so it's the same at any ping until a connection alone needs more. It gives the chaser 1.8.9's head start after a hit. Since 0.4.8 |
| Lag cap | Each hit carries the host-clock time of the victim state the attacker was looking at. If that's older than the head start or `Tuning.LagCap` (150 ms), whichever is more (150 ms at the default head start), the hit doesn't count. Views as old as the head start are the rule; older is a connection falling behind it, which would be a ghost hit |
| Knockback | Built exactly like a 1.8.9 server builds it, from the server's copy of the victim, but that copy is emulated on the victim's own game from its own up-to-date state. Same knockback at any ping |
| Hit protection | Counted from the click times on the shared host clock, on Minecraft's 1/20 s tick grid, so lag and jitter don't change which hits count |
| Hit feedback | The attacker sees the hit, sound and predicted knockback instantly |
| Update rate | 60 states per second each way (Minecraft: 20), so others move smoothly and, with no head start, are shown as fresh as the connection allows |
| Testing | Lag simulator plus bot players, so combos can be tuned at 0/50/100/150 ms |

Tradeoff: this trusts each player's game, so it's easy to cheat. Anti-cheat isn't a goal
for now. Adding it later means moving movement and hit decisions to the host, which is
a large rework.

## How it's built

**Topology.** Everyone connects to one host over ENet (UDP). Peer-to-peer is out because
12 players would need 66 connections. The host runs inside a player's game for now;
the same code can run headless on a rented server later. Port: `Tuning.DefaultPort`.

**Messages** (`scripts/Net/Messages.cs`, sent by `scripts/Autoload/Network.cs`). Every
message is a typed record with a compact binary format, sent through one reliable and one
unreliable RPC. Malformed input is dropped, never crashes. Joining:
`HELLO` -> host assigns a team -> `WELCOME` with the roster; others get `PLAYER_JOINED`.

**Shared clock** (`NetClock`). Players ping the host twice a second and estimate the
host's clock from the fastest recent round trip. All states and hits are stamped with
host time, which is what makes the lag cap possible.

**State flow** (`scripts/Gameplay/Game.cs`). Each player sends its own `PlayerState`
(position, yaw, pitch, flags) to the host every tick. The host sends everyone a
`SNAPSHOT` of all players every tick. States are stamped with host time by a tick clock
(`TickClock`) that keeps them one tick apart even when a late frame runs two ticks back to
back, so others never stutter.

**Showing other players** (`SnapshotBuffer`, `HeadStart`, `Player.UpdateDrawn`). Everyone but
you is drawn at `host_time - view_delay`, blending between the two nearest states. The view
delay is the match's head start, or the render delay if that's more: how late a remote
player's updates usually arrive, plus a jitter margin, plus one send interval (the freshest
that doesn't stutter). The host's own bots keep their states too (`Player.History`, one a tick)
and are drawn the same way. With the head start off it's the old behaviour: as fresh as
possible.

**Lag simulator** (`LagSimulator`). `--ping=MS --jitter=MS --loss=PERCENT` delays and
drops this player's packets, half on the way out and half on the way back. Reliable
packets are never dropped and stay in order, like ENet's reliable channel.

**Hits** (`scripts/Gameplay/Game.Combat.cs`). The attacker's game aims exactly like Minecraft
(`HitScan`) against players where they're drawn on its screen (the head start in the past),
then sends an `Attack` with the host-clock time of the click and of the victim state it was
showing, plus where it saw the victim. Each player is "owned" by one game: their own, or the
host for bots. The owner checks the lag cap (the view no older than the head start, or than
`Tuning.LagCap` if that's more) and reach (with 0.5 block tolerance) against where the attacker
saw them, and hit protection (from the click's time), then applies damage and knockback (from
the victim's emulated server copy, `PlayerMotor.ServerMotion`; see [combat.md](combat.md)), and
sends a `Hurt` with the click time to everyone. The knockback pushes away from the attacker as
the two really stood at the click (the victim's own recorded position then), as a 1.8.9 server
does with its copies, not from the attacker's delayed view. Clients route through the host,
which only relays messages from the right owner.

**Fishing rod** (`scripts/Gameplay/Game.Rod.cs`, details in [rod.md](rod.md)). A throw sends a
`RodCast` (host time, start point and launch motion) to everyone, and every game flies the same
bobber from it. Someone else's bobber is shown on the same delay as they are, so it leaves their
hand as drawn; the host's bots' bobbers too. The thrower's game checks the bobber's path against
players where they're drawn on its screen (a bot's, where it sees them: the head start in the
past, as for its sword); when it catches someone it sends a `RodHook` to everyone (to show it hanging on
them) and an `Attack` with the `Rod` flag to the victim's owner, which checks the lag cap, that
the thrower was within the line's 32 blocks, and hit protection, then applies the knockback like
a sword hit's. If the thrower's guess of the victim's hit protection says the hit won't count,
the bobber flies on through instead.

**Instant feedback.** Hitting a player whose game is elsewhere shows the red flash and hurt
sound immediately, unless the attacker's game expects the hit not to count: it keeps a guess of
every remote player's hit protection from the click times in the hits it makes and the `Hurt`
messages it hears (exact in a 1v1). The same guess decides whether the attacker's sprint
knockback is used up. The confirming `Hurt` doesn't replay the effects. Against host-run bots on the host, everything is local and instant.

**Bots** are owned by the host's game and appear to everyone else as normal players in snapshots.
They see everyone the way a person at the host's screen does, through the head start (the host's
own player too), and their sword and rod find people there; their reaction time comes on top, as
a person's does (see [combat.md](combat.md), "Practice bots").

**Match rules** (`MatchRules`, our twists: strafe speed, strafe snappiness, wasted clicks, player
pushing, combo knockback, head start). The host picks them in the pause menu's Twists screen and sends a
`RulesUpdate` to each player as they join and to everyone when they change. Every game moves its
own player and decides its own clicks, so they must all use the same rules; a client takes them
only from the host.

**Player pushing** needs no messages of its own: each game keeps its own player (and the host its
bots) out of the other players where it last heard they are: our bots where they really are,
remote players where their latest updates put them (their render delay, without the head start),
and the other games do the same from their side. Each follows only half of how fast the others
back away, and remembers which side of someone it came from, so under lag two games never chase
each other's reactions back and forth and nobody swaps sides (details and measurements in
[movement.md](movement.md)). The one thing it sends is a flag in each player's state while they
fly from a hit, so every game lets them pass through. Pushing ignores the head start on
purpose: with 175 ms of it, two players sprinting at each other would pass through before either
game saw them touch. The cost: you can be shoved by someone up to the head start before their
drawn body reaches you (at most about a block, when both move fast).

## The head start (1.8.9's display delay, since 0.4.8)

The owner: "comboing feels kinda hard. i want comboing to be a bigger part of the game." The main
cause was that we had removed 1.8.9's display delay (above). The fix brings it back, on by
default, as a match rule the host can turn down to off or up to 250 ms (Twists screen, "Head
start"; `MatchRules.HeadStart`). 0.4.8 started at 1.8.9's 175 ms (`Tuning.McHeadStart`, kept as
the reference and in plain 1.8, `MatchRules.Minecraft`). Since 0.4.9 the default is **75 ms**
(`Tuning.DefaultHeadStart`): the owner played 0.4.8 and picked it (2026-10-23, "these feel
good"). In the combo simulation, 100 ms already gave no longer combos than none against a victim
who lets the knockback carry them (175 ms gave about 4).

**What 1.8.9 does, measured.** `tests/McReference.cs` ports the server's entity tracker
(`EntityTrackerEntry`: positions every 2 ticks or sooner after a jump, a hit or a change of
health or flags, rounded down to 1/32 block, sent only after moving 4/32, a teleport when the
on-ground flag changes) and the client's glide (`EntityOtherPlayerMP`: a third of the way, then
half the rest, then the rest, one step a tick), with the three tick grids (the player's client,
the server, the watching client) at any phase (`McView`). Over every phase and click moment, at 0
ping, a click finds a sprinting player 175 ms in the past on average (101 to 252 ms); the screen
shows them 200 ms in the past (`tests/HeadStartTests.cs`).

**What we do.** A fixed delay for everyone. 1.8.9's 175 ms is the average where its clicks find
people; the default is shorter, 75 ms (above), and the numbers below are for 175 ms, 1.8.9's. We
draw and hit-test the same position (what you see is what you hit), 25 ms fresher on screen than
1.8.9's, so the clicks keep 1.8.9's timing. Chosen over porting the glide itself because it
gives the same combos (the combo simulation, best attacker against each victim response: no keys
3.7 hits either way, strafing 5.0 against 4.7 for the port, jump on hit 4.7 against 4.3, the
rest within a third of a hit), works as a slider from off to 250 ms, keeps others moving
smoothly at 60 states a second, and is the same every time where 1.8.9's varied from 100 to 250
ms with tick timing. Drawn 175 ms back, players sit 0.1 to 0.3 blocks (on average) from where
1.8.9 would draw them while running, strafing or being comboed, against 0.4 to 1.7 when drawn
fresh.

**At any ping.** A remote player's network delay counts toward the head start: at 175 ms, someone
whose updates arrive 60 ms late is still shown 175 ms back, not 235. Only when a connection alone
needs more than the head start is that player shown later, and F3 says who: at 175 ms, round
trips over about 300 ms; at the default 75 ms, much sooner, from updates arriving about 60 ms
late (a round trip of about 120 ms to the host, less between two remote players). 1.8.9's delay
grows with every millisecond of ping; ours doesn't until then.

**What a victim notices.** You're hit where you were a head start ago (75 ms by default, 175 ms
in 1.8.9): after strafing out of reach on your screen you can still be hit for a moment. The red
flash, hurt sound and knockback come at once (the knockback straight from the attacker's click,
for the host's bots), while the attacker's body and swing show where they were a head start ago,
like 1.8.9. A bot you hit flashes red at once and starts flying a head start later.

**At high ping.** Hits are judged on the attacker's screen, so the lag cap now allows views up to
the head start or 150 ms, whichever is more, and no older. At the default 75 ms head start that's
150 ms (at 1.8.9's 175 ms, 175): an attacker whose view of you falls more than 150 ms behind has
their hits thrown out, which stops ghost hits from very laggy players. Views between the head
start and the cap come from connections that need more than the head start (above).

**What uses the delayed view, and what doesn't.**
- Sword hits and the rod's catches: the delayed view (what's on the attacker's screen, or a bot's
  view through the same delay).
- Crit sparks, the red flash, hurt sounds, footsteps and the bobber hanging on someone: where the
  player is drawn.
- Knockback's direction: where the two really were at the click (see Hits).
- Hit protection, wasted clicks and the attacker's guess of someone's protection: click times on
  the host clock, so the delay doesn't touch them.
- Practice bots: see everyone through the delay, then react (combat.md).
- Pushing: the freshest positions (above), not the delayed view.
- Deaths and respawns (since 0.4.10): shown on the delay too. Whoever sees a player in the past
  sees their body until their view reaches the respawn, then the player at their spawn, with no
  sliding between; a host bot falls and gets up a head start after it happens, like everything
  else about it. A player can be hit or caught only where they're seen alive, both read from the
  same moment of the view (`Sighting`), so a swing or a dropped bobber can't land on their old
  body the moment they respawn (before, every bot respawn in a practice fight took one, 25 to 40
  blocks from the bot). Nor once this game knows they've died, from their states or the hit that
  killed them, even while they're still drawn alive: their game would turn it down anyway. Their
  game also turns down a hit whose view of them is from before their latest respawn (a tick's
  allowance). If every state showing a death is lost on the way (a network stall), a state
  showing them alive more than half the shortest respawn after the kill counts as the respawn,
  so nobody stays unhittable.

**Measured** (combo simulation, best attacker strategy, average combo of 3 tick phases, 8 is the
cap; full table in [combat.md](combat.md), under combo knockback): before, holding no keys,
strafing, switching A/D or jumping on the hit all held a combo to 1 hit; with 1.8.9's head start
they take 3.7 to 5.0 (no keys 3.7, A 5.0, A/D 4.0, jump on hit 4.7), and 6 to 8 at 250 ms. Holding
S still breaks every combo; running back in still gets you comboed.

## Still to build

- Predicted knockback on the attacker's screen, blending into the real path (hits on remote
  players currently fly back after one round trip).
- Steam relay networking (internet play milestone).

## References

- Riot, [Peeking into VALORANT's Netcode](https://www.riotgames.com/en/news/peeking-valorants-netcode): peeker's advantage measured at ~141 ms, cut to ~71 ms; rewind caps.
- Valve, [Lag Compensation](https://developer.valvesoftware.com/wiki/Lag_Compensation) and [Interpolation](https://developer.valvesoftware.com/wiki/Interpolation).
- Gabriel Gambetta, [Fast-Paced Multiplayer](https://gabrielgambetta.com/client-side-prediction-server-reconciliation.html).
- [KnockbackSync](https://github.com/CASELOAD7000/knockback-sync): a Minecraft plugin that fixes ping-dependent knockback.
- Infil, [Netcode explained](https://words.infil.net/w02-netcode-p5.html): delay-based vs rollback.
