# AIInfluence Prism

> Route AIInfluence's AI requests to *any* LLM provider you want — OpenAI-compatible, Anthropic, Gemini, Ollama, KoboldCpp, or Player2 — with independent settings for every AI scenario.

---

## Description

AIInfluence Prism is a Mount & Blade II: Bannerlord mod that takes over the
AI calls made by the [AIInfluence](https://www.nexusmods.com/mountandblade2bannerlord)
mod and redirects them to the LLM provider of your choice.

Each AI scenario — **Dialogue, Diplomacy, Events, Memory Book, Battle Tactics,
Group Conversation, Unique AI characters** — gets its own connection profile,
so you can use a different provider, model and parameters for every situation,
or switch a single scenario back to AIInfluence's own backend.

---

## Installation instructions

1. **Install the required mods** (see *Requirements* below) and enable
   **Bannerlord Harmony**, **Bannerlord Mod Manager (MCM)** and **AIInfluence**
   in the game launcher (all three from Steam Workshop).
2. **Copy** the `AIInfluencePrism` folder into
   `Mount & Blade II Bannerlord/Modules/`.
3. **Enable** `AIInfluence Prism` in the launcher — load it **after** AIInfluence.
4. **Start the game**, open `Options → AIInfluence Prism`, pick a provider for
   each scenario and press **Test connection to provider** (or **to model**).
5. Done — Prism intercepts AI requests automatically and shows a live timer in chat.

### Building from source

```bat
build.bat -GameFolder "C:\...\Mount & Blade II Bannerlord"
```

Requires .NET SDK 8+ and a Bannerlord installation for reference assemblies.

---

## Main features

- **Any provider, one mod**: OpenAI-compatible, OpenAI+SSE (streaming), Anthropic, Google Gemini, Ollama, KoboldCpp, Player2 (local + cloud), and **Custom** (arbitrary endpoints/proxies).
- **Per-scenario profiles** — each AI scenario has its own provider, model, API key, address, timeout, temperature, max tokens, and reasoning effort.
- **Model Auto-Discovery ("Fetch")** — query your endpoint with one click to populate a live dropdown with all available models.
- **Reasoning Effort Controls** — configurable thinking level (`Default`, `None`, `Low`, `Medium`, `High`) for OpenAI o1/o3, DeepSeek R1, and Anthropic Claude 3.7 Sonnet.
- **Address Protection** — default endpoints are locked automatically; custom URLs are easily configured via `Provider: Custom`.
- **Master switch** — disable Prism entirely (falls back to AIInfluence's own backend) or per-scenario.
- **Color-coded in-game chat notices** — distinct signature colors per scenario (Dialogue, Diplomacy, Events, Battle Tactics, Memory, Group, Unique Characters).
- **Clear quota & error reporting** — friendly notifications for HTTP 429 (*Quota exceeded or subscription expired*) and HTTP 503 (*Model temporarily unavailable due to high demand*).
- **Player2 auto-login & health check** — local (`127.0.0.1:4315`) works with every field empty; cloud mode auto-logs in and adds `X-Player2-Trace-Id`.
- **Live response timer in chat**: `Request sent` → `Timer: Ns` every 10 s → green `Response received in Ns`.
- **Two connection tests per scenario**: *provider* (endpoint reachable) and *model* (one real prompt, model reply shown in chat). An empty reply still counts as a valid connection.
- **Free-form max tokens** — leave empty for no limit (provider default), or type any number.
- **JSON repair** for malformed/truncated model replies (`JsonHealer`).
- **In-game MCM screen** with a Support tab, Master tab, per-scope groups and Diagnostics (logging, JSON repair, error notices, About).

---

## Requirements

- **Mount & Blade II: Bannerlord** (any version with the `AIInfluence` mod).
- **AIInfluence** — from Steam Workshop (required).
- **Bannerlord Harmony** — from Steam Workshop (required).
- **Bannerlord Mod Manager (MCM)** — from Steam Workshop (required)
- **.NET SDK 8+** only if building from source.

> No reference assemblies are bundled — they are fetched from your game install
> the first time you build.

---

## Shout outs

- Thanks to the **Bannerlord Mod Manager (MCM)** and **Bannerlord Harmony**
  maintainers — without them, in-game settings and patching would be a nightmare.
- Thanks to everyone in the Bannerlord modding community for the help,
  feedback and inspiration.

---

## License

MIT — see [LICENSE](LICENSE).
