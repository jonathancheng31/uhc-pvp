class_name SnapshotBuffer
extends RefCounted
## Keeps recent timestamped states of one other player and blends between
## them, so they move smoothly even though updates arrive unevenly.
##
## It also decides how far in the past to show that player: just enough to
## cover how late their updates usually arrive, and no more. Showing them
## any older is what causes "ghosting".

const MAX_STATES := 120
## How strongly the delay estimate follows new measurements (0..1).
const AGE_SMOOTHING := 0.1
## How quickly the render delay moves toward its target (0..1 per update).
const DELAY_SMOOTHING := 0.05

## How far behind the host clock this player is drawn, in seconds.
var render_delay := Tuning.INTERP_MIN_DELAY

var _times := PackedFloat64Array()
var _states: Array[PackedFloat32Array] = []
var _age_avg := 0.0
var _age_dev := 0.0
var _has_age := false


func is_empty() -> bool:
	return _times.is_empty()


func size() -> int:
	return _times.size()


func latest_time() -> float:
	return _times[_times.size() - 1] if not _times.is_empty() else 0.0


func latest_state() -> PackedFloat32Array:
	return _states[_states.size() - 1] if not _states.is_empty() else PackedFloat32Array()


## Adds a state stamped with `time` (host clock). Old or repeated states are ignored.
## Returns true if the state was stored.
func push(time: float, state: PackedFloat32Array) -> bool:
	if not _times.is_empty() and time <= latest_time():
		return false
	_times.append(time)
	_states.append(state)
	if _times.size() > MAX_STATES:
		_times.remove_at(0)
		_states.remove_at(0)
	return true


## Records how old a state already was when it arrived, and updates the render delay.
func note_arrival_age(age: float) -> void:
	if not _has_age:
		_age_avg = age
		_age_dev = 0.0
		_has_age = true
		render_delay = target_delay()
		return
	_age_dev = lerpf(_age_dev, absf(age - _age_avg), AGE_SMOOTHING)
	_age_avg = lerpf(_age_avg, age, AGE_SMOOTHING)
	render_delay = lerpf(render_delay, target_delay(), DELAY_SMOOTHING)


## Usual arrival age, plus a margin for jitter, plus one send interval.
func target_delay() -> float:
	var target := _age_avg + 2.0 * _age_dev + 1.0 / Tuning.STATE_SEND_RATE
	return maxf(target, Tuning.INTERP_MIN_DELAY)


## The blended state at `time` on the host clock. Holds the first or last
## state when `time` is outside the stored range.
func sample(time: float) -> PackedFloat32Array:
	var n := _times.size()
	if n == 0:
		return PackedFloat32Array()
	if time <= _times[0]:
		return _states[0]
	if time >= _times[n - 1]:
		return _states[n - 1]
	# Binary search for the pair of states around `time`.
	var lo := 0
	var hi := n - 1
	while hi - lo > 1:
		var mid := (lo + hi) / 2
		if _times[mid] <= time:
			lo = mid
		else:
			hi = mid
	var t := (time - _times[lo]) / (_times[hi] - _times[lo])
	return blend(_states[lo], _states[hi], t)


static func blend(a: PackedFloat32Array, b: PackedFloat32Array, t: float) -> PackedFloat32Array:
	var out := a.duplicate() if t < 0.5 else b.duplicate()
	out[PlayerState.X] = lerpf(a[PlayerState.X], b[PlayerState.X], t)
	out[PlayerState.Y] = lerpf(a[PlayerState.Y], b[PlayerState.Y], t)
	out[PlayerState.Z] = lerpf(a[PlayerState.Z], b[PlayerState.Z], t)
	out[PlayerState.YAW] = a[PlayerState.YAW] + wrapf(b[PlayerState.YAW] - a[PlayerState.YAW], -180.0, 180.0) * t
	out[PlayerState.PITCH] = lerpf(a[PlayerState.PITCH], b[PlayerState.PITCH], t)
	return out
