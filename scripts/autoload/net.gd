extends Node
## Networking: hosting, joining, sending messages, clock sync and the lag simulator.
##
## Every message is an Array whose first element is a Msg value. All messages
## travel through two RPCs (one reliable, one unreliable), which keeps the lag
## simulator in a single place. Everyone connects to one host; see docs/netcode.md.

signal joined_host
signal join_failed(reason: String)
signal connection_lost
signal peer_connected(id: int)
signal peer_disconnected(id: int)
signal message_received(from_id: int, msg: Array)

enum Msg {
	HELLO, ## client -> host: [HELLO, protocol_version, name]
	WELCOME, ## host -> client: [WELCOME, your_id, roster]
	REJECTED, ## host -> client: [REJECTED, reason]
	PLAYER_JOINED, ## host -> all: [PLAYER_JOINED, id, name, team]
	PLAYER_LEFT, ## host -> all: [PLAYER_LEFT, id]
	STATE, ## client -> host: [STATE, host_time, PlayerState]
	SNAPSHOT, ## host -> all: [SNAPSHOT, ids, times, flat PlayerStates]
	PING, ## client -> host: [PING, client_send_time]
	PONG, ## host -> client: [PONG, client_send_time, host_time]
}

const PROTOCOL_VERSION := 1
const HOST_ID := 1

var clock := NetClock.new()
## Delays this player's outgoing packets.
var outgoing_lag := LagSimulator.new()
## Delays this player's incoming packets.
var incoming_lag := LagSimulator.new()

var _start_usec := Time.get_ticks_usec()
var _ping_timer := 0.0


func _ready() -> void:
	multiplayer.peer_connected.connect(func(id: int) -> void: peer_connected.emit(id))
	multiplayer.peer_disconnected.connect(func(id: int) -> void: peer_disconnected.emit(id))
	multiplayer.connected_to_server.connect(func() -> void: joined_host.emit())
	multiplayer.connection_failed.connect(func() -> void:
		_reset()
		join_failed.emit("Couldn't reach the host"))
	multiplayer.server_disconnected.connect(func() -> void:
		_reset()
		connection_lost.emit())
	set_simulated_lag(LaunchArgs.ping_ms, LaunchArgs.jitter_ms, LaunchArgs.loss_percent)


## Local time in seconds since the game started.
func now() -> float:
	return (Time.get_ticks_usec() - _start_usec) / 1_000_000.0


## Time on the host's clock: the shared timeline every player stamps events with.
func host_time() -> float:
	return now() if is_host() else clock.host_time(now())


func is_online() -> bool:
	var peer := multiplayer.multiplayer_peer
	return peer != null and not peer is OfflineMultiplayerPeer \
		and peer.get_connection_status() == MultiplayerPeer.CONNECTION_CONNECTED


func is_host() -> bool:
	return is_online() and multiplayer.is_server()


func my_id() -> int:
	return multiplayer.get_unique_id()


## Round-trip time to the host in seconds, including simulated lag.
func ping() -> float:
	return 0.0 if is_host() else clock.ping


func is_lag_simulated() -> bool:
	return outgoing_lag.is_active()


## Adds fake ping, jitter (both in ms) and packet loss (percent) to this player's connection.
## Half the delay is applied on the way out and half on the way back.
func set_simulated_lag(ping_ms: float, jitter_ms: float, loss_percent: float) -> void:
	for sim: LagSimulator in [outgoing_lag, incoming_lag]:
		sim.latency = maxf(ping_ms, 0.0) / 2000.0
		sim.jitter = maxf(jitter_ms, 0.0) / 2000.0
		sim.loss = clampf(loss_percent / 200.0, 0.0, 1.0)


func host_game(port: int) -> Error:
	leave()
	var peer := ENetMultiplayerPeer.new()
	var err := peer.create_server(port, Tuning.MAX_PLAYERS - 1)
	if err != OK:
		return err
	multiplayer.multiplayer_peer = peer
	return OK


func join_game(address: String, port: int) -> Error:
	leave()
	var peer := ENetMultiplayerPeer.new()
	var err := peer.create_client(address, port)
	if err != OK:
		return err
	multiplayer.multiplayer_peer = peer
	return OK


func leave() -> void:
	var peer := multiplayer.multiplayer_peer
	if peer != null and not peer is OfflineMultiplayerPeer:
		peer.close()
	_reset()


## Disconnects a player shortly after sending them a reason.
func kick(id: int, reason: String) -> void:
	send(id, [Msg.REJECTED, reason], true)
	get_tree().create_timer(0.3).timeout.connect(func() -> void:
		if is_host() and multiplayer.get_peers().has(id):
			multiplayer.multiplayer_peer.disconnect_peer(id))


func send(to_id: int, msg: Array, reliable: bool) -> void:
	if not is_online():
		return
	var data := var_to_bytes(msg)
	if outgoing_lag.is_active():
		outgoing_lag.push([to_id, data, reliable], now(), reliable)
	else:
		_transmit(to_id, data, reliable)


func send_to_host(msg: Array, reliable: bool) -> void:
	send(HOST_ID, msg, reliable)


## Host only: sends a message to every connected player except `except_id`.
func broadcast(msg: Array, reliable: bool, except_id: int = 0) -> void:
	for id in multiplayer.get_peers():
		if id != except_id:
			send(id, msg, reliable)


func _process(delta: float) -> void:
	var t := now()
	for item: Array in outgoing_lag.pop_ready(t):
		_transmit(item[0], item[1], item[2])
	for item: Array in incoming_lag.pop_ready(t):
		_dispatch(item[0], item[1])
	if is_online() and not is_host():
		_ping_timer -= delta
		if _ping_timer <= 0.0:
			_ping_timer = Tuning.PING_INTERVAL
			send_to_host([Msg.PING, t], false)


func _transmit(to_id: int, data: PackedByteArray, reliable: bool) -> void:
	if not is_online():
		return
	# Skip players who are mid-disconnect; ENet errors if we send to them.
	var enet := multiplayer.multiplayer_peer as ENetMultiplayerPeer
	if enet:
		var target := enet.get_peer(to_id)
		if target == null or target.get_state() != ENetPacketPeer.STATE_CONNECTED:
			return
	if reliable:
		_receive_reliable.rpc_id(to_id, data)
	else:
		_receive_unreliable.rpc_id(to_id, data)


@rpc("any_peer", "call_remote", "reliable", 0)
func _receive_reliable(data: PackedByteArray) -> void:
	_on_packet(multiplayer.get_remote_sender_id(), data, true)


@rpc("any_peer", "call_remote", "unreliable_ordered", 1)
func _receive_unreliable(data: PackedByteArray) -> void:
	_on_packet(multiplayer.get_remote_sender_id(), data, false)


func _on_packet(from_id: int, data: PackedByteArray, reliable: bool) -> void:
	if incoming_lag.is_active():
		incoming_lag.push([from_id, data], now(), reliable)
	else:
		_dispatch(from_id, data)


func _dispatch(from_id: int, data: PackedByteArray) -> void:
	var msg: Variant = bytes_to_var(data)
	if not msg is Array or msg.is_empty() or not msg[0] is int:
		return
	match msg[0]:
		Msg.PING:
			if is_host() and msg.size() == 2:
				send(from_id, [Msg.PONG, msg[1], now()], false)
		Msg.PONG:
			if not is_host() and msg.size() == 3:
				clock.add_sample(msg[1], msg[2], now())
		_:
			message_received.emit(from_id, msg)


func _reset() -> void:
	multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	outgoing_lag.clear()
	incoming_lag.clear()
	clock.reset()
	_ping_timer = 0.0
