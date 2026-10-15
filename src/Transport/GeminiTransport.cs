using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Google Gemini: POST {root}/v1beta/models/{model}:generateContent.
    /// The key travels as a query parameter. The assistant role in
    /// Gemini is "model".
    /// </summary>
    public sealed class GeminiTransport : HttpChatTransport
    {
        public override ProviderKind Kind
        {
            get { return ProviderKind.Gemini; }
        }

        public const string DefaultModel = "gemini-3.8-flash";

        public override string EndpointUrl(Profile profile)
        {
            ModelOptions options = ModelOptions.FromProfile(profile);
            string model = !string.IsNullOrWhiteSpace(options.Model) ? options.Model.Trim() : DefaultModel;
            return EndpointMapper.GeminiGenerateUrl(profile.EffectiveRoot(), model);
        }

        public override string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile)
        {
            JArray contents = new JArray
            {
                new JObject
                {
                    { "role", "user" },
                    { "parts", new JArray { new JObject { { "text", turn.User } } } }
                }
            };

            JObject generation = new JObject
            {
                { "temperature", options.Temperature }
            };

            // 0 = no limit: omit the token field so the provider uses its default.
            if (options.MaxTokens > 0)
            {
                generation["maxOutputTokens"] = options.MaxTokens;
            }

            JObject body = new JObject
            {
                { "contents", contents },
                { "generationConfig", generation }
            };

            if (!string.IsNullOrEmpty(turn.System))
            {
                body["systemInstruction"] = new JObject
                {
                    { "parts", new JArray { new JObject { { "text", turn.System } } } }
                };
            }

            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        public override void ConfigureRequest(HttpRequestMessage request, Profile profile)
        {
            string key = profile.ApiKey == null ? string.Empty : profile.ApiKey.Trim();
            if (key.Length > 0)
            {
                request.Headers.Add("x-goog-api-key", key);
            }
        }

        public override string ParseReply(string rawBody)
        {
            return ReplyReader.ReadBlocks(rawBody, "candidates[0].content.parts", "text");
        }

        protected override string ProbeUrl(Profile profile)
        {
            return EndpointMapper.GeminiModelsUrl(profile.EffectiveRoot(), profile.ApiKey);
        }
    }
}