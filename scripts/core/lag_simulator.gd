class_name LagSimulator
extends RefCounted
## Delays and drops packets to imitate a bad connection while testing.
##
## Reliable packets are never dropped and always come out in the order they
## went in, like ENet's reliable channel. Unreliable packets can be dropped
## and, with jitter, can arrive out of order.

## One-way delay in seconds.
var latency := 0.0
## Random extra delay added to each packet, from 0 up to this many seconds.
var jitter := 0.0
## Chance from 0 to 1 that an unreliable packet is lost.
var loss := 0.0

var _rng := RandomNumberGenerator.new()
var _queue: Array = [] # Entries: [deliver_at: float, order: int, packet: Variant]
var _order := 0
var _last_reliable_at := 0.0


func _init(rng_seed: int = 0) -> void:
	if rng_seed != 0:
		_rng.seed = rng_seed
	else:
		_rng.randomize()


func is_active() -> bool:
	return latency > 0.0 or jitter > 0.0 or loss > 0.0


func pending() -> int:
	return _queue.size()


func clear() -> void:
	_queue.clear()
	_last_reliable_at = 0.0


## Queues a packet sent at time `now`. Returns false if the packet was lost.
func push(packet: Variant, now: float, reliable: bool) -> bool:
	if not reliable and loss > 0.0 and _rng.randf() < loss:
		return false
	var deliver_at := now + latency
	if jitter > 0.0:
		deliver_at += _rng.randf() * jitter
	if reliable:
		deliver_at = maxf(deliver_at, _last_reliable_at)
		_last_reliable_at = deliver_at
	_queue.append([deliver_at, _order, packet])
	_order += 1
	return true


## Removes and returns every packet due by time `now`, earliest first.
func pop_ready(now: float) -> Array:
	var ready: Array = []
	var waiting: Array = []
	for entry: Array in _queue:
		if entry[0] <= now:
			ready.append(entry)
		else:
			waiting.append(entry)
	if ready.is_empty():
		return []
	_queue = waiting
	ready.sort_custom(func(a: Array, b: Array) -> bool:
		return a[0] < b[0] or (a[0] == b[0] and a[1] < b[1]))
	return ready.map(func(entry: Array) -> Variant: return entry[2])
