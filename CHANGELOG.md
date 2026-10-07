# Changelog

All notable changes to AIInfluence Prism are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
