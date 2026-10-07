using System;
using System.Reflection;
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

                if (!TryExtract(__args, out string prompt, out int cachePrefix))
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

                __result = Pipeline.SendAsync(role, scope, prompt, cachePrefix, __originalMethod);
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
            prompt = null;
            cachePrefix = 0;

            if (args == null || args.Length == 0)
            {
                return false;
            }

            // Prompt = the longest string argument: at the API entry points it
            // is stably the most voluminous parameter, while utility strings
            // (npc, faction, backend name, tags) are short. Scanning by position
            // is unreliable: parameter shapes changed between API versions.
            string best = null;
            foreach (object arg in args)
            {
                string candidate = AsPrompt(arg);
                if (candidate != null && (best == null || candidate.Length > best.Length))
                {
                    best = candidate;
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

            return true;
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
    }
}
