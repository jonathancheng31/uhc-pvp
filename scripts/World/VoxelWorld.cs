using System;
using Godot;

namespace UhcPvp.World;

/// <summary>
/// A fixed-size grid of blocks. Block (x, y, z) fills the cube from (x, y, z) to
/// (x + 1, y + 1, z + 1) in world space.
///
/// The world is split into 16x16x16 chunks for drawing, so that placing or breaking a block
/// later only needs one chunk redrawn.
/// </summary>
public sealed class VoxelWorld
{
    public const int ChunkSize = 16;

    private readonly Block[] _blocks;

    public VoxelWorld(Vector3I size)
    {
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
        Size = size;
        _blocks = new Block[size.X * size.Y * size.Z];
    }

    public Vector3I Size { get; }

    public Vector3I ChunkCounts => new(
        (Size.X + ChunkSize - 1) / ChunkSize,
        (Size.Y + ChunkSize - 1) / ChunkSize,
        (Size.Z + ChunkSize - 1) / ChunkSize);

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && y >= 0 && z >= 0 && x < Size.X && y < Size.Y && z < Size.Z;

    /// <summary>The block at a position; outside the world is air.</summary>
    public Block Get(int x, int y, int z) => InBounds(x, y, z) ? _blocks[Index(x, y, z)] : Block.Air;

    public void Set(int x, int y, int z, Block block)
    {
        if (InBounds(x, y, z))
        {
            _blocks[Index(x, y, z)] = block;
        }
    }

    public bool IsSolid(int x, int y, int z) => Get(x, y, z).IsSolid();

    /// <summary>Fills the box between two corners, both included.</summary>
    public void Fill(Vector3I from, Vector3I to, Block block)
    {
        for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
        {
            for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
            {
                for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
                {
                    Set(x, y, z, block);
                }
            }
        }
    }

    private int Index(int x, int y, int z) => x + Size.X * (z + Size.Z * y);
}
