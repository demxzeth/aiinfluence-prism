using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// The single prefix interceptor: Harmony passes __args and
    /// __originalMethod of every patched method here. The job — extract
    /// the prompt, classify the call and decide: intercept (return
    /// Task&lt;string&gt;) or pass the original through.
    ///
    /// At E3 the send pipeline was not wired yet, so the decision was
    /// always "pass through" (return true), but with full parsing and
    /// logging. E4 replaced that with the real hand-off.
    /// </summary>
    public static class Dispatch
    {
        /// <summary>
        /// The decision point. All parameters are Harmony-injected ("__name"
        /// forms): __args — all arguments of the original, __originalMethod —
        /// the method itself. Harmony binds ordinary parameter names by name
        /// against the original's parameters, and ours differ — so the shared
        /// prefix must rely only on the "__" forms.
        /// </summary>
        /// <param name="scope">The scope the call was routed to.</param>
        public static bool Prefix(object[] __args, MethodBase __originalMethod, ref Task<string> __result)
        {
            try
            {
                TargetRole role = SignatureRules.Classify(__originalMethod);
                if (role == TargetRole.None)
                {
                    LogSink.Warn("dispatch: unknown role for " + __originalMethod.Name + "; passing through.");
                    return true;
                }

                // Master switch: when off, Prism does not intercept anything —
                // requests go to AIInfluence's own backend.
                if (!ProfileBook.Current.MasterEnabled)
                {
                    LogSink.Info("dispatch: master switch off; passing through.");
                    return true;
                }

                if (!TryExtract(__args, out string prompt, out int cachePrefix, out string systemPart, out string userPart))
                {
                    LogSink.Warn("dispatch[" + role + "]: no prompt in args ("
                        + (__args == null ? "null" : __args.Length.ToString()) + " params); passing through.");
                    return true;
                }

                ScopeId scope = RouteScope(role, prompt);
                LogSink.Info("dispatch[" + role + "]: scope=" + scope
                    + ", prompt=" + prompt.Length + " chars, cachePrefix=" + cachePrefix);

                // Per-scope switch: when this scope is off, pass through to AIInfluence.
                if (scope != ScopeId.None && !ProfileBook.Current.Scope(scope).Enabled)
                {
                    LogSink.Info("dispatch[" + role + "]: scope " + scope + " disabled; passing through.");
                    return true;
                }

                // Intercepted: send through our pipeline instead of the original.
                MethodInfo callable = __originalMethod as MethodInfo;
                if (callable == null || callable.ReturnType != typeof(Task<string>))
                {
                    // Safety net: the prefix returns Task<string>, and the target
                    // method must produce one. Anything else is not our target.
                    LogSink.Warn("dispatch[" + role + "]: unexpected return type on "
                        + __originalMethod.Name + "; passing through.");
                    return true;
                }

                // Dialogue Sliding Window optimization:
                // When enabled, limits active dialogue history to the last N messages
                // to dramatically improve response latency (~15-20s instead of 40-90s).
                if (scope == ScopeId.Dialogue)
                {
                    ScopeSettings dialogueSettings = ProfileBook.Current.Scope(ScopeId.Dialogue);
                    if (dialogueSettings.DialogueSlidingWindowEnabled && !string.IsNullOrEmpty(userPart))
                    {
                        int beforeLen = userPart.Length;
                        userPart = ApplySlidingWindow(userPart, dialogueSettings.DialogueSlidingWindowSize);
                        if (userPart.Length < beforeLen)
                        {
                            LogSink.Info("dispatch[Dialogue]: sliding window applied (retained last "
                                + dialogueSettings.DialogueSlidingWindowSize + " messages, reduced history from "
                                + beforeLen + " to " + userPart.Length + " chars)");
                        }
                    }
                }

                // If this is a dialogue LLM request and dialogue image generation is enabled,
                // initiate simultaneous image generation concurrently with the text generation!
                if (scope == ScopeId.Dialogue && ProfileBook.Current.Scope(ScopeId.Dialogue).GenerateImages)
                {
                    DialogueSceneCoordinator.StartConcurrentDialogueImage(prompt, userPart);
                }

                __result = Pipeline.SendAsync(role, scope, prompt, cachePrefix, __originalMethod, systemPart, userPart);
                return false;
            }
            catch (Exception ex)
            {
                // Any failure of ours must not break the game: pass the original through.
                LogSink.Error("dispatch failed; passing through.", ex);
                return true;
            }
        }

        /// <summary>Extracts the prompt and cachePrefix from the argument array.</summary>
        public static bool TryExtract(object[] args, out string prompt, out int cachePrefix)
        {
            return TryExtract(args, out prompt, out cachePrefix, out _, out _);
        }

        /// <summary>Extracts the prompt, cachePrefix, and two-stage split parts from the argument array.</summary>
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

            // Prompt = the longest string argument: at the API entry points it
            // is stably the most voluminous parameter, while utility strings
            // (npc, faction, backend name, tags) are short. Scanning by position
            // is unreliable: parameter shapes changed between API versions.
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

            // cachePrefix: the last int argument (the shape of dialogue and raw calls).
            for (int i = args.Length - 1; i >= 0; i--)
            {
                if (args[i] is int value)
                {
                    cachePrefix = value;
                    break;
                }
            }

            // 1. High priority: Semantic dialogue markers in the prompt text.
            // When dialogue markers are present, they define the exact physical boundary
            // between static world/rules/lore context (Stage 1) and dynamic conversation history (Stage 2).
            if (TryDeriveTextSplit(prompt, 0, out systemContent, out userContent))
            {
                return true;
            }

            if (bestArg != null && !(bestArg is string))
            {
                ExtractPayloadSplit(bestArg, ref cachePrefix, out systemContent, out userContent);
            }

            if (string.IsNullOrEmpty(systemContent) || string.IsNullOrEmpty(userContent))
            {
                TryDeriveTextSplit(prompt, cachePrefix, out systemContent, out userContent);
            }

            return true;
        }

        private static void ExtractPayloadSplit(
            object payload, ref int cachePrefix, out string systemContent, out string userContent)
        {
            systemContent = null;
            userContent = null;
            if (payload == null) return;

            Type type = payload.GetType();
            PropertyInfo sysProp = type.GetProperty("SystemContent", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo usrProp = type.GetProperty("UserContent", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo pfxProp = type.GetProperty("StaticPrefixLength", BindingFlags.Public | BindingFlags.Instance);

            if (sysProp != null && usrProp != null)
            {
                string s = sysProp.GetValue(payload, null) as string;
                string u = usrProp.GetValue(payload, null) as string;
                if (!string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(u))
                {
                    systemContent = s;
                    userContent = u;
                }
            }

            if (pfxProp != null)
            {
                object val = pfxProp.GetValue(payload, null);
                if (val is int pfx && pfx > 0)
                {
                    cachePrefix = pfx;
                }
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

        /// <summary>
        /// Coerces an argument to prompt text. A string — as is; a payload —
        /// by its structure: its text lives in FullText (or the System/User
        /// pair), not in ToString (that is just the class name).
        /// </summary>
        public static string AsPrompt(object arg)
        {
            if (arg == null)
            {
                return null;
            }

            if (arg is string text)
            {
                return text.Length > 0 ? text : null;
            }

            PropertyInfo prompt = arg.GetType().GetProperty(
                "FullText", BindingFlags.Public | BindingFlags.Instance);
            if (prompt != null)
            {
                string value = prompt.GetValue(arg, null) as string;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }

                // FullText is not set — assemble from the System/User pair.
                PropertyInfo system = arg.GetType().GetProperty(
                    "SystemContent", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo user = arg.GetType().GetProperty(
                    "UserContent", BindingFlags.Public | BindingFlags.Instance);
                string s = system == null ? null : system.GetValue(arg, null) as string;
                string u = user == null ? null : user.GetValue(arg, null) as string;
                if (!string.IsNullOrEmpty(u))
                {
                    return string.IsNullOrEmpty(s) ? u : s + "\n\n" + u;
                }

                return null;
            }

            return null;
        }

        private static ScopeId RouteScope(TargetRole invokedRole, string prompt)
        {
            switch (invokedRole)
            {
                case TargetRole.Dialogue:
                case TargetRole.Raw:
                    return Routing.Router.ClassifyChatPrompt(prompt, out string chatMarker);

                case TargetRole.RawWithBackend:
                case TargetRole.BackendDirect:
                    return Routing.Router.ClassifyPipelinePrompt(prompt, out string pipeMarker);

                default:
                    return Routing.Router.ClassifyChatPrompt(prompt, out string fallbackMarker);
            }
        }

        /// <summary>
        /// Trims dialogue history to the last keepCount messages using a sliding window.
        /// Preserves the header marker (e.g. ### Conversation history ###) and inserts
        /// a brief omission notice before the retained recent messages.
        /// If the total message count is less than or equal to keepCount, the text is returned unmodified.
        /// </summary>
        public static string ApplySlidingWindow(string userContent, int keepCount = 50)
        {
            if (string.IsNullOrEmpty(userContent) || keepCount <= 0)
            {
                return userContent;
            }

            // Find message boundaries.
            // In AIInfluence, messages in dialogue history start on a new line with patterns like:
            // "Name (`id`):", "I (Name, `id`):", "Player:", "You:", "[Turn N]", etc.
            Regex messageHeaderRegex = new Regex(
                @"(?:^|\r?\n)(?:[^\r\n]{1,80}?\s*\([^)\r\n]+?\)\s*:|(?:Player|You|User|Игрок|Собеседник|Hero|MainHero)\s*:|\[(?:Turn\s*)?\d+\])",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            MatchCollection matches = messageHeaderRegex.Matches(userContent);
            if (matches.Count <= keepCount)
            {
                return userContent;
            }

            // Target match: the one that starts the last keepCount messages
            int targetMatchIndex = matches.Count - keepCount;
            int cutoffIndex = matches[targetMatchIndex].Index;

            // Preserve header part (before the first message)
            int firstMsgIndex = matches[0].Index;
            string header = userContent.Substring(0, firstMsgIndex).TrimEnd();

            // Retained recent dialogue part
            string recentPart = userContent.Substring(cutoffIndex).TrimStart('\r', '\n');

            int omittedCount = targetMatchIndex;
            string omissionNotice = "\n\n[Earlier conversation history (" + omittedCount
                + " older messages omitted for faster response)...]\n\n";

            return header + omissionNotice + recentPart;
        }
    }
}
