# Changelog

All notable changes to AIInfluence Prism are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-10-13

### Added

- **Two Independent MCM Menus**: Separated configuration into two distinct MCM option screens in-game:
  - `AIInfluence Prism: Providers`: Global active provider selection, per-provider settings for OpenAI, Anthropic, Gemini, Nvidia, Ollama, KoboldCpp, Antigravity, Player2, and Custom, with live connection testing, models discovery, and crystal logo banner.
  - `AIInfluence Prism: Scopes`: Scope routing overrides for Dialogue, Diplomacy, Events, Memory Book, Battle Tactics, Group Conversations, and Unique Characters, allowing custom per-scope routing or inheriting from the active provider.
- **Per-Provider Settings Memory & Persistence**: Switching providers in the MCM dropdown no longer overwrites or resets parameters; settings (Endpoint, Model, API Key, Timeout, Reasoning Effort, CLI Path) for each provider are stored in independent isolated configuration blocks and instantly remembered when switching back.
- **In-Game MCM Banner Widget**: Dynamically injected custom Crystal Prism logo banner at the top of the MCM options screen using GauntletUI `TextureWidget` and TaleWorlds Engine `Texture.CreateTextureFromPath`, without adding third-party UI dependencies.
- **Clean Alignment & Aspect-Ratio Scaling**: Banner automatically calculates the aspect ratio to display cleanly centered with proper bounds for both square logos and wide banners.
- **AI Image Generation Pipeline**: Native support for in-game visual scene generation:
  - Supports **Google Gemini** (multimodal generation with character reference images), **OpenAI** (DALL-E 2 / 3), **Free (Pollinations)** (Flux), and **Custom** image endpoints.
  - Dedicated MCM configuration group in `Providers` menu with API endpoint, model selector, API key (auto-inherited from text provider if left empty), and live connection probe (`Test Image Generation`).
  - Endpoint model discovery for image models (`Refresh Models from Endpoint` / `Fetch` and `Clear` buttons) automatically filtering and ranking DALL-E, Imagen, and Flux models.
  - Six Medieval Art Style presets (`Bannerlord Concept Art`, `Oil Painting`, `Dark Medieval`, `Medieval Tapestry`, `Photorealistic Fantasy`, `None (Raw Prompt)`).
  - Configurable error modes: `Image Fail Silently` toggle to prevent gameplay disruption, and automatic fallback to `Free (Pollinations)` Flux for dynamic events.
- **Concurrent Text & Image Generation for Dialogues**:
  - `DialogueSceneCoordinator` background pipeline initiates scene image generation concurrently while the LLM generates response text, drastically cutting response waiting times.
- **Character Likeness Preservation & Reference-Only Dialogue Scope**:
  - Integrated with Google Gemini's multimodal API via `inlineData (image/png)` passing captured hero face and armor render sheets.
  - Reference image caching across conversation turns so subsequent concurrent dialogue turns maintain exact character visual likeness.
  - Strictly restricts Dialogue image generation to reference-capable models (`Google Gemini`, `Custom multimodal`).
  - Text-to-image models (`OpenAI`, `Free (Pollinations)`) without photo reference capabilities are blocked in dialogues with an explicit in-game notification explaining that dialogue scenes require photo references.
  - Events scope retains full text-to-image support (`OpenAI`, `Free (Pollinations)`, `Custom`).
- **Clean Scope Group Titles**: Spaced out MCM group headers for enhanced readability: `Memory Book`, `Battle Tactics`, and `Group Conversations`.
- **Friendly Diagnostics & Quota Handling**: User-facing guidance for Gemini 429 quota exhaustion and `:predict` vs `:generateContent` method routing.

## [0.3.0] - 2026-10-10

### Added

- **Clear Fetched Models Button**: added a dedicated `Clear Fetched Models` (`Clear`) button in MCM under `Refresh Models from Endpoint` across all 7 scopes. Clicking it now completely clears the `Model` text field and resets the `Select Model` dropdown to the placeholder `(Click Fetch to load)` in the UI in-game and in configuration without restarting the game.
- **Player2 Model Guidance & Hints**: updated MCM hint text across all scopes for `Model` and `Select Model` clarifying that Player2 models are managed directly within the Player2 desktop app and should be left empty in game.
- **Built-in Two-Stage Antigravity Requests**: two-stage request pipeline (static world context/rules in stage 1, dialogue history in stage 2 via `--continue`) is now built directly into the engine and active by default for Antigravity, removing the manual toggle button from MCM.
- **Robust Antigravity CLI Path Resolution**: `ResolveCliPath` now strips surrounding quotes (e.g. from Explorer "Copy as Path"), auto-detects omitted `.exe` extensions, and strictly preserves custom paths with directory separators without silently falling back to auto-discovery.
- **Antigravity JSON & Code Fence Healing**: `AntigravityTransport` now automatically strips markdown code fences (```json ... ```) and applies `JsonHealer` to guarantee clean, valid JSON responses for AIInfluence, preventing dialogue truncation.
- **System Prompt Formatting Fix**: updated Antigravity system instructions to avoid "plain text only" conflicts, allowing models to output valid JSON while preventing tool execution.
- **Nvidia API (NVIDIA NIM) Provider**: added `Nvidia` provider option with default endpoint `https://integrate.api.nvidia.com/v1`, full OpenAI-compatible chat completions support, model discovery via `/models`, and example profile template.
- **Antigravity CLI Large Prompt Support**: prompts are now piped asynchronously through standard input (`--input-format text`) instead of command-line arguments `-p`, resolving Win32 32KB command length limits and eliminating crashes on large prompts (120k+ chars).
- **Prefix-Only Chat Colors**: in-game chat notices format only the prefix `[Prism - <scope>]` in the scope's signature color; the timer and all message text following the prefix are displayed in clean white.
- **Clean File Logs**: formatting markup and color tags are stripped before logging to disk, keeping `logs/prism_log.txt` clean and human-readable.

## [0.2.0] - 2026-10-09

### Added

- **Antigravity Provider**: added `Antigravity` provider option allowing Bannerlord AI to route prompts directly to Google Antigravity via the local CLI (`agy.exe`).
- **Antigravity MCM Configuration**: dedicated MCM configuration group before Diagnostics with configurable CLI path, model input, and dedicated "Test CLI" (version check) and "Test Model" (live prompt ping) buttons.
- **Reasoning Effort Dropdown**: added per-scope reasoning / thinking effort dropdown (`Default`, `None`, `Low`, `Medium`, `High`) sent to supported models (OpenAI o1/o3, DeepSeek R1, Claude 3.7 extended thinking, Antigravity effort).
- **Custom Provider**: added `Custom` provider option for arbitrary endpoints and custom OpenAI-compatible proxies.
- **Model Discovery & Dropdown**: added endpoint model querying (`Fetch` button) and `Select Model` dropdown per scope.
- **Color-coded Scopes**: scope-specific colors for in-game chat notices (Dialogue, Diplomacy, Events, Memory, Battle Tactics, Group Conversation, Unique Characters).
- **HTTP 429 & 503 Friendly Messages**: specific user-facing chat notices for quota expiration (HTTP 429) and provider overload (HTTP 503).
- **MCM Live Dropdown Sync**: in-place item list updates in MCM UI without requiring game restart.

### Changed

- **Gemini Health Probe Fix**: connection test for Google Gemini now correctly targets the models endpoint (`/v1beta/models`), eliminating HTTP 404 errors caused by sending GET to Google's POST-only `:generateContent` endpoint.
- **UI Capitalization Overhaul**: full Title Case / Capitalization and proper casing across all MCM setting names, group titles, button texts, dropdown options (`Player2`, `OpenAI`, `OpenAI-SSE`, `Gemini`, `Anthropic`, `Ollama`, `KoboldCpp`, `Custom`), and error messages.
- **API Address Protection**: API address input is locked for standard providers (default canonical address used automatically). Editing is allowed only when `Provider` is set to `Custom`. Switching away from `Custom` clears the field.
- **Model Selection & Fallback**: selecting a model in the dropdown immediately populates the `Model` text field; if `Model` text is left empty, the pipeline and connection tests automatically fall back to the dropdown selection.
- **Model Text Field Typing Fix**: typing in `Model` text box no longer corrupts or prepends text fragments into the dropdown.
- **Max Tokens Template**: default template leaves `maxTokens` empty (0 = unlimited by default, let model/provider decide).
- **Request Timeout**: default timeout raised to 180–300s to accommodate slow reasoning models.

## [0.1.0] - 2026-10-07

### Added

- Route AI requests through any provider: OpenAI-compatible, OpenAI+SSE,
  Anthropic, Google Gemini, Ollama, KoboldCpp and **Player2** (local + cloud).
- Declarative scope routing via `ScopeCatalog`: Dialogue, Diplomacy, Events,
  MemoryBook, BattleTactics, GroupConversation, UniqueCharacters.
- Per-connection profile in `Config/prism_profiles.json`: provider, root URL,
  API key, model, temperature, max tokens, timeout.
- Player2 auto-login (POST `/v1/login/web/{clientId}`), health check endpoint,
  `X-Player2-Trace-Id` header, empty model field support.
- Automatic URL normalisation (EndpointMapper): base address is enough,
  the chat path is appended automatically.
- Timer in chat (`[Prism - <scope>] Timer: 156s`).
- Progress timer in chat: "request sent" → "still waiting... Ns" every 10 s →
  green "response received in Ns (N chars)".
- Two connection tests per scope: **provider** probe (endpoint reachable) and
  **model** probe (one small real prompt, model reply shown in chat).
- Max tokens as a free-text field (no slider cap; long responses such as
  16k–40k tokens are supported).
- Per-scope timeouts up to 3600 s (1 hour).
- Max tokens = 0 means "no limit": the token field is omitted from the
  request body (provider default is used).
- Model connection test: an empty reply counts as a valid connection
  (shown in green); the hint explains this.
- Temperature is rounded to at most 2 decimals in the UI.
- Per-scope timeout slider range is 180–600 s.
- JSON repair for malformed or truncated model responses (JsonHealer).
- SSE stream accumulation for OpenAI-streaming providers.
- Harmony interception of all AIInfluence.API.AIClient entry points
  (GetAIResponse, GetRawTextResponse, GetRawTextResponseWithBackend,
  and direct `Get*Response` backend calls).
- Call-stack probe + marker-file detection (SHA-256 cached) for scope
  recognition.
- MCM settings screen with "Test connection" button per profile.
- Periodic elapsed-time logging (every 60s) and final flush on unload.
- Verbose logging to `logs/prism_log.txt`.

### Notes

- Requires AIInfluence (Workshop), Bannerlord Harmony and Bannerlord Mod
  Manager. Prism intercepts AIInfluence's backend calls, so its own backend
  setting becomes irrelevant while Prism is active.
- Configuration from v0.0.1 (withdrawn mod) is **not** compatible — the new
  format is `Config/prism_profiles.json`. Reconfigure once.
