using System;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Config
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
    /// Minimum for E4; at E6 the profile will grow per-scope overrides
    /// and MCM editing.
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
            AntigravityTwoStageRequest = true;
        }

        /// <summary>Display name of the profile.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Master switch: when off, Prism passes every request through to
        /// AIInfluence's own backend (no interception).
        /// </summary>
        public bool MasterEnabled { get; set; }

        /// <summary>Diagnostics: write the verbose log.</summary>
        public bool LogEnabled { get; set; }

        /// <summary>Diagnostics: repair malformed JSON model replies.</summary>
        public bool JsonRepairEnabled { get; set; }

        /// <summary>Diagnostics: surface provider errors in the in-game chat.</summary>
        public bool ErrorNoticesEnabled { get; set; }

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

        /// <summary>Reasoning / thinking effort level: default, none, low, medium, high.</summary>
        public string ReasoningEffort { get; set; }

        /// <summary>Path to agy.exe CLI executable.</summary>
        public string AntigravityCliPath { get; set; }

        /// <summary>Default model identifier for Antigravity CLI.</summary>
        public string AntigravityModel { get; set; }

        /// <summary>Whether to send Antigravity requests in two stages (context first, then dialogue history via --continue).</summary>
        public bool AntigravityTwoStageRequest { get; set; }

        /// <summary>Active global image generation provider: OpenAI, Google Gemini, Free (Pollinations), Custom.</summary>
        public string ImageProvider { get; set; } = "OpenAI";

        /// <summary>Image generation API root URL.</summary>
        public string ImageRootUrl { get; set; } = "https://api.openai.com";

        /// <summary>Image generation API key.</summary>
        public string ImageApiKey { get; set; } = string.Empty;

        /// <summary>Image generation model name (e.g. dall-e-3, gemini-2.5-flash-image, flux).</summary>
        public string ImageModel { get; set; } = "dall-e-3";

        /// <summary>Art style preset for image generation prompts.</summary>
        public string ImageArtStyle { get; set; } = "Bannerlord Concept Art";

        /// <summary>When true, image generation errors fail silently without disruptive chat alerts.</summary>
        public bool ImageFailSilently { get; set; } = true;

        /// <summary>When true, automatically falls back to Free (Pollinations) if the primary image provider fails.</summary>
        public bool ImageAutoFallbackFree { get; set; } = true;

        /// <summary>Per-image-provider settings cache (OpenAI, Google Gemini, Free (Pollinations), Custom).</summary>
        public System.Collections.Generic.Dictionary<string, ImageProviderSettings> ImageProviders { get; set; } =
            new System.Collections.Generic.Dictionary<string, ImageProviderSettings>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Saves current image provider fields into its slot.</summary>
        public void SaveCurrentImageProvider(string providerName = null)
        {
            string key = providerName ?? ImageProvider ?? "OpenAI";
            if (ImageProviders == null)
            {
                ImageProviders = new System.Collections.Generic.Dictionary<string, ImageProviderSettings>(StringComparer.OrdinalIgnoreCase);
            }
            System.Collections.Generic.List<string> existingModels = null;
            if (ImageProviders.TryGetValue(key, out ImageProviderSettings existing) && existing != null && existing.AvailableModels != null)
            {
                existingModels = existing.AvailableModels;
            }
            ImageProviders[key] = new ImageProviderSettings
            {
                RootUrl = this.ImageRootUrl ?? string.Empty,
                ApiKey = this.ImageApiKey ?? string.Empty,
                Model = this.ImageModel ?? string.Empty,
                AvailableModels = existingModels != null ? new System.Collections.Generic.List<string>(existingModels) : new System.Collections.Generic.List<string>()
            };
        }

        /// <summary>Switches active image provider, preserving current settings.</summary>
        public void SwitchImageProvider(string newProvider)
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

        /// <summary>
        /// Resolves the effective image provider configuration for a requested provider,
        /// falling back to the text provider's API key if no dedicated image key is entered.
        /// </summary>
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

            // Fallback for API key from text provider if empty
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

        /// <summary>Per-provider settings cache for default profile.</summary>
        public System.Collections.Generic.Dictionary<string, ProviderSettings> Providers { get; set; } =
            new System.Collections.Generic.Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Saves current global fields into the slot of the given provider.</summary>
        public void SaveCurrentToProvider(ProviderKind kind)
        {
            string key = Label(kind);
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
                AvailableModels = new System.Collections.Generic.List<string>()
            };
        }

        /// <summary>
        /// Switches active provider for the global profile, preserving current settings
        /// and restoring saved settings for the target provider.
        /// </summary>
        public void SwitchProvider(ProviderKind newKind)
        {
            if (Provider == newKind)
            {
                return;
            }

            // 1. Save current state
            SaveCurrentToProvider(Provider);

            // 2. Switch provider
            Provider = newKind;

            // 3. Load or initialize
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

        /// <summary>
        /// Per-scope settings keyed by scope id. Built-in defaults are created
        /// lazily; each scope gets its own connection + generation settings.
        /// </summary>
        public System.Collections.Generic.Dictionary<ScopeId, ScopeSettings> Scopes { get; set; }

        /// <summary>Get (creating if needed) the settings for a scope.</summary>
        public ScopeSettings Scope(ScopeId scope)
        {
            if (Scopes == null)
            {
                Scopes = new System.Collections.Generic.Dictionary<ScopeId, ScopeSettings>();
            }

            ScopeSettings settings;
            if (!Scopes.TryGetValue(scope, out settings))
            {
                settings = new ScopeSettings();
                Scopes[scope] = settings;
            }

            return settings;
        }

        /// <summary>Root accounting for the empty field: substitutes the provider default,
        /// trims trailing slashes and spaces.</summary>
        public string EffectiveRoot()
        {
            string root = RootUrl == null ? string.Empty : RootUrl.Trim().TrimEnd('/');
            return root.Length > 0 ? root : DefaultRoot(Provider);
        }

        /// <summary>Default API root by provider kind.</summary>
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

        /// <summary>Short provider name for the log.</summary>
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

        /// <summary>Parses a config string; unrecognized — player2.</summary>
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

        /// <summary>Built-in default profile: master on, Player2 local, all scopes enabled.</summary>
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