class_name PlayerModel
extends Node3D
## A blocky placeholder body: head, torso, arms and legs, with the shirt in
## the team's color and a name tag overhead. Proportions follow a 32-pixel-tall
## character scaled to the 1.8-block player height.

const PIXEL := Tuning.PLAYER_HEIGHT / 32.0
const SKIN := Color(0.87, 0.68, 0.53)
const PANTS := Color(0.2, 0.22, 0.32)

var head: Node3D
var _name_tag: Label3D


func setup(player_name: String, team_color: Color) -> void:
	var shirt := team_color
	# Legs: 4x12x4 pixels each, from the feet up to 12 px.
	_add_box(Vector3(4, 12, 4), Vector3(-2, 6, 0), PANTS)
	_add_box(Vector3(4, 12, 4), Vector3(2, 6, 0), PANTS)
	# Torso: 8x12x4, from 12 px to 24 px.
	_add_box(Vector3(8, 12, 4), Vector3(0, 18, 0), shirt)
	# Arms: 4x12x4 beside the torso.
	_add_box(Vector3(4, 12, 4), Vector3(-6, 18, 0), shirt.darkened(0.15))
	_add_box(Vector3(4, 12, 4), Vector3(6, 18, 0), shirt.darkened(0.15))
	# Head: 8x8x8 on a pivot at the neck so it can look up and down.
	head = Node3D.new()
	head.name = "Head"
	head.position = Vector3(0, 24, 0) * PIXEL
	add_child(head)
	var face := _add_box(Vector3(8, 8, 8), Vector3(0, 4, 0), SKIN, head)
	# Eyes, so you can tell which way someone is facing.
	for x: float in [-2.0, 2.0]:
		_add_box(Vector3(1.5, 1, 0.2), Vector3(x, 4.5, -4.05), Color(0.1, 0.1, 0.15), face)

	_name_tag = Label3D.new()
	_name_tag.text = player_name
	_name_tag.modulate = team_color.lightened(0.35)
	_name_tag.outline_modulate = Color(0, 0, 0, 0.8)
	_name_tag.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	_name_tag.no_depth_test = true
	_name_tag.pixel_size = 0.01
	_name_tag.font_size = 32
	_name_tag.position = Vector3(0, Tuning.PLAYER_HEIGHT + 0.35, 0)
	add_child(_name_tag)


## Pitch in degrees; positive looks up.
func set_head_pitch(pitch: float) -> void:
	if head:
		head.rotation_degrees.x = pitch


func _add_box(size_px: Vector3, center_px: Vector3, color: Color, parent: Node3D = self) -> MeshInstance3D:
	var mesh := BoxMesh.new()
	mesh.size = size_px * PIXEL
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	mesh.material = material
	var instance := MeshInstance3D.new()
	instance.mesh = mesh
	instance.position = center_px * PIXEL
	parent.add_child(instance)
	return instance
