using System;
using System.Collections.Generic;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Profiles
{
    /// <summary>
    /// Per-scope connection settings. Each AI scenario
    /// can override the provider, model, key, address and generation parameters.
    /// </summary>
    public sealed class ScopeSettings
    {
        public ScopeSettings()
        {
            Enabled = true;
            Provider = ProviderKind.Player2;
            RootUrl = string.Empty;
            ApiKey = string.Empty;
            Model = string.Empty;
            Temperature = 0.7;
            MaxTokens = 0; // 0 = no limit (empty field in the UI)
            TimeoutSeconds = 180;
            TimerInChat = false;
            ReasoningEffort = "default";
            RecentTurns = 15;
            EmergencyHistoryLength = 12;
        }

        public int EmergencyHistoryLength { get; set; } = 12;
        public int RecentTurns { get; set; } = 15;
        public bool Enabled { get; set; }
        public ProviderKind Provider { get; set; }
        public string RootUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public int TimeoutSeconds { get; set; }
        public bool TimerInChat { get; set; }
        public string ProviderRaw { get; set; }
        public List<string> AvailableModels { get; set; } = new List<string>();
        public string ReasoningEffort { get; set; }
        public bool GenerateImages { get; set; } = false;
        public string ImageProvider { get; set; } = "Default";
        public string ImageModel { get; set; } = string.Empty;
        public string ImageEndpoint { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;

        public Dictionary<string, ProviderSettings> Providers { get; set; } =
            new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);

        public void SaveCurrentToProvider(ProviderKind kind)
        {
            string key = Profile.Label(kind);
            if (Providers == null)
            {
                Providers = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
            }

            Providers[key] = new ProviderSettings
            {
                RootUrl = this.RootUrl ?? string.Empty,
                ApiKey = this.ApiKey ?? string.Empty,
                Model = this.Model ?? string.Empty,
                Temperature = this.Temperature,
                MaxTokens = this.MaxTokens,
                TimeoutSeconds = this.TimeoutSeconds,
                ReasoningEffort = this.ReasoningEffort ?? "default",
                AvailableModels = new List<string>(this.AvailableModels ?? new List<string>())
            };
        }

        public void SwitchProvider(ProviderKind newKind, string newRaw = null)
        {
            if (Provider == newKind && string.Equals(ProviderRaw, newRaw, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SaveCurrentToProvider(Provider);
            Provider = newKind;
            ProviderRaw = newRaw;

            string key = Profile.Label(newKind);
            if (Providers != null && Providers.TryGetValue(key, out ProviderSettings saved) && saved != null)
            {
                RootUrl = saved.RootUrl ?? string.Empty;
                ApiKey = saved.ApiKey ?? string.Empty;
                Model = saved.Model ?? string.Empty;
                Temperature = saved.Temperature;
                MaxTokens = saved.MaxTokens;
                TimeoutSeconds = saved.TimeoutSeconds > 0 ? saved.TimeoutSeconds : 180;
                ReasoningEffort = saved.ReasoningEffort ?? "default";
                AvailableModels = new List<string>(saved.AvailableModels ?? new List<string>());
            }
            else
            {
                ProviderSettings def = ProviderSettings.CreateDefault(newKind);
                RootUrl = newKind == ProviderKind.Custom ? string.Empty : def.RootUrl;
                ApiKey = string.Empty;
                Model = def.Model;
                Temperature = def.Temperature;
                MaxTokens = def.MaxTokens;
                TimeoutSeconds = def.TimeoutSeconds;
                ReasoningEffort = def.ReasoningEffort;
                AvailableModels = new List<string>();
                SaveCurrentToProvider(newKind);
            }
        }

        public string EffectiveRoot()
        {
            if (Provider != ProviderKind.Custom)
            {
                return Profile.DefaultRoot(Provider);
            }
            string root = RootUrl == null ? string.Empty : RootUrl.Trim().TrimEnd('/');
            return root.Length > 0 ? root : Profile.DefaultRoot(Provider);
        }

        public static Func<ScopeSettings, string> FallbackModelResolver { get; set; }

        public string EffectiveModel()
        {
            if (!string.IsNullOrWhiteSpace(Model))
            {
                return Model.Trim();
            }
            if (FallbackModelResolver != null)
            {
                string dropdownModel = FallbackModelResolver(this);
                if (!string.IsNullOrWhiteSpace(dropdownModel))
                {
                    return dropdownModel.Trim();
                }
            }
            if (AvailableModels != null && AvailableModels.Count > 0)
            {
                foreach (string m in AvailableModels)
                {
                    if (!string.IsNullOrWhiteSpace(m) && !m.StartsWith("(", StringComparison.Ordinal))
                    {
                        return m.Trim();
                    }
                }
            }
            return string.Empty;
        }

        public Profile ToProfile(string name, Profile parent = null)
        {
            Profile baseProfile = parent ?? ProfileBook.Current;
            string key = ApiKey ?? string.Empty;
            string root = RootUrl ?? string.Empty;
            if (baseProfile != null)
            {
                string provLabel = Profile.Label(Provider);
                if (baseProfile.Providers != null && baseProfile.Providers.TryGetValue(provLabel, out ProviderSettings provSettings) && provSettings != null)
                {
                    if (string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(provSettings.ApiKey))
                    {
                        key = provSettings.ApiKey;
                    }
                    if (string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(provSettings.RootUrl))
                    {
                        root = provSettings.RootUrl;
                    }
                }
                if (string.IsNullOrEmpty(key) && baseProfile.Provider == Provider && !string.IsNullOrEmpty(baseProfile.ApiKey))
                {
                    key = baseProfile.ApiKey;
                }
                if (string.IsNullOrEmpty(root) && baseProfile.Provider == Provider && !string.IsNullOrEmpty(baseProfile.RootUrl))
                {
                    root = baseProfile.RootUrl;
                }
            }

            return new Profile
            {
                Name = name ?? string.Empty,
                MasterEnabled = baseProfile != null ? baseProfile.MasterEnabled : true,
                LogEnabled = baseProfile != null ? baseProfile.LogEnabled : true,
                JsonRepairEnabled = baseProfile != null ? baseProfile.JsonRepairEnabled : true,
                ErrorNoticesEnabled = baseProfile != null ? baseProfile.ErrorNoticesEnabled : false,
                Provider = Provider,
                RootUrl = root,
                ApiKey = key,
                Model = EffectiveModel(),
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                TimeoutSeconds = TimeoutSeconds,
                ReasoningEffort = ReasoningEffort ?? "default",
                AntigravityCliPath = baseProfile != null ? baseProfile.AntigravityCliPath : "agy",
                AntigravityModel = baseProfile != null ? baseProfile.AntigravityModel : "gemini-3.8-flash-high",
                EmergencyHistoryLength = EmergencyHistoryLength
            };
        }
    }
}
