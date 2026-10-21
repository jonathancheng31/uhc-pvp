class_name Player
extends Node3D
## One player in the match. The local player has a first-person camera and
## sends its state to the host; other players are drawn from their recent
## states, slightly in the past (see SnapshotBuffer).
##
## Movement comes in the next milestone. For now the local player stands at
## their spawn and can look around; with --bot they walk in a circle so
## multiplayer can be tested.

var peer_id := 0
var player_name := ""
var team := 0
var is_local := false

## Degrees. Yaw 0 faces -Z; positive turns left. Positive pitch looks up.
var yaw := 0.0
var pitch := 0.0
var flags := 0

var camera: Camera3D
var model: PlayerModel
var buffer := SnapshotBuffer.new()

var _spawn := Vector3.ZERO
var _bot_time := 0.0
var _mouse_degrees := Tuning.degrees_per_mouse_count(Tuning.DEFAULT_MOUSE_SENSITIVITY)


func setup(id: int, local: bool) -> void:
	peer_id = id
	is_local = local
	player_name = Session.name_of(id)
	team = Session.team_of(id)
	name = "Player_%d" % id
	_spawn = Arena.spawn_position(team, Session.slot_of(id))
	position = _spawn
	yaw = Arena.spawn_yaw(team)

	model = PlayerModel.new()
	model.name = "Model"
	model.setup(player_name, Arena.team_color(team))
	add_child(model)

	if is_local:
		model.visible = false # first person: don't draw our own body
		camera = Camera3D.new()
		camera.name = "Camera"
		camera.fov = Tuning.FOV
		camera.near = 0.05
		camera.position.y = Tuning.EYE_HEIGHT
		add_child(camera)
		camera.make_current()
	_apply_rotation()


func _unhandled_input(event: InputEvent) -> void:
	if not is_local or Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
		return
	if event is InputEventMouseMotion:
		var motion: Vector2 = event.screen_relative
		yaw = wrapf(yaw - motion.x * _mouse_degrees, -180.0, 180.0)
		pitch = clampf(pitch - motion.y * _mouse_degrees, -90.0, 90.0)
		_apply_rotation()


func _physics_process(delta: float) -> void:
	if is_local and LaunchArgs.bot:
		_bot_time += delta
		position = _spawn + Vector3(cos(_bot_time * 1.5), 0.0, sin(_bot_time * 1.5)) * 2.0
		yaw = wrapf(yaw + 90.0 * delta, -180.0, 180.0)
		_apply_rotation()


func get_state() -> PackedFloat32Array:
	return PlayerState.make(position, yaw, pitch, flags)


## A state for this (remote) player arrived. `host_now` is when it arrived.
func receive_state(time: float, state: PackedFloat32Array, host_now: float) -> void:
	if buffer.push(time, state):
		buffer.note_arrival_age(host_now - time)


## Moves this (remote) player to where they were `buffer.render_delay` ago.
func update_remote(host_now: float) -> void:
	if buffer.is_empty():
		return
	var state := buffer.sample(host_now - buffer.render_delay)
	position = PlayerState.position_of(state)
	yaw = state[PlayerState.YAW]
	pitch = state[PlayerState.PITCH]
	flags = PlayerState.flags_of(state)
	_apply_rotation()


func _apply_rotation() -> void:
	if is_local:
		rotation_degrees.y = yaw
		if camera:
			camera.rotation_degrees.x = pitch
	else:
		model.rotation_degrees.y = yaw
		model.set_head_pitch(pitch)
