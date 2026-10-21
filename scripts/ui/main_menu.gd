extends Control
## Title screen: pick a name, then host a match or join one by address.

const SETTINGS_PATH := "user://settings.cfg"

var _name_edit: LineEdit
var _address_edit: LineEdit
var _status: Label


func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_build_ui()
	_load_settings()
	_status.text = Session.status
	if LaunchArgs.wants_auto_start():
		_auto_start.call_deferred()


func _auto_start() -> void:
	var player_name := LaunchArgs.player_name if not LaunchArgs.player_name.is_empty() else "Bot"
	if LaunchArgs.host:
		Session.host(player_name, LaunchArgs.port)
	else:
		Session.join(player_name, LaunchArgs.join_address, LaunchArgs.port, 20)
	_status.text = Session.status


func _process(_delta: float) -> void:
	if _status.text != Session.status:
		_status.text = Session.status


func _on_host_pressed() -> void:
	_save_settings()
	if Session.host(_name_edit.text, Tuning.DEFAULT_PORT) != OK:
		_status.text = Session.status


func _on_join_pressed() -> void:
	_save_settings()
	var address := _address_edit.text.strip_edges()
	var port := Tuning.DEFAULT_PORT
	if address.contains(":"):
		port = address.get_slice(":", 1).to_int()
		address = address.get_slice(":", 0)
	if address.is_empty():
		_status.text = "Type the host's address first"
		return
	if Session.join(_name_edit.text, address, port) != OK:
		_status.text = "Couldn't start connecting"
		return
	_status.text = Session.status


func _build_ui() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var background := ColorRect.new()
	background.color = Color(0.11, 0.13, 0.17)
	background.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(background)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	var box := VBoxContainer.new()
	box.custom_minimum_size = Vector2(340, 0)
	box.add_theme_constant_override("separation", 10)
	center.add_child(box)

	var title := Label.new()
	title.text = "UHC PvP"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 48)
	box.add_child(title)
	var version := Label.new()
	version.text = "dev build %s" % ProjectSettings.get_setting("application/config/version")
	version.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	version.modulate = Color(1, 1, 1, 0.5)
	box.add_child(version)

	box.add_child(_make_caption("Name"))
	_name_edit = LineEdit.new()
	_name_edit.max_length = 16
	_name_edit.placeholder_text = "Your name"
	box.add_child(_name_edit)

	var host_button := Button.new()
	host_button.text = "Host match"
	host_button.custom_minimum_size.y = 40
	host_button.pressed.connect(_on_host_pressed)
	box.add_child(host_button)

	box.add_child(_make_caption("Host address"))
	_address_edit = LineEdit.new()
	_address_edit.placeholder_text = "e.g. 192.168.1.20"
	_address_edit.text_submitted.connect(func(_text: String) -> void: _on_join_pressed())
	box.add_child(_address_edit)
	var join_button := Button.new()
	join_button.text = "Join match"
	join_button.custom_minimum_size.y = 40
	join_button.pressed.connect(_on_join_pressed)
	box.add_child(join_button)

	_status = Label.new()
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.modulate = Color(1, 0.85, 0.5)
	box.add_child(_status)

	var quit_button := Button.new()
	quit_button.text = "Quit"
	quit_button.pressed.connect(func() -> void: get_tree().quit())
	box.add_child(quit_button)


func _make_caption(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.modulate = Color(1, 1, 1, 0.7)
	return label


func _load_settings() -> void:
	var config := ConfigFile.new()
	config.load(SETTINGS_PATH)
	_name_edit.text = config.get_value("player", "name", "")
	_address_edit.text = config.get_value("player", "last_address", "127.0.0.1")


func _save_settings() -> void:
	var config := ConfigFile.new()
	config.load(SETTINGS_PATH)
	config.set_value("player", "name", _name_edit.text)
	config.set_value("player", "last_address", _address_edit.text)
	config.save(SETTINGS_PATH)
