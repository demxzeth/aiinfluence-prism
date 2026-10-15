using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        private struct CallSiteKey : IEquatable<CallSiteKey>
        {
            public readonly MethodBase M0;
            public readonly MethodBase M1;
            public readonly MethodBase M2;

            public CallSiteKey(MethodBase m0, MethodBase m1, MethodBase m2)
            {
                M0 = m0;
                M1 = m1;
                M2 = m2;
            }

            public bool Equals(CallSiteKey other) =>
                M0 == other.M0 && M1 == other.M1 && M2 == other.M2;

            public override bool Equals(object obj) =>
                obj is CallSiteKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (M0?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (M1?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (M2?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }

        private static readonly ConcurrentDictionary<CallSiteKey, ScopeId> CallSiteCache =
            new ConcurrentDictionary<CallSiteKey, ScopeId>();

        /// <summary>Clears call-site cache.</summary>
        public static void ResetCache()
        {
            CallSiteCache.Clear();
        }

        /// <summary>The live stack. Never called in unit tests.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ScopeId Probe()
        {
            try
            {
                int firstExternal = 1;
                MethodBase m0 = null;
                MethodBase m1 = null;
                MethodBase m2 = null;

                // Быстро находим первый внешний фрейм за пределами AIInfluencePrism без полного создания StackTrace
                for (int i = 1; i <= 8; i++)
                {
                    StackFrame sf = new StackFrame(i, false);
                    MethodBase mb = sf.GetMethod();
                    if (mb == null) break;

                    Type dt = mb.DeclaringType;
                    if (dt == null) continue;

                    string ns = dt.Namespace;
                    if (ns == null || !ns.StartsWith("AIInfluencePrism", StringComparison.Ordinal))
                    {
                        firstExternal = i;
                        m0 = mb;
                        m1 = new StackFrame(i + 1, false).GetMethod();
                        m2 = new StackFrame(i + 2, false).GetMethod();
                        break;
                    }
                }

                if (m0 != null)
                {
                    CallSiteKey key = new CallSiteKey(m0, m1, m2);
                    if (CallSiteCache.TryGetValue(key, out ScopeId cached))
                    {
                        return cached;
                    }

                    // При промахе кэша захватываем только необходимые фреймы без сбора PDB/файловой информации
                    StackFrame[] frames = new StackTrace(firstExternal, false).GetFrames();
                    if (frames == null || frames.Length == 0)
                    {
                        CallSiteCache[key] = ScopeId.None;
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

                    ScopeId result = ProbeFrames(names);
                    CallSiteCache[key] = result;
                    return result;
                }
            }
            catch
            {
                return ScopeId.None;
            }

            return ScopeId.None;
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
