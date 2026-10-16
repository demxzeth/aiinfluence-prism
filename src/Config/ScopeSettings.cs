using System;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Per-scope connection settings. Each AI scenario (dialogue, diplomacy,
    /// events, memory, battle tactics, group conversations, unique characters)
    /// can override the provider, model, key, address and generation parameters.
    /// This is the v0.1 "one profile per scope" model backed by the config file.
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
        }

        /// <summary>Whether this scope routes through Prism (off = AIInfluence's own backend).</summary>
        public bool Enabled { get; set; }

        /// <summary>Protocol kind.</summary>
        public ProviderKind Provider { get; set; }

        /// <summary>API root; empty — the provider default is used.</summary>
        public string RootUrl { get; set; }

        /// <summary>API key; empty — no authorization header is sent.</summary>
        public string ApiKey { get; set; }

        /// <summary>Model identifier; empty is valid for Player2.</summary>
        public string Model { get; set; }

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; set; }

        /// <summary>Response token limit.</summary>
        public int MaxTokens { get; set; }

        /// <summary>Timeout of a single request, seconds.</summary>
        public int TimeoutSeconds { get; set; }

        /// <summary>Show the response timer in chat for this scope.</summary>
        public bool TimerInChat { get; set; }

        /// <summary>Raw provider text entered in UI / config (null when unset).</summary>
        public string ProviderRaw { get; set; }

        /// <summary>List of models fetched from the provider endpoint for this scope.</summary>
        public System.Collections.Generic.List<string> AvailableModels { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>Reasoning / thinking effort level: default, none, low, medium, high.</summary>
        public string ReasoningEffort { get; set; }

        /// <summary>Whether to generate images for this scope (supported for Dialogue and Events).</summary>
        public bool GenerateImages { get; set; } = false;

        /// <summary>Image generation provider: Default, OpenAI, Google Gemini, Custom.</summary>
        public string ImageProvider { get; set; } = "Default";

        /// <summary>Image model name (e.g. dall-e-3, dall-e-2, flux). Empty = provider default.</summary>
        public string ImageModel { get; set; } = string.Empty;

        /// <summary>Image API endpoint. Empty = provider default (/v1/images/generations).</summary>
        public string ImageEndpoint { get; set; } = string.Empty;

        /// <summary>Per-provider settings cache for this scope.</summary>
        public System.Collections.Generic.Dictionary<string, ProviderSettings> Providers { get; set; } =
            new System.Collections.Generic.Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Saves current fields into the slot of the given provider.</summary>
        public void SaveCurrentToProvider(ProviderKind kind)
        {
            string key = Profile.Label(kind);
            if (Providers == null)
            {
                Providers = new System.Collections.Generic.Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
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
                AvailableModels = new System.Collections.Generic.List<string>(this.AvailableModels ?? new System.Collections.Generic.List<string>())
            };
        }

        /// <summary>
        /// Switches active provider, saving current settings for the old provider
        /// and restoring saved settings for the new provider.
        /// </summary>
        public void SwitchProvider(ProviderKind newKind, string newRaw = null)
        {
            if (Provider == newKind && string.Equals(ProviderRaw, newRaw, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 1. Save current state to old provider slot
            SaveCurrentToProvider(Provider);

            // 2. Switch provider kind
            Provider = newKind;
            ProviderRaw = newRaw;

            // 3. Load or initialize state for new provider
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
                AvailableModels = new System.Collections.Generic.List<string>(saved.AvailableModels ?? new System.Collections.Generic.List<string>());
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
                AvailableModels = new System.Collections.Generic.List<string>();
                SaveCurrentToProvider(newKind);
            }
        }

        /// <summary>Root accounting for the empty field.</summary>
        public string EffectiveRoot()
        {
            if (Provider != ProviderKind.Custom)
            {
                return Profile.DefaultRoot(Provider);
            }
            string root = RootUrl == null ? string.Empty : RootUrl.Trim().TrimEnd('/');
            return root.Length > 0 ? root : Profile.DefaultRoot(Provider);
        }

        /// <summary>Delegate to query UI dropdown fallback when Model field is empty.</summary>
        public static Func<ScopeSettings, string> FallbackModelResolver { get; set; }

        /// <summary>Model accounting for empty field fallback.</summary>
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

        /// <summary>Convert to a live profile object for the pipeline (snapshot).</summary>
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
                AntigravityModel = baseProfile != null ? baseProfile.AntigravityModel : "gemini-3.7-flash-high",
                AntigravityTwoStageRequest = baseProfile != null ? baseProfile.AntigravityTwoStageRequest : false
            };
        }
    }
}