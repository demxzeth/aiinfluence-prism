using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Config;
using AIInfluencePrism.Hook;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Entry point of the send pipeline: profile -&gt; transport -&gt; HTTP -&gt; text.
    /// Dispatch hands intercepted prompts here; errors always become
    /// a text reply, never an exception — the game must not crash
    /// because a provider is unreachable.
    /// </summary>
    public static class Pipeline
    {
        private static readonly OpenAiTransport OpenAiPlain =
            new OpenAiTransport(ProviderKind.OpenAiCompat, stream: false);

        private static readonly OpenAiTransport OpenAiStream =
            new OpenAiTransport(ProviderKind.OpenAiSse, stream: true);

        private static readonly AnthropicTransport Anthropic = new AnthropicTransport();
        private static readonly GeminiTransport Gemini = new GeminiTransport();
        private static readonly OllamaTransport Ollama = new OllamaTransport();
        private static readonly KoboldCppTransport Kobold = new KoboldCppTransport();
        private static readonly Player2Transport Player2 = new Player2Transport();

        static Pipeline()
        {
            HttpChatTransport.EnsureTls12();
        }

        /// <summary>Transport by provider kind. Public for unit tests.</summary>
        public static IChatTransport Resolve(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.OpenAiCompat: return OpenAiPlain;
                case ProviderKind.OpenAiSse: return OpenAiStream;
                case ProviderKind.Anthropic: return Anthropic;
                case ProviderKind.Gemini: return Gemini;
                case ProviderKind.Ollama: return Ollama;
                case ProviderKind.KoboldCpp: return Kobold;
                case ProviderKind.Player2: return Player2;
                default: return Player2;
            }
        }

        /// <summary>
        /// Send substitution for unit tests: when set, no real HTTP
        /// happens. Always null in the product.
        /// </summary>
        public static Func<TargetRole, ScopeId, string, int, MethodBase, Task<string>> SenderForTests
        {
            get;
            set;
        }

        /// <summary>
        /// Probe substitution for unit tests: when set, the real transport
        /// is not called. Always null in the product.
        /// </summary>
        public static Func<Profile, Task<bool>> ProbeForTests
        {
            get;
            set;
        }

        /// <summary>
        /// Send an intercepted prompt to the active provider.
        /// Always returns a completed task with text (success or a
        /// diagnostic error message).
        /// </summary>
        public static async Task<string> SendAsync(
            TargetRole role, ScopeId scope, string prompt, int cachePrefix, MethodBase originalMethod)
        {
            Func<TargetRole, ScopeId, string, int, MethodBase, Task<string>> sender = SenderForTests;
            if (sender != null)
            {
                return await sender(role, scope, prompt, cachePrefix, originalMethod).ConfigureAwait(false);
            }

            Profile profile = ProfileBook.Current;
            // Per-scope settings take precedence when the scope is routed through Prism.
            ScopeSettings scopeSettings = profile.Scope(scope);
            if (scopeSettings.Enabled)
            {
                profile = scopeSettings.ToProfile(scope.ToString());
            }

            IChatTransport transport = Resolve(profile.Provider);
            ChatTurn turn = ChatTurn.FromPrompt(prompt);
            ModelOptions options = ModelOptions.FromProfile(profile);
            Stopwatch clock = Stopwatch.StartNew();

            if (scopeSettings.TimerInChat)
            {
                ChatNotices.Show("- Request sent", scope.ToString());
            }

            try
            {
                int timeoutSeconds = profile.TimeoutSeconds < 10 ? 10 : profile.TimeoutSeconds;
                using (CancellationTokenSource timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    Task<string> sendTask = transport.SendAsync(turn, options, profile, timeout.Token);

                    // Periodic progress timer while the request is in flight.
                    Task<object> progress = scopeSettings.TimerInChat
                        ? RunProgressTicker(scope, sendTask, clock)
                        : Task.FromResult<object>(null);

                    string reply = await sendTask.ConfigureAwait(false);
                    clock.Stop();

                    if (string.IsNullOrEmpty(reply))
                    {
                        if (scopeSettings.TimerInChat)
                        {
                            ChatNotices.Error("empty reply from " + Profile.Label(profile.Provider), scope.ToString());
                        }

                        LogSink.Warn("pipeline[" + scope + "]: empty reply from "
                            + Profile.Label(profile.Provider) + " in " + clock.ElapsedMilliseconds + " ms.");
                        return string.Empty;
                    }

                    LogSink.Always("pipeline[" + scope + "]: " + Profile.Label(profile.Provider)
                        + " replied " + reply.Length + " chars in " + clock.ElapsedMilliseconds + " ms"
                        + " (cachePrefix=" + cachePrefix + ", role=" + role + ").");
                    ElapsedTicker.Add(clock.Elapsed.TotalMilliseconds);

                    if (scopeSettings.TimerInChat)
                    {
                        ChatNotices.Show("- Response received in "
                            + FormatSeconds(clock.ElapsedMilliseconds), scope.ToString(),
                            0.35f, 0.85f, 0.35f);
                    }

                    return reply;
                }
            }
            catch (Exception ex)
            {
                clock.Stop();
                ElapsedTicker.Add(clock.Elapsed.TotalMilliseconds);

                if (profile.ErrorNoticesEnabled)
                {
                    ChatNotices.Error("request failed (" + clock.ElapsedMilliseconds + " ms)", scope.ToString());
                }

                LogSink.Error("pipeline[" + scope + "]: request failed ("
                    + Profile.Label(profile.Provider) + ", " + clock.ElapsedMilliseconds + " ms"
                    + ", url=" + transport.EndpointUrl(profile) + ").", ex);
                return "[Prism] request failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Fire-and-forget helper: every ~10 seconds posts a "still waiting"
        /// notice until the send task completes.
        /// </summary>
        private static async Task<object> RunProgressTicker(
            ScopeId scope, Task<string> sendTask, Stopwatch clock)
        {
            try
            {
                while (!sendTask.IsCompleted)
                {
                    Task completed = await Task.WhenAny(sendTask, Task.Delay(10000)).ConfigureAwait(false);
                    if (completed == sendTask)
                    {
                        break;
                    }

                    ChatNotices.Show("- Timer: " + (clock.ElapsedMilliseconds / 1000) + "s", scope.ToString());
                }
            }
            catch
            {
                // The ticker is best-effort; a failure must not break the send.
            }

            return null;
        }

        /// <summary>Human-readable seconds for chat notices ("12s", "1m 4s").</summary>
        public static string FormatSeconds(long elapsedMs)
        {
            long totalSeconds = elapsedMs / 1000;
            if (totalSeconds < 60)
            {
                return totalSeconds + "s";
            }

            return (totalSeconds / 60) + "m " + (totalSeconds % 60) + "s";
        }

        /// <summary>
        /// Connection test for the MCM button: probe the active provider
        /// and report the outcome in the chat/log. Never throws — uses
        /// the probe test hook when set (unit tests), otherwise a real
        /// transport probe with a 10-second timeout.
        /// </summary>
        public static string ProbeCurrent(Profile profile = null)
        {
            Profile active = profile ?? ProfileBook.Current;
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10)))
                {
                    Func<Profile, Task<bool>> probe = ProbeForTests;
                    bool ok = probe != null
                        ? probe(active).GetAwaiter().GetResult()
                        : Resolve(active.Provider).ProbeAsync(active, timeout.Token)
                            .GetAwaiter().GetResult();

                    clock.Stop();

                    if (ok)
                    {
                        ChatNotices.Show("Connection OK (" + clock.ElapsedMilliseconds
                            + " ms): " + EndpointLabel(active), null, 0.35f, 0.85f, 0.35f);
                        return "ok";
                    }

                    ChatNotices.Error("Connection failed: " + EndpointLabel(active));
                    return "failed";
                }
            }
            catch (Exception ex)
            {
                clock.Stop();
                ChatNotices.Error("connection test error: " + ex.Message);
                return "error: " + ex.Message;
            }
        }

        /// <summary>
        /// Connection test for one scope (the MCM "Test" button in a scope group).
        /// Uses the scope's own settings; reports "[Prism - &lt;scope&gt;] Connection OK/failed".
        /// </summary>
        public static string ProbeScope(ScopeId scope)
        {
            Profile active = ProfileBook.Current.Scope(scope).ToProfile(scope.ToString());
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10)))
                {
                    Func<Profile, Task<bool>> probe = ProbeForTests;
                    bool ok = probe != null
                        ? probe(active).GetAwaiter().GetResult()
                        : Resolve(active.Provider).ProbeAsync(active, timeout.Token)
                            .GetAwaiter().GetResult();

                    clock.Stop();
                    string label = EndpointLabel(active);

                    if (ok)
                    {
                        ChatNotices.Show("Connection OK (" + clock.ElapsedMilliseconds + " ms): "
                            + label, scope.ToString(), 0.35f, 0.85f, 0.35f);
                        return "ok";
                    }

                    ChatNotices.Error("Connection failed: " + label, scope.ToString());
                    return "failed";
                }
            }
            catch (Exception ex)
            {
                clock.Stop();
                ChatNotices.Error("Connection test error: " + ex.Message, scope.ToString());
                return "error: " + ex.Message;
            }
        }

        /// <summary>
        /// Connection test against every enabled scope (the MCM "Test all" button).
        /// </summary>
        public static void ProbeAllEnabledScopes()
        {
            int okCount = 0;
            int total = 0;

            foreach (ScopeDescriptor descriptor in ScopeCatalog.All)
            {
                if (descriptor.Id == ScopeId.None)
                {
                    continue;
                }

                ScopeSettings s = ProfileBook.Current.Scope(descriptor.Id);
                if (!s.Enabled)
                {
                    continue;
                }

                total++;
                if (string.Equals(ProbeScope(descriptor.Id), "ok", StringComparison.Ordinal))
                {
                    okCount++;
                }
            }

            ChatNotices.Show("Test all: " + okCount + "/" + total + " connected.");
        }

/// <summary>
        /// Connection test to the model itself: sends one tiny real prompt
        /// through the scope's provider and reports the model's reply in the
        /// chat (green "Connection OK" + response preview + elapsed time).
        /// </summary>
        public static string ProbeModel(ScopeId scope)
        {
            Profile profile = ProfileBook.Current.Scope(scope).ToProfile(scope.ToString());
            IChatTransport transport = Resolve(profile.Provider);
            Stopwatch clock = Stopwatch.StartNew();

            string title = "Prism test message. Reply with exactly: pong";

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10)))
                {
                    ChatTurn turn = ChatTurn.FromPrompt(title);
                    ModelOptions options = ModelOptions.FromProfile(profile);
                    // Small token cap keeps the model test fast.
                    options = new ModelOptions(options.Model, options.Temperature, 64);

                    string reply = transport
                        .SendAsync(turn, options, profile, timeout.Token)
                        .GetAwaiter().GetResult();

                    clock.Stop();

                    if (string.IsNullOrEmpty(reply) || reply.Length == 0)
                    {
                        // An empty reply is still a valid connection — the model
                        // may just return nothing for the tiny test prompt.
                        ChatNotices.Show("- Connection OK (model responded in "
                            + FormatSeconds(clock.ElapsedMilliseconds)
                            + ", empty reply)", scope.ToString(), 0.35f, 0.85f, 0.35f);
                        return "ok";
                    }

                    string preview = reply.Trim();
                    if (preview.Length > 120)
                    {
                        preview = preview.Substring(0, 120) + "…";
                    }

                    ChatNotices.Show("Connection OK (model responded in "
                        + FormatSeconds(clock.ElapsedMilliseconds) + "): \""
                        + preview + "\"", scope.ToString(), 0.35f, 0.85f, 0.35f);
                    return "ok";
                }
            }
            catch (Exception ex)
            {
                clock.Stop();
                ChatNotices.Error("connection test error: " + ex.Message, scope.ToString());
                return "error: " + ex.Message;
            }
        }

        private static string EndpointLabel(Profile profile)
        {
            try
            {
                return Resolve(profile.Provider).EndpointUrl(profile);
            }
            catch
            {
                return Profile.Label(profile.Provider);
            }
        }
    }
}