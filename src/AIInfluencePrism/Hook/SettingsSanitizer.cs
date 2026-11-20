using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MCM.Abstractions;
using AIInfluencePrism.Logging;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Triple-layer safeguard to ensure AIInfluence never aborts requests locally
    /// due to empty API keys (e.g. OpenRouter or DeepSeek selected in MCM with empty keys).
    /// With non-empty keys, AIInfluence proceeds to HttpClient.SendAsync,
    /// where PrismHttpMessageHandler intercepts and routes the request directly to the chosen LLM.
    /// </summary>
    public static class SettingsSanitizer
    {
        private const string DummyKey = "prism-bridge-key";
        private static bool _diskConfigChecked;
        private static bool _inMemoryKeysSet;
        private static DateTime _lastSanitizeTime = DateTime.MinValue;
        private static readonly TimeSpan SanitizeInterval = TimeSpan.FromSeconds(10);

        public static void EnsureAiInfluenceKeys(bool force = false)
        {
            if (!force && (DateTime.UtcNow - _lastSanitizeTime) < SanitizeInterval)
            {
                return;
            }
            _lastSanitizeTime = DateTime.UtcNow;

            // 1. Update on disk (once per game session or when needed)
            if (!_diskConfigChecked)
            {
                SanitizeDiskConfig();
            }

            // 2. Update via MCM BaseSettingsProvider
            SanitizeViaMcmProvider();

            // 3. Update via AppDomain reflection on AIInfluence ModSettings.Instance
            SanitizeViaReflection();
        }

        public static void Reset()
        {
            _diskConfigChecked = false;
            _inMemoryKeysSet = false;
            _lastSanitizeTime = DateTime.MinValue;
        }

        public static void SanitizeDiskConfig()
        {
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string configPath = Path.Combine(docs, @"Mount and Blade II Bannerlord\Configs\ModSettings\Global\AIInfluence\AIInfluenceSettings.json");

                if (File.Exists(configPath))
                {
                    string content = File.ReadAllText(configPath);
                    JObject json = JObject.Parse(content);
                    bool modified = false;

                    if (json["ApiKey"] != null && string.IsNullOrEmpty((string)json["ApiKey"]))
                    {
                        json["ApiKey"] = DummyKey;
                        modified = true;
                    }

                    if (json["DeepSeekApiKey"] != null && string.IsNullOrEmpty((string)json["DeepSeekApiKey"]))
                    {
                        json["DeepSeekApiKey"] = DummyKey;
                        modified = true;
                    }

                    if (json["AIBackend"] == null || (int)json["AIBackend"] != 0)
                    {
                        json["AIBackend"] = 0;
                        modified = true;
                    }

                    if (json["DiplomacyAIBackend"] == null || (int)json["DiplomacyAIBackend"] != 0)
                    {
                        json["DiplomacyAIBackend"] = 0;
                        modified = true;
                    }

                    if (json["DynamicEventsAIBackend"] == null || (int)json["DynamicEventsAIBackend"] != 0)
                    {
                        json["DynamicEventsAIBackend"] = 0;
                        modified = true;
                    }

                    if (json["EnableStreamingResponses"] == null || (bool)json["EnableStreamingResponses"])
                    {
                        json["EnableStreamingResponses"] = false;
                        modified = true;
                    }

                    if (modified)
                    {
                        File.WriteAllText(configPath, json.ToString(Formatting.Indented));
                        LogSink.Always("[SettingsSanitizer] Sanitized AIInfluenceSettings.json on disk (AIBackend=0, EnableStreamingResponses=false, dummy keys).");
                    }

                    _diskConfigChecked = true;
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[SettingsSanitizer] SanitizeDiskConfig note: " + ex.Message);
            }
        }

        public static void SanitizeViaMcmProvider()
        {
            try
            {
                BaseSettingsProvider provider = BaseSettingsProvider.Instance;
                if (provider != null)
                {
                    var settings = provider.GetSettings("AIInfluence");
                    if (settings != null)
                    {
                        bool applied = ApplyDummyKeys(settings);
                        if (applied)
                        {
                            provider.SaveSettings(settings);
                            LogSink.Always("[SettingsSanitizer] Injected dummy keys into AIInfluence MCM settings instance and saved.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[SettingsSanitizer] SanitizeViaMcmProvider note: " + ex.Message);
            }
        }

        public static void SanitizeViaReflection()
        {
            try
            {
                Assembly asm = TargetScanner.TargetAssembly ?? TargetScanner.FindTargetAssembly();
                if (asm == null)
                {
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

                if (asm == null) return;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null).ToArray(); }
                catch { return; }

                foreach (Type t in types)
                {
                    if (t != null && t.Name == "ModSettings")
                    {
                        PropertyInfo instProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        object inst = instProp?.GetValue(null);
                        if (inst != null)
                        {
                            ApplyDummyKeys(inst);
                            _inMemoryKeysSet = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[SettingsSanitizer] SanitizeViaReflection note: " + ex.Message);
            }
        }

        private static bool ApplyDummyKeys(object target)
        {
            if (target == null) return false;
            bool changed = false;
            Type type = target.GetType();

            try
            {
                PropertyInfo apiKeyProp = type.GetProperty("ApiKey", BindingFlags.Public | BindingFlags.Instance);
                if (apiKeyProp != null && apiKeyProp.CanWrite)
                {
                    string cur = (string)apiKeyProp.GetValue(target);
                    if (string.IsNullOrEmpty(cur))
                    {
                        apiKeyProp.SetValue(target, DummyKey);
                        changed = true;
                    }
                }

                PropertyInfo dsKeyProp = type.GetProperty("DeepSeekApiKey", BindingFlags.Public | BindingFlags.Instance);
                if (dsKeyProp != null && dsKeyProp.CanWrite)
                {
                    string cur = (string)dsKeyProp.GetValue(target);
                    if (string.IsNullOrEmpty(cur))
                    {
                        dsKeyProp.SetValue(target, DummyKey);
                        changed = true;
                    }
                }

                PropertyInfo streamProp = type.GetProperty("EnableStreamingResponses", BindingFlags.Public | BindingFlags.Instance);
                if (streamProp != null && streamProp.CanWrite)
                {
                    bool cur = (bool)streamProp.GetValue(target);
                    if (cur)
                    {
                        streamProp.SetValue(target, false);
                        changed = true;
                    }
                }

                string[] backendPropNames = new string[] { "AIBackend", "DiplomacyAIBackend", "DynamicEventsAIBackend" };
                foreach (string bpName in backendPropNames)
                {
                    PropertyInfo bProp = type.GetProperty(bpName, BindingFlags.Public | BindingFlags.Instance);
                    if (bProp == null) continue;

                    object bVal = bProp.GetValue(target);
                    if (bVal == null) continue;

                    Type valType = bVal.GetType();
                    if (valType.Name.IndexOf("Dropdown", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        PropertyInfo selIdxProp = valType.GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
                        if (selIdxProp != null && selIdxProp.CanWrite)
                        {
                            int curIdx = Convert.ToInt32(selIdxProp.GetValue(bVal));
                            if (curIdx != 0)
                            {
                                selIdxProp.SetValue(bVal, 0);
                                changed = true;
                            }
                        }
                    }
                    else if (bProp.CanWrite)
                    {
                        int cur = Convert.ToInt32(bVal);
                        if (cur != 0)
                        {
                            object val = bProp.PropertyType.IsEnum ? Enum.ToObject(bProp.PropertyType, 0) : (object)0;
                            bProp.SetValue(target, val);
                            changed = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[SettingsSanitizer] ApplyDummyKeys error: " + ex.Message);
            }

            return changed;
        }
    }
}
