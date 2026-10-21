using System.Linq;
using Godot;
using UhcPvp.Core;
using UhcPvp.World;

namespace UhcPvp.Tests;

public class WorldTests : TestCase
{
    public void TestGetAndSetBlocks()
    {
        var world = new VoxelWorld(new Vector3I(4, 4, 4));
        CheckEqual(world.Get(1, 2, 3), Block.Air, "starts empty");
        world.Set(1, 2, 3, Block.Stone);
        CheckEqual(world.Get(1, 2, 3), Block.Stone, "set block");
        CheckEqual(world.Get(-1, 0, 0), Block.Air, "out of bounds is air");
        world.Set(9, 9, 9, Block.Stone); // ignored, no crash
    }

    public void TestSingleBlockHasSixFaces()
    {
        var world = new VoxelWorld(new Vector3I(16, 16, 16));
        world.Set(5, 5, 5, Block.Stone);
        ChunkMeshData data = ChunkMesher.Build(world, Vector3I.Zero);
        CheckEqual(data.Vertices.Count, 6 * 4, "vertices");
        CheckEqual(data.Indices.Count, 6 * 6, "indices");
    }

    public void TestHiddenFacesAreSkipped()
    {
        var world = new VoxelWorld(new Vector3I(16, 16, 16));
        world.Set(5, 5, 5, Block.Stone);
        world.Set(6, 5, 5, Block.Stone);
        CheckEqual(ChunkMesher.Build(world, Vector3I.Zero).Vertices.Count, 10 * 4, "two touching blocks show 10 faces");
    }

    public void TestEmptyChunkHasNoMesh() =>
        Check(ChunkMesher.Build(new VoxelWorld(new Vector3I(16, 16, 16)), Vector3I.Zero).IsEmpty, "empty");

    public void TestFacesWindClockwiseFromOutside()
    {
        // Godot draws the side of a triangle that winds clockwise, so every face must wind
        // clockwise when seen from outside the block.
        foreach (Face face in System.Enum.GetValues<Face>())
        {
            var c = ChunkMesher.FaceCorners(face);
            var normal = (Vector3)ChunkMesher.Normals[(int)face];
            Check((c[1] - c[0]).Cross(c[2] - c[0]).Dot(normal) < 0f, $"{face} winding");
            float expectedPlane = normal.X + normal.Y + normal.Z > 0 ? 1f : 0f;
            Check(c.All(corner => Mathf.IsEqualApprox(corner.Dot(normal.Abs()), expectedPlane)), $"{face} plane");
        }
    }

    public void TestArenaFloorAndWalls()
    {
        VoxelWorld world = Arena.Build();
        Vector3 c = Arena.Center;
        CheckEqual(world.Get((int)c.X, Tuning.ArenaFloorY - 1, (int)c.Z), Block.Grass, "grass under center");
        CheckEqual(world.Get((int)c.X, Tuning.ArenaFloorY, (int)c.Z), Block.Air, "air above floor");
        CheckEqual(world.Get(0, Tuning.ArenaFloorY, (int)c.Z), Block.Wall, "wall at edge");
    }

    public void TestSpawnsAreInsideArenaAndTeamsFaceEachOther()
    {
        Vector3I size = Arena.WorldSize;
        foreach (Team team in new[] { Team.Red, Team.Blue })
        {
            for (int slot = 0; slot < Tuning.MaxPlayersPerTeam; slot++)
            {
                Vector3 p = Arena.SpawnPosition(team, slot);
                Check(p.X > 1 && p.X < size.X - 1 && p.Z > 1 && p.Z < size.Z - 1, $"{team} slot {slot} inside walls");
            }
        }
        Vector3 redForward = Vector3.Forward.Rotated(Vector3.Up, Mathf.DegToRad(Arena.SpawnYaw(Team.Red)));
        Vector3 toBlue = Arena.SpawnPosition(Team.Blue, 0) - Arena.SpawnPosition(Team.Red, 0);
        Check(redForward.Dot(toBlue.Normalized()) > 0.9f, "red faces blue");
    }
}
