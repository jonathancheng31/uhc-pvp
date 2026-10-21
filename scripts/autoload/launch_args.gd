extends Node
## Command-line options, mainly for automated tests and quick local testing.
## Pass them after "--", for example:
##   godot --path . -- --host --name=Alice --bot
##   godot --path . -- --join=127.0.0.1 --ping=120 --jitter=20 --loss=2
##
## --host               Host a match right away.
## --join=ADDRESS       Join a match right away.
## --port=N             Port to host on or join (default Tuning.DEFAULT_PORT).
## --name=NAME          Player name.
## --bot                Walk in a circle by itself (for multiplayer tests).
## --ping=MS            Simulate this much extra ping.
## --jitter=MS          Simulate this much random extra delay.
## --loss=PERCENT       Simulate this percentage of lost packets.
## --quit-after=SECONDS Quit after this many seconds.
## --screenshot=PATH    Save a screenshot of the match (needs a real display).
## --screenshot-at=SEC  Seconds into the match to take the screenshot (default 2).

## Emitted just before --quit-after closes the game, so tests can print results.
signal quitting

var host := false
var join_address := ""
var port := Tuning.DEFAULT_PORT
var player_name := ""
var bot := false
var ping_ms := 0.0
var jitter_ms := 0.0
var loss_percent := 0.0
var quit_after := 0.0
var screenshot_path := ""
var screenshot_at := 2.0


func _ready() -> void:
	parse(OS.get_cmdline_user_args())
	if quit_after > 0.0:
		get_tree().create_timer(quit_after).timeout.connect(_quit)


func parse(args: PackedStringArray) -> void:
	for arg in args:
		var key := arg
		var value := ""
		var eq := arg.find("=")
		if eq != -1:
			key = arg.substr(0, eq)
			value = arg.substr(eq + 1)
		match key:
			"--host": host = true
			"--join": join_address = value
			"--port": port = value.to_int()
			"--name": player_name = value
			"--bot": bot = true
			"--ping": ping_ms = value.to_float()
			"--jitter": jitter_ms = value.to_float()
			"--loss": loss_percent = value.to_float()
			"--quit-after": quit_after = value.to_float()
			"--screenshot": screenshot_path = value
			"--screenshot-at": screenshot_at = value.to_float()
			_: push_warning("Unknown launch option: %s" % arg)


func wants_auto_start() -> bool:
	return host or not join_address.is_empty()


func _quit() -> void:
	quitting.emit()
	get_tree().quit()
