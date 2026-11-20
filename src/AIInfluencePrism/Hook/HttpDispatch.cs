using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;
using AIInfluencePrism.UI;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Custom HttpMessageHandler that intercepts all HTTP requests initiated by AIInfluence.
    /// Eliminates network timeouts, bypasses obfuscated DelegatingHandlers (Class681),
    /// and routes LLM completion calls directly to the active Prism profile and Pipeline.
    /// </summary>
    public class PrismHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _fallbackInvoker = new HttpMessageInvoker(new HttpClientHandler());

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (request == null)
                {
                    return HttpDispatch.CreateEmptyOkResponse();
                }

                // Resolve any relative URI against loopback port 4315 (Player2)
                if (request.RequestUri == null)
                {
                    request.RequestUri = new Uri("http://127.0.0.1:4315/");
                }
                else if (!request.RequestUri.IsAbsoluteUri)
                {
                    request.RequestUri = new Uri(new Uri("http://127.0.0.1:4315/"), request.RequestUri);
                }

                LogSink.Always($"[PrismHttpMessageHandler] SendAsync: Method={request.Method}, Uri='{request.RequestUri}'");

                Profile profile = ProfileBook.Current;
                if (profile != null && !profile.MasterEnabled)
                {
                    LogSink.Always("[HttpDispatch] Master switch is disabled; passing through request to fallback handler.");
                    return await _fallbackInvoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }

                return await HttpDispatch.ProcessHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSink.Error("[PrismHttpMessageHandler] Error in SendAsync: " + LogSink.FormatException(ex), ex);
                return HttpDispatch.CreateFallbackResponse();
            }
        }
    }

    /// <summary>
    /// Intercepts outgoing HTTP requests made by AIInfluence's AIClient, Player2Client, and backends.
    /// Uses two complementary mechanisms:
    /// 1. Direct Reflection Field Replacement on all static HttpClient fields across AIInfluence assembly
    ///    (replaces AIClient.httpClient, Player2Client.httpClient, Class675.httpClient_0, etc. with PrismHttpMessageHandler).
    ///    This completely bypasses Class681 DelegatingHandler timeouts and Babel-obfuscated trampolines.
    /// 2. Harmony prefix on System.Net.Http.HttpClientHandler.SendAsync as an additional AppDomain catch-all.
    /// </summary>
    public static class HttpDispatch
    {
        private static bool _installed;
        private static bool _fieldsReplaced;
        private static HttpClient _prismHttpClient;
        private static readonly List<FieldInfo> _monitoredFields = new List<FieldInfo>();
        private static readonly object _syncLock = new object();

        public static bool Installed => _fieldsReplaced || _installed;
        public static HttpClient PrismHttpClient => _prismHttpClient;
        public static IReadOnlyList<FieldInfo> MonitoredFields => _monitoredFields;

        public static void Reset()
        {
            lock (_syncLock)
            {
                _installed = false;
                _fieldsReplaced = false;
                _monitoredFields.Clear();
                _prismHttpClient = null;
            }
        }

        /// <summary>
        /// Installs the reflection field replacement and Harmony prefix on HttpClientHandler.SendAsync.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            ReplaceHttpClientFields();

            if (_installed || harmony == null)
            {
                return;
            }

            try
            {
                MethodInfo targetMethod = typeof(HttpClientHandler).GetMethod(
                    "SendAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new Type[] { typeof(HttpRequestMessage), typeof(CancellationToken) },
                    null);

                if (targetMethod == null)
                {
                    LogSink.Warn("[Hook/HttpDispatch] HttpClientHandler.SendAsync method not found in BCL!");
                    return;
                }

                MethodInfo prefix = typeof(HttpDispatch).GetMethod(
                    nameof(SendAsyncPrefix),
                    BindingFlags.Static | BindingFlags.Public);

                if (prefix == null)
                {
                    LogSink.Error("[Hook/HttpDispatch] HttpDispatch.SendAsyncPrefix method not found!");
                    return;
                }

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefix));
                _installed = true;
                LogSink.Always("[Hook/HttpDispatch] Patched HttpClientHandler.SendAsync [Prefix]. Status: SUCCESS");
            }
            catch (Exception ex)
            {
                LogSink.Error("[Hook/HttpDispatch] Failed to patch HttpClientHandler.SendAsync: " + LogSink.FormatException(ex), ex);
            }
        }

        /// <summary>
        /// Discovers and replaces all static HttpClient fields across the AIInfluence assembly.
        /// Pre-executes class constructors (.cctor) via RuntimeHelpers to ensure replacements are never overwritten.
        /// </summary>
        public static void ReplaceHttpClientFields()
        {
            lock (_syncLock)
            {
                if (_prismHttpClient == null)
                {
                    _prismHttpClient = new HttpClient(new PrismHttpMessageHandler())
                    {
                        BaseAddress = new Uri("http://127.0.0.1:4315/"),
                        Timeout = TimeSpan.FromSeconds(60)
                    };
                }

                if (_monitoredFields.Count > 0)
                {
                    CheckAndReassertFields();
                    return;
                }

                Assembly asm = TargetScanner.TargetAssembly ?? TargetScanner.FindTargetAssembly();
                if (asm == null)
                {
                    // Scan AppDomain directly in case TargetScanner has not run yet
                    foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (string.Equals(loaded.GetName().Name, "AIInfluence", StringComparison.OrdinalIgnoreCase))
                            {
                                asm = loaded;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (asm == null)
                {
                    LogSink.Info("[HttpDispatch] AIInfluence assembly not loaded yet for field replacement.");
                    return;
                }

                Type[] allTypes;
                try
                {
                    allTypes = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    allTypes = rtle.Types.Where(t => t != null).ToArray();
                }
                catch (Exception ex)
                {
                    LogSink.Error("[HttpDispatch] Error reflecting types from AIInfluence assembly: " + ex.Message, ex);
                    return;
                }

                int count = 0;
                foreach (Type type in allTypes)
                {
                    try
                    {
                        FieldInfo[] fields = type.GetFields(
                            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                        foreach (FieldInfo field in fields)
                        {
                            if (field.FieldType == typeof(HttpClient))
                            {
                                try
                                {
                                    // Ensure static constructor for this type has executed before we overwrite its fields
                                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                                }
                                catch (Exception exCctor)
                                {
                                    LogSink.Warn($"[HttpDispatch] Note: RunClassConstructor for {type.FullName}: {exCctor.Message}");
                                }

                                try
                                {
                                    field.SetValue(null, _prismHttpClient);
                                    if (!_monitoredFields.Contains(field))
                                    {
                                        _monitoredFields.Add(field);
                                    }
                                    count++;
                                    LogSink.Always($"[HttpDispatch] Replaced static HttpClient field: {type.FullName}.{field.Name}");
                                }
                                catch (Exception exSet)
                                {
                                    LogSink.Error($"[HttpDispatch] Failed to set field {type.FullName}.{field.Name}: {exSet.Message}", exSet);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSink.Warn($"[HttpDispatch] Error inspecting fields of type {type.FullName}: {ex.Message}");
                    }
                }

                _fieldsReplaced = count > 0;
                LogSink.Always($"[HttpDispatch] Finished HttpClient field replacement: {count} field(s) replaced across AIInfluence.");
            }
        }

        /// <summary>
        /// Periodically called to verify that monitored HttpClient fields have not been reset or overwritten.
        /// </summary>
        public static void CheckAndReassertFields()
        {
            lock (_syncLock)
            {
                if (_monitoredFields.Count == 0 || _prismHttpClient == null)
                {
                    ReplaceHttpClientFields();
                    return;
                }

                foreach (FieldInfo field in _monitoredFields)
                {
                    try
                    {
                        object currentVal = field.GetValue(null);
                        if (!ReferenceEquals(currentVal, _prismHttpClient))
                        {
                            field.SetValue(null, _prismHttpClient);
                            LogSink.Always($"[HttpDispatch] Re-asserted Prism HttpClient on field: {field.DeclaringType?.FullName}.{field.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSink.Error($"[HttpDispatch] Error re-asserting field {field.DeclaringType?.FullName}.{field.Name}: {ex.Message}", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Harmony Prefix for HttpClientHandler.SendAsync(HttpRequestMessage request, CancellationToken cancellationToken).
        /// Serves as a secondary fallback if any code bypasses replaced HttpClient instances.
        /// </summary>
        public static bool SendAsyncPrefix(
            HttpRequestMessage request,
            CancellationToken cancellationToken,
            ref Task<HttpResponseMessage> __result)
        {
            try
            {
                if (request == null)
                {
                    return true;
                }

                if (!IsAiInfluenceRequest(request))
                {
                    return true;
                }

                Profile profile = ProfileBook.Current;
                if (profile != null && !profile.MasterEnabled)
                {
                    LogSink.Always("[HttpDispatch] Skipped interception: Master switch is disabled; passing through HTTP request.");
                    return true;
                }

                __result = ProcessHttpRequestAsync(request, cancellationToken);
                return false; // Skip real HTTP network transport
            }
            catch (Exception ex)
            {
                LogSink.Error("[HttpDispatch] SendAsyncPrefix error; falling through to original handler.", ex);
                return true;
            }
        }

        /// <summary>
        /// Validates whether an outgoing HTTP request originated from AIInfluence LLM/backend communication.
        /// </summary>
        public static bool IsAiInfluenceRequest(HttpRequestMessage request)
        {
            if (request == null || request.RequestUri == null)
            {
                return false;
            }

            // 1. AIInfluence specific headers
            if (request.Headers.Contains("GAME_KEY") ||
                request.Headers.Contains("X-Game-Key") ||
                request.Headers.Contains("player2-resource") ||
                request.Headers.Contains("player2-suggested-model") ||
                request.Headers.Contains("player2-forced-model"))
            {
                return true;
            }

            Uri uri = request.RequestUri;
            if (!uri.IsAbsoluteUri)
            {
                uri = new Uri(new Uri("http://127.0.0.1:4315/"), uri);
            }

            string uriString = uri.ToString();

            // 2. Local provider ports
            int port = uri.Port;
            if (port == 4315 || port == 5000 || port == 5001 || port == 11434 || port == 8080 || port == 1234)
            {
                return true;
            }

            // 3. Known API paths
            if (uriString.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/v1/completions", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/api/generate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/api/chat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/v1/embeddings", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/models", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/health", StringComparison.OrdinalIgnoreCase) >= 0 ||
                uriString.IndexOf("/joules", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // 4. Remote provider endpoints used by AIInfluence
            string host = uri.Host;
            if (host.IndexOf("openrouter.ai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                host.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads request body, routes through Prism FastRouter & Pipeline, and synthesizes a universal JSON response.
        /// </summary>
        public static async Task<HttpResponseMessage> ProcessHttpRequestAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                string uriString = request.RequestUri != null ? request.RequestUri.ToString() : string.Empty;

                // 1. Embeddings endpoint stub
                if (uriString.IndexOf("/embeddings", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LogSink.Info($"[HttpDispatch] Handled embeddings request: {uriString}");
                    var embResp = new JObject
                    {
                        ["object"] = "list",
                        ["data"] = new JArray
                        {
                            new JObject
                            {
                                ["object"] = "embedding",
                                ["index"] = 0,
                                ["embedding"] = new JArray()
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(embResp.ToString(Formatting.None), Encoding.UTF8, "application/json")
                    };
                }

                // 2. Models / Health / Joules endpoint stubs
                if (uriString.IndexOf("/models", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LogSink.Info($"[HttpDispatch] Handled models request: {uriString}");
                    var modelsResp = new JObject
                    {
                        ["object"] = "list",
                        ["data"] = new JArray()
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(modelsResp.ToString(Formatting.None), Encoding.UTF8, "application/json")
                    };
                }

                if (uriString.IndexOf("/health", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json")
                    };
                }

                if (uriString.IndexOf("/joules", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"joules\":1000000,\"balance\":1000000}", Encoding.UTF8, "application/json")
                    };
                }

                // 3. Read request content
                string jsonBody = string.Empty;
                if (request.Content != null)
                {
                    jsonBody = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                string systemPrompt = null;
                string userPrompt = null;
                string fullPrompt = null;
                string model = null;

                if (!string.IsNullOrEmpty(jsonBody))
                {
                    try
                    {
                        JObject root = JObject.Parse(jsonBody);
                        if (root["model"] != null)
                        {
                            model = (string)root["model"];
                        }

                        JArray messages = root["messages"] as JArray;
                        if (messages != null && messages.Count > 0)
                        {
                            var sb = new StringBuilder();
                            foreach (JToken msg in messages)
                            {
                                string role = (string)msg["role"];
                                string content = (string)msg["content"];
                                if (string.IsNullOrEmpty(content)) continue;

                                if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
                                {
                                    systemPrompt = content;
                                }
                                else if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                                {
                                    userPrompt = content;
                                }

                                sb.AppendLine($"[{role}]: {content}");
                            }
                            fullPrompt = sb.ToString().Trim();
                        }
                        else if (root["prompt"] != null)
                        {
                            fullPrompt = (string)root["prompt"];
                            userPrompt = fullPrompt;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSink.Error("[HttpDispatch] Error parsing request JSON: " + ex.Message, ex);
                    }
                }

                // 4. Feature and Scope resolution
                string player2Feature = null;
                if (request.Headers.TryGetValues("player2-resource", out var featureValues))
                {
                    player2Feature = featureValues.FirstOrDefault();
                }

                ScopeId scope = ScopeId.DialogueHeroes;
                if (!string.IsNullOrEmpty(player2Feature))
                {
                    scope = FastRouter.Resolve(player2Feature);
                }
                else if (!string.IsNullOrEmpty(model))
                {
                    scope = FastRouter.Resolve(model);
                }

                if (scope == ScopeId.None)
                {
                    scope = ScopeId.DialogueHeroes;
                }

                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);

                if (!scopeEnabled)
                {
                    LogSink.Always($"[HttpDispatch] Scope '{scope}' is disabled in active profile '{profileName}'; passing through HTTP request.");
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("Scope disabled in Prism", Encoding.UTF8, "text/plain")
                    };
                }

                int promptLen = !string.IsNullOrEmpty(fullPrompt) ? fullPrompt.Length : 0;
                LogSink.Always($"[HttpDispatch] Intercepted HTTP chat completion: uri='{request.RequestUri}', scope={scope}, feature='{player2Feature ?? ""}', prompt_len={promptLen}");

                Stopwatch sw = Stopwatch.StartNew();

                // 5. Route to Prism Pipeline with the ENTIRE prompt (two-stage context: system & user prompts passed cleanly)
                string promptToSend = !string.IsNullOrEmpty(fullPrompt) ? fullPrompt : (userPrompt ?? string.Empty);
                string reply = await Pipeline.SendAsync(scope, promptToSend, systemPrompt, userPrompt, player2Feature).ConfigureAwait(false);
                sw.Stop();
                AIInfluencePrism.UI.DiagnosticsTelemetry.RecordDialogue(player2Feature ?? "NPC", scope.ToString(), sw.ElapsedMilliseconds, reply);

                if (string.IsNullOrEmpty(reply))
                {
                    reply = "Мне нечего вам сказать.";
                }

                bool isDialogue = scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk;
                string payloadText = reply;
                if (reply.Contains("```") || reply.Contains("{"))
                {
                    if (isDialogue)
                    {
                        payloadText = Dispatch.FormatAiInfluenceDialogueResponse(reply);
                    }
                    else
                    {
                        payloadText = AIInfluencePrism.Post.JsonHealer.RepairContent(reply);
                    }
                }

                LogSink.Always($"[HttpDispatch] Pipeline replied ({reply.Length} chars). Returning universal completion JSON.");

                // 6. Build universal response JSON compatible with all AIInfluence backends:
                // - choices[0].message.content (OpenAI, Player2, DeepSeek, OpenRouter)
                // - choices[0].text (legacy OpenAI)
                // - results[0].text (KoboldCpp)
                // - response (Ollama /api/generate)
                // - message.content (Ollama /api/chat)
                var responseObj = new JObject
                {
                    ["id"] = "chatcmpl-prism-" + Guid.NewGuid().ToString("N"),
                    ["object"] = "chat.completion",
                    ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["model"] = model ?? "prism-routed",
                    ["choices"] = new JArray
                    {
                        new JObject
                        {
                            ["index"] = 0,
                            ["message"] = new JObject
                            {
                                ["role"] = "assistant",
                                ["content"] = payloadText
                            },
                            ["text"] = payloadText,
                            ["finish_reason"] = "stop"
                        }
                    },
                    ["results"] = new JArray
                    {
                        new JObject
                        {
                            ["text"] = payloadText
                        }
                    },
                    ["response"] = payloadText,
                    ["message"] = new JObject
                    {
                        ["role"] = "assistant",
                        ["content"] = payloadText
                    }
                };

                string respJson = responseObj.ToString(Formatting.None);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(respJson, Encoding.UTF8, "application/json")
                };
            }
            catch (Exception ex)
            {
                LogSink.Error("[HttpDispatch] Exception in ProcessHttpRequestAsync: " + LogSink.FormatException(ex), ex);
                return CreateFallbackResponse();
            }
        }

        public static HttpResponseMessage CreateEmptyOkResponse()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }

        public static HttpResponseMessage CreateFallbackResponse()
        {
            var fallbackObj = new JObject
            {
                ["choices"] = new JArray
                {
                    new JObject
                    {
                        ["index"] = 0,
                        ["message"] = new JObject
                        {
                            ["role"] = "assistant",
                            ["content"] = "Мне нечего вам сказать."
                        },
                        ["text"] = "Мне нечего вам сказать.",
                        ["finish_reason"] = "stop"
                    }
                },
                ["results"] = new JArray
                {
                    new JObject
                    {
                        ["text"] = "Мне нечего вам сказать."
                    }
                },
                ["response"] = "Мне нечего вам сказать.",
                ["message"] = new JObject
                {
                    ["role"] = "assistant",
                    ["content"] = "Мне нечего вам сказать."
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fallbackObj.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
        }
    }
}
