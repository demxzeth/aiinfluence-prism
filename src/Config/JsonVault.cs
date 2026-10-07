using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Additional JSON settings outside the profile system:
    /// per-scope overrides, feature flags, UI state.
    /// Stored in Config/prism_vault.json in the mod root.
    /// Thread-safe (lock on read/write).
    /// </summary>
    public static class JsonVault
    {
        private static readonly object Gate = new object();
        private static JObject _cache;
        private static string _path;

        private static string ResolvePath()
        {
            string root = ModuleLocator.Root;
            return root.Length == 0
                ? null
                : System.IO.Path.Combine(
                    System.IO.Path.Combine(root, PrismInfo.ConfigFolderName), "prism_vault.json");
        }

        private static JObject Load()
        {
            string path = ResolvePath();
            if (path == null)
            {
                return new JObject();
            }

            _path = path;
            try
            {
                if (System.IO.File.Exists(path))
                {
                    string text = System.IO.File.ReadAllText(path);
                    JObject obj = JObject.Parse(text);
                    if (obj != null)
                    {
                        return obj;
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("vault: failed to read " + path + " — " + ex.Message);
            }

            return new JObject();
        }

        private static void Flush()
        {
            string path = _path;
            if (path == null)
            {
                return;
            }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                System.IO.File.WriteAllText(path, _cache.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                LogSink.Warn("vault: failed to write " + path + " — " + ex.Message);
            }
        }

        /// <summary>Read a string value; returns fallback on missing or wrong type.</summary>
        public static string GetString(string key, string fallback = "")
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                JToken token = _cache[key];
                return token != null && token.Type == JTokenType.String
                    ? token.Value<string>()
                    : fallback;
            }
        }

        /// <summary>Read an integer value; returns fallback on missing or wrong type.</summary>
        public static int GetInt(string key, int fallback = 0)
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                JToken token = _cache[key];
                if (token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float))
                {
                    try { return token.Value<int>(); }
                    catch { return fallback; }
                }

                return fallback;
            }
        }

        /// <summary>Read a boolean value; returns fallback on missing or wrong type.</summary>
        public static bool GetBool(string key, bool fallback = false)
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                JToken token = _cache[key];
                return token != null && token.Type == JTokenType.Boolean
                    ? token.Value<bool>()
                    : fallback;
            }
        }

        /// <summary>Write a string value and persist immediately.</summary>
        public static void SetString(string key, string value)
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                _cache[key] = value ?? string.Empty;
                Flush();
            }
        }

        /// <summary>Write an integer value and persist immediately.</summary>
        public static void SetInt(string key, int value)
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                _cache[key] = value;
                Flush();
            }
        }

        /// <summary>Write a boolean value and persist immediately.</summary>
        public static void SetBool(string key, bool value)
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                _cache[key] = value;
                Flush();
            }
        }

        /// <summary>Remove a key and persist immediately.</summary>
        public static void Remove(string key)
        {
            lock (Gate)
            {
                if (_cache == null)
                {
                    _cache = Load();
                }

                if (_cache.Remove(key))
                {
                    Flush();
                }
            }
        }

        /// <summary>Reset the in-memory cache (next read re-reads the file).</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                _cache = null;
                _path = null;
            }
        }
    }
}