using System;
using System.Collections.Generic;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Stored settings for a single provider (per scope or global profile).
    /// Preserves credentials, selected model, and fetched model list when switching providers.
    /// </summary>
    public sealed class ProviderSettings
    {
        public string RootUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 0;
        public int TimeoutSeconds { get; set; } = 180;
        public string ReasoningEffort { get; set; } = "default";
        public List<string> AvailableModels { get; set; } = new List<string>();

        public ProviderSettings Clone()
        {
            return new ProviderSettings
            {
                RootUrl = this.RootUrl,
                ApiKey = this.ApiKey,
                Model = this.Model,
                Temperature = this.Temperature,
                MaxTokens = this.MaxTokens,
                TimeoutSeconds = this.TimeoutSeconds,
                ReasoningEffort = this.ReasoningEffort,
                AvailableModels = new List<string>(this.AvailableModels ?? new List<string>())
            };
        }

        /// <summary>
        /// Creates sensible default settings for a given provider kind.
        /// </summary>
        public static ProviderSettings CreateDefault(ProviderKind kind)
        {
            var s = new ProviderSettings
            {
                RootUrl = Profile.DefaultRoot(kind),
                Temperature = 0.7,
                MaxTokens = 0,
                TimeoutSeconds = 180,
                ReasoningEffort = "default"
            };

            switch (kind)
            {
                case ProviderKind.OpenAiCompat:
                case ProviderKind.OpenAiSse:
                    s.Model = "gpt-4o-mini";
                    break;
                case ProviderKind.Anthropic:
                    s.Model = "claude-3-5-haiku-latest";
                    break;
                case ProviderKind.Gemini:
                    s.Model = "gemini-2.0-flash";
                    break;
                case ProviderKind.Nvidia:
                    s.Model = "meta/llama-3.1-70b-instruct";
                    break;
                case ProviderKind.Ollama:
                    s.Model = "llama3.2";
                    s.RootUrl = "http://127.0.0.1:11434";
                    break;
                case ProviderKind.KoboldCpp:
                    s.Model = string.Empty;
                    s.RootUrl = "http://127.0.0.1:5001";
                    break;
                case ProviderKind.Antigravity:
                    s.Model = "gemini-3.8-flash-high";
                    break;
                case ProviderKind.Player2:
                case ProviderKind.Custom:
                default:
                    s.Model = string.Empty;
                    break;
            }

            return s;
        }
    }
}
