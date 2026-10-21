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
