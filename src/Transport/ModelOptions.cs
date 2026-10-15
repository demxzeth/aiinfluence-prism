using AIInfluencePrism.Config;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Generation parameters for one request. A snapshot of the profile
    /// at call time — the transport must not re-read the profile midway.
    /// </summary>
    public sealed class ModelOptions
    {
        public ModelOptions(string model, double temperature, int maxTokens, string reasoningEffort = "default")
        {
            Model = model ?? string.Empty;
            Temperature = temperature;
            MaxTokens = maxTokens;
            ReasoningEffort = reasoningEffort ?? "default";
        }

        /// <summary>Model identifier; empty — the provider decides.</summary>
        public string Model { get; }

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; }

        /// <summary>Reply token limit.</summary>
        public int MaxTokens { get; }

        /// <summary>Reasoning / thinking effort level: default, none, low, medium, high.</summary>
        public string ReasoningEffort { get; }

        public static ModelOptions FromProfile(Profile profile)
        {
            if (profile == null)
            {
                return new ModelOptions(string.Empty, 0.7, 0, "default"); // 0 = no limit
            }

            return new ModelOptions(
                profile.Model ?? string.Empty,
                profile.Temperature,
                profile.MaxTokens,
                profile.ReasoningEffort ?? "default");
        }
    }
}