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
- **Inconsistent knockback.** The server computes knockback from its out-of-date view of
  the victim (`EntityLivingBase.knockBack` halves the victim's current motion first),
  then sends the victim a velocity packet to apply. The knockback you get depends on
  both players' connections.

## Our approach

| Part | How it works |
|---|---|
| Movement | Each player's game owns its own movement, like 1.8. Always smooth, never corrected |
| Hits | The attacker's game decides hits from exactly what was on their screen |
| Reach | Exactly `Tuning.REACH` (3.0) plus a tiny tolerance. No 6-block slack |
| Lag cap | Each hit carries the host-clock time of the victim state the attacker was looking at. If that's older than `Tuning.LAG_CAP` (150 ms to start), the hit doesn't count. This stops ghost hits |
| Knockback | The victim's game applies it using its own real, current motion, so it's the same at any ping |
| Hit feedback | The attacker sees the hit, sound and predicted knockback instantly |
| Update rate | 60 states per second each way (Minecraft: 20), so others are shown closer to where they are |
| Testing | Lag simulator plus bot players, so combos can be tuned at 0/50/100/150 ms |

Tradeoff: this trusts each player's game, so it's easy to cheat. Anti-cheat isn't a goal
for now. Adding it later means moving movement and hit decisions to the host, which is
a large rework.

## How it's built

**Topology.** Everyone connects to one host over ENet (UDP). Peer-to-peer is out because
12 players would need 66 connections. The host runs inside a player's game for now;
the same code can run headless on a rented server later. Port: `Tuning.DEFAULT_PORT`.

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
the lag cap (view no older than `Tuning.LagCap`) and reach (with 0.5 block tolerance), then
applies damage and knockback to the victim's real current motion, and sends a `Hurt` to
everyone. Clients route through the host, which only relays messages from the right owner.

**Instant feedback.** Hitting a player whose game is elsewhere shows the red flash and hurt
sound immediately (unless they were hit in the last half second); the confirming `Hurt` doesn't
replay them. Against host-run bots on the host, everything is local and instant.

**Bots** are owned by the host's game and appear to everyone else as normal players in snapshots.

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
