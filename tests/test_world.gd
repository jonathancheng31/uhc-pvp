extends TestCase


func test_get_and_set_blocks() -> void:
	var world := VoxelWorld.new(Vector3i(4, 4, 4))
	check_eq(world.get_block(1, 2, 3), Blocks.AIR, "starts empty")
	world.set_block(1, 2, 3, Blocks.STONE)
	check_eq(world.get_block(1, 2, 3), Blocks.STONE, "set block")
	check_eq(world.get_block(-1, 0, 0), Blocks.AIR, "out of bounds is air")
	world.set_block(9, 9, 9, Blocks.STONE) # ignored, no crash


func test_single_block_has_six_faces() -> void:
	var world := VoxelWorld.new(Vector3i(16, 16, 16))
	world.set_block(5, 5, 5, Blocks.STONE)
	var arrays := ChunkMesher.build_arrays(world, Vector3i.ZERO)
	check_eq(arrays[Mesh.ARRAY_VERTEX].size(), 6 * 4, "vertices")
	check_eq(arrays[Mesh.ARRAY_INDEX].size(), 6 * 6, "indices")


func test_hidden_faces_are_skipped() -> void:
	var world := VoxelWorld.new(Vector3i(16, 16, 16))
	world.set_block(5, 5, 5, Blocks.STONE)
	world.set_block(6, 5, 5, Blocks.STONE)
	var arrays := ChunkMesher.build_arrays(world, Vector3i.ZERO)
	check_eq(arrays[Mesh.ARRAY_VERTEX].size(), 10 * 4, "two touching blocks show 10 faces")


func test_empty_chunk_has_no_mesh() -> void:
	var world := VoxelWorld.new(Vector3i(16, 16, 16))
	check(ChunkMesher.build_arrays(world, Vector3i.ZERO).is_empty(), "empty")


func test_faces_wind_clockwise_from_outside() -> void:
	# Godot draws the side of a triangle that winds clockwise, so every face
	# must wind clockwise when seen from outside the block.
	for face in 6:
		var corners: Array = ChunkMesher.face_corners(face)
		var a: Vector3 = corners[0]
		var b: Vector3 = corners[1]
		var c: Vector3 = corners[2]
		var normal := Vector3(ChunkMesher.NORMALS[face])
		check((b - a).cross(c - a).dot(normal) < 0.0, "face %d winding" % face)
		for corner: Vector3 in corners:
			var on_plane: float = corner.dot(normal.abs())
			check_near(on_plane, 1.0 if normal.dot(Vector3.ONE) > 0 else 0.0, 0.0001, "face %d plane" % face)


func test_arena_floor_and_walls() -> void:
	var world := Arena.build()
	var c := Arena.center()
	check_eq(world.get_block(int(c.x), Tuning.ARENA_FLOOR_Y - 1, int(c.z)), Blocks.GRASS, "grass under center")
	check_eq(world.get_block(int(c.x), Tuning.ARENA_FLOOR_Y, int(c.z)), Blocks.AIR, "air above floor")
	check_eq(world.get_block(0, Tuning.ARENA_FLOOR_Y, int(c.z)), Blocks.WALL, "wall at edge")


func test_spawns_are_inside_arena_and_teams_face_each_other() -> void:
	var size := Arena.world_size()
	for team in [Arena.Team.RED, Arena.Team.BLUE]:
		for slot in Tuning.MAX_PLAYERS_PER_TEAM:
			var p := Arena.spawn_position(team, slot)
			check(p.x > 1.0 and p.x < size.x - 1.0 and p.z > 1.0 and p.z < size.z - 1.0,
				"team %d slot %d inside walls" % [team, slot])
	var red_forward := Vector3.FORWARD.rotated(Vector3.UP, deg_to_rad(Arena.spawn_yaw(Arena.Team.RED)))
	var to_blue := Arena.spawn_position(Arena.Team.BLUE, 0) - Arena.spawn_position(Arena.Team.RED, 0)
	check(red_forward.dot(to_blue.normalized()) > 0.9, "red faces blue")
