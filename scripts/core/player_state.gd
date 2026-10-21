class_name PlayerState
extends RefCounted
## Layout of the compact state each player sends many times per second.
## A state is a PackedFloat32Array: [x, y, z, yaw, pitch, flags].
## Yaw and pitch are in degrees. Yaw 0 faces -Z; positive yaw turns left.

const X := 0
const Y := 1
const Z := 2
const YAW := 3
const PITCH := 4
const FLAGS := 5
const SIZE := 6

const FLAG_ON_GROUND := 1
const FLAG_SPRINTING := 2
const FLAG_SNEAKING := 4


static func make(position: Vector3, yaw: float, pitch: float, flags: int) -> PackedFloat32Array:
	return PackedFloat32Array([position.x, position.y, position.z, yaw, pitch, float(flags)])


static func position_of(state: PackedFloat32Array) -> Vector3:
	return Vector3(state[X], state[Y], state[Z])


static func flags_of(state: PackedFloat32Array) -> int:
	return int(state[FLAGS])


static func is_valid(state: PackedFloat32Array) -> bool:
	if state.size() != SIZE:
		return false
	for value in state:
		if is_nan(value) or is_inf(value):
			return false
	return true
