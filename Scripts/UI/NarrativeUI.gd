## NarrativeUI.gd
## Controller for the main narrative/exploration interface.
## Handles all text display, party status updates, action input, and status messaging.
## Attach to the root NarrativeUI Control node in NarrativeUI.tscn.

extends Control

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the player submits free-form text via the input field or Send button.
signal action_submitted(text: String)

## Emitted when the player clicks one of the four suggested action buttons.
## index corresponds to the position in the last set_suggested_actions() call (0-3).
signal suggested_action_selected(index: int)

## Emitted when the player clicks the Menu button in the top bar.
## SceneManager listens for this to navigate back to the main menu.
signal menu_pressed

## Emitted when the player clicks the Inventory button or presses I.
## GameLoop listens for this to open the inventory overlay.
signal inventory_requested

## Emitted when the player clicks the Journal button or presses J.
## GameLoop listens for this to open the journal overlay.
signal journal_requested

## Emitted when the player clicks the Save button or presses F5.
## GameLoop listens for this to trigger a save via SaveLoadBridge.
signal save_requested

# ── Color constants ────────────────────────────────────────────────────────────
## Centralized design tokens — match the scene's PanelContainer stylebox colors.

const COLOR_TEXT_DEFAULT    := Color("#eee8d5")   # warm off-white — body text
const COLOR_TEXT_DIM        := Color("#b8b0a0")   # muted — secondary labels
const COLOR_ACCENT_GOLD     := Color("#c9a227")   # gold — headings, companion names
const COLOR_HP_HEALTHY      := Color("#2ecc71")   # green — HP bars above 50%
const COLOR_HP_HURT         := Color("#f39c12")   # amber — HP bars 25–50%
const COLOR_HP_CRITICAL     := Color("#e74c3c")   # red — HP bars below 25%
const COLOR_DICE_RESULT     := Color("#a8dadc")   # pale cyan — dice roll entries
const COLOR_NARRATOR        := Color("#eee8d5")   # same as default; kept for clarity

# Relationship tier display: maps score ranges to label strings.
# Checked in order — first match wins.
const RELATIONSHIP_TIERS := [
	[-100, -61, "Hostile"],
	[-60,  -21, "Cold"],
	[-20,   20, "Neutral"],
	[21,    60, "Friendly"],
	[61,    85, "Close"],
	[86,   100, "Devoted"],
]

# ── Node references ────────────────────────────────────────────────────────────
## Populated in _ready(). All paths are relative to the scene root.

@onready var narrative_label:    RichTextLabel = %NarrativeLabel
@onready var input_field:        LineEdit       = %PlayerInput
@onready var send_button:        Button         = %SendButton
@onready var status_label:       Label          = %StatusLabel
@onready var menu_button:        Button         = %MenuButton
@onready var inventory_button:  Button         = %InventoryButton
@onready var journal_button:    Button         = %JournalButton
@onready var save_button:      Button         = %SaveButton

# Suggested action buttons — indexed 0-3.
@onready var action_buttons: Array[Button] = [
	%ActionButton0,
	%ActionButton1,
	%ActionButton2,
	%ActionButton3,
]

# Party member slot containers — indexed 0-4 (slot 0 = player character).
# Each entry is a Dictionary with node refs populated in _ready().
var _party_slots: Array[Dictionary] = []

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	add_to_group("narrative_ui")
	_cache_party_slot_nodes()
	_connect_signals()
	_initialise_placeholder_state()


func _cache_party_slot_nodes() -> void:
	## Populates _party_slots array by resolving slot roots via unique name,
	## then resolving child nodes by relative path within each slot.
	## Slot roots are PartySlot0–PartySlot4 (unique_name_in_owner = true).
	## Child nodes are resolved by their known local path inside each slot.
	##
	## NOTE: scene-unique-name shorthand (%Name) only works as a literal token in
	## source, not in a runtime-constructed string. Child nodes within each slot
	## are accessed by relative path to avoid the ambiguity of 5 nodes sharing
	## the same local name across different sub-trees.
	_party_slots.clear()

	# Slot-root VBox paths (relative to this Control root) for each slot index.
	# Format: "PartyPanel/PartyVBox/PartySlotN/SlotNVBox"
	# This mirrors the scene tree exactly so no string-based get_node magic is needed.
	var slot_roots: Array[Control] = [
		%PartySlot0,
		%PartySlot1,
		%PartySlot2,
		%PartySlot3,
		%PartySlot4,
	]

	# Inner VBox naming convention: Slot0VBox, Slot1VBox, ...
	for i in range(5):
		var slot_root: Control    = slot_roots[i]
		var vbox: VBoxContainer   = slot_root.get_node("Slot%dVBox" % i)
		var top_row: HBoxContainer = vbox.get_node("Slot%dTopRow" % i)
		_party_slots.append({
			"root":        slot_root,
			"name_label":  top_row.get_node("SlotName"),
			"rel_label":   top_row.get_node("SlotRelationship"),
			"class_label": vbox.get_node("SlotClass"),
			"hp_bar":      vbox.get_node("SlotHPBar"),
			"hp_label":    vbox.get_node("SlotHPLabel"),
		})


func _connect_signals() -> void:
	send_button.pressed.connect(_on_send_pressed)
	input_field.text_submitted.connect(_on_input_text_submitted)
	menu_button.pressed.connect(_on_menu_pressed)
	inventory_button.pressed.connect(func(): inventory_requested.emit())
	journal_button.pressed.connect(func(): journal_requested.emit())
	save_button.pressed.connect(func(): save_requested.emit())

	for i in range(action_buttons.size()):
		var btn: Button = action_buttons[i]
		# Use a lambda with captured index to avoid the classic loop-closure pitfall.
		btn.pressed.connect(func(): _on_action_button_pressed(i))


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_I:
			inventory_requested.emit()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_J:
			journal_requested.emit()
			get_viewport().set_input_as_handled()
		elif event.keycode == KEY_F5:
			save_requested.emit()
			get_viewport().set_input_as_handled()


func _initialise_placeholder_state() -> void:
	## Sets up an empty-but-valid UI state for before any game data arrives.
	narrative_label.clear()
	narrative_label.bbcode_enabled = true

	# Welcome banner so the panel isn't blank on first load.
	add_narrative_text(
		"[center][color=#c9a227][b]Chronicles Unbound[/b][/color][/center]\n\n" +
		"[color=#b8b0a0]Awaiting your adventure...[/color]\n"
	)

	set_status("Ready.")

	# Suggested actions start hidden.
	for btn in action_buttons:
		btn.hide()

	# Party slots start in an empty/unknown state.
	for i in range(5):
		update_party_member(i, "—", "Unknown", 1, 0, 0, 0)
		_party_slots[i].root.modulate.a = 0.4   # dim until populated


# ── Public API ────────────────────────────────────────────────────────────────

## Appends BBCode-formatted text to the narrative window and scrolls to the bottom.
## The caller is responsible for any BBCode tags; plain text is safe to pass directly.
func add_narrative_text(text: String) -> void:
	narrative_label.append_text(text)
	_scroll_narrative_to_bottom()


## Formats and appends a companion's spoken line.
## @param name     Companion name — rendered in their accent color (gold).
## @param text     The dialogue line.
## @param emotion  Optional emotion tag shown in italic before the name (e.g. "worried").
func add_companion_dialogue(name: String, text: String, emotion: String) -> void:
	var formatted: String = ""

	if emotion != "":
		formatted += "[color=#b8b0a0][i]*%s*[/i][/color] " % emotion

	formatted += "[color=#c9a227][b]%s:[/b][/color] " % name
	formatted += "[color=#eee8d5]%s[/color]\n" % text

	add_narrative_text(formatted)


## Formats and appends a dice roll result entry.
## Displayed in pale cyan with a dice prefix to distinguish from narrative text.
func add_dice_result(text: String) -> void:
	var formatted: String = (
		"[color=#a8dadc][b]◈ %s[/b][/color]\n" % text
	)
	add_narrative_text(formatted)


## Updates the four suggested action buttons.
## Pass up to 4 strings; buttons beyond the array length are hidden.
## Buttons are automatically shown/hidden; labels are updated in order.
func set_suggested_actions(actions: Array[String]) -> void:
	for i in range(action_buttons.size()):
		var btn: Button = action_buttons[i]
		if i < actions.size():
			btn.text = "[%d]  %s" % [i + 1, actions[i]]
			btn.show()
		else:
			btn.hide()


## Updates the status bar text at the bottom of the screen.
func set_status(text: String) -> void:
	status_label.text = text


## Updates a single party member slot (index 0–4).
## Slot 0 is the player character by convention; slots 1–4 are companions.
## @param relationship  Score in range [-100, 100]; ignored for slot 0 (player).
func update_party_member(
	index: int,
	name: String,
	char_class: String,
	level: int,
	hp: int,
	max_hp: int,
	relationship: int
) -> void:
	if index < 0 or index >= _party_slots.size():
		push_warning("NarrativeUI.update_party_member: index %d out of range" % index)
		return

	var slot: Dictionary = _party_slots[index]

	# Restore full opacity now that this slot has real data.
	slot.root.modulate.a = 1.0

	slot.name_label.text  = name
	slot.class_label.text = "Lvl %d %s" % [level, char_class]

	# HP bar value and color.
	var hp_bar: ProgressBar = slot.hp_bar
	hp_bar.max_value = max(max_hp, 1)   # guard against divide-by-zero
	hp_bar.value     = clamp(hp, 0, max_hp)

	var hp_ratio: float = float(hp) / float(max(max_hp, 1))
	var bar_color: Color
	if hp_ratio > 0.5:
		bar_color = COLOR_HP_HEALTHY
	elif hp_ratio > 0.25:
		bar_color = COLOR_HP_HURT
	else:
		bar_color = COLOR_HP_CRITICAL

	# Apply color to the fill stylebox via a theme override.
	var bar_style := StyleBoxFlat.new()
	bar_style.bg_color = bar_color
	hp_bar.add_theme_stylebox_override("fill", bar_style)

	slot.hp_label.text = "%d / %d" % [hp, max_hp]

	# Relationship indicator — hidden for player slot (index 0).
	var rel_label: Label = slot.rel_label
	if index == 0:
		rel_label.hide()
	else:
		rel_label.show()
		rel_label.text = "♥ %s" % _relationship_tier_label(relationship)


# ── Private helpers ────────────────────────────────────────────────────────────

func _scroll_narrative_to_bottom() -> void:
	## Forces the RichTextLabel's parent ScrollContainer to the bottom on the next frame.
	## Deferred so the text layout has been computed before we read scroll limits.
	await get_tree().process_frame
	narrative_label.scroll_to_line(narrative_label.get_line_count() - 1)


func _relationship_tier_label(score: int) -> String:
	for tier in RELATIONSHIP_TIERS:
		if score >= tier[0] and score <= tier[1]:
			return tier[2]
	return "Unknown"


# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_send_pressed() -> void:
	var text: String = input_field.text.strip_edges()
	if text.is_empty():
		return
	input_field.clear()
	input_field.grab_focus()
	action_submitted.emit(text)


func _on_input_text_submitted(text: String) -> void:
	## LineEdit's text_submitted signal fires when Enter is pressed.
	## Delegate to the same handler as the Send button.
	_on_send_pressed()


func _on_action_button_pressed(index: int) -> void:
	suggested_action_selected.emit(index)


func _on_menu_pressed() -> void:
	menu_pressed.emit()
