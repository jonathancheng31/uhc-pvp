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

    /// <summary>The attacker side of a sprint hit (EntityPlayer.attackTargetEntityWithCurrentItem).</summary>
    public void SprintHitSlowdown()
    {
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

        // EntityPlayer.onLivingUpdate
        double jumpMovementFactor = 0.02;
        if (Sprinting)
        {
            jumpMovementFactor += 0.02 * 0.3;
        }
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
        double f5 = OnGround ? aiMoveSpeed * f : jumpMovementFactor;
        Motion += PlayerMotor.MoveFlying(moveStrafe, moveForward, f5, input.Yaw);
        MoveEntity(Motion.X, Motion.Y, Motion.Z);
        double motionY = (Motion.Y - 0.08) * 0.98;
        Motion = new Vec3d(Motion.X * f4, motionY, Motion.Z * f4);
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
