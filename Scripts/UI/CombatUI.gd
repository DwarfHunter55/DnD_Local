## CombatUI.gd
## Controller for the tactical combat overlay in Chronicles Unbound.
## Manages the initiative bar, active-character panel, action buttons,
## combat log, and round counter. Attach to the root CombatUI Control node.
##
## This UI sits on top of the combat grid. The centre GridPassthrough area
## has mouse_filter = IGNORE so grid clicks pass through unobstructed.

extends Control

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the player selects a combat action.
## action_type matches ActionType enum names (e.g. "Attack", "CastSpell").
## target_id is the combatant Id string; empty when no target is needed.
signal action_selected(action_type: String, target_id: String)

## Emitted when the player clicks End Turn.
signal end_turn_pressed()

## Emitted when the player clicks Undo Move.
signal undo_move_pressed()

## Emitted when the player clicks a cell on the combat grid.
## grid_pos is a Vector2i with the grid cell coordinates.
signal grid_cell_clicked(grid_pos: Vector2i)

## Emitted when the player presses Continue on the combat end overlay.
signal combat_end_continue_pressed()

## Companion action review signals — forwarded from CompanionActionPanel.
signal companion_action_approved(companion_id: String, action_data: Dictionary)
signal companion_action_modified(companion_id: String, new_action: Dictionary)
signal companion_action_overridden(companion_id: String)

# ── Color constants ────────────────────────────────────────────────────────────
## Match NarrativeUI design tokens exactly.

const COLOR_TEXT_DEFAULT  := Color("#eee8d5")
const COLOR_TEXT_DIM      := Color("#b8b0a0")
const COLOR_ACCENT_GOLD   := Color("#c9a227")
const COLOR_HP_HEALTHY    := Color("#2ecc71")
const COLOR_HP_HURT       := Color("#f39c12")
const COLOR_HP_CRITICAL   := Color("#e74c3c")

## Combatant border colors in the initiative bar.
const COLOR_BORDER_PLAYER := Color("#3498db")   # blue  — player character
const COLOR_BORDER_ALLY   := Color("#27ae60")   # green — allies / companions
const COLOR_BORDER_ENEMY  := Color("#e74c3c")   # red   — enemies

## Condition text color.
const COLOR_CONDITION     := Color(0.86, 0.44, 0.44, 1.0)

## Victory / defeat banner colors.
const COLOR_VICTORY       := Color("#2ecc71")
const COLOR_DEFEAT        := Color("#e74c3c")

## Semi-transparent overlay for combat end screen.
const COLOR_OVERLAY_BG    := Color(0.04, 0.04, 0.08, 0.85)

# ── Node references ────────────────────────────────────────────────────────────

@onready var initiative_bar:       HBoxContainer  = %InitiativeBar
@onready var round_label:          Label          = %RoundLabel

@onready var char_name_label:      Label          = %CharNameLabel
@onready var char_level_label:     Label          = %CharLevelLabel
@onready var char_class_label:     Label          = %CharClassLabel
@onready var char_hp_bar:          ProgressBar    = %CharHPBar
@onready var char_hp_label:        Label          = %CharHPLabel
@onready var char_ac_label:        Label          = %CharACLabel
@onready var char_speed_label:     Label          = %CharSpeedLabel
@onready var char_conditions_label: Label         = %CharConditionsLabel

@onready var move_button:          Button         = %MoveButton
@onready var end_turn_button:      Button         = %EndTurnButton
@onready var undo_move_button:     Button         = %UndoMoveButton
@onready var reaction_status_label: Label         = %ReactionStatusLabel
@onready var combat_log:           RichTextLabel  = %CombatLog
@onready var companion_action_panel: PanelContainer = %CompanionActionPanel

# ── Action button map ──────────────────────────────────────────────────────────
## Maps ActionType string → Button node.
## Populated in _ready() after @onready resolution.
## This allows update_available_actions() and show_action_used() to operate
## via string lookup rather than a long if/elif chain.
var _action_buttons: Dictionary = {}

# ── StyleBoxes cached for End Turn highlighting ───────────────────────────────
## Pre-built at _ready() time so highlight_end_turn() has zero allocation cost.
var _endturn_style_normal:    StyleBoxFlat
var _endturn_style_hover:     StyleBoxFlat
var _endturn_style_highlight: StyleBoxFlat

# ── StyleBoxes cached for initiative chips ────────────────────────────────────
var _chip_style_default: StyleBoxFlat
var _chip_style_active:  StyleBoxFlat

# ── Grid interaction state ───────────────────────────────────────────────────
## Reference to the CombatGridRenderer in the scene tree.
## Resolved in _ready() by searching /root/Main/CombatGrid/Renderer.
var _grid_renderer: Node2D = null

## Reference to the CombatGrid node (parent of the renderer).
var _combat_grid: Node2D = null

## Cell size in pixels — read from CombatGrid.CellSize once resolved.
var _cell_size: int = 64

# ── Combat end overlay ────────────────────────────────────────────────────────
## Dynamically created overlay shown when combat ends.
var _combat_end_overlay: ColorRect = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_build_action_button_map()
	_cache_end_turn_styles()
	_cache_chip_styles()
	_connect_signals()
	_initialise_state()
	_resolve_grid_references()


func _build_action_button_map() -> void:
	## Populate the ActionType string → Button lookup.
	## Keys exactly match ActionType enum names in CombatAction.cs.
	_action_buttons = {
		"Attack":           %BtnAttack,
		"CastSpell":        %BtnCastSpell,
		"Dash":             %BtnDash,
		"Dodge":            %BtnDodge,
		"Disengage":        %BtnDisengage,
		"Help":             %BtnHelp,
		"Hide":             %BtnHide,
		"Ready":            %BtnReady,
		"UseObject":        %BtnUseObject,
		"OffhandAttack":    %BtnOffhandAttack,
		"BonusActionSpell": %BtnBonusSpell,
		"SecondWind":       %BtnSecondWind,
		"ActionSurge":      %BtnActionSurge,
	}


func _cache_end_turn_styles() -> void:
	## Read the three End Turn StyleBoxFlat sub-resources from the scene.
	## They are already the correct objects used by the button; cache references
	## so we can swap them at runtime without allocating new resources each call.
	_endturn_style_normal    = end_turn_button.get_theme_stylebox("normal")     as StyleBoxFlat
	_endturn_style_hover     = end_turn_button.get_theme_stylebox("hover")      as StyleBoxFlat

	## The highlight style is a distinct pre-built sub_resource in the scene.
	## We store it here and swap it in when all actions have been spent.
	## It is defined as StyleBox_endturn_highlight in the .tscn.
	## We clone it once so we have a mutable copy if needed in the future.
	_endturn_style_highlight = _build_endturn_highlight_style()


func _build_endturn_highlight_style() -> StyleBoxFlat:
	## Constructs the "all-actions-spent" End Turn stylebox at ready time.
	## Values mirror StyleBox_endturn_highlight defined in CombatUI.tscn.
	var s := StyleBoxFlat.new()
	s.bg_color             = Color(0.40, 0.30, 0.05, 1.0)
	s.border_width_left    = 2
	s.border_width_top     = 2
	s.border_width_right   = 2
	s.border_width_bottom  = 2
	s.border_color         = Color(0.95, 0.82, 0.35, 1.0)
	s.corner_radius_top_left     = 4
	s.corner_radius_top_right    = 4
	s.corner_radius_bottom_left  = 4
	s.corner_radius_bottom_right = 4
	s.content_margin_left   = 10.0
	s.content_margin_top    = 8.0
	s.content_margin_right  = 10.0
	s.content_margin_bottom = 8.0
	return s


func _cache_chip_styles() -> void:
	## Build the two initiative chip StyleBoxFlat objects used by
	## update_initiative_bar(). Creating them here avoids per-frame allocation.
	_chip_style_default = _build_chip_style(false)
	_chip_style_active  = _build_chip_style(true)


func _build_chip_style(active: bool) -> StyleBoxFlat:
	## Mirrors StyleBox_init_chip / StyleBox_init_chip_active from CombatUI.tscn.
	var s := StyleBoxFlat.new()
	s.bg_color            = Color(0.22, 0.17, 0.04, 1.0) if active else Color(0.11, 0.11, 0.20, 1.0)
	s.border_width_left   = 2
	s.border_width_top    = 2
	s.border_width_right  = 2
	s.border_width_bottom = 2
	s.border_color        = Color(0.95, 0.82, 0.35, 1.0) if active else Color(0.35, 0.30, 0.18, 0.7)
	s.corner_radius_top_left     = 3
	s.corner_radius_top_right    = 3
	s.corner_radius_bottom_left  = 3
	s.corner_radius_bottom_right = 3
	s.content_margin_left   = 6.0
	s.content_margin_top    = 3.0
	s.content_margin_right  = 6.0
	s.content_margin_bottom = 3.0
	return s


func _connect_signals() -> void:
	end_turn_button.pressed.connect(_on_end_turn_pressed)
	undo_move_button.pressed.connect(_on_undo_move_pressed)
	move_button.pressed.connect(_on_move_pressed)

	for action_type in _action_buttons:
		var btn: Button = _action_buttons[action_type]
		# Capture action_type string by value to avoid closure pitfall.
		btn.pressed.connect(func(): _on_action_button_pressed(action_type))

	# Forward CompanionActionPanel signals so CombatBridge can subscribe on CombatUI.
	if companion_action_panel:
		companion_action_panel.action_approved.connect(
			func(cid: String, data: Dictionary):
				companion_action_approved.emit(cid, data))
		companion_action_panel.action_modified.connect(
			func(cid: String, data: Dictionary):
				companion_action_modified.emit(cid, data))
		companion_action_panel.action_overridden.connect(
			func(cid: String):
				companion_action_overridden.emit(cid))


func _initialise_state() -> void:
	## Sets a clean default UI state before any game data arrives.
	combat_log.clear()
	combat_log.bbcode_enabled = true

	set_round(1)
	update_movement_remaining(30)
	reaction_status_label.text = "Available"

	# All action buttons start hidden.
	for btn in _action_buttons.values():
		btn.hide()

	char_conditions_label.hide()


func _resolve_grid_references() -> void:
	## Finds the CombatGrid and its Renderer in the scene tree.
	## CombatGrid lives at /root/Main/CombatGrid (created by CombatBridge.StartCombat).
	## The Renderer is a child of CombatGrid per CombatGrid.tscn.
	## We defer so the grid has time to be added if combat is starting.
	await get_tree().process_frame

	var main := get_tree().root.get_node_or_null("Main")
	if main == null:
		return

	_combat_grid = main.get_node_or_null("CombatGrid")
	if _combat_grid == null:
		return

	_grid_renderer = _combat_grid.get_node_or_null("Renderer")

	# Read cell size from the CombatGrid C# node.
	if _combat_grid.has_method("get") or "CellSize" in _combat_grid:
		_cell_size = _combat_grid.get("CellSize")
	else:
		_cell_size = 64


func _unhandled_input(event: InputEvent) -> void:
	## Detects left mouse clicks on the combat grid area and translates screen
	## position to grid cell coordinates. The centre GridPassthrough Control has
	## mouse_filter = IGNORE, so clicks pass through the UI layer to here.
	##
	## Escape key cancels the current pending action by re-emitting Move with
	## an invalid target, which CombatBridge interprets as "clear pending."

	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_ESCAPE:
			# Cancel any pending action. CombatBridge.ClearPendingAction is
			# triggered by emitting a special cancel signal — we re-use
			# action_selected with a reserved type.
			action_selected.emit("CancelPending", "")
			get_viewport().set_input_as_handled()
			return

	if not (event is InputEventMouseButton):
		return

	var mb: InputEventMouseButton = event as InputEventMouseButton
	if mb.button_index != MOUSE_BUTTON_LEFT or not mb.pressed:
		return

	if _combat_grid == null or _grid_renderer == null:
		# Try to resolve again in case the grid was added after _ready.
		_try_resolve_grid()
		if _combat_grid == null:
			return

	# Convert screen position to grid-local position.
	# The renderer is a Node2D — its global_transform positions the grid
	# in world space. We need to go from screen -> viewport -> world -> grid-local.
	var screen_pos: Vector2 = mb.position

	# Get the viewport's canvas transform to account for camera / zoom.
	var canvas_xform: Transform2D = _grid_renderer.get_viewport().canvas_transform
	var world_pos: Vector2 = canvas_xform.affine_inverse() * screen_pos

	# Transform from world space to the renderer's local space.
	var local_pos: Vector2 = _grid_renderer.get_global_transform().affine_inverse() * world_pos

	# Grid cell coordinates (integer division by cell size).
	var grid_x: int = int(floor(local_pos.x / _cell_size))
	var grid_y: int = int(floor(local_pos.y / _cell_size))

	# Bounds check — reject clicks outside the grid.
	var grid_width: int = _combat_grid.get("GridWidth") if "GridWidth" in _combat_grid else 0
	var grid_height: int = _combat_grid.get("GridHeight") if "GridHeight" in _combat_grid else 0

	if grid_x < 0 or grid_x >= grid_width or grid_y < 0 or grid_y >= grid_height:
		return

	var grid_pos := Vector2i(grid_x, grid_y)
	grid_cell_clicked.emit(grid_pos)
	get_viewport().set_input_as_handled()


func _try_resolve_grid() -> void:
	## Synchronous fallback for resolving grid references after the initial
	## deferred attempt in _ready(). Called from _unhandled_input when the
	## grid was not found at _ready time (e.g. combat started after UI loaded).
	var main := get_tree().root.get_node_or_null("Main")
	if main == null:
		return

	_combat_grid = main.get_node_or_null("CombatGrid")
	if _combat_grid == null:
		return

	_grid_renderer = _combat_grid.get_node_or_null("Renderer")

	if _combat_grid != null and "CellSize" in _combat_grid:
		_cell_size = _combat_grid.get("CellSize")


# ── Public API ─────────────────────────────────────────────────────────────────

## Rebuilds the initiative order chips to reflect the current turn order.
##
## @param combatants  Array of Dictionaries, each with keys:
##                      "id"    : String  — unique combatant Id
##                      "name"  : String  — display name
##                      "is_player_controlled" : bool
##                      "is_ally"              : bool
## @param active_id   The id of the combatant whose turn it currently is.
func update_initiative_bar(combatants: Array, active_id: String) -> void:
	# Remove all existing chips.
	for child in initiative_bar.get_children():
		child.queue_free()

	for data in combatants:
		var chip := _create_initiative_chip(data, active_id)
		initiative_bar.add_child(chip)


## Updates the right-panel character info block for the active combatant.
##
## @param p_name       Display name string.
## @param p_class_name Class name string (e.g. "Fighter").
## @param p_level      Integer level.
## @param p_hp         Current HP integer.
## @param p_max_hp     Maximum HP integer.
## @param p_ac         Armour Class integer.
## @param p_speed      Speed in feet integer.
## @param p_conditions Array of condition name strings (empty = no conditions).
func update_active_character(
	p_name:       String,
	p_class_name: String,
	p_level:      int,
	p_hp:         int,
	p_max_hp:     int,
	p_ac:         int,
	p_speed:      int,
	p_conditions: Array
) -> void:
	char_name_label.text  = p_name
	char_level_label.text = "Lv%d" % p_level
	char_class_label.text = p_class_name

	# HP bar.
	var safe_max: int = max(p_max_hp, 1)   # guard divide-by-zero
	char_hp_bar.max_value = safe_max
	char_hp_bar.value     = clamp(p_hp, 0, p_max_hp)

	var ratio: float = float(p_hp) / float(safe_max)
	var bar_color: Color
	if ratio > 0.5:
		bar_color = COLOR_HP_HEALTHY
	elif ratio > 0.25:
		bar_color = COLOR_HP_HURT
	else:
		bar_color = COLOR_HP_CRITICAL

	var fill_style := StyleBoxFlat.new()
	fill_style.bg_color                  = bar_color
	fill_style.corner_radius_top_left    = 3
	fill_style.corner_radius_top_right   = 3
	fill_style.corner_radius_bottom_left = 3
	fill_style.corner_radius_bottom_right = 3
	char_hp_bar.add_theme_stylebox_override("fill", fill_style)

	char_hp_label.text    = "%d / %d" % [p_hp, p_max_hp]
	char_ac_label.text    = "AC: %d" % p_ac
	char_speed_label.text = "Spd: %dft" % p_speed

	# Conditions list.
	if p_conditions.is_empty():
		char_conditions_label.hide()
		char_conditions_label.text = ""
	else:
		char_conditions_label.text = ", ".join(p_conditions)
		char_conditions_label.show()


## Shows only the action buttons present in the given list; hides all others.
## Move button visibility and text is NOT changed here — use update_movement_remaining().
##
## @param actions  Array of ActionType name strings (e.g. ["Attack", "CastSpell", "Dash"]).
##                 Unrecognised strings are silently ignored with a push_warning.
func update_available_actions(actions: Array) -> void:
	# Hide everything first.
	for btn in _action_buttons.values():
		btn.hide()
		btn.disabled = false   # reset disabled state from previous turn

	for action_type in actions:
		if _action_buttons.has(action_type):
			_action_buttons[action_type].show()
		else:
			push_warning("CombatUI.update_available_actions: unknown action '%s'" % action_type)


## Updates the Move button label to reflect remaining movement this turn.
## Hides the button entirely when no movement remains.
##
## @param feet  Remaining movement in feet.
func update_movement_remaining(feet: int) -> void:
	if feet <= 0:
		move_button.text     = "Move (0 ft remaining)"
		move_button.disabled = true
	else:
		move_button.text     = "Move (%d ft remaining)" % feet
		move_button.disabled = false


## Appends BBCode-formatted text to the combat log.
## The caller is responsible for any BBCode tags; plain text is safe to pass.
func add_combat_log(text: String) -> void:
	combat_log.append_text(text)
	_scroll_log_to_bottom()


## Updates the Round counter label.
func set_round(round_number: int) -> void:
	round_label.text = "Round: %d" % round_number


## Grays out (disables) the button for a given action type.
## Call after the action has been executed so the player can see it is spent.
##
## @param action_type  ActionType name string (e.g. "Attack").
func show_action_used(action_type: String) -> void:
	if action_type == "Move":
		# Movement is shown via update_movement_remaining, not a dedicated button.
		return

	if _action_buttons.has(action_type):
		var btn: Button = _action_buttons[action_type]
		btn.disabled = true
	elif action_type == "Reaction":
		reaction_status_label.text = "Used"
		reaction_status_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
	else:
		push_warning("CombatUI.show_action_used: unknown action '%s'" % action_type)


## Applies the prominent highlight style to the End Turn button.
## Call when all of the active combatant's actions have been spent.
## The bright gold border draws the eye and cues the player to end their turn.
func highlight_end_turn() -> void:
	end_turn_button.add_theme_stylebox_override("normal", _endturn_style_highlight)
	end_turn_button.add_theme_stylebox_override("hover",  _endturn_style_highlight)


## Restores the End Turn button to its default (non-highlighted) style.
## Call at the start of each new player turn.
func reset_end_turn_style() -> void:
	end_turn_button.add_theme_stylebox_override("normal", _endturn_style_normal)
	end_turn_button.add_theme_stylebox_override("hover",  _endturn_style_hover)


## Resets the Reaction status label for the new turn.
func reset_reaction_status() -> void:
	reaction_status_label.text = "Available"
	reaction_status_label.remove_theme_color_override("font_color")


## Shows the combat end overlay with victory/defeat banner, XP earned, and a
## Continue button. The overlay covers the entire viewport.
##
## @param data  Dictionary with keys:
##                "victory" : bool  — true for victory, false for defeat.
##                "xp"      : int   — total XP earned (0 on defeat).
##                "message" : String — optional flavour text.
func show_combat_end(data: Dictionary) -> void:
	var victory: bool = data.get("victory", false)
	var xp: int = data.get("xp", 0)
	var message: String = data.get("message", "")

	# Remove any previous overlay.
	if _combat_end_overlay != null:
		_combat_end_overlay.queue_free()
		_combat_end_overlay = null

	# ── Full-viewport semi-transparent background ────────────────────────
	_combat_end_overlay = ColorRect.new()
	_combat_end_overlay.color = COLOR_OVERLAY_BG
	_combat_end_overlay.set_anchors_preset(PRESET_FULL_RECT)
	_combat_end_overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_combat_end_overlay)

	# ── Centre card container ────────────────────────────────────────────
	var card := PanelContainer.new()
	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(0.063, 0.063, 0.118, 0.97)
	card_style.border_width_left   = 2
	card_style.border_width_top    = 2
	card_style.border_width_right  = 2
	card_style.border_width_bottom = 2
	card_style.border_color = COLOR_ACCENT_GOLD
	card_style.corner_radius_top_left     = 8
	card_style.corner_radius_top_right    = 8
	card_style.corner_radius_bottom_left  = 8
	card_style.corner_radius_bottom_right = 8
	card_style.content_margin_left   = 40.0
	card_style.content_margin_top    = 30.0
	card_style.content_margin_right  = 40.0
	card_style.content_margin_bottom = 30.0
	card.add_theme_stylebox_override("panel", card_style)

	card.set_anchors_preset(PRESET_CENTER)
	card.grow_horizontal = Control.GROW_DIRECTION_BOTH
	card.grow_vertical   = Control.GROW_DIRECTION_BOTH
	card.custom_minimum_size = Vector2(400, 250)
	_combat_end_overlay.add_child(card)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	card.add_child(vbox)

	# ── Banner label ─────────────────────────────────────────────────────
	var banner_label := Label.new()
	if victory:
		banner_label.text = "VICTORY"
		banner_label.add_theme_color_override("font_color", COLOR_VICTORY)
	else:
		banner_label.text = "DEFEAT"
		banner_label.add_theme_color_override("font_color", COLOR_DEFEAT)
	banner_label.add_theme_font_size_override("font_size", 28)
	banner_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(banner_label)

	# ── XP label (victory only) ──────────────────────────────────────────
	if victory and xp > 0:
		var xp_label := Label.new()
		xp_label.text = "Party earned %d XP" % xp
		xp_label.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
		xp_label.add_theme_font_size_override("font_size", 16)
		xp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		vbox.add_child(xp_label)

	# ── Optional flavour text ────────────────────────────────────────────
	if message != "":
		var msg_label := Label.new()
		msg_label.text = message
		msg_label.add_theme_color_override("font_color", COLOR_TEXT_DIM)
		msg_label.add_theme_font_size_override("font_size", 13)
		msg_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		msg_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		vbox.add_child(msg_label)

	# ── Spacer ───────────────────────────────────────────────────────────
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 8)
	vbox.add_child(spacer)

	# ── Continue button ──────────────────────────────────────────────────
	var continue_btn := Button.new()
	continue_btn.text = "Continue"
	continue_btn.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	continue_btn.custom_minimum_size = Vector2(160, 0)
	continue_btn.add_theme_color_override("font_color", COLOR_ACCENT_GOLD)
	continue_btn.add_theme_color_override("font_hover_color", Color("#f5d76e"))
	continue_btn.add_theme_color_override("font_pressed_color", Color("#fffacd"))
	continue_btn.add_theme_font_size_override("font_size", 15)
	continue_btn.add_theme_stylebox_override("normal", _build_endturn_highlight_style())
	continue_btn.add_theme_stylebox_override("hover", _build_endturn_highlight_style())
	continue_btn.add_theme_stylebox_override("pressed", _build_endturn_highlight_style())
	continue_btn.add_theme_stylebox_override("focus", _build_endturn_highlight_style())
	continue_btn.pressed.connect(_on_combat_end_continue_pressed)
	vbox.add_child(continue_btn)

	# Grab focus so the player can press Enter.
	continue_btn.grab_focus()


## Shows the companion action review panel with the AI's suggested action.
## Called by CombatBridge when it is a companion's turn in Suggest autonomy mode.
##
## @param p_companion_id     Unique companion identifier string.
## @param p_companion_name   Display name (e.g. "Elara").
## @param p_suggested_action Dictionary with at least "display_text": String.
## @param p_reasoning        Plain English explanation of the AI's decision.
## @param p_alternatives     Array of alternative action Dictionaries (may be empty).
func show_action_review(
	p_companion_id:     String,
	p_companion_name:   String,
	p_suggested_action: Dictionary,
	p_reasoning:        String,
	p_alternatives:     Array
) -> void:
	if companion_action_panel:
		companion_action_panel.show_suggestion(
			p_companion_id, p_companion_name,
			p_suggested_action, p_reasoning, p_alternatives)


## Hides the companion action review panel if currently visible.
func hide_action_review() -> void:
	if companion_action_panel and companion_action_panel.visible:
		companion_action_panel.hide()


## Hides and destroys the combat end overlay. Called internally or by bridge
## after the Continue button is pressed.
func hide_combat_end() -> void:
	if _combat_end_overlay != null:
		_combat_end_overlay.queue_free()
		_combat_end_overlay = null


# ── Private helpers ────────────────────────────────────────────────────────────

func _create_initiative_chip(data: Dictionary, active_id: String) -> PanelContainer:
	## Builds a single initiative order chip PanelContainer for one combatant.
	## Chip shows a 3-character name abbreviation with a colored side border:
	##   blue  = player character (is_player_controlled)
	##   green = ally  (is_ally and not player)
	##   red   = enemy (!is_ally)
	## The active combatant gets the gold highlight stylebox and bold text.

	var is_active: bool = (data.get("id", "") == active_id)

	# Chip border color reflects faction.
	var border_color: Color
	if data.get("is_player_controlled", false):
		border_color = COLOR_BORDER_PLAYER
	elif data.get("is_ally", false):
		border_color = COLOR_BORDER_ALLY
	else:
		border_color = COLOR_BORDER_ENEMY

	# Build chip PanelContainer style.
	var chip_style := StyleBoxFlat.new()
	if is_active:
		chip_style.bg_color   = Color(0.22, 0.17, 0.04, 1.0)
		# Active chip uses gold outer border plus faction left-stripe.
		# We simulate the faction stripe via a wider left border with faction color.
		chip_style.border_width_left   = 4
		chip_style.border_width_top    = 2
		chip_style.border_width_right  = 2
		chip_style.border_width_bottom = 2
		chip_style.border_color        = Color(0.95, 0.82, 0.35, 1.0)
	else:
		chip_style.bg_color   = Color(0.11, 0.11, 0.20, 1.0)
		chip_style.border_width_left   = 4   # faction stripe is always 4px on the left
		chip_style.border_width_top    = 1
		chip_style.border_width_right  = 1
		chip_style.border_width_bottom = 1
		chip_style.border_color        = border_color

	chip_style.corner_radius_top_left     = 3
	chip_style.corner_radius_top_right    = 3
	chip_style.corner_radius_bottom_left  = 3
	chip_style.corner_radius_bottom_right = 3
	chip_style.content_margin_left   = 6.0
	chip_style.content_margin_top    = 3.0
	chip_style.content_margin_right  = 6.0
	chip_style.content_margin_bottom = 3.0

	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", chip_style)

	# Faction left-stripe for active chip: layer a thin ColorRect on the left.
	# For non-active chips the border_color already gives the stripe.
	# For active chips we need to show faction color separately since gold covers all borders.
	if is_active:
		var stripe := ColorRect.new()
		stripe.color         = border_color
		stripe.custom_minimum_size = Vector2(3, 0)
		stripe.layout_mode   = 1
		stripe.anchor_top    = 0.0
		stripe.anchor_bottom = 1.0
		stripe.anchor_left   = 0.0
		stripe.anchor_right  = 0.0
		stripe.grow_vertical = 2
		panel.add_child(stripe)

	# Name label — up to 4 chars to keep chips narrow.
	var full_name: String = data.get("name", "?")
	var abbr: String      = full_name.substr(0, 4)

	var label := Label.new()
	label.text = abbr
	label.add_theme_color_override(
		"font_color",
		COLOR_ACCENT_GOLD if is_active else COLOR_TEXT_DEFAULT
	)
	label.add_theme_font_size_override("font_size", 11)
	label.tooltip_text = full_name   # full name on hover
	panel.add_child(label)

	return panel


func _scroll_log_to_bottom() -> void:
	## Deferred so the RichTextLabel layout has been computed before we read line count.
	await get_tree().process_frame
	combat_log.scroll_to_line(combat_log.get_line_count() - 1)

# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_end_turn_pressed() -> void:
	end_turn_pressed.emit()


func _on_undo_move_pressed() -> void:
	undo_move_pressed.emit()


func _on_move_pressed() -> void:
	## Move button activates movement mode; no target_id needed.
	action_selected.emit("Move", "")


func _on_action_button_pressed(action_type: String) -> void:
	## Most actions require the player to click a target on the grid after
	## selecting the action type. We emit the signal here and let the combat
	## controller handle target selection state.
	action_selected.emit(action_type, "")
