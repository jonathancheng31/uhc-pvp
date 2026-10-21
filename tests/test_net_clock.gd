extends TestCase


func test_estimates_offset_from_symmetric_round_trip() -> void:
	var clock := NetClock.new()
	# Host clock is 100 s ahead; 40 ms each way.
	clock.add_sample(5.0, 105.04, 5.08)
	check_near(clock.offset, 100.0, 0.0001, "offset")
	check_near(clock.ping, 0.08, 0.0001, "ping")
	check_near(clock.host_time(6.0), 106.0, 0.0001, "host time")


func test_prefers_fastest_round_trip() -> void:
	var clock := NetClock.new()
	clock.add_sample(1.0, 101.3, 1.4) # slow, skewed sample
	clock.add_sample(2.0, 102.02, 2.04) # fast, accurate sample
	clock.add_sample(3.0, 103.25, 3.3) # slow again
	check_near(clock.offset, 100.0, 0.0001, "offset comes from the fastest sample")
