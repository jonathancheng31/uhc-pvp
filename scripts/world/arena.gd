class_name Arena
extends RefCounted
## The flat test arena: grass floor, walls around the edge, and the spawn
## lines for each team.

enum Team { RED, BLUE }


static func world_size() -> Vector3i:
	var width := Tuning.ARENA_SIZE + 2 # one wall block on each side
	var height := Tuning.ARENA_FLOOR_Y + Tuning.ARENA_WALL_HEIGHT + 4
	return Vector3i(width, height, width)


static func build() -> VoxelWorld:
	var size := world_size()
	var world := VoxelWorld.new(size)
	var top := Tuning.ARENA_FLOOR_Y - 1
	var last := size.x - 1
	# Floor layers, bottom to top.
	world.fill(Vector3i(0, 0, 0), Vector3i(last, 0, last), Blocks.WALL)
	world.fill(Vector3i(0, 1, 0), Vector3i(last, top - 2, last), Blocks.STONE)
	world.fill(Vector3i(0, top - 1, 0), Vector3i(last, top - 1, last), Blocks.DIRT)
	world.fill(Vector3i(0, top, 0), Vector3i(last, top, last), Blocks.GRASS)
	# Walls around the edge.
	var wall_top := Tuning.ARENA_FLOOR_Y + Tuning.ARENA_WALL_HEIGHT - 1
	world.fill(Vector3i(0, 0, 0), Vector3i(last, wall_top, 0), Blocks.WALL)
	world.fill(Vector3i(0, 0, last), Vector3i(last, wall_top, last), Blocks.WALL)
	world.fill(Vector3i(0, 0, 0), Vector3i(0, wall_top, last), Blocks.WALL)
	world.fill(Vector3i(last, 0, 0), Vector3i(last, wall_top, last), Blocks.WALL)
	return world


static func center() -> Vector3:
	var size := world_size()
	return Vector3(size.x * 0.5, Tuning.ARENA_FLOOR_Y, size.z * 0.5)


## Spawn position for a team member; `slot` is their index within the team.
static func spawn_position(team: int, slot: int) -> Vector3:
	var side := 1.0 if team == Team.RED else -1.0
	var spread := (slot - (Tuning.MAX_PLAYERS_PER_TEAM - 1) * 0.5) * 2.5
	return center() + Vector3(spread, 0.0, side * Tuning.SPAWN_DISTANCE)


## Yaw in degrees that faces the other team. Yaw 0 faces -Z.
static func spawn_yaw(team: int) -> float:
	return 0.0 if team == Team.RED else 180.0


static func team_name(team: int) -> String:
	return "Red" if team == Team.RED else "Blue"


static func team_color(team: int) -> Color:
	return Color(0.86, 0.22, 0.22) if team == Team.RED else Color(0.23, 0.45, 0.93)
