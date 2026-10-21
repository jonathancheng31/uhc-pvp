using System;
using System.Collections.Generic;
using UhcPvp.Core;

namespace UhcPvp.Movement;

/// <summary>What the player is pressing this tick.</summary>
/// <param name="Forward">+1 for forward (W), -1 for back (S).</param>
/// <param name="Strafe">+1 for left (A), -1 for right (D).</param>
/// <param name="Yaw">Facing in degrees; 0 faces -Z, positive turns left.</param>
public readonly record struct MovementInput(
    double Forward, double Strafe, bool Jump, bool Sprint, bool Sneak, double Yaw);

/// <summary>
/// Player movement, following Minecraft 1.8.9 (EntityPlayerSP, EntityLivingBase and Entity
/// .moveEntity) step for step, at 60 ticks per second. <see cref="MotionModel"/> converts the
/// numbers so positions match Minecraft at every Minecraft tick; tests compare against an
/// exact copy of the 1.8.9 code (tests/McReference.cs).
/// </summary>
public sealed class PlayerMotor
{
    private static readonly int N = MotionModel.N;

    private readonly ICollisionWorld _world;
    private readonly List<Box> _boxes = new();
    private int _jumpCooldown;
    private int _groundHold;
    private int _sprintToggleTimer;
    private double _previousForward;
    private bool _previousSneak;
    /// <summary>Which of the N subticks of a Minecraft tick comes next (0 = a tick starts).</summary>
    private int _phase;
    /// <summary>Whether the last move ended resting on something.</summary>
    private bool _touchingGround;
    private FrictionRegime _lastRegime = MotionModel.Ground;
    /// <summary>The last position we reported to the (emulated) server; where its copy of us stands.</summary>
    private Vec3d _reportedPosition;
    private int _ticksSinceReport;
    /// <summary>The on-ground flag of our last report.</summary>
    private bool _reportedOnGround;
    private double _yaw;

    public PlayerMotor(ICollisionWorld world, Vec3d position)
    {
        _world = world;
        Position = position;
        _reportedPosition = position;
    }

    /// <summary>Feet position: center of the player's box, at its bottom.</summary>
    public Vec3d Position { get; private set; }
    /// <summary>Velocity in blocks per subtick (our internal units; see <see cref="FrictionRegime"/>).</summary>
    public Vec3d Velocity { get; private set; }
    /// <summary>How far the player actually moved in the last tick.</summary>
    public Vec3d LastMove { get; private set; }
    /// <summary>
    /// Standing on the ground, as Minecraft sees it: Minecraft only notices landing or leaving
    /// the ground at its 1/20 s ticks, and that rhythm decides friction and when you can jump
    /// again. Updated at the start of each Minecraft tick from the actual contact.
    /// </summary>
    public bool OnGround { get; private set; }
    /// <summary>Whether the player is physically resting on something right now.</summary>
    public bool IsTouchingGround => _touchingGround;
    public bool Sprinting { get; private set; }
    public bool Sneaking { get; private set; }
    public bool CollidedHorizontally { get; private set; }
    /// <summary>How far the player has fallen since last on the ground (for critical hits).</summary>
    public double FallDistance { get; private set; }

    /// <summary>
    /// Whether Minecraft's server would think we're sprinting. The client tells the server when
    /// sprinting starts or stops (once per tick, only on a change), and the server switches its
    /// copy off after a sprint hit without telling the client. So after one sprint hit, the next
    /// only gets sprint knockback once the player has really stopped and restarted sprinting;
    /// that's why w-tapping works in 1.8. See docs/combat.md.
    /// </summary>
    public bool ServerSprinting { get; private set; }
    private bool _toldServerSprinting;

    /// <summary>
    /// Motion of the server's copy of this player (EntityPlayerMP), in Minecraft units. 1.8.9
    /// builds knockback from this, not from the player's real motion. The server never sees the
    /// player's keys, so its copy doesn't move sideways; vertically it sits at -0.0784 on the
    /// ground and, once it sees the player leave the ground, it starts its own jump and keeps
    /// falling until its copy touches down. That's why a combo's knockback is the same sideways
    /// every hit while the lift shrinks as the victim stays airborne. Updated once per Minecraft
    /// tick, as the server would from our position report. See docs/combat.md.
    /// </summary>
    public Vec3d ServerMotion { get; private set; }

    /// <summary>Minecraft 1.8 critical hit: falling and not on the ground.</summary>
    public bool CanCrit => FallDistance > 0 && !_touchingGround;

    public Box Bounds => Box.ForPlayer(Position, Tuning.PlayerWidth, Tuning.PlayerHeight);

    /// <summary>Horizontal speed over the last tick, in blocks per second.</summary>
    public double HorizontalSpeed => LastMove.HorizontalLength * Tuning.SimRate;

    public PlayerFlags Flags =>
        (OnGround ? PlayerFlags.OnGround : 0)
        | (Sprinting ? PlayerFlags.Sprinting : 0)
        | (Sneaking ? PlayerFlags.Sneaking : 0);

    /// <summary>Moves the player somewhere and stops all motion (spawning, respawning).</summary>
    public void Teleport(Vec3d position)
    {
        Position = position;
        // Zero in our units is Minecraft's "standing on the ground" motion (0, -0.0784, 0).
        Velocity = Vec3d.Zero;
        LastMove = Vec3d.Zero;
        _groundHold = 0;
        _touchingGround = false;
        OnGround = false;
        Sprinting = false;
        FallDistance = 0;
        // A fresh copy on the server too.
        ServerMotion = Vec3d.Zero;
        _reportedPosition = position;
        _ticksSinceReport = 0;
        _reportedOnGround = false;
    }

    /// <summary>
    /// Adds a horizontal push given in Minecraft units (blocks per MC tick), such as the
    /// sprint-jump boost or knockback. It moves the player exactly as far as Minecraft's would.
    /// </summary>
    public void PushHorizontal(Vec3d mcImpulse)
    {
        // Velocity is stored in the units of the friction used last; a switch to the other
        // friction converts it (and this push) on the next move.
        double toState = _lastRegime.ImpulseToState;
        Velocity += new Vec3d(mcImpulse.X * toState, 0, mcImpulse.Z * toState);
    }

    /// <summary>Motion in Minecraft units (motionX/Y/Z, blocks per MC tick), as of the start of the next tick.</summary>
    public Vec3d McMotion
    {
        get
        {
            double k = _lastRegime.ImpulseToState;
            return new Vec3d(Velocity.X / k, MotionModel.McVerticalFromState(Velocity.Y), Velocity.Z / k);
        }
    }

    /// <summary>
    /// Replaces the motion with one in Minecraft units, the way a Minecraft client applies a
    /// velocity packet (knockback) between ticks: the next tick starts fresh from it.
    /// </summary>
    public void SetMcMotion(Vec3d motion)
    {
        double k = _lastRegime.ImpulseToState;
        Velocity = new Vec3d(motion.X * k, MotionModel.VerticalStateFromMc(motion.Y), motion.Z * k);
        _phase = 0;
        _groundHold = 0;
    }

    /// <summary>
    /// The attacker's side of a click on a player. The attacker's own game treats every such
    /// click as a hit: a sprinting attacker slows to 60% and stops sprinting, even when the
    /// victim was protected. The server only uses up its sprint flag (and slows its copy) when
    /// the hit counted (<paramref name="accepted"/>: a full or stronger hit), so a click into
    /// hit protection keeps the next hit's sprint knockback.
    /// </summary>
    public void OnHitLanded(bool accepted)
    {
        if (Sprinting)
        {
            Velocity = new Vec3d(Velocity.X * Tuning.SprintHitSlowdown, Velocity.Y, Velocity.Z * Tuning.SprintHitSlowdown);
            Sprinting = false;
        }
        if (accepted && ServerSprinting)
        {
            ServerSprinting = false;
            ServerMotion = new Vec3d(
                ServerMotion.X * Tuning.SprintHitSlowdown, ServerMotion.Y, ServerMotion.Z * Tuning.SprintHitSlowdown);
        }
    }

    /// <summary>
    /// A push that only reaches the server's copy: a sprint hit that landed during hit
    /// protection sends the victim nothing, but stays on the copy and adds to the next knockback.
    /// </summary>
    public void PushServerCopy(Vec3d mcImpulse) => ServerMotion += mcImpulse;

    /// <summary>Ground friction while standing (or on the tick of a jump), air friction otherwise.</summary>
    private FrictionRegime CurrentRegime => OnGround || _groundHold > 0 ? MotionModel.Ground : MotionModel.Air;

    /// <summary>Advances one tick (1/60 s).</summary>
    public void Step(in MovementInput input)
    {
        bool tickStart = _phase == 0;
        _yaw = input.Yaw;
        if (tickStart)
        {
            OnGround = _touchingGround;
        }

        // --- Input and sprinting (EntityPlayerSP.onLivingUpdate) ---
        if (_sprintToggleTimer > 0)
        {
            _sprintToggleTimer--;
        }
        bool wasSneaking = _previousSneak;
        bool wasHoldingForward = _previousForward >= Tuning.SprintForwardThreshold;
        double forward = Math.Clamp(input.Forward, -1, 1);
        double strafe = Math.Clamp(input.Strafe, -1, 1);
        if (input.Sneak)
        {
            forward *= Tuning.SneakInputMultiplier;
            strafe *= Tuning.SneakInputMultiplier;
        }
        _previousForward = forward;
        _previousSneak = input.Sneak;
        Sneaking = input.Sneak;

        bool holdingForward = forward >= Tuning.SprintForwardThreshold;
        if (OnGround && !wasSneaking && !wasHoldingForward && holdingForward && !Sprinting)
        {
            // Double-tap W: the first tap arms the timer, a second tap within the window sprints.
            if (_sprintToggleTimer <= 0 && !input.Sprint)
            {
                _sprintToggleTimer = Tuning.DoubleTapSprintWindowMcTicks * N;
            }
            else
            {
                Sprinting = true;
            }
        }
        if (!Sprinting && holdingForward && input.Sprint)
        {
            Sprinting = true;
        }
        if (Sprinting && (!holdingForward || CollidedHorizontally))
        {
            Sprinting = false;
        }

        // --- Jumping and tiny-speed cleanup (EntityLivingBase.onLivingUpdate) ---
        if (_jumpCooldown > 0)
        {
            _jumpCooldown--;
        }
        if (tickStart)
        {
            // Minecraft zeroes speeds under 0.005 blocks/tick once per tick; same threshold here,
            // converted to our units.
            double horizontal = Tuning.MinMotion * _lastRegime.ImpulseToState;
            double vy = Velocity.Y;
            if (Math.Abs(MotionModel.McVerticalFromState(vy)) < Tuning.MinMotion)
            {
                vy = MotionModel.VerticalStateFromMc(0);
            }
            Velocity = new Vec3d(SnapSmall(Velocity.X, horizontal), vy, SnapSmall(Velocity.Z, horizontal));
        }
        if (input.Jump)
        {
            if (OnGround && _jumpCooldown == 0)
            {
                // Jumps start instantly; the Minecraft-tick rhythm restarts from the jump, so
                // the landing and the next hop fall exactly where Minecraft's would.
                _phase = 0;
                Jump(input.Yaw);
                _jumpCooldown = Tuning.JumpCooldownMcTicks * N;
            }
        }
        else
        {
            _jumpCooldown = 0;
        }
        forward *= Tuning.InputScale;
        strafe *= Tuning.InputScale;

        // --- Acceleration, collision and friction (EntityLivingBase.moveEntityWithHeading) ---
        bool groundFriction = OnGround || _groundHold > 0;
        FrictionRegime regime = CurrentRegime;
        if (_groundHold > 0)
        {
            _groundHold--;
        }
        if (regime != _lastRegime)
        {
            // Speeds are stored in units that depend on the friction (see FrictionRegime), so
            // convert when switching between ground and air. Minecraft's speed carries over as is.
            double scale = regime.ImpulseToState / _lastRegime.ImpulseToState;
            Velocity = new Vec3d(Velocity.X * scale, Velocity.Y, Velocity.Z * scale);
            _lastRegime = regime;
        }
        double speedMultiplier = Sprinting ? Tuning.SprintSpeedMultiplier : 1.0;
        double acceleration = groundFriction
            ? MotionModel.GroundAcceleration(Tuning.WalkSpeed * speedMultiplier)
            : Tuning.AirAcceleration * (Sprinting ? 1 + Tuning.SprintAirAccelerationBonus : 1);
        Vec3d accel = MoveFlying(strafe, forward, acceleration, input.Yaw);

        double px = Velocity.X + regime.StateGain * accel.X;
        double pz = Velocity.Z + regime.StateGain * accel.Z;
        double dx = px + regime.Feedthrough * accel.X;
        double dz = pz + regime.Feedthrough * accel.Z;

        FrictionRegime vertical = MotionModel.Vertical;
        double py = Velocity.Y + vertical.StateGain * MotionModel.GravityInput;
        double dy = py + vertical.Feedthrough * MotionModel.GravityInput;

        (bool blockedX, bool blockedY, bool blockedZ) = Move(dx, dy, dz);
        if (blockedX)
        {
            px = 0;
        }
        if (blockedZ)
        {
            pz = 0;
        }
        if (blockedY)
        {
            py = 0;
        }
        Velocity = new Vec3d(px * regime.SubFriction, py * vertical.SubFriction, pz * regime.SubFriction);
        _phase = (_phase + 1) % N;
        if (_phase == 0)
        {
            // End of a Minecraft tick: the client reports a change in sprinting to the server,
            // then its position.
            if (Sprinting != _toldServerSprinting)
            {
                _toldServerSprinting = Sprinting;
                ServerSprinting = Sprinting;
            }
            ReportToServer();
        }
    }

    /// <summary>
    /// What the server does with our end-of-tick report (EntityPlayerSP.onUpdateWalkingPlayer,
    /// then NetHandlerPlayServer.processPlayer), as far as its copy's motion goes.
    /// </summary>
    private void ReportToServer()
    {
        // The position is only sent after moving 0.03 blocks, or every 20 ticks.
        Vec3d last = _reportedPosition;
        Vec3d moved = Position - last;
        bool sendsPosition = moved.X * moved.X + moved.Y * moved.Y + moved.Z * moved.Z
            > Tuning.PositionReportDistance * Tuning.PositionReportDistance
            || _ticksSinceReport >= Tuning.PositionReportMaxTicks;
        _ticksSinceReport++;
        Vec3d reported = last;
        if (sendsPosition)
        {
            reported = Position;
            _reportedPosition = Position;
            _ticksSinceReport = 0;
        }

        // The server runs one tick of its copy with no keys pressed, from the last position
        // (EntityLivingBase.onLivingUpdate and moveEntityWithHeading), then puts it back there.
        Vec3d m = ServerMotion;
        m = new Vec3d(SnapSmall(m.X, Tuning.MinMotion), SnapSmall(m.Y, Tuning.MinMotion), SnapSmall(m.Z, Tuning.MinMotion));
        double friction = _reportedOnGround ? Tuning.BlockSlipperiness * Tuning.AirFriction : Tuning.AirFriction;
        Box box = Box.ForPlayer(last, Tuning.PlayerWidth, Tuning.PlayerHeight);
        (double cx, double cy, double cz) = Clip(box, m.X, m.Y, m.Z);
        bool copyOnGround = cy != m.Y && m.Y < 0;
        m = new Vec3d(
            cx != m.X ? 0 : m.X * friction,
            ((cy != m.Y ? 0 : m.Y) - Tuning.Gravity) * Tuning.VerticalDrag,
            cz != m.Z ? 0 : m.Z * friction);

        // Seeing us leave the ground, the server makes its copy jump.
        if (copyOnGround && !_touchingGround && reported.Y - last.Y > 0)
        {
            m = m.WithY(Tuning.JumpVelocity);
            if (ServerSprinting)
            {
                m += Vec3d.Forward(_yaw) * Tuning.SprintJumpBoost;
            }
        }

        // Then it moves the copy to the reported position (normally a free move).
        Vec3d delta = reported - last;
        (double dx, double dy, double dz) = Clip(box, delta.X, delta.Y, delta.Z);
        ServerMotion = new Vec3d(dx != delta.X ? 0 : m.X, dy != delta.Y ? 0 : m.Y, dz != delta.Z ? 0 : m.Z);
        _reportedOnGround = _touchingGround;
    }

    private void Jump(double yaw)
    {
        Velocity = Velocity.WithY(MotionModel.VerticalStateFromMc(Tuning.JumpVelocity));
        // Minecraft treats the whole tick you jump on as a ground tick.
        _groundHold = N;
        if (Sprinting)
        {
            PushHorizontal(Vec3d.Forward(yaw) * Tuning.SprintJumpBoost);
        }
    }

    /// <summary>Minecraft's moveFlying: turns key input into an acceleration (MC units).</summary>
    public static Vec3d MoveFlying(double strafe, double forward, double acceleration, double yaw)
    {
        double f = strafe * strafe + forward * forward;
        if (f < 1.0e-4)
        {
            return Vec3d.Zero;
        }
        f = Math.Max(Math.Sqrt(f), 1.0);
        f = acceleration / f;
        return Vec3d.Forward(yaw) * (forward * f) + Vec3d.Left(yaw) * (strafe * f);
    }

    /// <summary>
    /// Minecraft's Entity.moveEntity: stops at ledges while sneaking, then slides the player's
    /// box along Y, X and Z against every solid box. Returns which axes were blocked.
    /// </summary>
    private (bool X, bool Y, bool Z) Move(double dx, double dy, double dz)
    {
        Box box = Bounds;
        if (OnGround && Sneaking)
        {
            const double step = Tuning.SneakEdgeStep;
            const double depth = -Tuning.SneakEdgeDepth;
            while (dx != 0 && IsFree(box.Offset(dx, depth, 0)))
            {
                dx = Shrink(dx, step);
            }
            while (dz != 0 && IsFree(box.Offset(0, depth, dz)))
            {
                dz = Shrink(dz, step);
            }
            while (dx != 0 && dz != 0 && IsFree(box.Offset(dx, depth, dz)))
            {
                dx = Shrink(dx, step);
                dz = Shrink(dz, step);
            }
        }
        double wantX = dx;
        double wantY = dy;
        double wantZ = dz;
        (dx, dy, dz) = Clip(box, dx, dy, dz);
        box = box.Offset(dx, dy, dz);

        Position = new Vec3d((box.MinX + box.MaxX) / 2, box.MinY, (box.MinZ + box.MaxZ) / 2);
        LastMove = new Vec3d(dx, dy, dz);
        bool blockedX = wantX != dx;
        bool blockedY = wantY != dy;
        bool blockedZ = wantZ != dz;
        CollidedHorizontally = blockedX || blockedZ;
        _touchingGround = blockedY && wantY < 0;
        if (_touchingGround)
        {
            FallDistance = 0;
        }
        else if (dy < 0)
        {
            FallDistance -= dy;
        }
        return (blockedX, blockedY, blockedZ);
    }

    /// <summary>
    /// How far a box can move: slides it along Y, then X, then Z, stopping at every solid box
    /// (Entity.moveEntity). Doesn't move anything.
    /// </summary>
    private (double X, double Y, double Z) Clip(Box box, double dx, double dy, double dz)
    {
        _boxes.Clear();
        _world.CollectBoxes(box.Stretch(dx, dy, dz), _boxes);
        foreach (Box solid in _boxes)
        {
            dy = solid.ClipY(box, dy);
        }
        box = box.Offset(0, dy, 0);
        foreach (Box solid in _boxes)
        {
            dx = solid.ClipX(box, dx);
        }
        box = box.Offset(dx, 0, 0);
        foreach (Box solid in _boxes)
        {
            dz = solid.ClipZ(box, dz);
        }
        return (dx, dy, dz);
    }

    private bool IsFree(Box region)
    {
        _boxes.Clear();
        _world.CollectBoxes(region, _boxes);
        foreach (Box solid in _boxes)
        {
            if (solid.Intersects(region))
            {
                return false;
            }
        }
        return true;
    }

    private static double Shrink(double d, double step) =>
        d < step && d >= -step ? 0 : d > 0 ? d - step : d + step;

    private static double SnapSmall(double v, double threshold) => Math.Abs(v) < threshold ? 0 : v;
}
