using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Ollama: POST {root}/api/chat. Generation parameters are nested
    /// under "options", where the token limit is called num_predict.
    /// </summary>
    public sealed class OllamaTransport : HttpChatTransport
    {
        public override ProviderKind Kind
        {
            get { return ProviderKind.Ollama; }
        }

        public override string EndpointUrl(Profile profile)
        {
            return EndpointMapper.OllamaChatUrl(profile.EffectiveRoot());
        }

        public override string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile)
        {
            JArray messages = new JArray();

            if (!string.IsNullOrEmpty(turn.System))
            {
                messages.Add(new JObject
                {
                    { "role", "system" },
                    { "content", turn.System }
                });
            }

            messages.Add(new JObject
            {
                { "role", "user" },
                { "content", turn.User }
            });

            JObject ollamaOptions = new JObject
            {
                { "temperature", options.Temperature }
            };

            // 0 = no limit: omit the token field so the provider uses its default.
            if (options.MaxTokens > 0)
            {
                ollamaOptions["num_predict"] = options.MaxTokens;
            }

            return new JObject
            {
                { "model", options.Model.Length > 0 ? options.Model : "llama3.2" },
                { "messages", messages },
                { "stream", false },
                { "options", ollamaOptions }
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public override void ConfigureRequest(HttpRequestMessage request, Profile profile)
        {
            // Local Ollama: no authentication.
        }

        public override string ParseReply(string rawBody)
        {
            return ReplyReader.ReadJson(rawBody, "message.content");
        }

        protected override string ProbeUrl(Profile profile)
        {
            return EndpointMapper.OllamaTagsUrl(profile.EffectiveRoot());
        }
    }
}