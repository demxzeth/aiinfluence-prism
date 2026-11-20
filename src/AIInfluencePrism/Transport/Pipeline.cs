using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AIInfluence.API;
using AIInfluencePrism.Hook;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Routing;
using AIInfluencePrism.UI;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Entry point of the send pipeline: profile -> transport -> HTTP/CLI -> text / StreamedAIResponse.
    /// Intercepted requests are handled here; errors always produce a valid JSON fallback
    /// conforming to the dialogue schema ({"response": "[Prism Error: ...]", "emotion": "neutral"})
    /// and a red notice in the chat, preventing game crashes or broken dialogue states.
    /// </summary>
    public static class Pipeline
    {
        static Pipeline()
        {
            HttpChatTransport.EnsureTls12();
        }

        /// <summary>
        /// Transport resolution via TransportFactory.
        /// </summary>
        public static IChatTransport Resolve(ProviderKind kind)
        {
            return TransportFactory.Resolve(kind);
        }

        /// <summary>
        /// Send substitution for unit tests: when set, no real network calls occur.
        /// </summary>
        public static Func<ScopeId, PromptPayload, Action<string>, CancellationToken, Task<StreamedAIResponse>> StreamingSenderForTests
        {
            get;
            set;
        }

        /// <summary>
        /// String send substitution for unit tests.
        /// </summary>
        public static Func<ScopeId, string, Task<string>> SenderForTests
        {
            get;
            set;
        }

        /// <summary>
        /// Probe substitution for unit tests.
        /// </summary>
        public static Func<Profile, Task<bool>> ProbeForTests
        {
            get;
            set;
        }

        /// <summary>
        /// Sends a streaming prompt payload to the active provider for the given scope.
        /// Calls onDelta as tokens arrive. Returns a StreamedAIResponse struct.
        /// On error, returns a safe fallback JSON dialogue response and alerts the player in chat.
        /// </summary>
        public static async Task<StreamedAIResponse> SendStreamingAsync(
            ScopeId scope,
            PromptPayload payload,
            Action<string> onDelta,
            CancellationToken cancellationToken,
            string player2Feature = null,
            string npcName = null,
            string faction = null)
        {
            if (StreamingSenderForTests != null)
            {
                return await StreamingSenderForTests(scope, payload, onDelta, cancellationToken).ConfigureAwait(false);
            }

            Profile profile = ProfileBook.Current;
            ScopeSettings scopeSettings = profile.Scope(scope);
            if (scopeSettings.Enabled)
            {
                profile = scopeSettings.ToProfile(scope.ToString(), profile);
            }

            IChatTransport transport = Resolve(profile.Provider);
            ChatTurn turn = ChatTurn.FromPayload(payload);
            turn.Scope = scope;
            ModelOptions options = ModelOptions.FromProfile(profile);
            Stopwatch clock = Stopwatch.StartNew();

            if (scopeSettings.TimerInChat)
            {
                ChatNotices.Show("- Request sent", scope.ToString());
            }

            int timeoutSeconds = profile.TimeoutSeconds < 10 ? 10 : profile.TimeoutSeconds;
            string url = SafeEndpointUrl(transport, profile);
            string model = options.Model ?? string.Empty;

            LogSink.Always($"[Pipeline] Sending to {profile.Provider} (model='{model}', url='{url}', timeout={timeoutSeconds})");

            // Streaming instrumentation: TTFT, chunk counting, periodic progress
            int chunkCount = 0;
            int totalChars = 0;
            long firstTokenMs = -1;
            long lastProgressMs = 0;

            Action<string> instrumentedOnDelta = (delta) =>
            {
                if (!string.IsNullOrEmpty(delta))
                {
                    long elapsed = clock.ElapsedMilliseconds;
                    chunkCount++;
                    totalChars += delta.Length;

                    if (firstTokenMs < 0)
                    {
                        firstTokenMs = elapsed;
                        LogSink.Always($"[Pipeline] Stream first token received (TTFT: {firstTokenMs} ms, provider={profile.Provider}, scope={scope})");
                    }
                    else if (elapsed - lastProgressMs >= 1000 || chunkCount % 50 == 0)
                    {
                        lastProgressMs = elapsed;
                        LogSink.Info($"[Pipeline] Stream progress: {chunkCount} chunks, {totalChars} chars in {elapsed} ms (provider={profile.Provider}, scope={scope})");
                    }
                }

                onDelta?.Invoke(delta);
            };

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 5)))
            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    Task<string> sendTask = transport.SendAsync(turn, options, profile, linkedCts.Token, instrumentedOnDelta);

                    Task<object> progress = scopeSettings.TimerInChat
                        ? RunProgressTicker(scope, sendTask, clock)
                        : Task.FromResult<object>(null);

                    string reply = await sendTask.ConfigureAwait(false);
                    clock.Stop();

                    if (firstTokenMs >= 0 || chunkCount > 0)
                    {
                        LogSink.Always($"[Pipeline] Stream completed: {chunkCount} chunks, {reply?.Length ?? totalChars} chars in {clock.ElapsedMilliseconds} ms (TTFT: {(firstTokenMs >= 0 ? firstTokenMs + " ms" : "N/A")}, provider={profile.Provider}, scope={scope})");
                    }

                    if (string.IsNullOrEmpty(reply))
                    {
                        if (scopeSettings.TimerInChat)
                        {
                            ChatNotices.Error("Empty reply from " + Profile.Label(profile.Provider), scope.ToString());
                        }

                        LogSink.Warn("pipeline[" + scope + "]: empty reply from "
                            + Profile.Label(profile.Provider) + " in " + clock.ElapsedMilliseconds + " ms.");
                        string emptyFallback = BuildErrorFallback("Empty response from AI provider", scope);
                        return new StreamedAIResponse(emptyFallback, fromStream: true, completed: true);
                    }

                    LogSink.Always("pipeline[" + scope + "]: " + Profile.Label(profile.Provider)
                        + " replied " + reply.Length + " chars in " + clock.ElapsedMilliseconds + " ms"
                        + " (feature=" + (player2Feature ?? "null") + ", npc=" + (npcName ?? "null") + ").");

                    if (scopeSettings.TimerInChat)
                    {
                        ChatNotices.Show("- Response received in "
                            + FormatSeconds(clock.ElapsedMilliseconds), scope.ToString(),
                            0.35f, 0.85f, 0.35f);
                    }

                    return new StreamedAIResponse(reply, fromStream: true, completed: true);
                }
                catch (Exception ex)
                {
                    clock.Stop();

                    LogSink.Error($"[ERROR] [Pipeline] Provider {profile.Provider} failed! Exception: {ex.GetType().Name}: {ex.Message}. StackTrace: {ex.StackTrace}");
                    DumpHttpOrCliError(ex, profile.Provider);

                    int errorCode = 1;
                    string detail = ExtractErrorDetail(ex, out errorCode);

                    if (profile.ErrorNoticesEnabled)
                    {
                        ChatNotices.RequestFailed(scope.ToString(), errorCode, detail);
                    }

                    LogSink.Error("pipeline[" + scope + "]: request failed ("
                        + Profile.Label(profile.Provider) + ", " + clock.ElapsedMilliseconds + " ms"
                        + ", url=" + SafeEndpointUrl(transport, profile) + "). Reason: " + detail, ex);

                    string fallback = BuildErrorFallback(detail, scope);
                    return new StreamedAIResponse(fallback, fromStream: true, completed: true);
                }
            }
        }

        /// <summary>
        /// Sends an intercepted non-streaming prompt to the active provider for the given scope.
        /// Returns reply text (or valid JSON fallback error response).
        /// </summary>
        public static async Task<string> SendAsync(
            ScopeId scope,
            string prompt,
            string splitSystem = null,
            string splitUser = null,
            string player2Feature = null,
            string npcName = null,
            string faction = null)
        {
            if (SenderForTests != null)
            {
                return await SenderForTests(scope, prompt).ConfigureAwait(false);
            }

            Profile profile = ProfileBook.Current;
            ScopeSettings scopeSettings = profile.Scope(scope);
            if (scopeSettings.Enabled)
            {
                profile = scopeSettings.ToProfile(scope.ToString(), profile);
            }

            IChatTransport transport = Resolve(profile.Provider);
            // Single unified prompt string: Send the ENTIRE prompt text directly without splitting or slicing!
            ChatTurn turn = ChatTurn.FromPrompt(prompt);
            turn.Scope = scope;
            ModelOptions options = ModelOptions.FromProfile(profile);
            Stopwatch clock = Stopwatch.StartNew();

            if (scopeSettings.TimerInChat)
            {
                ChatNotices.Show("- Request sent", scope.ToString());
            }

            int timeoutSeconds = profile.TimeoutSeconds < 10 ? 10 : profile.TimeoutSeconds;
            string url = SafeEndpointUrl(transport, profile);
            string model = options.Model ?? string.Empty;

            LogSink.Always($"[Pipeline] Sending to {profile.Provider} (model='{model}', url='{url}', timeout={timeoutSeconds})");

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 5)))
            {
                try
                {
                    Task<string> sendTask = transport.SendAsync(turn, options, profile, timeoutCts.Token, null);

                    Task<object> progress = scopeSettings.TimerInChat
                        ? RunProgressTicker(scope, sendTask, clock)
                        : Task.FromResult<object>(null);

                    string reply = await sendTask.ConfigureAwait(false);
                    clock.Stop();

                    if (string.IsNullOrEmpty(reply))
                    {
                        if (scopeSettings.TimerInChat)
                        {
                            ChatNotices.Error("Empty reply from " + Profile.Label(profile.Provider), scope.ToString());
                        }

                        LogSink.Warn("pipeline[" + scope + "]: empty reply from "
                            + Profile.Label(profile.Provider) + " in " + clock.ElapsedMilliseconds + " ms.");
                        return BuildErrorFallback("Empty response from AI provider", scope);
                    }

                    LogSink.Always("pipeline[" + scope + "]: " + Profile.Label(profile.Provider)
                        + " replied " + reply.Length + " chars in " + clock.ElapsedMilliseconds + " ms"
                        + " (feature=" + (player2Feature ?? "null") + ", npc=" + (npcName ?? "null") + ").");

                    if (scopeSettings.TimerInChat)
                    {
                        ChatNotices.Show("- Response received in "
                            + FormatSeconds(clock.ElapsedMilliseconds), scope.ToString(),
                            0.35f, 0.85f, 0.35f);
                    }

                    return reply;
                }
                catch (Exception ex)
                {
                    clock.Stop();

                    LogSink.Error($"[ERROR] [Pipeline] Provider {profile.Provider} failed! Exception: {ex.GetType().Name}: {ex.Message}. StackTrace: {ex.StackTrace}");
                    DumpHttpOrCliError(ex, profile.Provider);

                    int errorCode = 1;
                    string detail = ExtractErrorDetail(ex, out errorCode);

                    if (profile.ErrorNoticesEnabled)
                    {
                        ChatNotices.RequestFailed(scope.ToString(), errorCode, detail);
                    }

                    LogSink.Error("pipeline[" + scope + "]: request failed ("
                        + Profile.Label(profile.Provider) + ", " + clock.ElapsedMilliseconds + " ms"
                        + ", url=" + SafeEndpointUrl(transport, profile) + "). Reason: " + detail, ex);

                    return BuildErrorFallback(detail, scope);
                }
            }
        }

        /// <summary>
        /// Backward-compatible overload for legacy string hooks.
        /// </summary>
        public static Task<string> SendAsync(
            ScopeId scope,
            PromptPayload payload,
            string player2Feature = null)
        {
            ChatTurn turn = ChatTurn.FromPayload(payload);
            return SendAsync(scope, turn.Prompt, null, null, player2Feature);
        }

        /// <summary>
        /// Legacy method overload with method reflection context.
        /// </summary>
        public static Task<string> SendAsync(
            object role, ScopeId scope, string prompt, int cachePrefix, MethodBase originalMethod,
            string splitSystem = null, string splitUser = null)
        {
            return SendAsync(scope, prompt, null, null, null);
        }

        /// <summary>
        /// Generates a valid JSON fallback object matching the Bannerlord / AIInfluence dialogue schema:
        /// {"response": "[Prism Error: &lt;detail&gt;]", "emotion": "neutral"}
        /// </summary>
        public static string BuildErrorFallback(string detail, ScopeId scope = ScopeId.None)
        {
            string cleanDetail = detail ?? "An unknown error occurred.";
            string escaped = JsonConvert.ToString("[Prism Error: " + cleanDetail + "]");
            string fallbackJson = "{\"response\": " + escaped + ", \"emotion\": \"neutral\"}";
            LogSink.Always($"[Pipeline] Returning fallback JSON: {fallbackJson}");
            return fallbackJson;
        }

        private static void DumpHttpOrCliError(Exception ex, ProviderKind provider)
        {
            if (ex == null) return;

            // 1. Antigravity / CLI stderr dump
            if (provider == ProviderKind.Antigravity || (ex is TransportException teCli && teCli.IsCli))
            {
                string stderr = null;
                if (ex is TransportException te && !string.IsNullOrEmpty(te.RawResponse))
                {
                    stderr = te.RawResponse;
                }
                if (!string.IsNullOrEmpty(stderr))
                {
                    LogSink.Error($"[ERROR] [Pipeline] Antigravity process stderr dump:{Environment.NewLine}{stderr}");
                }
            }

            // 2. HTTP response body dump from TransportException
            if (ex is TransportException teHttp && !teHttp.IsCli)
            {
                LogSink.Error($"[ERROR] [Pipeline] Server HTTP response body dump (Status: {teHttp.StatusCode}):{Environment.NewLine}{teHttp.RawResponse}");
            }

            // 3. Inspect inner exceptions for WebException and HttpRequestException
            Exception cur = ex;
            while (cur != null)
            {
                if (cur is WebException wex)
                {
                    if (wex.Response is HttpWebResponse webResponse)
                    {
                        try
                        {
                            using (Stream stream = webResponse.GetResponseStream())
                            {
                                if (stream != null)
                                {
                                    using (StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                                    {
                                        string body = reader.ReadToEnd();
                                        LogSink.Error($"[ERROR] [Pipeline] WebException HTTP {(int)webResponse.StatusCode} ({webResponse.StatusDescription}) response body:{Environment.NewLine}{body}");
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        LogSink.Error($"[ERROR] [Pipeline] WebException status: {wex.Status}, message: {wex.Message}");
                    }
                }
                else if (cur is HttpRequestException hre)
                {
                    LogSink.Error($"[ERROR] [Pipeline] HttpRequestException: {hre.Message}");
                }

                cur = cur.InnerException;
            }
        }

        private static string ExtractErrorDetail(Exception ex, out int errorCode)
        {
            errorCode = 1;
            string detail = null;

            if (ex is TransportException te)
            {
                errorCode = te.StatusCode;
                detail = te.Message;
            }
            else if (ex.InnerException is TransportException innerTe)
            {
                errorCode = innerTe.StatusCode;
                detail = innerTe.Message;
            }
            else
            {
                detail = FormatErrorMessage(ex);
            }

            if (detail != null && detail.StartsWith("CLI Process error (code", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = detail.IndexOf("):", StringComparison.Ordinal);
                if (colonIdx > 0 && colonIdx + 2 < detail.Length)
                {
                    detail = detail.Substring(colonIdx + 2).Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = "An unknown error occurred.";
            }

            return detail;
        }

        private static string SafeEndpointUrl(IChatTransport transport, Profile profile)
        {
            try
            {
                return transport.EndpointUrl(profile);
            }
            catch
            {
                return "unknown";
            }
        }

        /// <summary>
        /// Periodic progress timer while the request is in flight.
        /// </summary>
        private static async Task<object> RunProgressTicker(
            ScopeId scope, Task<string> sendTask, Stopwatch clock)
        {
            try
            {
                while (!sendTask.IsCompleted)
                {
                    Task delayTask = Task.Delay(10000);
                    Task completed = await Task.WhenAny(sendTask, delayTask).ConfigureAwait(false);
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
        /// Connection test for MCM button: probes active provider.
        /// </summary>
        public static string ProbeCurrent(Profile profile = null)
        {
            Profile active = profile ?? ProfileBook.Current;
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    Func<Profile, Task<bool>> probe = ProbeForTests;
                    bool ok = probe != null
                        ? probe(active).GetAwaiter().GetResult()
                        : Resolve(active.Provider).ProbeAsync(active, timeout.Token).GetAwaiter().GetResult();

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
                string msg = FormatErrorMessage(ex);
                ChatNotices.Error("Connection test error: " + msg);
                return "error: " + msg;
            }
        }

        /// <summary>
        /// Connection test for one scope (the MCM "Test" button in a scope group).
        /// </summary>
        public static string ProbeScope(ScopeId scope)
        {
            Profile current = ProfileBook.Current;
            Profile active = current.Scope(scope).ToProfile(scope.ToString(), current);
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    Func<Profile, Task<bool>> probe = ProbeForTests;
                    bool ok = probe != null
                        ? probe(active).GetAwaiter().GetResult()
                        : Resolve(active.Provider).ProbeAsync(active, timeout.Token).GetAwaiter().GetResult();

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
                string msg = FormatErrorMessage(ex);
                ChatNotices.Error("Connection test error: " + msg, scope.ToString());
                return "error: " + msg;
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
        /// Connection test to the model itself: sends a tiny test prompt.
        /// </summary>
        public static string ProbeModel(ScopeId scope)
        {
            Profile current = ProfileBook.Current;
            Profile profile = current.Scope(scope).ToProfile(scope.ToString(), current);
            IChatTransport transport = Resolve(profile.Provider);
            Stopwatch clock = Stopwatch.StartNew();

            string title = "Prism test message. Reply with exactly: pong";

            try
            {
                int timeoutSec = Math.Max(60, profile.TimeoutSeconds);
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
                {
                    ChatTurn turn = ChatTurn.FromPrompt(title);
                    turn.Scope = scope;
                    ModelOptions options = ModelOptions.FromProfile(profile);
                    options = new ModelOptions(options.Model, options.Temperature, 64);

                    string reply = transport
                        .SendAsync(turn, options, profile, timeout.Token)
                        .GetAwaiter().GetResult();

                    clock.Stop();

                    if (string.IsNullOrEmpty(reply))
                    {
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
                string msg = FormatErrorMessage(ex);
                ChatNotices.Error("Connection test error: " + msg, scope.ToString());
                return "error: " + msg;
            }
        }

        /// <summary>
        /// General CLI test for Antigravity: checks if agy.exe is executable and reports its version.
        /// </summary>
        public static string ProbeAntigravityCli()
        {
            Profile current = ProfileBook.Current;
            string cli = AntigravityTransport.ResolveCliPath(current.AntigravityCliPath);
            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    ProcessResult res = AntigravityTransport.RunProcessAsync(cli, "--version", 10, cts.Token)
                        .GetAwaiter().GetResult();
                    clock.Stop();

                    if (res.ExitCode == 0)
                    {
                        string ver = res.Output.Trim();
                        ChatNotices.Show("CLI OK (" + clock.ElapsedMilliseconds + " ms): "
                            + (string.IsNullOrEmpty(ver) ? "ready" : ver), "Antigravity", 0.35f, 0.85f, 0.35f);
                        return "ok";
                    }

                    string err = !string.IsNullOrWhiteSpace(res.Error) ? res.Error : "Exit code " + res.ExitCode;
                    ChatNotices.Error("CLI test failed: " + err, "Antigravity");
                    return "failed";
                }
            }
            catch (Exception ex)
            {
                clock.Stop();
                string msg = FormatErrorMessage(ex);
                ChatNotices.Error("CLI test error: " + msg, "Antigravity");
                return "error: " + msg;
            }
        }

        /// <summary>
        /// Model test for Antigravity: sends a small ping prompt via agy.exe.
        /// </summary>
        public static string ProbeAntigravityModel()
        {
            Profile current = ProfileBook.Current;
            string cli = AntigravityTransport.ResolveCliPath(current.AntigravityCliPath);
            string model = !string.IsNullOrWhiteSpace(current.AntigravityModel)
                ? current.AntigravityModel.Trim()
                : "gemini-3.8-flash-high";

            Stopwatch clock = Stopwatch.StartNew();

            try
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.Append("--input-format text");
                    sb.Append(" --model ");
                    sb.Append(AntigravityTransport.EscapeArgument(model));
                    sb.Append(" --disable-slash-commands");

                    ProcessResult res = AntigravityTransport.RunProcessAsync(
                        cli, sb.ToString(), 60, cts.Token, "Prism test message. Reply with exactly: pong")
                        .GetAwaiter().GetResult();
                    clock.Stop();

                    if (res.ExitCode == 0)
                    {
                        string reply = res.Output.Trim();
                        if (string.IsNullOrEmpty(reply))
                        {
                            ChatNotices.Show("Connection OK (model responded in "
                                + FormatSeconds(clock.ElapsedMilliseconds)
                                + ", empty reply)", "Antigravity", 0.35f, 0.85f, 0.35f);
                        }
                        else
                        {
                            string preview = reply;
                            if (preview.Length > 120)
                            {
                                preview = preview.Substring(0, 120) + "…";
                            }
                            ChatNotices.Show("Connection OK (model responded in "
                                + FormatSeconds(clock.ElapsedMilliseconds) + "): \""
                                + preview + "\"", "Antigravity", 0.35f, 0.85f, 0.35f);
                        }
                        return "ok";
                    }

                    string err = !string.IsNullOrWhiteSpace(res.Error) ? res.Error : "Exit code " + res.ExitCode;
                    ChatNotices.Error("Model test failed: " + err, "Antigravity");
                    return "failed";
                }
            }
            catch (Exception ex)
            {
                clock.Stop();
                string msg = FormatErrorMessage(ex);
                ChatNotices.Error("Model test error: " + msg, "Antigravity");
                return "error: " + msg;
            }
        }

        public static bool IsContentSafetyError(Exception ex, string output = null)
        {
            string combined = ((ex != null ? ex.Message : string.Empty) + " " + (output ?? string.Empty)).ToLowerInvariant();
            return combined.Contains("content safety") ||
                   combined.Contains("safety filter") ||
                   combined.Contains("safety_ratings") ||
                   combined.Contains("blocked by content safety") ||
                   combined.Contains("blocked by safety");
        }

        public static string FormatErrorMessage(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            if (ex is AggregateException agg && agg.InnerExceptions != null && agg.InnerExceptions.Count > 0)
            {
                return FormatErrorMessage(agg.InnerExceptions[0]);
            }

            if (ex.InnerException is TransportException innerTe)
            {
                return FormatErrorMessage(innerTe);
            }

            if (ex is TransportException te)
            {
                if (te.IsCli)
                {
                    return te.Message;
                }
                if (te.StatusCode == 429)
                {
                    return "Quota exceeded or subscription expired (HTTP 429)";
                }
                if (te.StatusCode == 503)
                {
                    return "Model temporarily unavailable due to high demand (HTTP 503)";
                }
                return te.Message;
            }

            return ex.Message;
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
