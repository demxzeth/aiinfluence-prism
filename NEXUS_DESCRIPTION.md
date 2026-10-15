# Description

**AIInfluence Prism** takes over AI calls made by the **AIInfluence** mod and redirects them to the LLM provider of your choice.

Every AI scenario — **Dialogue, Diplomacy, Events, Memory Book, Battle Tactics, Group Conversation, Unique AI Characters** — gets its own independent connection profile. You can mix and match different providers, models, reasoning efforts, and parameters for each situation, or switch any single scenario back to AIInfluence's native backend.

**Playing with Player2?** Simply select `Player2` and leave all fields empty — Prism auto-detects Player2 and handles everything automatically. No API key, address, or model needed.

**GitHub**: https://github.com/demxzeth/aiinfluence-prism

> ⚠️ **Work in Progress (WIP)**: The mod is in active development. Please back up your game saves. If you experience any issues, reach out on Discord (`@demxzeth`) or via the Nexus Mods comment/bug section.

---

# What's New in v0.3.0

- **Two-Stage Request for Antigravity**: Added toggle in MCM (`Split Request in Parts (Two-Stage)`) that sends static context (rules, character sheet, lore, events) in stage 1, then feeds dialogue history and generates responses in stage 2 using `--continue`. A single continuous chat timer tracks the entire request seamlessly.
- **Antigravity JSON & Code Fence Healing**: Automatic markdown fence (` ```json `) stripping and JSON repair to guarantee clean JSON replies for AIInfluence and eliminate dialogue truncation.
- **Nvidia API (NVIDIA NIM) Support**: Native integration with NVIDIA NIM (`https://integrate.api.nvidia.com/v1`) with full model auto-discovery.
- **Prefix-Only Chat Colors**: Only the prefix tag `[Prism - <scope>]` is tinted in the scenario's color; timer numbers and message bodies remain crisp white.
- **Large Context / Prompt Stability**: Prompts of any size (120,000+ characters) are streamed asynchronously through standard input, bypassing all command-line length limits.

---

# Main Features

- **Any provider, one mod**: OpenAI-compatible, OpenAI+SSE (streaming), Anthropic (Claude), Google Gemini, Ollama, KoboldCpp, Antigravity (CLI), Player2 (local + cloud), and Custom API endpoints.
- **Per-scenario profiles**: Each AI scenario has its own Provider, Model, API Key, API Address, Timeout, Temperature, Max Tokens, and Reasoning Effort.
- **One-click model fetch**: Click **Fetch** (`Refresh Models from Endpoint`) to pull available models directly from your provider into a selectable dropdown.
- **Reasoning / Thinking controls**: Set reasoning effort per scope to let models think deeply during diplomacy or tactics while keeping dialogue fast.
- **Color-coded live chat timer**: Live feedback on prompt processing (`Request sent` → `Timer: Ns` → green `Response received in Ns`) tinted with signature colors for each scenario (Dialogue, Diplomacy, Events, etc.).
- **Two connection tests per scenario**: 
  - **Test Connection to Provider**: Verifies that your endpoint and credentials are valid and reachable.
  - **Test Connection to Model**: Sends a minimal real prompt and displays the model's actual response in chat.
- **Auto-healing JSON repair**: Built-in `JsonHealer` repairs malformed or truncated JSON replies before AIInfluence parses them.
- **Master & per-scope switches**: Toggle Prism on/off globally or per-scenario with immediate fallback to AIInfluence's native backend.
- **Flexible token limits**: Leave `Max Tokens` empty for unlimited / provider defaults, or set any custom cap.

---

# Installation Instructions

1. Install the required mods (see Requirements below) and enable **Bannerlord Harmony**, **Bannerlord Mod Manager (MCM)**, and **AIInfluence** in your launcher.
2. Extract the `AIInfluencePrism` folder into your `Mount & Blade II Bannerlord/Modules/` directory.
3. Enable **AIInfluence Prism** in the launcher — ensure it is loaded **after** `AIInfluence`.
4. Launch the game, open **Options → Mod Options → AIInfluence Prism**.
5. Select your desired provider from the dropdown, enter your API key, click **Fetch** to pick a model (or type one in), and press **Test Connection to Provider** / **Test Connection to Model**.
6. Done! Prism will automatically route AI prompts and display live status in chat.

---

# Requirements

- **Mount & Blade II: Bannerlord** (v1.2.x+ / compatible with AIInfluence)
- [AIInfluence](https://www.nexusmods.com/mountandblade2bannerlord/mods/7057) (Required)
- [Bannerlord.Harmony](https://www.nexusmods.com/mountandblade2bannerlord/mods/2006) (Required)
- [MBOptionScreen (MCMv5)](https://www.nexusmods.com/mountandblade2bannerlord/mods/612) (Required)

---

# Shout Outs

- Thanks to the **AIInfluence** author for the groundbreaking mod that brings Bannerlord NPCs to life.
- Thanks to the **Bannerlord Mod Manager (MCM)** and **Harmony** teams — without them, clean in-game UI and robust patching wouldn't be possible.
- *AIInfluence Prism is an independent project built from scratch. It does not share code with third-party or legacy add-ons.*

---

# License

MIT.
