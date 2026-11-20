using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Post;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Player2: OpenAI-compatible chat with its own defaults, auto-login and health check.
    /// </summary>
    public sealed class Player2Transport : OpenAiTransport
    {
        public static readonly string ClientId = Guid.NewGuid().ToString("N");

        private static string _sessionKey;
        private static readonly object LoginGate = new object();

        public Player2Transport()
            : base(ProviderKind.Player2, stream: false)
        {
        }

        public override string EndpointUrl(Profile profile)
        {
            return EndpointMapper.ChatCompletionsUrl(
                profile.RootUrl != null && profile.RootUrl.Trim().Length > 0
                    ? profile.RootUrl
                    : Profile.DefaultRoot(ProviderKind.Player2));
        }

        public override async Task<string> SendAsync(
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken, Action<string> onDelta = null)
        {
            if (EffectiveKey(profile).Length == 0)
            {
                TryLoginOnce(profile);
            }

            return await base.SendAsync(turn, options, profile, cancellationToken, onDelta).ConfigureAwait(false);
        }

        public string EffectiveKey(Profile profile)
        {
            string key = profile.ApiKey == null ? string.Empty : profile.ApiKey.Trim();
            if (key.Length > 0)
            {
                return key;
            }

            lock (LoginGate)
            {
                return _sessionKey ?? string.Empty;
            }
        }

        public override void ConfigureRequest(HttpRequestMessage request, Profile profile)
        {
            string key = EffectiveKey(profile);
            if (key.Length > 0)
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }

            request.Headers.Add("X-Player2-Trace-Id", Guid.NewGuid().ToString("N"));
        }

        protected override string ProbeUrl(Profile profile)
        {
            return EndpointMapper.Player2HealthUrl(
                profile.RootUrl != null && profile.RootUrl.Trim().Length > 0
                    ? profile.RootUrl
                    : Profile.DefaultRoot(ProviderKind.Player2));
        }

        public bool TryLoginOnce(Profile profile)
        {
            lock (LoginGate)
            {
                if (_sessionKey != null)
                {
                    return _sessionKey.Length > 0;
                }

                string root = profile.RootUrl != null && profile.RootUrl.Trim().Length > 0
                    ? profile.RootUrl
                    : Profile.DefaultRoot(ProviderKind.Player2);
                string url = EndpointMapper.Player2LoginUrl(root, ClientId);

                try
                {
                    using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300)))
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
                    using (StringContent empty = new StringContent("{}", Encoding.UTF8, "application/json"))
                    {
                        request.Content = empty;
                        using (HttpResponseMessage response = SharedClient
                            .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                            .GetAwaiter()
                            .GetResult())
                        {
                            string raw = response.Content == null
                                ? string.Empty
                                : response.Content.ReadAsStringAsync()
                                    .ConfigureAwait(false).GetAwaiter().GetResult();

                            string key = response.IsSuccessStatusCode ? ParseLogin(raw) : null;
                            _sessionKey = key ?? string.Empty;

                            if (key != null)
                            {
                                LogSink.Info("player2: auto-login ok; Authorization enabled.");
                            }
                            else
                            {
                                LogSink.Info("player2: auto-login unavailable (HTTP "
                                    + (int)response.StatusCode + "); continuing without Authorization.");
                            }

                            return _sessionKey.Length > 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _sessionKey = string.Empty;
                    LogSink.Error("player2: auto-login failed; continuing without Authorization.", ex);
                    return false;
                }
            }
        }

        public static string ParseLogin(string rawBody)
        {
            return ReplyReader.ReadField(rawBody, "p2Key");
        }
    }
}
