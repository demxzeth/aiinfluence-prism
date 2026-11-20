using System;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using AIInfluencePrism.Hook;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// MCM Screen: AIInfluence Prism.
    /// Clean, consolidated single informational tab and diagnostics hub.
    /// All provider & scope settings are managed in-game via Escape Menu -> Prism Settings (or http://127.0.0.1:43210/).
    /// </summary>
    public sealed class McmProviderSettings : AttributeGlobalSettings<McmProviderSettings>
    {
        public override string Id => "AIInfluencePrism_v1";
        public override string DisplayName => "AIInfluence Prism";
        public override string FolderName => "AIInfluencePrism";
        public override string FormatType => "json";

        // ─── Header & Notice ──────────────────────────────────────

        private string _headerAndVersion = "AIInfluence Prism v1.0.0";
        private string _infoNotice = "All configuration is now managed in-game via Escape Menu -> Prism Settings (or http://127.0.0.1:43210/)";
        private Action _openWebUi;

        [SettingPropertyGroup("Information", GroupOrder = 1)]
        [SettingPropertyText("Header & Version", Order = 1, RequireRestart = false,
            HintText = "Installed version of AIInfluence Prism.")]
        public string HeaderAndVersion
        {
            get => _headerAndVersion;
            set => _headerAndVersion = value;
        }

        [SettingPropertyGroup("Information", GroupOrder = 1)]
        [SettingPropertyText("Notice", Order = 2, RequireRestart = false,
            HintText = "All configuration is now managed in-game via Escape Menu -> Prism Settings (or http://127.0.0.1:43210/).")]
        public string Notice
        {
            get => _infoNotice;
            set => _infoNotice = value;
        }

        /// <summary>
        /// Backward-compatible alias for Notice.
        /// </summary>
        public string SettingsNotice
        {
            get => Notice;
            set => Notice = value;
        }

        [SettingPropertyGroup("Information", GroupOrder = 1)]
        [SettingPropertyButton("Open Prism Settings", Order = 3, Content = "Open WebUI", RequireRestart = false,
            HintText = "Opens the In-Game Prism Settings WebUI (http://127.0.0.1:43210/).")]
        public Action OpenWebUi
        {
            get => _openWebUi ?? (_openWebUi = () => PrismWebUiHost.Open());
            set => _openWebUi = value;
        }

        // ─── Status & Diagnostics Summary ─────────────────────────

        private Action _testAll;
        private Action _openLogs;

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyBool("Master Switch", Order = 1, RequireRestart = false,
            HintText = "When ON, Prism routes Bannerlord requests to your configured providers. When OFF, requests pass through to AIInfluence backend.")]
        public bool MasterEnabled
        {
            get => ProfileBook.Current.MasterEnabled;
            set
            {
                if (ProfileBook.Current.MasterEnabled != value)
                {
                    ProfileBook.Current.MasterEnabled = value;
                    ProfileBook.Save();
                }
            }
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyText("Active Profile", Order = 2, RequireRestart = false,
            HintText = "Name of the active connection profile.")]
        public string ActiveProfileName
        {
            get => string.IsNullOrWhiteSpace(ProfileBook.Current?.Name) ? "Default" : ProfileBook.Current.Name;
            set { }
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyText("Bridge Status", Order = 3, RequireRestart = false,
            HintText = "Status of the AIInfluence Prism routing bridge.")]
        public string BridgeStatus
        {
            get
            {
                if (!ProfileBook.Current.MasterEnabled)
                    return "Bypassed (Native AIInfluence)";
                return Engine.Installed ? "Online (Routing Active)" : "Standby (Engine Priming)";
            }
            set { }
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyText("BCL Interceptor Status", Order = 4, RequireRestart = false,
            HintText = "Status of HttpClientHandler BCL interceptor and anti-tamper detours.")]
        public string BclInterceptorStatus
        {
            get
            {
                bool bcl = HttpDispatch.Installed;
                bool guard = ProtectionGuard.IsApplied;
                if (bcl && guard) return "Active (BCL & Protection Engaged)";
                if (bcl) return "Active (BCL Intercept Engaged)";
                if (guard) return "Protection Active (BCL Standby)";
                return "Standby";
            }
            set { }
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyText("Active Provider", Order = 5, RequireRestart = false,
            HintText = "Currently active primary provider.")]
        public string ActiveProvider
        {
            get => Profile.Label(ProfileBook.Current.Provider);
            set { }
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyText("Active Model", Order = 6, RequireRestart = false,
            HintText = "Model assigned to the active provider.")]
        public string ActiveModel
        {
            get => string.IsNullOrWhiteSpace(ProfileBook.Current.Model) ? "(auto)" : ProfileBook.Current.Model;
            set { }
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyButton("Test All Connections", Order = 7, Content = "Test All", RequireRestart = false,
            HintText = "Runs a probe against every enabled scope and reports each result in chat.")]
        public Action TestAll
        {
            get => _testAll ?? (_testAll = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeAllEnabledScopes()));
            set => _testAll = value;
        }

        [SettingPropertyGroup("Status & Diagnostics", GroupOrder = 2)]
        [SettingPropertyButton("Open Log Folder", Order = 8, Content = "Open Logs", RequireRestart = false,
            HintText = "Opens the folder where prism_log.txt is written.")]
        public Action OpenLogs
        {
            get => _openLogs ?? (_openLogs = McmUiHelpers.OpenLogFolder);
            set => _openLogs = value;
        }

        // ─── Support & Links ──────────────────────────────────────

        private Action _openNexus;
        private Action _openGitHub;
        private Action _showDiscord;

        [SettingPropertyGroup("Support & Links", GroupOrder = 3)]
        [SettingPropertyButton("Nexus Mods", Order = 1, Content = "Nexus Page", RequireRestart = false,
            HintText = "Opens the Nexus Mods page of AIInfluence Prism in your browser.")]
        public Action OpenNexus
        {
            get => _openNexus ?? (_openNexus = () => McmUiHelpers.OpenUrl(PrismInfo.NexusUrl));
            set => _openNexus = value;
        }

        [SettingPropertyGroup("Support & Links", GroupOrder = 3)]
        [SettingPropertyButton("GitHub Repository", Order = 2, Content = "GitHub", RequireRestart = false,
            HintText = "Opens the GitHub repository of AIInfluence Prism in your browser.")]
        public Action OpenGitHub
        {
            get => _openGitHub ?? (_openGitHub = () => McmUiHelpers.OpenUrl(PrismInfo.GitHubUrl));
            set => _openGitHub = value;
        }

        [SettingPropertyGroup("Support & Links", GroupOrder = 3)]
        [SettingPropertyButton("Discord / Contacts", Order = 3, Content = "@demxzeth", RequireRestart = false,
            HintText = "Reach the author on Discord: @demxzeth")]
        public Action ShowDiscord
        {
            get => _showDiscord ?? (_showDiscord = () => ChatNotices.Show("Discord: " + PrismInfo.DiscordContact));
            set => _showDiscord = value;
        }

        // ─── Backward-compatible synchronization helpers ──────────

        public void PullFromProfile()
        {
            OnPropertyChanged(nameof(HeaderAndVersion));
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(SettingsNotice));
            OnPropertyChanged(nameof(MasterEnabled));
            OnPropertyChanged(nameof(ActiveProfileName));
            OnPropertyChanged(nameof(BridgeStatus));
            OnPropertyChanged(nameof(BclInterceptorStatus));
            OnPropertyChanged(nameof(ActiveProvider));
            OnPropertyChanged(nameof(ActiveModel));
        }

        public void PushToProfile()
        {
            ProfileBook.Save();
        }
    }
}
