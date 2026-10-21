extends Node
## Registers the game's input actions in code, so key bindings live in one
## readable place and can be made rebindable later.
## Movement keys use physical key positions, so WASD works on any keyboard layout.

const KEYS := {
	"move_forward": KEY_W,
	"move_back": KEY_S,
	"move_left": KEY_A,
	"move_right": KEY_D,
	"jump": KEY_SPACE,
	"sprint": KEY_CTRL,
	"sneak": KEY_SHIFT,
	"hotbar_1": KEY_1,
	"hotbar_2": KEY_2,
	"toggle_debug": KEY_F3,
	"pause": KEY_ESCAPE,
}

const MOUSE_BUTTONS := {
	"attack": MOUSE_BUTTON_LEFT,
	"use": MOUSE_BUTTON_RIGHT,
}


func _ready() -> void:
	for action: String in KEYS:
		_ensure_action(action)
		var event := InputEventKey.new()
		event.physical_keycode = KEYS[action]
		InputMap.action_add_event(action, event)
	for action: String in MOUSE_BUTTONS:
		_ensure_action(action)
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTONS[action]
		InputMap.action_add_event(action, event)


func _ensure_action(action: String) -> void:
	if InputMap.has_action(action):
		InputMap.action_erase_events(action)
	else:
		InputMap.add_action(action)
