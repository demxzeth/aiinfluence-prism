using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Generation parameters for one request. A snapshot of the profile
    /// at call time.
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

        public string Model { get; }
        public double Temperature { get; }
        public int MaxTokens { get; }
        public string ReasoningEffort { get; }

        public static ModelOptions FromProfile(Profile profile)
        {
            if (profile == null)
            {
                return new ModelOptions(string.Empty, 0.7, 0, "default");
            }

            return new ModelOptions(
                profile.Model ?? string.Empty,
                profile.Temperature,
                profile.MaxTokens,
                profile.ReasoningEffort ?? "default");
        }
    }
}
