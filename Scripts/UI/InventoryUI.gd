## InventoryUI.gd
## Controller for the inventory and equipment management overlay.
## Displays character equipment slots, carried items, item details, and stat previews.
## Attach to the root InventoryUI Control node in InventoryUI.tscn.

extends Control

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the player clicks an item in the inventory list or equipment slots.
signal item_selected(item_name: String)

## Emitted when the player clicks Equip on an item. If the item can go in multiple
## slots, this is called after the slot selection popup.
signal equip_requested(item_name: String, slot: String)

## Emitted when the player clicks Unequip on an equipped item.
signal unequip_requested(slot: String)

## Emitted when the player clicks Use on a consumable item.
signal use_item_requested(item_name: String)

## Emitted when the player clicks Drop on an item.
signal drop_item_requested(item_name: String)

## Emitted when the player switches party member tabs.
signal party_member_selected(index: int)

## Emitted when the player requests to attune a magic item.
signal attune_requested(item_name: String)

## Emitted when the player requests to unattune a magic item.
signal unattune_requested(item_name: String)

## Emitted when the player closes the inventory overlay.
signal close_requested()

# ── Color constants ────────────────────────────────────────────────────────────
## Centralized design tokens — match dark fantasy palette.

const COLOR_TEXT_DEFAULT: Color    = Color("#eee8d5")   # warm off-white — body text
const COLOR_TEXT_DIM: Color        = Color("#b8b0a0")   # muted — secondary labels
const COLOR_TEXT_DISABLED: Color   = Color("#595650")   # disabled text
const COLOR_ACCENT_GOLD: Color     = Color("#c9a227")   # gold — headings, active slots
const COLOR_GOLD_HOVER: Color      = Color("#f2d238")   # bright gold hover
const COLOR_BORDER_MUTED: Color    = Color(0.35, 0.30, 0.18, 0.7)
const COLOR_BORDER_GOLD: Color     = Color(0.788, 0.635, 0.153, 1.0)

# Rarity colors
const RARITY_COLORS: Dictionary = {
	"Common": Color("#b8b0a0"),
	"Uncommon": Color("#2ecc71"),
	"Rare": Color("#3498db"),
	"Very Rare": Color("#9b59b6"),
	"Legendary": Color("#f39c12")
}

# Stat preview change colors
const COLOR_STAT_INCREASE: Color = Color("#2ecc71")  # green
const COLOR_STAT_DECREASE: Color = Color("#e74c3c")  # red
const COLOR_STAT_NEUTRAL: Color  = Color("#b8b0a0")  # gray

# ── Node references ────────────────────────────────────────────────────────────

@onready var close_button:        Button       = %CloseButton
@onready var party_tabs_hbox:     HBoxContainer = %PartyTabsContainer
@onready var attunement_label:    Label        = %AttunementLabel
@onready var slots_vbox:          VBoxContainer = %SlotsVBox
@onready var items_vbox:          VBoxContainer = %ItemsVBox
@onready var details_vbox:        VBoxContainer = %DetailsVBox
@onready var placeholder_label:   Label        = %PlaceholderLabel
@onready var weight_label:        Label        = %WeightLabel
@onready var gold_label:          Label        = %GoldLabel

# ── Runtime state ──────────────────────────────────────────────────────────────

# Currently selected item name (for highlighting).
var _selected_item: String = ""

# Currently selected party member index (for tab highlighting).
var _active_party_index: int = 0

# Party tabs (buttons) — rebuilt when update_party_tabs() is called.
var _party_tab_buttons: Array[Button] = []

# Equipment slot buttons — rebuilt when update_equipment_slots() is called.
# Dictionary keyed by slot name (e.g. "MainHand") to Button node.
var _slot_buttons: Dictionary = {}

# Inventory item cards — rebuilt when update_inventory_list() is called.
# Array of Dictionaries with {name, button, panel}.
var _item_cards: Array[Dictionary] = []

# Details panel widgets — created dynamically when showing item details.
var _details_widgets: Dictionary = {}

# Slot selection popup — shown when an item can go in multiple slots.
var _slot_selection_popup: PopupPanel = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	close_button.pressed.connect(_on_close_button_pressed)
	_initialize_placeholder_state()


func _input(event: InputEvent) -> void:
	## Allow Escape key to close the inventory overlay.
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		close_requested.emit()


func _initialize_placeholder_state() -> void:
	## Sets up an empty-but-valid UI state before data arrives.
	placeholder_label.show()
	weight_label.text = "Weight: 0 / 0 lb"
	gold_label.text = "Gold: 0 gp"


# ══════════════════════════════════════════════════════════════════════════════
# PUBLIC API — called by InventoryBridge via Node.Call()
# ══════════════════════════════════════════════════════════════════════════════

## Updates the party member tabs at the top of the screen.
## party_data is an Array of Dictionaries with: {name: String, class_name: String, level: int}
## active_index is the currently selected party member.
func update_party_tabs(party: Array, active_index: int) -> void:
	_active_party_index = active_index
	_clear_party_tabs()

	for i in range(party.size()):
		var member: Dictionary = party[i]
		var name: String = member.get("name", "Unknown")
		var char_class: String = member.get("class_name", "")
		var level: int = member.get("level", 1)

		var btn := Button.new()
		btn.text = "%s (Lvl %d %s)" % [name, level, char_class]
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		# Style based on active state.
		if i == active_index:
			_apply_button_style(btn, "party_active")
		else:
			_apply_button_style(btn, "party_normal")

		btn.pressed.connect(func(): _on_party_tab_pressed(i))

		party_tabs_hbox.add_child(btn)
		_party_tab_buttons.append(btn)


## Updates the equipment slot list in the left column.
## slots is an Array of Dictionaries with:
## {slot: String, display_name: String, item_name: String, is_empty: bool}
func update_equipment_slots(slots: Array) -> void:
	_clear_equipment_slots()

	for slot_data: Dictionary in slots:
		var slot_key: String = slot_data.get("slot", "")
		var display_name: String = slot_data.get("display_name", "")
		var item_name: String = slot_data.get("item_name", "")
		var is_empty: bool = slot_data.get("is_empty", true)

		var btn := _create_equipment_slot_button(slot_key, display_name, item_name, is_empty)
		slots_vbox.add_child(btn)
		_slot_buttons[slot_key] = btn


## Updates the inventory item list in the center column.
## items is an Array of Dictionaries with:
## {name: String, quantity: int, weight: float, rarity: String, is_equippable: bool, is_consumable: bool}
func update_inventory_list(items: Array) -> void:
	_clear_inventory_items()

	if items.is_empty():
		var empty_label := Label.new()
		empty_label.text = "Inventory is empty."
		empty_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		empty_label.add_theme_font_size_override("font_size", 13)
		empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		items_vbox.add_child(empty_label)
		return

	for item_data: Dictionary in items:
		var item_name: String = item_data.get("name", "Unknown")
		var quantity: int = item_data.get("quantity", 1)
		var weight: float = item_data.get("weight", 0.0)
		var rarity: String = item_data.get("rarity", "Common")

		var card := _create_inventory_item_card(item_name, quantity, weight, rarity)
		items_vbox.add_child(card.panel)
		_item_cards.append(card)


## Updates the item details panel in the right column.
## item is a Dictionary with: {name, type, rarity, description, damage, armor_class,
## properties, weight, cost}
func update_item_details(item: Dictionary) -> void:
	placeholder_label.hide()
	_clear_details_panel()

	var name: String = item.get("name", "Unknown Item")
	var item_type: String = item.get("type", "")
	var rarity: String = item.get("rarity", "Common")
	var description: String = item.get("description", "")
	var damage: String = item.get("damage", "")
	var armor_class: String = item.get("armor_class", "")
	var properties: String = item.get("properties", "")
	var weight: float = item.get("weight", 0.0)
	var cost: int = item.get("cost", 0)

	# Item name (large, gold)
	var name_label := Label.new()
	name_label.text = name
	name_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	name_label.add_theme_font_size_override("font_size", 20)
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	details_vbox.add_child(name_label)
	_details_widgets["name_label"] = name_label

	# Type and rarity
	var type_label := Label.new()
	type_label.text = "%s — %s" % [item_type, rarity]
	var rarity_color: Color = RARITY_COLORS.get(rarity, COLOR_TEXT_DIM)
	type_label.add_theme_color_override("font_color", rarity_color)
	type_label.add_theme_font_size_override("font_size", 14)
	details_vbox.add_child(type_label)
	_details_widgets["type_label"] = type_label

	# Attunement badge
	var requires_attunement: bool = item.get("requires_attunement", false)
	if requires_attunement:
		var is_attuned: bool = item.get("is_attuned", false)
		var attune_label := Label.new()
		attune_label.add_theme_font_size_override("font_size", 13)
		if is_attuned:
			attune_label.text = "[Attuned]"
			attune_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
		else:
			var restriction: String = item.get("attunement_restriction", "")
			if restriction.is_empty():
				attune_label.text = "Requires Attunement"
			else:
				attune_label.text = "Requires Attunement (by %s)" % restriction
			attune_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		details_vbox.add_child(attune_label)
		_details_widgets["attune_label"] = attune_label

	# Description
	if !description.is_empty():
		var desc_label := Label.new()
		desc_label.text = description
		desc_label.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
		desc_label.add_theme_font_size_override("font_size", 13)
		desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		details_vbox.add_child(desc_label)
		_details_widgets["description_label"] = desc_label

	# Stats section
	var stats_text := ""
	if !damage.is_empty():
		stats_text += "Damage: %s\n" % damage
	if !armor_class.is_empty():
		stats_text += "Armor Class: %s\n" % armor_class
	if !properties.is_empty():
		stats_text += "Properties: %s\n" % properties
	stats_text += "Weight: %.1f lb\n" % weight
	stats_text += "Value: %d gp" % cost

	if !stats_text.is_empty():
		var stats_label := Label.new()
		stats_label.text = stats_text.strip_edges()
		stats_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		stats_label.add_theme_font_size_override("font_size", 12)
		stats_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		details_vbox.add_child(stats_label)
		_details_widgets["stats_label"] = stats_label

	# Spacer before stat preview and action buttons
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 16)
	details_vbox.add_child(spacer)

	# Stat preview section placeholder (filled by update_stats_preview)
	var preview_vbox := VBoxContainer.new()
	preview_vbox.add_theme_constant_override("separation", 4)
	details_vbox.add_child(preview_vbox)
	_details_widgets["preview_vbox"] = preview_vbox

	# Action buttons (Equip, Unequip, Use, Drop)
	var actions_hbox := HBoxContainer.new()
	actions_hbox.add_theme_constant_override("separation", 8)
	details_vbox.add_child(actions_hbox)
	_details_widgets["actions_hbox"] = actions_hbox

	_rebuild_action_buttons(item)


## Updates the stat preview section (shows AC/damage changes if equipping).
## preview is a Dictionary with: {current_ac, new_ac, current_damage, new_damage}
## Use null or empty dict to clear the preview.
func update_stats_preview(preview: Dictionary) -> void:
	if !_details_widgets.has("preview_vbox"):
		return

	var preview_vbox: VBoxContainer = _details_widgets["preview_vbox"]

	# Clear existing preview content.
	for child in preview_vbox.get_children():
		child.queue_free()

	if preview.is_empty():
		return

	var current_ac: int = preview.get("current_ac", 0)
	var new_ac: int = preview.get("new_ac", 0)
	var current_damage: String = preview.get("current_damage", "")
	var new_damage: String = preview.get("new_damage", "")

	var preview_label := Label.new()
	preview_label.text = "STAT PREVIEW"
	preview_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	preview_label.add_theme_font_size_override("font_size", 14)
	preview_vbox.add_child(preview_label)

	# AC change
	if new_ac != 0 and new_ac != current_ac:
		var ac_change := new_ac - current_ac
		var ac_color: Color = COLOR_STAT_INCREASE if ac_change > 0 else COLOR_STAT_DECREASE
		var ac_text := "AC: %d → %d (%+d)" % [current_ac, new_ac, ac_change]

		var ac_label := Label.new()
		ac_label.text = ac_text
		ac_label.add_theme_color_override("font_color", ac_color)
		ac_label.add_theme_font_size_override("font_size", 12)
		preview_vbox.add_child(ac_label)

	# Damage change (string comparison, show as text)
	if !new_damage.is_empty() and new_damage != current_damage:
		var dmg_text := "Damage: %s → %s" % [current_damage if !current_damage.is_empty() else "None", new_damage]

		var dmg_label := Label.new()
		dmg_label.text = dmg_text
		dmg_label.add_theme_color_override("font_color", COLOR_STAT_NEUTRAL)
		dmg_label.add_theme_font_size_override("font_size", 12)
		preview_vbox.add_child(dmg_label)


## Updates the gold and weight display at the bottom of the inventory column.
func update_gold_weight(gold: int, weight: float, capacity: float) -> void:
	gold_label.text = "Gold: %d gp" % gold

	var weight_color: Color = COLOR_TEXT_DEFAULT
	if weight > capacity:
		weight_color = Color("#e74c3c")  # red if encumbered

	weight_label.text = "Weight: %.1f / %.1f lb" % [weight, capacity]
	weight_label.add_theme_color_override("font_color", weight_color)


## Updates the attunement slot counter (e.g. "Attunement: 1/3").
func update_attunement_counter(used: int, max_slots: int) -> void:
	attunement_label.text = "Attunement: %d/%d" % [used, max_slots]
	if used >= max_slots:
		attunement_label.add_theme_color_override("font_color", Color("#e74c3c"))
	else:
		attunement_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)


## Clears the item details panel and shows the placeholder message.
func clear_item_details() -> void:
	_clear_details_panel()
	placeholder_label.show()
	_selected_item = ""


## Shows a slot selection popup when an item can be equipped in multiple slots.
## slots is an Array of Strings (slot keys like "MainHand", "OffHand").
func show_slot_selection(slots: Array) -> void:
	if _slot_selection_popup != null:
		_slot_selection_popup.queue_free()

	_slot_selection_popup = PopupPanel.new()
	_slot_selection_popup.size = Vector2(300, 0)
	_slot_selection_popup.popup_window = false
	_slot_selection_popup.exclusive = true

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 8)
	_slot_selection_popup.add_child(vbox)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_right", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	vbox.add_child(margin)

	var inner_vbox := VBoxContainer.new()
	inner_vbox.add_theme_constant_override("separation", 8)
	margin.add_child(inner_vbox)

	var title := Label.new()
	title.text = "Select Equipment Slot"
	title.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	title.add_theme_font_size_override("font_size", 16)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	inner_vbox.add_child(title)

	for slot_key: String in slots:
		var display_name: String = _get_slot_display_name(slot_key)
		var btn := Button.new()
		btn.text = display_name
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_apply_button_style(btn, "normal")
		btn.pressed.connect(func(): _on_slot_selection_confirmed(slot_key))
		inner_vbox.add_child(btn)

	add_child(_slot_selection_popup)
	_slot_selection_popup.popup_centered()


# ══════════════════════════════════════════════════════════════════════════════
# PRIVATE HELPERS — UI Building
# ══════════════════════════════════════════════════════════════════════════════

func _create_equipment_slot_button(slot_key: String, display_name: String, item_name: String, is_empty: bool) -> Button:
	## Builds a single equipment slot button showing slot name and equipped item (or "Empty").
	var btn := Button.new()
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.custom_minimum_size = Vector2(0, 40)

	var slot_text := display_name
	if is_empty:
		slot_text += ": Empty"
	else:
		slot_text += ": %s" % item_name

	btn.text = slot_text

	# Style based on empty state
	if is_empty:
		_apply_button_style(btn, "slot_empty")
	else:
		_apply_button_style(btn, "slot_equipped")

	btn.pressed.connect(func(): _on_equipment_slot_pressed(slot_key, item_name, is_empty))

	return btn


func _create_inventory_item_card(item_name: String, quantity: int, weight: float, rarity: String) -> Dictionary:
	## Builds a single inventory item card showing name, quantity, and weight.
	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_item_card"))

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	panel.add_child(hbox)

	# Icon placeholder (future: texture icon)
	var icon_rect := ColorRect.new()
	icon_rect.custom_minimum_size = Vector2(32, 32)
	icon_rect.color = Color(0.2, 0.2, 0.25, 1.0)
	hbox.add_child(icon_rect)

	# Item info VBox
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(vbox)

	var name_label := Label.new()
	name_label.text = item_name
	var rarity_color: Color = RARITY_COLORS.get(rarity, COLOR_TEXT_DEFAULT)
	name_label.add_theme_color_override("font_color", rarity_color)
	name_label.add_theme_font_size_override("font_size", 13)
	vbox.add_child(name_label)

	var details_label := Label.new()
	var qty_text: String = "x%d" % quantity if quantity > 1 else ""
	var weight_text := "%.1f lb" % (weight * quantity)
	details_label.text = "%s — %s" % [qty_text, weight_text] if quantity > 1 else weight_text
	details_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	details_label.add_theme_font_size_override("font_size", 11)
	vbox.add_child(details_label)

	# Make the panel clickable
	var btn := Button.new()
	btn.flat = true
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.size_flags_vertical = Control.SIZE_EXPAND_FILL
	btn.mouse_filter = Control.MOUSE_FILTER_PASS
	btn.pressed.connect(func(): _on_inventory_item_pressed(item_name, panel))
	panel.add_child(btn)
	panel.move_child(btn, 0)  # Place button behind content for clickable background

	return {"name": item_name, "button": btn, "panel": panel}


func _rebuild_action_buttons(item: Dictionary) -> void:
	## Rebuilds the action buttons (Equip, Unequip, Use, Drop) in the details panel.
	if !_details_widgets.has("actions_hbox"):
		return

	var actions_hbox: HBoxContainer = _details_widgets["actions_hbox"]

	# Clear existing buttons
	for child in actions_hbox.get_children():
		child.queue_free()

	var is_equippable: bool = item.get("is_equippable", false)
	var is_consumable: bool = item.get("is_consumable", false)
	var is_equipped: bool = item.get("is_equipped", false)
	var item_name: String = item.get("name", "")

	# Equip button (only for equippable items not currently equipped)
	if is_equippable and !is_equipped:
		var equip_btn := Button.new()
		equip_btn.text = "Equip"
		equip_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_apply_button_style(equip_btn, "gold")
		equip_btn.pressed.connect(func(): _on_equip_button_pressed(item_name))
		actions_hbox.add_child(equip_btn)

	# Unequip button (only for equipped items)
	if is_equipped:
		var unequip_btn := Button.new()
		unequip_btn.text = "Unequip"
		unequip_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_apply_button_style(unequip_btn, "normal")
		unequip_btn.pressed.connect(func(): _on_unequip_button_pressed(item.get("equipped_slot", "")))
		actions_hbox.add_child(unequip_btn)

	# Attune/Unattune buttons (for items that require attunement)
	var requires_attunement: bool = item.get("requires_attunement", false)
	var is_attuned: bool = item.get("is_attuned", false)
	var can_attune: bool = item.get("can_attune", false)

	if requires_attunement:
		if is_attuned:
			var unattune_btn := Button.new()
			unattune_btn.text = "Unattune"
			unattune_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			_apply_button_style(unattune_btn, "normal")
			unattune_btn.pressed.connect(func(): _on_unattune_button_pressed(item_name))
			actions_hbox.add_child(unattune_btn)
		elif can_attune:
			var attune_btn := Button.new()
			attune_btn.text = "Attune"
			attune_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			_apply_button_style(attune_btn, "gold")
			attune_btn.pressed.connect(func(): _on_attune_button_pressed(item_name))
			actions_hbox.add_child(attune_btn)

	# Use button (only for consumables)
	if is_consumable:
		var use_btn := Button.new()
		use_btn.text = "Use"
		use_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_apply_button_style(use_btn, "gold")
		use_btn.pressed.connect(func(): _on_use_button_pressed(item_name))
		actions_hbox.add_child(use_btn)

	# Drop button (always available)
	var drop_btn := Button.new()
	drop_btn.text = "Drop"
	drop_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_apply_button_style(drop_btn, "normal")
	drop_btn.pressed.connect(func(): _on_drop_button_pressed(item_name))
	actions_hbox.add_child(drop_btn)


# ── Cleanup helpers ────────────────────────────────────────────────────────────

func _clear_party_tabs() -> void:
	for btn in _party_tab_buttons:
		btn.queue_free()
	_party_tab_buttons.clear()


func _clear_equipment_slots() -> void:
	for btn: Button in _slot_buttons.values():
		btn.queue_free()
	_slot_buttons.clear()


func _clear_inventory_items() -> void:
	for card in _item_cards:
		card.panel.queue_free()
	_item_cards.clear()


func _clear_details_panel() -> void:
	for widget: Variant in _details_widgets.values():
		if widget is Node:
			widget.queue_free()
	_details_widgets.clear()


# ── Styling helpers ────────────────────────────────────────────────────────────

func _apply_button_style(btn: Button, style_type: String) -> void:
	## Applies consistent button styling based on category.
	match style_type:
		"gold":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_gold_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_gold_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_gold_pressed"))
			btn.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
			btn.add_theme_color_override("font_hover_color", COLOR_GOLD_HOVER)
		"normal":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
			btn.add_theme_color_override("font_color", COLOR_TEXT_DEFAULT)
			btn.add_theme_color_override("font_hover_color", COLOR_ACCENT_GOLD)
		"slot_empty":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_slot_empty"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
			btn.add_theme_color_override("font_color", COLOR_TEXT_DISABLED)
			btn.add_theme_color_override("font_hover_color", COLOR_TEXT_DIM)
		"slot_equipped":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_slot_equipped"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_pressed"))
			btn.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
			btn.add_theme_color_override("font_hover_color", COLOR_GOLD_HOVER)
		"party_active":
			btn.add_theme_stylebox_override("normal", _get_stylebox("StyleBox_btn_gold_normal"))
			btn.add_theme_stylebox_override("hover", _get_stylebox("StyleBox_btn_gold_hover"))
			btn.add_theme_stylebox_override("pressed", _get_stylebox("StyleBox_btn_gold_pressed"))
			btn.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
			btn.add_theme_color_override("font_hover_color", COLOR_GOLD_HOVER)
		"party_normal":
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

		"StyleBox_slot_empty":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.08, 0.08, 0.12, 1.0)
			style.border_width_left = 1
			style.border_width_top = 1
			style.border_width_right = 1
			style.border_width_bottom = 1
			style.border_color = Color(0.25, 0.22, 0.15, 0.5)
			style.corner_radius_top_left = 4
			style.corner_radius_top_right = 4
			style.corner_radius_bottom_left = 4
			style.corner_radius_bottom_right = 4
			style.content_margin_left = 10.0
			style.content_margin_top = 8.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 8.0
			return style

		"StyleBox_slot_equipped":
			var style := StyleBoxFlat.new()
			style.bg_color = Color(0.14, 0.12, 0.08, 1.0)
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
			style.content_margin_top = 8.0
			style.content_margin_right = 10.0
			style.content_margin_bottom = 8.0
			return style

		"StyleBox_item_card":
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

		"StyleBox_item_card_selected":
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
			style.content_margin_left = 8.0
			style.content_margin_top = 6.0
			style.content_margin_right = 8.0
			style.content_margin_bottom = 6.0
			return style

		_:
			push_warning("InventoryUI._get_stylebox: Unknown stylebox '%s'" % stylebox_name)
			return StyleBoxFlat.new()


func _get_slot_display_name(slot_key: String) -> String:
	## Convert slot key to display name (matches EquipmentSlotResolver logic).
	match slot_key:
		"MainHand": return "Main Hand"
		"OffHand": return "Off Hand"
		"Armor": return "Armor"
		"Ranged": return "Ranged"
		"Head": return "Head"
		"Neck": return "Neck"
		"Ring1": return "Ring 1"
		"Ring2": return "Ring 2"
		"Cloak": return "Cloak"
		"Boots": return "Boots"
		"Gloves": return "Gloves"
		"Belt": return "Belt"
		_: return slot_key


# ══════════════════════════════════════════════════════════════════════════════
# SIGNAL HANDLERS
# ══════════════════════════════════════════════════════════════════════════════

func _on_close_button_pressed() -> void:
	close_requested.emit()


func _on_party_tab_pressed(index: int) -> void:
	party_member_selected.emit(index)


func _on_equipment_slot_pressed(slot_key: String, item_name: String, is_empty: bool) -> void:
	if is_empty:
		return

	# Show the equipped item's details
	item_selected.emit(item_name)


func _on_inventory_item_pressed(item_name: String, panel: PanelContainer) -> void:
	# Highlight selected item
	_selected_item = item_name

	# Update all item card borders to show selection
	for card in _item_cards:
		if card.name == item_name:
			card.panel.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_item_card_selected"))
		else:
			card.panel.add_theme_stylebox_override("panel", _get_stylebox("StyleBox_item_card"))

	item_selected.emit(item_name)


func _on_equip_button_pressed(item_name: String) -> void:
	# Note: If the item can go in multiple slots, InventoryBridge should call
	# show_slot_selection() instead of directly calling equip_requested.
	# For now, emit with empty slot and let C# handle multi-slot logic.
	equip_requested.emit(item_name, "")


func _on_unequip_button_pressed(slot_key: String) -> void:
	unequip_requested.emit(slot_key)


func _on_use_button_pressed(item_name: String) -> void:
	use_item_requested.emit(item_name)


func _on_drop_button_pressed(item_name: String) -> void:
	drop_item_requested.emit(item_name)


func _on_attune_button_pressed(item_name: String) -> void:
	attune_requested.emit(item_name)


func _on_unattune_button_pressed(item_name: String) -> void:
	unattune_requested.emit(item_name)


func _on_slot_selection_confirmed(slot_key: String) -> void:
	if _slot_selection_popup:
		_slot_selection_popup.queue_free()
		_slot_selection_popup = null

	equip_requested.emit(_selected_item, slot_key)
