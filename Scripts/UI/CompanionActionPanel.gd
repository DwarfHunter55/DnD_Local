## CompanionActionPanel.gd
## Companion Action Review panel for Chronicles Unbound.
## Displayed during a companion's turn (Suggest autonomy mode) to let the player
## approve, modify, or override the AI-suggested action before it executes.
##
## Slides in from the right edge of the viewport when show_suggestion() is called.
## Slides back out after any of the three response buttons is pressed.
##
## Expected parent: the root CombatUI Control (or any Control that spans the viewport).
## The panel positions itself anchored to the right side of its parent.

extends PanelContainer

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the player approves the AI suggestion without modification.
## companion_id  — the companion's unique identifier string.
## action_data   — the original suggested_action Dictionary passed to show_suggestion().
signal action_approved(companion_id: String, action_data: Dictionary)

## Emitted when the player selects a different action from the alternatives list.
## companion_id  — the companion's unique identifier string.
## new_action    — one of the alternative action Dictionaries from the alternatives Array.
signal action_modified(companion_id: String, new_action: Dictionary)

## Emitted when the player clicks Override — they want full manual control this turn.
## The caller should switch to FullControl behaviour for this turn only.
## companion_id  — the companion's unique identifier string.
signal action_overridden(companion_id: String)

# ── Color constants ────────────────────────────────────────────────────────────
## Match project-wide design tokens exactly.

const COLOR_TEXT_DEFAULT  := Color("#eee8d5")
const COLOR_TEXT_DIM      := Color("#b8b0a0")
const COLOR_ACCENT_GOLD   := Color("#c9a227")
const COLOR_TEXT_BODY     := Color("#e8e0d4")

## Button accent colors specified in the task brief.
const COLOR_APPROVE       := Color("#4a7c59")
const COLOR_APPROVE_HOVER := Color("#5d9e6f")
const COLOR_MODIFY        := Color("#c9a227")
const COLOR_MODIFY_HOVER  := Color("#e0b83a")
const COLOR_OVERRIDE      := Color("#8b2252")
const COLOR_OVERRIDE_HOVER:= Color("#a8285f")

# ── Slide animation ────────────────────────────────────────────────────────────
## The panel slides from right (off-screen) to its resting position and back.
## SLIDE_DURATION is intentionally short — the player is waiting on an action review.
const SLIDE_DURATION := 0.28   # seconds
const SLIDE_EASE     := Tween.EASE_OUT
const SLIDE_TRANS    := Tween.TRANS_QUART

# ── Node references ────────────────────────────────────────────────────────────

@onready var portrait_rect:      ColorRect    = %PortraitRect
@onready var portrait_initial:   Label        = %PortraitInitial
@onready var companion_name_label: Label      = %CompanionNameLabel

@onready var action_label:       Label        = %ActionLabel
@onready var reasoning_label:    Label        = %ReasoningLabel

@onready var approve_button:     Button       = %ApproveButton
@onready var modify_button:      Button       = %ModifyButton
@onready var override_button:    Button       = %OverrideButton

## Collapsible alternatives list — hidden until Modify is pressed.
@onready var alternatives_section: VBoxContainer = %AlternativesSection
@onready var alternatives_scroll:  ScrollContainer = %AlternativesScroll
@onready var alternatives_vbox:    VBoxContainer = %AlternativesVBox

# ── Private state ──────────────────────────────────────────────────────────────

var _companion_id:   String     = ""
var _suggested_action: Dictionary = {}
var _alternatives:   Array      = []
var _tween:          Tween      = null

# The resting X position (fully visible). Computed in _ready() from layout.
var _rest_x: float = 0.0

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_connect_buttons()
	_cache_rest_position()
	alternatives_section.hide()
	# Panel starts off-screen to the right; show_suggestion() slides it in.
	hide()


func _connect_buttons() -> void:
	approve_button.pressed.connect(_on_approve_pressed)
	modify_button.pressed.connect(_on_modify_pressed)
	override_button.pressed.connect(_on_override_pressed)


func _cache_rest_position() -> void:
	## Called once in _ready() after layout has resolved.
	## We defer by one frame so the panel's size is known.
	await get_tree().process_frame
	_rest_x = position.x


# ── Public API ─────────────────────────────────────────────────────────────────

## Populates the panel with a companion's suggested action and slides it into view.
##
## @param p_companion_id     Unique companion identifier (passed through to signals).
## @param p_companion_name   Display name (e.g. "Elara"). Used for portrait initial and header.
## @param p_suggested_action Dictionary describing the AI's chosen action. Required keys:
##                             "display_text" : String — plain English summary
##                             (all other keys are passed through to action_approved/action_modified)
## @param p_reasoning        Plain English explanation of why the AI chose this action.
## @param p_alternatives     Array of action Dictionaries. Each must have "display_text": String.
##                           May be empty — Modify button is hidden when no alternatives exist.
func show_suggestion(
	p_companion_id:     String,
	p_companion_name:   String,
	p_suggested_action: Dictionary,
	p_reasoning:        String,
	p_alternatives:     Array
) -> void:
	_companion_id      = p_companion_id
	_suggested_action  = p_suggested_action
	_alternatives      = p_alternatives

	# Populate portrait.
	_set_portrait_color(p_companion_name)
	portrait_initial.text     = p_companion_name.substr(0, 1).to_upper()
	companion_name_label.text = p_companion_name

	# Populate action and reasoning text.
	action_label.text    = p_suggested_action.get("display_text", "(no action description)")
	reasoning_label.text = p_reasoning

	# Hide/show Modify button based on whether alternatives exist.
	modify_button.visible = not p_alternatives.is_empty()

	# Always collapse alternatives when a new suggestion arrives.
	alternatives_section.hide()
	_clear_alternatives_list()

	# Slide the panel in.
	_slide_in()


# ── Private helpers ────────────────────────────────────────────────────────────

func _set_portrait_color(companion_name: String) -> void:
	## Derives a stable hue from the companion's name so each companion
	## gets a consistent portrait background color across the session.
	## The hue cycles through a dark-fantasy-friendly range (cool to warm).
	var hash_val: int = 0
	for i in companion_name.length():
		hash_val = (hash_val * 31 + companion_name.unicode_at(i)) & 0xFFFFFF
	var hue: float = fmod(float(hash_val % 1000) / 1000.0, 1.0)
	# Keep saturation and value muted so the letter remains legible.
	portrait_rect.color = Color.from_hsv(hue, 0.45, 0.28, 1.0)


func _clear_alternatives_list() -> void:
	## Removes all dynamically created alternative action buttons.
	for child in alternatives_vbox.get_children():
		child.queue_free()


func _build_alternatives_list() -> void:
	## Creates one button per alternative action using the same StyleBox tokens
	## as the existing action buttons in CombatUI.
	_clear_alternatives_list()

	for i in _alternatives.size():
		var alt: Dictionary = _alternatives[i]
		var display_text: String = alt.get("display_text", "Unknown action")

		var btn := Button.new()
		btn.text = display_text
		btn.layout_mode = 2
		btn.size_flags_horizontal = 3

		# Style the alternative button to match CombatUI normal/hover/pressed.
		btn.add_theme_stylebox_override("normal",   _make_alt_btn_style_normal())
		btn.add_theme_stylebox_override("hover",    _make_alt_btn_style_hover())
		btn.add_theme_stylebox_override("pressed",  _make_alt_btn_style_pressed())
		btn.add_theme_stylebox_override("focus",    _make_alt_btn_style_hover())
		btn.add_theme_color_override("font_color",         COLOR_TEXT_DEFAULT)
		btn.add_theme_color_override("font_hover_color",   COLOR_ACCENT_GOLD)
		btn.add_theme_color_override("font_pressed_color", Color(1.0, 0.95, 0.6, 1.0))
		btn.add_theme_font_size_override("font_size", 12)

		# Capture loop variable by value to avoid closure pitfall.
		var captured_alt: Dictionary = alt
		btn.pressed.connect(func(): _on_alternative_selected(captured_alt))

		alternatives_vbox.add_child(btn)


func _make_alt_btn_style_normal() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color            = Color(0.11, 0.11, 0.20, 1.0)
	s.border_width_left   = 1
	s.border_width_top    = 1
	s.border_width_right  = 1
	s.border_width_bottom = 1
	s.border_color        = Color(0.788, 0.635, 0.153, 0.7)
	s.corner_radius_top_left     = 4
	s.corner_radius_top_right    = 4
	s.corner_radius_bottom_left  = 4
	s.corner_radius_bottom_right = 4
	s.content_margin_left   = 8.0
	s.content_margin_top    = 5.0
	s.content_margin_right  = 8.0
	s.content_margin_bottom = 5.0
	return s


func _make_alt_btn_style_hover() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color            = Color(0.18, 0.16, 0.10, 1.0)
	s.border_width_left   = 1
	s.border_width_top    = 1
	s.border_width_right  = 1
	s.border_width_bottom = 1
	s.border_color        = Color(0.788, 0.635, 0.153, 1.0)
	s.corner_radius_top_left     = 4
	s.corner_radius_top_right    = 4
	s.corner_radius_bottom_left  = 4
	s.corner_radius_bottom_right = 4
	s.content_margin_left   = 8.0
	s.content_margin_top    = 5.0
	s.content_margin_right  = 8.0
	s.content_margin_bottom = 5.0
	return s


func _make_alt_btn_style_pressed() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color            = Color(0.25, 0.20, 0.06, 1.0)
	s.border_width_left   = 1
	s.border_width_top    = 1
	s.border_width_right  = 1
	s.border_width_bottom = 1
	s.border_color        = Color(0.95, 0.82, 0.35, 1.0)
	s.corner_radius_top_left     = 4
	s.corner_radius_top_right    = 4
	s.corner_radius_bottom_left  = 4
	s.corner_radius_bottom_right = 4
	s.content_margin_left   = 8.0
	s.content_margin_top    = 5.0
	s.content_margin_right  = 8.0
	s.content_margin_bottom = 5.0
	return s


# ── Slide animation ────────────────────────────────────────────────────────────

func _slide_in() -> void:
	## Ensures panel is visible then animates from right-edge to resting position.
	if _tween and _tween.is_running():
		_tween.kill()

	# Snap to off-screen right before showing so there is no one-frame flash.
	var off_x: float = _rest_x + size.x + 16.0   # 16px gap past the right edge
	position.x = off_x
	show()

	_tween = create_tween()
	_tween.set_ease(SLIDE_EASE)
	_tween.set_trans(SLIDE_TRANS)
	_tween.tween_property(self, "position:x", _rest_x, SLIDE_DURATION)


func _slide_out_then_hide() -> void:
	## Animates the panel back to the right edge then hides it.
	if _tween and _tween.is_running():
		_tween.kill()

	var off_x: float = _rest_x + size.x + 16.0
	_tween = create_tween()
	_tween.set_ease(Tween.EASE_IN)
	_tween.set_trans(SLIDE_TRANS)
	_tween.tween_property(self, "position:x", off_x, SLIDE_DURATION)
	_tween.tween_callback(hide)


# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_approve_pressed() -> void:
	_slide_out_then_hide()
	action_approved.emit(_companion_id, _suggested_action)


func _on_modify_pressed() -> void:
	## Toggles the alternatives section. Builds the list on first reveal.
	if alternatives_section.visible:
		alternatives_section.hide()
	else:
		_build_alternatives_list()
		alternatives_section.show()


func _on_override_pressed() -> void:
	_slide_out_then_hide()
	action_overridden.emit(_companion_id)


func _on_alternative_selected(alt_action: Dictionary) -> void:
	_slide_out_then_hide()
	action_modified.emit(_companion_id, alt_action)
