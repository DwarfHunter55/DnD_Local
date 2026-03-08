# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Chronicles Unbound** — A single-player, text-driven D&D 5e RPG with tactical top-down grid combat, built in Godot 4.3+ (C# primary, GDScript for UI). A local LLM (via Ollama REST API) serves as Dungeon Master, narrating procedurally generated campaigns and controlling 4 AI companion party members.

All D&D mechanics are sourced from the **SRD 5.1** (Creative Commons Attribution 4.0). No proprietary WotC content.

## Tech Stack

- **Engine:** Godot 4.3+ with C# (.NET)
- **LLM Runtime:** Ollama (local HTTP REST API)
- **Database:** SQLite via Microsoft.Data.Sqlite
- **Data Formats:** JSON (game data: monsters, spells, items), YAML (LLM prompt templates)
- **Target Platform:** Windows 10/11 64-bit

## Architecture

The system has five major layers (top to bottom):

1. **UI Layer** — Three main views: narrative text interface, top-down combat grid (TileMapLayer), overworld map. GDScript handles UI scenes.
2. **Game State Manager** — C# singletons: PartyManager, CombatManager, CampaignManager, InventoryManager, RelationshipManager, QuestTracker.
3. **D&D 5e Rules Engine** — Pure C# logic: dice rolling, skill checks, spell slots, action economy, conditions, death saves, leveling. No LLM dependency — all rules are deterministic.
4. **LLM Interface Layer** — Prompt builder, JSON response parser, context/memory manager, structured output enforcement. Uses a multi-prompt pipeline (narrative DM, companion personality, combat narrator, campaign planner, companion combat decisions).
5. **Persistence** — SQLite for game state; tiered LLM memory (immediate context, session summaries, campaign summaries, permanent facts).

### Key Design Decisions

- **Companion AI is dual-layer:** LLM handles personality/dialogue; utility-based scoring handles tactical combat decisions. LLM provides flavor text for combat actions.
- **LLM responses are always structured JSON** with a validation + retry + fallback pipeline (schema validation → regex extraction → re-prompt → pre-scripted fallback).
- **Context window management** uses sliding summaries: full text for last 5-10 exchanges, LLM-generated session summary, running campaign summary, and structured data from SQLite injected as permanent memory.
- **Campaign generation** follows a 3-act structure (levels 1-5, 5-10, 10-15+) with story thread tracking and a Chekhov's Gun system for narrative coherence.
- **Combat grid** uses BSP + Wave Function Collapse for dungeon generation. Encounters use D&D 5e CR-based XP thresholds.

### Companion Autonomy Levels

Companions support four control modes (player-configurable per companion): Full Control, Suggest (default — AI suggests, player approves), Trusted, Full Auto.

### Relationship System

Score range -100 to +100 per companion, with tiers (Hostile → Cold → Neutral → Friendly → Close → Devoted) that affect combat bonuses, dialogue, story branches, and epilogues.

## Build & Run

```bash
# Open project in Godot 4.3+ with .NET support
# Build via Godot editor or CLI:
godot --export-release "Windows Desktop" build/ChroniclesUnbound.exe

# Ollama must be running separately for LLM features:
ollama serve
ollama pull llama3.1:8b  # or any supported model
```

## Testing

```bash
# Unit tests use NUnit/xUnit via Godot's test runner
# Focus areas: rules engine, dice roller, combat resolution, spell system, leveling
# Run tests via Godot editor or dotnet CLI:
dotnet test
```

## Data Files

- **SRD data** (monsters, spells, items, classes, races): JSON files, sourced from 5e-bits/5e-database
- **LLM prompt templates**: YAML files with system prompts for each pipeline stage
- **Save files**: SQLite databases per save slot

## Development Phases

The project is planned in 6 phases. Check the current phase to understand what's implemented:

- **Phase 0** (Foundation): Godot project setup, Ollama integration, basic data models, text UI, dice roller, SQLite persistence
- **Phase 1** (Rules Engine): Full character creation, combat grid, turn-based combat, spells, conditions, leveling, SRD monster database
- **Phase 2** (AI Companions): Companion generation, combat AI (utility-based), action review UI, LLM personality prompts, basic relationships
- **Phase 3** (Campaign Generation): World/quest/dungeon procedural generation, act structure, story thread tracking, context management, world map
- **Phase 4** (Relationships & Polish): Full relationship system, personal quests, inventory/equipment, magic items, journal,
- **Phase 5** (Testing & Balance): Playthroughs, combat balance, LLM prompt refinement, performance optimization

## Conventions

- C# for all game logic, rules engine, and LLM integration
- GDScript for UI scenes and rapid prototyping
- SRD data stored as JSON, loaded at runtime
- All dice rolls are programmatic and displayed to the player (full transparency)
- LLM calls are always async with streaming support and "DM is thinking..." indicator
- Companion combat actions go through a review system before execution (at default autonomy)
