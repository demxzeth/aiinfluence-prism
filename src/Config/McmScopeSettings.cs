using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// MCM Screen 2: Scope Routing.
    /// Manages per-scope configurations for all 7 game scopes:
    /// Dialogue, Diplomacy, Events, Memory Book, Battle Tactics, Group Conversations, Unique Characters.
    /// Includes contextual image generation controls for Dialogue and Events.
    /// </summary>
    public sealed class McmScopeSettings : AttributeGlobalSettings<McmScopeSettings>
    {
        static McmScopeSettings()
        {
            ScopeSettings.FallbackModelResolver = s => GetSelectedDropdownModel(s);
        }

        public override string Id => "AIInfluencePrism_v1_Scopes";
        public override string DisplayName => "AIInfluence Prism - Scopes";
        public override string FolderName => "AIInfluencePrism";
        public override string FormatType => "json";

        // ─── 1. Dialogue ──────────────────────────────────────

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool DialogueEnabled { get => Scope(ScopeId.Dialogue).Enabled; set => SetScopeEnabled(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> DialogueProviderDropdown { get => GetScopeProviderDropdown(ScopeId.Dialogue); set => SetScopeProviderDropdown(ScopeId.Dialogue, value); }

        public string DialogueProvider { get => GetScopeProvider(ScopeId.Dialogue); set => SetScopeProvider(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string DialogueModel { get => Scope(ScopeId.Dialogue).Model; set => SetScopeModel(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> DialogueModelDropdown { get => GetScopeModelDropdown(ScopeId.Dialogue); set => SetScopeModelDropdown(ScopeId.Dialogue, value); }

        private Action _fetchDialogueModels;

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchDialogueModels
        {
            get { return _fetchDialogueModels ?? (_fetchDialogueModels = () => FetchScopeModels(ScopeId.Dialogue)); }
            set { _fetchDialogueModels = value; }
        }

        private Action _clearDialogueModels;

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearDialogueModels
        {
            get { return _clearDialogueModels ?? (_clearDialogueModels = () => ClearScopeModels(ScopeId.Dialogue)); }
            set { _clearDialogueModels = value; }
        }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int DialogueTimeout { get => Scope(ScopeId.Dialogue).TimeoutSeconds; set => SetScopeTimeout(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool DialogueTimer { get => Scope(ScopeId.Dialogue).TimerInChat; set => SetScopeTimer(ScopeId.Dialogue, value); }

        private Action _testDialogueModel;

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestDialogueModel
        {
            get { return _testDialogueModel ?? (_testDialogueModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.Dialogue))); }
            set { _testDialogueModel = value; }
        }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float DialogueTemperature { get => (float)Scope(ScopeId.Dialogue).Temperature; set => SetScopeTemperature(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string DialogueMaxTokens
        {
            get { return Scope(ScopeId.Dialogue).MaxTokens > 0 ? Scope(ScopeId.Dialogue).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.Dialogue, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.Dialogue, parsed);
            }
        }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> DialogueReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.Dialogue); set => SetScopeReasoningDropdown(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyBool("Generate Dialogue Scene Images", Order = 13, RequireRestart = false,
            HintText = "When on, generates contextual scene / character images during dialogue.")]
        public bool DialogueGenerateImages { get => Scope(ScopeId.Dialogue).GenerateImages; set => SetScopeGenerateImages(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 1)]
        [SettingPropertyDropdown("Image Generation Provider", Order = 14, RequireRestart = false,
            HintText = "Image provider for dialogue scenes. Only reference-capable multimodal models (Google Gemini, Custom) are permitted to maintain character likeness.")]
        public Dropdown<string> DialogueImageProviderDropdown { get => GetScopeImageProviderDropdown(ScopeId.Dialogue); set => SetScopeImageProviderDropdown(ScopeId.Dialogue, value); }

        // ─── 2. Diplomacy ─────────────────────────────────────

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool DiplomacyEnabled { get => Scope(ScopeId.Diplomacy).Enabled; set => SetScopeEnabled(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> DiplomacyProviderDropdown { get => GetScopeProviderDropdown(ScopeId.Diplomacy); set => SetScopeProviderDropdown(ScopeId.Diplomacy, value); }

        public string DiplomacyProvider { get => GetScopeProvider(ScopeId.Diplomacy); set => SetScopeProvider(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string DiplomacyModel { get => Scope(ScopeId.Diplomacy).Model; set => SetScopeModel(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> DiplomacyModelDropdown { get => GetScopeModelDropdown(ScopeId.Diplomacy); set => SetScopeModelDropdown(ScopeId.Diplomacy, value); }

        private Action _fetchDiplomacyModels;

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchDiplomacyModels
        {
            get { return _fetchDiplomacyModels ?? (_fetchDiplomacyModels = () => FetchScopeModels(ScopeId.Diplomacy)); }
            set { _fetchDiplomacyModels = value; }
        }

        private Action _clearDiplomacyModels;

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearDiplomacyModels
        {
            get { return _clearDiplomacyModels ?? (_clearDiplomacyModels = () => ClearScopeModels(ScopeId.Diplomacy)); }
            set { _clearDiplomacyModels = value; }
        }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int DiplomacyTimeout { get => Scope(ScopeId.Diplomacy).TimeoutSeconds; set => SetScopeTimeout(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool DiplomacyTimer { get => Scope(ScopeId.Diplomacy).TimerInChat; set => SetScopeTimer(ScopeId.Diplomacy, value); }

        private Action _testDiplomacyModel;

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestDiplomacyModel
        {
            get { return _testDiplomacyModel ?? (_testDiplomacyModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.Diplomacy))); }
            set { _testDiplomacyModel = value; }
        }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float DiplomacyTemperature { get => (float)Scope(ScopeId.Diplomacy).Temperature; set => SetScopeTemperature(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string DiplomacyMaxTokens
        {
            get { return Scope(ScopeId.Diplomacy).MaxTokens > 0 ? Scope(ScopeId.Diplomacy).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.Diplomacy, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.Diplomacy, parsed);
            }
        }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 2)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> DiplomacyReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.Diplomacy); set => SetScopeReasoningDropdown(ScopeId.Diplomacy, value); }

        // ─── 3. Events ────────────────────────────────────────

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool EventsEnabled { get => Scope(ScopeId.Events).Enabled; set => SetScopeEnabled(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> EventsProviderDropdown { get => GetScopeProviderDropdown(ScopeId.Events); set => SetScopeProviderDropdown(ScopeId.Events, value); }

        public string EventsProvider { get => GetScopeProvider(ScopeId.Events); set => SetScopeProvider(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string EventsModel { get => Scope(ScopeId.Events).Model; set => SetScopeModel(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> EventsModelDropdown { get => GetScopeModelDropdown(ScopeId.Events); set => SetScopeModelDropdown(ScopeId.Events, value); }

        private Action _fetchEventsModels;

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchEventsModels
        {
            get { return _fetchEventsModels ?? (_fetchEventsModels = () => FetchScopeModels(ScopeId.Events)); }
            set { _fetchEventsModels = value; }
        }

        private Action _clearEventsModels;

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearEventsModels
        {
            get { return _clearEventsModels ?? (_clearEventsModels = () => ClearScopeModels(ScopeId.Events)); }
            set { _clearEventsModels = value; }
        }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int EventsTimeout { get => Scope(ScopeId.Events).TimeoutSeconds; set => SetScopeTimeout(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool EventsTimer { get => Scope(ScopeId.Events).TimerInChat; set => SetScopeTimer(ScopeId.Events, value); }

        private Action _testEventsModel;

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestEventsModel
        {
            get { return _testEventsModel ?? (_testEventsModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.Events))); }
            set { _testEventsModel = value; }
        }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float EventsTemperature { get => (float)Scope(ScopeId.Events).Temperature; set => SetScopeTemperature(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string EventsMaxTokens
        {
            get { return Scope(ScopeId.Events).MaxTokens > 0 ? Scope(ScopeId.Events).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.Events, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.Events, parsed);
            }
        }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> EventsReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.Events); set => SetScopeReasoningDropdown(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyBool("Generate Event Images", Order = 13, RequireRestart = false,
            HintText = "When on, generates illustrated images for dynamic world events.")]
        public bool EventsGenerateImages { get => Scope(ScopeId.Events).GenerateImages; set => SetScopeGenerateImages(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 3)]
        [SettingPropertyDropdown("Image Generation Provider", Order = 14, RequireRestart = false,
            HintText = "Image provider for dynamic events. 'Default' uses the Image Generation Provider configured in the Providers menu.")]
        public Dropdown<string> EventsImageProviderDropdown { get => GetScopeImageProviderDropdown(ScopeId.Events); set => SetScopeImageProviderDropdown(ScopeId.Events, value); }

        // ─── 4. Memory Book ───────────────────────────────────

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool MemoryEnabled { get => Scope(ScopeId.MemoryBook).Enabled; set => SetScopeEnabled(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> MemoryProviderDropdown { get => GetScopeProviderDropdown(ScopeId.MemoryBook); set => SetScopeProviderDropdown(ScopeId.MemoryBook, value); }

        public string MemoryProvider { get => GetScopeProvider(ScopeId.MemoryBook); set => SetScopeProvider(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string MemoryModel { get => Scope(ScopeId.MemoryBook).Model; set => SetScopeModel(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> MemoryModelDropdown { get => GetScopeModelDropdown(ScopeId.MemoryBook); set => SetScopeModelDropdown(ScopeId.MemoryBook, value); }

        private Action _fetchMemoryModels;

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchMemoryModels
        {
            get { return _fetchMemoryModels ?? (_fetchMemoryModels = () => FetchScopeModels(ScopeId.MemoryBook)); }
            set { _fetchMemoryModels = value; }
        }

        private Action _clearMemoryModels;

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearMemoryModels
        {
            get { return _clearMemoryModels ?? (_clearMemoryModels = () => ClearScopeModels(ScopeId.MemoryBook)); }
            set { _clearMemoryModels = value; }
        }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int MemoryTimeout { get => Scope(ScopeId.MemoryBook).TimeoutSeconds; set => SetScopeTimeout(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool MemoryTimer { get => Scope(ScopeId.MemoryBook).TimerInChat; set => SetScopeTimer(ScopeId.MemoryBook, value); }

        private Action _testMemoryModel;

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestMemoryModel
        {
            get { return _testMemoryModel ?? (_testMemoryModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.MemoryBook))); }
            set { _testMemoryModel = value; }
        }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float MemoryTemperature { get => (float)Scope(ScopeId.MemoryBook).Temperature; set => SetScopeTemperature(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string MemoryMaxTokens
        {
            get { return Scope(ScopeId.MemoryBook).MaxTokens > 0 ? Scope(ScopeId.MemoryBook).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.MemoryBook, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.MemoryBook, parsed);
            }
        }

        [SettingPropertyGroup("Memory Book", GroupOrder = 4)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> MemoryReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.MemoryBook); set => SetScopeReasoningDropdown(ScopeId.MemoryBook, value); }

        // ─── 5. Battle Tactics ────────────────────────────────

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool BattleEnabled { get => Scope(ScopeId.BattleTactics).Enabled; set => SetScopeEnabled(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> BattleProviderDropdown { get => GetScopeProviderDropdown(ScopeId.BattleTactics); set => SetScopeProviderDropdown(ScopeId.BattleTactics, value); }

        public string BattleProvider { get => GetScopeProvider(ScopeId.BattleTactics); set => SetScopeProvider(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string BattleModel { get => Scope(ScopeId.BattleTactics).Model; set => SetScopeModel(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> BattleModelDropdown { get => GetScopeModelDropdown(ScopeId.BattleTactics); set => SetScopeModelDropdown(ScopeId.BattleTactics, value); }

        private Action _fetchBattleModels;

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchBattleModels
        {
            get { return _fetchBattleModels ?? (_fetchBattleModels = () => FetchScopeModels(ScopeId.BattleTactics)); }
            set { _fetchBattleModels = value; }
        }

        private Action _clearBattleModels;

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearBattleModels
        {
            get { return _clearBattleModels ?? (_clearBattleModels = () => ClearScopeModels(ScopeId.BattleTactics)); }
            set { _clearBattleModels = value; }
        }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int BattleTimeout { get => Scope(ScopeId.BattleTactics).TimeoutSeconds; set => SetScopeTimeout(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool BattleTimer { get => Scope(ScopeId.BattleTactics).TimerInChat; set => SetScopeTimer(ScopeId.BattleTactics, value); }

        private Action _testBattleModel;

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestBattleModel
        {
            get { return _testBattleModel ?? (_testBattleModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.BattleTactics))); }
            set { _testBattleModel = value; }
        }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float BattleTemperature { get => (float)Scope(ScopeId.BattleTactics).Temperature; set => SetScopeTemperature(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string BattleMaxTokens
        {
            get { return Scope(ScopeId.BattleTactics).MaxTokens > 0 ? Scope(ScopeId.BattleTactics).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.BattleTactics, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.BattleTactics, parsed);
            }
        }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 5)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> BattleReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.BattleTactics); set => SetScopeReasoningDropdown(ScopeId.BattleTactics, value); }

        // ─── 6. Group Conversations ───────────────────────────
 
        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool GroupEnabled { get => Scope(ScopeId.GroupConversation).Enabled; set => SetScopeEnabled(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> GroupProviderDropdown { get => GetScopeProviderDropdown(ScopeId.GroupConversation); set => SetScopeProviderDropdown(ScopeId.GroupConversation, value); }

        public string GroupProvider { get => GetScopeProvider(ScopeId.GroupConversation); set => SetScopeProvider(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string GroupModel { get => Scope(ScopeId.GroupConversation).Model; set => SetScopeModel(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> GroupModelDropdown { get => GetScopeModelDropdown(ScopeId.GroupConversation); set => SetScopeModelDropdown(ScopeId.GroupConversation, value); }

        private Action _fetchGroupModels;

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchGroupModels
        {
            get { return _fetchGroupModels ?? (_fetchGroupModels = () => FetchScopeModels(ScopeId.GroupConversation)); }
            set { _fetchGroupModels = value; }
        }

        private Action _clearGroupModels;

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearGroupModels
        {
            get { return _clearGroupModels ?? (_clearGroupModels = () => ClearScopeModels(ScopeId.GroupConversation)); }
            set { _clearGroupModels = value; }
        }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int GroupTimeout { get => Scope(ScopeId.GroupConversation).TimeoutSeconds; set => SetScopeTimeout(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool GroupTimer { get => Scope(ScopeId.GroupConversation).TimerInChat; set => SetScopeTimer(ScopeId.GroupConversation, value); }

        private Action _testGroupModel;

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestGroupModel
        {
            get { return _testGroupModel ?? (_testGroupModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.GroupConversation))); }
            set { _testGroupModel = value; }
        }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float GroupTemperature { get => (float)Scope(ScopeId.GroupConversation).Temperature; set => SetScopeTemperature(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string GroupMaxTokens
        {
            get { return Scope(ScopeId.GroupConversation).MaxTokens > 0 ? Scope(ScopeId.GroupConversation).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.GroupConversation, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.GroupConversation, parsed);
            }
        }

        [SettingPropertyGroup("Group Conversations", GroupOrder = 6)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> GroupReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.GroupConversation); set => SetScopeReasoningDropdown(ScopeId.GroupConversation, value); }

        // ─── 7. Unique Characters ─────────────────────────────

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyBool("Use Prism for This Scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool UniqueEnabled { get => Scope(ScopeId.UniqueCharacters).Enabled; set => SetScopeEnabled(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyDropdown("Provider", Order = 2, RequireRestart = false,
            HintText = "Select the provider for this scope.")]
        public Dropdown<string> UniqueProviderDropdown { get => GetScopeProviderDropdown(ScopeId.UniqueCharacters); set => SetScopeProviderDropdown(ScopeId.UniqueCharacters, value); }

        public string UniqueProvider { get => GetScopeProvider(ScopeId.UniqueCharacters); set => SetScopeProvider(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyText("Model", Order = 3, RequireRestart = false,
            HintText = "Model id; empty = provider default. For Player2: leave empty (the model is configured inside the Player2 desktop app).")]
        public string UniqueModel { get => Scope(ScopeId.UniqueCharacters).Model; set => SetScopeModel(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyDropdown("Select Model", Order = 4, RequireRestart = false,
            HintText = "Select an available model fetched from the endpoint. For Player2: model selection in-game is not required.")]
        public Dropdown<string> UniqueModelDropdown { get => GetScopeModelDropdown(ScopeId.UniqueCharacters); set => SetScopeModelDropdown(ScopeId.UniqueCharacters, value); }

        private Action _fetchUniqueModels;

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 5, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available models.")]
        public Action FetchUniqueModels
        {
            get { return _fetchUniqueModels ?? (_fetchUniqueModels = () => FetchScopeModels(ScopeId.UniqueCharacters)); }
            set { _fetchUniqueModels = value; }
        }

        private Action _clearUniqueModels;

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyButton("Clear Fetched Models", Order = 6, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the model dropdown for this scope.")]
        public Action ClearUniqueModels
        {
            get { return _clearUniqueModels ?? (_clearUniqueModels = () => ClearScopeModels(ScopeId.UniqueCharacters)); }
            set { _clearUniqueModels = value; }
        }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyInteger("Timeout (Seconds)", 60, 600, "{0}", Order = 7, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int UniqueTimeout { get => Scope(ScopeId.UniqueCharacters).TimeoutSeconds; set => SetScopeTimeout(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyBool("Response Timer in Chat", Order = 8, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool UniqueTimer { get => Scope(ScopeId.UniqueCharacters).TimerInChat; set => SetScopeTimer(ScopeId.UniqueCharacters, value); }

        private Action _testUniqueModel;

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyButton("Test Connection to Model", Order = 9, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestUniqueModel
        {
            get { return _testUniqueModel ?? (_testUniqueModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeModel(ScopeId.UniqueCharacters))); }
            set { _testUniqueModel = value; }
        }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 10, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float UniqueTemperature { get => (float)Scope(ScopeId.UniqueCharacters).Temperature; set => SetScopeTemperature(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyText("Max Tokens", Order = 11, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string UniqueMaxTokens
        {
            get { return Scope(ScopeId.UniqueCharacters).MaxTokens > 0 ? Scope(ScopeId.UniqueCharacters).MaxTokens.ToString() : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value)) { SetScopeMaxTokens(ScopeId.UniqueCharacters, 0); return; }
                if (int.TryParse(value, out int parsed) && parsed >= 0) SetScopeMaxTokens(ScopeId.UniqueCharacters, parsed);
            }
        }

        [SettingPropertyGroup("Unique Characters", GroupOrder = 7)]
        [SettingPropertyDropdown("Reasoning Effort", Order = 12, RequireRestart = false,
            HintText = "Reasoning / thinking effort level sent to supported models (e.g. OpenAI o1/o3, Claude 3.7, DeepSeek R1).")]
        public Dropdown<string> UniqueReasoningDropdown { get => GetScopeReasoningDropdown(ScopeId.UniqueCharacters); set => SetScopeReasoningDropdown(ScopeId.UniqueCharacters, value); }

        // ─── Scope Plumbing ───────────────────────────────────

        public void PullFromProfile()
        {
            Profile p = ProfileBook.Current;
            LogSink.Info("mcm[scopes]: pulled scopes from profile \"" + p.Name + "\".");
        }

        private static ScopeSettings Scope(ScopeId id)
        {
            return ProfileBook.Current.Scope(id);
        }

        private static string GetScopeProvider(ScopeId id)
        {
            ScopeSettings s = Scope(id);
            if (!string.IsNullOrEmpty(s.ProviderRaw))
            {
                return s.ProviderRaw;
            }
            return s.Provider == ProviderKind.Player2 ? string.Empty : Profile.Label(s.Provider);
        }

        private static readonly Dictionary<ScopeId, Dropdown<string>> ProviderDropdowns =
            new Dictionary<ScopeId, Dropdown<string>>();

        private static Dropdown<string> GetScopeProviderDropdown(ScopeId id)
        {
            lock (ProviderDropdowns)
            {
                Dropdown<string> dd;
                if (!ProviderDropdowns.TryGetValue(id, out dd))
                {
                    string current = Profile.Label(Scope(id).Provider);
                    int idx = Array.FindIndex(McmUiHelpers.AvailableProviders, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = 0;
                    dd = new Dropdown<string>(McmUiHelpers.AvailableProviders, idx);
                    dd.PropertyChanged += (sender, args) =>
                    {
                        SetScopeProviderDropdown(id, (Dropdown<string>)sender);
                    };
                    ProviderDropdowns[id] = dd;
                }
                else
                {
                    string current = Profile.Label(Scope(id).Provider);
                    int idx = Array.FindIndex(McmUiHelpers.AvailableProviders, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && dd.SelectedIndex != idx)
                    {
                        dd.SelectedIndex = idx;
                    }
                }
                return dd;
            }
        }

        private static void SetScopeProviderDropdown(ScopeId id, Dropdown<string> dropdown)
        {
            if (dropdown == null || dropdown.SelectedIndex < 0 || dropdown.SelectedIndex >= dropdown.Count)
            {
                return;
            }

            string selected = dropdown.SelectedValue;
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            if (!Profile.TryParseProvider(selected, out ProviderKind kind))
            {
                kind = ProviderKind.Player2;
            }

            ScopeSettings s = Scope(id);
            if (s.Provider != kind || s.ProviderRaw != selected)
            {
                s.SwitchProvider(kind, selected);
                Save();
                NotifyScopeAllProperties(id);
            }
        }

        private static void SetScopeProvider(ScopeId id, string value)
        {
            ScopeSettings s = Scope(id);
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0 || !Profile.TryParseProvider(trimmed, out ProviderKind kind))
            {
                kind = ProviderKind.Player2;
            }

            if (s.Provider != kind || s.ProviderRaw != trimmed)
            {
                s.SwitchProvider(kind, trimmed.Length > 0 ? trimmed : null);
                Save();

                lock (ProviderDropdowns)
                {
                    if (ProviderDropdowns.TryGetValue(id, out Dropdown<string> dd))
                    {
                        string lbl = Profile.Label(kind);
                        int idx = Array.FindIndex(McmUiHelpers.AvailableProviders, p => string.Equals(p, lbl, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0 && dd.SelectedIndex != idx)
                        {
                            dd.SelectedIndex = idx;
                        }
                    }
                }

                NotifyScopeAllProperties(id);
            }
        }

        private static void NotifyScopeAllProperties(ScopeId id)
        {
            try
            {
                ScopeSettings s = Scope(id);
                List<string> modelItems = new List<string>();
                int modelSelIdx = 0;
                if (s.AvailableModels != null && s.AvailableModels.Count > 0)
                {
                    modelItems.AddRange(s.AvailableModels);
                    if (!string.IsNullOrEmpty(s.Model))
                    {
                        if (!modelItems.Contains(s.Model)) modelItems.Insert(0, s.Model);
                        modelSelIdx = modelItems.IndexOf(s.Model);
                    }
                }
                else if (!string.IsNullOrEmpty(s.Model))
                {
                    modelItems.Add(s.Model);
                    modelSelIdx = 0;
                }
                else
                {
                    modelItems.Add("(Click Fetch to load)");
                    modelSelIdx = 0;
                }

                lock (ModelDropdowns)
                {
                    if (ModelDropdowns.TryGetValue(id, out Dropdown<string> mdd) && mdd != null)
                    {
                        _suppressDropdownSync = true;
                        try
                        {
                            var newDd = new Dropdown<string>(modelItems, (modelSelIdx >= 0 && modelSelIdx < modelItems.Count) ? modelSelIdx : 0);
                            newDd.PropertyChanged += (sender, args) =>
                            {
                                SetScopeModelDropdown(id, (Dropdown<string>)sender);
                            };
                            ModelDropdowns[id] = newDd;
                        }
                        finally
                        {
                            _suppressDropdownSync = false;
                        }
                    }
                }

                lock (ReasoningDropdowns)
                {
                    if (ReasoningDropdowns.TryGetValue(id, out Dropdown<string> rdd) && rdd != null)
                    {
                        string cur = s.ReasoningEffort ?? "Default";
                        int rIdx = Array.FindIndex(McmUiHelpers.ReasoningOptions, o => string.Equals(o, cur, StringComparison.OrdinalIgnoreCase));
                        if (rIdx < 0) rIdx = 0;
                        if (rdd.SelectedIndex != rIdx)
                        {
                            rdd.SelectedIndex = rIdx;
                        }
                    }
                }

                if (Instance != null)
                {
                    string modelProp = null;
                    string modelDdProp = null;
                    switch (id)
                    {
                        case ScopeId.Dialogue:
                            Instance.OnPropertyChanged(nameof(DialogueModel));
                            Instance.OnPropertyChanged(nameof(DialogueModelDropdown));
                            Instance.OnPropertyChanged(nameof(DialogueTimeout));
                            Instance.OnPropertyChanged(nameof(DialogueTemperature));
                            Instance.OnPropertyChanged(nameof(DialogueMaxTokens));
                            Instance.OnPropertyChanged(nameof(DialogueReasoningDropdown));
                            Instance.OnPropertyChanged(nameof(DialogueGenerateImages));
                            Instance.OnPropertyChanged(nameof(DialogueImageProviderDropdown));
                            modelProp = nameof(DialogueModel);
                            modelDdProp = nameof(DialogueModelDropdown);
                            break;
                        case ScopeId.Diplomacy:
                            Instance.OnPropertyChanged(nameof(DiplomacyModel));
                            Instance.OnPropertyChanged(nameof(DiplomacyModelDropdown));
                            Instance.OnPropertyChanged(nameof(DiplomacyTimeout));
                            Instance.OnPropertyChanged(nameof(DiplomacyTemperature));
                            Instance.OnPropertyChanged(nameof(DiplomacyMaxTokens));
                            Instance.OnPropertyChanged(nameof(DiplomacyReasoningDropdown));
                            modelProp = nameof(DiplomacyModel);
                            modelDdProp = nameof(DiplomacyModelDropdown);
                            break;
                        case ScopeId.Events:
                            Instance.OnPropertyChanged(nameof(EventsModel));
                            Instance.OnPropertyChanged(nameof(EventsModelDropdown));
                            Instance.OnPropertyChanged(nameof(EventsTimeout));
                            Instance.OnPropertyChanged(nameof(EventsTemperature));
                            Instance.OnPropertyChanged(nameof(EventsMaxTokens));
                            Instance.OnPropertyChanged(nameof(EventsReasoningDropdown));
                            Instance.OnPropertyChanged(nameof(EventsGenerateImages));
                            Instance.OnPropertyChanged(nameof(EventsImageProviderDropdown));
                            modelProp = nameof(EventsModel);
                            modelDdProp = nameof(EventsModelDropdown);
                            break;
                        case ScopeId.MemoryBook:
                            Instance.OnPropertyChanged(nameof(MemoryModel));
                            Instance.OnPropertyChanged(nameof(MemoryModelDropdown));
                            Instance.OnPropertyChanged(nameof(MemoryTimeout));
                            Instance.OnPropertyChanged(nameof(MemoryTemperature));
                            Instance.OnPropertyChanged(nameof(MemoryMaxTokens));
                            Instance.OnPropertyChanged(nameof(MemoryReasoningDropdown));
                            modelProp = nameof(MemoryModel);
                            modelDdProp = nameof(MemoryModelDropdown);
                            break;
                        case ScopeId.BattleTactics:
                            Instance.OnPropertyChanged(nameof(BattleModel));
                            Instance.OnPropertyChanged(nameof(BattleModelDropdown));
                            Instance.OnPropertyChanged(nameof(BattleTimeout));
                            Instance.OnPropertyChanged(nameof(BattleTemperature));
                            Instance.OnPropertyChanged(nameof(BattleMaxTokens));
                            Instance.OnPropertyChanged(nameof(BattleReasoningDropdown));
                            modelProp = nameof(BattleModel);
                            modelDdProp = nameof(BattleModelDropdown);
                            break;
                        case ScopeId.GroupConversation:
                            Instance.OnPropertyChanged(nameof(GroupModel));
                            Instance.OnPropertyChanged(nameof(GroupModelDropdown));
                            Instance.OnPropertyChanged(nameof(GroupTimeout));
                            Instance.OnPropertyChanged(nameof(GroupTemperature));
                            Instance.OnPropertyChanged(nameof(GroupMaxTokens));
                            Instance.OnPropertyChanged(nameof(GroupReasoningDropdown));
                            modelProp = nameof(GroupModel);
                            modelDdProp = nameof(GroupModelDropdown);
                            break;
                        case ScopeId.UniqueCharacters:
                            Instance.OnPropertyChanged(nameof(UniqueModel));
                            Instance.OnPropertyChanged(nameof(UniqueModelDropdown));
                            Instance.OnPropertyChanged(nameof(UniqueTimeout));
                            Instance.OnPropertyChanged(nameof(UniqueTemperature));
                            Instance.OnPropertyChanged(nameof(UniqueMaxTokens));
                            Instance.OnPropertyChanged(nameof(UniqueReasoningDropdown));
                            modelProp = nameof(UniqueModel);
                            modelDdProp = nameof(UniqueModelDropdown);
                            break;
                    }

                    if (!string.IsNullOrEmpty(modelDdProp))
                    {
                        McmUiHelpers.TryRefreshDropdownWithItems(modelDdProp, modelItems, modelSelIdx, modelProp, s.Model ?? string.Empty);
                    }
                    McmUiHelpers.TryRefreshActiveScreenUi();
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm[scope]: NotifyScopeAllProperties error: " + ex.Message);
            }
        }

        private static void SetScopeEnabled(ScopeId id, bool value)
        {
            ScopeSettings s = Scope(id);
            if (s.Enabled != value) { s.Enabled = value; Save(); }
        }

        private static void SetScopeModel(ScopeId id, string value)
        {
            ScopeSettings s = Scope(id);
            string v = (value ?? string.Empty).Trim();
            if (s.Model != v)
            {
                s.Model = v;
                Save();

                lock (ModelDropdowns)
                {
                    if (ModelDropdowns.TryGetValue(id, out Dropdown<string> dd) && dd != null)
                    {
                        if (v.Length > 0)
                        {
                            int idx = -1;
                            for (int i = 0; i < dd.Count; i++)
                            {
                                if (string.Equals(dd[i], v, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                            }
                            if (idx >= 0 && dd.SelectedIndex != idx)
                            {
                                _suppressDropdownSync = true;
                                try { dd.SelectedIndex = idx; }
                                finally { _suppressDropdownSync = false; }
                            }
                        }
                    }
                }
            }
        }

        private static void SetScopeTimeout(ScopeId id, int value)
        {
            ScopeSettings s = Scope(id);
            if (s.TimeoutSeconds != value) { s.TimeoutSeconds = value; Save(); }
        }

        private static void SetScopeTimer(ScopeId id, bool value)
        {
            ScopeSettings s = Scope(id);
            if (s.TimerInChat != value) { s.TimerInChat = value; Save(); }
        }

        private static void SetScopeTemperature(ScopeId id, float value)
        {
            ScopeSettings s = Scope(id);
            if (Math.Abs(s.Temperature - value) > 0.001) { s.Temperature = value; Save(); }
        }

        private static void SetScopeMaxTokens(ScopeId id, int value)
        {
            ScopeSettings s = Scope(id);
            if (s.MaxTokens != value) { s.MaxTokens = value; Save(); }
        }

        private static void SetScopeGenerateImages(ScopeId id, bool value)
        {
            ScopeSettings s = Scope(id);
            if (s.GenerateImages != value) { s.GenerateImages = value; Save(); }
        }

        private static readonly Dictionary<ScopeId, Dropdown<string>> ImageProviderDropdowns =
            new Dictionary<ScopeId, Dropdown<string>>();

        private static Dropdown<string> GetScopeImageProviderDropdown(ScopeId id)
        {
            lock (ImageProviderDropdowns)
            {
                string[] providers = id == ScopeId.Dialogue
                    ? McmUiHelpers.DialogueScopeImageProviders
                    : McmUiHelpers.ScopeImageProviders;

                if (!ImageProviderDropdowns.TryGetValue(id, out Dropdown<string> dd) || dd == null)
                {
                    ScopeSettings s = Scope(id);
                    string current = s.ImageProvider;
                    if (id == ScopeId.Dialogue)
                    {
                        if (string.IsNullOrEmpty(current) || string.Equals(current, "Default", StringComparison.OrdinalIgnoreCase) ||
                            Array.FindIndex(providers, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) < 0)
                        {
                            current = "Google Gemini";
                            s.ImageProvider = current;
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(current)) current = "Default";
                    }

                    int idx = Array.FindIndex(providers, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = 0;
                    dd = new Dropdown<string>(providers, idx);
                    dd.PropertyChanged += (sender, args) =>
                    {
                        var d = (Dropdown<string>)sender;
                        if (d.SelectedIndex >= 0 && d.SelectedIndex < d.Count)
                        {
                            string selected = d.SelectedValue;
                            if (!string.Equals(s.ImageProvider, selected, StringComparison.OrdinalIgnoreCase))
                            {
                                s.ImageProvider = selected;
                                Save();
                            }
                        }
                    };
                    ImageProviderDropdowns[id] = dd;
                }
                else
                {
                    ScopeSettings s = Scope(id);
                    string current = s.ImageProvider;
                    if (id == ScopeId.Dialogue)
                    {
                        if (string.IsNullOrEmpty(current) || string.Equals(current, "Default", StringComparison.OrdinalIgnoreCase) ||
                            Array.FindIndex(providers, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) < 0)
                        {
                            current = "Google Gemini";
                            s.ImageProvider = current;
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(current)) current = "Default";
                    }

                    int idx = Array.FindIndex(providers, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && dd.SelectedIndex != idx)
                    {
                        dd.SelectedIndex = idx;
                    }
                }
                return dd;
            }
        }

        private static void SetScopeImageProviderDropdown(ScopeId id, Dropdown<string> value)
        {
            lock (ImageProviderDropdowns)
            {
                ImageProviderDropdowns[id] = value;
            }
        }

        private static readonly Dictionary<ScopeId, Dropdown<string>> ReasoningDropdowns =
            new Dictionary<ScopeId, Dropdown<string>>();

        private static Dropdown<string> GetScopeReasoningDropdown(ScopeId id)
        {
            lock (ReasoningDropdowns)
            {
                Dropdown<string> dd;
                if (!ReasoningDropdowns.TryGetValue(id, out dd))
                {
                    string cur = Scope(id).ReasoningEffort ?? "Default";
                    int idx = Array.FindIndex(McmUiHelpers.ReasoningOptions, o => string.Equals(o, cur, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = 0;
                    dd = new Dropdown<string>(McmUiHelpers.ReasoningOptions, idx);
                    dd.PropertyChanged += (sender, args) =>
                    {
                        SetScopeReasoningDropdown(id, (Dropdown<string>)sender);
                    };
                    ReasoningDropdowns[id] = dd;
                }
                else
                {
                    string cur = Scope(id).ReasoningEffort ?? "Default";
                    int idx = Array.FindIndex(McmUiHelpers.ReasoningOptions, o => string.Equals(o, cur, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && dd.SelectedIndex != idx)
                    {
                        dd.SelectedIndex = idx;
                    }
                }
                return dd;
            }
        }

        private static void SetScopeReasoningDropdown(ScopeId id, Dropdown<string> dropdown)
        {
            if (dropdown == null || dropdown.SelectedIndex < 0 || dropdown.SelectedIndex >= dropdown.Count)
                return;

            string selected = dropdown.SelectedValue;
            if (string.IsNullOrEmpty(selected)) return;

            ScopeSettings s = Scope(id);
            if (!string.Equals(s.ReasoningEffort, selected, StringComparison.OrdinalIgnoreCase))
            {
                s.ReasoningEffort = selected;
                Save();
            }
        }

        private static readonly Dictionary<ScopeId, Dropdown<string>> ModelDropdowns =
            new Dictionary<ScopeId, Dropdown<string>>();
        private static bool _suppressDropdownSync = false;

        private static Dropdown<string> GetScopeModelDropdown(ScopeId id)
        {
            lock (ModelDropdowns)
            {
                Dropdown<string> dd;
                if (!ModelDropdowns.TryGetValue(id, out dd))
                {
                    ScopeSettings s = Scope(id);
                    List<string> items = new List<string>();
                    int selIdx = 0;

                    if (s.AvailableModels != null && s.AvailableModels.Count > 0)
                    {
                        items.AddRange(s.AvailableModels);
                        if (!string.IsNullOrEmpty(s.Model))
                        {
                            if (!items.Contains(s.Model)) items.Insert(0, s.Model);
                            selIdx = items.IndexOf(s.Model);
                        }
                    }
                    else if (!string.IsNullOrEmpty(s.Model))
                    {
                        items.Add(s.Model);
                        selIdx = 0;
                    }
                    else
                    {
                        items.Add("(Click Fetch to load)");
                        selIdx = 0;
                    }

                    dd = new Dropdown<string>(items, (selIdx >= 0 && selIdx < items.Count) ? selIdx : 0);
                    dd.PropertyChanged += (sender, args) =>
                    {
                        SetScopeModelDropdown(id, (Dropdown<string>)sender);
                    };
                    ModelDropdowns[id] = dd;
                }
                return dd;
            }
        }

        private static void SetScopeModelDropdown(ScopeId id, Dropdown<string> dropdown)
        {
            if (_suppressDropdownSync) return;
            if (dropdown == null || dropdown.SelectedIndex < 0 || dropdown.SelectedIndex >= dropdown.Count) return;

            string selected = dropdown.SelectedValue;
            if (string.IsNullOrEmpty(selected) || selected.StartsWith("(", StringComparison.Ordinal)) return;

            ScopeSettings s = Scope(id);
            if (s.Model != selected)
            {
                s.Model = selected;
                Save();
                NotifyScopeModelProperties(id);
            }
        }

        public static string GetSelectedDropdownModel(ScopeId id)
        {
            lock (ModelDropdowns)
            {
                if (ModelDropdowns.TryGetValue(id, out Dropdown<string> dd) && dd != null)
                {
                    if (dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Count)
                    {
                        string val = dd.SelectedValue;
                        if (!string.IsNullOrEmpty(val) && !val.StartsWith("(", StringComparison.Ordinal))
                        {
                            return val;
                        }
                    }
                }
            }
            return null;
        }

        public static string GetSelectedDropdownModel(ScopeSettings settings)
        {
            if (settings == null) return null;
            foreach (ScopeDescriptor d in ScopeCatalog.All)
            {
                if (d.Id != ScopeId.None && Scope(d.Id) == settings)
                {
                    return GetSelectedDropdownModel(d.Id);
                }
            }
            return null;
        }

        private static void NotifyScopeModelProperties(ScopeId id, List<string> items = null, int selectedIndex = -1)
        {
            try
            {
                if (Instance == null) return;
                string dropdownPropName = null;
                string textPropName = null;
                switch (id)
                {
                    case ScopeId.Dialogue:
                        dropdownPropName = nameof(DialogueModelDropdown);
                        textPropName = nameof(DialogueModel);
                        Instance.OnPropertyChanged(nameof(DialogueModelDropdown));
                        Instance.OnPropertyChanged(nameof(DialogueModel));
                        break;
                    case ScopeId.Diplomacy:
                        dropdownPropName = nameof(DiplomacyModelDropdown);
                        textPropName = nameof(DiplomacyModel);
                        Instance.OnPropertyChanged(nameof(DiplomacyModelDropdown));
                        Instance.OnPropertyChanged(nameof(DiplomacyModel));
                        break;
                    case ScopeId.Events:
                        dropdownPropName = nameof(EventsModelDropdown);
                        textPropName = nameof(EventsModel);
                        Instance.OnPropertyChanged(nameof(EventsModelDropdown));
                        Instance.OnPropertyChanged(nameof(EventsModel));
                        break;
                    case ScopeId.MemoryBook:
                        dropdownPropName = nameof(MemoryModelDropdown);
                        textPropName = nameof(MemoryModel);
                        Instance.OnPropertyChanged(nameof(MemoryModelDropdown));
                        Instance.OnPropertyChanged(nameof(MemoryModel));
                        break;
                    case ScopeId.BattleTactics:
                        dropdownPropName = nameof(BattleModelDropdown);
                        textPropName = nameof(BattleModel);
                        Instance.OnPropertyChanged(nameof(BattleModelDropdown));
                        Instance.OnPropertyChanged(nameof(BattleModel));
                        break;
                    case ScopeId.GroupConversation:
                        dropdownPropName = nameof(GroupModelDropdown);
                        textPropName = nameof(GroupModel);
                        Instance.OnPropertyChanged(nameof(GroupModelDropdown));
                        Instance.OnPropertyChanged(nameof(GroupModel));
                        break;
                    case ScopeId.UniqueCharacters:
                        dropdownPropName = nameof(UniqueModelDropdown);
                        textPropName = nameof(UniqueModel);
                        Instance.OnPropertyChanged(nameof(UniqueModelDropdown));
                        Instance.OnPropertyChanged(nameof(UniqueModel));
                        break;
                }

                if (!string.IsNullOrEmpty(dropdownPropName))
                {
                    string curText = Scope(id).Model ?? string.Empty;
                    TryRefreshMcmUi(dropdownPropName, items, selectedIndex, textPropName, curText);
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm: notify model properties failed: " + ex.Message);
            }
        }

        private static void TryRefreshMcmUi(string dropdownPropertyName, List<string> items = null, int selectedIndex = -1, string textPropertyName = null, string textValue = null)
        {
            McmUiHelpers.TryRefreshDropdownWithItems(dropdownPropertyName, items, selectedIndex, textPropertyName, textValue);
        }

        private static void FetchScopeModels(ScopeId id)
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    Profile current = ProfileBook.Current;
                    Profile active = current.Scope(id).ToProfile(id.ToString(), current);
                    ChatNotices.Show("- Fetching available models...", id.ToString());

                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    {
                        List<string> models = await ModelDiscovery.FetchModelsAsync(active, cts.Token).ConfigureAwait(false);
                        if (models != null && models.Count > 0)
                        {
                            ScopeSettings s = Scope(id);
                            s.AvailableModels = new List<string>(models);
                            Save();

                            List<string> items = new List<string>();
                            int selIdx = 0;
                            string cur = s.Model;
                            if (!string.IsNullOrEmpty(cur))
                            {
                                items.AddRange(models);
                                if (!items.Contains(cur))
                                {
                                    items.Insert(0, cur);
                                }
                                selIdx = items.IndexOf(cur);
                            }
                            else
                            {
                                items.Add("(select model)");
                                items.AddRange(models);
                                selIdx = 0;
                            }

                            McmUiHelpers.EnqueueMainThreadAction(() =>
                            {
                                lock (ModelDropdowns)
                                {
                                    _suppressDropdownSync = true;
                                    try
                                    {
                                        if (ModelDropdowns.TryGetValue(id, out Dropdown<string> dd) && dd != null)
                                        {
                                            dd.Clear();
                                            foreach (string m in items)
                                            {
                                                dd.Add(m);
                                            }
                                            dd.SelectedIndex = (selIdx >= 0 && selIdx < items.Count) ? selIdx : 0;
                                        }
                                        else
                                        {
                                            dd = new Dropdown<string>(items, (selIdx >= 0 && selIdx < items.Count) ? selIdx : 0);
                                            dd.PropertyChanged += (sender, args) =>
                                            {
                                                SetScopeModelDropdown(id, (Dropdown<string>)sender);
                                            };
                                            ModelDropdowns[id] = dd;
                                        }
                                    }
                                    finally
                                    {
                                        _suppressDropdownSync = false;
                                    }
                                }

                                NotifyScopeModelProperties(id, items, selIdx);
                            });

                            string preview = string.Join(", ", models.Take(4)) + (models.Count > 4 ? "..." : string.Empty);
                            ChatNotices.Show("Fetched " + models.Count + " models: " + preview,
                                id.ToString(), 0.35f, 0.85f, 0.35f);
                        }
                        else
                        {
                            ChatNotices.Show("No models returned by endpoint.", id.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    string msg = ex is TransportException te ? te.Message : ex.Message;
                    ChatNotices.Error("Fetch models error: " + msg, id.ToString());
                }
            });
        }

        private static void ClearScopeModels(ScopeId id)
        {
            try
            {
                ScopeSettings s = Scope(id);
                if (s.AvailableModels != null)
                {
                    s.AvailableModels.Clear();
                }
                else
                {
                    s.AvailableModels = new List<string>();
                }
                s.Model = string.Empty;
                Save();

                List<string> items = new List<string> { "(Click Fetch to load)" };
                int selIdx = 0;

                lock (ModelDropdowns)
                {
                    _suppressDropdownSync = true;
                    try
                    {
                        if (ModelDropdowns.TryGetValue(id, out Dropdown<string> dd) && dd != null)
                        {
                            dd.Clear();
                            foreach (string m in items)
                            {
                                dd.Add(m);
                            }
                            dd.SelectedIndex = 0;
                        }
                        else
                        {
                            dd = new Dropdown<string>(items, 0);
                            dd.PropertyChanged += (sender, args) =>
                            {
                                SetScopeModelDropdown(id, (Dropdown<string>)sender);
                            };
                            ModelDropdowns[id] = dd;
                        }
                    }
                    finally
                    {
                        _suppressDropdownSync = false;
                    }
                }

                NotifyScopeModelProperties(id, items, selIdx);
                ChatNotices.Show("- Fetched models cleared.", id.ToString(), 0.35f, 0.85f, 0.35f);
            }
            catch (Exception ex)
            {
                LogSink.Error("clear models: failed for " + id, ex);
            }
        }

        private static void Save()
        {
            ProfileBook.Save();
        }
    }
}
