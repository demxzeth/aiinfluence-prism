using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Finds the target methods of the target assembly for interception.
    ///
    /// Two groups:
    ///  1) primary entry points by exact name: GetAIResponse / GetRawTextResponse /
    ///     GetRawTextResponseWithBackend — parameter shapes are validated by rules;
    ///  2) direct backend calls (Get&lt;Name&gt;Response) — caught by shape, names do not matter.
    ///
    /// While the target type is not in the domain, returns an empty list —
    /// the hook engine will keep retrying every tick.
    /// </summary>
    public static class TargetScanner
    {
        /// <summary>Full name of the AI-request entry point of the target assembly.</summary>
        public const string EntryTypeFullName = "AIInfluence.API.AIClient";

        private static Type _entryType;

        /// <summary>The AIInfluence.API.AIClient type (or null if the target type is not loaded yet).</summary>
        public static Type EntryType
        {
            get { return _entryType ?? (_entryType = FindEntryType()); }
        }

        /// <summary>The list of targets with roles; duplicates are filtered out.</summary>
        public static IReadOnlyList<HookTarget> CaptureTargets()
        {
            Type entry = EntryType;
            if (entry == null)
            {
                LogSink.Info("TargetScanner: " + EntryTypeFullName + " not loaded yet.");
                return new HookTarget[0];
            }

            const BindingFlags scan =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            List<HookTarget> targets = new List<HookTarget>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (MethodInfo method in entry.GetMethods(scan))
            {
                // The prefix returns Task<string> via __result; a method with
                // another return type cannot be collected by Harmony — skipped early.
                if (method.ReturnType != typeof(Task<string>))
                {
                    continue;
                }

                TargetRole role = SignatureRules.Classify(method);
                if (role == TargetRole.None)
                {
                    continue;
                }

                string key = method.Module.ModuleVersionId + ":" + method.MetadataToken;
                if (!seen.Add(key))
                {
                    continue;
                }

                targets.Add(new HookTarget(method, role));
            }

            LogSink.Info("TargetScanner: " + targets.Count + " target(s) on " + entry.FullName
                + " (dialogue=" + Count(targets, TargetRole.Dialogue)
                + " raw=" + Count(targets, TargetRole.Raw)
                + " withBackend=" + Count(targets, TargetRole.RawWithBackend)
                + " backendDirect=" + Count(targets, TargetRole.BackendDirect) + ").");

            return targets;
        }

        public static void Reset()
        {
            _entryType = null;
        }

        private static int Count(IReadOnlyList<HookTarget> targets, TargetRole role)
        {
            int n = 0;
            foreach (HookTarget target in targets)
            {
                if (target.Role == role)
                {
                    n++;
                }
            }

            return n;
        }

        private static Type FindEntryType()
        {
            Type type = Type.GetType(EntryTypeFullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(EntryTypeFullName, throwOnError: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // assemblies still loading — skip silently.
                }
            }

            return null;
        }
    }
}
