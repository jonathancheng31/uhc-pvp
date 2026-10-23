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

## Our approach

| Part | How it works |
|---|---|
| Movement | Each player's game owns its own movement, like 1.8. Always smooth, never corrected |
| Hits | The attacker's game decides hits from exactly what was on their screen |
| Reach | Exactly `Tuning.Reach` (3.0) plus a tiny tolerance. No 6-block slack |
| Lag cap | Each hit carries the host-clock time of the victim state the attacker was looking at. If that's older than `Tuning.LagCap` (150 ms to start), the hit doesn't count. This stops ghost hits |
| Knockback | Built exactly like a 1.8.9 server builds it, from the server's copy of the victim, but that copy is emulated on the victim's own game from its own up-to-date state. Same knockback at any ping |
| Hit protection | Counted from the click times on the shared host clock, on Minecraft's 1/20 s tick grid, so lag and jitter don't change which hits count |
| Hit feedback | The attacker sees the hit, sound and predicted knockback instantly |
| Update rate | 60 states per second each way (Minecraft: 20), so others are shown closer to where they are |
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
`SNAPSHOT` of all players every tick.

**Showing other players** (`SnapshotBuffer`). Each other player is drawn at
`host_time - render_delay`, blending between the two nearest states. The render delay
adapts per player: how late their updates usually arrive, plus a jitter margin, plus
one send interval. That's as fresh as possible without stuttering, which keeps
ghosting small.

**Lag simulator** (`LagSimulator`). `--ping=MS --jitter=MS --loss=PERCENT` delays and
drops this player's packets, half on the way out and half on the way back. Reliable
packets are never dropped and stay in order, like ENet's reliable channel.

**Hits** (`scripts/Gameplay/Game.Combat.cs`). The attacker's game aims exactly like Minecraft
(`HitScan`) against players where they're drawn on its screen, then sends an `Attack` with the
host-clock time of the click and of the victim state it was showing, plus where it saw the
victim. Each player is "owned" by one game: their own, or the host for bots. The owner checks
the lag cap (view no older than `Tuning.LagCap`) and reach (with 0.5 block tolerance) and hit
protection (from the click's time), then applies damage and knockback (from the victim's
emulated server copy, `PlayerMotor.ServerMotion`; see [combat.md](combat.md)), and sends a
`Hurt` with the click time to everyone. Clients route through the host, which only relays
messages from the right owner.

**Fishing rod** (`scripts/Gameplay/Game.Rod.cs`, details in [rod.md](rod.md)). A throw sends a
`RodCast` (host time, start point and launch motion) to everyone, and every game flies the same
bobber from it. Someone else's bobber is shown on the same delay as they are, so it leaves their
hand as drawn. The thrower's game checks the bobber's path against players where they're drawn
on its screen; when it catches someone it sends a `RodHook` to everyone (to show it hanging on
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

**Match rules** (`MatchRules`, our twists: strafe speed, strafe snappiness, wasted clicks, player
pushing, combo knockback). The host picks them in the pause menu's Twists screen and sends a
`RulesUpdate` to each player as they join and to everyone when they change. Every game moves its
own player and decides its own clicks, so they must all use the same rules; a client takes them
only from the host.

**Player pushing** needs no messages of its own: each game keeps its own player (and the host its
bots) out of the other players where it draws them, so you're pushed by the bodies you see, and
the other games do the same from their side. Each reacts only to where the others are drawn and
follows only half of how fast they back away, and remembers which side of someone it came from,
so under lag two games never chase each other's reactions back and forth and nobody swaps sides
(details and measurements in [movement.md](movement.md)). The one thing it sends is a flag in each
player's state while they fly from a hit, so every game lets them pass through.

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
