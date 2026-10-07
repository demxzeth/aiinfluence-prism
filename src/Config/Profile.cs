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
        Player2
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

                default:
                    return "http://127.0.0.1:4315";
            }
        }

        /// <summary>Short provider name for the log.</summary>
        public static string Label(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenAiCompat: return "openai";
                case ProviderKind.OpenAiSse: return "openai-sse";
                case ProviderKind.Anthropic: return "anthropic";
                case ProviderKind.Gemini: return "gemini";
                case ProviderKind.Ollama: return "ollama";
                case ProviderKind.KoboldCpp: return "koboldcpp";
                case ProviderKind.Player2: return "player2";
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