extends TestCase
## Lint: every script in the project must compile. Runs inside the full
## project, so autoloads like Net and Session resolve.

const ROOTS := ["res://scripts", "res://tests"]


func test_every_script_compiles() -> void:
	var count := 0
	for root: String in ROOTS:
		for path in _find_scripts(root):
			var script := ResourceLoader.load(path, "GDScript", ResourceLoader.CACHE_MODE_REUSE) as GDScript
			check(script != null and script.can_instantiate(), "%s compiles" % path)
			count += 1
	check(count > 10, "found the project's scripts (found %d)" % count)


func _find_scripts(dir_path: String) -> PackedStringArray:
	var found := PackedStringArray()
	for file in DirAccess.get_files_at(dir_path):
		if file.ends_with(".gd"):
			found.append(dir_path.path_join(file))
	for sub in DirAccess.get_directories_at(dir_path):
		found.append_array(_find_scripts(dir_path.path_join(sub)))
	return found
