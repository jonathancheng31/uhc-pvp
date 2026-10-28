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
/// hits on them do. Everyone but you is drawn in the past by the match's head start (see
/// <see cref="HeadStart"/>): remote players from the states they send (<see cref="Buffer"/>),
/// our bots from the states they leave (<see cref="History"/>). Bots see everyone, you included,
/// the same way.
/// </summary>
public partial class Player : Node3D
{
    private static readonly int N = Tuning.SubticksPerMcTick;
    private static readonly double SwingSeconds = (double)Tuning.SwingMcTicks / Tuning.McTickRate;
    private static readonly double FlashSeconds = (double)Tuning.HurtFlashMcTicks / Tuning.McTickRate;
    private const double CastSeconds = 0.3;

    private Game _game = null!;
    private Vec3d _previousPosition;
    /// <summary>Where an owned player stood before each of its last few ticks (for <see cref="PushBody"/>).</summary>
    private readonly Vec3d[] _feetBefore = new Vec3d[N];
    private int _feetIndex;
    /// <summary>For pushing: the side of each player we're touching that we came from (by id), this tick and last.</summary>
    private Dictionary<int, Vec3d> _pushSides = new();
    private Dictionary<int, Vec3d> _pushSidesNext = new();
    private Vector3 _lastShownPosition;
    private readonly MissPenalty _missPenalty = new();
    /// <summary>Counts subticks to find Minecraft's 1/20 s client ticks (for the miss penalty).</summary>
    private int _subtick;
    private double _sinceHurt;
    private double _swingTime = -1;
    private double _castTime = -1;
    private double _flashTime;
    private double _deadTime;
    /// <summary>Whether the state this player is drawn in (anyone but you) is alive.</summary>
    private bool _shownAlive = true;

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
    /// <summary>A remote player's states, as they arrive.</summary>
    public SnapshotBuffer Buffer { get; } = new();
    /// <summary>The states of a player this game runs, one per tick, to show them in the past.</summary>
    public SnapshotBuffer History { get; } = new();
    /// <summary>Movement, for players this game runs; null for remote players.</summary>
    public PlayerMotor? Motor { get; private set; }
    /// <summary>Real health for owned players; for remote players, the last health we heard.</summary>
    public Vitality Vitality { get; } = new();
    /// <summary>The rod's cooldown after a throw, for players this game runs.</summary>
    public RodCooldown RodCooldown { get; } = new();
    /// <summary>The wasted-clicks twist's lockout, for players this game runs.</summary>
    public ClickLockout ClickLockout { get; } = new();
    /// <summary>The AI for bots (and for --bot test players); null for people.</summary>
    public BotBrain? Brain { get; set; }

    /// <summary>Where this player (re)spawns, and which way they face.</summary>
    public Vec3d Home { get; set; }
    public float HomeYaw { get; set; }

    public bool IsDead => IsOwned ? Vitality.IsDead : (Flags & PlayerFlags.Dead) != 0;
    /// <summary>
    /// Whether this game knows this player is dead right now: really, for a player it runs;
    /// for anyone else, from their latest state or the hit that killed them, until a state shows
    /// them back. (A remote player's <see cref="IsDead"/> is as they're drawn, the head start ago.)
    /// </summary>
    public bool KnownDead => IsOwned ? Vitality.IsDead : Buffer.KnownDead;
    public double Health => Vitality.Health;
    /// <summary>Seconds until a dead owned player respawns.</summary>
    public double RespawnIn { get; private set; }

    /// <summary>Where this player really is (owned players: latest tick; remote: as shown).</summary>
    public Vec3d Feet => Motor?.Position ?? Vec3d.From(Position);
    /// <summary>Where this player is drawn on this screen right now.</summary>
    public Vec3d ShownFeet => Vec3d.From(Position);
    public bool Sneaking => (Flags & PlayerFlags.Sneaking) != 0;
    /// <summary>
    /// Eye position for aiming, of a player this game runs (Minecraft lowers it 0.08 when
    /// sneaking): yours as the camera shows it right now, a bot's where it really is.
    /// </summary>
    public Vec3d Eye => (IsLocal ? ShownFeet : Feet) + new Vec3d(0, Tuning.EyeHeight - (Sneaking ? Tuning.SneakEyeDrop : 0), 0);

    /// <summary>
    /// How far in the past everyone on this computer (you and our bots) sees this player, in
    /// seconds: the match's head start, or what their updates need to arrive if that's more.
    /// </summary>
    public double ViewDelay => HeadStart.ViewDelay(Session.Instance.Rules.HeadStart, IsOwned, Buffer.RenderDelay);

    /// <summary>Host time of the state of this player that's seen at host time <paramref name="hostNow"/>.</summary>
    public double ViewTime(double hostNow) => hostNow - ViewDelay;

    /// <summary>Where this player is seen at host time <paramref name="hostNow"/> (drawn there, unless it's you).</summary>
    public Vec3d ViewFeet(double hostNow) =>
        States.IsEmpty ? Feet : Vec3d.From(States.Sample(ViewTime(hostNow)).Position);

    /// <summary>Which way this player is seen facing at host time <paramref name="hostNow"/>.</summary>
    public double ViewYaw(double hostNow) => States.IsEmpty ? Yaw : States.Sample(ViewTime(hostNow)).Yaw;

    /// <summary>
    /// This player as our bots see them at host time <paramref name="hostNow"/>: where, and
    /// whether they can be hit there, from the same moment (see <see cref="Sighting"/>).
    /// </summary>
    public Sighting SeenAt(double hostNow) =>
        States.IsEmpty ? new Sighting(Feet, !KnownDead) : Sighting.Of(States.Sample(ViewTime(hostNow)), KnownDead);

    /// <summary>This player as drawn on this screen (for your clicks and bobber): where, and whether they can be hit there.</summary>
    public Sighting Shown => new(ShownFeet, _shownAlive && !KnownDead);

    /// <summary>
    /// Where this player is by the freshest news: really, if this game runs them; for a remote
    /// player, where their latest updates put them, without the head start. Used for pushing.
    /// </summary>
    public Vec3d FreshFeet(double hostNow) =>
        IsOwned || Buffer.IsEmpty ? Feet : Vec3d.From(Buffer.Sample(hostNow - Buffer.RenderDelay).Position);

    private SnapshotBuffer States => IsOwned ? History : Buffer;
    public bool CanAttack => IsOwned && !IsDead && !_missPenalty.Blocks && !ClickLockout.Blocks;

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
            Array.Fill(_feetBefore, home);
        }
        if (IsLocal)
        {
            Model.Visible = false; // first person: don't draw our own body
            Camera = new Camera3D
            {
                Name = "Camera",
                Fov = Settings.Instance.Current.Fov,
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
            double degrees = Settings.Instance.Current.DegreesPerMouseCount;
            Look(Yaw - (float)(motion.ScreenRelative.X * degrees), Pitch - (float)(motion.ScreenRelative.Y * degrees));
        }
        else if (@event.IsActionPressed(Controls.Attack))
        {
            // Every click is one attack, like 1.8: no cooldown, so clicking speed matters.
            _game.TryAttack(this);
        }
        else if (@event.IsActionPressed(Controls.Use))
        {
            // Right-click throws the rod (again, after its cooldown: the old bobber vanishes and a new one flies).
            _game.ThrowRod(this);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Motor is null)
        {
            return;
        }
        Vitality.Tick();
        RodCooldown.Tick();
        ClickLockout.Tick();
        _subtick = (_subtick + 1) % N;
        if (_subtick == 0)
        {
            _missPenalty.Tick(AttackHeld);
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
        if (intent?.Attack == true)
        {
            // A bot's click comes before its move, as Minecraft handles clicks before the player
            // moves each tick (and as a person's click lands between our ticks).
            _game.TryAttack(this);
        }
        _previousPosition = Motor.Position;
        _feetBefore[_feetIndex] = Motor.Position;
        _feetIndex = (_feetIndex + 1) % N;
        // The match's twists, as the host set them (the same for every player).
        MatchRules rules = Session.Instance.Rules;
        Motor.StrafeSpeed = rules.StrafeSpeed;
        Motor.StrafeGrip = rules.StrafeGrip;
        Motor.PushStrength = rules.Pushing;
        if (rules.Pushing > 0)
        {
            _game.FindPushContacts(this, Motor.Contacts);
        }
        else
        {
            Motor.Contacts.Clear();
            _pushSides.Clear();
        }
        Motor.Step(input);
        if (Motor.Position.Y < Tuning.VoidY)
        {
            Respawn();
        }
        if (intent?.Rod == true)
        {
            _game.ThrowRod(this);
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
        // Everyone but you falls and gets up as drawn, the head start after it happens.
        _deadTime = (IsLocal ? IsDead : !_shownAlive) ? _deadTime + delta : 0;
        if (_castTime >= 0)
        {
            _castTime += delta;
            if (_castTime >= CastSeconds)
            {
                _castTime = -1;
            }
        }

        if (IsLocal && Motor is not null)
        {
            // Draw between the last two ticks so motion looks smooth at any frame rate.
            float t = (float)Engine.GetPhysicsInterpolationFraction();
            Position = _previousPosition.ToGodot().Lerp(Motor.Position.ToGodot(), t);
        }
        if (Camera is not null && Motor is not null)
        {
            UpdateCamera();
        }
        Model.SetHealth(Vitality.Health);
        Model.SetSwing(SwingProgress, Pitch);
        Model.SetCast(_castTime < 0 ? -1 : (float)(_castTime / CastSeconds));
        Model.SetHurtTint((float)(_flashTime / FlashSeconds));
        Model.SetDeath(_deadTime);
    }

    /// <summary>0 to 1 through an arm swing, or -1 when not swinging.</summary>
    public float SwingProgress => _swingTime < 0 ? -1 : (float)(_swingTime / SwingSeconds);

    /// <summary>
    /// Starts an arm swing (Minecraft restarts one only once it's half done). Returns whether it
    /// started, which is when its whoosh plays.
    /// </summary>
    public bool PlaySwing()
    {
        if (_swingTime >= 0 && _swingTime < SwingSeconds / 2)
        {
            return false;
        }
        _swingTime = 0;
        ViewModel?.Swing();
        return true;
    }

    /// <summary>Plays the rod cast: the left arm flicks forward.</summary>
    public void PlayCast()
    {
        _castTime = 0;
        ViewModel?.Cast();
    }

    /// <summary>Where this player's fishing line starts: the rod tip on screen.</summary>
    public Vector3 RodTip => ViewModel is { Visible: true } ? ViewModel.RodTip : Model.RodTip;

    /// <summary>Clicking at nothing: Minecraft 1.8's miss penalty (see <see cref="MissPenalty"/>).</summary>
    public void StartMissCooldown() => _missPenalty.Missed();

    /// <summary>
    /// Whether the attack button is held down. Bots click (press and release) instantly, so
    /// they never hold it.
    /// </summary>
    private bool AttackHeld =>
        IsLocal && Brain is null && Input.MouseMode == Input.MouseModeEnum.Captured && Input.IsActionPressed(Controls.Attack);

    /// <summary>
    /// When (<see cref="Network.Now"/>) this player's red flash last started: a full hit, which
    /// starts their hit protection. Practice bots watch for it, as people do.
    /// </summary>
    public double HurtShownAt { get; private set; } = double.NegativeInfinity;
    /// <summary>Whether that hit was a rod's.</summary>
    public bool HurtShownByRod { get; private set; }

    /// <summary>Shows a hit on this player (flash, health). Sound and particles are played by the game.</summary>
    public void ShowHurt(Hurt hurt)
    {
        if ((hurt.Flags & HurtFlags.Fresh) != 0)
        {
            _flashTime = FlashSeconds;
            HurtShownAt = Network.Instance.Now;
            HurtShownByRod = (hurt.Flags & HurtFlags.Rod) != 0;
        }
        if (!IsOwned)
        {
            Vitality.Show(hurt.Health);
            if ((hurt.Flags & HurtFlags.Killed) != 0)
            {
                Flags |= PlayerFlags.Dead;
                Buffer.NoteKilled(double.IsFinite(hurt.AttackTime) ? hurt.AttackTime : Network.Instance.HostTime);
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
        RodCooldown.Reset();
        ClickLockout.Reset();
        Motor.Teleport(Home);
        _previousPosition = Home;
        Array.Fill(_feetBefore, Home);
        _pushSides.Clear();
        Look(HomeYaw, 0);
        RespawnIn = 0;
        Flags = Motor.Flags;
    }

    /// <summary>
    /// This player's body for pushing, as this game sees it at host time <paramref name="hostNow"/>:
    /// where they stand and how far they moved over the last Minecraft tick (1/20 s). Owned
    /// players: where they really are. Remote players: where their latest updates put them, as
    /// smoothly as they arrive (<see cref="SnapshotBuffer.RenderDelay"/>), without the head start
    /// they're drawn with (see <see cref="FreshFeet"/>).
    /// </summary>
    public (Vec3d Feet, Vec3d Move, bool Flying) PushBody(double hostNow)
    {
        if (Motor is not null)
        {
            // The oldest entry is from three ticks back, one Minecraft tick.
            return (Motor.Position, Motor.Position - _feetBefore[_feetIndex], Motor.KnockbackFlight);
        }
        if (Buffer.IsEmpty)
        {
            return (ShownFeet, Vec3d.Zero, false);
        }
        double shown = hostNow - Buffer.RenderDelay;
        PlayerState state = Buffer.Sample(shown);
        Vec3d feet = Vec3d.From(state.Position);
        Vec3d before = Vec3d.From(Buffer.Sample(shown - 1.0 / Tuning.McTickRate).Position);
        return (feet, feet - before, state.Has(PlayerFlags.KnockbackFlight));
    }

    /// <summary>Starts a new round of <see cref="PushContactWith"/> (one per tick).</summary>
    public void BeginPushContacts() => _pushSidesNext.Clear();

    /// <summary>Ends a round: sides are only remembered for players touched this tick.</summary>
    public void EndPushContacts() => (_pushSides, _pushSidesNext) = (_pushSidesNext, _pushSides);

    /// <summary>
    /// This (owned) player's push contact with another player whose body is at
    /// <paramref name="feet"/> and moved <paramref name="move"/> over the last Minecraft tick,
    /// keeping track of which side of them we came from (see <see cref="Pushing.Side"/>).
    /// </summary>
    public PushContact PushContactWith(int otherId, Vec3d feet, Vec3d move)
    {
        Vec3d remembered = _pushSides.GetValueOrDefault(otherId);
        Vec3d tieBreak = Pushing.TieBreak(PeerId, otherId);
        if (Pushing.Touching(Feet, feet))
        {
            _pushSidesNext[otherId] = Pushing.Side(Feet, feet, remembered, tieBreak);
        }
        return new PushContact(feet, move, remembered, tieBreak);
    }

    /// <summary>A tick of a player this game runs ended at host time <paramref name="time"/>: keeps its state, to show it in the past.</summary>
    public void Record(double time) => History.Push(time, GetState());

    /// <summary>The state to send to everyone else: the latest tick, not the smoothed view.</summary>
    public PlayerState GetState() => new(
        Feet.ToGodot(), Yaw, Pitch, Flags | (IsDead ? PlayerFlags.Dead : 0), (float)Vitality.Health);

    /// <summary>A state for this (remote) player arrived at host time <paramref name="hostNow"/>.</summary>
    public void ReceiveState(double time, PlayerState state, double hostNow)
    {
        if (Buffer.Push(time, state))
        {
            Buffer.NoteArrivalAge(hostNow - time);
        }
    }

    /// <summary>
    /// Draws this player (anyone but you) where they're seen at host time
    /// <paramref name="hostNow"/>: <see cref="ViewDelay"/> in the past. A remote player also takes
    /// their look, flags and health from that state; a bot keeps its real ones for playing.
    /// </summary>
    public void UpdateDrawn(double hostNow, double delta)
    {
        if (States.IsEmpty)
        {
            return;
        }
        PlayerState state = States.Sample(ViewTime(hostNow));
        Position = state.Position;
        _shownAlive = !state.Has(PlayerFlags.Dead);
        if (!IsOwned)
        {
            Yaw = state.Yaw;
            Pitch = state.Pitch;
            Flags = state.Flags;
            Vitality.Show(state.Health);
        }
        Model.RotationDegrees = new Vector3(0, state.Yaw, 0);
        Model.SetHeadPitch(state.Pitch);
        Vector3 moved = Position - _lastShownPosition;
        _lastShownPosition = Position;
        double speed = delta > 0 ? new Vector2(moved.X, moved.Z).Length() / delta : 0;
        Model.Animate(delta, speed, state.Has(PlayerFlags.Sneaking));
    }

    private void UpdateCamera()
    {
        if (Camera is null || Motor is null)
        {
            return;
        }
        double eye = IsDead ? 0.3 : Tuning.EyeHeight - (Motor.Sneaking ? Tuning.SneakEyeDrop : 0);
        Camera.Position = new Vector3(0, (float)eye, 0);
        // Minecraft's hurt camera: a quick tilt that eases back, scaled by the player's setting
        // (a quarter of Minecraft's by default; 0 turns it off).
        float f = Vitality.HurtFlashFraction;
        float tilt = MathF.Sin(f * f * f * f * MathF.PI) * Tuning.HurtTiltDegrees * (float)Settings.Instance.Current.HurtShake;
        Camera.RotationDegrees = new Vector3(Pitch, 0, -tilt);
        Camera.Fov = Settings.Instance.Current.Fov;
        if (ViewModel is not null)
        {
            ViewModel.Visible = !IsDead;
            ViewModel.MatchFov(Camera.Fov);
        }
    }

    private MovementInput KeyboardInput()
    {
        string held = LaunchArgs.Instance.HoldKeys;
        if (held.Length > 0)
        {
            // --hold: keys held down all match (tests and screenshots).
            static double Key(string keys, char key) => keys.Contains(key, StringComparison.Ordinal) ? 1 : 0;
            return new MovementInput(Key(held, 'w') - Key(held, 's'), Key(held, 'a') - Key(held, 'd'),
                Key(held, 'j') > 0, Sprint: true, false, Yaw);
        }
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // Menus open: stand still, like Minecraft.
            return new MovementInput(0, 0, false, false, false, Yaw);
        }
        double forward = Input.GetActionStrength(Controls.MoveForward) - Input.GetActionStrength(Controls.MoveBack);
        double strafe = Input.GetActionStrength(Controls.MoveLeft) - Input.GetActionStrength(Controls.MoveRight);
        // Sprinting is always on: moving forward sprints, as if the sprint key were held.
        return new MovementInput(
            forward, strafe,
            Input.IsActionPressed(Controls.Jump),
            Sprint: true,
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
