using System;
using System.ComponentModel;
using System.Diagnostics;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// MCM settings screen. Layout (top to bottom):
    ///   Support     - links to Nexus / GitHub / Discord.
    ///   Master      - master switch + test-all + open log folder.
    ///   DialogueвЂ¦   - one group per scope.
    ///   Diagnostics - log switch, JSON repair, error notices, About.
    /// </summary>
    public sealed class McmBridge : AttributeGlobalSettings<McmBridge>
    {
        public override string Id => "AIInfluencePrism_v1";
        public override string DisplayName => "AIInfluence Prism";
        public override string FolderName => "AIInfluencePrism";
        public override string FormatType => "json";

        // ─── Support ─────────

        private Action _openNexus;
        private Action _openGitHub;
        private Action _showDiscord;

        [SettingPropertyGroup("Support", GroupOrder = 1)]
        [SettingPropertyButton("Nexus Page", Order = 1, Content = "Nexus Page", RequireRestart = false,
            HintText = "Opens the Nexus Mods page of AIInfluence Prism in your browser.")]
        public Action OpenNexus
        {
            get { return _openNexus ?? (_openNexus = () => OpenUrl(PrismInfo.NexusUrl)); }
            set { _openNexus = value; }
        }

        [SettingPropertyGroup("Support", GroupOrder = 1)]
        [SettingPropertyButton("GitHub", Order = 2, Content = "GitHub", RequireRestart = false,
            HintText = "Opens the GitHub repository of AIInfluence Prism in your browser.")]
        public Action OpenGitHub
        {
            get { return _openGitHub ?? (_openGitHub = () => OpenUrl(PrismInfo.GitHubUrl)); }
            set { _openGitHub = value; }
        }

        [SettingPropertyGroup("Support", GroupOrder = 1)]
        [SettingPropertyButton("Contact me on Discord", Order = 3, Content = "@demxzeth", RequireRestart = false,
            HintText = "Reach me on Discord: @demxzeth")]
        public Action ShowDiscord
        {
            get { return _showDiscord ?? (_showDiscord = () => ChatNotices.Show("Discord: " + PrismInfo.DiscordContact)); }
            set { _showDiscord = value; }
        }

        // ─── Master ─────────

        [SettingPropertyGroup("Master", GroupOrder = 2)]
        [SettingPropertyBool("Master switch", Order = 1, RequireRestart = false,
            HintText = "When on, Prism routes requests to your configured providers. When off, requests go to AIInfluence's own backend.")]
        public bool MasterEnabled
        {
            get { return _masterEnabled; }
            set { if (_masterEnabled != value) { _masterEnabled = value; SaveMaster(); } }
        }
        private bool _masterEnabled = true;

        private Action _testAll;
        private Action _openLogs;

        [SettingPropertyGroup("Master", GroupOrder = 2)]
        [SettingPropertyButton("Test all connections", Order = 2, Content = "Test all", RequireRestart = false,
            HintText = "Runs a connection test against every enabled scope and reports each result in chat.")]
        public Action TestAll
        {
            get { return _testAll ?? (_testAll = () => Pipeline.ProbeAllEnabledScopes()); }
            set { _testAll = value; }
        }

        [SettingPropertyGroup("Master", GroupOrder = 2)]
        [SettingPropertyButton("Open log folder", Order = 3, Content = "Open logs", RequireRestart = false,
            HintText = "Opens the folder where prism_log.txt is written.")]
        public Action OpenLogs
        {
            get { return _openLogs ?? (_openLogs = OpenLogFolder); }
            set { _openLogs = value; }
        }
// ─── Dialogue ─────────────────

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool DialogueEnabled { get => Scope(ScopeId.Dialogue).Enabled; set => SetScopeEnabled(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false, 
            HintText = "API key for the provider of this scope.")]
        public string DialogueApiKey { get => Scope(ScopeId.Dialogue).ApiKey; set => SetScopeKey(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string DialogueRootUrl { get => Scope(ScopeId.Dialogue).RootUrl; set => SetScopeRoot(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string DialogueModel { get => Scope(ScopeId.Dialogue).Model; set => SetScopeModel(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int DialogueTimeout { get => Scope(ScopeId.Dialogue).TimeoutSeconds; set => SetScopeTimeout(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool DialogueTimer { get => Scope(ScopeId.Dialogue).TimerInChat; set => SetScopeTimer(ScopeId.Dialogue, value); }

        private Action _testDialogue;

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestDialogue
        {
            get { return _testDialogue ?? (_testDialogue = () => Pipeline.ProbeScope(ScopeId.Dialogue)); }
            set { _testDialogue = value; }
        }

        private Action _testDialogueModel;

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestDialogueModel
        {
            get { return _testDialogueModel ?? (_testDialogueModel = () => Pipeline.ProbeModel(ScopeId.Dialogue)); }
            set { _testDialogueModel = value; }
        }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float DialogueTemperature { get => (float)Scope(ScopeId.Dialogue).Temperature; set => SetScopeTemperature(ScopeId.Dialogue, value); }

        [SettingPropertyGroup("Dialogue", GroupOrder = 3)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string DialogueMaxTokens
        {
            get { return Scope(ScopeId.Dialogue).MaxTokens > 0
                    ? Scope(ScopeId.Dialogue).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.Dialogue, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.Dialogue, parsed);
                }
            }
        }
// ─── Diplomacy ────────────────

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool DiplomacyEnabled { get => Scope(ScopeId.Diplomacy).Enabled; set => SetScopeEnabled(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the provider of this scope.")]
        public string DiplomacyApiKey { get => Scope(ScopeId.Diplomacy).ApiKey; set => SetScopeKey(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string DiplomacyRootUrl { get => Scope(ScopeId.Diplomacy).RootUrl; set => SetScopeRoot(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string DiplomacyModel { get => Scope(ScopeId.Diplomacy).Model; set => SetScopeModel(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int DiplomacyTimeout { get => Scope(ScopeId.Diplomacy).TimeoutSeconds; set => SetScopeTimeout(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool DiplomacyTimer { get => Scope(ScopeId.Diplomacy).TimerInChat; set => SetScopeTimer(ScopeId.Diplomacy, value); }

        private Action _testDiplomacy;

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestDiplomacy
        {
            get { return _testDiplomacy ?? (_testDiplomacy = () => Pipeline.ProbeScope(ScopeId.Diplomacy)); }
            set { _testDiplomacy = value; }
        }

        private Action _testDiplomacyModel;

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestDiplomacyModel
        {
            get { return _testDiplomacyModel ?? (_testDiplomacyModel = () => Pipeline.ProbeModel(ScopeId.Diplomacy)); }
            set { _testDiplomacyModel = value; }
        }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float DiplomacyTemperature { get => (float)Scope(ScopeId.Diplomacy).Temperature; set => SetScopeTemperature(ScopeId.Diplomacy, value); }

        [SettingPropertyGroup("Diplomacy", GroupOrder = 4)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string DiplomacyMaxTokens
        {
            get { return Scope(ScopeId.Diplomacy).MaxTokens > 0
                    ? Scope(ScopeId.Diplomacy).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.Diplomacy, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.Diplomacy, parsed);
                }
            }
        }
// ─── Events ──────────────────

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool EventsEnabled { get => Scope(ScopeId.Events).Enabled; set => SetScopeEnabled(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the provider of this scope.")]
        public string EventsApiKey { get => Scope(ScopeId.Events).ApiKey; set => SetScopeKey(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string EventsRootUrl { get => Scope(ScopeId.Events).RootUrl; set => SetScopeRoot(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string EventsModel { get => Scope(ScopeId.Events).Model; set => SetScopeModel(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int EventsTimeout { get => Scope(ScopeId.Events).TimeoutSeconds; set => SetScopeTimeout(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool EventsTimer { get => Scope(ScopeId.Events).TimerInChat; set => SetScopeTimer(ScopeId.Events, value); }

        private Action _testEvents;

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestEvents
        {
            get { return _testEvents ?? (_testEvents = () => Pipeline.ProbeScope(ScopeId.Events)); }
            set { _testEvents = value; }
        }

        private Action _testEventsModel;

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestEventsModel
        {
            get { return _testEventsModel ?? (_testEventsModel = () => Pipeline.ProbeModel(ScopeId.Events)); }
            set { _testEventsModel = value; }
        }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float EventsTemperature { get => (float)Scope(ScopeId.Events).Temperature; set => SetScopeTemperature(ScopeId.Events, value); }

        [SettingPropertyGroup("Events", GroupOrder = 5)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string EventsMaxTokens
        {
            get { return Scope(ScopeId.Events).MaxTokens > 0
                    ? Scope(ScopeId.Events).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.Events, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.Events, parsed);
                }
            }
        }
// ─── Memory Book ──────────────

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool MemoryEnabled { get => Scope(ScopeId.MemoryBook).Enabled; set => SetScopeEnabled(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the provider of this scope.")]
        public string MemoryApiKey { get => Scope(ScopeId.MemoryBook).ApiKey; set => SetScopeKey(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string MemoryRootUrl { get => Scope(ScopeId.MemoryBook).RootUrl; set => SetScopeRoot(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string MemoryModel { get => Scope(ScopeId.MemoryBook).Model; set => SetScopeModel(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int MemoryTimeout { get => Scope(ScopeId.MemoryBook).TimeoutSeconds; set => SetScopeTimeout(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool MemoryTimer { get => Scope(ScopeId.MemoryBook).TimerInChat; set => SetScopeTimer(ScopeId.MemoryBook, value); }

        private Action _testMemory;

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestMemory
        {
            get { return _testMemory ?? (_testMemory = () => Pipeline.ProbeScope(ScopeId.MemoryBook)); }
            set { _testMemory = value; }
        }

        private Action _testMemoryModel;

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestMemoryModel
        {
            get { return _testMemoryModel ?? (_testMemoryModel = () => Pipeline.ProbeModel(ScopeId.MemoryBook)); }
            set { _testMemoryModel = value; }
        }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float MemoryTemperature { get => (float)Scope(ScopeId.MemoryBook).Temperature; set => SetScopeTemperature(ScopeId.MemoryBook, value); }

        [SettingPropertyGroup("Memory Book", GroupOrder = 6)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string MemoryMaxTokens
        {
            get { return Scope(ScopeId.MemoryBook).MaxTokens > 0
                    ? Scope(ScopeId.MemoryBook).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.MemoryBook, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.MemoryBook, parsed);
                }
            }
        }
// ─── Battle Tactics ──────────

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool BattleEnabled { get => Scope(ScopeId.BattleTactics).Enabled; set => SetScopeEnabled(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the provider of this scope.")]
        public string BattleApiKey { get => Scope(ScopeId.BattleTactics).ApiKey; set => SetScopeKey(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string BattleRootUrl { get => Scope(ScopeId.BattleTactics).RootUrl; set => SetScopeRoot(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string BattleModel { get => Scope(ScopeId.BattleTactics).Model; set => SetScopeModel(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int BattleTimeout { get => Scope(ScopeId.BattleTactics).TimeoutSeconds; set => SetScopeTimeout(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool BattleTimer { get => Scope(ScopeId.BattleTactics).TimerInChat; set => SetScopeTimer(ScopeId.BattleTactics, value); }

        private Action _testBattle;

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestBattle
        {
            get { return _testBattle ?? (_testBattle = () => Pipeline.ProbeScope(ScopeId.BattleTactics)); }
            set { _testBattle = value; }
        }

        private Action _testBattleModel;

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestBattleModel
        {
            get { return _testBattleModel ?? (_testBattleModel = () => Pipeline.ProbeModel(ScopeId.BattleTactics)); }
            set { _testBattleModel = value; }
        }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float BattleTemperature { get => (float)Scope(ScopeId.BattleTactics).Temperature; set => SetScopeTemperature(ScopeId.BattleTactics, value); }

        [SettingPropertyGroup("Battle Tactics", GroupOrder = 7)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string BattleMaxTokens
        {
            get { return Scope(ScopeId.BattleTactics).MaxTokens > 0
                    ? Scope(ScopeId.BattleTactics).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.BattleTactics, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.BattleTactics, parsed);
                }
            }
        }
// ─── Group Conversation ──────

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool GroupEnabled { get => Scope(ScopeId.GroupConversation).Enabled; set => SetScopeEnabled(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the provider of this scope.")]
        public string GroupApiKey { get => Scope(ScopeId.GroupConversation).ApiKey; set => SetScopeKey(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string GroupRootUrl { get => Scope(ScopeId.GroupConversation).RootUrl; set => SetScopeRoot(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string GroupModel { get => Scope(ScopeId.GroupConversation).Model; set => SetScopeModel(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int GroupTimeout { get => Scope(ScopeId.GroupConversation).TimeoutSeconds; set => SetScopeTimeout(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool GroupTimer { get => Scope(ScopeId.GroupConversation).TimerInChat; set => SetScopeTimer(ScopeId.GroupConversation, value); }

        private Action _testGroup;

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestGroup
        {
            get { return _testGroup ?? (_testGroup = () => Pipeline.ProbeScope(ScopeId.GroupConversation)); }
            set { _testGroup = value; }
        }

        private Action _testGroupModel;

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestGroupModel
        {
            get { return _testGroupModel ?? (_testGroupModel = () => Pipeline.ProbeModel(ScopeId.GroupConversation)); }
            set { _testGroupModel = value; }
        }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float GroupTemperature { get => (float)Scope(ScopeId.GroupConversation).Temperature; set => SetScopeTemperature(ScopeId.GroupConversation, value); }

        [SettingPropertyGroup("Group Conversation", GroupOrder = 8)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string GroupMaxTokens
        {
            get { return Scope(ScopeId.GroupConversation).MaxTokens > 0
                    ? Scope(ScopeId.GroupConversation).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.GroupConversation, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.GroupConversation, parsed);
                }
            }
        }
// ─── Unique AI Characters ────

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyBool("Use Prism for this scope", Order = 1, RequireRestart = false,
            HintText = "When off, this scope uses AIInfluence's own backend.")]
        public bool UniqueEnabled { get => Scope(ScopeId.UniqueCharacters).Enabled; set => SetScopeEnabled(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the provider of this scope.")]
        public string UniqueApiKey { get => Scope(ScopeId.UniqueCharacters).ApiKey; set => SetScopeKey(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyText("API address", Order = 3, RequireRestart = false,
            HintText = "Base address; the chat path is appended automatically.")]
        public string UniqueRootUrl { get => Scope(ScopeId.UniqueCharacters).RootUrl; set => SetScopeRoot(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Model id; empty = provider default.")]
        public string UniqueModel { get => Scope(ScopeId.UniqueCharacters).Model; set => SetScopeModel(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyInteger("Timeout (seconds)", 180, 600, "{0}", Order = 5, RequireRestart = false,
            HintText = "Single request timeout for this scope.")]
        public int UniqueTimeout { get => Scope(ScopeId.UniqueCharacters).TimeoutSeconds; set => SetScopeTimeout(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyBool("Response timer in chat", Order = 6, RequireRestart = false,
            HintText = "Shows how long the provider took to reply.")]
        public bool UniqueTimer { get => Scope(ScopeId.UniqueCharacters).TimerInChat; set => SetScopeTimer(ScopeId.UniqueCharacters, value); }

        private Action _testUnique;

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyButton("Test connection to provider", Order = 7, Content = "Test", RequireRestart = false,
            HintText = "Sends one tiny request with these fields and reports the result in chat.")]
        public Action TestUnique
        {
            get { return _testUnique ?? (_testUnique = () => Pipeline.ProbeScope(ScopeId.UniqueCharacters)); }
            set { _testUnique = value; }
        }

        private Action _testUniqueModel;

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyButton("Test connection to model", Order = 8, Content = "Test model", RequireRestart = false,
            HintText = "Sends a small request to the model itself and reports its reply in the chat. An empty reply is still a valid connection.")]
        public Action TestUniqueModel
        {
            get { return _testUniqueModel ?? (_testUniqueModel = () => Pipeline.ProbeModel(ScopeId.UniqueCharacters)); }
            set { _testUniqueModel = value; }
        }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyFloatingInteger("Temperature", 0f, 2f, "{0:0.##}", Order = 9, RequireRestart = false,
            HintText = "Sampling randomness for this scope.")]
        public float UniqueTemperature { get => (float)Scope(ScopeId.UniqueCharacters).Temperature; set => SetScopeTemperature(ScopeId.UniqueCharacters, value); }

        [SettingPropertyGroup("Unique AI characters", GroupOrder = 9)]
        [SettingPropertyText("Max tokens", Order = 10, RequireRestart = false,
            HintText = "Response token limit. Leave empty for no limit (provider default).")]
        public string UniqueMaxTokens
        {
            get { return Scope(ScopeId.UniqueCharacters).MaxTokens > 0
                    ? Scope(ScopeId.UniqueCharacters).MaxTokens.ToString()
                    : string.Empty; }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Empty = no limit.
                    SetScopeMaxTokens(ScopeId.UniqueCharacters, 0);
                    return;
                }

                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                {
                    SetScopeMaxTokens(ScopeId.UniqueCharacters, parsed);
                }
            }
        }

        // ─── Diagnostics ───────────

        [SettingPropertyGroup("Diagnostics", GroupOrder = 10)]
        [SettingPropertyBool("Write log file", Order = 1, RequireRestart = false,
            HintText = "When on, Prism writes a verbose log to logs/prism_log.txt.")]
        public bool LogEnabled
        {
            get { return _logEnabled; }
            set { if (_logEnabled != value) { _logEnabled = value; SaveLogFlag(); } }
        }
        private bool _logEnabled = true;

        [SettingPropertyGroup("Diagnostics", GroupOrder = 10)]
        [SettingPropertyBool("Repair JSON replies", Order = 2, RequireRestart = false,
            HintText = "When on, malformed or truncated model responses are repaired before parsing.")]
        public bool JsonRepairEnabled
        {
            get { return _jsonRepairEnabled; }
            set { if (_jsonRepairEnabled != value) { _jsonRepairEnabled = value; SaveJsonRepairFlag(); } }
        }
        private bool _jsonRepairEnabled = true;

        [SettingPropertyGroup("Diagnostics", GroupOrder = 10)]
        [SettingPropertyBool("Show provider errors in chat", Order = 3, RequireRestart = false,
            HintText = "When on, provider errors (HTTP failures, timeouts) are shown as chat notices.")]
        public bool ErrorNoticesEnabled
        {
            get { return _errorNoticesEnabled; }
            set { if (_errorNoticesEnabled != value) { _errorNoticesEnabled = value; SaveErrorsFlag(); } }
        }
        private bool _errorNoticesEnabled;

        private Action _about;

        [SettingPropertyGroup("Diagnostics", GroupOrder = 10)]
        [SettingPropertyButton("About", Order = 4, Content = "Made by Demxzeth", RequireRestart = false,
            HintText = "About AIInfluence Prism.")]
        public Action About
        {
            get { return _about ?? (_about = () => ChatNotices.Show("AIInfluence Prism " + PrismInfo.VersionText + " - made by Demxzeth")); }
            set { _about = value; }
        }
// ─── plumbing ─────────

        /// <summary>Pull master and diagnostics flags from the live profile (called on load).</summary>
        public void PullFromProfile()
        {
            Profile p = ProfileBook.Current;
            _masterEnabled = p.MasterEnabled;
            _logEnabled = p.LogEnabled;
            _jsonRepairEnabled = p.JsonRepairEnabled;
            _errorNoticesEnabled = p.ErrorNoticesEnabled;
            LogSink.Info("mcm: pulled from profile \"" + p.Name + "\".");
        }

        private static ScopeSettings Scope(ScopeId id)
        {
            return ProfileBook.Current.Scope(id);
        }

        private static void SetScopeEnabled(ScopeId id, bool value)
        {
            ScopeSettings s = Scope(id);
            if (s.Enabled != value) { s.Enabled = value; Save(); }
        }

        private static void SetScopeKey(ScopeId id, string value)
        {
            ScopeSettings s = Scope(id);
            string v = value ?? string.Empty;
            if (s.ApiKey != v) { s.ApiKey = v; Save(); }
        }

        private static void SetScopeRoot(ScopeId id, string value)
        {
            ScopeSettings s = Scope(id);
            string v = (value ?? string.Empty).Trim();
            if (s.RootUrl != v) { s.RootUrl = v; Save(); }
        }

        private static void SetScopeModel(ScopeId id, string value)
        {
            ScopeSettings s = Scope(id);
            string v = (value ?? string.Empty).Trim();
            if (s.Model != v) { s.Model = v; Save(); }
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
            // Round to at most 2 decimals so the value never shows like "1.2848323".
            float rounded = (float)Math.Round(value, 2);
            if (Math.Abs(s.Temperature - rounded) > 0.0001) { s.Temperature = rounded; Save(); }
        }

        private static void SetScopeMaxTokens(ScopeId id, int value)
        {
            ScopeSettings s = Scope(id);
            if (s.MaxTokens != value) { s.MaxTokens = value; Save(); }
        }

        private static void SaveMaster()
        {
            Profile p = ProfileBook.Current;
            p.MasterEnabled = Instance._masterEnabled;
            Save();
            LogSink.Info("mcm: master switch -> " + (p.MasterEnabled ? "on" : "off") + ".");
        }

        private static void SaveLogFlag()
        {
            ProfileBook.Current.LogEnabled = Instance._logEnabled;
            Save();
        }

        private static void SaveJsonRepairFlag()
        {
            ProfileBook.Current.JsonRepairEnabled = Instance._jsonRepairEnabled;
            Save();
        }

        private static void SaveErrorsFlag()
        {
            ProfileBook.Current.ErrorNoticesEnabled = Instance._errorNoticesEnabled;
            Save();
        }

        private static void Save()
        {
            try { ProfileBook.Save(); }
            catch (Exception ex) { LogSink.Warn("mcm: save failed: " + ex.Message); }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm: could not open " + url + ": " + ex.Message);
            }
        }

        /// <summary>Open the folder where prism_log.txt is written.</summary>
        public static void OpenLogFolder()
        {
            string root = ModuleLocator.Root;
            if (root.Length == 0)
            {
                ChatNotices.Error("log folder is unknown (module root not resolved).");
                return;
            }

            string folder = System.IO.Path.Combine(root, "logs");
            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            try
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ChatNotices.Error("could not open log folder: " + ex.Message);
            }
        }
    }
}






