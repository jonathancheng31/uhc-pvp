# Movement

Player movement copies Minecraft 1.8.9 step for step, but runs 60 times a second instead of
20 so controls respond faster. The result lands on **exactly** the same positions as Minecraft
at every Minecraft tick (every 3rd of our ticks). Tests enforce this against a line-by-line copy
of the 1.8.9 code.

## What's covered

- Walking, sprinting, sneaking, strafing, backpedaling, diagonal movement (including
  Minecraft's 2% diagonal speed quirk).
- Sprinting is always on: moving forward sprints, as if 1.8's sprint key were always held
  (there's no sprint key). Sprint stops when you let go of W,
  sneak, or run into a wall.
- Minecraft's late wall stop: letting go of W or sneaking stops sprint at once, but a wall stops
  it on Minecraft's rhythm. 1.8.9 checks for a wall hit at the start of each 1/20 s tick, using
  the tick before's move, so the tick that hits the wall still sprints (and sets the sprint air
  acceleration). Sprint then stays off for every tick that starts against the wall, and comes
  back on the first tick after one that moved without touching it. Sliding along a wall at an
  angle matches Minecraft exactly because of this.
- Jumping, sprint-jumping (the forward boost), holding jump to bunny-hop, air control.
- Minecraft's late air sprint: in the air, starting or stopping a sprint only changes how fast
  you speed up from the next 1/20 s tick on, because 1.8.9 sets the air acceleration
  (`jumpMovementFactor`: 0.02, or 0.026 sprinting) after moving, not before. On the ground,
  sprint speed changes at once, as in Minecraft. A respawn starts with the walking value, like
  a newly spawned Minecraft player.
- Block collision (Minecraft's own box-sliding rules), ceilings, walls.
- Sneaking stops you at ledges, hanging over the edge like Minecraft.
- Camera drop while sneaking. Unlike 1.8.9, the view doesn't widen while sprinting (removed in
  0.4.2 at the owner's request).
- Walk/sprint leg and arm swing and the sneak lean on other players, using Minecraft's
  animation numbers.

## Our twist: faster strafing (since 0.4.5, a match rule)

Strafe speed is 1.8.9's by default. The host can turn up **strafe speed** in the pause menu's Twists screen (100%,
which is 1.8.9, to 150%, in 5% steps; the value is kept for their next match), and it applies to
everyone in the match, bots included. Code: `PlayerMotor.BoostStrafe`.

- **Ground only.** On the ground the sideways part of your acceleration (relative to where you
  face) is multiplied by the strafe speed. In the air nothing changes, so knockback and combos
  stay exactly 1.8.9's.
- **Never faster than before.** If that makes the total more than a straight sprint (or than
  1.8.9's own total for those keys, which is 2% more for W+A/D), forward or backward speed gives
  way. Sideways alone tops out there too. Straight ahead and straight back don't change.
- What it does, at steady speed on the ground (blocks per second):

  | Strafe speed | A or D alone | W+A/D sideways | W+A/D forward |
  |---|---|---|---|
  | 100% (1.8.9) | 4.3 | 4.1 | 4.1 |
  | 115% | 5.0 | 4.7 | 3.3 |
  | 130% | 5.6 (a sprint) | 5.3 | 2.3 |
  | 150% | 5.6 | 5.7 | 0 |

- `tests/McReference.cs` applies the same rule when its `StrafeSpeed` is set, and
  `tests/StrafeTests.cs` checks that our 60 ticks/s movement still lands exactly on it, that
  nothing beats a straight sprint at any step, and that the air is untouched.

## Our twist: snappier strafing (since 0.4.6, a match rule)

The owner wanted strafing "faster for changing directions, not just top speed". In 1.8.9 the
ground's friction already lets you stop almost at once, but reaching full speed the other way
takes a quarter second. **Strafe snappiness** (0%, which is 1.8.9, to 100%, the default since
0.4.9, the owner's pick) shortens that: on the ground, at the start of each Minecraft tick, your
sideways speed covers that fraction of the gap to the steady speed your keys lead to. Starting,
stopping and switching between A and D all get quicker; top speeds don't change. Code:
`PlayerMotor.GripStrafe`. A motor on its own (as in the parity tests) moves like plain 1.8.9; a
match sets the twists from its rules.

| Snappiness | Switching from A to D (to 90% of full speed) |
|---|---|
| Off (1.8.9) | 250 ms |
| 25% | 200 ms |
| 50% | 150 ms |
| 75% | 100 ms |
| 100% | 50 ms (one tick) |

- **Ground only**, like strafe speed: the air stays 1.8.9's.
- **Knockback stays whole:** the tick right after a knockback is left alone (then you're
  airborne), so being hit from the side while holding A or D pushes you exactly as far as in 1.8.9.
- It works with strafe speed: the steady speed it pulls toward is the boosted one.
- `McReference` applies the same rule (`StrafeGrip`, and `ReceiveVelocity` for knockback), and
  `tests/StrafeTests.cs` checks we still land exactly on it, the switch times above, unchanged top
  speeds, and the knockback path.

## Our twist: player pushing (since 0.4.7, a match rule)

In 1.8.9 players walk straight through each other. Its push code only runs on the server, and
the client overwrites the server's copy of the player every tick, so nobody is ever pushed. (The
pushes only build up invisibly in the server's copy, which a knockback halves and passes on:
a tiny extra push on a hit inside someone. We don't copy that.) Minecraft 1.9 added a gentle
push that you can still walk through.

**Player pushing** (Off, which is 1.8.9, to 100%, in 10% steps) makes bodies solid. Why: being
able to run through someone, or stand inside them, looks wrong and takes away body-blocking and
shoving as things you can do. Every game keeps its own player (and its bots) out of everyone else
as it last heard they are, teammates too, so it needs nothing extra on the network: its bots
where they really are, remote players where their latest updates put them. Not where they're
drawn: since 0.4.8 others are drawn a head start in the past (75 ms by default, 175 ms in
1.8.9, [netcode.md](netcode.md)), and pushing from there would let two players sprinting at each other
pass through before either game saw them touch. So you can be shoved by someone up to the head
start before their drawn body reaches you (at most about a block, when both move fast).
Bodies count as upright cylinders the player's width (0.6) and height, so you slide around
people instead of catching on corners.

- **Walking into someone** (your keys point within 60 degrees of them), you ease in until your
  bodies overlap by 0.2 blocks, closing a third of what's left each Minecraft tick, and stop
  there. So you stop at them instead of passing through, without a thud or a bounce.
- **Not walking into them** (standing, walking past, backing off), you slide out of any overlap.
  So running into someone who isn't pushing back shoves them along; two players walking into
  each other both stop; two players caught inside each other (a spawn, lag) slide apart.
- **Knockback always stays whole.** A player flying from a hit (sword or rod) passes through
  everyone until they land, and nobody is pushed by them; any overlap is sorted out after the
  landing. In a 1v1 a hit always sends you away from the attacker, so it never comes up there.

At 100%, measured on flat ground (`tests/PushTests.cs`):

| | |
|---|---|
| Walking, sprinting or sprint-jumping into a standing player | you stop 0.11 blocks into them; they're shoved along at 1.1 blocks/s |
| Two players sprinting head-on | both stop, 0.2 blocks into each other |
| Two players caught inside each other (spawn) | apart within 0.13 s, as a slide (never faster than 3 blocks/s each) |
| Walking past someone, brushing them | you slide around them; they're nudged aside a little |
| Walking into one player who stands in front of another | both are shoved along, in order |

**Lower settings are softer.** Overlaps part more gently, and below 60% you can push through
someone (slowly at 40 to 50%, hardly slowed at 30% and below, where it's close to Minecraft 1.9's
gentle push). From 60% up nobody gets through; the higher, the less you sink into each other:

| Setting | Walk into a standing player | Head-on | Spawned 0.1 apart, apart in | Walking through someone takes |
|---|---|---|---|---|
| 10% to 30% | through | through | 2.0 s at 10%, 1.0 s at 20%, 0.6 s at 30% | 0.9 s (as with it off) |
| 40% | through, slowed | through | 0.45 s | 1.1 s |
| 50% | through, slowed | through | 0.28 s | 2.0 s |
| 60% | stop, 0.34 overlap | stop, 0.52 | 0.22 s | can't |
| 80% | stop, 0.12 | stop, 0.29 | 0.15 s | can't |
| 100% | stop, 0.11 | stop, 0.20 | 0.10 s | can't |

**Steady, with lag too.** Each game reacts only to where it last heard the others are, and only follows
half of how fast someone backs away, so two games never chase each other's reactions back and
forth. With other players drawn 100 or 150 ms late, two players pressed together, one shoving
another, and four players pushing into each other from four sides all settle and stay still
(less than 0.002 blocks of wobble per tick, the tests' limit). Two players sprinting head-on at
that lag can overlap deeply for a moment, since each sees the other late, but each remembers which
side it came from and they settle on their own sides. Over the network, a player at 150 ms ping
sprinting head-on into the host ends 0.42 blocks apart from them, steady, on both screens.

How it's built (`Pushing`, `PlayerMotor.Contacts`, `Game.FindPushContacts`): once per Minecraft
tick, after the keys and any jump, each player's move this tick (its motion plus this tick's
acceleration) gets a limit toward each player nearby, and the smallest push away from them that
keeps every limit is added to its motion. Squeezed between several players, the corrections are
shared out so conflicting ones meet in the middle. `McReference` applies the same rule at 20
ticks a second, and `tests/PushTests.cs` checks our motor still lands on it exactly at every
Minecraft tick, plus every situation above, at every setting, with and without lag.
To see it over the network: `godot --path . -- --host --hold=w --push=100` and, in a second
game, `godot --path . -- --join=127.0.0.1 --hold=w --ping=150` run straight into each other.

Not yet: step-up onto slabs (no slabs exist), water and lava.

## How the 60-tick version matches Minecraft exactly

Code: `scripts/Movement/MotionModel.cs` (the math) and `scripts/Movement/PlayerMotor.cs`.

1. **Front-loaded acceleration.** Minecraft does `v += accel; move by v; v *= friction` once per
   tick. Running that 3x as often with scaled numbers stops 25% too slowly and starts too
   slowly. Each of our ticks instead does `p = v + b*accel; move by p + c*accel; v = fs*p`, with
   `fs = friction^(1/3)` and `b`, `c` solved so 3 of our ticks equal 1 Minecraft tick exactly.
2. **Units depend on friction.** Our stored velocity relates to Minecraft's by a factor that
   differs between ground and air friction (`FrictionRegime.ImpulseToState`). When switching
   between ground and air, velocity is converted. Pushes (sprint-jump boost, knockback later)
   are added in Minecraft units through `PlayerMotor.PushHorizontal`.
3. **Gravity is the same scheme.** Minecraft's vertical update has the same shape with
   friction 0.98 and a constant acceleration of `-0.08 * 0.98`, so jumps and falls are exact too.
4. **Minecraft's rhythm.** Minecraft only notices landing, leaving the ground, wall hits (to
   stop sprint) and tiny-speed cleanup once per 1/20 s tick, and sets the air acceleration at
   the end of each tick. We keep a phase counter and do those things on the same rhythm. The
   wall check uses the last of our three moves, which ends where Minecraft's single move ends;
   an earlier one can clip a corner or a wall's top edge that Minecraft's move clears. A jump
   starts instantly whenever you press it and starts a new Minecraft tick there (with the wall
   check and tiny-speed cleanup), so landing and the next bunny-hop fall exactly where
   Minecraft's would.
5. **Knockback lands between ticks**, like 1.8.9's velocity packet: one that arrives mid-tick
   waits for the tick to end (at most 1/30 s), and a jump can't restart a tick that knockback
   started (1.8.9 reads the jump key once per tick). So a jump around knockback follows exactly
   one of 1.8.9's paths, whichever 1/60 s step it's pressed on (jump resets, see
   [combat.md](combat.md); `tests/JumpResetTests.cs`). Before 0.4.8 knockback cut the tick in
   progress short, and a jump just after it restarted the tick, adding ground friction that
   1.8.9 never applies.

Between Minecraft's ticks our path follows the smooth curve, so a jump's true peak is a hair
(under 0.01 block) above Minecraft's tick-sampled 1.2492.

## Changing movement

Change numbers in `scripts/Core/Tuning.cs`, never in the motor. If a change is meant to differ
from Minecraft, also change `tests/McReference.cs` (or the specific test) and say why in the
commit. Run `tools/run_tests.sh Movement`.
