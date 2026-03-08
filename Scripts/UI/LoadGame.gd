# LoadGame.gd
# Load game save slot selection UI controller for Chronicles Unbound.
# Emits signals that SceneManager connects to for navigation.

extends Control

# ──────────────────────────────────────────────────────────────────────────────
# Signals
# ──────────────────────────────────────────────────────────────────────────────

signal save_selected(slot_id: int)
signal back_pressed

# ──────────────────────────────────────────────────────────────────────────────
# Node references (scene-unique names)
# ──────────────────────────────────────────────────────────────────────────────

@onready var save_slot_list: VBoxContainer = %SaveSlotList
@onready var empty_state_label: Label = %EmptyStateLabel
@onready var scroll_container: ScrollContainer = %ScrollContainer
@onready var back_button: Button = %BackButton

# ──────────────────────────────────────────────────────────────────────────────
# Preloaded scene for dynamically created save slot UI
# ──────────────────────────────────────────────────────────────────────────────

# TODO: Create a SaveSlotItem.tscn scene for individual save slot display
# For now, we'll create save slot UI programmatically

# ──────────────────────────────────────────────────────────────────────────────
# Lifecycle
# ──────────────────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Connect back button
	back_button.pressed.connect(_on_back_pressed)

	# Populate save slot list
	_populate_save_slots()

	# Focus the first focusable element (first Load button or Back button)
	call_deferred("_set_initial_focus")

	print("[LoadGame] Load game screen initialized.")

# ──────────────────────────────────────────────────────────────────────────────
# Save slot population
# ──────────────────────────────────────────────────────────────────────────────

func _populate_save_slots() -> void:
	# Clear any existing slot UI (except the empty state label)
	for child in save_slot_list.get_children():
		if child != empty_state_label:
			child.queue_free()

	# Retrieve save slots from SaveLoadBridge autoload
	var bridge = get_node_or_null("/root/SaveLoadBridge")
	var save_slots: Array = []
	if bridge:
		save_slots = bridge.GetAllSaveSlots()
	else:
		push_warning("[LoadGame] SaveLoadBridge not found at /root/SaveLoadBridge.")

	if save_slots.is_empty():
		empty_state_label.visible = true
		print("[LoadGame] No saved games found.")
	else:
		empty_state_label.visible = false
		for slot_data in save_slots:
			_create_save_slot_ui(slot_data)

# ──────────────────────────────────────────────────────────────────────────────
# Save slot UI creation
# ──────────────────────────────────────────────────────────────────────────────

func _create_save_slot_ui(slot_data: Dictionary) -> void:
	# slot_data keys from SaveLoadBridge: id, name, character_name, character_level, play_time, play_time_formatted, updated_at

	# Create a PanelContainer for the save slot
	var panel = PanelContainer.new()
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	# Apply the slot panel style (we need to create this StyleBox programmatically
	# or reference it from the scene's SubResources — for now, create a simple one)
	var style_box = StyleBoxFlat.new()
	style_box.bg_color = Color(0.11, 0.11, 0.20, 1.0)
	style_box.border_width_left = 1
	style_box.border_width_top = 1
	style_box.border_width_right = 1
	style_box.border_width_bottom = 1
	style_box.border_color = Color(0.35, 0.30, 0.18, 0.7)
	style_box.corner_radius_top_left = 4
	style_box.corner_radius_top_right = 4
	style_box.corner_radius_bottom_left = 4
	style_box.corner_radius_bottom_right = 4
	style_box.content_margin_left = 16.0
	style_box.content_margin_top = 12.0
	style_box.content_margin_right = 16.0
	style_box.content_margin_bottom = 12.0
	panel.add_theme_stylebox_override("panel", style_box)

	# Create an HBoxContainer for slot content
	var hbox = HBoxContainer.new()
	hbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_theme_constant_override("separation", 20)
	panel.add_child(hbox)

	# Left side: VBoxContainer for slot info
	var info_vbox = VBoxContainer.new()
	info_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	info_vbox.add_theme_constant_override("separation", 4)
	hbox.add_child(info_vbox)

	# Slot name (bold, gold color)
	var slot_name_label = Label.new()
	slot_name_label.text = slot_data.get("name", "Unknown Slot")
	slot_name_label.add_theme_color_override("font_color", Color(0.788, 0.635, 0.153, 1.0))
	slot_name_label.add_theme_font_size_override("font_size", 18)
	info_vbox.add_child(slot_name_label)

	# Character info (name + level)
	var character_name = slot_data.get("character_name", "")
	var character_level = slot_data.get("character_level", 0)
	var char_info_text = ""
	if character_name != "" and character_level > 0:
		char_info_text = "%s - Level %d" % [character_name, character_level]
	elif character_name != "":
		char_info_text = character_name
	elif character_level > 0:
		char_info_text = "Level %d" % character_level
	else:
		char_info_text = "No character data"

	var char_info_label = Label.new()
	char_info_label.text = char_info_text
	char_info_label.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835, 1.0))
	char_info_label.add_theme_font_size_override("font_size", 14)
	info_vbox.add_child(char_info_label)

	# Play time and last updated
	var play_time_seconds = slot_data.get("play_time", 0)
	var updated_at = slot_data.get("updated_at", "")
	var hours = play_time_seconds / 3600
	var minutes = (play_time_seconds % 3600) / 60
	var time_str = "%dh %dm" % [hours, minutes]
	var meta_text = "Play time: %s | Last saved: %s" % [time_str, updated_at]

	var meta_label = Label.new()
	meta_label.text = meta_text
	meta_label.add_theme_color_override("font_color", Color(0.533, 0.533, 0.533, 1.0))
	meta_label.add_theme_font_size_override("font_size", 12)
	info_vbox.add_child(meta_label)

	# Right side: VBoxContainer for buttons
	var button_vbox = VBoxContainer.new()
	button_vbox.add_theme_constant_override("separation", 8)
	hbox.add_child(button_vbox)

	# Load button (gold-accented)
	var load_button = Button.new()
	load_button.text = "Load"
	load_button.custom_minimum_size = Vector2(100, 36)
	load_button.add_theme_color_override("font_color", Color(0.95, 0.82, 0.35, 1.0))
	load_button.add_theme_color_override("font_hover_color", Color(1.0, 0.95, 0.55, 1.0))
	load_button.add_theme_color_override("font_pressed_color", Color(1.0, 1.0, 0.75, 1.0))
	load_button.add_theme_font_size_override("font_size", 14)

	# Create gold button styles
	var load_normal = _create_gold_button_style(Color(0.18, 0.14, 0.04, 1.0), Color(0.788, 0.635, 0.153, 1.0), 2)
	var load_hover = _create_gold_button_style(Color(0.28, 0.22, 0.06, 1.0), Color(0.95, 0.82, 0.35, 1.0), 2)
	var load_pressed = _create_gold_button_style(Color(0.35, 0.28, 0.08, 1.0), Color(1.0, 0.95, 0.6, 1.0), 2)
	load_button.add_theme_stylebox_override("normal", load_normal)
	load_button.add_theme_stylebox_override("hover", load_hover)
	load_button.add_theme_stylebox_override("pressed", load_pressed)
	load_button.add_theme_stylebox_override("focus", load_hover)

	# Store slot ID in button metadata for the signal
	var slot_id = slot_data.get("id", 0)
	load_button.set_meta("slot_id", slot_id)
	load_button.pressed.connect(_on_load_slot.bind(slot_id))
	button_vbox.add_child(load_button)

	# Delete button (red-tinted)
	var delete_button = Button.new()
	delete_button.text = "Delete"
	delete_button.custom_minimum_size = Vector2(100, 36)
	delete_button.add_theme_color_override("font_color", Color(0.95, 0.70, 0.70, 1.0))
	delete_button.add_theme_color_override("font_hover_color", Color(1.0, 0.85, 0.85, 1.0))
	delete_button.add_theme_color_override("font_pressed_color", Color(1.0, 0.95, 0.95, 1.0))
	delete_button.add_theme_font_size_override("font_size", 14)

	# Create delete button styles
	var delete_normal = _create_button_style(Color(0.20, 0.11, 0.11, 1.0), Color(0.35, 0.18, 0.18, 0.7), 1)
	var delete_hover = _create_button_style(Color(0.30, 0.14, 0.14, 1.0), Color(0.788, 0.153, 0.153, 1.0), 1)
	var delete_pressed = _create_button_style(Color(0.40, 0.18, 0.18, 1.0), Color(0.95, 0.35, 0.35, 1.0), 1)
	delete_button.add_theme_stylebox_override("normal", delete_normal)
	delete_button.add_theme_stylebox_override("hover", delete_hover)
	delete_button.add_theme_stylebox_override("pressed", delete_pressed)
	delete_button.add_theme_stylebox_override("focus", delete_hover)

	delete_button.pressed.connect(_on_delete_slot.bind(slot_id))
	button_vbox.add_child(delete_button)

	# Add the panel to the save slot list (before the empty state label, which should be last)
	save_slot_list.add_child(panel)
	save_slot_list.move_child(panel, save_slot_list.get_child_count() - 2)

# ──────────────────────────────────────────────────────────────────────────────
# Helper functions for creating StyleBoxFlat programmatically
# ──────────────────────────────────────────────────────────────────────────────

func _create_button_style(bg: Color, border: Color, border_width: int) -> StyleBoxFlat:
	var style = StyleBoxFlat.new()
	style.bg_color = bg
	style.border_width_left = border_width
	style.border_width_top = border_width
	style.border_width_right = border_width
	style.border_width_bottom = border_width
	style.border_color = border
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right = 4
	style.content_margin_left = 12.0
	style.content_margin_top = 8.0
	style.content_margin_right = 12.0
	style.content_margin_bottom = 8.0
	return style

func _create_gold_button_style(bg: Color, border: Color, border_width: int) -> StyleBoxFlat:
	var style = StyleBoxFlat.new()
	style.bg_color = bg
	style.border_width_left = border_width
	style.border_width_top = border_width
	style.border_width_right = border_width
	style.border_width_bottom = border_width
	style.border_color = border
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right = 4
	style.content_margin_left = 16.0
	style.content_margin_top = 8.0
	style.content_margin_right = 16.0
	style.content_margin_bottom = 8.0
	return style

# ──────────────────────────────────────────────────────────────────────────────
# Focus management
# ──────────────────────────────────────────────────────────────────────────────

func _set_initial_focus() -> void:
	# Focus the first Load button if there are any save slots, otherwise focus Back
	var first_button: Button = null
	for child in save_slot_list.get_children():
		if child is PanelContainer:
			# Find the first Load button in this panel
			var buttons = child.find_children("*", "Button", true, false)
			for btn in buttons:
				if btn.text == "Load":
					first_button = btn
					break
			if first_button:
				break

	if first_button:
		first_button.grab_focus()
	else:
		back_button.grab_focus()

# ──────────────────────────────────────────────────────────────────────────────
# Signal handlers
# ──────────────────────────────────────────────────────────────────────────────

func _on_load_slot(slot_id: int) -> void:
	print("[LoadGame] Load slot %d requested." % slot_id)
	save_selected.emit(slot_id)

func _on_delete_slot(slot_id: int) -> void:
	print("[LoadGame] Delete slot %d requested." % slot_id)
	var bridge = get_node_or_null("/root/SaveLoadBridge")
	if bridge:
		bridge.DeleteSaveSlot(slot_id)
		_populate_save_slots()

func _on_back_pressed() -> void:
	print("[LoadGame] Back to main menu.")
	back_pressed.emit()
