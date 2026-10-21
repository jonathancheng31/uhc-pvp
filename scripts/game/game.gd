extends Node3D
## The match: builds the arena, spawns players and keeps everyone's positions
## in sync.
##
## Networking model (docs/netcode.md): each player's game owns its own
## movement and sends its state to the host 60 times a second. The host
## sends everyone a snapshot of all players 60 times a second, and each game
## draws the other players slightly in the past so they move smoothly.

var world: VoxelWorld
var renderer: WorldRenderer
var local_player: Player
var remote_players: Dictionary = {} # peer id -> Player
var hud: Hud

## Host only: latest state of every player, peer id -> [time, PlayerState].
var _latest_states: Dictionary = {}
## Test stats: peer id -> [min corner, max corner] of where we've seen them.
## Kept after a player leaves, so the end-of-test report covers the whole match.
var _seen_bounds: Dictionary = {}


func _ready() -> void:
	world = Arena.build()
	renderer = WorldRenderer.new()
	renderer.name = "World"
	add_child(renderer)
	renderer.build(world)
	_setup_environment()

	local_player = _spawn_player(Net.my_id(), true)
	for id: int in Session.players:
		if id != Net.my_id():
			_add_remote(id)

	hud = Hud.new()
	hud.name = "Hud"
	add_child(hud)
	hud.game = self

	Session.player_added.connect(_add_remote)
	Session.player_removed.connect(_remove_remote)
	Net.message_received.connect(_on_message)
	LaunchArgs.quitting.connect(_print_test_report)
	if not LaunchArgs.screenshot_path.is_empty():
		get_tree().create_timer(LaunchArgs.screenshot_at).timeout.connect(_take_screenshot)
	print("[game] match started as %s (id %d, team %s)" % [
		local_player.player_name, Net.my_id(), Arena.team_name(local_player.team)])


func _physics_process(_delta: float) -> void:
	if not Net.is_online():
		return
	var now := Net.host_time()
	var state := local_player.get_state()
	if Net.is_host():
		_latest_states[Net.my_id()] = [now, state]
		_broadcast_snapshot()
	else:
		Net.send_to_host([Net.Msg.STATE, now, state], false)


func _process(_delta: float) -> void:
	var now := Net.host_time()
	for player: Player in remote_players.values():
		player.update_remote(now)
		_track_seen(player)


func player_count() -> int:
	return remote_players.size() + 1


func _spawn_player(id: int, local: bool) -> Player:
	var player := Player.new()
	player.setup(id, local)
	add_child(player)
	return player


func _add_remote(id: int) -> void:
	if id == Net.my_id() or remote_players.has(id):
		return
	remote_players[id] = _spawn_player(id, false)


func _remove_remote(id: int) -> void:
	var player: Player = remote_players.get(id)
	if player:
		player.queue_free()
		remote_players.erase(id)
	_latest_states.erase(id)


func _on_message(from_id: int, msg: Array) -> void:
	match msg[0]:
		Net.Msg.STATE:
			if Net.is_host():
				_host_receive_state(from_id, msg)
		Net.Msg.SNAPSHOT:
			if not Net.is_host():
				_receive_snapshot(msg)


func _host_receive_state(from_id: int, msg: Array) -> void:
	if msg.size() != 3 or not msg[1] is float or not msg[2] is PackedFloat32Array:
		return
	var player: Player = remote_players.get(from_id)
	var state: PackedFloat32Array = msg[2]
	if player == null or not PlayerState.is_valid(state):
		return
	var time: float = msg[1]
	var now := Net.host_time()
	if time > now + 1.0: # clock still settling or a bad timestamp
		time = now
	_latest_states[from_id] = [time, state]
	player.receive_state(time, state, now)


func _broadcast_snapshot() -> void:
	var ids := PackedInt32Array()
	var times := PackedFloat64Array()
	var flat := PackedFloat32Array()
	for id: int in _latest_states:
		ids.append(id)
		times.append(_latest_states[id][0])
		flat.append_array(_latest_states[id][1])
	Net.broadcast([Net.Msg.SNAPSHOT, ids, times, flat], false)


func _receive_snapshot(msg: Array) -> void:
	if msg.size() != 4 or not msg[1] is PackedInt32Array or not msg[2] is PackedFloat64Array \
			or not msg[3] is PackedFloat32Array:
		return
	var ids: PackedInt32Array = msg[1]
	var times: PackedFloat64Array = msg[2]
	var flat: PackedFloat32Array = msg[3]
	var n := PlayerState.SIZE
	if times.size() != ids.size() or flat.size() != ids.size() * n:
		return
	var now := Net.host_time()
	for i in ids.size():
		var player: Player = remote_players.get(ids[i])
		if player == null:
			continue
		var state := flat.slice(i * n, (i + 1) * n)
		if PlayerState.is_valid(state):
			player.receive_state(times[i], state, now)


func _setup_environment() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.53, 0.72, 1.0)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(1, 1, 1)
	env.ambient_light_energy = 0.55
	var world_env := WorldEnvironment.new()
	world_env.environment = env
	add_child(world_env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-55, -30, 0)
	sun.light_energy = 0.75
	add_child(sun)


func _track_seen(player: Player) -> void:
	var bounds: Array = _seen_bounds.get(player.peer_id, [player.position, player.position])
	bounds[0] = bounds[0].min(player.position)
	bounds[1] = bounds[1].max(player.position)
	_seen_bounds[player.peer_id] = bounds


## Printed when --quit-after ends the game; tools/smoke_test.sh checks this line.
func _print_test_report() -> void:
	var moving := 0
	for id: int in _seen_bounds:
		var bounds: Array = _seen_bounds[id]
		if (bounds[1] - bounds[0]).length() > 1.0:
			moving += 1
	print("SMOKE name=%s host=%s players_now=%d seen=%d moving=%d ping_ms=%d" % [
		local_player.player_name, Net.is_host(), player_count(), _seen_bounds.size(),
		moving, roundi(Net.ping() * 1000.0)])


func _take_screenshot() -> void:
	await RenderingServer.frame_post_draw
	var image := get_viewport().get_texture().get_image()
	var err := image.save_png(LaunchArgs.screenshot_path)
	print("[game] screenshot saved to %s (%s)" % [LaunchArgs.screenshot_path, error_string(err)])
