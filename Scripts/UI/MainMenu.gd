# MainMenu.gd
# Main menu UI controller for Chronicles Unbound.
# Emits signals that SceneManager connects to for navigation.

extends Control

# ──────────────────────────────────────────────────────────────────────────────
# Signals
# ──────────────────────────────────────────────────────────────────────────────

signal new_game_pressed
signal continue_pressed
signal load_game_pressed
signal settings_pressed
signal quit_pressed

# ──────────────────────────────────────────────────────────────────────────────
# Node references (scene-unique names)
# ──────────────────────────────────────────────────────────────────────────────

@onready var new_game_button: Button = %NewGameButton
@onready var continue_button: Button = %ContinueButton
@onready var load_game_button: Button = %LoadGameButton
@onready var settings_button: Button = %SettingsButton
@onready var quit_button: Button = %QuitButton

# ──────────────────────────────────────────────────────────────────────────────
# Lifecycle
# ──────────────────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Connect button signals to handlers
	new_game_button.pressed.connect(_on_new_game_pressed)
	continue_button.pressed.connect(_on_continue_pressed)
	load_game_button.pressed.connect(_on_load_game_pressed)
	settings_button.pressed.connect(_on_settings_pressed)
	quit_button.pressed.connect(_on_quit_pressed)

	# Check if saves exist and show/hide Continue button accordingly
	var bridge = get_node_or_null("/root/SaveLoadBridge")
	if bridge:
		continue_button.visible = bridge.HasAnySaves()
	else:
		continue_button.visible = false

	# Focus the first button for keyboard navigation
	new_game_button.grab_focus()

	print("[MainMenu] Main menu initialized.")

# ──────────────────────────────────────────────────────────────────────────────
# Button handlers
# ──────────────────────────────────────────────────────────────────────────────

func _on_new_game_pressed() -> void:
	print("[MainMenu] New Game button pressed.")
	new_game_pressed.emit()

func _on_continue_pressed() -> void:
	print("[MainMenu] Continue button pressed.")
	continue_pressed.emit()

func _on_load_game_pressed() -> void:
	print("[MainMenu] Load Game button pressed.")
	load_game_pressed.emit()

func _on_settings_pressed() -> void:
	print("[MainMenu] Settings button pressed.")
	settings_pressed.emit()

func _on_quit_pressed() -> void:
	print("[MainMenu] Quit button pressed.")
	quit_pressed.emit()
	get_tree().quit()
