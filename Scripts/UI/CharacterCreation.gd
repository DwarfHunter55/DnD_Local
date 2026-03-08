extends Control

# CharacterCreation.gd
# Multi-step wizard for D&D 5e character creation.
# Emits signals that SceneManager connects to for navigation.

# ══════════════════════════════════════════════════════════════════════════════
# SIGNALS
# ══════════════════════════════════════════════════════════════════════════════

signal character_created()
signal creation_cancelled()

# ══════════════════════════════════════════════════════════════════════════════
# CONSTANTS
# ══════════════════════════════════════════════════════════════════════════════

const STEP_COUNT = 7
const STANDARD_ARRAY = [15, 14, 13, 12, 10, 8]

# SRD Data — Hard-coded for now. In production, load from JSON files.
const RACES = [
	{"name": "Human", "description": "Versatile and ambitious, humans are the most common race.", "bonuses": {"str": 1, "dex": 1, "con": 1, "int": 1, "wis": 1, "cha": 1}},
	{"name": "Elf", "description": "Graceful and long-lived, with keen senses and fey ancestry.", "bonuses": {"dex": 2}},
	{"name": "Dwarf", "description": "Bold and hardy, dwarves are known as skilled warriors and craftspeople.", "bonuses": {"con": 2}},
	{"name": "Halfling", "description": "Small and nimble, halflings are quick and resourceful.", "bonuses": {"dex": 2}},
	{"name": "Gnome", "description": "Clever and inventive, gnomes are natural tinkerers.", "bonuses": {"int": 2}},
	{"name": "Half-Elf", "description": "Charismatic and adaptable, combining human and elven traits.", "bonuses": {"cha": 2}},
	{"name": "Half-Orc", "description": "Strong and fierce, with endurance and savage instincts.", "bonuses": {"str": 2, "con": 1}},
	{"name": "Tiefling", "description": "Infernally touched, with innate magical abilities.", "bonuses": {"int": 1, "cha": 2}},
	{"name": "Dragonborn", "description": "Draconic humanoids with breath weapons and scales.", "bonuses": {"str": 2, "cha": 1}}
]

const CLASSES = [
	{"name": "Fighter", "description": "Master of weapons and armor. Hit Die: d10. Primary: Strength or Dexterity.", "hit_die": 10, "skills_count": 2, "skills": ["Acrobatics", "Animal Handling", "Athletics", "History", "Insight", "Intimidation", "Perception", "Survival"]},
	{"name": "Wizard", "description": "Scholar of arcane magic. Hit Die: d6. Primary: Intelligence.", "hit_die": 6, "skills_count": 2, "skills": ["Arcana", "History", "Insight", "Investigation", "Medicine", "Religion"]},
	{"name": "Rogue", "description": "Stealthy expert in subterfuge. Hit Die: d8. Primary: Dexterity.", "hit_die": 8, "skills_count": 4, "skills": ["Acrobatics", "Athletics", "Deception", "Insight", "Intimidation", "Investigation", "Perception", "Performance", "Persuasion", "Sleight of Hand", "Stealth"]},
	{"name": "Cleric", "description": "Divine spellcaster and healer. Hit Die: d8. Primary: Wisdom.", "hit_die": 8, "skills_count": 2, "skills": ["History", "Insight", "Medicine", "Persuasion", "Religion"]},
	{"name": "Ranger", "description": "Wilderness warrior and tracker. Hit Die: d10. Primary: Dexterity or Strength.", "hit_die": 10, "skills_count": 3, "skills": ["Animal Handling", "Athletics", "Insight", "Investigation", "Nature", "Perception", "Stealth", "Survival"]},
	{"name": "Paladin", "description": "Holy warrior bound by oath. Hit Die: d10. Primary: Strength and Charisma.", "hit_die": 10, "skills_count": 2, "skills": ["Athletics", "Insight", "Intimidation", "Medicine", "Persuasion", "Religion"]},
	{"name": "Bard", "description": "Inspiring musician and jack-of-all-trades. Hit Die: d8. Primary: Charisma.", "hit_die": 8, "skills_count": 3, "skills": ["Acrobatics", "Animal Handling", "Arcana", "Athletics", "Deception", "History", "Insight", "Intimidation", "Investigation", "Medicine", "Nature", "Perception", "Performance", "Persuasion", "Religion", "Sleight of Hand", "Stealth", "Survival"]},
	{"name": "Barbarian", "description": "Fierce warrior fueled by primal rage. Hit Die: d12. Primary: Strength.", "hit_die": 12, "skills_count": 2, "skills": ["Animal Handling", "Athletics", "Intimidation", "Nature", "Perception", "Survival"]},
	{"name": "Druid", "description": "Priest of nature who wields primal magic. Hit Die: d8. Primary: Wisdom.", "hit_die": 8, "skills_count": 2, "skills": ["Arcana", "Animal Handling", "Insight", "Medicine", "Nature", "Perception", "Religion", "Survival"]},
	{"name": "Monk", "description": "Master of martial arts and ki energy. Hit Die: d8. Primary: Dexterity and Wisdom.", "hit_die": 8, "skills_count": 2, "skills": ["Acrobatics", "Athletics", "History", "Insight", "Religion", "Stealth"]},
	{"name": "Sorcerer", "description": "Innate spellcaster with raw magical power. Hit Die: d6. Primary: Charisma.", "hit_die": 6, "skills_count": 2, "skills": ["Arcana", "Deception", "Insight", "Intimidation", "Persuasion", "Religion"]},
	{"name": "Warlock", "description": "Wielder of magic granted by a powerful patron. Hit Die: d8. Primary: Charisma.", "hit_die": 8, "skills_count": 2, "skills": ["Arcana", "Deception", "History", "Intimidation", "Investigation", "Nature", "Religion"]}
]

const BACKGROUNDS = [
	{"name": "Acolyte", "description": "You served in a temple, learning religious rites and history.", "skills": ["Insight", "Religion"]},
	{"name": "Criminal", "description": "You have a history of breaking the law and living in the shadows.", "skills": ["Deception", "Stealth"]},
	{"name": "Folk Hero", "description": "You come from humble origins and became a champion of the common people.", "skills": ["Animal Handling", "Survival"]},
	{"name": "Noble", "description": "You were born into privilege and high society.", "skills": ["History", "Persuasion"]},
	{"name": "Sage", "description": "You spent years learning the lore of the multiverse.", "skills": ["Arcana", "History"]},
	{"name": "Soldier", "description": "You trained for war as part of an organized military.", "skills": ["Athletics", "Intimidation"]},
	{"name": "Charlatan", "description": "You have always had a way with people and made your living through schemes.", "skills": ["Deception", "Sleight of Hand"]},
	{"name": "Entertainer", "description": "You thrive in front of an audience, performing for their pleasure.", "skills": ["Acrobatics", "Performance"]},
	{"name": "Guild Artisan", "description": "You are a member of an artisan's guild, skilled in a particular craft.", "skills": ["Insight", "Persuasion"]},
	{"name": "Hermit", "description": "You lived in seclusion, seeking enlightenment or hiding from society.", "skills": ["Medicine", "Religion"]},
	{"name": "Outlander", "description": "You grew up in the wilds, far from civilization.", "skills": ["Athletics", "Survival"]},
	{"name": "Sailor", "description": "You sailed on a ship for years, learning the ways of the sea.", "skills": ["Athletics", "Perception"]},
	{"name": "Urchin", "description": "You grew up alone and poor on the streets.", "skills": ["Sleight of Hand", "Stealth"]}
]

const ABILITY_NAMES = ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"]
const ABILITY_ABBREV = {"Strength": "STR", "Dexterity": "DEX", "Constitution": "CON", "Intelligence": "INT", "Wisdom": "WIS", "Charisma": "CHA"}

# ══════════════════════════════════════════════════════════════════════════════
# STATE
# ══════════════════════════════════════════════════════════════════════════════

var current_step: int = 0

# Character data
var selected_race: Dictionary = {}
var selected_class: Dictionary = {}
var selected_background: Dictionary = {}
var ability_scores: Dictionary = {}  # {"Strength": 15, "Dexterity": 14, ...}
var selected_skills: Array = []
var character_name: String = ""
var character_description: String = ""

# UI references
@onready var step_container: TabContainer = %StepContainer
@onready var back_button: Button = %BackButton
@onready var next_button: Button = %NextButton

# Step content containers
@onready var race_content: VBoxContainer = %RaceContent
@onready var class_content: VBoxContainer = %ClassContent
@onready var background_content: VBoxContainer = %BackgroundContent
@onready var ability_content: VBoxContainer = %AbilityContent
@onready var skill_content: VBoxContainer = %SkillContent
@onready var name_content: VBoxContainer = %NameContent
@onready var summary_content: VBoxContainer = %SummaryContent

# Step panels for visual feedback
@onready var step_panels: Array = [
	%Step0, %Step1, %Step2, %Step3, %Step4, %Step5, %Step6
]

# Selected buttons for each step (to highlight selections)
var selected_race_button: Button = null
var selected_class_button: Button = null
var selected_background_button: Button = null

# Ability score OptionButton references keyed by ability name
var ability_option_buttons: Dictionary = {}  # {"Strength": OptionButton, ...}

# ══════════════════════════════════════════════════════════════════════════════
# LIFECYCLE
# ══════════════════════════════════════════════════════════════════════════════

func _ready() -> void:
	back_button.pressed.connect(_on_back_pressed)
	next_button.pressed.connect(_on_next_pressed)

	# Build UI for each step
	_build_race_step()
	_build_class_step()
	_build_background_step()
	_build_ability_step()
	# Skill step is built after class is selected (dynamic)
	# Name step is built on demand
	# Summary step is built on demand

	_update_step_display()

# ══════════════════════════════════════════════════════════════════════════════
# STEP NAVIGATION
# ══════════════════════════════════════════════════════════════════════════════

func _on_back_pressed() -> void:
	if current_step == 0:
		# Cancel creation
		creation_cancelled.emit()
	else:
		current_step -= 1
		_update_step_display()

func _on_next_pressed() -> void:
	# Validate current step
	if not _validate_current_step():
		return

	if current_step == STEP_COUNT - 1:
		# Final step — create character
		_create_character()
	else:
		current_step += 1
		_prepare_step()
		_update_step_display()

func _validate_current_step() -> bool:
	match current_step:
		0:  # Race
			if selected_race.is_empty():
				_show_error("Please select a race.")
				return false
		1:  # Class
			if selected_class.is_empty():
				_show_error("Please select a class.")
				return false
		2:  # Background
			if selected_background.is_empty():
				_show_error("Please select a background.")
				return false
		3:  # Abilities
			if ability_scores.size() != 6:
				_show_error("Please assign all ability scores.")
				return false
		4:  # Skills
			var required = selected_class.get("skills_count", 2)
			if selected_skills.size() != required:
				_show_error("Please select exactly %d skills." % required)
				return false
		5:  # Name
			if character_name.strip_edges().is_empty():
				_show_error("Please enter a character name.")
				return false
		6:  # Summary
			pass
	return true

func _prepare_step() -> void:
	# Build dynamic content when entering certain steps
	match current_step:
		3:  # Abilities — rebuild to show racial bonuses from selected race
			_build_ability_step()
		4:  # Skills
			_build_skill_step()
		5:  # Name
			_build_name_step()
		6:  # Summary
			_build_summary_step()

func _update_step_display() -> void:
	# Update TabContainer
	step_container.current_tab = current_step

	# Update step indicator visuals
	for i in step_panels.size():
		var panel: Panel = step_panels[i]
		var label: Label = panel.get_node("Label")

		if i == current_step:
			# Active step
			panel.add_theme_stylebox_override("panel", _get_custom_stylebox("StyleBox_step_active"))
			label.add_theme_color_override("font_color", Color(0.95, 0.82, 0.35))
		elif i < current_step:
			# Completed step
			panel.add_theme_stylebox_override("panel", _get_custom_stylebox("StyleBox_step_active"))
			label.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
		else:
			# Inactive step
			panel.add_theme_stylebox_override("panel", _get_custom_stylebox("StyleBox_step_inactive"))
			label.add_theme_color_override("font_color", Color(0.533, 0.533, 0.533))

	# Update navigation buttons
	if current_step == 0:
		back_button.text = "Cancel"
	else:
		back_button.text = "Back"

	if current_step == STEP_COUNT - 1:
		next_button.text = "Create Character"
	else:
		next_button.text = "Next"

func _show_error(message: String) -> void:
	print("[CharacterCreation] Validation error: ", message)
	# TODO: Show error dialog or label

# ══════════════════════════════════════════════════════════════════════════════
# STEP 0: RACE SELECTION
# ══════════════════════════════════════════════════════════════════════════════

func _build_race_step() -> void:
	var title = Label.new()
	title.text = "Choose Your Race"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	race_content.add_child(title)

	for race in RACES:
		var btn = _create_selection_button(race["name"], race["description"])
		btn.pressed.connect(_on_race_selected.bind(race, btn))
		race_content.add_child(btn)

func _on_race_selected(race: Dictionary, btn: Button) -> void:
	selected_race = race
	_highlight_button(btn, selected_race_button)
	selected_race_button = btn
	print("[CharacterCreation] Selected race: ", race["name"])

# ══════════════════════════════════════════════════════════════════════════════
# STEP 1: CLASS SELECTION
# ══════════════════════════════════════════════════════════════════════════════

func _build_class_step() -> void:
	var title = Label.new()
	title.text = "Choose Your Class"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	class_content.add_child(title)

	for cls in CLASSES:
		var btn = _create_selection_button(cls["name"], cls["description"])
		btn.pressed.connect(_on_class_selected.bind(cls, btn))
		class_content.add_child(btn)

func _on_class_selected(cls: Dictionary, btn: Button) -> void:
	selected_class = cls
	_highlight_button(btn, selected_class_button)
	selected_class_button = btn
	print("[CharacterCreation] Selected class: ", cls["name"])

# ══════════════════════════════════════════════════════════════════════════════
# STEP 2: BACKGROUND SELECTION
# ══════════════════════════════════════════════════════════════════════════════

func _build_background_step() -> void:
	var title = Label.new()
	title.text = "Choose Your Background"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	background_content.add_child(title)

	for bg in BACKGROUNDS:
		var skills_text = "Skills: " + ", ".join(bg["skills"])
		var btn = _create_selection_button(bg["name"], bg["description"] + "\n" + skills_text)
		btn.pressed.connect(_on_background_selected.bind(bg, btn))
		background_content.add_child(btn)

func _on_background_selected(bg: Dictionary, btn: Button) -> void:
	selected_background = bg
	_highlight_button(btn, selected_background_button)
	selected_background_button = btn
	print("[CharacterCreation] Selected background: ", bg["name"])

# ══════════════════════════════════════════════════════════════════════════════
# STEP 3: ABILITY SCORES
# ══════════════════════════════════════════════════════════════════════════════

func _build_ability_step() -> void:
	# Clear previous content so we can rebuild (e.g. after race selection changes)
	for child in ability_content.get_children():
		child.queue_free()
	ability_option_buttons.clear()

	var title = Label.new()
	title.text = "Assign Ability Scores"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	ability_content.add_child(title)

	var subtitle = Label.new()
	subtitle.text = "Standard Array: " + str(STANDARD_ARRAY) + "\nAssign each score to an ability. Racial bonuses will be applied automatically."
	subtitle.add_theme_font_size_override("font_size", 14)
	subtitle.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
	subtitle.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	ability_content.add_child(subtitle)

	# Create dropdowns for each ability
	for ability in ABILITY_NAMES:
		var hbox = HBoxContainer.new()
		hbox.add_theme_constant_override("separation", 12)

		var label = Label.new()
		label.text = ability
		label.custom_minimum_size = Vector2(150, 0)
		label.add_theme_font_size_override("font_size", 16)
		label.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
		hbox.add_child(label)

		var option_btn = OptionButton.new()
		option_btn.custom_minimum_size = Vector2(100, 40)
		option_btn.add_theme_font_size_override("font_size", 14)
		option_btn.name = "Option_" + ability

		# Add placeholder and available scores.
		# Use index 0 as the unassigned placeholder (id=0 via auto-assign).
		option_btn.add_item("--")
		for score in STANDARD_ARRAY:
			option_btn.add_item(str(score), score)

		# Restore previous selection if user navigated back to this step
		var prev_score = ability_scores.get(ability, -1)
		if prev_score > 0:
			# Find the item index matching the previously assigned score
			for i in option_btn.get_item_count():
				if option_btn.get_item_id(i) == prev_score:
					option_btn.select(i)
					break

		option_btn.item_selected.connect(_on_ability_score_selected.bind(ability, option_btn))
		hbox.add_child(option_btn)

		# Store reference for duplicate-score reset
		ability_option_buttons[ability] = option_btn

		# Show racial bonus if applicable
		if not selected_race.is_empty():
			var bonus = _get_ability_bonus(ability)
			if bonus > 0:
				var bonus_label = Label.new()
				bonus_label.text = "(+%d racial bonus)" % bonus
				bonus_label.add_theme_font_size_override("font_size", 12)
				bonus_label.add_theme_color_override("font_color", Color(0.4, 0.8, 0.4))
				hbox.add_child(bonus_label)

		ability_content.add_child(hbox)

func _on_ability_score_selected(index: int, ability: String, option_btn: OptionButton) -> void:
	if index == 0:
		# Selected the "--" placeholder — deselect this ability
		ability_scores.erase(ability)
	else:
		var selected_value = option_btn.get_item_id(index)

		# Check if this score is already assigned to another ability
		for other_ability in ability_scores.keys():
			if other_ability != ability and ability_scores[other_ability] == selected_value:
				# Already assigned — unassign it
				ability_scores.erase(other_ability)
				var other_option = ability_option_buttons.get(other_ability)
				if other_option and is_instance_valid(other_option):
					other_option.select(0)  # Reset to "--"

		ability_scores[ability] = selected_value

	print("[CharacterCreation] Ability scores: ", ability_scores)

func _get_ability_bonus(ability: String) -> int:
	if selected_race.is_empty():
		return 0

	var bonuses = selected_race.get("bonuses", {})
	var key = ability.to_lower().substr(0, 3)  # "str", "dex", etc.
	return bonuses.get(key, 0)

# ══════════════════════════════════════════════════════════════════════════════
# STEP 4: SKILL SELECTION
# ══════════════════════════════════════════════════════════════════════════════

func _build_skill_step() -> void:
	# Clear previous content
	for child in skill_content.get_children():
		child.queue_free()

	selected_skills.clear()

	var title = Label.new()
	title.text = "Choose Your Skills"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	skill_content.add_child(title)

	var required = selected_class.get("skills_count", 2)
	var subtitle = Label.new()
	subtitle.text = "Select %d skills from your class list. Background skills are applied automatically." % required
	subtitle.add_theme_font_size_override("font_size", 14)
	subtitle.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
	subtitle.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	skill_content.add_child(subtitle)

	var available_skills = selected_class.get("skills", [])

	for skill in available_skills:
		var check = CheckBox.new()
		check.text = skill
		check.add_theme_font_size_override("font_size", 16)
		check.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
		check.toggled.connect(_on_skill_toggled.bind(skill, check))
		skill_content.add_child(check)

func _on_skill_toggled(toggled_on: bool, skill: String, check: CheckBox) -> void:
	var required = selected_class.get("skills_count", 2)

	if toggled_on:
		if selected_skills.size() >= required:
			# Already at max — untoggle
			check.set_pressed_no_signal(false)
			_show_error("You can only select %d skills." % required)
			return
		selected_skills.append(skill)
	else:
		selected_skills.erase(skill)

	print("[CharacterCreation] Selected skills: ", selected_skills)

# ══════════════════════════════════════════════════════════════════════════════
# STEP 5: NAME & DETAILS
# ══════════════════════════════════════════════════════════════════════════════

func _build_name_step() -> void:
	# Clear previous content
	for child in name_content.get_children():
		child.queue_free()

	var title = Label.new()
	title.text = "Name Your Character"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	name_content.add_child(title)

	# Name input
	var name_label = Label.new()
	name_label.text = "Character Name:"
	name_label.add_theme_font_size_override("font_size", 16)
	name_label.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	name_content.add_child(name_label)

	var name_edit = LineEdit.new()
	name_edit.custom_minimum_size = Vector2(400, 40)
	name_edit.add_theme_font_size_override("font_size", 16)
	name_edit.placeholder_text = "Enter name..."
	name_edit.text = character_name
	name_edit.text_changed.connect(_on_name_changed)
	name_content.add_child(name_edit)

	# Description input
	var desc_label = Label.new()
	desc_label.text = "Brief Description (optional):"
	desc_label.add_theme_font_size_override("font_size", 16)
	desc_label.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	name_content.add_child(desc_label)

	var desc_edit = TextEdit.new()
	desc_edit.custom_minimum_size = Vector2(400, 120)
	desc_edit.add_theme_font_size_override("font_size", 14)
	desc_edit.placeholder_text = "Describe your character's appearance, personality, or backstory..."
	desc_edit.text = character_description
	desc_edit.text_changed.connect(_on_description_changed.bind(desc_edit))
	desc_edit.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	name_content.add_child(desc_edit)

func _on_name_changed(new_text: String) -> void:
	character_name = new_text

func _on_description_changed(desc_edit: TextEdit) -> void:
	character_description = desc_edit.text

# ══════════════════════════════════════════════════════════════════════════════
# STEP 6: SUMMARY
# ══════════════════════════════════════════════════════════════════════════════

func _build_summary_step() -> void:
	# Clear previous content
	for child in summary_content.get_children():
		child.queue_free()

	var title = Label.new()
	title.text = "Character Summary"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color(0.788, 0.635, 0.153))
	summary_content.add_child(title)

	# Name
	_add_summary_section("Name", character_name)

	# Race
	_add_summary_section("Race", selected_race["name"])

	# Class
	_add_summary_section("Class", "%s (Hit Die: d%d)" % [selected_class["name"], selected_class["hit_die"]])

	# Background
	_add_summary_section("Background", selected_background["name"])

	# Ability Scores (with racial bonuses)
	var abilities_text = ""
	for ability in ABILITY_NAMES:
		var base_score = ability_scores.get(ability, 0)
		var bonus = _get_ability_bonus(ability)
		var final_score = base_score + bonus
		var modifier = int((final_score - 10) / 2)
		var mod_sign = "+" if modifier >= 0 else ""
		abilities_text += "%s: %d (%s%d)\n" % [ABILITY_ABBREV[ability], final_score, mod_sign, modifier]
	_add_summary_section("Ability Scores", abilities_text.strip_edges())

	# Skills
	var all_skills = selected_skills.duplicate()
	all_skills.append_array(selected_background.get("skills", []))
	_add_summary_section("Skill Proficiencies", ", ".join(all_skills))

	# Calculated stats
	var con_modifier = int((ability_scores.get("Constitution", 10) + _get_ability_bonus("Constitution") - 10) / 2)
	var hp = selected_class["hit_die"] + con_modifier
	hp = max(hp, 1)

	var dex_modifier = int((ability_scores.get("Dexterity", 10) + _get_ability_bonus("Dexterity") - 10) / 2)
	var ac = 10 + dex_modifier

	_add_summary_section("Hit Points", str(hp))
	_add_summary_section("Armor Class", str(ac))

	if not character_description.is_empty():
		_add_summary_section("Description", character_description)

func _add_summary_section(label_text: String, value_text: String) -> void:
	var label = Label.new()
	label.text = label_text + ":"
	label.add_theme_font_size_override("font_size", 16)
	label.add_theme_color_override("font_color", Color(0.788, 0.635, 0.153))
	summary_content.add_child(label)

	var value = Label.new()
	value.text = value_text
	value.add_theme_font_size_override("font_size", 14)
	value.add_theme_color_override("font_color", Color(0.933, 0.910, 0.835))
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	summary_content.add_child(value)

	# Spacer
	var spacer = Control.new()
	spacer.custom_minimum_size = Vector2(0, 8)
	summary_content.add_child(spacer)

# ══════════════════════════════════════════════════════════════════════════════
# CHARACTER CREATION
# ══════════════════════════════════════════════════════════════════════════════

func _create_character() -> void:
	var character_data = {
		"name": character_name,
		"race": selected_race["name"],
		"character_class": selected_class["name"],
		"background": selected_background["name"],
		"ability_scores": _get_final_ability_scores(),
		"skills": _get_all_skills(),
		"description": character_description,
		"hit_die": selected_class["hit_die"]
	}

	print("[CharacterCreation] Character created: ", character_data)
	character_created.emit()

func _get_final_ability_scores() -> Dictionary:
	var final_scores = {}
	for ability in ABILITY_NAMES:
		var base_score = ability_scores.get(ability, 10)
		var bonus = _get_ability_bonus(ability)
		final_scores[ability] = base_score + bonus
	return final_scores

func _get_all_skills() -> Array:
	var all_skills = selected_skills.duplicate()
	all_skills.append_array(selected_background.get("skills", []))
	return all_skills

# ══════════════════════════════════════════════════════════════════════════════
# UI HELPERS
# ══════════════════════════════════════════════════════════════════════════════

func _create_selection_button(title: String, description: String) -> Button:
	var btn = Button.new()
	btn.custom_minimum_size = Vector2(0, 80)

	# Use VBox to display title and description
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var title_label = Label.new()
	title_label.text = title
	title_label.add_theme_font_size_override("font_size", 18)
	title_label.add_theme_color_override("font_color", Color(0.95, 0.82, 0.35))
	title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(title_label)

	var desc_label = Label.new()
	desc_label.text = description
	desc_label.add_theme_font_size_override("font_size", 13)
	desc_label.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(desc_label)

	btn.add_child(vbox)

	# Apply theme styles
	btn.add_theme_stylebox_override("normal", _get_custom_stylebox("StyleBox_selection_normal"))
	btn.add_theme_stylebox_override("hover", _get_custom_stylebox("StyleBox_selection_hover"))
	btn.add_theme_stylebox_override("pressed", _get_custom_stylebox("StyleBox_selection_pressed"))
	btn.add_theme_stylebox_override("focus", _get_custom_stylebox("StyleBox_selection_hover"))

	return btn

func _highlight_button(selected: Button, previous: Button) -> void:
	# Reset previous button
	if previous and is_instance_valid(previous):
		previous.add_theme_stylebox_override("normal", _get_custom_stylebox("StyleBox_selection_normal"))

	# Highlight selected button
	if selected:
		selected.add_theme_stylebox_override("normal", _get_custom_stylebox("StyleBox_selection_pressed"))

var _stylebox_cache: Dictionary = {}

func _get_custom_stylebox(style_name: String) -> StyleBox:
	if _stylebox_cache.has(style_name):
		return _stylebox_cache[style_name]
	var s = StyleBoxFlat.new()
	match style_name:
		"StyleBox_step_active":
			s.bg_color = Color(0.18, 0.14, 0.04, 1.0)
			s.border_width_left = 2; s.border_width_top = 2; s.border_width_right = 2; s.border_width_bottom = 2
			s.border_color = Color(0.788, 0.635, 0.153, 1.0)
			s.corner_radius_top_left = 4; s.corner_radius_top_right = 4; s.corner_radius_bottom_left = 4; s.corner_radius_bottom_right = 4
			s.content_margin_left = 12.0; s.content_margin_top = 8.0; s.content_margin_right = 12.0; s.content_margin_bottom = 8.0
		"StyleBox_step_inactive":
			s.bg_color = Color(0.15, 0.15, 0.25, 1.0)
			s.border_width_left = 1; s.border_width_top = 1; s.border_width_right = 1; s.border_width_bottom = 1
			s.border_color = Color(0.25, 0.22, 0.15, 0.5)
			s.corner_radius_top_left = 4; s.corner_radius_top_right = 4; s.corner_radius_bottom_left = 4; s.corner_radius_bottom_right = 4
			s.content_margin_left = 12.0; s.content_margin_top = 8.0; s.content_margin_right = 12.0; s.content_margin_bottom = 8.0
		"StyleBox_selection_normal":
			s.bg_color = Color(0.11, 0.11, 0.20, 1.0)
			s.border_width_left = 1; s.border_width_top = 1; s.border_width_right = 1; s.border_width_bottom = 1
			s.border_color = Color(0.35, 0.30, 0.18, 0.7)
			s.corner_radius_top_left = 4; s.corner_radius_top_right = 4; s.corner_radius_bottom_left = 4; s.corner_radius_bottom_right = 4
			s.content_margin_left = 16.0; s.content_margin_top = 12.0; s.content_margin_right = 16.0; s.content_margin_bottom = 12.0
		"StyleBox_selection_hover":
			s.bg_color = Color(0.18, 0.16, 0.10, 1.0)
			s.border_width_left = 1; s.border_width_top = 1; s.border_width_right = 1; s.border_width_bottom = 1
			s.border_color = Color(0.788, 0.635, 0.153, 1.0)
			s.corner_radius_top_left = 4; s.corner_radius_top_right = 4; s.corner_radius_bottom_left = 4; s.corner_radius_bottom_right = 4
			s.content_margin_left = 16.0; s.content_margin_top = 12.0; s.content_margin_right = 16.0; s.content_margin_bottom = 12.0
		"StyleBox_selection_pressed":
			s.bg_color = Color(0.25, 0.20, 0.06, 1.0)
			s.border_width_left = 2; s.border_width_top = 2; s.border_width_right = 2; s.border_width_bottom = 2
			s.border_color = Color(0.95, 0.82, 0.35, 1.0)
			s.corner_radius_top_left = 4; s.corner_radius_top_right = 4; s.corner_radius_bottom_left = 4; s.corner_radius_bottom_right = 4
			s.content_margin_left = 16.0; s.content_margin_top = 12.0; s.content_margin_right = 16.0; s.content_margin_bottom = 12.0
		_:
			s.bg_color = Color(0.11, 0.11, 0.20)
	_stylebox_cache[style_name] = s
	return s
