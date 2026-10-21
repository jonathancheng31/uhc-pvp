using System.Collections.Generic;
using Godot;

namespace UhcPvp.World;

/// <summary>Draws a <see cref="VoxelWorld"/> as one mesh per chunk.</summary>
public partial class WorldRenderer : Node3D
{
    private readonly Dictionary<Vector3I, MeshInstance3D> _chunks = new();
    private VoxelWorld _world = null!;
    private StandardMaterial3D _material = null!;

    public int ChunkInstanceCount => _chunks.Count;

    public void Build(VoxelWorld world)
    {
        _world = world;
        _material = MakeMaterial();
        Vector3I counts = world.ChunkCounts;
        for (int cy = 0; cy < counts.Y; cy++)
        {
            for (int cz = 0; cz < counts.Z; cz++)
            {
                for (int cx = 0; cx < counts.X; cx++)
                {
                    RebuildChunk(new Vector3I(cx, cy, cz));
                }
            }
        }
    }

    /// <summary>Redraws one chunk, e.g. after a block in it changed.</summary>
    public void RebuildChunk(Vector3I chunk)
    {
        ArrayMesh? mesh = ChunkMesher.BuildMesh(_world, chunk);
        _chunks.TryGetValue(chunk, out MeshInstance3D? instance);
        if (mesh is null)
        {
            if (instance is not null)
            {
                instance.QueueFree();
                _chunks.Remove(chunk);
            }
            return;
        }
        if (instance is null)
        {
            instance = new MeshInstance3D
            {
                Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}",
                MaterialOverride = _material,
            };
            AddChild(instance);
            _chunks[chunk] = instance;
        }
        instance.Mesh = mesh;
    }

    private static StandardMaterial3D MakeMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        AlbedoTexture = GD.Load<Texture2D>(Blocks.AtlasPath),
    };
}
