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

        /// <summary>Root accounting for the empty field.</summary>
        public string EffectiveRoot()
        {
            string root = RootUrl == null ? string.Empty : RootUrl.Trim().TrimEnd('/');
            return root.Length > 0 ? root : Profile.DefaultRoot(Provider);
        }

        /// <summary>Convert to a live profile object for the pipeline (snapshot).</summary>
        public Profile ToProfile(string name)
        {
            return new Profile
            {
                Name = name ?? string.Empty,
                Provider = Provider,
                RootUrl = RootUrl ?? string.Empty,
                ApiKey = ApiKey ?? string.Empty,
                Model = Model ?? string.Empty,
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                TimeoutSeconds = TimeoutSeconds
            };
        }
    }
}