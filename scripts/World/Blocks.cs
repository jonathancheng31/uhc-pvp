namespace UhcPvp.World;

/// <summary>Block types. Stored as one byte per block.</summary>
public enum Block : byte
{
    Air,
    Grass,
    Dirt,
    Stone,
    Wall,
    Planks,
}

/// <summary>Block faces, in the order the mesher uses.</summary>
public enum Face
{
    PosX,
    NegX,
    PosY,
    NegY,
    PosZ,
    NegZ,
}

public static class Blocks
{
    /// <summary>The texture atlas is a square grid of 16x16 pixel tiles.</summary>
    public const string AtlasPath = "res://assets/textures/blocks.png";
    public const int AtlasColumns = 4;

    public const int TileGrassTop = 0;
    public const int TileGrassSide = 1;
    public const int TileDirt = 2;
    public const int TileStone = 3;
    public const int TileWall = 4;
    public const int TilePlanks = 5;

    public static bool IsSolid(this Block block) => block != Block.Air;

    public static int TileFor(Block block, Face face) => block switch
    {
        Block.Grass => face switch
        {
            Face.PosY => TileGrassTop,
            Face.NegY => TileDirt,
            _ => TileGrassSide,
        },
        Block.Dirt => TileDirt,
        Block.Stone => TileStone,
        Block.Wall => TileWall,
        Block.Planks => TilePlanks,
        _ => TileStone,
    };
}
