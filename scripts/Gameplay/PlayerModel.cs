using System;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// A blocky placeholder body: head, torso, arms and legs, with the shirt in the team's color
/// and a name tag overhead. Proportions follow a 32-pixel-tall character scaled to the
/// 1.8-block player height. Legs and arms swing with movement and the body leans when sneaking,
/// using Minecraft's animation numbers.
/// </summary>
public partial class PlayerModel : Node3D
{
    public const float Pixel = (float)Tuning.PlayerHeight / 32f;

    /// <summary>Lean of the upper body while sneaking, in radians (Minecraft: 0.5).</summary>
    private const float SneakLean = 0.5f;

    private static readonly Color Skin = new(0.87f, 0.68f, 0.53f);
    private static readonly Color Pants = new(0.2f, 0.22f, 0.32f);

    private Node3D _head = null!;
    private Node3D _upperBody = null!;
    private Node3D _leftArm = null!;
    private Node3D _rightArm = null!;
    private Node3D _leftLeg = null!;
    private Node3D _rightLeg = null!;
    private Label3D _nameTag = null!;
    private float _swing;
    private float _swingAmount;
    private float _pitch;
    private bool _sneaking;

    public void Setup(string playerName, Color teamColor)
    {
        // Legs hang from the hips (12 px up) so they can swing.
        _leftLeg = AddPivot(new Vector3(-2, 12, 0), this);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -6, 0), Pants, _leftLeg);
        _rightLeg = AddPivot(new Vector3(2, 12, 0), this);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -6, 0), Pants, _rightLeg);

        // Torso and arms lean together from the hips.
        _upperBody = AddPivot(new Vector3(0, 12, 0), this);
        AddBox(new Vector3(8, 12, 4), new Vector3(0, 6, 0), teamColor, _upperBody);
        _leftArm = AddPivot(new Vector3(-6, 10, 0), _upperBody);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -4, 0), teamColor.Darkened(0.15f), _leftArm);
        _rightArm = AddPivot(new Vector3(6, 10, 0), _upperBody);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -4, 0), teamColor.Darkened(0.15f), _rightArm);

        // Head on a pivot at the neck so it can look up and down.
        _head = AddPivot(new Vector3(0, 24, 0), this);
        var face = AddBox(new Vector3(8, 8, 8), new Vector3(0, 4, 0), Skin, _head);
        foreach (float x in new[] { -2f, 2f })
        {
            // Eyes, so you can tell which way someone is facing.
            AddBox(new Vector3(1.5f, 1, 0.2f), new Vector3(x, 0.5f, -4.05f), new Color(0.1f, 0.1f, 0.15f), face);
        }

        _nameTag = new Label3D
        {
            Name = "NameTag",
            Text = playerName,
            Modulate = teamColor.Lightened(0.35f),
            OutlineModulate = new Color(0, 0, 0, 0.8f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.01f,
            FontSize = 32,
            Position = new Vector3(0, (float)Tuning.PlayerHeight + 0.35f, 0),
        };
        AddChild(_nameTag);
    }

    /// <summary>Pitch in degrees; positive looks up.</summary>
    public void SetHeadPitch(float pitch)
    {
        _pitch = pitch;
        ApplyPose();
    }

    /// <summary>
    /// Advances the walk cycle. <paramref name="horizontalSpeed"/> is in blocks per second.
    /// Follows Minecraft's limb swing: the swing amount chases (blocks moved per tick * 4) and
    /// the legs swing by cos(swing * 0.6662) * 1.4 * amount.
    /// </summary>
    public void Animate(double delta, double horizontalSpeed, bool sneaking)
    {
        float ticks = (float)(delta * Tuning.McTickRate);
        float target = Math.Min((float)(horizontalSpeed / Tuning.McTickRate) * 4f, 1f);
        _swingAmount += (target - _swingAmount) * (1f - MathF.Pow(0.6f, ticks));
        _swing += _swingAmount * ticks;
        _sneaking = sneaking;
        ApplyPose();
    }

    private void ApplyPose()
    {
        float leg = MathF.Cos(_swing * 0.6662f) * 1.4f * _swingAmount;
        float arm = MathF.Cos(_swing * 0.6662f + MathF.PI) * _swingAmount;
        float lean = _sneaking ? SneakLean : 0f;
        _leftLeg.Rotation = new Vector3(leg, 0, 0);
        _rightLeg.Rotation = new Vector3(-leg, 0, 0);
        _leftArm.Rotation = new Vector3(arm - (_sneaking ? 0.4f : 0f), 0, 0);
        _rightArm.Rotation = new Vector3(-arm - (_sneaking ? 0.4f : 0f), 0, 0);
        // Leaning forward means tipping the top toward -Z.
        _upperBody.Rotation = new Vector3(-lean, 0, 0);
        // The head sits a little lower and further forward while sneaking.
        float drop = _sneaking ? 3f : 0f;
        _head.Position = new Vector3(0, 24 - drop, _sneaking ? -4f : 0f) * Pixel;
        _head.RotationDegrees = new Vector3(_pitch, 0, 0);
        _nameTag.Position = new Vector3(0, (float)Tuning.PlayerHeight + (_sneaking ? 0.2f : 0.35f), 0);
    }

    private static Node3D AddPivot(Vector3 positionPx, Node3D parent)
    {
        var pivot = new Node3D { Position = positionPx * Pixel };
        parent.AddChild(pivot);
        return pivot;
    }

    private static MeshInstance3D AddBox(Vector3 sizePx, Vector3 centerPx, Color color, Node3D parent)
    {
        var instance = new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = sizePx * Pixel,
                Material = new StandardMaterial3D { AlbedoColor = color },
            },
            Position = centerPx * Pixel,
        };
        parent.AddChild(instance);
        return instance;
    }
}
