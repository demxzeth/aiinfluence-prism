using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Anthropic Messages API: POST {root}/v1/messages.
    /// Headers: x-api-key + anthropic-version. max_tokens is required.
    /// </summary>
    public sealed class AnthropicTransport : HttpChatTransport
    {
        /// <summary>The API version pinned in the header.</summary>
        public const string ApiVersion = "2023-06-01";

        public override ProviderKind Kind
        {
            get { return ProviderKind.Anthropic; }
        }

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

            // max_tokens is required by Anthropic. 0 = no limit → use a generous default.
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
            // Content is a list of typed blocks; only "text" blocks
            // carry the answer (tool_use and others are skipped).
            return ReplyReader.ReadBlocks(rawBody, "content", "text", "type", "text");
        }
    }
}