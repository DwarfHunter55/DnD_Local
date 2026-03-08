## ExplorationUI.gd
## Controller for the exploration screen — the main interface players see when
## exploring the world outside of combat. Displays current location info, narrative
## descriptions, party status, available actions, and connected locations for travel.
## Attach to the root ExplorationUI Control node in ExplorationUI.tscn.

extends Control

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the player selects an action button. The action_type matches
## the ActionDescriptor.Id from ExplorationManager.GetAvailableActions().
## action_data is a Dictionary containing any additional context (e.g. NPC ID, location ID).
signal action_selected(action_type: String, action_data: Dictionary)

## Emitted when the player selects a location to travel to.
signal travel_selected(location_id: String)

## Emitted when the player selects an NPC from a list to talk to.
signal npc_selected(npc_id: String)

## Emitted when the player submits dialogue text in the dialogue panel.
signal dialogue_submitted(text: String)

## Emitted when the player requests to buy an item from a shop.
signal shop_buy_requested(item_name: String, quantity: int)

## Emitted when the player requests to sell an item to a shop.
signal shop_sell_requested(item_name: String, quantity: int)

## Emitted when the player closes the shop panel.
signal shop_close_requested()

## Emitted when the player ends the current dialogue session.
signal dialogue_end_requested()

## Emitted when the player accepts a quest offered during dialogue.
signal quest_accept_requested(quest_id: String)

## Emitted when the player clicks the menu button.
signal menu_pressed()

## Emitted when the player clicks the Save button or presses F5.
## ExplorationBridge listens for this to trigger a save via SaveLoadBridge.
signal save_requested()

# ── Color constants ────────────────────────────────────────────────────────────
## Centralized design tokens — match NarrativeUI palette.

const COLOR_TEXT_DEFAULT    : Color = Color("#eee8d5")   # warm off-white — body text
const COLOR_TEXT_DIM        : Color = Color("#b8b0a0")   # muted — secondary labels
const COLOR_ACCENT_GOLD     : Color = Color("#c9a227")   # gold — headings, location names
const COLOR_HP_HEALTHY      : Color = Color("#2ecc71")   # green — HP bars above 50%
const COLOR_HP_HURT         : Color = Color("#f39c12")   # amber — HP bars 25–50%
const COLOR_HP_CRITICAL     : Color = Color("#e74c3c")   # red — HP bars below 25%
const COLOR_DANGER_LOW      : Color = Color("#2ecc71")   # green dots — danger 0-1
const COLOR_DANGER_MEDIUM   : Color = Color("#f39c12")   # amber dots — danger 2-3
const COLOR_DANGER_HIGH     : Color = Color("#e74c3c")   # red dots — danger 4-5

# ── Node references ────────────────────────────────────────────────────────────

@onready var location_name_label:   Label          = %LocationNameLabel
@onready var location_type_label:   Label          = %LocationTypeLabel
@onready var danger_dots_label:     Label          = %DangerDotsLabel
@onready var narrative_label:       RichTextLabel  = %NarrativeLabel
@onready var party_vbox:            VBoxContainer  = %PartyVBox
@onready var actions_vbox:          VBoxContainer  = %ActionsVBox
@onready var locations_vbox:        VBoxContainer  = %LocationsVBox
@onready var actions_margin:        MarginContainer = %ActionsMargin
@onready var inventory_button:     Button          = %InventoryButton
@onready var journal_button:       Button          = %JournalButton
@onready var save_button:          Button          = %SaveButton

# ── Runtime containers ─────────────────────────────────────────────────────────

# Party member slot nodes — rebuilt each time update_party() is called.
# Each entry is a Dictionary with: {root, name_label, class_label, hp_bar, hp_label}
var _party_slots: Array[Dictionary] = []

# Action button nodes — rebuilt each time update_actions() is called.
# Dictionary keyed by action_id (string) to Button node.
var _action_buttons: Dictionary = {}

# Location card nodes — rebuilt each time update_connected_locations() is called.
# Array of Dictionaries with: {root, location_id, name_label, type_label, distance_label, travel_button}
var _location_cards: Array[Dictionary] = []

# ── UI panel references (created dynamically) ──────────────────────────────

# Dialogue panel container — overlays the action area during NPC conversations.
var _dialogue_panel: VBoxContainer = null
var _dialogue_input: LineEdit = null
var _dialogue_send_btn: Button = null

# Shop panel container — overlays the action area when browsing a shop.
var _shop_panel: VBoxContainer = null
var _shop_gold_label: Label = null
var _shop_items_vbox: VBoxContainer = null

# Status bar label — shown at the bottom of the narrative panel.
var _status_label: Label = null

# Backup reference to original actions VBox parent for restoration.
var _original_actions_visible: bool = true

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_initialise_placeholder_state()
	inventory_button.pressed.connect(func(): action_selected.emit("inventory", {}))
	journal_button.pressed.connect(func(): action_selected.emit("journal", {}))
	save_button.pressed.connect(func(): save_requested.emit())


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_I:
			action_selected.emit("inventory", {})
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_J:
			action_selected.emit("journal", {})
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_F5:
			save_requested.emit()
			get_viewport().set_input_as_handled()


func _initialise_placeholder_state() -> void:
	## Sets up an empty-but-valid UI state for before any game data arrives.
	narrative_label.clear()
	narrative_label.bbcode_enabled = true

	# Welcome banner
	add_narrative_text(
		"[center][color=#c9a227][b]Chronicles Unbound[/b][/color][/center]\n\n" +
		"[color=#b8b0a0]Awaiting exploration data...[/color]\n"
	)

	location_name_label.text = "Unknown Location"
	location_type_label.text = "—"
	danger_dots_label.text = "○○○○○"

	# Clear dynamic content
	_clear_party_slots()
	_clear_action_buttons()
	_clear_location_cards()


# ── Public API ────────────────────────────────────────────────────────────────

## Updates the location header: name, type tag, and danger level indicator.
## danger_level should be 0-5 (0=safe, 5=deadly).
func update_location(location_name: String, location_type: String, danger_level: int, description: String) -> void:
	location_name_label.text = location_name
	location_type_label.text = location_type.capitalize()

	# Danger dots — filled count = danger_level, color by threshold
	var filled_dots: int = clamp(danger_level, 0, 5)
	var empty_dots: int = 5 - filled_dots
	var dots_text: String = "●".repeat(filled_dots) + "○".repeat(empty_dots)

	danger_dots_label.text = dots_text

	# Color the danger dots based on level
	if danger_level <= 1:
		danger_dots_label.add_theme_color_override("font_color", COLOR_DANGER_LOW)
	elif danger_level <= 3:
		danger_dots_label.add_theme_color_override("font_color", COLOR_DANGER_MEDIUM)
	else:
		danger_dots_label.add_theme_color_override("font_color", COLOR_DANGER_HIGH)

	# Add the location description to the narrative panel
	if description != "":
		add_narrative_text("\n[color=#c9a227][b]%s[/b][/color]\n%s\n" % [location_name, description])


## Appends BBCode-formatted text to the narrative window and scrolls to the bottom.
func add_narrative_text(text: String) -> void:
	narrative_label.append_text(text)
	_scroll_narrative_to_bottom()


## Clears the narrative panel.
func clear_narrative() -> void:
	narrative_label.clear()


## Updates the action button list based on available actions.
## actions is an Array of Dictionaries with: {id: String, display_text: String, category: String}
## This mirrors ExplorationManager.ActionDescriptor structure.
func update_actions(actions: Array) -> void:
	_clear_action_buttons()

	for action_data: Dictionary in actions:
		var action_id: String = action_data.get("id", "")
		var display_text: String = action_data.get("display_text", "Action")
		var category: String = action_data.get("category", "explore")

		var btn := Button.new()
		btn.text = display_text
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		# Apply button styles based on category
		if category == "travel":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_travel_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_travel_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_travel_hover"))
			btn.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_travel_normal"))
		else:
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
			btn.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_btn_hover"))

		btn.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
		btn.add_theme_color_override("font_hover_color", Color("#f5d76e"))
		btn.add_theme_color_override("font_pressed_color", Color("#fffacd"))
		btn.add_theme_font_size_override("font_size", 13)

		# Connect signal — capture action_id in lambda
		btn.pressed.connect(func(): _on_action_button_pressed(action_id))

		actions_vbox.add_child(btn)
		_action_buttons[action_id] = btn


## Updates the party panel with current party member data.
## party_data is an Array of Dictionaries with:
## {name: String, class_name: String, level: int, hp: int, max_hp: int}
func update_party(party_data: Array) -> void:
	_clear_party_slots()

	for i in range(party_data.size()):
		var member: Dictionary = party_data[i] as Dictionary
		var name: String = member.get("name", "Unknown")
		var char_class: String = member.get("class_name", "Adventurer")
		var level: int = member.get("level", 1)
		var hp: int = member.get("hp", 0)
		var max_hp: int = member.get("max_hp", 1)

		var slot: Dictionary = _create_party_slot(i, name, char_class, level, hp, max_hp)
		party_vbox.add_child(slot.root)
		_party_slots.append(slot)


## Updates the connected locations panel. Shows all discovered locations
## reachable from the current location.
## locations is an Array of Dictionaries with:
## {id: String, name: String, type: String, distance: int (in hops)}
func update_connected_locations(locations: Array) -> void:
	_clear_location_cards()

	if locations.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No discovered connections."
		empty_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		empty_label.add_theme_font_size_override("font_size", 12)
		empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		locations_vbox.add_child(empty_label)
		return

	for loc_data: Dictionary in locations:
		var loc_id: String = loc_data.get("id", "")
		var loc_name: String = loc_data.get("name", "Unknown")
		var loc_type: String = loc_data.get("type", "wilderness")
		var distance: int = loc_data.get("distance", 0)

		var card: Dictionary = _create_location_card(loc_id, loc_name, loc_type, distance)
		locations_vbox.add_child(card.root)
		_location_cards.append(card)


## Shows a list of NPCs available at the current location for the player to select.
## npcs is an Array of Dictionaries with: {id: String, name: String, role: String}
func show_npc_list(npcs: Array) -> void:
	# Clear action buttons and replace with NPC selection buttons
	_clear_action_buttons()

	if npcs.is_empty():
		var empty_btn := Button.new()
		empty_btn.text = "No NPCs available"
		empty_btn.disabled = true
		empty_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		actions_vbox.add_child(empty_btn)
		return

	for npc_data: Dictionary in npcs:
		var npc_id: String = npc_data.get("id", "")
		var npc_name: String = npc_data.get("name", "Stranger")
		var npc_role: String = npc_data.get("role", "")

		var display_text: String = npc_name
		if npc_role != "":
			display_text += " (%s)" % npc_role

		var btn := Button.new()
		btn.text = display_text
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_normal"))
		btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
		btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
		btn.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_btn_hover"))

		btn.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
		btn.add_theme_color_override("font_hover_color", Color("#f5d76e"))
		btn.add_theme_color_override("font_pressed_color", Color("#fffacd"))
		btn.add_theme_font_size_override("font_size", 13)

		btn.pressed.connect(func(): _on_npc_button_pressed(npc_id))

		actions_vbox.add_child(btn)


## Shows a list of travel destinations (connected locations) as buttons.
## locations is an Array of Dictionaries with: {id: String, name: String, distance: int}
func show_travel_options(locations: Array) -> void:
	# Clear action buttons and replace with travel destination buttons
	_clear_action_buttons()

	if locations.is_empty():
		var empty_btn := Button.new()
		empty_btn.text = "No destinations available"
		empty_btn.disabled = true
		empty_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		actions_vbox.add_child(empty_btn)
		return

	for loc_data: Dictionary in locations:
		var loc_id: String = loc_data.get("id", "")
		var loc_name: String = loc_data.get("name", "Unknown")
		var distance: int = loc_data.get("distance", 0)

		var display_text: String = "%s (%d hops)" % [loc_name, distance]

		var btn := Button.new()
		btn.text = display_text
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_travel_normal"))
		btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_travel_hover"))
		btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_travel_hover"))
		btn.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_travel_normal"))

		btn.add_theme_color_override("font_color", Color("#f5d76e"))
		btn.add_theme_color_override("font_hover_color", Color("#fffacd"))
		btn.add_theme_color_override("font_pressed_color", Color("#ffffff"))
		btn.add_theme_font_size_override("font_size", 13)

		btn.pressed.connect(func(): _on_travel_button_pressed(loc_id))

		actions_vbox.add_child(btn)


## ══════════════════════════════════════════════════════════════════════════════
## DIALOGUE SUB-PANEL
## ══════════════════════════════════════════════════════════════════════════════

## Shows the dialogue panel with NPC info and input controls.
## npc_name: The NPC's display name (shown in header).
## npc_role: The NPC's role/title (shown in header).
## greeting: The NPC's initial greeting (shown in narrative area).
func show_dialogue_panel(npc_name: String, npc_role: String, greeting: String) -> void:
	# Hide normal actions and create the dialogue panel
	_hide_actions_area()
	_create_dialogue_panel_if_needed()

	# Update dialogue header with NPC info
	var header_text: String = "[color=#c9a227][b]%s[/b][/color] - [color=#b8b0a0]%s[/color]" % [npc_name, npc_role]
	if _dialogue_panel.get_child_count() > 0:
		var header_label: RichTextLabel = _dialogue_panel.get_child(0) as RichTextLabel
		if header_label:
			header_label.clear()
			header_label.append_text(header_text)

	# Add greeting to narrative panel
	add_narrative_text("\n[color=#c9a227][b]%s[/b][/color]: %s\n" % [npc_name, greeting])

	_dialogue_panel.show()


## Appends a dialogue exchange to the narrative panel with speaker name formatting.
## speaker_name: Who is speaking (NPC name or "You").
## text: What they said.
func update_dialogue_response(speaker_name: String, text: String) -> void:
	var color: String = "#c9a227" if speaker_name != "You" else "#f5d76e"
	add_narrative_text("\n[color=%s][b]%s[/b][/color]: %s\n" % [color, speaker_name, text])


## Hides the dialogue panel and restores the normal action buttons.
func hide_dialogue_panel() -> void:
	if _dialogue_panel:
		_dialogue_panel.hide()
	_show_actions_area()


## ══════════════════════════════════════════════════════════════════════════════
## SHOP SUB-PANEL
## ══════════════════════════════════════════════════════════════════════════════

## Shows the shop panel with item list and gold display.
## shop_name: The shop's name (shown as header).
## items: Array of Dictionaries with: {name: String, price: int, quantity: int, description: String}.
## player_gold: The player's current gold amount.
func show_shop_panel(shop_name: String, items: Array, player_gold: int) -> void:
	_hide_actions_area()
	_create_shop_panel_if_needed()

	# Update shop header
	if _shop_panel.get_child_count() > 0:
		var header_label: Label = _shop_panel.get_child(0) as Label
		if header_label:
			header_label.text = shop_name.to_upper()

	# Update gold display
	update_shop_gold(player_gold)

	# Rebuild item list
	_rebuild_shop_item_list(items, player_gold)

	_shop_panel.show()


## Updates the player's gold display in the shop panel.
func update_shop_gold(player_gold: int) -> void:
	if _shop_gold_label:
		_shop_gold_label.text = "Your Gold: %d gp" % player_gold


## Hides the shop panel and restores normal action buttons.
func hide_shop_panel() -> void:
	if _shop_panel:
		_shop_panel.hide()
	_show_actions_area()


## ══════════════════════════════════════════════════════════════════════════════
## STATUS BAR
## ══════════════════════════════════════════════════════════════════════════════

## Shows or hides a status message at the bottom of the narrative panel.
## Use this for "DM is thinking..." or "Generating response..." indicators.
## Pass an empty string to hide the status label.
func set_status(text: String) -> void:
	if _status_label == null:
		_create_status_label()

	if text.is_empty():
		_status_label.hide()
	else:
		_status_label.text = text
		_status_label.show()


## ══════════════════════════════════════════════════════════════════════════════
## QUEST NOTIFICATIONS
## ══════════════════════════════════════════════════════════════════════════════

## Appends a quest notification to the narrative panel with distinct styling.
## Use this for quest updates, new quests, or quest completion messages.
func show_quest_notification(text: String) -> void:
	add_narrative_text("\n[color=#ffd700]⚔ QUEST UPDATE ⚔[/color]\n[color=#ffeb99]%s[/color]\n" % text)


# ── Private helpers ────────────────────────────────────────────────────────────

func _scroll_narrative_to_bottom() -> void:
	## Forces the RichTextLabel to scroll to the bottom on the next frame.
	await get_tree().process_frame
	narrative_label.scroll_to_line(narrative_label.get_line_count() - 1)


func _clear_party_slots() -> void:
	for slot: Dictionary in _party_slots:
		slot.root.queue_free()
	_party_slots.clear()


func _clear_action_buttons() -> void:
	for btn: Button in _action_buttons.values():
		btn.queue_free()
	_action_buttons.clear()


func _clear_location_cards() -> void:
	for card: Dictionary in _location_cards:
		card.root.queue_free()
	_location_cards.clear()


func _create_party_slot(index: int, name: String, char_class: String, level: int, hp: int, max_hp: int) -> Dictionary:
	## Builds a party member slot card at runtime.
	var root := PanelContainer.new()
	root.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_slot_card"))

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	root.add_child(vbox)

	# Name label — gold for slot 0 (player), off-white for companions
	var name_label := Label.new()
	name_label.text = name
	if index == 0:
		name_label.add_theme_color_override("font_color", Color("#f5d76e"))
	else:
		name_label.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	name_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(name_label)

	# Class label
	var class_label := Label.new()
	class_label.text = "Lvl %d %s" % [level, char_class]
	class_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	class_label.add_theme_font_size_override("font_size", 11)
	vbox.add_child(class_label)

	# HP bar
	var hp_bar := ProgressBar.new()
	hp_bar.custom_minimum_size = Vector2(0, 6)
	hp_bar.max_value = max(max_hp, 1)
	hp_bar.value = clamp(hp, 0, max_hp)
	hp_bar.show_percentage = false
	hp_bar.add_theme_stylebox_override("background", _get_stylebox("StyleBox_hpbar_bg"))

	# HP bar fill color based on ratio
	var hp_ratio: float = float(hp) / float(max(max_hp, 1))
	var bar_color: Color
	if hp_ratio > 0.5:
		bar_color = COLOR_HP_HEALTHY
	elif hp_ratio > 0.25:
		bar_color = COLOR_HP_HURT
	else:
		bar_color = COLOR_HP_CRITICAL

	var bar_style := StyleBoxFlat.new()
	bar_style.bg_color = bar_color
	bar_style.corner_radius_top_left = 3
	bar_style.corner_radius_top_right = 3
	bar_style.corner_radius_bottom_left = 3
	bar_style.corner_radius_bottom_right = 3
	hp_bar.add_theme_stylebox_override("fill", bar_style)
	vbox.add_child(hp_bar)

	# HP label — "hp / max_hp"
	var hp_label := Label.new()
	hp_label.text = "%d / %d" % [hp, max_hp]
	hp_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	hp_label.add_theme_font_size_override("font_size", 10)
	hp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	vbox.add_child(hp_label)

	return {
		"root": root,
		"name_label": name_label,
		"class_label": class_label,
		"hp_bar": hp_bar,
		"hp_label": hp_label
	}


func _create_location_card(location_id: String, location_name: String, location_type: String, distance: int) -> Dictionary:
	## Builds a location card at runtime for the connected locations panel.
	var root := PanelContainer.new()
	root.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_location_card"))

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	root.add_child(vbox)

	# Location name — gold accent
	var name_label := Label.new()
	name_label.text = location_name
	name_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	name_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(name_label)

	# Location type
	var type_label := Label.new()
	type_label.text = location_type.capitalize()
	type_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	type_label.add_theme_font_size_override("font_size", 11)
	vbox.add_child(type_label)

	# Distance
	var distance_label := Label.new()
	distance_label.text = "%d hop%s away" % [distance, "s" if distance != 1 else ""]
	distance_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	distance_label.add_theme_font_size_override("font_size", 10)
	vbox.add_child(distance_label)

	# Travel button
	var travel_button := Button.new()
	travel_button.text = "Travel"
	travel_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	travel_button.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_travel_normal"))
	travel_button.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_travel_hover"))
	travel_button.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_travel_hover"))
	travel_button.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_travel_normal"))

	travel_button.add_theme_color_override("font_color", Color("#f5d76e"))
	travel_button.add_theme_color_override("font_hover_color", Color("#fffacd"))
	travel_button.add_theme_color_override("font_pressed_color", Color("#ffffff"))
	travel_button.add_theme_font_size_override("font_size", 12)

	travel_button.pressed.connect(func(): _on_travel_button_pressed(location_id))
	vbox.add_child(travel_button)

	return {
		"root": root,
		"location_id": location_id,
		"name_label": name_label,
		"type_label": type_label,
		"distance_label": distance_label,
		"travel_button": travel_button
	}


func _get_stylebox(stylebox_name: String) -> StyleBoxFlat:
	## Retrieve a stylebox resource from the scene's sub_resources.
	## This is a runtime lookup pattern used by CombatUI.gd.
	var scene_state: Node = get_tree().edited_scene_root if Engine.is_editor_hint() else get_tree().current_scene
	if scene_state == null:
		scene_state = self

	# In practice, the scene file defines these as sub_resources.
	# We'll reconstruct them here as needed for runtime button creation.
	# This matches the pattern from CombatUI.gd where styles are built inline.

	match stylebox_name:
		"StyleBox_btn_normal":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.11, 0.11, 0.20, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = Color(0.788, 0.635, 0.153, 0.7)
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
			style.border_color = Color(0.788, 0.635, 0.153, 1.0)
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
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
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

		"StyleBox_travel_normal":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.18, 0.14, 0.04, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = Color(0.788, 0.635, 0.153, 1.0)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 6.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_travel_hover":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.28, 0.22, 0.06, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
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

		"StyleBox_slot_card":
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
			style.content_margin_left = 8.0
			style.content_margin_top = 6.0
			style.content_margin_right = 8.0
			style.content_margin_bottom = 6.0
			return style

		"StyleBox_location_card":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.063, 0.063, 0.118, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = Color(0.35, 0.30, 0.18, 0.6)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 8.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 8.0
			return style

		"StyleBox_hpbar_bg":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.08, 0.08, 0.08, 1.0)
			style.corner_radius_top_left = 3
			style.corner_radius_top_right = 3
			style.corner_radius_bottom_left = 3
			style.corner_radius_bottom_right = 3
			return style

		_:
			push_warning("ExplorationUI._get_stylebox: Unknown stylebox '%s'" % stylebox_name)
			return StyleBoxFlat.new()


# ── Dialogue panel helpers ────────────────────────────────────────────────────

func _create_dialogue_panel_if_needed() -> void:
	## Creates the dialogue panel UI overlay if it doesn't exist yet.
	if _dialogue_panel != null:
		return

	_dialogue_panel = VBoxContainer.new()
	_dialogue_panel.add_theme_constant_override("separation", 8)
	_dialogue_panel.hide()

	# Header: NPC name + role
	var header := RichTextLabel.new()
	header.bbcode_enabled = true
	header.fit_content = true
	header.custom_minimum_size = Vector2(0, 40)
	header.scroll_active = false
	_dialogue_panel.add_child(header)

	# Quick actions row
	var quick_actions := HBoxContainer.new()
	quick_actions.add_theme_constant_override("separation", 6)

	var btn_quests := Button.new()
	btn_quests.text = "Ask about quests"
	btn_quests.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(btn_quests, "normal")
	btn_quests.pressed.connect(_on_dialogue_quick_action.bind("ask_quests"))
	quick_actions.add_child(btn_quests)

	var btn_wares := Button.new()
	btn_wares.text = "Browse wares"
	btn_wares.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(btn_wares, "normal")
	btn_wares.pressed.connect(_on_dialogue_quick_action.bind("browse_wares"))
	quick_actions.add_child(btn_wares)

	var btn_goodbye := Button.new()
	btn_goodbye.text = "Say goodbye"
	btn_goodbye.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(btn_goodbye, "normal")
	btn_goodbye.pressed.connect(_on_dialogue_goodbye)
	quick_actions.add_child(btn_goodbye)

	_dialogue_panel.add_child(quick_actions)

	# Text input row
	var input_row := HBoxContainer.new()
	input_row.add_theme_constant_override("separation", 6)

	_dialogue_input = LineEdit.new()
	_dialogue_input.placeholder_text = "Type your response..."
	_dialogue_input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_dialogue_input.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	_dialogue_input.add_theme_font_size_override("font_size", 13)
	_dialogue_input.text_submitted.connect(_on_dialogue_text_submitted)
	input_row.add_child(_dialogue_input)

	_dialogue_send_btn = Button.new()
	_dialogue_send_btn.text = "Send"
	_dialogue_send_btn.custom_minimum_size = Vector2(80, 0)
	_apply_button_style(_dialogue_send_btn, "normal")
	_dialogue_send_btn.pressed.connect(_on_dialogue_send_pressed)
	input_row.add_child(_dialogue_send_btn)

	_dialogue_panel.add_child(input_row)

	# Add to actions margin container
	actions_margin.add_child(_dialogue_panel)


func _on_dialogue_text_submitted(text: String) -> void:
	## Called when player presses Enter in the dialogue input field.
	if text.strip_edges().is_empty():
		return
	dialogue_submitted.emit(text)
	_dialogue_input.clear()


func _on_dialogue_send_pressed() -> void:
	## Called when player clicks the Send button.
	var text: String = _dialogue_input.text.strip_edges()
	if text.is_empty():
		return
	dialogue_submitted.emit(text)
	_dialogue_input.clear()


func _on_dialogue_quick_action(action: String) -> void:
	## Quick action buttons in dialogue panel.
	match action:
		"ask_quests":
			dialogue_submitted.emit("Do you have any quests for me?")
		"browse_wares":
			dialogue_submitted.emit("Can I see your wares?")


func _on_dialogue_goodbye() -> void:
	## Called when player clicks "Say goodbye" in dialogue panel.
	dialogue_end_requested.emit()


# ── Shop panel helpers ─────────────────────────────────────────────────────────

func _create_shop_panel_if_needed() -> void:
	## Creates the shop panel UI overlay if it doesn't exist yet.
	if _shop_panel != null:
		return

	_shop_panel = VBoxContainer.new()
	_shop_panel.add_theme_constant_override("separation", 8)
	_shop_panel.hide()

	# Header: Shop name
	var header := Label.new()
	header.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	header.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	header.add_theme_font_size_override("font_size", 16)
	_shop_panel.add_child(header)

	# Gold display
	_shop_gold_label = Label.new()
	_shop_gold_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_shop_gold_label.add_theme_color_override("font_color", Color("#f5d76e"))
	_shop_gold_label.add_theme_font_size_override("font_size", 13)
	_shop_panel.add_child(_shop_gold_label)

	# Scrollable item list
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(0, 200)
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL

	_shop_items_vbox = VBoxContainer.new()
	_shop_items_vbox.add_theme_constant_override("separation", 4)
	scroll.add_child(_shop_items_vbox)
	_shop_panel.add_child(scroll)

	# Leave shop button
	var leave_btn := Button.new()
	leave_btn.text = "Leave Shop"
	leave_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(leave_btn, "normal")
	leave_btn.pressed.connect(_on_shop_leave_pressed)
	_shop_panel.add_child(leave_btn)

	# Add to actions margin container
	actions_margin.add_child(_shop_panel)


func _rebuild_shop_item_list(items: Array, player_gold: int) -> void:
	## Rebuilds the shop item list from the given item data.
	if _shop_items_vbox == null:
		return

	# Clear existing items
	for child: Node in _shop_items_vbox.get_children():
		child.queue_free()

	if items.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No items available."
		empty_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_shop_items_vbox.add_child(empty_label)
		return

	# Build item cards
	for item_data: Dictionary in items:
		var item_name: String = item_data.get("name", "Unknown")
		var price: int = item_data.get("price", 0)
		var quantity: int = item_data.get("quantity", -1)
		var description: String = item_data.get("description", "")

		var card := _create_shop_item_card(item_name, price, quantity, description, player_gold)
		_shop_items_vbox.add_child(card)


func _create_shop_item_card(item_name: String, price: int, quantity: int, description: String, player_gold: int) -> PanelContainer:
	## Creates a single shop item card with name, price, and buy button.
	var root := PanelContainer.new()
	root.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_slot_card"))

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 8)
	root.add_child(hbox)

	# Left side: item info
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var name_label := Label.new()
	name_label.text = item_name
	name_label.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
	name_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(name_label)

	var price_label := Label.new()
	var stock_text: String = " (∞ in stock)" if quantity == -1 else " (%d in stock)" % quantity
	price_label.text = "%d gp%s" % [price, stock_text]
	price_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	price_label.add_theme_font_size_override("font_size", 11)
	vbox.add_child(price_label)

	if !description.is_empty():
		var desc_label := Label.new()
		desc_label.text = description
		desc_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		desc_label.add_theme_font_size_override("font_size", 10)
		desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		vbox.add_child(desc_label)

	hbox.add_child(vbox)

	# Right side: buy button
	var buy_btn := Button.new()
	buy_btn.text = "Buy"
	buy_btn.custom_minimum_size = Vector2(60, 0)
	_apply_button_style(buy_btn, "normal")

	# Disable if can't afford or out of stock
	if player_gold < price or quantity == 0:
		buy_btn.disabled = true

	buy_btn.pressed.connect(func(): _on_shop_buy_pressed(item_name, 1))
	hbox.add_child(buy_btn)

	return root


func _on_shop_buy_pressed(item_name: String, quantity: int) -> void:
	## Called when player clicks Buy on a shop item.
	shop_buy_requested.emit(item_name, quantity)


func _on_shop_leave_pressed() -> void:
	## Called when player clicks Leave Shop.
	shop_close_requested.emit()


# ── Status label helpers ───────────────────────────────────────────────────────

func _create_status_label() -> void:
	## Creates the status label if it doesn't exist yet.
	if _status_label != null:
		return

	_status_label = Label.new()
	_status_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status_label.add_theme_color_override("font_color", Color("#b8b0a0"))
	_status_label.add_theme_font_size_override("font_size", 12)
	_status_label.hide()

	# Add below the narrative panel
	var narrative_parent: MarginContainer = narrative_label.get_parent().get_parent() as MarginContainer
	if narrative_parent:
		var parent_vbox: VBoxContainer = narrative_parent.get_parent() as VBoxContainer
		if parent_vbox:
			parent_vbox.add_child(_status_label)
			parent_vbox.move_child(_status_label, parent_vbox.get_child_count() - 2)


# ── Actions area visibility helpers ────────────────────────────────────────────

func _hide_actions_area() -> void:
	## Hides the normal actions VBox to make room for dialogue/shop panels.
	_original_actions_visible = actions_vbox.visible
	actions_vbox.hide()


func _show_actions_area() -> void:
	## Restores the normal actions VBox visibility.
	actions_vbox.visible = _original_actions_visible


func _apply_button_style(btn: Button, category: String) -> void:
	## Applies consistent button styling matching the existing action buttons.
	if category == "travel":
		btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_travel_normal"))
		btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_travel_hover"))
		btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_travel_hover"))
		btn.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_travel_normal"))
		btn.add_theme_color_override("font_color", Color("#f5d76e"))
		btn.add_theme_color_override("font_hover_color", Color("#fffacd"))
		btn.add_theme_color_override("font_pressed_color", Color("#ffffff"))
	else:
		btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_normal"))
		btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
		btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
		btn.add_theme_stylebox_override("focus", _get_stylebox("StyleBox_btn_hover"))
		btn.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
		btn.add_theme_color_override("font_hover_color", Color("#f5d76e"))
		btn.add_theme_color_override("font_pressed_color", Color("#fffacd"))

	btn.add_theme_font_size_override("font_size", 13)


# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_action_button_pressed(action_id: String) -> void:
	## Called when an action button is clicked. Emit action_selected with empty data.
	action_selected.emit(action_id, {})


func _on_travel_button_pressed(location_id: String) -> void:
	## Called when a travel button is clicked. Emit travel_selected signal.
	travel_selected.emit(location_id)


func _on_npc_button_pressed(npc_id: String) -> void:
	## Called when an NPC selection button is clicked. Emit npc_selected signal.
	npc_selected.emit(npc_id)
