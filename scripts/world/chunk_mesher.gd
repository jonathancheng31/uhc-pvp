class_name ChunkMesher
extends RefCounted
## Turns the blocks of one chunk into mesh arrays. Only faces that touch air
## are drawn. Each face gets a fixed brightness (tops brightest, bottoms
## darkest) instead of real lighting, which keeps the classic blocky look and
## costs nothing to render.

## Brightness per face, in Blocks.Face order (+X, -X, +Y, -Y, +Z, -Z).
const FACE_SHADE: Array[float] = [0.8, 0.8, 1.0, 0.5, 0.6, 0.6]
const NORMALS: Array[Vector3i] = [
	Vector3i(1, 0, 0), Vector3i(-1, 0, 0),
	Vector3i(0, 1, 0), Vector3i(0, -1, 0),
	Vector3i(0, 0, 1), Vector3i(0, 0, -1),
]
## Small inset so neighbouring atlas tiles never bleed into each other.
const UV_INSET := 0.001

## Corner offsets per face, wound so the face is visible from outside.
static var _face_corners: Array = _build_face_corners()


## Mesh arrays (Mesh.ARRAY_MAX long) for one chunk, or an empty Array when the
## chunk has nothing to draw.
static func build_arrays(world: VoxelWorld, chunk: Vector3i) -> Array:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()

	var origin := chunk * VoxelWorld.CHUNK_SIZE
	for ly in VoxelWorld.CHUNK_SIZE:
		for lz in VoxelWorld.CHUNK_SIZE:
			for lx in VoxelWorld.CHUNK_SIZE:
				var x := origin.x + lx
				var y := origin.y + ly
				var z := origin.z + lz
				var id := world.get_block(x, y, z)
				if not Blocks.is_solid(id):
					continue
				for face in 6:
					var n: Vector3i = NORMALS[face]
					if world.is_solid(x + n.x, y + n.y, z + n.z):
						continue
					_add_face(Vector3(x, y, z), face, Blocks.tile_for(id, face),
						vertices, normals, uvs, colors, indices)

	if vertices.is_empty():
		return []
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices
	return arrays


static func build_mesh(world: VoxelWorld, chunk: Vector3i) -> ArrayMesh:
	var arrays := build_arrays(world, chunk)
	if arrays.is_empty():
		return null
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


## The four corner offsets of a face, in drawing order.
static func face_corners(face: int) -> Array:
	return _face_corners[face]


static func _add_face(block: Vector3, face: int, tile: int,
		vertices: PackedVector3Array, normals: PackedVector3Array, uvs: PackedVector2Array,
		colors: PackedColorArray, indices: PackedInt32Array) -> void:
	var start := vertices.size()
	var n := Vector3(NORMALS[face])
	var shade := FACE_SHADE[face]
	var color := Color(shade, shade, shade)
	var tile_origin := Vector2(tile % Blocks.ATLAS_COLUMNS, tile / Blocks.ATLAS_COLUMNS)
	for corner: Vector3 in _face_corners[face]:
		vertices.append(block + corner)
		normals.append(n)
		colors.append(color)
		uvs.append((tile_origin + _corner_uv(corner, face)) / Blocks.ATLAS_COLUMNS)
	indices.append_array([start, start + 1, start + 2, start, start + 2, start + 3])


## Where a corner falls on the face's texture, from (0, 0) top-left to (1, 1).
static func _corner_uv(corner: Vector3, face: int) -> Vector2:
	var uv: Vector2
	match face:
		Blocks.Face.POS_X, Blocks.Face.NEG_X:
			uv = Vector2(corner.z, 1.0 - corner.y)
		Blocks.Face.POS_Z, Blocks.Face.NEG_Z:
			uv = Vector2(corner.x, 1.0 - corner.y)
		_:
			uv = Vector2(corner.x, corner.z)
	return uv.clamp(Vector2(UV_INSET, UV_INSET), Vector2(1.0 - UV_INSET, 1.0 - UV_INSET))


## Builds each face's corners and orders them so triangles (0, 1, 2) and
## (0, 2, 3) wind clockwise when seen from outside, which is what Godot
## treats as the front.
static func _build_face_corners() -> Array:
	var result := []
	for face in 6:
		var n := Vector3(NORMALS[face])
		var axis := 0 if n.x != 0 else (1 if n.y != 0 else 2)
		var u_axis := (axis + 1) % 3
		var v_axis := (axis + 2) % 3
		var plane := 1.0 if n[axis] > 0 else 0.0
		var corners := []
		for uv: Vector2 in [Vector2(0, 0), Vector2(1, 0), Vector2(1, 1), Vector2(0, 1)]:
			var c := Vector3()
			c[axis] = plane
			c[u_axis] = uv.x
			c[v_axis] = uv.y
			corners.append(c)
		var a: Vector3 = corners[0]
		var b: Vector3 = corners[1]
		var c2: Vector3 = corners[2]
		if (b - a).cross(c2 - a).dot(n) > 0.0:
			corners.reverse()
		result.append(corners)
	return result
