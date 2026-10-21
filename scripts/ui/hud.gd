class_name Hud
extends CanvasLayer
## In-match overlay: crosshair, debug info (F3) and the pause menu (Esc).

var game: Node

var _debug_label: Label
var _pause_panel: PanelContainer
var _click_hint: Label


func _ready() -> void:
	_build_crosshair()
	_debug_label = Label.new()
	_debug_label.position = Vector2(8, 6)
	_debug_label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	_debug_label.add_theme_constant_override("shadow_offset_x", 2)
	_debug_label.add_theme_constant_override("shadow_offset_y", 2)
	add_child(_debug_label)
	_build_pause_menu()
	_click_hint = _make_center_label("Click to play")
	_click_hint.visible = false
	add_child(_click_hint)
	if not LaunchArgs.bot:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("pause"):
		_set_paused(not _pause_panel.visible)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("toggle_debug"):
		_debug_label.visible = not _debug_label.visible
	elif event is InputEventMouseButton and event.pressed and not _pause_panel.visible \
			and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED


func _process(_delta: float) -> void:
	_click_hint.visible = not _pause_panel.visible and not LaunchArgs.bot \
		and DisplayServer.get_name() != "headless" \
		and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED
	if not _debug_label.visible or game == null:
		return
	var player: Player = game.local_player
	var lines := [
		"UHC PvP %s" % ProjectSettings.get_setting("application/config/version"),
		"FPS: %d" % Engine.get_frames_per_second(),
		"Role: %s" % ("host" if Net.is_host() else "player"),
		"Ping: %d ms" % roundi(Net.ping() * 1000.0),
		"Players: %d/%d" % [game.player_count(), Tuning.MAX_PLAYERS],
		"Team: %s" % Arena.team_name(player.team),
		"XYZ: %.2f / %.2f / %.2f" % [player.position.x, player.position.y, player.position.z],
		"Facing: yaw %.1f, pitch %.1f" % [player.yaw, player.pitch],
	]
	if Net.is_lag_simulated():
		lines.append("Simulated lag: +%d ms ping" % roundi(LaunchArgs.ping_ms))
	_debug_label.text = "\n".join(lines)


func _set_paused(paused: bool) -> void:
	# The match keeps running while the menu is open; this only frees the mouse.
	_pause_panel.visible = paused
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if paused else Input.MOUSE_MODE_CAPTURED


func _build_crosshair() -> void:
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(center)
	var cross := Control.new()
	cross.custom_minimum_size = Vector2(18, 18)
	cross.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.add_child(cross)
	for rect: Rect2 in [Rect2(8, 0, 2, 18), Rect2(0, 8, 18, 2)]:
		var bar := ColorRect.new()
		bar.color = Color(1, 1, 1, 0.85)
		bar.position = rect.position
		bar.size = rect.size
		bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
		cross.add_child(bar)


func _build_pause_menu() -> void:
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, 0.45)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	_pause_panel = PanelContainer.new()
	_pause_panel.set_anchors_preset(Control.PRESET_FULL_RECT)
	_pause_panel.add_theme_stylebox_override("panel", StyleBoxEmpty.new())
	_pause_panel.visible = false
	_pause_panel.add_child(dim)
	var center := CenterContainer.new()
	_pause_panel.add_child(center)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	center.add_child(box)
	var title := Label.new()
	title.text = "Paused"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 28)
	box.add_child(title)
	var resume := Button.new()
	resume.text = "Back to game"
	resume.custom_minimum_size = Vector2(240, 40)
	resume.pressed.connect(_set_paused.bind(false))
	box.add_child(resume)
	var leave := Button.new()
	leave.text = "Leave match"
	leave.custom_minimum_size = Vector2(240, 40)
	leave.pressed.connect(func() -> void: Session.leave())
	box.add_child(leave)
	add_child(_pause_panel)


func _make_center_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.set_anchors_preset(Control.PRESET_CENTER)
	label.position.y += 40
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	return label
