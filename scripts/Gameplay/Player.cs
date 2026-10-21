using System;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.World;

namespace UhcPvp.Gameplay;

/// <summary>
/// One player in the match.
///
/// The local player is moved by a <see cref="PlayerMotor"/> 60 times a second from the keyboard
/// (or by a simple script with --bot), has a first-person camera that is smoothed between those
/// ticks, and sends its state to the host. Other players are drawn from their recent states,
/// slightly in the past (see <see cref="SnapshotBuffer"/>).
/// </summary>
public partial class Player : Node3D
{
    private readonly double _mouseDegrees = Tuning.DegreesPerMouseCount(Tuning.DefaultMouseSensitivity);
    private Vec3d _spawn;
    private PlayerMotor? _motor;
    private Vec3d _previousPosition;
    private float _fov = Tuning.Fov;
    private Vector3 _lastRemotePosition;
    private double _botTime;

    public int PeerId { get; private set; }
    public string PlayerName { get; private set; } = "";
    public Team Team { get; private set; }
    public bool IsLocal { get; private set; }

    /// <summary>Degrees. Yaw 0 faces -Z; positive turns left. Positive pitch looks up.</summary>
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public PlayerFlags Flags { get; private set; }

    public Camera3D? Camera { get; private set; }
    public PlayerModel Model { get; private set; } = null!;
    public SnapshotBuffer Buffer { get; } = new();
    /// <summary>The local player's movement; null for other players.</summary>
    public PlayerMotor? Motor => _motor;

    public void Setup(int id, bool local, VoxelWorld world)
    {
        var session = Session.Instance;
        PeerId = id;
        IsLocal = local;
        PlayerName = session.NameOf(id);
        Team = session.TeamOf(id);
        Name = $"Player_{id}";
        _spawn = Vec3d.From(Arena.SpawnPosition(Team, session.SlotOf(id)));
        Position = _spawn.ToGodot();
        Yaw = Arena.SpawnYaw(Team);

        Model = new PlayerModel { Name = "Model" };
        Model.Setup(PlayerName, Team.Color());
        AddChild(Model);

        if (IsLocal)
        {
            _motor = new PlayerMotor(world, _spawn);
            _previousPosition = _spawn;
            Model.Visible = false; // first person: don't draw our own body
            Camera = new Camera3D
            {
                Name = "Camera",
                Fov = Tuning.Fov,
                Near = 0.05f,
                Position = new Vector3(0, (float)Tuning.EyeHeight, 0),
            };
            AddChild(Camera);
            Camera.MakeCurrent();
        }
        else
        {
            _lastRemotePosition = Position;
        }
        ApplyRotation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsLocal || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }
        if (@event is InputEventMouseMotion motion)
        {
            // Mouse look is applied every frame, never waiting for a tick.
            Yaw = Mathf.Wrap(Yaw - (float)(motion.ScreenRelative.X * _mouseDegrees), -180f, 180f);
            Pitch = Mathf.Clamp(Pitch - (float)(motion.ScreenRelative.Y * _mouseDegrees), -90f, 90f);
            ApplyRotation();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_motor is null)
        {
            return;
        }
        MovementInput input = LaunchArgs.Instance.Bot ? BotInput(delta) : KeyboardInput();
        _previousPosition = _motor.Position;
        _motor.Step(input);
        if (_motor.Position.Y < Tuning.VoidY)
        {
            _motor.Teleport(_spawn);
            _previousPosition = _spawn;
        }
        Flags = _motor.Flags;
    }

    public override void _Process(double delta)
    {
        if (_motor is null || Camera is null)
        {
            return;
        }
        // Draw between the last two ticks so motion looks smooth at any frame rate.
        float t = (float)Engine.GetPhysicsInterpolationFraction();
        Position = _previousPosition.ToGodot().Lerp(_motor.Position.ToGodot(), t);
        double eye = Tuning.EyeHeight - (_motor.Sneaking ? Tuning.SneakEyeDrop : 0);
        Camera.Position = new Vector3(0, (float)eye, 0);
        // Sprinting widens the view like Minecraft, easing in by half the gap per MC tick.
        double target = Tuning.Fov * (_motor.Sprinting ? Tuning.SprintFovMultiplier : 1.0);
        double ease = 1 - Math.Pow(1 - Tuning.FovSmoothingPerMcTick, delta * Tuning.McTickRate);
        _fov += (float)((target - _fov) * ease);
        Camera.Fov = _fov;
    }

    /// <summary>The state to send to everyone else: the latest tick, not the smoothed view.</summary>
    public PlayerState GetState() =>
        _motor is null
            ? new PlayerState(Position, Yaw, Pitch, Flags)
            : new PlayerState(_motor.Position.ToGodot(), Yaw, Pitch, _motor.Flags);

    /// <summary>A state for this (remote) player arrived at host time <paramref name="hostNow"/>.</summary>
    public void ReceiveState(double time, PlayerState state, double hostNow)
    {
        if (Buffer.Push(time, state))
        {
            Buffer.NoteArrivalAge(hostNow - time);
        }
    }

    /// <summary>Moves this (remote) player to where they were <see cref="SnapshotBuffer.RenderDelay"/> ago.</summary>
    public void UpdateRemote(double hostNow, double delta)
    {
        if (Buffer.IsEmpty)
        {
            return;
        }
        PlayerState state = Buffer.Sample(hostNow - Buffer.RenderDelay);
        Position = state.Position;
        Yaw = state.Yaw;
        Pitch = state.Pitch;
        Flags = state.Flags;
        ApplyRotation();
        Vector3 moved = Position - _lastRemotePosition;
        _lastRemotePosition = Position;
        double speed = delta > 0 ? new Vector2(moved.X, moved.Z).Length() / delta : 0;
        Model.Animate(delta, speed, state.Has(PlayerFlags.Sneaking));
    }

    private MovementInput KeyboardInput()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // Menus open: stand still, like Minecraft.
            return new MovementInput(0, 0, false, false, false, Yaw);
        }
        double forward = Input.GetActionStrength(Controls.MoveForward) - Input.GetActionStrength(Controls.MoveBack);
        double strafe = Input.GetActionStrength(Controls.MoveLeft) - Input.GetActionStrength(Controls.MoveRight);
        return new MovementInput(
            forward, strafe,
            Input.IsActionPressed(Controls.Jump),
            Input.IsActionPressed(Controls.Sprint),
            Input.IsActionPressed(Controls.Sneak),
            Yaw);
    }

    /// <summary>Test bot: sprints in wide circles, hops now and then, and sometimes strafes or sneaks.</summary>
    private MovementInput BotInput(double delta)
    {
        _botTime += delta;
        Yaw = Mathf.Wrap(Yaw + 60f * (float)delta, -180f, 180f);
        ApplyRotation();
        double cycle = _botTime % 6.0;
        bool sneak = cycle is > 4.5 and < 5.5;
        bool jump = !sneak && _botTime % 1.5 < 0.1;
        double strafe = cycle is > 2 and < 3 ? 1 : 0;
        return new MovementInput(1, strafe, jump, !sneak, sneak, Yaw);
    }

    private void ApplyRotation()
    {
        if (IsLocal)
        {
            RotationDegrees = new Vector3(0, Yaw, 0);
            if (Camera is not null)
            {
                Camera.RotationDegrees = new Vector3(Pitch, 0, 0);
            }
        }
        else
        {
            Model.RotationDegrees = new Vector3(0, Yaw, 0);
            Model.SetHeadPitch(Pitch);
        }
    }
}
