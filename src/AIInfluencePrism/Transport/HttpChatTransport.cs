using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    public sealed class TransportException : Exception
    {
        public TransportException(int statusCode, string bodySnippet, bool isCli = false, string rawResponse = null)
            : base(FormatMessage(statusCode, bodySnippet, isCli))
        {
            StatusCode = statusCode;
            BodySnippet = bodySnippet;
            IsCli = isCli;
            RawResponse = rawResponse ?? bodySnippet;
        }

        public bool IsCli { get; }
        public int StatusCode { get; }
        public string BodySnippet { get; }
        public string RawResponse { get; }
        public string Stderr => IsCli ? RawResponse : null;

        public static TransportException ForCli(int exitCode, string message, string stderr = null)
        {
            return new TransportException(exitCode, message, isCli: true, rawResponse: stderr ?? message);
        }

        private static string FormatMessage(int statusCode, string bodySnippet, bool isCli)
        {
            if (isCli)
            {
                if (string.IsNullOrEmpty(bodySnippet))
                {
                    return statusCode != 0 ? "CLI Process error (code " + statusCode + ")" : "CLI Process error";
                }
                if (bodySnippet.IndexOf("CLI Process error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return bodySnippet;
                }
                return statusCode != 0
                    ? "CLI Process error (code " + statusCode + "): " + bodySnippet
                    : bodySnippet;
            }

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
    }

    public abstract class HttpChatTransport : IChatTransport
    {
        private static HttpClient _shared;

        public static HttpClient SharedClient
        {
            get
            {
                if (_shared == null)
                {
                    HttpClient client = new HttpClient();
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    _shared = client;
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
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken, Action<string> onDelta = null)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            string url = EndpointUrl(profile);
            string body = BuildRequestBody(turn, options, profile);
            LogSink.Info("http: POST " + url + " (" + body.Length + " bytes).");

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
                            throw new TransportException(429, string.IsNullOrEmpty(snippet) ? "Quota exceeded or subscription expired (HTTP 429)" : snippet, isCli: false, rawResponse: raw);
                        }
                        if (code == 503)
                        {
                            throw new TransportException(503, string.IsNullOrEmpty(snippet) ? "Model temporarily unavailable due to high demand (HTTP 503)" : snippet, isCli: false, rawResponse: raw);
                        }
                        throw new TransportException(code, snippet, isCli: false, rawResponse: raw);
                    }

                    string parsed = ParseReply(raw);
                    onDelta?.Invoke(parsed);
                    return parsed;
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
                        LogSink.Info("probe: " + url + " -> " + code);

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
                LogSink.Error("probe: " + url + " failed.", ex);
                throw;
            }
        }

        protected virtual string ProbeUrl(Profile profile)
        {
            return EndpointUrl(profile);
        }

        protected static string Snippet(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 300 ? flat : flat.Substring(0, 300) + "…";
        }

        public static void EnsureTls12()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }
    }
}
