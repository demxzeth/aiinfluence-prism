using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Transport-layer error: non-2xx status or the network is down.
    /// A separate type so the pipeline can tell it from parse errors.
    /// </summary>
    public sealed class TransportException : Exception
    {
        public TransportException(int statusCode, string bodySnippet)
            : base(FormatMessage(statusCode, bodySnippet))
        {
            StatusCode = statusCode;
            BodySnippet = bodySnippet;
        }

        private static string FormatMessage(int statusCode, string bodySnippet)
        {
            if (string.IsNullOrEmpty(bodySnippet))
            {
                return "HTTP " + statusCode;
            }
            if (bodySnippet.IndexOf("HTTP " + statusCode, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return bodySnippet;
            }
            return "HTTP " + statusCode + ": " + bodySnippet;
        }

        /// <summary>HTTP status of the reply (0 — network error).</summary>
        public int StatusCode { get; }

        /// <summary>Start of the reply body, for diagnostics.</summary>
        public string BodySnippet { get; }
    }

    /// <summary>
    /// Shared HTTP plumbing for all protocols: one HttpClient per
    /// process, the timeout is driven by the cancellation token (the
    /// client itself is infinite, because LLM requests can outlive the
    /// default 100 seconds). Protocol-specific concerns (URL, body,
    /// headers, parsing) live in the subclasses.
    /// </summary>
    public abstract class HttpChatTransport : IChatTransport
    {
        private static readonly object _clientLock = new object();
        private static HttpClient _shared;

        static HttpChatTransport()
        {
            EnsureTls12();
            try
            {
                ServicePointManager.DefaultConnectionLimit = 64;
            }
            catch
            {
                // the platform decides itself; continue quietly.
            }
        }

        /// <summary>
        /// Creates an HttpClientHandler configured with system proxy and local bypass.
        /// </summary>
        public static HttpClientHandler CreateHandler()
        {
            IWebProxy proxy = WebRequest.GetSystemWebProxy();
            if (proxy is WebProxy webProxy)
            {
                webProxy.BypassProxyOnLocal = true;
            }

            return new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true
            };
        }

        public static HttpClient SharedClient
        {
            get
            {
                if (_shared == null)
                {
                    lock (_clientLock)
                    {
                        if (_shared == null)
                        {
                            HttpClient client = new HttpClient(CreateHandler());
                            client.Timeout = Timeout.InfiniteTimeSpan;
                            _shared = client;
                        }
                    }
                }

                return _shared;
            }
        }

        public abstract ProviderKind Kind { get; }

        public abstract string EndpointUrl(Profile profile);

        public abstract string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile);

        public abstract void ConfigureRequest(HttpRequestMessage request, Profile profile);

        public abstract string ParseReply(string rawBody);

        public virtual async Task<string> SendAsync(
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            string url = EndpointUrl(profile);
            string body = BuildRequestBody(turn, options, profile);
            LogSink.Info("http: POST " + EndpointMapper.MaskUrl(url) + " (" + body.Length + " bytes).");

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            using (StringContent content = new StringContent(body, Encoding.UTF8, "application/json"))
            {
                request.Content = content;
                ConfigureRequest(request, profile);

                using (HttpResponseMessage response = await SharedClient
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    string raw = response.Content == null
                        ? string.Empty
                        : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        string snippet = Snippet(raw);
                        if (code == 429)
                        {
                            throw new TransportException(429, string.IsNullOrEmpty(snippet) ? "Quota exceeded or subscription expired (HTTP 429)" : snippet);
                        }
                        if (code == 503)
                        {
                            throw new TransportException(503, string.IsNullOrEmpty(snippet) ? "Model temporarily unavailable due to high demand (HTTP 503)" : snippet);
                        }
                        throw new TransportException(code, snippet);
                    }

                    return SanitizeReply(ParseReply(raw));
                }
            }
        }

        public virtual async Task<bool> ProbeAsync(Profile profile, CancellationToken cancellationToken)
        {
            string url = ProbeUrl(profile);
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    ConfigureRequest(request, profile);
                    using (HttpResponseMessage response = await SharedClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        int code = (int)response.StatusCode;
                        LogSink.Info("probe: " + EndpointMapper.MaskUrl(url) + " -> " + code);

                        if (response.IsSuccessStatusCode)
                        {
                            return true;
                        }

                        if (code == 429)
                        {
                            throw new TransportException(429, "Quota exceeded or subscription expired (HTTP 429)");
                        }
                        if (code == 503)
                        {
                            throw new TransportException(503, "Model temporarily unavailable due to high demand (HTTP 503)");
                        }
                        if (code == 401 || code == 403)
                        {
                            throw new TransportException(code, "Authentication failed (HTTP " + code + " - check API key)");
                        }
                        if (code == 404)
                        {
                            throw new TransportException(404, "Endpoint not found (HTTP 404 - check URL/model)");
                        }

                        throw new TransportException(code, "Server returned HTTP " + code);
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("probe: " + EndpointMapper.MaskUrl(url) + " failed.", ex);
                throw;
            }
        }

        /// <summary>URL for the connection test; protocols with a "native" health endpoint override it.</summary>
        protected virtual string ProbeUrl(Profile profile)
        {
            return EndpointUrl(profile);
        }

        /// <summary>Trim the payload text for the error message.</summary>
        protected static string Snippet(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 300 ? flat : flat.Substring(0, 300) + "…";
        }

        /// <summary>
        /// Strips markdown code fences (```json ... ```) and repairs malformed JSON
        /// using JsonHealer if JsonRepairEnabled is true.
        /// </summary>
        public static string SanitizeReply(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string trimmed = text.Trim();

            // Strip markdown code fences (e.g. ```json ... ``` or ``` ... ```)
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline > 0)
                {
                    int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                    if (lastFence > firstNewline)
                    {
                        trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                    }
                }
            }

            // Heal JSON if needed and repair is enabled
            if (ProfileBook.Current.JsonRepairEnabled && trimmed.Contains("{"))
            {
                string healed = JsonHealer.Heal(trimmed);
                if (!string.IsNullOrEmpty(healed))
                {
                    return healed;
                }
            }

            return trimmed;
        }

        /// <summary>TLS 1.2 for net472 (some providers only speak it).</summary>
        public static void EnsureTls12()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                // the platform decides itself; continue quietly.
            }
        }
    }
}