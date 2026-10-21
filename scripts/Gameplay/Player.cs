using Godot;
using UhcPvp.Autoload;
using UhcPvp.Core;
using UhcPvp.World;

namespace UhcPvp.Gameplay;

/// <summary>
/// One player in the match. The local player has a first-person camera and sends its state to
/// the host; other players are drawn from their recent states, slightly in the past
/// (see <see cref="SnapshotBuffer"/>).
/// </summary>
public partial class Player : Node3D
{
    private Vector3 _spawn;
    private double _botTime;
    private readonly double _mouseDegrees = Tuning.DegreesPerMouseCount(Tuning.DefaultMouseSensitivity);

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

    public void Setup(int id, bool local)
    {
        var session = Session.Instance;
        PeerId = id;
        IsLocal = local;
        PlayerName = session.NameOf(id);
        Team = session.TeamOf(id);
        Name = $"Player_{id}";
        _spawn = Arena.SpawnPosition(Team, session.SlotOf(id));
        Position = _spawn;
        Yaw = Arena.SpawnYaw(Team);

        Model = new PlayerModel { Name = "Model" };
        Model.Setup(PlayerName, Team.Color());
        AddChild(Model);

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
            Yaw = Mathf.Wrap(Yaw - (float)(motion.ScreenRelative.X * _mouseDegrees), -180f, 180f);
            Pitch = Mathf.Clamp(Pitch - (float)(motion.ScreenRelative.Y * _mouseDegrees), -90f, 90f);
            ApplyRotation();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsLocal && LaunchArgs.Instance.Bot)
        {
            _botTime += delta;
            Position = _spawn + new Vector3(Mathf.Cos((float)_botTime * 1.5f), 0, Mathf.Sin((float)_botTime * 1.5f)) * 2f;
            Yaw = Mathf.Wrap(Yaw + 90f * (float)delta, -180f, 180f);
            ApplyRotation();
        }
    }

    public PlayerState GetState() => new(Position, Yaw, Pitch, Flags);

    /// <summary>A state for this (remote) player arrived at host time <paramref name="hostNow"/>.</summary>
    public void ReceiveState(double time, PlayerState state, double hostNow)
    {
        if (Buffer.Push(time, state))
        {
            Buffer.NoteArrivalAge(hostNow - time);
        }
    }

    /// <summary>Moves this (remote) player to where they were <see cref="SnapshotBuffer.RenderDelay"/> ago.</summary>
    public void UpdateRemote(double hostNow)
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
