class_name NetClock
extends RefCounted
## Estimates the host's clock from ping/pong round trips, so every player can
## stamp events on the same timeline. That shared timeline is what the lag cap
## and the smoothing of other players rely on.

const WINDOW := 20

## host_time - local_time, in seconds.
var offset := 0.0
## Smoothed round-trip time in seconds, for display.
var ping := 0.0
var has_sample := false

var _samples: Array = [] # Entries: [rtt: float, offset: float]


func add_sample(sent_local: float, host_time_at_reply: float, received_local: float) -> void:
	var rtt := maxf(received_local - sent_local, 0.0)
	var sample_offset := host_time_at_reply + rtt * 0.5 - received_local
	_samples.append([rtt, sample_offset])
	if _samples.size() > WINDOW:
		_samples.pop_front()
	# The fastest recent round trip had the least queuing delay, so its
	# offset is the most trustworthy.
	var best: Array = _samples[0]
	for sample: Array in _samples:
		if sample[0] < best[0]:
			best = sample
	offset = best[1]
	ping = rtt if not has_sample else lerpf(ping, rtt, 0.2)
	has_sample = true


func host_time(local_now: float) -> float:
	return local_now + offset


func reset() -> void:
	offset = 0.0
	ping = 0.0
	has_sample = false
	_samples.clear()
