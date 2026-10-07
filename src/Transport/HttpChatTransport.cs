using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Transport-layer error: non-2xx status or the network is down.
    /// A separate type so the pipeline can tell it from parse errors.
    /// </summary>
    public sealed class TransportException : Exception
    {
        public TransportException(int statusCode, string bodySnippet)
            : base("HTTP " + statusCode + (bodySnippet.Length > 0 ? ": " + bodySnippet : string.Empty))
        {
            StatusCode = statusCode;
            BodySnippet = bodySnippet;
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
        private static HttpClient _shared;

        protected static HttpClient SharedClient
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
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
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
                        throw new TransportException((int)response.StatusCode, Snippet(raw));
                    }

                    return ParseReply(raw);
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
                        // Any answer (including 4xx) means "the server is alive and it is this one".
                        LogSink.Info("probe: " + url + " -> " + (int)response.StatusCode);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("probe: " + url + " failed.", ex);
                return false;
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