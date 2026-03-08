## JournalUI.gd
## Controller for the journal overlay displaying quest logs, NPC notes, locations, and player notes.
## Displays category filters (left), entry list (center), and entry details (right).
## Attach to the root JournalUI Control node in JournalUI.tscn.

extends Control

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the player selects a journal entry from the center list.
signal entry_selected(entry_id: int)

## Emitted when the player clicks a category filter button.
signal filter_changed(category: String)

## Emitted when the player closes the journal overlay.
signal close_requested()

## Emitted when the player requests to add a new note via the Add Note button.
signal add_note_requested(title: String, text: String)

# ── Color constants ────────────────────────────────────────────────────────────
## Centralized design tokens — match dark fantasy palette.

const COLOR_TEXT_DEFAULT    := Color("#eee8d5")   # warm off-white — body text
const COLOR_TEXT_DIM        := Color("#b8b0a0")   # muted — secondary labels
const COLOR_TEXT_DISABLED   := Color("#595650")   # disabled text
const COLOR_ACCENT_GOLD     := Color("#c9a227")   # gold — headings, active category
const COLOR_GOLD_HOVER      := Color("#f2d238")   # bright gold hover
const COLOR_BORDER_MUTED    := Color(0.35, 0.30, 0.18, 0.7)
const COLOR_BORDER_GOLD     := Color(0.788, 0.635, 0.153, 1.0)

# Category badge colors (unread counts)
const COLOR_BADGE_BG        := Color(0.788, 0.635, 0.153, 1.0)  # gold
const COLOR_BADGE_TEXT      := Color(0.063, 0.063, 0.157, 1.0) # dark navy

# ── Node references ────────────────────────────────────────────────────────────

@onready var close_button:         Button         = %CloseButton
@onready var filter_buttons_vbox:  VBoxContainer  = %FilterButtonsVBox
@onready var entries_vbox:         VBoxContainer  = %EntriesVBox
@onready var details_vbox:         VBoxContainer  = %DetailsVBox
@onready var placeholder_label:    Label          = %PlaceholderLabel
@onready var add_note_button:      Button         = %AddNoteButton

# ── Runtime state ──────────────────────────────────────────────────────────────

# Currently active category filter ("All", "Quests", "NPCs", etc.).
var _active_filter: String = "All"

# Currently selected entry ID (for highlighting).
var _selected_entry_id: int = -1

# Filter category buttons — rebuilt when update_entries() is called.
# Dictionary keyed by category name to Button node.
var _filter_buttons: Dictionary = {}

# Entry list buttons — rebuilt when update_entries() is called.
# Array of Dictionaries with {id, button, panel}.
var _entry_cards: Array[Dictionary] = []

# Details panel widgets — created dynamically when showing entry details.
var _details_widgets: Dictionary = {}

# Add Note popup dialog — shown when Add Note button is pressed.
var _add_note_popup: PopupPanel = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	close_button.pressed.connect(_on_close_button_pressed)
	add_note_button.pressed.connect(_on_add_note_button_pressed)
	_initialize_placeholder_state()
	_initialize_filter_buttons()


func _input(event: InputEvent) -> void:
	## Allow Escape key to close the journal overlay.
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		close_requested.emit()


func _initialize_placeholder_state() -> void:
	## Sets up an empty-but-valid UI state before data arrives.
	placeholder_label.show()


func _initialize_filter_buttons() -> void:
	## Creates the category filter buttons in the left sidebar.
	var categories := ["All", "Quests", "NPCs", "Locations", "Combat", "Items", "Notes"]

	for category in categories:
		var btn := _create_filter_button(category)
		filter_buttons_vbox.add_child(btn)
		_filter_buttons[category] = btn

	# Highlight "All" by default
	set_active_filter("All")


# ══════════════════════════════════════════════════════════════════════════════
# PUBLIC API — called by JournalBridge via Node.Call()
# ══════════════════════════════════════════════════════════════════════════════

## Updates the journal entry list in the center column.
## entries is an Array of Dictionaries with:
## {id: int, category: String, title: String, description: String, timestamp: String, location_name: String, is_read: bool}
func update_entries(entries: Array) -> void:
	_clear_entry_list()

	if entries.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No journal entries found."
		empty_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		empty_label.add_theme_font_size_override("font_size", 13)
		empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		entries_vbox.add_child(empty_label)
		return

	for entry_data in entries:
		var entry_id: int = entry_data.get("id", -1)
		var category: String = entry_data.get("category", "Unknown")
		var title: String = entry_data.get("title", "Untitled Entry")
		var timestamp: String = entry_data.get("timestamp", "")
		var is_read: bool = entry_data.get("is_read", true)

		var card := _create_entry_card(entry_id, title, timestamp, is_read, category)
		entries_vbox.add_child(card.panel)
		_entry_cards.append(card)


## Updates the entry details panel in the right column.
## entry is a Dictionary with: {id, category, title, description, timestamp, location_name, is_read}
func update_entry_details(entry: Dictionary) -> void:
	placeholder_label.hide()
	_clear_details_panel()

	var entry_id: int = entry.get("id", -1)
	var title: String = entry.get("title", "Untitled Entry")
	var category: String = entry.get("category", "Unknown")
	var timestamp: String = entry.get("timestamp", "")
	var location_name: String = entry.get("location_name", "")
	var description: String = entry.get("description", "")

	# Entry title (large, gold)
	var title_label := Label.new()
	title_label.text = title
	title_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	title_label.add_theme_font_size_override("font_size", 20)
	title_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	details_vbox.add_child(title_label)
	_details_widgets["title_label"] = title_label

	# Category tag
	var category_label := Label.new()
	category_label.text = "[%s]" % category
	category_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	category_label.add_theme_font_size_override("font_size", 13)
	details_vbox.add_child(category_label)
	_details_widgets["category_label"] = category_label

	# Timestamp and location
	var meta_text := ""
	if !timestamp.is_empty():
		meta_text += timestamp
	if !location_name.is_empty():
		meta_text += " — %s" % location_name if !meta_text.is_empty() else location_name

	if !meta_text.is_empty():
		var meta_label := Label.new()
		meta_label.text = meta_text
		meta_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		meta_label.add_theme_font_size_override("font_size", 12)
		details_vbox.add_child(meta_label)
		_details_widgets["meta_label"] = meta_label

	# Spacer
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 12)
	details_vbox.add_child(spacer)

	# Description
	if !description.is_empty():
		var desc_label := Label.new()
		desc_label.text = description
		desc_label.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
		desc_label.add_theme_font_size_override("font_size", 13)
		desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		details_vbox.add_child(desc_label)
		_details_widgets["description_label"] = desc_label


## Sets the active category filter and highlights the corresponding button.
func set_active_filter(category: String) -> void:
	_active_filter = category

	# Update button styles
	for cat_name in _filter_buttons.keys():
		var btn: Button = _filter_buttons[cat_name]
		if cat_name == category:
			_apply_button_style(btn, "filter_active")
		else:
			_apply_button_style(btn, "filter_normal")


## Updates the unread count badges on category filter buttons.
## counts is a Dictionary with category names as keys and unread counts as values.
## Example: {"Quests": 3, "NPCs": 1, "Locations": 0}
func set_unread_counts(counts: Dictionary) -> void:
	for category in _filter_buttons.keys():
		var btn: Button = _filter_buttons[category]
		var count: int = counts.get(category, 0)

		# Update button text with badge
		if count > 0:
			btn.text = "%s (%d)" % [category, count]
		else:
			btn.text = category


## Clears the entry details panel and shows the placeholder message.
func clear_entry_details() -> void:
	_clear_details_panel()
	placeholder_label.show()
	_selected_entry_id = -1


# ══════════════════════════════════════════════════════════════════════════════
# PRIVATE HELPERS — UI Building
# ══════════════════════════════════════════════════════════════════════════════

func _create_filter_button(category: String) -> Button:
	## Builds a single category filter button for the left sidebar.
	var btn := Button.new()
	btn.text = category
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.custom_minimum_size = Vector2(0, 36)

	_apply_button_style(btn, "filter_normal")

	btn.pressed.connect(func(): _on_filter_button_pressed(category))

	return btn


func _create_entry_card(entry_id: int, title: String, timestamp: String, is_read: bool, category: String) -> Dictionary:
	## Builds a single entry card for the center list showing title and timestamp.
	## Unread entries are bolded.
	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_entry_card"))

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)

	# Title label
	var title_label := Label.new()
	title_label.text = title
	var title_color := COLOR_ACCENT_GOLD if !is_read else COLOR_TEXT_DEFAULT
	title_label.add_theme_color_override("font_color", title_color)
	title_label.add_theme_font_size_override("font_size", 14)
	title_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(title_label)

	# Timestamp and category label
	var meta_label := Label.new()
	meta_label.text = "%s — [%s]" % [timestamp, category]
	meta_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	meta_label.add_theme_font_size_override("font_size", 11)
	vbox.add_child(meta_label)

	# Make the panel clickable
	var btn := Button.new()
	btn.flat = true
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.size_flags_vertical = Control.SIZE_EXPAND_FILL
	btn.mouse_filter = Control.MOUSE_FILTER_PASS
	btn.pressed.connect(func(): _on_entry_card_pressed(entry_id, panel))
	panel.add_child(btn)
	panel.move_child(btn, 0)  # Place button behind content for clickable background

	return {"id": entry_id, "button": btn, "panel": panel}


# ── Cleanup helpers ────────────────────────────────────────────────────────────

func _clear_entry_list() -> void:
	for card in _entry_cards:
		card.panel.queue_free()
	_entry_cards.clear()


func _clear_details_panel() -> void:
	for widget in _details_widgets.values():
		if widget is Node:
			widget.queue_free()
	_details_widgets.clear()


# ── Styling helpers ────────────────────────────────────────────────────────────

func _apply_button_style(btn: Button, style_type: String) -> void:
	## Applies consistent button styling based on category.
	match style_type:
		"filter_active":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_gold_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_gold_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_gold_pressed"))
			btn.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
			btn.add_theme_color_override("font_hover_color", COLOR_GOLD_HOVER)
		"filter_normal":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
			btn.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
			btn.add_theme_color_override("font_hover_color", COLOR_ACCENT_GOLD)

	btn.add_theme_font_size_override("font_size", 13)


func _get_stylebox(stylebox_name: String) -> StyleBoxFlat:
	## Retrieve or construct a stylebox for runtime button creation.
	match stylebox_name:
		"StyleBox_btn_normal":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.11, 0.11, 0.20, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = COLOR_BORDER_MUTED
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 6.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_btn_hover":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.18, 0.16, 0.10, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = COLOR_BORDER_GOLD
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 6.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_btn_pressed":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.25, 0.20, 0.06, 1.0)
			style.border_width_left = 2
			style.border_width_top = 2
			style.border_width_right = 2
			style.border_width_bottom = 2
			style.border_color = Color(0.95, 0.82, 0.35, 1.0)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 6.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_btn_gold_normal":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.18, 0.14, 0.04, 1.0)
			style.border_width_left = 2
			style.border_width_top = 2
			style.border_width_right = 2
			style.border_width_bottom = 2
			style.border_color = COLOR_BORDER_GOLD
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 12.0
			style.content_margin_top = 6.0
			style.content_margin_right = 12.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_btn_gold_hover":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.28, 0.22, 0.06, 1.0)
			style.border_width_left = 2
			style.border_width_top = 2
			style.border_width_right = 2
			style.border_width_bottom = 2
			style.border_color = Color(0.95, 0.82, 0.35, 1.0)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 12.0
			style.content_margin_top = 6.0
			style.content_margin_right = 12.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_btn_gold_pressed":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.35, 0.28, 0.08, 1.0)
			style.border_width_left = 2
			style.border_width_top = 2
			style.border_width_right = 2
			style.border_width_bottom = 2
			style.border_color = Color(1.0, 0.95, 0.6, 1.0)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 12.0
			style.content_margin_top = 6.0
			style.content_margin_right = 12.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_entry_card":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.11, 0.11, 0.20, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = Color(0.35, 0.30, 0.18, 0.5)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 8.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 8.0
			return style

		"StyleBox_entry_card_selected":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.18, 0.16, 0.10, 1.0)
			style.border_width_left = 2
			style.border_width_top = 2
			style.border_width_right = 2
			style.border_width_bottom = 2
			style.border_color = COLOR_BORDER_GOLD
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 8.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 8.0
			return style

		_:
			push_warning("JournalUI._get_stylebox: Unknown stylebox '%s'" % stylebox_name)
			return StyleBoxFlat.new()


# ══════════════════════════════════════════════════════════════════════════════
# SIGNAL HANDLERS
# ══════════════════════════════════════════════════════════════════════════════

func _on_close_button_pressed() -> void:
	close_requested.emit()


func _on_filter_button_pressed(category: String) -> void:
	filter_changed.emit(category)


func _on_entry_card_pressed(entry_id: int, panel: PanelContainer) -> void:
	# Highlight selected entry
	_selected_entry_id = entry_id

	# Update all entry card borders to show selection
	for card in _entry_cards:
		if card.id == entry_id:
			card.panel.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_entry_card_selected"))
		else:
			card.panel.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_entry_card"))

	entry_selected.emit(entry_id)


func _on_add_note_button_pressed() -> void:
	## Shows a popup for the player to create a new note.
	if _add_note_popup != null:
		_add_note_popup.queue_free()

	_add_note_popup = PopupPanel.new()
	_add_note_popup.size = Vector2(500, 0)
	_add_note_popup.popup_window = false
	_add_note_popup.exclusive = true

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 20)
	margin.add_theme_constant_override("margin_top", 20)
	margin.add_theme_constant_override("margin_right", 20)
	margin.add_theme_constant_override("margin_bottom", 20)
	_add_note_popup.add_child(margin)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	margin.add_child(vbox)

	# Title
	var title_label := Label.new()
	title_label.text = "Add New Note"
	title_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	title_label.add_theme_font_size_override("font_size", 18)
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title_label)

	# Note title field
	var title_field_label := Label.new()
	title_field_label.text = "Title:"
	title_field_label.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	title_field_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(title_field_label)

	var title_field := LineEdit.new()
	title_field.placeholder_text = "Enter note title..."
	title_field.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	title_field.add_theme_font_size_override("font_size", 13)
	vbox.add_child(title_field)

	# Note text field
	var text_field_label := Label.new()
	text_field_label.text = "Text:"
	text_field_label.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	text_field_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(text_field_label)

	var text_field := TextEdit.new()
	text_field.placeholder_text = "Enter note text..."
	text_field.custom_minimum_size = Vector2(0, 120)
	text_field.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	text_field.add_theme_font_size_override("font_size", 13)
	vbox.add_child(text_field)

	# Action buttons
	var buttons_hbox := HBoxContainer.new()
	buttons_hbox.add_theme_constant_override("separation", 12)
	vbox.add_child(buttons_hbox)

	var cancel_btn := Button.new()
	cancel_btn.text = "Cancel"
	cancel_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(cancel_btn, "filter_normal")
	cancel_btn.pressed.connect(func():
		if _add_note_popup:
			_add_note_popup.queue_free()
			_add_note_popup = null
	)
	buttons_hbox.add_child(cancel_btn)

	var add_btn := Button.new()
	add_btn.text = "Add Note"
	add_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(add_btn, "filter_active")
	add_btn.pressed.connect(func():
		var note_title := title_field.text.strip_edges()
		var note_text := text_field.text.strip_edges()

		if note_title.is_empty() or note_text.is_empty():
			var error_color := Color("#8b2020")
			if note_title.is_empty():
				title_field.add_theme_color_override("font_placeholder_color", error_color)
				title_field.placeholder_text = "Title is required"
				get_tree().create_timer(1.5).timeout.connect(func():
					if is_instance_valid(title_field):
						title_field.add_theme_color_override("font_placeholder_color", COLOR_TEXT_DIM)
						title_field.placeholder_text = "Enter note title..."
				)
			if note_text.is_empty():
				text_field.add_theme_color_override("font_placeholder_color", error_color)
				text_field.placeholder_text = "Note text is required"
				get_tree().create_timer(1.5).timeout.connect(func():
					if is_instance_valid(text_field):
						text_field.add_theme_color_override("font_placeholder_color", COLOR_TEXT_DIM)
						text_field.placeholder_text = "Enter note text..."
				)
			return

		add_note_requested.emit(note_title, note_text)

		if _add_note_popup:
			_add_note_popup.queue_free()
			_add_note_popup = null
	)
	buttons_hbox.add_child(add_btn)

	add_child(_add_note_popup)
	_add_note_popup.popup_centered()
