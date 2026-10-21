extends TestCase


func test_default_sensitivity_matches_minecraft() -> void:
	# Minecraft at "100%" turns 0.15 degrees per mouse count.
	check_near(Tuning.degrees_per_mouse_count(0.5), 0.15, 0.00001, "100% sensitivity")


func test_player_state_round_trip() -> void:
	var state := PlayerState.make(Vector3(1, 2, 3), 45.0, -10.0, PlayerState.FLAG_SPRINTING)
	check(PlayerState.is_valid(state), "valid")
	check_eq(PlayerState.position_of(state), Vector3(1, 2, 3), "position")
	check_eq(PlayerState.flags_of(state), PlayerState.FLAG_SPRINTING, "flags")
	state[0] = NAN
	check(not PlayerState.is_valid(state), "NaN rejected")
