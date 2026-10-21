using Godot;
using UhcPvp.Core;

namespace UhcPvp.Gameplay;

/// <summary>
/// A blocky placeholder body: head, torso, arms and legs, with the shirt in the team's color
/// and a name tag overhead. Proportions follow a 32-pixel-tall character scaled to the
/// 1.8-block player height.
/// </summary>
public partial class PlayerModel : Node3D
{
    public const float Pixel = (float)Tuning.PlayerHeight / 32f;

    private static readonly Color Skin = new(0.87f, 0.68f, 0.53f);
    private static readonly Color Pants = new(0.2f, 0.22f, 0.32f);

    private Node3D _head = null!;

    public void Setup(string playerName, Color teamColor)
    {
        // Legs: 4x12x4 pixels each, from the feet up to 12 px.
        AddBox(new Vector3(4, 12, 4), new Vector3(-2, 6, 0), Pants, this);
        AddBox(new Vector3(4, 12, 4), new Vector3(2, 6, 0), Pants, this);
        // Torso 8x12x4 and arms 4x12x4, from 12 px to 24 px.
        AddBox(new Vector3(8, 12, 4), new Vector3(0, 18, 0), teamColor, this);
        AddBox(new Vector3(4, 12, 4), new Vector3(-6, 18, 0), teamColor.Darkened(0.15f), this);
        AddBox(new Vector3(4, 12, 4), new Vector3(6, 18, 0), teamColor.Darkened(0.15f), this);
        // Head 8x8x8 on a pivot at the neck so it can look up and down.
        _head = new Node3D { Name = "Head", Position = new Vector3(0, 24, 0) * Pixel };
        AddChild(_head);
        var face = AddBox(new Vector3(8, 8, 8), new Vector3(0, 4, 0), Skin, _head);
        // Eyes, so you can tell which way someone is facing.
        foreach (float x in new[] { -2f, 2f })
        {
            AddBox(new Vector3(1.5f, 1, 0.2f), new Vector3(x, 4.5f, -4.05f), new Color(0.1f, 0.1f, 0.15f), face);
        }

        AddChild(new Label3D
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
        });
    }

    /// <summary>Pitch in degrees; positive looks up.</summary>
    public void SetHeadPitch(float pitch) => _head.RotationDegrees = new Vector3(pitch, 0, 0);

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
