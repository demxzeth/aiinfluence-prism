using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AIInfluencePrism.Routing
{
    /// <summary>Simplified stack frame name (for the testable probe).</summary>
    public struct FrameName
    {
        public FrameName(string ns, string fullName)
        {
            Namespace = ns ?? string.Empty;
            FullName = fullName ?? string.Empty;
        }

        public string Namespace { get; }
        public string FullName { get; }
    }

    /// <summary>
    /// Call-stack probe: we look at whose code called us and map the
    /// frame's namespace to a scope. For diplomacy, events and the memory
    /// book this is the most reliable channel — their prompts have no
    /// unique visible markers. The logic is split in two: frame collection
    /// (game plumbing) and a pure matching function (covered by unit tests).
    /// </summary>
    public static class CallSiteProbe
    {
        /// <summary>The live stack. Never called in unit tests.</summary>
        public static ScopeId Probe()
        {
            StackFrame[] frames;
            try
            {
                frames = new StackTrace().GetFrames();
            }
            catch
            {
                return ScopeId.None;
            }

            if (frames == null)
            {
                return ScopeId.None;
            }

            List<FrameName> names = new List<FrameName>(frames.Length);
            foreach (StackFrame frame in frames)
            {
                Type declaring = frame.GetMethod()?.DeclaringType;
                if (declaring == null)
                {
                    continue;
                }

                names.Add(new FrameName(declaring.Namespace, declaring.FullName));
            }

            return ProbeFrames(names);
        }

        /// <summary>Pure function: frames -&gt; scope. The first matching frame decides.</summary>
        public static ScopeId ProbeFrames(IEnumerable<FrameName> frames)
        {
            if (frames == null)
            {
                return ScopeId.None;
            }

            foreach (FrameName frame in frames)
            {
                foreach (ScopeDescriptor descriptor in ScopeCatalog.All)
                {
                    if (Matches(descriptor, frame))
                    {
                        return descriptor.Id;
                    }
                }
            }

            return ScopeId.None;
        }

        private static bool Matches(ScopeDescriptor descriptor, FrameName frame)
        {
            foreach (string prefix in descriptor.StackPrefixes)
            {
                if (frame.Namespace.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (string hint in descriptor.TypeHints)
            {
                if (frame.FullName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
