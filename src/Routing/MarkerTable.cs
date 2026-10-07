using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Marker table: which strings in the prompt text identify a scope.
    /// Markers live in new-prompts-detection/*.txt next to the mod and are
    /// edited by hand if the target assembly changes its prompt wording.
    ///
    /// The cache is keyed by the SHA-256 hash of the file, so edits are
    /// picked up right after saving, without restarting the game.
    ///
    /// The scan is two-pass: first the prompt "tail" (utility headers live
    /// at the end), then the whole text.
    /// </summary>
    public static class MarkerTable
    {
        /// <summary>Marker folder name, relative to the mod root.</summary>
        public const string FolderName = "new-prompts-detection";

        /// <summary>Tail window size, in characters.</summary>
        public const int TailWindow = 4096;

        private sealed class CachedFile
        {
            public byte[] Hash;
            public string[] Markers;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, CachedFile> Cache =
            new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tries to determine the scope by markers. matchedMarker returns
        /// the exact string the match fired on (for logs).
        /// </summary>
        public static bool TryMatch(string prompt, out ScopeId scope, out string matchedMarker)
        {
            scope = ScopeId.None;
            matchedMarker = string.Empty;

            if (string.IsNullOrEmpty(prompt))
            {
                return false;
            }

            string tail = prompt.Length > TailWindow
                ? prompt.Substring(prompt.Length - TailWindow)
                : prompt;

            foreach (ScopeDescriptor descriptor in ScopeCatalog.MarkerScopes())
            {
                string path = PathFor(descriptor);
                string[] markers = Load(path);
                if (markers.Length == 0)
                {
                    continue;
                }

                if (Scan(tail, markers, out matchedMarker))
                {
                    scope = descriptor.Id;
                    return true;
                }

                // The tail missed — check the whole text (the marker may be
                // in the middle, e.g. in an instruction block).
                if (tail.Length != prompt.Length && Scan(prompt, markers, out matchedMarker))
                {
                    scope = descriptor.Id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Preload all marker files. Returns the total line count.</summary>
        public static int Warmup()
        {
            int total = 0;
            foreach (ScopeDescriptor descriptor in ScopeCatalog.MarkerScopes())
            {
                total += Load(PathFor(descriptor)).Length;
            }

            return total;
        }

        /// <summary>Reset the cache (unit tests and mod root change).</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Cache.Clear();
            }
        }

        /// <summary>Parse the file content: a line = OR-condition, "// " — a comment.</summary>
        public static string[] ParseLines(IEnumerable<string> lines)
        {
            List<string> result = new List<string>();
            foreach (string raw in lines ?? new string[0])
            {
                string line = (raw ?? string.Empty).Trim();
                if (line.Length != 0 && !line.StartsWith("//", StringComparison.Ordinal))
                {
                    result.Add(line);
                }
            }

            return result.ToArray();
        }

        private static bool Scan(string text, string[] markers, out string matched)
        {
            matched = null;
            foreach (string marker in markers)
            {
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched = marker;
                    return true;
                }
            }

            return false;
        }

        private static string PathFor(ScopeDescriptor descriptor)
        {
            return Path.Combine(ModuleLocator.Root, FolderName, descriptor.MarkerFile);
        }

        private static string[] Load(string path)
        {
            byte[] hash = null;
            try
            {
                if (File.Exists(path))
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        using (SHA256 sha = SHA256.Create())
                        {
                            hash = sha.ComputeHash(stream);
                        }
                    }
                }
            }
            catch
            {
                // the file is locked/unavailable right now — try reading as is.
                hash = null;
            }

            if (hash != null)
            {
                lock (Gate)
                {
                    if (Cache.TryGetValue(path, out CachedFile cached) && cached.Hash != null
                        && StructuralEquals(cached.Hash, hash))
                    {
                        return cached.Markers;
                    }
                }
            }

            string[] parsed;
            try
            {
                parsed = File.Exists(path) ? ParseLines(File.ReadAllLines(path)) : new string[0];
            }
            catch
            {
                parsed = new string[0];
            }

            if (hash != null)
            {
                lock (Gate)
                {
                    Cache[path] = new CachedFile { Hash = hash, Markers = parsed };
                }
            }

            return parsed;
        }

        private static bool StructuralEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
