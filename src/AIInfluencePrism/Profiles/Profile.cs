using System;
using System.Collections.Generic;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Profiles
{
    /// <summary>Provider kind the transport talks to.</summary>
    public enum ProviderKind
    {
        OpenAiCompat,
        OpenAiSse,
        Anthropic,
        Gemini,
        Ollama,
        KoboldCpp,
        Player2,
        Custom,
        Antigravity,
        Nvidia
    }

    /// <summary>
    /// Connection profile: which provider, where to, with which key and model.
    /// </summary>
    public sealed class Profile
    {
        public Profile()
        {
            Name = "default";
            MasterEnabled = true;
            LogEnabled = true;
            JsonRepairEnabled = true;
            ErrorNoticesEnabled = false;
            Provider = ProviderKind.Player2;
            RootUrl = string.Empty;
            ApiKey = string.Empty;
            Model = string.Empty;
            Temperature = 0.7;
            MaxTokens = 0; // 0 = no limit
            TimeoutSeconds = 180;
            ReasoningEffort = "default";
            AntigravityCliPath = "agy";
            AntigravityModel = "gemini-3.8-flash-high";
            EmergencyHistoryLength = 12;
        }

        public Profile CloneWithModel(string newModel)
        {
            Profile copy = (Profile)MemberwiseClone();
            copy.Model = newModel;
            copy.AntigravityModel = newModel;
            return copy;
        }

        public string Name { get; set; }

        public bool MasterEnabled { get; set; }
        public bool LogEnabled { get; set; }
        public bool JsonRepairEnabled { get; set; }
        public bool ErrorNoticesEnabled { get; set; }
        public ProviderKind Provider { get; set; }
        public string RootUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public int TimeoutSeconds { get; set; }
        public string ReasoningEffort { get; set; }
        public string AntigravityCliPath { get; set; }
        public string AntigravityModel { get; set; }
        public string SystemPrompt { get; set; } = string.Empty;
        public int EmergencyHistoryLength { get; set; } = 12;

        public string ImageProvider { get; set; } = "OpenAI";
        public string ImageRootUrl { get; set; } = "https://api.openai.com";
        public string ImageApiKey { get; set; } = string.Empty;
        public string ImageModel { get; set; } = "dall-e-3";
        public string ImageArtStyle { get; set; } = "Bannerlord Concept Art";
        public bool ImageFailSilently { get; set; } = true;
        public bool ImageAutoFallbackFree { get; set; } = true;

        private readonly object _syncRoot = new object();

        public Dictionary<string, ImageProviderSettings> ImageProviders { get; set; } =
            new Dictionary<string, ImageProviderSettings>(StringComparer.OrdinalIgnoreCase);

        public void SaveCurrentImageProvider(string providerName = null)
        {
            lock (_syncRoot)
            {
                string key = providerName ?? ImageProvider ?? "OpenAI";
                if (ImageProviders == null)
                {
                    ImageProviders = new Dictionary<string, ImageProviderSettings>(StringComparer.OrdinalIgnoreCase);
                }
                List<string> existingModels = null;
                if (ImageProviders.TryGetValue(key, out ImageProviderSettings existing) && existing != null && existing.AvailableModels != null)
                {
                    existingModels = existing.AvailableModels;
                }
                ImageProviders[key] = new ImageProviderSettings
                {
                    RootUrl = this.ImageRootUrl ?? string.Empty,
                    ApiKey = this.ImageApiKey ?? string.Empty,
                    Model = this.ImageModel ?? string.Empty,
                    AvailableModels = existingModels != null ? new List<string>(existingModels) : new List<string>()
                };
            }
        }

        public void SwitchImageProvider(string newProvider)
        {
            lock (_syncRoot)
            {
                if (string.Equals(ImageProvider, newProvider, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SaveCurrentImageProvider(ImageProvider);
                ImageProvider = newProvider;

                if (ImageProviders != null && ImageProviders.TryGetValue(newProvider, out ImageProviderSettings saved) && saved != null)
                {
                    ImageRootUrl = saved.RootUrl ?? string.Empty;
                    ImageApiKey = saved.ApiKey ?? string.Empty;
                    ImageModel = saved.Model ?? string.Empty;
                }
                else
                {
                    ImageProviderSettings def = ImageProviderSettings.CreateDefault(newProvider);
                    ImageRootUrl = def.RootUrl;
                    ImageApiKey = string.Empty;
                    ImageModel = def.Model;
                    SaveCurrentImageProvider(newProvider);
                }
            }
        }

        public ImageProviderSettings GetEffectiveImageConfig(string requestedProvider = null)
        {
            string targetProvider = requestedProvider;
            if (string.IsNullOrEmpty(targetProvider) || string.Equals(targetProvider, "Default", StringComparison.OrdinalIgnoreCase))
            {
                targetProvider = ImageProvider ?? "OpenAI";
            }

            string root = this.ImageRootUrl;
            string key = this.ImageApiKey;
            string model = this.ImageModel;

            if (!string.Equals(targetProvider, ImageProvider, StringComparison.OrdinalIgnoreCase))
            {
                if (ImageProviders != null && ImageProviders.TryGetValue(targetProvider, out ImageProviderSettings saved) && saved != null)
                {
                    root = saved.RootUrl;
                    key = saved.ApiKey;
                    model = saved.Model;
                }
                else
                {
                    var def = ImageProviderSettings.CreateDefault(targetProvider);
                    root = def.RootUrl;
                    key = def.ApiKey;
                    model = def.Model;
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                string lower = targetProvider.ToLowerInvariant();
                if (lower.Contains("openai"))
                {
                    if (Providers != null && Providers.TryGetValue("OpenAI", out var ps) && !string.IsNullOrWhiteSpace(ps.ApiKey))
                    {
                        key = ps.ApiKey;
                    }
                    else if ((Provider == ProviderKind.OpenAiCompat || Provider == ProviderKind.OpenAiSse) && !string.IsNullOrWhiteSpace(ApiKey))
                    {
                        key = ApiKey;
                    }
                }
                else if (lower.Contains("gemini"))
                {
                    if (Providers != null && Providers.TryGetValue("Gemini", out var ps) && !string.IsNullOrWhiteSpace(ps.ApiKey))
                    {
                        key = ps.ApiKey;
                    }
                    else if (Provider == ProviderKind.Gemini && !string.IsNullOrWhiteSpace(ApiKey))
                    {
                        key = ApiKey;
                    }
                }
            }

            return new ImageProviderSettings
            {
                RootUrl = root ?? string.Empty,
                ApiKey = key ?? string.Empty,
                Model = model ?? string.Empty
            };
        }

        public Dictionary<string, ProviderSettings> Providers { get; set; } =
            new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);

        public void SaveCurrentToProvider(ProviderKind kind)
        {
            lock (_syncRoot)
            {
                string key = Label(kind);
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
                    AvailableModels = new List<string>()
                };
            }
        }

        public void SwitchProvider(ProviderKind newKind)
        {
            lock (_syncRoot)
            {
                if (Provider == newKind)
                {
                    return;
                }

                SaveCurrentToProvider(Provider);
                Provider = newKind;

                string key = Label(newKind);
                if (Providers != null && Providers.TryGetValue(key, out ProviderSettings saved) && saved != null)
                {
                    RootUrl = saved.RootUrl ?? string.Empty;
                    ApiKey = saved.ApiKey ?? string.Empty;
                    Model = saved.Model ?? string.Empty;
                    Temperature = saved.Temperature;
                    MaxTokens = saved.MaxTokens;
                    TimeoutSeconds = saved.TimeoutSeconds > 0 ? saved.TimeoutSeconds : 180;
                    ReasoningEffort = saved.ReasoningEffort ?? "default";
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
                    SaveCurrentToProvider(newKind);
                }
            }
        }

        public Dictionary<ScopeId, ScopeSettings> Scopes { get; set; }

        public ScopeSettings Scope(ScopeId scope)
        {
            lock (_syncRoot)
            {
                if (Scopes == null)
                {
                    Scopes = new Dictionary<ScopeId, ScopeSettings>();
                }

                if (!Scopes.TryGetValue(scope, out ScopeSettings settings))
                {
                    settings = new ScopeSettings();
                    Scopes[scope] = settings;
                }

                return settings;
            }
        }

        public string EffectiveRoot()
        {
            string root = RootUrl == null ? string.Empty : RootUrl.Trim().TrimEnd('/');
            return root.Length > 0 ? root : DefaultRoot(Provider);
        }

        public static string DefaultRoot(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenAiCompat:
                case ProviderKind.OpenAiSse:
                    return "https://api.openai.com";

                case ProviderKind.Anthropic:
                    return "https://api.anthropic.com";

                case ProviderKind.Gemini:
                    return "https://generativelanguage.googleapis.com";

                case ProviderKind.Ollama:
                    return "http://127.0.0.1:11434";

                case ProviderKind.KoboldCpp:
                    return "http://127.0.0.1:5001";

                case ProviderKind.Player2:
                    return "http://127.0.0.1:4315";

                case ProviderKind.Custom:
                case ProviderKind.Antigravity:
                    return string.Empty;

                case ProviderKind.Nvidia:
                    return "https://integrate.api.nvidia.com/v1";

                default:
                    return "http://127.0.0.1:4315";
            }
        }

        public static string Label(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenAiCompat: return "OpenAI";
                case ProviderKind.OpenAiSse: return "OpenAI-SSE";
                case ProviderKind.Anthropic: return "Anthropic";
                case ProviderKind.Gemini: return "Gemini";
                case ProviderKind.Ollama: return "Ollama";
                case ProviderKind.KoboldCpp: return "KoboldCpp";
                case ProviderKind.Player2: return "Player2";
                case ProviderKind.Custom: return "Custom";
                case ProviderKind.Antigravity: return "Antigravity";
                case ProviderKind.Nvidia: return "Nvidia";
                default: return kind.ToString();
            }
        }

        public static bool TryParseProvider(string text, out ProviderKind kind)
        {
            kind = ProviderKind.Player2;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            switch (text.Trim().ToLowerInvariant())
            {
                case "openai":
                case "openai-compat":
                case "deepseek":
                case "openrouter":
                    kind = ProviderKind.OpenAiCompat;
                    return true;

                case "openai-sse":
                case "openai_sse":
                    kind = ProviderKind.OpenAiSse;
                    return true;

                case "anthropic":
                case "claude":
                    kind = ProviderKind.Anthropic;
                    return true;

                case "gemini":
                    kind = ProviderKind.Gemini;
                    return true;

                case "ollama":
                    kind = ProviderKind.Ollama;
                    return true;

                case "koboldcpp":
                case "kobold":
                    kind = ProviderKind.KoboldCpp;
                    return true;

                case "player2":
                case "p2":
                    kind = ProviderKind.Player2;
                    return true;

                case "custom":
                case "custom-api":
                case "customapi":
                    kind = ProviderKind.Custom;
                    return true;

                case "antigravity":
                case "agy":
                    kind = ProviderKind.Antigravity;
                    return true;

                case "nvidia":
                case "nim":
                case "nvapi":
                    kind = ProviderKind.Nvidia;
                    return true;

                default:
                    return false;
            }
        }

        public static Profile CreateDefault()
        {
            Profile profile = new Profile
            {
                Name = "default",
                MasterEnabled = true,
                Provider = ProviderKind.Player2,
                RootUrl = string.Empty,
                ApiKey = string.Empty,
                Model = string.Empty
            };

            profile.Scopes = null;
            return profile;
        }
    }
}
