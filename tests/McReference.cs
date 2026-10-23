using System;
using System.Collections.Generic;
using UhcPvp.Core;
using UhcPvp.Movement;

namespace UhcPvp.Tests;

/// <summary>
/// A direct copy of Minecraft 1.8.9's player movement at 20 ticks per second: the parts of
/// EntityPlayerSP.onLivingUpdate, EntityPlayer.onLivingUpdate, EntityLivingBase.onLivingUpdate,
/// moveEntityWithHeading, moveFlying and Entity.moveEntity that apply to a survival player on
/// normal blocks. It's the yardstick for <see cref="PlayerMotor"/>; it never runs in the game.
/// </summary>
public sealed class McReference
{
    private readonly ICollisionWorld _world;
    private readonly List<Box> _boxes = new();
    private int _jumpTicks;
    private int _sprintToggleTimer;
    private double _previousForward;
    private bool _previousSneak;
    /// <summary>
    /// Air acceleration. EntityPlayer.onLivingUpdate only sets it after super.onLivingUpdate()
    /// has moved the player, so a tick's air movement uses the sprint state from the end of the
    /// tick before. A new player starts at 0.02.
    /// </summary>
    private double _jumpMovementFactor = 0.02;

    public McReference(ICollisionWorld world, Vec3d position)
    {
        _world = world;
        Position = position;
    }

    public Vec3d Position { get; private set; }
    /// <summary>motionX/Y/Z, in blocks per MC tick.</summary>
    public Vec3d Motion { get; set; }
    public bool OnGround { get; private set; }
    public bool Sprinting { get; private set; }
    public bool Sneaking { get; private set; }
    public bool CollidedHorizontally { get; private set; }
    /// <summary>
    /// Our faster strafing (not 1.8.9; off at 1.0), applied to the ground acceleration exactly
    /// as <see cref="PlayerMotor.BoostStrafe"/> describes, so the motor can be checked against it.
    /// </summary>
    public double StrafeSpeed { get; set; } = 1.0;
    /// <summary>Our snappier strafing (not 1.8.9; off at 0), as <see cref="PlayerMotor.GripStrafe"/> describes.</summary>
    public double StrafeGrip { get; set; }
    private bool _skipGrip;
    /// <summary>Our player pushing (not 1.8.9; off at 0), with <see cref="Contacts"/>, as <see cref="Pushing"/> describes.</summary>
    public double PushStrength { get; set; }
    public List<PushContact> Contacts { get; } = new();
    /// <summary>Flying from a knockback until a tick starts on the ground: pushing leaves it alone.</summary>
    public bool KnockbackFlight { get; private set; }

    /// <summary>A velocity packet (knockback) between ticks: replaces the motion.</summary>
    public void ReceiveVelocity(Vec3d motion)
    {
        Motion = motion;
        _skipGrip = true;
        KnockbackFlight = true;
    }

    /// <summary>
    /// The attacker side of a click on a player (EntityPlayer.attackTargetEntityWithCurrentItem,
    /// which a client runs for every click on a player): only while sprinting, it slows to 60%
    /// and stops sprint, until the next tick restarts it (with the sprint key held).
    /// </summary>
    public void SprintHitSlowdown()
    {
        if (!Sprinting)
        {
            return;
        }
        Motion = new Vec3d(Motion.X * 0.6, Motion.Y, Motion.Z * 0.6);
        Sprinting = false;
    }

    public void Tick(MovementInput input)
    {
        // EntityPlayerSP.onLivingUpdate
        if (_sprintToggleTimer > 0)
        {
            --_sprintToggleTimer;
        }
        bool flag1 = _previousSneak;
        bool flag2 = _previousForward >= 0.8;
        double moveForward = input.Forward;
        double moveStrafe = input.Strafe;
        if (input.Sneak)
        {
            moveStrafe *= 0.3;
            moveForward *= 0.3;
        }
        _previousForward = moveForward;
        _previousSneak = input.Sneak;
        Sneaking = input.Sneak;
        if (OnGround && !flag1 && !flag2 && moveForward >= 0.8 && !Sprinting)
        {
            if (_sprintToggleTimer <= 0 && !input.Sprint)
            {
                _sprintToggleTimer = 7;
            }
            else
            {
                Sprinting = true;
            }
        }
        if (!Sprinting && moveForward >= 0.8 && input.Sprint)
        {
            Sprinting = true;
        }
        if (Sprinting && (moveForward < 0.8 || CollidedHorizontally))
        {
            Sprinting = false;
        }

        // EntityPlayer.getAIMoveSpeed reads the movement speed attribute, which setSprinting
        // changes right away, so ground speed follows this tick's sprint state.
        double aiMoveSpeed = 0.1 * (Sprinting ? 1.3 : 1.0);

        // EntityLivingBase.onLivingUpdate
        if (_jumpTicks > 0)
        {
            --_jumpTicks;
        }
        double mx = Math.Abs(Motion.X) < 0.005 ? 0 : Motion.X;
        double my = Math.Abs(Motion.Y) < 0.005 ? 0 : Motion.Y;
        double mz = Math.Abs(Motion.Z) < 0.005 ? 0 : Motion.Z;
        Motion = new Vec3d(mx, my, mz);
        bool skipGrip = _skipGrip;
        _skipGrip = false;
        if (KnockbackFlight && !skipGrip && OnGround)
        {
            KnockbackFlight = false;
        }
        if (OnGround && StrafeGrip > 0 && !skipGrip)
        {
            // Our snappier strafing (not 1.8.9), with this tick's ground acceleration.
            const double groundFactor = 0.16277136 / (0.6 * 0.91 * 0.6 * 0.91 * 0.6 * 0.91);
            Vec3d groundAccel = PlayerMotor.BoostStrafe(
                PlayerMotor.MoveFlying(moveStrafe * 0.98, moveForward * 0.98, aiMoveSpeed * groundFactor, input.Yaw),
                input.Yaw, StrafeSpeed, 0.1 * 1.3 * groundFactor * 0.98);
            Motion = PlayerMotor.GripStrafe(Motion, groundAccel, input.Yaw, StrafeGrip);
        }
        if (input.Jump)
        {
            if (OnGround && _jumpTicks == 0)
            {
                Motion = Motion.WithY(0.42);
                if (Sprinting)
                {
                    Motion += Vec3d.Forward(input.Yaw) * 0.2;
                }
                _jumpTicks = 10;
            }
        }
        else
        {
            _jumpTicks = 0;
        }
        moveStrafe *= 0.98;
        moveForward *= 0.98;

        // EntityLivingBase.moveEntityWithHeading
        double f4 = 0.91;
        if (OnGround)
        {
            f4 = 0.6 * 0.91;
        }
        double f = 0.16277136 / (f4 * f4 * f4);
        double f5 = OnGround ? aiMoveSpeed * f : _jumpMovementFactor;
        Vec3d acceleration = PlayerMotor.MoveFlying(moveStrafe, moveForward, f5, input.Yaw);
        if (OnGround)
        {
            acceleration = PlayerMotor.BoostStrafe(acceleration, input.Yaw, StrafeSpeed, 0.1 * 1.3 * f * 0.98);
        }
        if (PushStrength > 0 && !KnockbackFlight)
        {
            // Our player pushing (not 1.8.9), from this tick's whole move.
            Motion += Pushing.Resolve(Position, Motion.WithY(0), acceleration.WithY(0), Contacts, PushStrength);
        }
        Motion += acceleration;
        MoveEntity(Motion.X, Motion.Y, Motion.Z);
        double motionY = (Motion.Y - 0.08) * 0.98;
        Motion = new Vec3d(Motion.X * f4, motionY, Motion.Z * f4);

        // EntityPlayer.onLivingUpdate, after super.onLivingUpdate(): sets the air acceleration
        // for the next tick.
        _jumpMovementFactor = 0.02;
        if (Sprinting)
        {
            _jumpMovementFactor += 0.02 * 0.3;
        }
    }

    private void MoveEntity(double x, double y, double z)
    {
        Box box = Box.ForPlayer(Position, Tuning.PlayerWidth, Tuning.PlayerHeight);
        double d3 = x;
        double d4 = y;
        double d5 = z;
        if (OnGround && Sneaking)
        {
            const double d6 = 0.05;
            for (; x != 0 && Free(box.Offset(x, -1, 0)); d3 = x)
            {
                x = x < d6 && x >= -d6 ? 0 : x > 0 ? x - d6 : x + d6;
            }
            for (; z != 0 && Free(box.Offset(0, -1, z)); d5 = z)
            {
                z = z < d6 && z >= -d6 ? 0 : z > 0 ? z - d6 : z + d6;
            }
            for (; x != 0 && z != 0 && Free(box.Offset(x, -1, z)); d5 = z)
            {
                x = x < d6 && x >= -d6 ? 0 : x > 0 ? x - d6 : x + d6;
                d3 = x;
                z = z < d6 && z >= -d6 ? 0 : z > 0 ? z - d6 : z + d6;
            }
        }
        _boxes.Clear();
        _world.CollectBoxes(box.Stretch(x, y, z), _boxes);
        foreach (Box b in _boxes)
        {
            y = b.ClipY(box, y);
        }
        box = box.Offset(0, y, 0);
        foreach (Box b in _boxes)
        {
            x = b.ClipX(box, x);
        }
        box = box.Offset(x, 0, 0);
        foreach (Box b in _boxes)
        {
            z = b.ClipZ(box, z);
        }
        box = box.Offset(0, 0, z);
        Position = new Vec3d((box.MinX + box.MaxX) / 2, box.MinY, (box.MinZ + box.MaxZ) / 2);
        CollidedHorizontally = d3 != x || d5 != z;
        OnGround = d4 != y && d4 < 0;
        Motion = new Vec3d(d3 != x ? 0 : Motion.X, d4 != y ? 0 : Motion.Y, d5 != z ? 0 : Motion.Z);
    }

    private bool Free(Box region)
    {
        _boxes.Clear();
        _world.CollectBoxes(region, _boxes);
        return _boxes.TrueForAll(b => !b.Intersects(region));
    }
}

/// <summary>
/// The server's copy of a player (EntityPlayerMP) in Minecraft 1.8.9, as the client's report each
/// tick moves it: EntityPlayerSP.onUpdateWalkingPlayer (when the position is sent), then
/// NetHandlerPlayServer.processPlayer (onUpdateEntity with no input, the server-side jump, the
/// move to the reported position). Knockback is built from its <see cref="Motion"/>. The
/// yardstick for <see cref="PlayerMotor.ServerMotion"/>; it never runs in the game.
/// </summary>
public sealed class McServerCopy
{
    private readonly ICollisionWorld _world;
    private readonly List<Box> _boxes = new();
    private Vec3d _lastReported;
    private int _positionUpdateTicks;

    public McServerCopy(ICollisionWorld world, Vec3d position)
    {
        _world = world;
        Position = position;
        _lastReported = position;
    }

    public Vec3d Position { get; private set; }
    public Vec3d Motion { get; set; }
    public bool OnGround { get; private set; }

    /// <summary>One client tick's report: where the client is, whether it's on the ground, and its yaw.</summary>
    public void Report(Vec3d clientPosition, bool clientOnGround, bool serverSprinting, double yaw)
    {
        // EntityPlayerSP.onUpdateWalkingPlayer
        Vec3d d = clientPosition - _lastReported;
        bool moving = d.X * d.X + d.Y * d.Y + d.Z * d.Z > 9.0E-4 || _positionUpdateTicks >= 20;
        ++_positionUpdateTicks;
        if (moving)
        {
            _lastReported = clientPosition;
            _positionUpdateTicks = 0;
        }

        // NetHandlerPlayServer.processPlayer
        Vec3d last = Position;
        Vec3d packet = moving ? clientPosition : last;
        // onUpdateEntity -> EntityLivingBase.onLivingUpdate / moveEntityWithHeading, no input
        double mx = Math.Abs(Motion.X) < 0.005 ? 0 : Motion.X;
        double my = Math.Abs(Motion.Y) < 0.005 ? 0 : Motion.Y;
        double mz = Math.Abs(Motion.Z) < 0.005 ? 0 : Motion.Z;
        double f4 = OnGround ? 0.6 * 0.91 : 0.91;
        var (x, y, z) = MoveEntity(last, mx, my, mz);
        OnGround = y != my && my < 0;
        if (x != mx)
        {
            mx = 0;
        }
        if (z != mz)
        {
            mz = 0;
        }
        if (y != my)
        {
            my = 0; // Block.onLanded
        }
        my = (my - 0.08) * 0.98;
        mx *= f4;
        mz *= f4;
        // setPositionAndRotation(lastPos): the simulated move is thrown away.
        if (OnGround && !clientOnGround && packet.Y - last.Y > 0)
        {
            // EntityLivingBase.jump
            my = 0.42;
            if (serverSprinting)
            {
                mx -= Math.Sin(yaw * Math.PI / 180) * 0.2;
                mz -= Math.Cos(yaw * Math.PI / 180) * 0.2;
            }
        }
        Vec3d delta = packet - last;
        var (dx, dy, dz) = MoveEntity(last, delta.X, delta.Y, delta.Z);
        Motion = new Vec3d(dx != delta.X ? 0 : mx, dy != delta.Y ? 0 : my, dz != delta.Z ? 0 : mz);
        OnGround = clientOnGround;
        Position = packet;
    }

    /// <summary>Entity.moveEntity's collision for a box at <paramref name="from"/>; returns the allowed move.</summary>
    private (double X, double Y, double Z) MoveEntity(Vec3d from, double x, double y, double z)
    {
        Box box = Box.ForPlayer(from, Tuning.PlayerWidth, Tuning.PlayerHeight);
        _boxes.Clear();
        _world.CollectBoxes(box.Stretch(x, y, z), _boxes);
        foreach (Box b in _boxes)
        {
            y = b.ClipY(box, y);
        }
        box = box.Offset(0, y, 0);
        foreach (Box b in _boxes)
        {
            x = b.ClipX(box, x);
        }
        box = box.Offset(x, 0, 0);
        foreach (Box b in _boxes)
        {
            z = b.ClipZ(box, z);
        }
        return (x, y, z);
    }
}

/// <summary>A position update from 1.8.9's entity tracker, in 1/32 block: relative (S15/S16/S17) or absolute (S18).</summary>
public readonly record struct McMove(bool Absolute, long X, long Y, long Z);

/// <summary>
/// The server side of how 1.8.9 shows a player to everyone else (EntityTrackerEntry.updatePlayerList;
/// players are tracked with an update frequency of 2). Once per server tick, after that tick's
/// position reports were applied: every 2nd tick, or on any tick where the player jumped, was
/// knocked back, or had their health or flags change (isAirBorne, DataWatcher.hasObjectChanged),
/// the position is rounded down to 1/32 block and sent if it moved at least 4/32 on some axis
/// since the last one sent (or every 60 ticks), as a relative move, or as an absolute teleport
/// when the on-ground flag differs from the last teleport's (or after 400 updates). A view that
/// turned 4/256 of a turn sends a look packet, which also restarts the client's glide.
/// The yardstick for the head start; it never runs in the game.
/// </summary>
public sealed class McTrackerEntry
{
    private int _updateCounter;
    private int _ticksSinceLastForcedTeleport;
    private long _encodedX;
    private long _encodedY;
    private long _encodedZ;
    private long _encodedYaw;
    private long _encodedPitch;
    private bool _onGround;

    /// <summary>The player spawns on the other client exactly here (S0CPacketSpawnPlayer).</summary>
    public McTrackerEntry(Vec3d position, bool onGround, double yaw, double pitch)
    {
        (_encodedX, _encodedY, _encodedZ) = Encode(position);
        _encodedYaw = Angle(yaw);
        _encodedPitch = Angle(pitch);
        _onGround = onGround;
    }

    public static (long X, long Y, long Z) Encode(Vec3d p) =>
        ((long)Math.Floor(p.X * 32.0), (long)Math.Floor(p.Y * 32.0), (long)Math.Floor(p.Z * 32.0));

    private static long Angle(double degrees) => (long)Math.Floor(degrees * 256.0 / 360.0);

    /// <summary>One server tick. Returns the packet sent, if any.</summary>
    public McMove? Update(Vec3d position, bool onGround, double yaw, double pitch, bool forced)
    {
        McMove? packet = null;
        if (_updateCounter % 2 == 0 || forced)
        {
            ++_ticksSinceLastForcedTeleport;
            (long k, long j1, long k1) = Encode(position);
            long l1 = Angle(yaw);
            long i2 = Angle(pitch);
            long dx = k - _encodedX;
            long dy = j1 - _encodedY;
            long dz = k1 - _encodedZ;
            bool moved = Math.Abs(dx) >= 4 || Math.Abs(dy) >= 4 || Math.Abs(dz) >= 4 || _updateCounter % 60 == 0;
            bool looked = Math.Abs(l1 - _encodedYaw) >= 4 || Math.Abs(i2 - _encodedPitch) >= 4;
            if (_updateCounter > 0)
            {
                if (dx >= -128 && dx < 128 && dy >= -128 && dy < 128 && dz >= -128 && dz < 128
                    && _ticksSinceLastForcedTeleport <= 400 && _onGround == onGround)
                {
                    if (moved)
                    {
                        packet = new McMove(false, dx, dy, dz);
                    }
                    else if (looked)
                    {
                        packet = new McMove(false, 0, 0, 0);
                    }
                }
                else
                {
                    _onGround = onGround;
                    _ticksSinceLastForcedTeleport = 0;
                    packet = new McMove(true, k, j1, k1);
                }
            }
            if (moved)
            {
                (_encodedX, _encodedY, _encodedZ) = (k, j1, k1);
            }
            if (looked)
            {
                (_encodedYaw, _encodedPitch) = (l1, i2);
            }
        }
        ++_updateCounter;
        return packet;
    }
}

/// <summary>
/// The client side (NetHandlerPlayClient.handleEntityMovement and handleEntityTeleport, then
/// EntityOtherPlayerMP): each update sets a target and 3 steps; each client tick moves the drawn
/// position 1/steps of the way there, so with no newer update it gets there in a straight line
/// over 3 ticks. The yardstick for the head start; it never runs in the game.
/// </summary>
public sealed class McOtherPlayer
{
    private long _serverX;
    private long _serverY;
    private long _serverZ;
    private Vec3d _target;
    private int _increments;

    public McOtherPlayer(Vec3d spawn)
    {
        (_serverX, _serverY, _serverZ) = McTrackerEntry.Encode(spawn);
        Position = new Vec3d(_serverX / 32.0, _serverY / 32.0, _serverZ / 32.0);
        PrevPosition = Position;
        _target = Position;
    }

    /// <summary>Where it's drawn as of the last tick: what a click is tested against (getMouseOver(1.0)).</summary>
    public Vec3d Position { get; private set; }
    /// <summary>As of the tick before; the screen blends from here to <see cref="Position"/>.</summary>
    public Vec3d PrevPosition { get; private set; }

    public void Receive(McMove move)
    {
        if (move.Absolute)
        {
            (_serverX, _serverY, _serverZ) = (move.X, move.Y, move.Z);
            var to = new Vec3d(_serverX / 32.0, _serverY / 32.0, _serverZ / 32.0);
            bool close = Math.Abs(Position.X - to.X) < 0.03125 && Math.Abs(Position.Y - to.Y) < 0.015625
                && Math.Abs(Position.Z - to.Z) < 0.03125;
            _target = close ? Position : to;
        }
        else
        {
            _serverX += move.X;
            _serverY += move.Y;
            _serverZ += move.Z;
            _target = new Vec3d(_serverX / 32.0, _serverY / 32.0, _serverZ / 32.0);
        }
        _increments = 3;
    }

    /// <summary>One client tick (onLivingUpdate).</summary>
    public void Tick()
    {
        PrevPosition = Position;
        if (_increments > 0)
        {
            Position += (_target - Position) * (1.0 / _increments);
            --_increments;
        }
    }
}

/// <summary>
/// One player as another 1.8.9 client shows them, at 0 ping, from that player's path. Their client
/// reports its position at the end of every tick; the server applies the reports at the start of
/// its next tick and runs the tracker at the end of it (<see cref="McTrackerEntry"/>); the watching
/// client applies what arrived at the start of its next tick, then glides (<see cref="McOtherPlayer"/>).
/// The three tick grids are 20 a second, with phases given in seconds. Feed it the player's state
/// every step, in time order. The yardstick for the head start; it never runs in the game.
/// </summary>
public sealed class McView
{
    private const double Tick = 0.05;
    private readonly double _serverPhase;
    private readonly double _viewerPhase;
    /// <summary>Starts tracking at the first server tick, when the spawn packet carries the position then.</summary>
    private McTrackerEntry? _tracker;
    private McOtherPlayer _drawn;
    private readonly Queue<(double Time, Vec3d Position, bool OnGround, double Yaw, double Pitch, int Flags)> _reports = new();
    private readonly Queue<(double Time, McMove Move)> _packets = new();
    private readonly Queue<double> _hits = new();
    private long _serverTick;
    private long _viewerTick;
    private Vec3d _copy;
    private bool _copyOnGround = true;
    private double _copyYaw;
    private double _copyPitch;
    private int _copyFlags;

    public McView(Vec3d spawn, double yaw, double serverPhase, double viewerPhase)
    {
        _serverPhase = serverPhase;
        _viewerPhase = viewerPhase;
        _drawn = new McOtherPlayer(spawn);
        _copy = spawn;
        _copyYaw = yaw;
    }

    /// <summary>Host time of the watching client's last tick.</summary>
    public double LastViewerTick => _viewerPhase + (_viewerTick - 1) * Tick;

    /// <summary>
    /// The player's client finished a tick at <paramref name="time"/> and reports this. Yaw is
    /// unwrapped (no jump at +-180), flags are sprinting and sneaking.
    /// </summary>
    public void Report(double time, Vec3d position, bool onGround, double yaw, double pitch, int flags) =>
        _reports.Enqueue((time, position, onGround, yaw, pitch, flags));

    /// <summary>The player was hit by a click at <paramref name="time"/> (knockback and health change).</summary>
    public void Hit(double time) => _hits.Enqueue(time);

    /// <summary>Runs every server and watching-client tick up to <paramref name="time"/>.</summary>
    public void Advance(double time)
    {
        while (true)
        {
            double server = _serverPhase + _serverTick * Tick;
            double viewer = _viewerPhase + _viewerTick * Tick;
            if (Math.Min(server, viewer) > time)
            {
                return;
            }
            if (server <= viewer)
            {
                ServerTick(server);
            }
            else
            {
                ViewerTick(viewer);
            }
        }
    }

    private void ServerTick(double now)
    {
        bool forced = false;
        while (_reports.Count > 0 && _reports.Peek().Time <= now)
        {
            var report = _reports.Dequeue();
            // NetHandlerPlayServer.processPlayer: seeing the client leave the ground going up, the
            // server's copy jumps, which forces a tracker update (isAirBorne).
            if (_copyOnGround && !report.OnGround && report.Position.Y > _copy.Y)
            {
                forced = true;
            }
            // A change of sprinting or sneaking changes the copy's flags (DataWatcher).
            forced |= report.Flags != _copyFlags;
            (_copy, _copyOnGround, _copyYaw, _copyPitch, _copyFlags) =
                (report.Position, report.OnGround, report.Yaw, report.Pitch, report.Flags);
        }
        while (_hits.Count > 0 && _hits.Peek() <= now)
        {
            // knockBack and the sprint push set isAirBorne; the health change is metadata.
            _hits.Dequeue();
            forced = true;
        }
        if (_tracker is null)
        {
            _tracker = new McTrackerEntry(_copy, _copyOnGround, _copyYaw, _copyPitch);
            _drawn = new McOtherPlayer(_copy);
        }
        if (_tracker.Update(_copy, _copyOnGround, _copyYaw, _copyPitch, forced) is McMove move)
        {
            _packets.Enqueue((now, move));
        }
        _serverTick++;
    }

    private void ViewerTick(double now)
    {
        while (_packets.Count > 0 && _packets.Peek().Time <= now)
        {
            _drawn.Receive(_packets.Dequeue().Move);
        }
        _drawn.Tick();
        _viewerTick++;
    }

    /// <summary>Where a click now finds the player: the glide as of the watching client's last tick.</summary>
    public Vec3d Hitbox => _drawn.Position;

    /// <summary>Where the screen shows the player at <paramref name="time"/>: between the last two ticks.</summary>
    public Vec3d Rendered(double time)
    {
        double f = Math.Clamp((time - LastViewerTick) / Tick, 0, 1);
        return _drawn.PrevPosition + (_drawn.Position - _drawn.PrevPosition) * f;
    }
}

/// <summary>
/// Minecraft 1.8.9's bobber (EntityFishHook) in open air: the cast from a player's eyes
/// (without the random spread) and the flight, in Minecraft's own angles (yaw 0 faces +Z and
/// turns right, positive pitch looks down). The yardstick for <see cref="Combat.Bobber"/>.
/// </summary>
public sealed class McBobber
{
    public McBobber(Vec3d feet, double eyeHeight, double mcYaw, double mcPitch)
    {
        double yaw = mcYaw * Math.PI / 180.0;
        double pitch = mcPitch * Math.PI / 180.0;
        // Starts at the eyes, 0.16 toward the right hand and 0.1 down.
        Position = new Vec3d(feet.X - Math.Cos(yaw) * 0.16, feet.Y + eyeHeight - 0.1, feet.Z - Math.Sin(yaw) * 0.16);
        double mx = -Math.Sin(yaw) * Math.Cos(pitch) * 0.4;
        double mz = Math.Cos(yaw) * Math.Cos(pitch) * 0.4;
        double my = -Math.Sin(pitch) * 0.4;
        // handleHookCasting: normalize, (spread), times 1.5.
        double f = Math.Sqrt(mx * mx + my * my + mz * mz);
        Motion = new Vec3d(mx / f * 1.5, my / f * 1.5, mz / f * 1.5);
    }

    public Vec3d Position { get; private set; }
    public Vec3d Motion { get; private set; }

    /// <summary>One tick with nothing in the way: move, gravity, air drag.</summary>
    public void Tick()
    {
        Position += Motion;
        Motion = new Vec3d(Motion.X * 0.92, (Motion.Y - 0.04) * 0.92, Motion.Z * 0.92);
    }
}
