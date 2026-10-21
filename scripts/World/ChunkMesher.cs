using System.Collections.Generic;
using Godot;

namespace UhcPvp.World;

/// <summary>Mesh data for one chunk, ready to hand to Godot.</summary>
public sealed class ChunkMeshData
{
    public List<Vector3> Vertices { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<Vector2> Uvs { get; } = new();
    public List<Color> Colors { get; } = new();
    public List<int> Indices { get; } = new();
    public bool IsEmpty => Vertices.Count == 0;
}

/// <summary>
/// Turns the blocks of one chunk into a mesh. Only faces that touch air are drawn. Each face
/// gets a fixed brightness (tops brightest, bottoms darkest) instead of real lighting, which
/// keeps the classic blocky look and costs nothing to render.
/// </summary>
public static class ChunkMesher
{
    /// <summary>Brightness per face, in <see cref="Face"/> order.</summary>
    public static readonly float[] FaceShade = [0.8f, 0.8f, 1.0f, 0.5f, 0.6f, 0.6f];

    public static readonly Vector3I[] Normals =
    [
        new(1, 0, 0), new(-1, 0, 0),
        new(0, 1, 0), new(0, -1, 0),
        new(0, 0, 1), new(0, 0, -1),
    ];

    /// <summary>Small inset so neighbouring atlas tiles never bleed into each other.</summary>
    public const float UvInset = 0.001f;

    /// <summary>Corner offsets per face, wound so the face is visible from outside.</summary>
    private static readonly Vector3[][] Corners = BuildFaceCorners();

    /// <summary>The four corner offsets of a face, in drawing order.</summary>
    public static IReadOnlyList<Vector3> FaceCorners(Face face) => Corners[(int)face];

    public static ChunkMeshData Build(VoxelWorld world, Vector3I chunk)
    {
        var data = new ChunkMeshData();
        Vector3I origin = chunk * VoxelWorld.ChunkSize;
        for (int ly = 0; ly < VoxelWorld.ChunkSize; ly++)
        {
            for (int lz = 0; lz < VoxelWorld.ChunkSize; lz++)
            {
                for (int lx = 0; lx < VoxelWorld.ChunkSize; lx++)
                {
                    int x = origin.X + lx;
                    int y = origin.Y + ly;
                    int z = origin.Z + lz;
                    Block block = world.Get(x, y, z);
                    if (!block.IsSolid())
                    {
                        continue;
                    }
                    for (int f = 0; f < 6; f++)
                    {
                        Vector3I n = Normals[f];
                        if (!world.IsSolid(x + n.X, y + n.Y, z + n.Z))
                        {
                            AddFace(data, new Vector3(x, y, z), (Face)f, Blocks.TileFor(block, (Face)f));
                        }
                    }
                }
            }
        }
        return data;
    }

    public static ArrayMesh? BuildMesh(VoxelWorld world, Vector3I chunk)
    {
        ChunkMeshData data = Build(world, chunk);
        if (data.IsEmpty)
        {
            return null;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = data.Uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = data.Indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static void AddFace(ChunkMeshData data, Vector3 block, Face face, int tile)
    {
        int start = data.Vertices.Count;
        var normal = (Vector3)Normals[(int)face];
        float shade = FaceShade[(int)face];
        var color = new Color(shade, shade, shade);
        var tileOrigin = new Vector2(tile % Blocks.AtlasColumns, tile / Blocks.AtlasColumns);
        foreach (Vector3 corner in Corners[(int)face])
        {
            data.Vertices.Add(block + corner);
            data.Normals.Add(normal);
            data.Colors.Add(color);
            data.Uvs.Add((tileOrigin + CornerUv(corner, face)) / Blocks.AtlasColumns);
        }
        data.Indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
    }

    /// <summary>Where a corner falls on the face's texture, from (0, 0) top-left to (1, 1).</summary>
    private static Vector2 CornerUv(Vector3 corner, Face face)
    {
        Vector2 uv = face switch
        {
            Face.PosX or Face.NegX => new Vector2(corner.Z, 1f - corner.Y),
            Face.PosZ or Face.NegZ => new Vector2(corner.X, 1f - corner.Y),
            _ => new Vector2(corner.X, corner.Z),
        };
        return uv.Clamp(new Vector2(UvInset, UvInset), new Vector2(1f - UvInset, 1f - UvInset));
    }

    /// <summary>
    /// Orders each face's corners so triangles (0, 1, 2) and (0, 2, 3) wind clockwise when seen
    /// from outside, which is what Godot treats as the front.
    /// </summary>
    private static Vector3[][] BuildFaceCorners()
    {
        var result = new Vector3[6][];
        Vector2[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        for (int f = 0; f < 6; f++)
        {
            var n = (Vector3)Normals[f];
            int axis = n.X != 0 ? 0 : n.Y != 0 ? 1 : 2;
            int uAxis = (axis + 1) % 3;
            int vAxis = (axis + 2) % 3;
            float plane = n[axis] > 0 ? 1f : 0f;
            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3();
                c[axis] = plane;
                c[uAxis] = square[i].X;
                c[vAxis] = square[i].Y;
                corners[i] = c;
            }
            if ((corners[1] - corners[0]).Cross(corners[2] - corners[0]).Dot(n) > 0f)
            {
                System.Array.Reverse(corners);
            }
            result[f] = corners;
        }
        return result;
    }
}
