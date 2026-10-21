using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// A blocky placeholder body: head, torso, arms and legs, with the shirt in the team's color,
/// a sword in the right hand, a fishing rod in the left, and a name tag with health overhead. Proportions follow a 32-pixel-tall
/// character scaled to the 1.8-block player height. Walking, sneaking, attacking, getting hurt
/// and dying use Minecraft 1.8's animation numbers.
/// </summary>
public partial class PlayerModel : Node3D
{
    public const float Pixel = (float)Tuning.PlayerHeight / 32f;

    /// <summary>Lean of the upper body while sneaking, in radians (Minecraft: 0.5).</summary>
    private const float SneakLean = 0.5f;

    private static readonly Color Skin = new(0.87f, 0.68f, 0.53f);
    private static readonly Color Pants = new(0.2f, 0.22f, 0.32f);
    private static readonly Color HurtRed = new(1f, 0.15f, 0.1f);

    private readonly List<(StandardMaterial3D Material, Color Base)> _materials = new();
    private Node3D _pose = null!;
    private Node3D _head = null!;
    private Node3D _upperBody = null!;
    private Node3D _leftArm = null!;
    private Node3D _rightArm = null!;
    private Node3D _leftLeg = null!;
    private Node3D _rightLeg = null!;
    private Label3D _nameTag = null!;
    private Label3D _healthTag = null!;
    private Node3D _rodTip = null!;
    private float _walkSwing;
    private float _walkAmount;
    private float _attackSwing = -1;
    private float _cast = -1;
    private float _pitch;
    private bool _sneaking;
    private float _shownHealth = -1;

    public void Setup(string playerName, Color teamColor)
    {
        // Everything that falls over on death hangs off _pose.
        _pose = new Node3D { Name = "Pose" };
        AddChild(_pose);

        // Legs hang from the hips (12 px up) so they can swing.
        _leftLeg = AddPivot(new Vector3(-2, 12, 0), _pose);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -6, 0), Pants, _leftLeg);
        _rightLeg = AddPivot(new Vector3(2, 12, 0), _pose);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -6, 0), Pants, _rightLeg);

        // Torso and arms lean together from the hips.
        _upperBody = AddPivot(new Vector3(0, 12, 0), _pose);
        AddBox(new Vector3(8, 12, 4), new Vector3(0, 6, 0), teamColor, _upperBody);
        _leftArm = AddPivot(new Vector3(-6, 10, 0), _upperBody);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -4, 0), teamColor.Darkened(0.15f), _leftArm);
        _rightArm = AddPivot(new Vector3(6, 10, 0), _upperBody);
        AddBox(new Vector3(4, 12, 4), new Vector3(0, -4, 0), teamColor.Darkened(0.15f), _rightArm);
        AddHeldSword(_rightArm);
        _rodTip = AddHeldRod(_leftArm);

        // Head on a pivot at the neck so it can look up and down.
        _head = AddPivot(new Vector3(0, 24, 0), _pose);
        var face = AddBox(new Vector3(8, 8, 8), new Vector3(0, 4, 0), Skin, _head);
        foreach (float x in new[] { -2f, 2f })
        {
            // Eyes, so you can tell which way someone is facing.
            AddBox(new Vector3(1.5f, 1, 0.2f), new Vector3(x, 0.5f, -4.05f), new Color(0.1f, 0.1f, 0.15f), face);
        }

        _nameTag = MakeLabel(playerName, teamColor.Lightened(0.35f), 32);
        AddChild(_nameTag);
        _healthTag = MakeLabel("", new Color(1f, 0.45f, 0.45f), 24);
        AddChild(_healthTag);
        PlaceTags();
    }

    /// <summary>Where the rod's line starts, in world space.</summary>
    public Vector3 RodTip => _rodTip.GlobalPosition;

    /// <summary>Rod cast progress from 0 to 1, or -1 when not casting.</summary>
    public void SetCast(float progress)
    {
        _cast = progress;
        ApplyPose();
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
        _walkAmount += (target - _walkAmount) * (1f - MathF.Pow(0.6f, ticks));
        _walkSwing += _walkAmount * ticks;
        _sneaking = sneaking;
        ApplyPose();
    }

    /// <summary>Attack swing progress from 0 to 1, or -1 when not swinging.</summary>
    public void SetSwing(float progress, float pitch)
    {
        _attackSwing = progress;
        _pitch = pitch;
        ApplyPose();
    }

    /// <summary>Red hurt tint, 1 right after a hit fading to 0.</summary>
    public void SetHurtTint(float amount)
    {
        float a = Math.Clamp(amount, 0f, 1f) * 0.6f;
        foreach (var (material, baseColor) in _materials)
        {
            material.AlbedoColor = baseColor.Lerp(HurtRed, a);
        }
    }

    /// <summary>Falls over like Minecraft's death animation, then disappears after a second.</summary>
    public void SetDeath(double secondsDead)
    {
        if (secondsDead <= 0)
        {
            _pose.Rotation = Vector3.Zero;
            _pose.Visible = true;
            _nameTag.Visible = _healthTag.Visible = true;
            return;
        }
        double ticks = secondsDead * Tuning.McTickRate;
        float fall = (float)Math.Min(Math.Sqrt(Math.Max(ticks - 1, 0) / 20.0 * 1.6), 1.0);
        _pose.Rotation = new Vector3(0, 0, fall * MathF.PI / 2);
        _pose.Visible = secondsDead < 1.0;
        _nameTag.Visible = _healthTag.Visible = false;
    }

    /// <summary>Shows health under the name tag.</summary>
    public void SetHealth(double health)
    {
        float rounded = MathF.Round((float)health * 2) / 2;
        if (rounded != _shownHealth)
        {
            _shownHealth = rounded;
            _healthTag.Text = rounded.ToString("0.#", CultureInfo.InvariantCulture) + " HP";
        }
    }

    private void ApplyPose()
    {
        float leg = MathF.Cos(_walkSwing * 0.6662f) * 1.4f * _walkAmount;
        float arm = MathF.Cos(_walkSwing * 0.6662f + MathF.PI) * _walkAmount;
        float sneakArms = _sneaking ? 0.4f : 0f;
        _leftLeg.Rotation = new Vector3(leg, 0, 0);
        _rightLeg.Rotation = new Vector3(-leg, 0, 0);
        // Casting the rod: the left arm comes up and flicks forward.
        float cast = _cast >= 0 ? MathF.Sin(_cast * MathF.PI) * 1.1f : 0f;
        _leftArm.Rotation = new Vector3(arm - sneakArms + cast, 0, 0);
        var rightArm = new Vector3(-arm - sneakArms, 0, 0);
        float bodyTwist = 0f;
        if (_attackSwing >= 0)
        {
            // ModelBiped's attack swing: the arm chops forward and the body twists a little.
            float p = _attackSwing;
            bodyTwist = MathF.Sin(MathF.Sqrt(p) * MathF.PI * 2f) * 0.2f;
            float eased = 1f - MathF.Pow(1f - p, 4);
            float chop = MathF.Sin(eased * MathF.PI) * 1.2f;
            float headFollow = MathF.Sin(p * MathF.PI) * (Mathf.DegToRad(_pitch) + 0.7f) * 0.75f;
            rightArm = new Vector3(rightArm.X + chop + headFollow, bodyTwist * 2f, MathF.Sin(p * MathF.PI) * 0.4f);
        }
        _rightArm.Rotation = rightArm;
        float lean = _sneaking ? SneakLean : 0f;
        // Leaning forward means tipping the top toward -Z.
        _upperBody.Rotation = new Vector3(-lean, bodyTwist, 0);
        // The head sits a little lower and further forward while sneaking.
        float drop = _sneaking ? 3f : 0f;
        _head.Position = new Vector3(0, 24 - drop, _sneaking ? -4f : 0f) * Pixel;
        _head.RotationDegrees = new Vector3(_pitch, 0, 0);
        PlaceTags();
    }

    private void PlaceTags()
    {
        float top = (float)Tuning.PlayerHeight + (_sneaking ? 0.2f : 0.35f);
        _nameTag.Position = new Vector3(0, top, 0);
        _healthTag.Position = new Vector3(0, top - 0.22f, 0);
    }

    /// <summary>A small blocky sword held in the right hand, pointing forward.</summary>
    private void AddHeldSword(Node3D arm)
    {
        var hand = AddPivot(new Vector3(0, -10, -1), arm);
        hand.Rotation = new Vector3(-1.2f, 0, 0);
        AddBox(new Vector3(1, 3, 1), new Vector3(0, -0.5f, 0), new Color(0.42f, 0.27f, 0.15f), hand);
        AddBox(new Vector3(3.5f, 0.8f, 1.2f), new Vector3(0, 1.2f, 0), new Color(0.2f, 0.22f, 0.26f), hand);
        AddBox(new Vector3(1.3f, 9, 0.6f), new Vector3(0, 6, 0), new Color(0.55f, 0.9f, 0.95f), hand);
    }

    /// <summary>A small blocky fishing rod held in the left hand, pointing forward and up. Returns its tip.</summary>
    private Node3D AddHeldRod(Node3D arm)
    {
        var hand = AddPivot(new Vector3(0, -10, -1), arm);
        hand.Rotation = new Vector3(-1.0f, 0, 0);
        AddBox(new Vector3(1, 3, 1), new Vector3(0, -0.5f, 0), new Color(0.25f, 0.17f, 0.11f), hand);
        AddBox(new Vector3(0.7f, 16, 0.7f), new Vector3(0, 9, 0), new Color(0.55f, 0.38f, 0.22f), hand);
        return AddPivot(new Vector3(0, 17, 0), hand);
    }

    private static Label3D MakeLabel(string text, Color color, int size) => new()
    {
        Text = text,
        Modulate = color,
        OutlineModulate = new Color(0, 0, 0, 0.8f),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        PixelSize = 0.01f,
        FontSize = size,
    };

    private static Node3D AddPivot(Vector3 positionPx, Node3D parent)
    {
        var pivot = new Node3D { Position = positionPx * Pixel };
        parent.AddChild(pivot);
        return pivot;
    }

    private MeshInstance3D AddBox(Vector3 sizePx, Vector3 centerPx, Color color, Node3D parent)
    {
        var material = new StandardMaterial3D { AlbedoColor = color };
        _materials.Add((material, color));
        var instance = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = sizePx * Pixel, Material = material },
            Position = centerPx * Pixel,
        };
        parent.AddChild(instance);
        return instance;
    }
}
