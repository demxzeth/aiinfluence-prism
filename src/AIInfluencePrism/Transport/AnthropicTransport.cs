using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Post;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Anthropic Messages API: POST {root}/v1/messages.
    /// Headers: x-api-key + anthropic-version. max_tokens is required.
    /// </summary>
    public sealed class AnthropicTransport : HttpChatTransport
    {
        public const string ApiVersion = "2023-06-01";

        public override ProviderKind Kind => ProviderKind.Anthropic;

        public override string EndpointUrl(Profile profile)
        {
            return EndpointMapper.AnthropicMessagesUrl(profile.EffectiveRoot());
        }

        public override string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile)
        {
            JArray messages = new JArray
            {
                new JObject
                {
                    { "role", "user" },
                    { "content", turn.User }
                }
            };

            int tokenLimit = options.MaxTokens > 0 ? options.MaxTokens : 4096;

            JObject body = new JObject
            {
                { "model", options.Model.Length > 0 ? options.Model : "claude-3-5-haiku-latest" },
                { "max_tokens", tokenLimit },
                { "temperature", options.Temperature },
                { "messages", messages }
            };

            if (!string.IsNullOrEmpty(turn.System))
            {
                body["system"] = turn.System;
            }

            if (!string.IsNullOrEmpty(options.ReasoningEffort) &&
                !string.Equals(options.ReasoningEffort, "default", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(options.ReasoningEffort, "none", StringComparison.OrdinalIgnoreCase))
                {
                    body["thinking"] = new JObject { { "type", "disabled" } };
                }
                else
                {
                    int budget = 2048;
                    if (string.Equals(options.ReasoningEffort, "low", StringComparison.OrdinalIgnoreCase)) budget = 1024;
                    else if (string.Equals(options.ReasoningEffort, "high", StringComparison.OrdinalIgnoreCase)) budget = 4096;

                    if (tokenLimit <= budget)
                    {
                        body["max_tokens"] = budget + 1024;
                    }
                    body["thinking"] = new JObject
                    {
                        { "type", "enabled" },
                        { "budget_tokens", budget }
                    };
                }
            }

            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        public override void ConfigureRequest(HttpRequestMessage request, Profile profile)
        {
            string key = profile.ApiKey == null ? string.Empty : profile.ApiKey.Trim();
            if (key.Length > 0)
            {
                request.Headers.Add("x-api-key", key);
            }

            request.Headers.Add("anthropic-version", ApiVersion);
        }

        public override string ParseReply(string rawBody)
        {
            return ReplyReader.ReadBlocks(rawBody, "content", "text", "type", "text");
        }

        protected override string ProbeUrl(Profile profile)
        {
            return EndpointMapper.V1BaseUrl(profile.EffectiveRoot()) + "/models";
        }
    }
}
