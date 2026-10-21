extends Node
## Who is in the match (names and teams), and moving between the menu and the
## match. Lives across scene changes, so the roster survives loading the arena.

signal player_added(id: int)
signal player_removed(id: int)

const MENU_SCENE := "res://scenes/main_menu.tscn"
const GAME_SCENE := "res://scenes/game.tscn"

var my_name := "Player"
## id -> {"name": String, "team": int}
var players: Dictionary = {}
## Shown on the main menu, e.g. why the last match ended.
var status := ""
var in_match := false

var _join_attempt := {}


func _ready() -> void:
	Net.joined_host.connect(_on_joined_host)
	Net.join_failed.connect(_on_join_failed)
	Net.connection_lost.connect(func() -> void: leave("Lost connection to the host"))
	Net.peer_disconnected.connect(_on_peer_disconnected)
	Net.message_received.connect(_on_message)


func host(player_name: String, port: int) -> Error:
	my_name = _clean_name(player_name)
	var err := Net.host_game(port)
	if err != OK:
		status = "Couldn't host on port %d (is it already in use?)" % port
		return err
	players = {Net.HOST_ID: {"name": my_name, "team": Arena.Team.RED}}
	print("[session] hosting on port %d as %s" % [port, my_name])
	_enter_match()
	return OK


func join(player_name: String, address: String, port: int, retries: int = 0) -> Error:
	my_name = _clean_name(player_name)
	_join_attempt = {"address": address, "port": port, "retries": retries}
	status = "Connecting to %s:%d..." % [address, port]
	return Net.join_game(address, port)


func leave(reason: String = "") -> void:
	Net.leave()
	players.clear()
	_join_attempt.clear()
	status = reason
	if in_match:
		in_match = false
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		get_tree().change_scene_to_file(MENU_SCENE)


func team_of(id: int) -> int:
	return players.get(id, {}).get("team", Arena.Team.RED)


func name_of(id: int) -> String:
	return players.get(id, {}).get("name", "?")


## A player's position within their team, used to pick a spawn spot.
func slot_of(id: int) -> int:
	var team := team_of(id)
	var ids := players.keys().filter(func(other: int) -> bool: return team_of(other) == team)
	ids.sort()
	return maxi(ids.find(id), 0)


func team_size(team: int) -> int:
	return players.keys().filter(func(id: int) -> bool: return team_of(id) == team).size()


func _enter_match() -> void:
	in_match = true
	status = ""
	get_tree().change_scene_to_file(GAME_SCENE)


func _on_joined_host() -> void:
	Net.send_to_host([Net.Msg.HELLO, Net.PROTOCOL_VERSION, my_name], true)


func _on_join_failed(reason: String) -> void:
	if _join_attempt.get("retries", 0) > 0:
		_join_attempt["retries"] -= 1
		get_tree().create_timer(0.5).timeout.connect(func() -> void:
			if not _join_attempt.is_empty():
				Net.join_game(_join_attempt["address"], _join_attempt["port"]))
		return
	_join_attempt.clear()
	leave(reason)


func _on_peer_disconnected(id: int) -> void:
	if not Net.is_host() or not players.has(id):
		return
	print("[session] %s left" % name_of(id))
	players.erase(id)
	Net.broadcast([Net.Msg.PLAYER_LEFT, id], true)
	player_removed.emit(id)


func _on_message(from_id: int, msg: Array) -> void:
	if Net.is_host():
		if msg[0] == Net.Msg.HELLO:
			_host_handle_hello(from_id, msg)
		return
	match msg[0]:
		Net.Msg.WELCOME:
			_client_handle_welcome(msg)
		Net.Msg.REJECTED:
			if msg.size() == 2:
				leave(str(msg[1]))
		Net.Msg.PLAYER_JOINED:
			if msg.size() == 4 and not players.has(msg[1]):
				players[msg[1]] = {"name": str(msg[2]), "team": int(msg[3])}
				player_added.emit(msg[1])
		Net.Msg.PLAYER_LEFT:
			if msg.size() == 2 and players.has(msg[1]):
				players.erase(msg[1])
				player_removed.emit(msg[1])


func _host_handle_hello(from_id: int, msg: Array) -> void:
	if players.has(from_id) or msg.size() != 3:
		return
	if msg[1] != Net.PROTOCOL_VERSION:
		Net.kick(from_id, "Version mismatch: update your game")
		return
	if players.size() >= Tuning.MAX_PLAYERS:
		Net.kick(from_id, "Match is full")
		return
	var team := Arena.Team.RED if team_size(Arena.Team.RED) <= team_size(Arena.Team.BLUE) else Arena.Team.BLUE
	var player_name := _clean_name(str(msg[2]))
	players[from_id] = {"name": player_name, "team": team}
	print("[session] %s joined team %s" % [player_name, Arena.team_name(team)])
	var roster := []
	for id: int in players:
		roster.append([id, players[id]["name"], players[id]["team"]])
	Net.send(from_id, [Net.Msg.WELCOME, from_id, roster], true)
	Net.broadcast([Net.Msg.PLAYER_JOINED, from_id, player_name, team], true, from_id)
	player_added.emit(from_id)


func _client_handle_welcome(msg: Array) -> void:
	if msg.size() != 3 or not msg[2] is Array:
		return
	players.clear()
	for entry: Variant in msg[2]:
		if entry is Array and entry.size() == 3:
			players[int(entry[0])] = {"name": str(entry[1]), "team": int(entry[2])}
	_join_attempt.clear()
	print("[session] joined as %s on team %s" % [my_name, Arena.team_name(team_of(Net.my_id()))])
	_enter_match()


static func _clean_name(raw: String) -> String:
	var cleaned := raw.strip_edges().left(16)
	return cleaned if not cleaned.is_empty() else "Player"
