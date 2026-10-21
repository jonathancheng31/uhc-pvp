class_name VoxelWorld
extends RefCounted
## A fixed-size grid of blocks. Block (x, y, z) fills the cube from
## (x, y, z) to (x + 1, y + 1, z + 1) in world space.
##
## The world is split into 16x16x16 chunks for drawing, so that placing or
## breaking a block later only needs one chunk redrawn.

const CHUNK_SIZE := 16

var size: Vector3i
var _blocks := PackedByteArray()


func _init(world_size: Vector3i) -> void:
	size = world_size
	_blocks.resize(size.x * size.y * size.z)


func in_bounds(x: int, y: int, z: int) -> bool:
	return x >= 0 and y >= 0 and z >= 0 and x < size.x and y < size.y and z < size.z


func get_block(x: int, y: int, z: int) -> int:
	if not in_bounds(x, y, z):
		return Blocks.AIR
	return _blocks[_index(x, y, z)]


func set_block(x: int, y: int, z: int, id: int) -> void:
	if in_bounds(x, y, z):
		_blocks[_index(x, y, z)] = id


func is_solid(x: int, y: int, z: int) -> bool:
	return Blocks.is_solid(get_block(x, y, z))


## Fills the box between two corners, both included.
func fill(from: Vector3i, to: Vector3i, id: int) -> void:
	for y in range(mini(from.y, to.y), maxi(from.y, to.y) + 1):
		for z in range(mini(from.z, to.z), maxi(from.z, to.z) + 1):
			for x in range(mini(from.x, to.x), maxi(from.x, to.x) + 1):
				set_block(x, y, z, id)


func chunk_counts() -> Vector3i:
	return Vector3i(
		ceili(float(size.x) / CHUNK_SIZE),
		ceili(float(size.y) / CHUNK_SIZE),
		ceili(float(size.z) / CHUNK_SIZE))


func _index(x: int, y: int, z: int) -> int:
	return x + size.x * (z + size.z * y)
