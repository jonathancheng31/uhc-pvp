extends Node
## Runs every tests/test_*.gd file, prints the results and exits with code 1
## if anything failed. Run it with tools/run_tests.sh.

const TEST_DIR := "res://tests"


func _ready() -> void:
	var files := Array(DirAccess.get_files_at(TEST_DIR)).filter(
		func(f: String) -> bool: return f.begins_with("test_") and f.ends_with(".gd") \
			and f != "test_case.gd" and f != "test_runner.gd")
	files.sort()
	var passed := 0
	var failed := 0
	for file: String in files:
		var script: GDScript = load("%s/%s" % [TEST_DIR, file])
		if script == null or not script.can_instantiate():
			print("FAIL %s: could not load" % file)
			failed += 1
			continue
		for method: Dictionary in script.get_script_method_list():
			var method_name: String = method["name"]
			if not method_name.begins_with("test_"):
				continue
			var suite: TestCase = script.new()
			suite.call(method_name)
			if suite.failures.is_empty():
				passed += 1
			else:
				failed += 1
				for failure in suite.failures:
					print("FAIL %s::%s: %s" % [file.get_basename(), method_name, failure])
	print("%d passed, %d failed" % [passed, failed])
	get_tree().quit(1 if failed > 0 or passed == 0 else 0)
