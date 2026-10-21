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
- Jumping, sprint-jumping (the forward boost), holding jump to bunny-hop, air control.
- Block collision (Minecraft's own box-sliding rules), ceilings, walls.
- Sneaking stops you at ledges, hanging over the edge like Minecraft.
- Sprint FOV change (×1.15, eased in like Minecraft), camera drop while sneaking.
- Walk/sprint leg and arm swing and the sneak lean on other players, using Minecraft's
  animation numbers.

Not yet: step-up onto slabs (no slabs exist), pushing between players, water and lava.

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
4. **Minecraft's rhythm.** Minecraft only notices landing, leaving the ground and tiny-speed
   cleanup once per 1/20 s tick. We keep a phase counter and do those things on the same
   rhythm. A jump starts instantly whenever you press it and restarts the rhythm, so landing
   and the next bunny-hop fall exactly where Minecraft's would.

Between Minecraft's ticks our path follows the smooth curve, so a jump's true peak is a hair
(under 0.01 block) above Minecraft's tick-sampled 1.2492.

## Changing movement

Change numbers in `scripts/Core/Tuning.cs`, never in the motor. If a change is meant to differ
from Minecraft, also change `tests/McReference.cs` (or the specific test) and say why in the
commit. Run `tools/run_tests.sh Movement`.
