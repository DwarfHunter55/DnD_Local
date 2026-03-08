# Chronicles Unbound

A single-player, text-driven D&D 5e RPG with tactical top-down grid combat, built in Godot 4.3+ (C# + GDScript). A local LLM (via Ollama) serves as your Dungeon Master, narrating procedurally generated campaigns and controlling 4 AI companion party members.

All D&D mechanics are sourced from the **SRD 5.1** (Creative Commons Attribution 4.0).

---

## Prerequisites

| Software | Version | Download |
|----------|---------|----------|
| **Godot Engine (.NET)** | 4.3+ | [godotengine.org](https://godotengine.org/download) — download the **.NET** version |
| **.NET SDK** | 6.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/6.0) |
| **Ollama** | Latest | [ollama.com](https://ollama.com/download) |

> **Important:** You must download the **Godot .NET** build (not the standard build) since this project uses C#.

---

## Setup & Running a New Campaign

### Step 1 — Install and start Ollama

1. Download and install Ollama from [ollama.com](https://ollama.com/download).
2. Open a terminal and pull a language model:
   ```bash
   ollama pull llama3.1:8b
   ```
   This downloads the ~4.7 GB model. You can also use other models (e.g., `mistral`, `llama3.2`, `gemma2`) — just update the model name in the game settings if needed.
3. Make sure Ollama is running:
   ```bash
   ollama serve
   ```
   Ollama typically runs automatically in the background after installation. You can verify it's active by visiting `http://localhost:11434` in your browser — you should see "Ollama is running".

### Step 2 — Open the project in Godot

1. Download and install [Godot 4.3+ .NET](https://godotengine.org/download).
2. Open Godot and click **Import** → navigate to the project folder → select `project.godot` → click **Import & Edit**.
3. Wait for Godot to import all assets and compile the C# solution. This may take a minute on first load.

### Step 3 — Run the game

1. Press **F5** in the Godot editor (or click the Play button in the top-right).
2. The **Main Menu** will appear.

### Step 4 — Start a new campaign

1. Click **New Game** on the Main Menu.
2. Walk through the **Character Creation** wizard (7 steps):
   - **Race** — Choose your character's race (Human, Elf, Dwarf, etc.)
   - **Class** — Choose your class (Fighter, Wizard, Rogue, etc.)
   - **Abilities** — Assign ability scores using the dropdown selectors
   - **Skills** — Pick your proficient skills
   - **Equipment** — Select starting equipment
   - **Background** — Choose your character's background story
   - **Summary** — Review and confirm your character
3. After confirmation, the game begins. The LLM Dungeon Master will generate your opening narrative.

### Step 5 — Playing the game

**Narrative Mode:**
- Read the DM's narration in the main text panel
- Type your actions/responses in the input box and press Enter
- The DM (powered by the local LLM) will respond to your choices

**Party Management:**
- Press **I** or click **Inventory** to open the inventory/equipment screen
- Press **J** or click **Journal** to open the quest journal

**Combat:**
- Combat triggers automatically during the narrative when appropriate
- Initiative is rolled and displayed in the combat log
- Use the action buttons (Attack, Move, Cast Spell, etc.) and click the grid to select targets/destinations
- Companions act based on their autonomy setting (default: Suggest mode — AI suggests, you approve)

**Saving & Loading:**
- Press **F5** or click **Save** to save your game
- From the Main Menu, click **Load Game** to resume a saved campaign

---

## Controls Summary

| Key / Action | Effect |
|--------------|--------|
| **Enter** | Submit typed action to the DM |
| **I** | Open/close Inventory |
| **J** | Open/close Journal |
| **F5** | Quick Save |
| **Mouse Click** (combat) | Select grid cell for movement/targeting |

---

## Project Structure

```
Scripts/
  Core/           # Bridge nodes, managers, scene management
  Combat/         # Combat system (grid, turns, AI)
  Characters/     # Character data, leveling, abilities
  Companions/     # AI companion behavior, personality
  Exploration/    # World exploration, NPCs, shops, quests
  LLM/            # Ollama integration, prompt building
  Data/           # SQLite persistence
  UI/             # GDScript UI controllers
Scenes/
  UI/             # UI scene files (.tscn)
  Combat/         # Combat scene files
Data/
  SRD/            # D&D 5e SRD data (JSON): monsters, spells, items, classes, races
  World/          # World data: locations, NPCs, quests, shops
Tests/            # NUnit test project (280 tests)
```

---

## Running Tests

```bash
cd Tests
dotnet test
```

Requires .NET SDK 6.0+. The test project uses GodotStubs to compile without the Godot editor.

---

## Tech Stack

- **Engine:** Godot 4.3+ with C# (.NET 6.0)
- **LLM Runtime:** Ollama (local HTTP REST API at `localhost:11434`)
- **Database:** SQLite via Microsoft.Data.Sqlite
- **Data:** JSON (SRD game data), YAML (LLM prompt templates)
- **Target Platform:** Windows 10/11 64-bit

---

## License

D&D game mechanics use the **SRD 5.1** under the Creative Commons Attribution 4.0 International License.
