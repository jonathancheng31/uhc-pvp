using System;
using System.Collections.Generic;
using Godot;
using UhcPvp.Autoload;
using UhcPvp.Combat;
using UhcPvp.Core;
using UhcPvp.Movement;
using UhcPvp.Net;
using UhcPvp.World;

namespace UhcPvp.Gameplay;

public enum PlayerKind
{
    /// <summary>The person playing on this computer.</summary>
    Local,
    /// <summary>A practice bot, run by the host's game.</summary>
    Bot,
    /// <summary>Someone else's player (or bot), shown from network updates.</summary>
    Remote,
}

/// <summary>
/// One player in the match.
///
/// Local players and bots are "owned" here: a <see cref="PlayerMotor"/> moves them 60 times a
/// second, <see cref="Combat.Vitality"/> holds their real health, and this game decides what
/// hits on them do. Remote players are drawn from their recent states, slightly in the past
/// (see <see cref="SnapshotBuffer"/>).
/// </summary>
public partial class Player : Node3D
{
    private static readonly int N = Tuning.SubticksPerMcTick;
    private static readonly double SwingSeconds = (double)Tuning.SwingMcTicks / Tuning.McTickRate;
    private static readonly double FlashSeconds = (double)Tuning.HurtFlashMcTicks / Tuning.McTickRate;

    private readonly double _mouseDegrees = Tuning.DegreesPerMouseCount(Tuning.DefaultMouseSensitivity);
    private Game _game = null!;
    private Vec3d _previousPosition;
    private float _fov = Tuning.Fov;
    private Vector3 _lastShownPosition;
    private int _missCooldown;
    private double _sinceHurt;
    private double _swingTime = -1;
    private double _flashTime;
    private double _deadTime;

    public int PeerId { get; private set; }
    public string PlayerName { get; private set; } = "";
    public Team Team { get; private set; }
    public PlayerKind Kind { get; private set; }
    public bool IsOwned => Kind != PlayerKind.Remote;
    public bool IsLocal => Kind == PlayerKind.Local;

    /// <summary>Degrees. Yaw 0 faces -Z; positive turns left. Positive pitch looks up.</summary>
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public PlayerFlags Flags { get; private set; }

    public Camera3D? Camera { get; private set; }
    public ViewModel? ViewModel { get; private set; }
    public PlayerModel Model { get; private set; } = null!;
    public SnapshotBuffer Buffer { get; } = new();
    /// <summary>Movement, for players this game runs; null for remote players.</summary>
    public PlayerMotor? Motor { get; private set; }
    /// <summary>Real health for owned players; for remote players, the last health we heard.</summary>
    public Vitality Vitality { get; } = new();
    /// <summary>The AI for bots (and for --bot test players); null for people.</summary>
    public BotBrain? Brain { get; set; }

    /// <summary>Where this player (re)spawns, and which way they face.</summary>
    public Vec3d Home { get; set; }
    public float HomeYaw { get; set; }

    public bool IsDead => IsOwned ? Vitality.IsDead : (Flags & PlayerFlags.Dead) != 0;
    public double Health => Vitality.Health;
    /// <summary>Seconds until a dead owned player respawns.</summary>
    public double RespawnIn { get; private set; }

    /// <summary>Where this player really is (owned players: latest tick; remote: as shown).</summary>
    public Vec3d Feet => Motor?.Position ?? Vec3d.From(Position);
    /// <summary>Where this player is drawn on this screen right now.</summary>
    public Vec3d ShownFeet => Vec3d.From(Position);
    public bool Sneaking => (Flags & PlayerFlags.Sneaking) != 0;
    /// <summary>Eye position as drawn right now, for aiming (Minecraft lowers it 0.08 when sneaking).</summary>
    public Vec3d Eye => ShownFeet + new Vec3d(0, Tuning.EyeHeight - (Sneaking ? Tuning.SneakEyeDrop : 0), 0);
    public bool CanAttack => IsOwned && !IsDead && _missCooldown == 0;

    public void Setup(Game game, int id, PlayerKind kind, VoxelWorld world, Vec3d home, float homeYaw)
    {
        var session = Session.Instance;
        _game = game;
        PeerId = id;
        Kind = kind;
        PlayerName = session.NameOf(id);
        Team = session.TeamOf(id);
        Name = $"Player_{id}";
        Home = home;
        HomeYaw = homeYaw;
        Position = home.ToGodot();
        Yaw = homeYaw;

        Model = new PlayerModel { Name = "Model" };
        Model.Setup(PlayerName, Team.Color());
        AddChild(Model);

        if (IsOwned)
        {
            Motor = new PlayerMotor(world, home);
            _previousPosition = home;
        }
        if (IsLocal)
        {
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
            ViewModel = new ViewModel { Name = "ViewModel" };
            Camera.AddChild(ViewModel);
        }
        _lastShownPosition = Position;
        ApplyRotation();
    }

    /// <summary>Points the view (used by bots).</summary>
    public void Look(float yaw, float pitch)
    {
        Yaw = Mathf.Wrap(yaw, -180f, 180f);
        Pitch = Mathf.Clamp(pitch, -90f, 90f);
        ApplyRotation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsLocal || Brain is not null || Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            return;
        }
        if (@event is InputEventMouseMotion motion)
        {
            // Mouse look is applied every frame, never waiting for a tick.
            Look(Yaw - (float)(motion.ScreenRelative.X * _mouseDegrees), Pitch - (float)(motion.ScreenRelative.Y * _mouseDegrees));
        }
        else if (@event.IsActionPressed(Controls.Attack))
        {
            // Every click is one attack, like 1.8: no cooldown, so clicking speed matters.
            _game.TryAttack(this);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Motor is null)
        {
            return;
        }
        Vitality.Tick();
        if (_missCooldown > 0)
        {
            _missCooldown--;
        }
        if (IsDead)
        {
            RespawnIn -= delta;
            if (RespawnIn <= 0)
            {
                Respawn();
            }
            Flags = PlayerFlags.Dead;
            return;
        }

        BotIntent? intent = Brain?.Think(this, _game.EnemiesOf(this), delta);
        MovementInput input = intent?.Input ?? KeyboardInput();
        _previousPosition = Motor.Position;
        Motor.Step(input);
        if (Motor.Position.Y < Tuning.VoidY)
        {
            Respawn();
        }
        if (intent?.Attack == true)
        {
            _game.TryAttack(this);
        }
        if (Kind == PlayerKind.Bot)
        {
            // Practice bots heal to full after a few seconds of peace.
            _sinceHurt += delta;
            if (_sinceHurt > Tuning.BotHealDelay && Vitality.Health < Tuning.MaxHealth)
            {
                Vitality.Reset();
            }
        }
        Flags = Motor.Flags;
    }

    public override void _Process(double delta)
    {
        _flashTime = Math.Max(_flashTime - delta, 0);
        if (_swingTime >= 0)
        {
            _swingTime += delta;
            if (_swingTime >= SwingSeconds)
            {
                _swingTime = -1;
            }
        }
        _deadTime = IsDead ? _deadTime + delta : 0;

        if (Motor is not null)
        {
            // Draw between the last two ticks so motion looks smooth at any frame rate.
            float t = (float)Engine.GetPhysicsInterpolationFraction();
            Position = _previousPosition.ToGodot().Lerp(Motor.Position.ToGodot(), t);
        }
        if (Kind == PlayerKind.Bot && Motor is not null)
        {
            Model.Animate(delta, Motor.HorizontalSpeed, Motor.Sneaking);
        }
        if (Camera is not null && Motor is not null)
        {
            UpdateCamera(delta);
        }
        Model.SetHealth(Vitality.Health);
        Model.SetSwing(SwingProgress, Pitch);
        Model.SetHurtTint((float)(_flashTime / FlashSeconds));
        Model.SetDeath(_deadTime);
    }

    /// <summary>0 to 1 through an arm swing, or -1 when not swinging.</summary>
    public float SwingProgress => _swingTime < 0 ? -1 : (float)(_swingTime / SwingSeconds);

    /// <summary>Starts an arm swing (Minecraft restarts one only once it's half done).</summary>
    public void PlaySwing()
    {
        if (_swingTime < 0 || _swingTime >= SwingSeconds / 2)
        {
            _swingTime = 0;
            ViewModel?.Swing();
        }
    }

    /// <summary>Clicking at nothing: Minecraft 1.8's half-second hit delay.</summary>
    public void StartMissCooldown() => _missCooldown = Tuning.MissClickCooldownMcTicks * N;

    /// <summary>Shows a hit on this player (flash, health). Sound and particles are played by the game.</summary>
    public void ShowHurt(Hurt hurt)
    {
        if ((hurt.Flags & HurtFlags.Fresh) != 0)
        {
            _flashTime = FlashSeconds;
        }
        if (!IsOwned)
        {
            Vitality.Show(hurt.Health);
            if ((hurt.Flags & HurtFlags.Killed) != 0)
            {
                Flags |= PlayerFlags.Dead;
            }
        }
    }

    /// <summary>The owner's side of taking a hit: start the respawn timer if it killed, reset bot healing.</summary>
    public void OnDamaged()
    {
        _sinceHurt = 0;
        if (IsDead)
        {
            RespawnIn = Kind == PlayerKind.Bot ? Tuning.BotRespawnDelay : Tuning.RespawnDelay;
            Flags = PlayerFlags.Dead;
        }
    }

    private void Respawn()
    {
        if (Motor is null)
        {
            return;
        }
        Vitality.Reset();
        Motor.Teleport(Home);
        _previousPosition = Home;
        Look(HomeYaw, 0);
        RespawnIn = 0;
        Flags = Motor.Flags;
    }

    /// <summary>The state to send to everyone else: the latest tick, not the smoothed view.</summary>
    public PlayerState GetState() => new(
        Feet.ToGodot(), Yaw, Pitch, Flags | (IsDead ? PlayerFlags.Dead : 0), (float)Vitality.Health);

    /// <summary>A state for this (remote) player arrived at host time <paramref name="hostNow"/>.</summary>
    public void ReceiveState(double time, PlayerState state, double hostNow)
    {
        if (!Buffer.IsEmpty && Buffer.LatestState.Has(PlayerFlags.Dead) && !state.Has(PlayerFlags.Dead))
        {
            // Respawned: jump straight to the new spot instead of sliding there.
            Buffer.Clear();
        }
        if (Buffer.Push(time, state))
        {
            Buffer.NoteArrivalAge(hostNow - time);
        }
    }

    /// <summary>Host time of the state this (remote) player is drawn at right now.</summary>
    public double ShownTime(double hostNow) => IsOwned ? hostNow : hostNow - Buffer.RenderDelay;

    /// <summary>Moves this (remote) player to where they were <see cref="SnapshotBuffer.RenderDelay"/> ago.</summary>
    public void UpdateRemote(double hostNow, double delta)
    {
        if (Buffer.IsEmpty)
        {
            return;
        }
        PlayerState state = Buffer.Sample(ShownTime(hostNow));
        Position = state.Position;
        Yaw = state.Yaw;
        Pitch = state.Pitch;
        Flags = state.Flags;
        Vitality.Show(state.Health);
        ApplyRotation();
        Vector3 moved = Position - _lastShownPosition;
        _lastShownPosition = Position;
        double speed = delta > 0 ? new Vector2(moved.X, moved.Z).Length() / delta : 0;
        Model.Animate(delta, speed, state.Has(PlayerFlags.Sneaking));
    }

    private void UpdateCamera(double delta)
    {
        if (Camera is null || Motor is null)
        {
            return;
        }
        double eye = IsDead ? 0.3 : Tuning.EyeHeight - (Motor.Sneaking ? Tuning.SneakEyeDrop : 0);
        Camera.Position = new Vector3(0, (float)eye, 0);
        // Sprinting widens the view like Minecraft, easing in by half the gap per MC tick.
        double target = Tuning.Fov * (Motor.Sprinting ? Tuning.SprintFovMultiplier : 1.0);
        double ease = 1 - Math.Pow(1 - Tuning.FovSmoothingPerMcTick, delta * Tuning.McTickRate);
        _fov += (float)((target - _fov) * ease);
        Camera.Fov = _fov;
        // Minecraft's hurt camera: a quick tilt that eases back.
        float f = Vitality.HurtFlashFraction;
        float tilt = MathF.Sin(f * f * f * f * MathF.PI) * 14f;
        Camera.RotationDegrees = new Vector3(Pitch, 0, -tilt);
        if (ViewModel is not null)
        {
            ViewModel.Visible = !IsDead;
        }
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

    private void ApplyRotation()
    {
        if (IsLocal)
        {
            RotationDegrees = new Vector3(0, Yaw, 0);
            if (Camera is not null)
            {
                Camera.RotationDegrees = new Vector3(Pitch, 0, Camera.RotationDegrees.Z);
            }
        }
        else
        {
            Model.RotationDegrees = new Vector3(0, Yaw, 0);
            Model.SetHeadPitch(Pitch);
        }
    }
}
