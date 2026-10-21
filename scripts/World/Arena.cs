using Godot;
using UhcPvp.Core;

namespace UhcPvp.World;

/// <summary>The flat test arena: grass floor, walls around the edge, and each team's spawn line.</summary>
public static class Arena
{
    public static Vector3I WorldSize
    {
        get
        {
            int width = Tuning.ArenaSize + 2; // one wall block on each side
            int height = Tuning.ArenaFloorY + Tuning.ArenaWallHeight + 4;
            return new Vector3I(width, height, width);
        }
    }

    public static VoxelWorld Build()
    {
        Vector3I size = WorldSize;
        var world = new VoxelWorld(size);
        int top = Tuning.ArenaFloorY - 1;
        int last = size.X - 1;
        // Floor layers, bottom to top.
        world.Fill(new(0, 0, 0), new(last, 0, last), Block.Wall);
        world.Fill(new(0, 1, 0), new(last, top - 2, last), Block.Stone);
        world.Fill(new(0, top - 1, 0), new(last, top - 1, last), Block.Dirt);
        world.Fill(new(0, top, 0), new(last, top, last), Block.Grass);
        // Walls around the edge.
        int wallTop = Tuning.ArenaFloorY + Tuning.ArenaWallHeight - 1;
        world.Fill(new(0, 0, 0), new(last, wallTop, 0), Block.Wall);
        world.Fill(new(0, 0, last), new(last, wallTop, last), Block.Wall);
        world.Fill(new(0, 0, 0), new(0, wallTop, last), Block.Wall);
        world.Fill(new(last, 0, 0), new(last, wallTop, last), Block.Wall);
        return world;
    }

    public static Vector3 Center
    {
        get
        {
            Vector3I size = WorldSize;
            return new Vector3(size.X * 0.5f, Tuning.ArenaFloorY, size.Z * 0.5f);
        }
    }

    /// <summary>Spawn position for a team member; <paramref name="slot"/> is their index within the team.</summary>
    public static Vector3 SpawnPosition(Team team, int slot)
    {
        float side = team == Team.Red ? 1f : -1f;
        float spread = (slot - (Tuning.MaxPlayersPerTeam - 1) * 0.5f) * 2.5f;
        return Center + new Vector3(spread, 0f, side * (float)Tuning.SpawnDistance);
    }

    /// <summary>Yaw in degrees that faces the other team. Yaw 0 faces -Z.</summary>
    public static float SpawnYaw(Team team) => team == Team.Red ? 0f : 180f;
}
