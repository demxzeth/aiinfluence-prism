# AIInfluence Prism

> Route AIInfluence's AI requests to *any* LLM provider you want — OpenAI-compatible, Anthropic, Gemini, Ollama, KoboldCpp — with independent settings for every AI scenario.

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
4. **Start the game**, open `Options → AIInfluence Prism → Open WebUI settings page`, pick a provider for
   each scenario and press **Test connection to provider** (or **to model**).
                               OR
4. **Enter your campaign**, press Esc → Prism Settings. It will open WebUI page with settings
5. Done — Prism intercepts AI requests automatically and shows a live timer in chat.

### Building from source

```bat
build.bat -GameFolder "C:\...\Mount & Blade II Bannerlord"
```

Requires .NET SDK 8+ and a Bannerlord installation for reference assemblies.

---

## Caution

Currently Prism is not updating, but if you having issues with the mod - feel free to open new issue on GitHub or DM me on discord (@demxzeth).
Will gladly help you

---

## Requirements

- **Mount & Blade II: Bannerlord** (any version with the `AIInfluence` mod).
- **AIInfluence** — required.
- **Bannerlord Harmony** — from Steam Workshop (required).
- **Bannerlord Mod Manager (MCM)** — from Steam Workshop (required)
- 
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
