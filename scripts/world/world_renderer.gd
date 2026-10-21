class_name WorldRenderer
extends Node3D
## Draws a VoxelWorld as one mesh per chunk.

var world: VoxelWorld
var _material: StandardMaterial3D
var _chunks: Dictionary = {} # Vector3i -> MeshInstance3D


func build(target: VoxelWorld) -> void:
	world = target
	_material = _make_material()
	var counts := world.chunk_counts()
	for cy in counts.y:
		for cz in counts.z:
			for cx in counts.x:
				rebuild_chunk(Vector3i(cx, cy, cz))


## Redraws one chunk, e.g. after a block in it changed.
func rebuild_chunk(chunk: Vector3i) -> void:
	var mesh := ChunkMesher.build_mesh(world, chunk)
	var instance: MeshInstance3D = _chunks.get(chunk)
	if mesh == null:
		if instance:
			instance.queue_free()
			_chunks.erase(chunk)
		return
	if instance == null:
		instance = MeshInstance3D.new()
		instance.name = "Chunk_%d_%d_%d" % [chunk.x, chunk.y, chunk.z]
		instance.material_override = _material
		add_child(instance)
		_chunks[chunk] = instance
	instance.mesh = mesh


func chunk_instance_count() -> int:
	return _chunks.size()


static func _make_material() -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.vertex_color_use_as_albedo = true
	material.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
	material.albedo_texture = load(Blocks.ATLAS_PATH)
	return material
