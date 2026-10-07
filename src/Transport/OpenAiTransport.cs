using System;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// OpenAI-compatible protocol: POST {root}/v1/chat/completions.
    /// Serves both modes: plain (stream=false) and SSE (stream=true,
    /// the reply is accumulated from deltas). Player2 is a thin
    /// tuning on top of it.
    /// </summary>
    public class OpenAiTransport : HttpChatTransport
    {
        private readonly bool _stream;

        public OpenAiTransport(ProviderKind kind, bool stream)
        {
            Kind = kind;
            _stream = stream;
        }

        public override ProviderKind Kind { get; }

        public override string EndpointUrl(Profile profile)
        {
            return EndpointMapper.ChatCompletionsUrl(profile.EffectiveRoot());
        }

        public override string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile)
        {
            JObject messages = new JObject();
            JArray list = new JArray();

            if (!string.IsNullOrEmpty(turn.System))
            {
                list.Add(new JObject
                {
                    { "role", "system" },
                    { "content", turn.System }
                });
            }

            list.Add(new JObject
            {
                { "role", "user" },
                { "content", turn.User }
            });

            JObject body = new JObject
            {
                { "messages", list },
                { "temperature", options.Temperature },
                { "stream", _stream }
            };

            // 0 = no limit: omit the token field so the provider uses its default.
            if (options.MaxTokens > 0)
            {
                body["max_tokens"] = options.MaxTokens;
            }

            // An empty model id is valid only for Player2; others require it.
            if (options.Model.Length > 0)
            {
                body["model"] = options.Model;
            }

            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        public override void ConfigureRequest(HttpRequestMessage request, Profile profile)
        {
            string key = profile.ApiKey == null ? string.Empty : profile.ApiKey.Trim();
            if (key.Length > 0)
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }
        }

        public override string ParseReply(string rawBody)
        {
            if (LooksLikeSse(rawBody))
            {
                return AccumulateSse(rawBody);
            }

            // Prefer the chat.completions shape; fall back to the
            // legacy completions shape (some OpenAI-compatible servers).
            return ReplyReader.ReadJson(
                rawBody, "choices[0].message.content", "choices[0].text");
        }

        /// <summary>Does the body look like an SSE stream (reply to stream=true)?</summary>
        public static bool LooksLikeSse(string rawBody)
        {
            return ReplyReader.LooksLikeSse(rawBody);
        }

        /// <summary>
        /// Join a chat SSE stream into full text. Broken frames are
        /// skipped: losing one fragment beats losing the whole reply.
        /// </summary>
        public static string AccumulateSse(string sseBody)
        {
            return ReplyReader.ReadSse(
                sseBody, "choices[0].delta.content", "choices[0].text");
        }
    }
}