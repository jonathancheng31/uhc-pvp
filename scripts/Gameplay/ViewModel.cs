using System;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// The sword in first person, bottom right of the screen, with Minecraft 1.8's swing animation
/// (ItemRenderer.transformFirstPersonItem and doItemUsedTransformations). Drawn on top of the
/// world so it never clips into walls.
/// </summary>
public partial class ViewModel : Node3D
{
    private static readonly double SwingSeconds = (double)Tuning.SwingMcTicks / Tuning.McTickRate;

    private Node3D _item = null!;
    private double _swingTime = -1;

    public override void _Ready()
    {
        _item = new Node3D { Name = "Sword" };
        AddChild(_item);
        BuildSword(_item);
        Apply(0);
    }

    public void Swing() => _swingTime = 0;

    public override void _Process(double delta)
    {
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
