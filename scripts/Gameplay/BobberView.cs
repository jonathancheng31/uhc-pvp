using System;
using Godot;
using UhcPvp.Combat;
using UhcPvp.Movement;

namespace UhcPvp.Gameplay;

/// <summary>
/// Draws one bobber and the line from the thrower's rod tip to it. Its place each frame comes
/// from <see cref="Game.UpdateBobber"/>, which runs after the players have moved so the line
/// stays attached to the hand.
/// </summary>
public partial class BobberView : Node3D
{
    private const int LineSegments = 16;
    /// <summary>How far the middle of the line hangs below straight, per block of line.</summary>
    private const float SagPerBlock = 0.03f;
    private const float MaxSag = 0.6f;

    private readonly Game _game;
    private Node3D _float = null!;
    private ImmediateMesh _line = null!;
    private StandardMaterial3D _lineMaterial = null!;

    public BobberView(Game game, Bobber bobber)
    {
        _game = game;
        Bobber = bobber;
        // After every player (priority 0) has placed itself and its hands this frame.
        ProcessPriority = 10;
    }

    public Bobber Bobber { get; }
    /// <summary>Thrower's game only: host time up to which this bobber's path was checked for players.</summary>
    public double CheckedUntil { get; set; }
    /// <summary>Someone else's bobber: whom it caught and when, to show once their delayed view gets there.</summary>
    public (int VictimId, double Time)? PendingHook { get; set; }
    /// <summary>One of our bots' bobbers: caught someone at this host time, shown once the bot's delayed view gets there.</summary>
    public double ShowHookAt { get; set; } = double.NegativeInfinity;
    /// <summary>Whether it has been shown yet (the cast animation and sound play then).</summary>
    public bool Appeared { get; private set; }
    /// <summary>The landing whose sound has played (<see cref="Bobber.LandedAt"/>), so each plays once.</summary>
    public double? HeardLandingAt { get; set; }
    /// <summary>Where it was last shown.</summary>
    public Vec3d Shown { get; private set; }

    public override void _Ready()
    {
        Visible = false;
        TopLevel = true;
        _float = new Node3D { Name = "Float" };
        AddChild(_float);
        // Our own bobber: a small cube, red on top and white below, with a dark tip.
        AddCube(_float, new Vector3(0.14f, 0.07f, 0.14f), new Vector3(0, 0.105f, 0), new Color(0.85f, 0.12f, 0.1f));
        AddCube(_float, new Vector3(0.14f, 0.07f, 0.14f), new Vector3(0, 0.035f, 0), new Color(0.95f, 0.95f, 0.92f));
        AddCube(_float, new Vector3(0.04f, 0.05f, 0.04f), new Vector3(0, 0.165f, 0), new Color(0.15f, 0.15f, 0.15f));
        _line = new ImmediateMesh();
        _lineMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.08f, 0.08f, 0.08f),
        };
        AddChild(new MeshInstance3D
        {
            Name = "Line",
            Mesh = _line,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The line moves every frame; don't let culling hide it based on a stale box.
            ExtraCullMargin = 64,
        });
    }

    public override void _Process(double delta) => _game.UpdateBobber(this);

    /// <summary>
    /// Places the bobber at <paramref name="position"/> and draws the line from
    /// <paramref name="rodTip"/>. <paramref name="onViewer"/>: it hangs on the player whose eyes
    /// the camera is, where it would only block the view, so only the line shows.
    /// </summary>
    public void Show(Vec3d position, Vector3 rodTip, bool onViewer)
    {
        Appeared = true;
        Visible = true;
        Shown = position;
        Vector3 bobber = position.ToGodot();
        _float.Position = bobber;
        _float.Visible = !onViewer;
        Vector3 end = bobber + new Vector3(0, 0.1f, 0);
        float length = rodTip.DistanceTo(end);
        float sag = Math.Min(length * SagPerBlock, MaxSag);
        _line.ClearSurfaces();
        _line.SurfaceBegin(Mesh.PrimitiveType.LineStrip, _lineMaterial);
        for (int i = 0; i <= LineSegments; i++)
        {
            float t = i / (float)LineSegments;
            _line.SurfaceAddVertex(rodTip.Lerp(end, t) - new Vector3(0, sag * 4 * t * (1 - t), 0));
        }
        _line.SurfaceEnd();
    }

    private static void AddCube(Node3D parent, Vector3 size, Vector3 center, Color color) =>
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size, Material = new StandardMaterial3D { AlbedoColor = color } },
            Position = center,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
}
