extends TestCase


func test_delays_packets_by_latency() -> void:
	var sim := LagSimulator.new(42)
	sim.latency = 0.05
	sim.push("a", 1.0, false)
	check_eq(sim.pop_ready(1.049), [], "not due yet")
	check_eq(sim.pop_ready(1.05), ["a"], "due at send time + latency")
	check_eq(sim.pending(), 0, "queue emptied")


func test_reliable_packets_keep_their_order_under_jitter() -> void:
	var sim := LagSimulator.new(7)
	sim.latency = 0.05
	sim.jitter = 0.2
	for i in 50:
		sim.push(i, 1.0 + i * 0.001, true)
	var out := sim.pop_ready(10.0)
	var expected := []
	for i in 50:
		expected.append(i)
	check_eq(out, expected, "reliable order")


func test_reliable_packets_are_never_lost() -> void:
	var sim := LagSimulator.new(3)
	sim.loss = 1.0
	for i in 20:
		check(sim.push(i, 0.0, true), "reliable push accepted")
	check_eq(sim.pop_ready(1.0).size(), 20, "all reliable packets delivered")


func test_unreliable_loss_rate_is_roughly_right() -> void:
	var sim := LagSimulator.new(99)
	sim.loss = 0.25
	var lost := 0
	for i in 4000:
		if not sim.push(i, 0.0, false):
			lost += 1
	check_near(lost / 4000.0, 0.25, 0.03, "loss rate")


func test_inactive_by_default() -> void:
	check(not LagSimulator.new().is_active(), "no lag unless configured")
