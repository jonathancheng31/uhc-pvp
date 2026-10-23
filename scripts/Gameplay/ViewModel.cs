using System;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// First person: the sword at the bottom right of the screen, with Minecraft 1.8's swing
/// animation (ItemRenderer.transformFirstPersonItem and doItemUsedTransformations), and the
/// fishing rod in the left hand, which flicks forward on a cast. Drawn on top of the world so
/// they never clip into walls.
/// </summary>
public partial class ViewModel : Node3D
{
    private static readonly double SwingSeconds = (double)Tuning.SwingMcTicks / Tuning.McTickRate;
    private const double CastSeconds = 0.3;

    private Node3D _item = null!;
    private Node3D _rod = null!;
    private Node3D _rodTip = null!;
    private double _swingTime = -1;
    private double _castTime = -1;

    /// <summary>Where the rod's line starts, in world space.</summary>
    public Vector3 RodTip => _rodTip.GlobalPosition;

    public override void _Ready()
    {
        _item = new Node3D { Name = "Sword" };
        AddChild(_item);
        BuildSword(_item);
        Apply(0);
        _rod = new Node3D { Name = "Rod" };
        AddChild(_rod);
        _rodTip = BuildRod(_rod);
        ApplyCast(-1);
    }

    public void Swing() => _swingTime = 0;

    /// <summary>
    /// Keeps the sword and rod looking the same at any field of view, as if drawn at
    /// <see cref="Tuning.HandFov"/> like Minecraft's hand. Stretching them sideways and up by the
    /// ratio of the two views' widths puts every point exactly where the hand view would draw it.
    /// </summary>
    public void MatchFov(float fov)
    {
        float k = HandScale(fov);
        Scale = new Vector3(k, k, 1);
    }

    /// <summary>How much to stretch the view model at <paramref name="fov"/> (1 at <see cref="Tuning.HandFov"/>).</summary>
    public static float HandScale(float fov) =>
        MathF.Tan(Mathf.DegToRad(fov) / 2) / MathF.Tan(Mathf.DegToRad(Tuning.HandFov) / 2);

    /// <summary>Flicks the rod forward (a cast).</summary>
    public void Cast() => _castTime = 0;

    public override void _Process(double delta)
    {
        if (_castTime >= 0)
        {
            _castTime += delta;
            if (_castTime >= CastSeconds)
            {
                _castTime = -1;
            }
            ApplyCast(_castTime < 0 ? -1 : (float)(_castTime / CastSeconds));
        }
        if (_swingTime < 0)
        {
            return;
        }
        _swingTime += delta;
        if (_swingTime >= SwingSeconds)
        {
            _swingTime = -1;
            Apply(0);
            return;
        }
        Apply((float)(_swingTime / SwingSeconds));
    }

    /// <summary>Places the rod for a cast progress from 0 to 1 (-1 at rest): a quick flick back, then forward.</summary>
    private void ApplyCast(float p)
    {
        float flick = p < 0 ? 0 : MathF.Sin(p * MathF.PI * 2f) * (1 - p) * 35f;
        float lift = p < 0 ? 0 : MathF.Sin(p * MathF.PI) * 0.06f;
        _rod.Transform = new Transform3D(
            new Basis(Vector3.Right, Mathf.DegToRad(flick)) * RodRestPose,
            RodRestPosition + new Vector3(0, lift, 0));
    }

    /// <summary>Places the sword for a swing progress from 0 to 1, using Minecraft's curves.</summary>
    private void Apply(float p)
    {
        float sqrtP = MathF.Sqrt(p);
        // Minecraft's swing: the item jerks left/up/forward (doItemUsedTransformations)...
        var swingOffset = new Vector3(
            -0.4f * MathF.Sin(sqrtP * MathF.PI),
            0.2f * MathF.Sin(sqrtP * MathF.PI * 2f),
            -0.2f * MathF.Sin(p * MathF.PI));
        float f = MathF.Sin(p * p * MathF.PI);
        float f1 = MathF.Sin(sqrtP * MathF.PI);
        // ...and chops forward while twisting (transformFirstPersonItem).
        Basis swing = new Basis(Vector3.Up, Mathf.DegToRad(f * -20f))
            * new Basis(Vector3.Back, Mathf.DegToRad(f1 * -20f))
            * new Basis(Vector3.Right, Mathf.DegToRad(f1 * -80f));
        _item.Transform = new Transform3D(swing * RestPose, RestPosition + swingOffset);
    }

    /// <summary>Handle position in camera space at rest: bottom right of the screen.</summary>
    private static readonly Vector3 RestPosition = new(0.52f, -0.46f, -0.8f);

    /// <summary>At rest the blade leans left toward the crosshair and tips away from the camera.</summary>
    private static readonly Basis RestPose =
        new Basis(Vector3.Back, Mathf.DegToRad(45f))
        * new Basis(Vector3.Right, Mathf.DegToRad(-30f))
        * Basis.FromScale(Vector3.One * 0.032f);

    /// <summary>Rod handle position in camera space at rest: bottom left of the screen.</summary>
    private static readonly Vector3 RodRestPosition = new(-0.6f, -0.52f, -0.85f);

    /// <summary>At rest the rod leans right toward the crosshair and tips well away from the camera.</summary>
    private static readonly Basis RodRestPose =
        new Basis(Vector3.Back, Mathf.DegToRad(-30f))
        * new Basis(Vector3.Right, Mathf.DegToRad(-58f))
        * Basis.FromScale(Vector3.One * 0.032f);

    /// <summary>
    /// Our own pixel fishing rod, upright from the handle (at the origin), in units of one
    /// pixel. Returns the tip, where the line starts.
    /// </summary>
    private static Node3D BuildRod(Node3D parent)
    {
        var grip = new Color(0.25f, 0.17f, 0.11f);
        var pole = new Color(0.55f, 0.38f, 0.22f);
        var poleDark = new Color(0.45f, 0.3f, 0.17f);
        var reel = new Color(0.6f, 0.62f, 0.66f);
        AddBlock(parent, new Vector3(-0.5f, -1, 0), new Vector3(1, 5, 1), grip);
        AddBlock(parent, new Vector3(0.5f, 1, 0), new Vector3(1.5f, 2, 1.5f), reel);
        AddBlock(parent, new Vector3(-0.5f, 4, 0), new Vector3(1, 9, 1), pole);
        AddBlock(parent, new Vector3(-0.4f, 13, 0), new Vector3(0.8f, 7, 0.8f), poleDark);
        var tip = new Node3D { Name = "Tip", Position = new Vector3(0, 20, 0) };
        parent.AddChild(tip);
        return tip;
    }

    /// <summary>
    /// Our own pixel sword, built upright from the handle (at the origin) to the tip, one pixel
    /// thick, in units of one pixel.
    /// </summary>
    private static void BuildSword(Node3D parent)
    {
        var blade = new Color(0.62f, 0.93f, 0.97f);
        var bladeEdge = new Color(0.32f, 0.7f, 0.78f);
        var guard = new Color(0.2f, 0.22f, 0.26f);
        var handle = new Color(0.42f, 0.27f, 0.15f);
        AddBlock(parent, new Vector3(-0.5f, -1, 0), new Vector3(1, 1, 1), guard); // pommel
        AddBlock(parent, new Vector3(-0.5f, 0, 0), new Vector3(1, 4, 1), handle);
        AddBlock(parent, new Vector3(-2.5f, 4, 0), new Vector3(5, 1, 1.2f), guard);
        AddBlock(parent, new Vector3(-1f, 5, 0), new Vector3(1, 10, 1), blade);
        AddBlock(parent, new Vector3(0f, 5, 0), new Vector3(1, 10, 1), bladeEdge);
        AddBlock(parent, new Vector3(-0.5f, 15, 0), new Vector3(1, 1, 1), blade); // tip
    }

    /// <summary>A box from <paramref name="corner"/> (x centred) spanning <paramref name="size"/> pixels.</summary>
    private static void AddBlock(Node3D parent, Vector3 corner, Vector3 size, Color color)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size, Material = material },
            Position = corner + size / 2 - new Vector3(0, 0, size.Z / 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
