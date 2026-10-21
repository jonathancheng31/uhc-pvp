extends TestCase


func _state(x: float, yaw: float) -> PackedFloat32Array:
	return PlayerState.make(Vector3(x, 5.0, 0.0), yaw, 0.0, 0)


func test_blends_between_states() -> void:
	var buffer := SnapshotBuffer.new()
	buffer.push(1.0, _state(0.0, 0.0))
	buffer.push(2.0, _state(10.0, 90.0))
	var mid := buffer.sample(1.5)
	check_near(mid[PlayerState.X], 5.0, 0.0001, "x halfway")
	check_near(mid[PlayerState.YAW], 45.0, 0.0001, "yaw halfway")


func test_yaw_blends_the_short_way_round() -> void:
	var buffer := SnapshotBuffer.new()
	buffer.push(1.0, _state(0.0, 170.0))
	buffer.push(2.0, _state(0.0, -170.0))
	var mid := buffer.sample(1.5)
	check_near(mid[PlayerState.YAW], 180.0, 0.001, "crosses 180, not 0")


func test_holds_ends_outside_range() -> void:
	var buffer := SnapshotBuffer.new()
	buffer.push(1.0, _state(1.0, 0.0))
	buffer.push(2.0, _state(2.0, 0.0))
	check_near(buffer.sample(0.0)[PlayerState.X], 1.0, 0.0001, "before first")
	check_near(buffer.sample(9.0)[PlayerState.X], 2.0, 0.0001, "after last")


func test_ignores_old_and_repeated_states() -> void:
	var buffer := SnapshotBuffer.new()
	check(buffer.push(2.0, _state(2.0, 0.0)), "first state stored")
	check(not buffer.push(2.0, _state(9.0, 0.0)), "repeat ignored")
	check(not buffer.push(1.0, _state(9.0, 0.0)), "older ignored")
	check_eq(buffer.size(), 1, "one state kept")


func test_render_delay_follows_arrival_age() -> void:
	var buffer := SnapshotBuffer.new()
	for i in 400:
		buffer.note_arrival_age(0.06)
	# Steady 60 ms arrivals: delay settles at 60 ms plus one send interval.
	check_near(buffer.render_delay, 0.06 + 1.0 / Tuning.STATE_SEND_RATE, 0.002, "settled delay")


func test_render_delay_never_below_minimum() -> void:
	var buffer := SnapshotBuffer.new()
	buffer.note_arrival_age(-1.0)
	check(buffer.render_delay >= Tuning.INTERP_MIN_DELAY, "minimum delay")
