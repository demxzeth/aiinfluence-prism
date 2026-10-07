using System;
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
    /// Player2: the same OpenAI-compatible chat, but with its own
    /// defaults, auto-login and a health check.
    ///  - local mode: root http://127.0.0.1:4315, the key may be omitted;
    ///  - cloud: https://api.player2.game, the key is required;
    ///  - auto-login: POST /v1/login/web/{clientId} -> {"p2Key":"..."};
    ///  - connection test: GET /v1/health;
    ///  - no model required: no auth header sent, the model field is skipped.
    /// </summary>
    public sealed class Player2Transport : OpenAiTransport
    {
        /// <summary>Client id of this session (used for auto-login).</summary>
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
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken)
        {
            // Local mode without a key: try auto-login once.
            if (EffectiveKey(profile).Length == 0)
            {
                TryLoginOnce(profile);
            }

            return await base.SendAsync(turn, options, profile, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The key actually signed with: from the profile or from auto-login.</summary>
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

        /// <summary>
        /// Auto-login for local mode. Once per session; on failure we keep
        /// working without Authorization (the local app is usually open).
        /// </summary>
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
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
                    using (StringContent empty = new StringContent("{}", Encoding.UTF8, "application/json"))
                    {
                        request.Content = empty;
                        using (HttpResponseMessage response = SharedClient
                            .SendAsync(request, HttpCompletionOption.ResponseContentRead, CancellationToken.None)
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

        /// <summary>Extract p2Key from the login reply. Public for unit tests.</summary>
        public static string ParseLogin(string rawBody)
        {
            return ReplyReader.ReadField(rawBody, "p2Key");
        }
    }
}