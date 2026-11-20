using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AIInfluence.API;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Harmony interceptors for AIInfluence 7.0.6:
    /// - Prefix: Intercepts GetStreamingAIResponse (Task&lt;StreamedAIResponse&gt;).
    /// - StringPrefix / AiResponsePrefix: Intercepts Task&lt;string&gt; methods (GetAIResponse, GetRawTextResponse*, GetPlayer2Response*, etc.).
    /// Uses universal object[] __args and reflection-based payload extraction to prevent MonoMod/Harmony struct layout mismatches
    /// across obfuscated assemblies.
    /// Deterministically routes requests using FastRouter without stack tracing or marker scanning.
    /// </summary>
    public static class Dispatch
    {
        /// <summary>
        /// Silver Bullet 1: Forces AIClient.StreamsResponses property getter to return false.
        /// When StreamsResponses returns false, AIInfluence completely bypasses GetStreamingAIResponse
        /// and unconditionally routes all dialogue through GetAIResponse (Task&lt;string&gt;).
        /// </summary>
        public static bool StreamsResponsesPrefix(ref bool __result)
        {
            __result = false;
            return false; // Skip original property getter execution
        }

        public static void StreamsResponsesPostfix(ref bool __result)
        {
            __result = false;
        }

        /// <summary>
        /// Silver Bullet 2: Untyped Harmony Prefix for AIClient.GetStreamingAIResponse.
        /// Uses ref object __result to eliminate assembly-boundary metadata token mismatches on Task&lt;StreamedAIResponse&gt;.
        /// When in game, dynamically completes TaskCompletionSource for the game's StreamedAIResponse type.
        /// </summary>
        public static bool StreamingUntypedPrefix(
            object[] __args,
            MethodBase __originalMethod,
            ref object __result)
        {
            Type returnType = (__originalMethod as MethodInfo)?.ReturnType ?? typeof(Task<StreamedAIResponse>);
            if (returnType == typeof(Task<StreamedAIResponse>))
            {
                Task<StreamedAIResponse> typedTask = null;
                bool pass = Prefix(__args, __originalMethod, ref typedTask);
                if (!pass)
                {
                    __result = typedTask;
                }
                return pass;
            }

            try
            {
                ExtractCallArgs(
                    __args,
                    __originalMethod,
                    out string npcName,
                    out string faction,
                    out string player2Feature,
                    out string requestLabel,
                    out string promptText,
                    out string systemContent,
                    out string userContent,
                    out int cachePrefix,
                    out Action<string> onDelta,
                    out CancellationToken cancellationToken);

                int promptLen = promptText != null ? promptText.Length : 0;
                LogSink.Always($"[Dispatch] Intercepted Streaming: method='{__originalMethod?.Name ?? ""}', npc='{npcName ?? ""}', faction='{faction ?? ""}', feature='{player2Feature ?? ""}', prompt_len={promptLen}, onDelta={(onDelta != null ? "yes" : "no")}");

                ScopeId scope = FastRouter.Resolve(player2Feature, requestLabel);
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);

                if (!masterEnabled || !scopeEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped streaming interception: master={masterEnabled}, scope={scopeEnabled}; passing through.");
                    return true;
                }

                Type itemType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : null;
                if (itemType == null) return true;

                Type tcsType = typeof(TaskCompletionSource<>).MakeGenericType(itemType);
                object tcs = Activator.CreateInstance(tcsType);
                MethodInfo setResultMethod = tcsType.GetMethod("SetResult");
                PropertyInfo taskProperty = tcsType.GetProperty("Task");

                if ((scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk) &&
                    profile.Scope(scope).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(promptText, userContent);
                }

                Task.Run(async () =>
                {
                    try
                    {
                        string rawReply = await Pipeline.SendAsync(scope, promptText ?? string.Empty, null, null, player2Feature).ConfigureAwait(false);
                        string dialogueJson = FormatAiInfluenceDialogueResponse(rawReply);

                        object streamedInstance = Activator.CreateInstance(itemType);
                        FieldInfo textField = itemType.GetField("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo fromStreamField = itemType.GetField("FromStream", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        FieldInfo completedField = itemType.GetField("Completed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        textField?.SetValue(streamedInstance, dialogueJson);
                        fromStreamField?.SetValue(streamedInstance, true);
                        completedField?.SetValue(streamedInstance, true);

                        if (onDelta != null)
                        {
                            try { onDelta(dialogueJson); } catch { }
                        }

                        setResultMethod?.Invoke(tcs, new object[] { streamedInstance });
                    }
                    catch (Exception ex)
                    {
                        LogSink.Error("[Dispatch] Streaming background task error: " + ex.Message, ex);
                        try
                        {
                            object fallbackInstance = Activator.CreateInstance(itemType);
                            FieldInfo textField = itemType.GetField("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            FieldInfo completedField = itemType.GetField("Completed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            textField?.SetValue(fallbackInstance, "{\"response\": \"Мне сейчас нечего сказать.\", \"emotion\": \"neutral\"}");
                            completedField?.SetValue(fallbackInstance, true);
                            setResultMethod?.Invoke(tcs, new object[] { fallbackInstance });
                        }
                        catch { }
                    }
                });

                __result = taskProperty.GetValue(tcs);
                return false;
            }
            catch (Exception ex)
            {
                LogSink.Error("[Dispatch] StreamingUntypedPrefix failed: " + ex.Message, ex);
                return true;
            }
        }

        /// <summary>
        /// Harmony Prefix for AIClient.GetStreamingAIResponse.
        /// Uses object[] __args to avoid struct layout and calling convention mismatches on obfuscated PromptPayload.
        /// </summary>
        public static bool Prefix(
            object[] __args,
            MethodBase __originalMethod,
            ref Task<StreamedAIResponse> __result)
        {
            try
            {
                ExtractCallArgs(
                    __args,
                    __originalMethod,
                    out string npcName,
                    out string faction,
                    out string player2Feature,
                    out string requestLabel,
                    out string promptText,
                    out string systemContent,
                    out string userContent,
                    out int cachePrefix,
                    out Action<string> onDelta,
                    out CancellationToken cancellationToken);

                int promptLen = promptText != null ? promptText.Length : 0;
                LogSink.Always($"[Dispatch] Intercepted Streaming: method='{__originalMethod?.Name ?? ""}', npc='{npcName ?? ""}', faction='{faction ?? ""}', feature='{player2Feature ?? ""}', prompt_len={promptLen}, onDelta={(onDelta != null ? "yes" : "no")}");

                ScopeId scope = FastRouter.Resolve(player2Feature, requestLabel);
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);
                bool effectivelyEnabled = masterEnabled && scopeEnabled;

                LogSink.Always($"[Router] Mapped feature='{player2Feature ?? ""}' / label='{requestLabel ?? ""}' -> scope={scope}, active_profile='{profileName}', enabled={effectivelyEnabled.ToString().ToLowerInvariant()}");

                if (!masterEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Master switch is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                if (!scopeEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Scope '{scope}' is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                if ((scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk) &&
                    profile.Scope(scope).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(promptText, userContent);
                }

                var payload = new PromptPayload(promptText ?? string.Empty, cachePrefix);
                __result = Pipeline.SendStreamingAsync(
                    scope,
                    payload,
                    onDelta,
                    cancellationToken,
                    player2Feature,
                    npcName,
                    faction);

                return false;
            }
            catch (Exception ex)
            {
                LogSink.Error("dispatch[streaming]: prefix failed; passing through to AIInfluence backend.", ex);
                return true;
            }
        }

        /// <summary>
        /// Strongly-typed Harmony Prefix for AIClient.GetStreamingAIResponse.
        /// Avoids boxing PromptPayload into object[] __args.
        /// </summary>
        public static bool StreamingPrefix(
            string npcName,
            string faction,
            PromptPayload payload,
            Action<string> onDelta,
            CancellationToken cancellationToken,
            string player2Feature,
            MethodBase __originalMethod,
            ref Task<StreamedAIResponse> __result)
        {
            try
            {
                string promptText = payload.FullText;
                string systemContent = payload.SystemContent;
                string userContent = payload.UserContent;
                int cachePrefix = payload.StaticPrefixLength;

                if (string.IsNullOrEmpty(systemContent) || string.IsNullOrEmpty(userContent))
                {
                    TryDeriveTextSplit(promptText, cachePrefix, out systemContent, out userContent);
                }

                int promptLen = promptText != null ? promptText.Length : 0;
                LogSink.Always($"[Dispatch] Intercepted Streaming: method='{__originalMethod?.Name ?? ""}', npc='{npcName ?? ""}', faction='{faction ?? ""}', feature='{player2Feature ?? ""}', prompt_len={promptLen}, onDelta={(onDelta != null ? "yes" : "no")}");

                ScopeId scope = FastRouter.Resolve(player2Feature);
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);
                bool effectivelyEnabled = masterEnabled && scopeEnabled;

                LogSink.Always($"[Router] Mapped feature='{player2Feature ?? ""}' -> scope={scope}, active_profile='{profileName}', enabled={effectivelyEnabled.ToString().ToLowerInvariant()}");

                // 1. Master switch: when off, Prism passes the original through to AIInfluence backend.
                if (!masterEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Master switch is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                // 2. Per-scope switch: when this scope is disabled in Prism, pass through to AIInfluence.
                if (!scopeEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Scope '{scope}' is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                // 3. Concurrent image generation for dialogue scopes if enabled
                if ((scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk) &&
                    profile.Scope(scope).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(promptText, userContent);
                }

                // 4. Intercept and stream through Prism pipeline
                __result = Pipeline.SendStreamingAsync(
                    scope,
                    payload,
                    onDelta,
                    cancellationToken,
                    player2Feature,
                    npcName,
                    faction);

                return false; // Skip original method
            }
            catch (Exception ex)
            {
                LogSink.Error("dispatch[streaming]: prefix failed; passing through to AIInfluence backend.", ex);
                return true;
            }
        }

        /// <summary>
        /// Dedicated strongly-typed Harmony prefix for AIClient.GetAIResponse and GetPlayer2Response.
        /// Bypasses object[] __args boxing for PromptPayload to ensure zero struct-layout trampoline issues.
        /// </summary>
        public static bool AiResponsePrefix(
            string npcName,
            string faction,
            PromptPayload payload,
            string player2Feature,
            MethodBase __originalMethod,
            ref Task<string> __result)
        {
            try
            {
                string promptText = payload.FullText;
                string systemContent = payload.SystemContent;
                string userContent = payload.UserContent;
                int cachePrefix = payload.StaticPrefixLength;

                if (string.IsNullOrEmpty(systemContent) || string.IsNullOrEmpty(userContent))
                {
                    TryDeriveTextSplit(promptText, cachePrefix, out systemContent, out userContent);
                }

                int promptLen = promptText != null ? promptText.Length : 0;
                LogSink.Always($"[Dispatch] Intercepted AiResponse: method='{__originalMethod?.Name ?? ""}', npc='{npcName ?? ""}', faction='{faction ?? ""}', feature='{player2Feature ?? ""}', prompt_len={promptLen}, onDelta=no");

                ScopeId scope = FastRouter.Resolve(player2Feature);
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);
                bool effectivelyEnabled = masterEnabled && scopeEnabled;

                LogSink.Always($"[Router] Mapped feature='{player2Feature ?? ""}' -> scope={scope}, active_profile='{profileName}', enabled={effectivelyEnabled.ToString().ToLowerInvariant()}");

                // 1. Master switch
                if (!masterEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Master switch is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                // 2. Per-scope switch
                if (!scopeEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Scope '{scope}' is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                // 3. Concurrent image generation if dialogue
                if ((scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk) &&
                    profile.Scope(scope).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(promptText, userContent);
                }

                // 4. Intercept call and route through Prism pipeline
                __result = Task.Run(async () =>
                {
                    string reply = await Pipeline.SendAsync(scope, promptText ?? string.Empty, null, null, player2Feature, npcName, faction).ConfigureAwait(false);
                    AIInfluencePrism.UI.DiagnosticsTelemetry.RecordDialogue(npcName ?? player2Feature ?? "NPC", scope.ToString(), 0, reply);
                    if (Pipeline.SenderForTests != null)
                    {
                        return reply ?? string.Empty;
                    }
                    return FormatAiInfluenceDialogueResponse(reply);
                });

                return false;
            }
            catch (Exception ex)
            {
                LogSink.Error("dispatch[airesponse]: prefix failed; passing through.", ex);
                return true;
            }
        }

        /// <summary>
        /// Dedicated strongly-typed Harmony prefix for methods taking PromptPayload payload.
        /// Matches parameter 'payload' by name without boxing into object[] __args.
        /// </summary>
        public static bool RawPayloadPrefix(
            PromptPayload payload,
            MethodBase __originalMethod,
            ref Task<string> __result)
        {
            try
            {
                string promptText = payload.FullText;
                string systemContent = payload.SystemContent;
                string userContent = payload.UserContent;
                int cachePrefix = payload.StaticPrefixLength;

                if (string.IsNullOrEmpty(systemContent) || string.IsNullOrEmpty(userContent))
                {
                    TryDeriveTextSplit(promptText, cachePrefix, out systemContent, out userContent);
                }

                string methodName = __originalMethod != null ? __originalMethod.Name : string.Empty;
                int promptLen = promptText != null ? promptText.Length : 0;
                LogSink.Always($"[Dispatch] Intercepted RawPayload: method='{methodName}', prompt_len={promptLen}");

                ScopeId scope = FastRouter.Resolve(null, methodName);
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);
                bool effectivelyEnabled = masterEnabled && scopeEnabled;

                LogSink.Always($"[Router] Mapped method='{methodName}' -> scope={scope}, active_profile='{profileName}', enabled={effectivelyEnabled.ToString().ToLowerInvariant()}");

                if (!masterEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Master switch is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                if (!scopeEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Scope '{scope}' is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                if ((scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk) &&
                    profile.Scope(scope).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(promptText, userContent);
                }

                bool isRawText = methodName.IndexOf("RawText", StringComparison.OrdinalIgnoreCase) >= 0;

                __result = Task.Run(async () =>
                {
                    string reply = await Pipeline.SendAsync(scope, promptText ?? string.Empty, systemContent, userContent, methodName).ConfigureAwait(false);
                    if (isRawText || Pipeline.SenderForTests != null)
                    {
                        return isRawText ? AIInfluencePrism.Post.JsonHealer.RepairContent(reply) : (reply ?? string.Empty);
                    }
                    return FormatAiInfluenceDialogueResponse(reply);
                });
                return false;
            }
            catch (Exception ex)
            {
                LogSink.Error("dispatch[rawpayload]: prefix failed; passing through.", ex);
                return true;
            }
        }

        /// <summary>
        /// Dedicated Harmony prefix for AIClient.GetAIResponse.
        /// Forwards directly to StringPrefix.
        /// </summary>
        public static bool AiResponsePrefix(
            object[] __args,
            MethodBase __originalMethod,
            ref Task<string> __result)
        {
            return StringPrefix(__args, __originalMethod, ref __result);
        }

        /// <summary>
        /// Harmony Prefix for all Task&lt;string&gt; methods:
        /// - Primary: GetAIResponse (all overloads), GetRawTextResponse* (all overloads)
        /// - Direct backends: GetPlayer2Response, GetPlayer2RawResponse, GetOpenRouterResponse, GetDeepSeekResponse, GetOllamaResponse, etc.
        /// </summary>
        public static bool StringPrefix(
            object[] __args,
            MethodBase __originalMethod,
            ref Task<string> __result)
        {
            try
            {
                ExtractCallArgs(
                    __args,
                    __originalMethod,
                    out string npcName,
                    out string faction,
                    out string player2Feature,
                    out string requestLabel,
                    out string promptText,
                    out string systemContent,
                    out string userContent,
                    out int cachePrefix,
                    out _,
                    out _);

                int promptLen = promptText != null ? promptText.Length : 0;
                LogSink.Always($"[Dispatch] Intercepted String: method='{__originalMethod?.Name ?? ""}', npc='{npcName ?? ""}', faction='{faction ?? ""}', feature='{player2Feature ?? ""}', prompt_len={promptLen}, onDelta=no");

                ScopeId scope = FastRouter.Resolve(player2Feature, requestLabel);
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = scope == ScopeId.None || (profile != null && profile.Scope(scope).Enabled);
                bool effectivelyEnabled = masterEnabled && scopeEnabled;

                LogSink.Always($"[Router] Mapped feature='{player2Feature ?? ""}' / label='{requestLabel ?? ""}' -> scope={scope}, active_profile='{profileName}', enabled={effectivelyEnabled.ToString().ToLowerInvariant()}");

                // 1. Master switch
                if (!masterEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Master switch is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                // 2. Per-scope switch
                if (!scopeEnabled)
                {
                    LogSink.Always($"[Dispatch] Skipped interception: Scope '{scope}' is disabled in active profile '{profileName}'; passing through to AIInfluence backend.");
                    return true;
                }

                // 3. Concurrent image generation if dialogue
                if ((scope == ScopeId.DialogueHeroes || scope == ScopeId.DialogueTownsfolk) &&
                    profile.Scope(scope).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(promptText, userContent);
                }

                // 4. Intercept call and route through Prism pipeline
                string methodName = __originalMethod != null ? __originalMethod.Name : string.Empty;
                bool isRawText = methodName.IndexOf("RawText", StringComparison.OrdinalIgnoreCase) >= 0;

                __result = Task.Run(async () =>
                {
                    string reply = await Pipeline.SendAsync(scope, promptText ?? string.Empty, null, null, player2Feature).ConfigureAwait(false);
                    AIInfluencePrism.UI.DiagnosticsTelemetry.RecordDialogue(player2Feature ?? "NPC", scope.ToString(), 0, reply);
                    if (isRawText || Pipeline.SenderForTests != null)
                    {
                        return isRawText ? AIInfluencePrism.Post.JsonHealer.RepairContent(reply) : (reply ?? string.Empty);
                    }
                    return FormatAiInfluenceDialogueResponse(reply);
                });

                return false;
            }
            catch (Exception ex)
            {
                LogSink.Error("dispatch[string]: prefix failed; passing through.", ex);
                return true;
            }
        }

        private static void ExtractCallArgs(
            object[] args,
            MethodBase originalMethod,
            out string npcName,
            out string faction,
            out string player2Feature,
            out string requestLabel,
            out string promptText,
            out string systemContent,
            out string userContent,
            out int cachePrefix,
            out Action<string> onDelta,
            out CancellationToken cancellationToken)
        {
            npcName = null;
            faction = null;
            player2Feature = null;
            requestLabel = null;
            promptText = null;
            systemContent = null;
            userContent = null;
            cachePrefix = 0;
            onDelta = null;
            cancellationToken = CancellationToken.None;

            if (args != null && args.Length > 0)
            {
                ParameterInfo[] parameters = originalMethod != null ? originalMethod.GetParameters() : null;
                for (int i = 0; i < args.Length; i++)
                {
                    string pName = (parameters != null && i < parameters.Length) ? parameters[i].Name : null;
                    object val = args[i];
                    if (val == null) continue;

                    if (string.Equals(pName, "player2Feature", StringComparison.OrdinalIgnoreCase))
                    {
                        player2Feature = val as string;
                    }
                    else if (string.Equals(pName, "requestLabel", StringComparison.OrdinalIgnoreCase) || string.Equals(pName, "label", StringComparison.OrdinalIgnoreCase))
                    {
                        requestLabel = val as string;
                    }
                    else if (string.Equals(pName, "npcName", StringComparison.OrdinalIgnoreCase))
                    {
                        npcName = val as string;
                    }
                    else if (string.Equals(pName, "faction", StringComparison.OrdinalIgnoreCase))
                    {
                        faction = val as string;
                    }
                    else if (string.Equals(pName, "prompt", StringComparison.OrdinalIgnoreCase))
                    {
                        promptText = val as string;
                    }
                    else if (string.Equals(pName, "systemPrompt", StringComparison.OrdinalIgnoreCase))
                    {
                        systemContent = val as string;
                    }
                    else if (val is Action<string> cb)
                    {
                        onDelta = cb;
                    }
                    else if (val is CancellationToken ct)
                    {
                        cancellationToken = ct;
                    }
                    else if (val is int pfx)
                    {
                        cachePrefix = pfx;
                    }

                    // Check if value is PromptPayload (either statically typed or dynamically boxed across assemblies)
                    Type vt = val.GetType();
                    if (vt.Name == "PromptPayload" || vt.FullName == "AIInfluence.API.PromptPayload" || val is PromptPayload)
                    {
                        try
                        {
                            PropertyInfo piFull = vt.GetProperty("FullText", BindingFlags.Public | BindingFlags.Instance);
                            if (piFull != null && piFull.GetValue(val, null) is string full && !string.IsNullOrEmpty(full))
                            {
                                promptText = full;
                            }

                            PropertyInfo piSys = vt.GetProperty("SystemContent", BindingFlags.Public | BindingFlags.Instance);
                            if (piSys != null && piSys.GetValue(val, null) is string sys && !string.IsNullOrEmpty(sys))
                            {
                                systemContent = sys;
                            }

                            PropertyInfo piUsr = vt.GetProperty("UserContent", BindingFlags.Public | BindingFlags.Instance);
                            if (piUsr != null && piUsr.GetValue(val, null) is string usr && !string.IsNullOrEmpty(usr))
                            {
                                userContent = usr;
                            }

                            PropertyInfo piPfx = vt.GetProperty("StaticPrefixLength", BindingFlags.Public | BindingFlags.Instance);
                            if (piPfx != null && piPfx.GetValue(val, null) is int p && p > 0)
                            {
                                cachePrefix = p;
                            }
                        }
                        catch { }
                    }
                }

                // If promptText not yet found from PromptPayload, find largest string not matching meta arguments
                if (promptText == null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] is string str && str != player2Feature && str != requestLabel && str != npcName && str != faction)
                        {
                            if (promptText == null || str.Length > promptText.Length)
                            {
                                promptText = str;
                            }
                        }
                    }
                }

                // If player2Feature not yet identified by parameter name, scan string arguments for recognized feature tags
                if (string.IsNullOrEmpty(player2Feature))
                {
                    for (int i = args.Length - 1; i >= 0; i--)
                    {
                        if (args[i] is string candidate && candidate != promptText && candidate != requestLabel && candidate != npcName && candidate != faction)
                        {
                            if (FastRouter.MapFeature(candidate) != ScopeId.None)
                            {
                                player2Feature = candidate;
                                break;
                            }
                        }
                    }
                }

                // If npcName / faction not yet identified by parameter name, use positional fallback
                if (npcName == null && args.Length >= 2 && args[0] is string first && args[1] is string second)
                {
                    if (first != promptText && first != player2Feature && first != requestLabel)
                    {
                        npcName = first;
                        faction = second;
                    }
                }
            }

            // Fallback for feature name if missing (e.g. direct calls or backend methods)
            if (string.IsNullOrEmpty(player2Feature))
            {
                if (originalMethod != null &&
                    (originalMethod.Name.StartsWith("GetAIResponse", StringComparison.OrdinalIgnoreCase) ||
                     originalMethod.Name.StartsWith("GetPlayer2Response", StringComparison.OrdinalIgnoreCase) ||
                     originalMethod.Name.StartsWith("GetStreamingAIResponse", StringComparison.OrdinalIgnoreCase) ||
                     originalMethod.Name.StartsWith("GetOpenRouterResponse", StringComparison.OrdinalIgnoreCase) ||
                     originalMethod.Name.StartsWith("GetDeepSeekResponse", StringComparison.OrdinalIgnoreCase) ||
                     originalMethod.Name.StartsWith("GetOllamaResponse", StringComparison.OrdinalIgnoreCase) ||
                     originalMethod.Name.StartsWith("GetKoboldCppResponse", StringComparison.OrdinalIgnoreCase)))
                {
                    player2Feature = "dialogue";
                }
                else if (!string.IsNullOrEmpty(npcName))
                {
                    player2Feature = "dialogue";
                }
            }

            if (string.IsNullOrEmpty(systemContent) || string.IsNullOrEmpty(userContent))
            {
                TryDeriveTextSplit(promptText, cachePrefix, out systemContent, out userContent);
            }
        }

        public static bool TryDeriveTextSplit(
            string prompt, int cachePrefix, out string systemContent, out string userContent)
        {
            systemContent = null;
            userContent = null;
            if (string.IsNullOrEmpty(prompt)) return false;

            string[] markers = new string[]
            {
                "### ТЕКУЩАЯ ИСТОРИЯ ДИАЛОГА",
                "### Conversation history ###",
                "### Conversation History ###",
                "### Group conversation ###",
                "### Dialogue history ###",
                "### CURRENT CONVERSATION HISTORY",
                "### Conversation history",
                "### Dialogue history",
                "### ИСТОРИЯ ДИАЛОГА"
            };

            foreach (string marker in markers)
            {
                int idx = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    int sep = prompt.LastIndexOf("============================================================", idx, StringComparison.Ordinal);
                    int split = (sep > 0 && (idx - sep) < 200) ? sep : idx;

                    systemContent = prompt.Substring(0, split).TrimEnd();
                    userContent = prompt.Substring(split).TrimStart();
                    return true;
                }
            }

            if (cachePrefix > 0 && cachePrefix < prompt.Length)
            {
                systemContent = prompt.Substring(0, cachePrefix).TrimEnd();
                userContent = prompt.Substring(cachePrefix).TrimStart();
                return true;
            }

            return false;
        }

        public static bool TryExtract(
            object[] args, out string prompt, out int cachePrefix, out string systemContent, out string userContent)
        {
            prompt = null;
            cachePrefix = 0;
            systemContent = null;
            userContent = null;

            if (args == null || args.Length == 0)
            {
                return false;
            }

            string best = null;
            object bestArg = null;
            foreach (object arg in args)
            {
                string candidate = AsPrompt(arg);
                if (candidate != null && (best == null || candidate.Length > best.Length))
                {
                    best = candidate;
                    bestArg = arg;
                }
            }

            prompt = best;
            if (prompt == null)
            {
                return false;
            }

            for (int i = args.Length - 1; i >= 0; i--)
            {
                if (args[i] is int value)
                {
                    cachePrefix = value;
                    break;
                }
            }

            if (bestArg is PromptPayload pp)
            {
                systemContent = pp.SystemContent;
                userContent = pp.UserContent;
                if (pp.StaticPrefixLength > 0) cachePrefix = pp.StaticPrefixLength;
            }

            if (string.IsNullOrEmpty(systemContent) || string.IsNullOrEmpty(userContent))
            {
                TryDeriveTextSplit(prompt, cachePrefix, out systemContent, out userContent);
            }

            return true;
        }

        public static bool TryExtract(object[] args, out string prompt, out int cachePrefix)
        {
            return TryExtract(args, out prompt, out cachePrefix, out _, out _);
        }

        private static string AsPrompt(object arg)
        {
            if (arg == null) return null;
            if (arg is string text) return text.Length > 0 ? text : null;
            if (arg is PromptPayload pp) return pp.FullText;
            return null;
        }

        /// <summary>
        /// Formats the raw LLM output into a valid Bannerlord AIInfluence dialogue JSON schema:
        /// {"response": "<spoken dialogue>", "emotion": "neutral", "actions": []}
        /// Guarantees that AIInfluence's JObject.Parse succeeds and extracts dialogue cleanly without fallback stubs.
        /// Extracts pure speech text using DialogueStubReplacer.CleanDialogueText to completely strip markdown fences
        /// (```json ... ```), raw schemas, or technical artifacts.
        /// </summary>
        public static string FormatAiInfluenceDialogueResponse(string rawReply)
        {
            if (string.IsNullOrWhiteSpace(rawReply))
            {
                return "{\"response\": \"Мне сейчас нечего сказать.\", \"emotion\": \"neutral\", \"actions\": []}";
            }

            // Universal repair: strip markdown code fences (```json ... ```), trailing commas, and extract JSON
            string repaired = Post.JsonHealer.RepairContent(rawReply);
            if (string.IsNullOrWhiteSpace(repaired))
            {
                return "{\"response\": \"Мне сейчас нечего сказать.\", \"emotion\": \"neutral\", \"actions\": []}";
            }

            // If repaired content is a JSON object, unpack OpenAI wrapper or return normalized JSON directly
            if (repaired.StartsWith("{") && repaired.EndsWith("}"))
            {
                try
                {
                    JObject obj = JObject.Parse(repaired);

                    // 1. If OpenAI Chat Completions format (choices[0].message.content)
                    if (obj["choices"] is JArray arr && arr.Count > 0)
                    {
                        string inner = (string)arr[0]?["message"]?["content"] ?? (string)arr[0]?["text"];
                        if (!string.IsNullOrEmpty(inner))
                        {
                            return FormatAiInfluenceDialogueResponse(inner);
                        }
                    }

                    // 2. If it contains non-empty "response", ensure emotion and actions are initialized and return directly
                    string respCandidate = obj["response"] != null ? ((string)obj["response"])?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(respCandidate))
                    {
                        obj["response"] = respCandidate;
                        if (obj["emotion"] == null) obj["emotion"] = "neutral";
                        if (obj["actions"] == null) obj["actions"] = new JArray();
                        return obj.ToString(Formatting.None);
                    }

                    // 3. If response is missing or empty/whitespace, synthesize speech so AIInfluence never sees Response == null:
                    //    If it contains a name property, synthesize a natural greeting speech:
                    string nameCandidate = obj["name"] != null ? ((string)obj["name"])?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(nameCandidate))
                    {
                        obj["response"] = $"- Приветствую. Меня зовут {nameCandidate}. Чего тебе надобно?";
                    }
                    else
                    {
                        obj["response"] = "Мне сейчас нечего сказать.";
                    }

                    if (obj["emotion"] == null) obj["emotion"] = "neutral";
                    if (obj["actions"] == null) obj["actions"] = new JArray();
                    return obj.ToString(Formatting.None);
                }
                catch
                {
                    // If JSON parse failed despite braces, fall through to text wrapping
                }
            }

            // 4. Plain text dialogue: strip surrounding quotes if present and wrap in valid AIInfluence JSON
            string speech = repaired.Trim();
            if (speech.Length >= 2 && speech.StartsWith("\"") && speech.EndsWith("\""))
            {
                speech = speech.Substring(1, speech.Length - 2).Trim();
            }

            if (string.IsNullOrWhiteSpace(speech))
            {
                speech = "Мне сейчас нечего сказать.";
            }

            var resObj = new JObject
            {
                ["response"] = speech,
                ["emotion"] = "neutral",
                ["actions"] = new JArray()
            };
            return resObj.ToString(Formatting.None);
        }

        /// <summary>
        /// Cleans raw LLM dialogue text or JSON output down to pure speech text,
        /// stripping markdown code fences (```json ... ```), raw JSON keys, or technical artifacts.
        /// Guaranteed to never return raw JSON syntax.
        /// </summary>
        public static string CleanDialogueText(string raw)
        {
            return DialogueStubReplacer.CleanDialogueText(raw);
        }
    }
}
