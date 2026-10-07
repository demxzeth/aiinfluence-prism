using System;
using System.IO;
using System.Reflection;

namespace AIInfluencePrism.Infra
{
    /// <summary>
    /// Resolves the mod folder root from the assembly's own location.
    /// The assembly sits in .../AIInfluencePrism/bin/Win64_Shipping_Client/,
    /// and the root is .../AIInfluencePrism/.
    /// </summary>
    public static class ModuleLocator
    {
        private static string _root;

        /// <summary>The mod root; empty when it could not be resolved.</summary>
        public static string Root
        {
            get { return _root ?? (_root = ResolveRoot()); }
        }

        /// <summary>Override the root (for unit tests).</summary>
        public static void OverrideRoot(string root)
        {
            _root = root ?? string.Empty;
        }

        /// <summary>Reset the root cache (for unit tests).</summary>
        public static void Reset()
        {
            _root = null;
        }

        private static string ResolveRoot()
        {
            try
            {
                string assemblyPath = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
                string binFolder = Path.GetDirectoryName(assemblyPath);
                string shipping = Path.GetDirectoryName(binFolder);
                return Directory.GetParent(shipping)?.FullName ?? string.Empty;
            }
            catch
            {
                // In-game the current directory differs from the mod folder, so
                // the fallback is intentionally empty: the caller must check
                // the result and log the problem.
                return string.Empty;
            }
        }
    }
}
