using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Post;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// OpenAI-compatible protocol: POST {root}/v1/chat/completions.
    /// Serves both modes: plain (stream=false) and SSE (stream=true,
    /// the reply is accumulated incrementally from deltas, calling onDelta).
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

            if (options.MaxTokens > 0)
            {
                body["max_tokens"] = options.MaxTokens;
            }

            if (options.Model.Length > 0)
            {
                body["model"] = options.Model;
            }

            if (!string.IsNullOrEmpty(options.ReasoningEffort) &&
                !string.Equals(options.ReasoningEffort, "default", StringComparison.OrdinalIgnoreCase))
            {
                body["reasoning_effort"] = options.ReasoningEffort.ToLowerInvariant();
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

        public override async Task<string> SendAsync(
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken, Action<string> onDelta = null)
        {
            if (!_stream)
            {
                return await base.SendAsync(turn, options, profile, cancellationToken, onDelta).ConfigureAwait(false);
            }

            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            string url = EndpointUrl(profile);
            string body = BuildRequestBody(turn, options, profile);
            LogSink.Info("http: POST (stream) " + url + " (" + body.Length + " bytes).");

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
            {
                request.Content = content;
                ConfigureRequest(request, profile);

                using (HttpResponseMessage response = await SharedClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string raw = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        int code = (int)response.StatusCode;
                        string snippet = Snippet(raw);
                        if (code == 429)
                        {
                            throw new TransportException(429, string.IsNullOrEmpty(snippet) ? "Quota exceeded or subscription expired (HTTP 429)" : snippet, isCli: false, rawResponse: raw);
                        }
                        if (code == 503)
                        {
                            throw new TransportException(503, string.IsNullOrEmpty(snippet) ? "Model temporarily unavailable due to high demand (HTTP 503)" : snippet, isCli: false, rawResponse: raw);
                        }
                        throw new TransportException(code, snippet, isCli: false, rawResponse: raw);
                    }

                    if (response.Content == null)
                    {
                        return string.Empty;
                    }

                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string text = await ReplyReader.ReadSseAsync(
                            reader, onDelta, "choices[0].delta.content", "choices[0].text").ConfigureAwait(false);
                        return text ?? string.Empty;
                    }
                }
            }
        }

        public override string ParseReply(string rawBody)
        {
            if (LooksLikeSse(rawBody))
            {
                return AccumulateSse(rawBody);
            }

            return ReplyReader.ReadJson(
                rawBody, "choices[0].message.content", "choices[0].text");
        }

        public static bool LooksLikeSse(string rawBody)
        {
            return ReplyReader.LooksLikeSse(rawBody);
        }

        public static string AccumulateSse(string sseBody)
        {
            return ReplyReader.ReadSse(
                sseBody, "choices[0].delta.content", "choices[0].text");
        }

        protected override string ProbeUrl(Profile profile)
        {
            return EndpointMapper.V1BaseUrl(profile.EffectiveRoot()) + "/models";
        }
    }
}
