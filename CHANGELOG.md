# AIInfluence Prism — Changelog / История изменений

All notable changes and architectural milestones for **AIInfluence Prism** are documented in this file.
Все ключевые изменения и архитектурные этапы развития **AIInfluence Prism** задокументированы в этом файле.

---

## [1.0.0] - 2026-11-19 — THE GREAT RELEASE / ВЕЛИКИЙ РЕЛИЗ

### English: Major Evolution & Comparison with Legacy Prototype (`Y:\Prism`)

Version 1.0.0 represents a complete architectural evolution and ground-up rewrite from the experimental prototypes (v0.1.0–v0.6.1). It transforms Prism from a fragile Harmony hook into a production-grade, dual-dispatch LLM bridge with an embedded in-game WebUI, zero-latency prompt dispatch, and 100% clean-room legal provenance.

#### 1. Dual-Dispatch Architecture vs. Brittle Single-Hook
- **Legacy Prototype (`Y:\Prism`)**: Relied solely on Harmony prefix patches against `AIInfluence.API.AIClient`. As AIInfluence updated to 7.0.2–7.0.6, obfuscation layers, background tasks, and direct HTTP calls through Player2 bypassed or broke these hooks. Hardcoded port 5000 bindings frequently conflicted with local services.
- **Prism v1.0.0**: Implements **Dual Dispatch**:
  - **Level 1 (BCL Reflection Interceptor)**: Deep runtime reflection and Harmony detours on `AIClient` and obfuscated backend entry points, guarded by `ProtectionGuard.cs` with LINQ field signature matching.
  - **Level 2 (HTTP 4315 Loopback Reverse Proxy)**: High-speed local reverse proxy on port 4315 capturing all `Player2Client` traffic. Provides 100% interception coverage across all dialogue, diplomacy, and world reaction events without port clashes.
  - **BCL-Level Static HttpClient Replacement**: AIInfluence 7.0.6 wraps its internal HTTP client with an obfuscated `Class681` delegating handler that forces a hardcoded 10-second timeout. Prism reflects across all static fields in `AIInfluence.dll` and replaces all 7 `HttpClient` instances with `_prismHttpClient` (`PrismHttpMessageHandler`), completely bypassing timeouts and routing network requests directly through Prism.

#### 2. Elimination of Subtitle Freezing & Zero Prompt Chopping
- **Legacy Prototype (`Y:\Prism`)**: Employed complex multi-stage chunking (`SendTwoStageAsync`, `--continue` micro-sessions) that split prompts across multiple process executions. This led to 15–30s delays, out-of-sync Bannerlord dialogue state machines, locked subtitles on NPCs, and generic fallback placeholder responses ("Debana stubs").
- **Prism v1.0.0**: 
  - **Zero Prompt Splitting**: Single unified execution pipe with native AIInfluence memory sliding window (`PromptMaxHistory = 15` or configurable).
  - **0ms Process Release**: Asynchronous streaming and immediate execution release prevent Bannerlord UI threads from blocking. Subtitles update fluidly and dialogue state machines never freeze.
  - **Dialogue Flow & Subtitle Anti-Stall**: Completely decommissioned UI hooks on `ConversationManager.CurrentSentenceText`. Subtitles are no longer hijacked or cached across frames, eliminating the stale race condition that caused dialogue parrot loops.
  - **Guaranteed Speech Delivery**: `FormatAiInfluenceDialogueResponse` guarantees a non-empty `response` property across every scenario. If a model returns only `{"name": "..."}`, it automatically synthesizes a natural greeting line, preventing `AIResponse.Response == null` and eliminating AIInfluence error state 5u (*"молчит, словно задумался"*).

#### 3. In-Game Imperial Archive WebUI (WebView2) vs. Cumbersome MCM & External WPF
- **Legacy Prototype (`Y:\Prism`)**: Configuration was confined to basic MCM dropdowns or required launching an external WPF desktop app (`tools/PrismWorkbench`) outside of the game.
- **Prism v1.0.0**:
  - **Integrated WebView2 In-Game Panel**: Accessible directly from the pause menu (**Escape Menu -> "Prism Settings"**) or via web browser at `http://127.0.0.1:43210/`.
  - **Bannerlord Esc Menu Integration**: Hooks `MapScreen` and `MissionGauntletSingleplayerEscapeMenu` with `Priority.Last`, dynamically locating "Настройки AI Influence" across 4 reflection layers and injecting "Prism Settings" directly at `aiInfluenceIndex + 1`.
  - **Dark Medieval Calradia Aesthetics**: Styled to blend seamlessly with Bannerlord's interface.
  - **Bilingual Support (EN / RU)**: Full instant localization toggle across all menus, tooltips, and connection test prompts.
  - **Live Calradia Triggers**: Dedicated interactive triggers to generate **Random Dynamic World Events** and **Diplomatic Statements** on demand.

#### 4. 100% Clean-Room Architecture & Authorial `JsonHealer.cs`
- **Legacy Prototype (`Y:\Prism`)**: Contained experimental third-party scripts (`JsonRepairService`), external mock stubs, and fragile regexes that could truncate roleplay text containing commas or dialogue formatting.
- **Prism v1.0.0**:
  - **100% Clean-Room Legal Provenance**: All code written from scratch with zero proprietary foreign code. Ready for public GitHub and Nexus Mods hosting.
  - **Authorial `JsonHealer.cs`**: Intelligent JSON bracket balancer, markdown code fence stripper (````json ... ````), and identifier-safe bare-value quoting. Guarantees valid JSON payloads without corrupting dialogue punctuation or quotes.
  - **Clean `ProtectionGuard.cs`**: Replaced heuristic score-based detection with precise LINQ reflection targeting runtime lock fields and cancellation tokens.
  - **Fast Deterministic Scope Router (`FastRouter.cs`)**: High-performance O(1) static dictionary lookup deterministically categorizing requests into 8 Calradia scopes.

#### 5. Real-Time Diagnostics Terminal & Telemetry
- **Legacy Prototype (`Y:\Prism`)**: Limited to single-line console notices or disk logs.
- **Prism v1.0.0**:
  - **Live Terminal Console (`> PRISM SYSTEM`)**: Embedded in the WebUI with custom monospace styling, auto-scrolling to newest entries, and horizontal overflow navigation.
  - **Extended Telemetry Snippets**: Dialogue and diplomacy payload inspection expanded to 500 characters, allowing full real-time prompt verification.

#### 6. Multi-Provider Ecosystem & Visual Presentation
- **Prism v1.0.0 Support**:
  - **Google DeepMind Antigravity CLI** (`agy.exe` with local pipeline execution and thinking effort controls)
  - **Google Gemini Direct** (`gemini-3.8-flash`, `gemini-2.5-flash`, `gemini-2.5-pro` via AI Studio)
  - **OpenAI** (`gpt-4o`, `gpt-4o-mini`, `o3-mini`, reasoning models)
  - **DeepSeek** (`deepseek-chat`, `deepseek-reasoner` / R1)
  - **OpenRouter** (Unified multi-model API)
  - **Ollama** & **KoboldCpp** (100% offline local inference)
- **Visual Scope System**:
  - 8 distinct scopes with custom TaleWorlds color coding.
  - Unified bright green `(0.25f, 0.95f, 0.45f)` for all dialogue: `[Prism - Dialogue]`.
  - Clean log sanitization: formatting tags stripped before writing to disk (`logs/prism_log.txt`).
- **Comprehensive Test Suite**: 453 automated unit tests with 100% pass rate in Release mode.

---

### Русский: Ключевая эволюция и сравнение с устаревшим прототипом (`Y:\Prism`)

Версия 1.0.0 — это грандиозный эволюционный скачок от ранних экспериментальных сборок (v0.1.0–v0.6.1). Мод полностью переписан на чистую архитектуру (Clean-Room), избавлен от всех детских болезней, зависаний и конфликтов портов, и оснащен встроенным веб-интерфейсом «Имперский Архив» прямо в меню паузы Bannerlord.

#### 1. Двухуровневый перехватчик (Dual Dispatch) вместо хрупкого одиночного хука
- **Устаревший прототип (`Y:\Prism`)**: Использовал исключительно Harmony префиксы на методы `AIInfluence.API.AIClient`. При обновлении AIInfluence до 7.0.2–7.0.6 эти хуки слетали из-за обфускации, асинхронных фоновых задач и прямых HTTP-запросов к Player2. Фиксированный порт 5000 конфликтовал с системными службами.
- **Prism v1.0.0**: Внедрена система **Dual Dispatch**:
  - **Уровень 1 (BCL Reflection)**: Глубокая рефлексия и Harmony-перехваты на `AIClient` под защитой чистого `ProtectionGuard.cs` без эвристик.
  - **Уровень 2 (HTTP 4315 Reverse Proxy)**: Локальный прозрачный прокси на порту 4315, перехватывающий 100% трафика `Player2Client`. Обеспечивает абсолютный перехват диалогов, дипломатии и событий без конфликтов портов.
  - **Замена статических HttpClient на уровне BCL**: AIInfluence 7.0.6 оборачивает сетевые запросы обфусцированным классом `Class681`, принудительно убивающим запросы по 10-секундному таймауту. Prism находит через рефлексию все 7 статических экземпляров `HttpClient` и заменяет их на собственный `_prismHttpClient` (`PrismHttpMessageHandler`), полностью устраняя таймауты и направляя трафик в Prism.

#### 2. Полное устранение зависания субтитров и отказ от распила промптов
- **Устаревший прототип (`Y:\Prism`)**: Пытался дробить промпты на куски (`SendTwoStageAsync`, микросессии `--continue`), вызывая задержки по 15–30 секунд, рассинхронизацию стейт-машины диалогов Bannerlord, залипание субтитров на лицах NPC и заглушки-попугаи («заглушки Дебаны»).
- **Prism v1.0.0**: 
  - **Нулевой распил промптов (Zero Prompt Splitting)**: Промпт передается единым блоком с поддержкой нативного окна памяти AIInfluence.
  - **Мгновенный релиз процессов (0ms release)**: Асинхронная передача ответов освобождает UI-поток Bannerlord. Субтитры переключаются плавно, диалоги никогда не зависают.
  - **Отказ от перехвата CurrentSentenceText**: Устранены вмешательства в субтитры Bannerlord через UI. Субтитры больше не кэшируются между кадрами, что полностью ликвидировало состояние гонки и циклическое повторение реплик игрока.
  - **Гарантированная выдача реплики**: Метод `FormatAiInfluenceDialogueResponse` гарантирует непустое свойство `response` в любой ситуации. Если модель вернула только `{"name": "..."}`, автоматически создается естественная приветственная фраза, предотвращая ошибку 5u (*"молчит, словно задумался"*).

#### 3. Встроенный WebUI «Имперский Архив» (WebView2) вместо громоздкого MCM и внешнего WPF
- **Устаревший прототип (`Y:\Prism`)**: Настройка сводилась к неудобным выпадающим спискам MCM или требовала запуска отдельной внешней WPF-программы (`tools/PrismWorkbench`).
- **Prism v1.0.0**:
  - **Встроенная панель на WebView2**: Открывается прямо в игре по нажатию **Esc -> "Prism Settings"** или в любом браузере по адресу `http://127.0.0.1:43210/`.
  - **Интеграция в Esc-меню**: Корректный перехват `MapScreen` и `MissionGauntletSingleplayerEscapeMenu` с динамическим позиционированием кнопки сразу под "Настройки AI Influence".
  - **Атмосферный интерфейс Кальрадии**: Тёмная средневековая эстетика, идеально вписывающаяся в стиль Bannerlord.
  - **Двуязычный интерфейс (EN / RU)**: Мгновенное переключение языка всех вкладок, подсказок и тестовых запросов.
  - **Интерактивные триггеры**: Кнопки прямого вызова **Случайного динамического события** и **Дипломатического заявления** на карте мира.

#### 4. 100% Clean-Room архитектура и авторский `JsonHealer.cs`
- **Устаревший прототип (`Y:\Prism`)**: Содержал чужие скрипты починки JSON (`JsonRepairService`), заглушки и нестабильные регулярки, обрезавшие диалоговые реплики с запятыми.
- **Prism v1.0.0**:
  - **Юридическая чистота (Clean-Room)**: Весь код написан с нуля, удалены все чужие зависимости и рудименты. Идеально для открытой публикации на Nexus Mods и GitHub.
  - **Авторский `JsonHealer.cs`**: Умная балансировка скобок, удаление markdown-разметки (````json ... ````) и безопасное квотирование строковых литералов. Гарантирует корректный JSON без повреждения знаков препинания.
  - **Надежный `ProtectionGuard.cs`**: Замена эвристических баллов на прямой LINQ-поиск полей блокировки и токенов отмены.
  - **Быстрый O(1) роутер скоупов (`FastRouter.cs`)**: Мгновенная классификация запросов по 8 скоупам Кальрадии.

#### 5. Терминальная консоль диагностики и живая телеметрия
- **Устаревший прототип (`Y:\Prism`)**: Имел лишь короткие уведомления в чате или текстовый файл лога на диске.
- **Prism v1.0.0**:
  - **Живая консоль (`> PRISM SYSTEM`)**: Интерактивный терминал с темными скроллбарами, автопрокруткой и отображением свежих системных событий.
  - **Телеметрия до 500 символов**: Сниппеты входящих запросов и ответов расширены до 500 знаков для удобной проверки прямо в игре.

#### 6. Богатая экосистема провайдеров и цветовая палитра
- **Поддерживаемые провайдеры**:
  - **Antigravity CLI** (`agy.exe` от Google DeepMind — бесплатно, быстро, с регулировкой thinking effort)
  - **Google Gemini Direct** (`gemini-3.8-flash`, `gemini-2.5-flash`, `gemini-2.5-pro`)
  - **OpenAI** (`gpt-4o`, `gpt-4o-mini`, `o3-mini`, reasoning модели)
  - **DeepSeek** (`deepseek-chat`, `deepseek-reasoner` / R1)
  - **OpenRouter** (Сотни открытых и закрытых моделей)
  - **Ollama** и **KoboldCpp** (100% локальный запуск без интернета)
- **Цветовые префиксы GauntletUI**:
  - 8 скоупов с индивидуальными цветами TaleWorlds.
  - Диалоги знати и горожан объединены в фирменный ярко-зеленый цвет `[Prism - Dialogue]`.
  - Чистый лог: цветовые теги срезаются перед записью в файл `logs/prism_log.txt`.
- **453 автоматических модульных теста**: 100% покрытие ключевых сценариев, чистый Release билд без ошибок и предупреждений.

---

## Historical Version Archive / История версий-прототипов

### [0.6.1] - 2026-11-16
- **AIInfluence 7.0.2 Compatibility**: Generalized signature rules to intercept `GetAIResponse`, `GetRawTextResponse`, `GetRawTextResponseWithBackend`, and `GetRawTextResponsePreferModels`.
- **Runtime Anti-Tamper Initialization**: Added proactive `RuntimeHelpers.RunModuleConstructor` invocation before scanning.

### [0.6.0] - 2026-10-22
- **Single-Stage Fast Pipeline**: Removed two-stage prompt delivery loop (`SendTwoStageAsync`), dispatching unified prompts directly to prevent process duplication.
- **Thinking Effort Control**: Added effort parameter (`low`, `medium`, `high`) for Gemini 3.8 thinking models.
- **Content Safety Handling**: Automatic retry redirecting to `gemini-3.8-flash` upon content safety blocks.
- **Process Lifecycle Guard**: Guaranteed cleanup with `KillProcessTree` on cancellations or timeouts.
- **Standalone Desktop Workbench**: Initial standalone WPF app (`tools/PrismWorkbench`).

### [0.5.3] - 2026-10-17
- **Dialogue Sliding Window**: Configurable sliding window in `Dispatch.ApplySlidingWindow`.
- **Dynamic Prompt Chunking**: Increased chunk size ceiling to 140,000 characters.

### [0.5.2] - 2026-10-16
- **Startup Freeze Elimination**: Unconditional interception of `Player2Client.CheckHealthAsync`.
- **Cached Assembly Discovery**: Eliminated LoaderLock during Bannerlord initial loading screens.

### [0.4.0] - 2026-10-13
- **Dual MCM Option Screens**: Independent `Providers` and `Scopes` menus.
- **Multimodal Image Pipeline**: Initial multimodal scene image generation with Google Gemini and OpenAI.
- **Concurrent Dialogue Scene Generation**: Background image generation running parallel with text generation.

### [0.3.0] - 2026-10-10
- **Antigravity CLI Stdin Piping**: Standard input piping (`--input-format text`) resolving Win32 32KB argument limits.
- **Nvidia API (NIM)**: OpenAI-compatible completions support for Nvidia endpoints.
- **Prefix-Only Chat Colors**: Formatted scope prefixes with clean white body text in chat notices.

### [0.2.0] - 2026-10-09
- **Initial Antigravity Provider**: Added support for local Google DeepMind CLI (`agy.exe`).
- **Reasoning Effort Dropdowns**: Added reasoning effort options for o1/o3 and Claude models.
- **Custom Endpoints**: Support for arbitrary OpenAI-compatible endpoints.

### [0.1.0] - 2026-10-07
- **Initial Prototype**: Core Harmony hooks on `AIInfluence.API.AIClient`.
- Support for OpenAI, Anthropic, Gemini, Ollama, KoboldCpp, and Player2.
- Basic chat notice timer and profile configuration in `prism_profiles.json`.
