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

1.8.9 movement is the default. The host can turn up **strafe speed** in the pause menu's Twists screen (100%,
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
takes a quarter second. **Strafe snappiness** (0%, which is 1.8.9, to 100%) shortens that: on the
ground, at the start of each Minecraft tick, your sideways speed covers that fraction of the gap
to the steady speed your keys lead to. Starting, stopping and switching between A and D all get
quicker; top speeds don't change. Code: `PlayerMotor.GripStrafe`.

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

**Player pushing** (Off, which is 1.8.9, to 200%) makes players solid-ish: each game pushes its
own player (and its bots) away from anyone whose box overlaps it, teammates too, harder the deeper
the overlap. Others are pushed by their own games, so it needs nothing over the network.

- At **100%**: at full overlap the push is 0.3 blocks per Minecraft tick. Nobody can run
  through anyone (walking, sprinting, head-on, sprint-jumping), and running into a standing
  player shoves them along at about 2.4 blocks/s. Two players spawned inside each other slide
  apart within a quarter second, as a shove, never a launch.
- At low values it's soft: at 20% a sprinting player still gets through, slowed.
- **In the air the push is halved**, since the air keeps speed far longer than the ground.
- It's added once per Minecraft tick, after the move, like Minecraft 1.9's. A knockback arriving
  at the same time replaces it, so **knockback stays whole**.
- Code: `Pushing.Away` (the push from one player on another), `Game.PushOn` (everyone around a
  player), `PlayerMotor.Push`. `tests/PushTests.cs` checks the shape of the push, the scenarios
  above, and that our 60 ticks/s motor adds it exactly where a 20 ticks/s player would.

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
   the end of each tick. We keep a phase counter and do those things on the same rhythm (a
   knockback that arrives mid-tick ends the tick in progress). The wall check uses the last of
   our three moves, which ends where Minecraft's single move ends; an earlier one can clip a
   corner or a wall's top edge that Minecraft's move clears. A jump starts instantly whenever
   you press it and starts a new Minecraft tick there (with the wall check and tiny-speed
   cleanup), so landing and the next bunny-hop fall exactly where Minecraft's would.

Between Minecraft's ticks our path follows the smooth curve, so a jump's true peak is a hair
(under 0.01 block) above Minecraft's tick-sampled 1.2492.

## Changing movement

Change numbers in `scripts/Core/Tuning.cs`, never in the motor. If a change is meant to differ
from Minecraft, also change `tests/McReference.cs` (or the specific test) and say why in the
commit. Run `tools/run_tests.sh Movement`.
