using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Source of the active connection profile. Reads Config/prism_profiles.json
    /// in the mod root; if the file is missing or broken — the built-in
    /// default (Player2 local). Full MCM and per-scope overrides — E6.
    ///
    /// File schema:
    ///   { "active": "main",
    ///     "profiles": [ { "name": "main", "provider": "openai",
    ///         "rootUrl": "...", "apiKey": "", "model": "...",
    ///         "temperature": 0.7, "maxTokens": 1024, "timeoutSeconds": 180 } ] }
    /// </summary>
    public static class ProfileBook
    {
        private static Profile _current;
        private static readonly object Gate = new object();

        /// <summary>The active profile (loaded lazily on first access).</summary>
        public static Profile Current
        {
            get
            {
                lock (Gate)
                {
                    return _current ?? (_current = Load());
                }
            }
        }

        /// <summary>Reset the cache (the next access re-reads the file).</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                _current = null;
            }
        }

        /// <summary>Warmup on mod start + a readable line in the log.</summary>
        public static Profile Warmup()
        {
            Profile profile = Current;
            string model = profile.Model == null || profile.Model.Length == 0 ? "(auto)" : profile.Model;
            LogSink.Always("profile: " + profile.Name
                + " provider=" + Profile.Label(profile.Provider)
                + " root=" + profile.EffectiveRoot()
                + " model=" + model);
            return profile;
        }

        private static Profile Load()
        {
            string root = ModuleLocator.Root;
            if (root.Length == 0)
            {
                LogSink.Warn("profiles: module root unknown; using built-in default.");
                return Profile.CreateDefault();
            }

            string path = Path.Combine(
                Path.Combine(root, PrismInfo.ConfigFolderName), PrismInfo.ProfilesFileName);

            try
            {
                if (!File.Exists(path))
                {
                    WriteTemplate(path);
                    LogSink.Info("profiles: " + PrismInfo.ProfilesFileName
                        + " not found; template written. Edit Config\\"
                        + PrismInfo.ProfilesFileName + " to choose a provider; using player2-local now.");
                    return Profile.CreateDefault();
                }

                string text = File.ReadAllText(path);
                Profile profile;
                string error;
                if (!TryParseJson(text, out profile, out error))
                {
                    LogSink.Error("profiles: bad " + PrismInfo.ProfilesFileName
                        + ": " + error + "; using built-in default.");
                    return Profile.CreateDefault();
                }

                return profile;
            }
            catch (Exception ex)
            {
                LogSink.Error("profiles: failed to read " + path + "; using built-in default.", ex);
                return Profile.CreateDefault();
            }
        }

        /// <summary>Persist the active profile back into the JSON file.</summary>
        public static void Save()
        {
            Profile profile;
            lock (Gate)
            {
                profile = _current ?? Profile.CreateDefault();
            }

            string root = ModuleLocator.Root;
            if (root.Length == 0)
            {
                return;
            }

            string path = System.IO.Path.Combine(
                System.IO.Path.Combine(root, PrismInfo.ConfigFolderName), PrismInfo.ProfilesFileName);
            if (!System.IO.File.Exists(path))
            {
                WriteTemplate(path);
                return;
            }

            try
            {
                string text = System.IO.File.ReadAllText(path);
                JObject document;
                try
                {
                    document = JObject.Parse(text);
                }
                catch
                {
                    // Corrupt file — overwrite with a clean one.
                    WriteTemplate(path);
                    return;
                }

                JArray profiles = document["profiles"] as JArray;
                bool found = false;

                if (profiles != null)
                {
                    foreach (JObject entry in profiles)
                    {
                        string name = (string)entry["name"];
                        if (string.IsNullOrEmpty(name) ||
                            !string.Equals(name, profile.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        WriteEntry(entry, profile);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Profile name changed or was added externally — add a new entry.
                    if (profiles == null)
                    {
                        profiles = new JArray();
                        document["profiles"] = profiles;
                    }

                    JObject newEntry = new JObject();
                    WriteEntry(newEntry, profile);
                    profiles.Add(newEntry);
                }

                string folder = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                System.IO.File.WriteAllText(path, document.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (System.Exception ex)
            {
                LogSink.Error("profiles: failed to save " + path + ".", ex);
            }
        }

        /// <summary>Config template for the first run (no keys).</summary>
        private static void WriteTemplate(string path)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(path, TemplateJson);
            }
            catch (Exception ex)
            {
                LogSink.Warn("profiles: could not write template: " + ex.Message);
            }
        }

        private const string TemplateJson =
            "{\r\n" +
            "  \"_comment\": \"Providers: openai | openai-sse | anthropic | gemini | ollama | koboldcpp | player2 | custom. Set rootUrl and apiKey of your provider; for player2 the model field may stay empty.\",\r\n" +
            "  \"active\": \"example\",\r\n" +
            "  \"profiles\": [\r\n" +
            "    {\r\n" +
            "      \"name\": \"example\",\r\n" +
            "      \"provider\": \"openai\",\r\n" +
            "      \"rootUrl\": \"https://api.openai.com\",\r\n" +
            "      \"apiKey\": \"\",\r\n" +
            "      \"model\": \"gpt-4o-mini\",\r\n" +
            "      \"temperature\": 0.7,\r\n" +
            "      \"maxTokens\": 0,\r\n" +
            "      \"timeoutSeconds\": 180\r\n" +
            "    },\r\n" +
            "    {\r\n" +
            "      \"name\": \"ollama-local\",\r\n" +
            "      \"provider\": \"ollama\",\r\n" +
            "      \"rootUrl\": \"http://127.0.0.1:11434\",\r\n" +
            "      \"apiKey\": \"\",\r\n" +
            "      \"model\": \"llama3.2\",\r\n" +
            "      \"temperature\": 0.7,\r\n" +
            "      \"maxTokens\": 0,\r\n" +
            "      \"timeoutSeconds\": 180\r\n" +
            "    }\r\n" +
            "  ]\r\n" +
            "}";

        // Serializes a profile to JSON format. Public for unit tests.
        public static string FormatProfileJson(Profile profile)
        {
            if (profile == null) return string.Empty;
            JObject entry = new JObject();
            WriteEntry(entry, profile);
            JObject doc = new JObject
            {
                ["active"] = profile.Name ?? "default",
                ["profiles"] = new JArray { entry }
            };
            return doc.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // Parse the JSON schema of profiles. Public for unit tests and E6.
        public static bool TryParseJson(string text, out Profile profile, out string error)
        {
            profile = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "empty file";
                return false;
            }

            JObject document;
            try
            {
                document = JObject.Parse(text);
            }
            catch (Exception ex)
            {
                error = "invalid json: " + ex.Message;
                return false;
            }

            JArray items = document["profiles"] as JArray;
            if (items == null || items.Count == 0)
            {
                error = "no profiles array";
                return false;
            }

            List<Profile> parsed = new List<Profile>();
            foreach (JToken item in items)
            {
                JObject entry = item as JObject;
                if (entry == null)
                {
                    continue;
                }

                Profile candidate = ReadProfile(entry);
                if (candidate != null)
                {
                    parsed.Add(candidate);
                }
            }

            if (parsed.Count == 0)
            {
                error = "no valid profiles";
                return false;
            }

            profile = FindByName(parsed, (string)document["active"]) ?? parsed[0];
            return true;
        }

        private static Profile FindByName(List<Profile> profiles, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (Profile profile in profiles)
            {
                if (string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private static Profile ReadProfile(JObject entry)
        {
            ProviderKind kind;
            Profile.TryParseProvider((string)entry["provider"], out kind);

            string name = ((string)entry["name"] ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                name = "profile";
            }

            // Diagnostics flags — only override when the key is present, so
            // existing configs keep their ctor defaults (on).
            bool repairOn, masterOn, logOn, errorsOn;

            Profile profile = new Profile
            {
                Name = name,
                Provider = kind,
                RootUrl = ((string)entry["rootUrl"] ?? string.Empty).Trim(),
                ApiKey = (string)entry["apiKey"] ?? string.Empty,
                Model = ((string)entry["model"] ?? string.Empty).Trim()
            };

            if (ReadBool(entry["jsonRepair"], out repairOn))
            {
                profile.JsonRepairEnabled = repairOn;
            }

            if (ReadBool(entry["masterEnabled"], out masterOn))
            {
                profile.MasterEnabled = masterOn;
            }

            if (ReadBool(entry["logEnabled"], out logOn))
            {
                profile.LogEnabled = logOn;
            }

            if (ReadBool(entry["errorNotices"], out errorsOn))
            {
                profile.ErrorNoticesEnabled = errorsOn;
            }

            double? temperature = ReadNumber(entry["temperature"]);
            if (temperature.HasValue)
            {
                profile.Temperature = Clamp(temperature.Value, 0.0, 2.0);
            }

            int? maxTokens = ReadInt(entry["maxTokens"]);
            if (maxTokens.HasValue && maxTokens.Value > 0)
            {
                profile.MaxTokens = maxTokens.Value;
            }

            int? timeout = ReadInt(entry["timeoutSeconds"]);
            if (timeout.HasValue && timeout.Value >= 10)
            {
                profile.TimeoutSeconds = timeout.Value;
            }

            string re = (string)entry["reasoningEffort"] ?? (string)entry["reasoning"];
            if (!string.IsNullOrEmpty(re))
            {
                profile.ReasoningEffort = re.Trim();
            }

            string cliPath = (string)entry["antigravityCliPath"];
            if (!string.IsNullOrWhiteSpace(cliPath))
            {
                profile.AntigravityCliPath = cliPath.Trim();
            }

            string agyModel = (string)entry["antigravityModel"];
            if (!string.IsNullOrWhiteSpace(agyModel))
            {
                profile.AntigravityModel = agyModel.Trim();
            }

            bool twoStage;
            if (ReadBool(entry["antigravityTwoStageRequest"] ?? entry["twoStageRequest"], out twoStage))
            {
                profile.AntigravityTwoStageRequest = twoStage;
            }

            // Per-scope settings.
            JObject scopes = entry["scopes"] as JObject;
            if (scopes != null)
            {
                foreach (JProperty prop in scopes.Properties())
                {
                    ScopeId id;
                    if (!TryParseScope((string)prop.Name, out id))
                    {
                        continue;
                    }

                    JObject so = prop.Value as JObject;
                    if (so == null)
                    {
                        continue;
                    }

                    ScopeSettings settings = ReadScope(so);
                    profile.Scope(id); // ensure created
                    profile.Scopes[id] = settings;
                }
            }

            // Per-provider stored settings
            JObject providersObj = entry["providers"] as JObject;
            if (providersObj != null)
            {
                foreach (JProperty prop in providersObj.Properties())
                {
                    JObject po = prop.Value as JObject;
                    if (po != null)
                    {
                        profile.Providers[prop.Name] = ReadProviderSettings(po);
                    }
                }
            }
            profile.SaveCurrentToProvider(profile.Provider);

            string imgProv = (string)entry["imageProvider"];
            if (!string.IsNullOrWhiteSpace(imgProv))
            {
                profile.ImageProvider = imgProv.Trim();
            }
            string imgRoot = (string)entry["imageRootUrl"];
            if (!string.IsNullOrWhiteSpace(imgRoot))
            {
                profile.ImageRootUrl = imgRoot.Trim();
            }
            string imgKey = (string)entry["imageApiKey"];
            if (!string.IsNullOrWhiteSpace(imgKey))
            {
                profile.ImageApiKey = imgKey.Trim();
            }
            string imgModel = (string)entry["imageModel"];
            if (!string.IsNullOrWhiteSpace(imgModel))
            {
                profile.ImageModel = imgModel.Trim();
            }
            string imgArt = (string)entry["imageArtStyle"];
            if (!string.IsNullOrWhiteSpace(imgArt))
            {
                profile.ImageArtStyle = imgArt.Trim();
            }
            bool failSilently;
            if (ReadBool(entry["imageFailSilently"], out failSilently))
            {
                profile.ImageFailSilently = failSilently;
            }
            bool autoFb;
            if (ReadBool(entry["imageAutoFallbackFree"], out autoFb))
            {
                profile.ImageAutoFallbackFree = autoFb;
            }

            JObject imgProvidersObj = entry["imageProviders"] as JObject;
            if (imgProvidersObj != null)
            {
                foreach (JProperty prop in imgProvidersObj.Properties())
                {
                    JObject ipo = prop.Value as JObject;
                    if (ipo != null)
                    {
                        var ips = new ImageProviderSettings
                        {
                            RootUrl = ((string)ipo["rootUrl"] ?? string.Empty).Trim(),
                            ApiKey = (string)ipo["apiKey"] ?? string.Empty,
                            Model = ((string)ipo["model"] ?? string.Empty).Trim()
                        };
                        JArray am = ipo["availableModels"] as JArray;
                        if (am != null)
                        {
                            foreach (JToken item in am)
                            {
                                string m = (string)item;
                                if (!string.IsNullOrEmpty(m) && !ips.AvailableModels.Contains(m))
                                {
                                    ips.AvailableModels.Add(m);
                                }
                            }
                        }
                        profile.ImageProviders[prop.Name] = ips;
                    }
                }
            }
            profile.SaveCurrentImageProvider(profile.ImageProvider);

            return profile;
        }

        private static bool ReadBool(JToken token, out bool value)
        {
            value = false;
            if (token == null || token.Type != JTokenType.Boolean)
            {
                return false;
            }

            value = token.Value<bool>();
            return true;
        }

        private static bool TryParseScope(string text, out ScopeId id)
        {
            id = ScopeId.None;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string t = text.Trim();
            string lower = t.ToLowerInvariant();
            switch (lower)
            {
                case "dialogue": id = ScopeId.Dialogue; return true;
                case "diplomacy": id = ScopeId.Diplomacy; return true;
                case "events": case "dynamicevents": id = ScopeId.Events; return true;
                case "memorybook": case "memory book": case "memory": id = ScopeId.MemoryBook; return true;
                case "battletactics": case "battle tactics": case "battle": id = ScopeId.BattleTactics; return true;
                case "groupconversation": case "groupconversations": case "group conversation": case "group conversations": case "group": id = ScopeId.GroupConversation; return true;
                case "uniquecharacters": case "unique characters": case "unique": id = ScopeId.UniqueCharacters; return true;
                default: return false;
            }
        }

        private static ScopeSettings ReadScope(JObject so)
        {
            ScopeSettings s = new ScopeSettings();
            bool enabled, timer, genImages;
            ReadBool(so["enabled"], out enabled);
            ReadBool(so["timerInChat"], out timer);
            ReadBool(so["generateImages"], out genImages);
            s.Enabled = enabled;
            s.TimerInChat = timer;
            s.GenerateImages = genImages;

            string imgProv = (string)so["imageProvider"];
            s.ImageProvider = !string.IsNullOrWhiteSpace(imgProv) ? imgProv.Trim() : "Default";

            s.ImageModel = ((string)so["imageModel"] ?? string.Empty).Trim();
            s.ImageEndpoint = ((string)so["imageEndpoint"] ?? string.Empty).Trim();

            ProviderKind kind;
            string rawProvider = (string)so["provider"];
            Profile.TryParseProvider(rawProvider, out kind);
            s.Provider = kind;
            s.ProviderRaw = rawProvider != null ? rawProvider.Trim() : null;

            s.RootUrl = ((string)so["rootUrl"] ?? string.Empty).Trim();
            s.ApiKey = (string)so["apiKey"] ?? string.Empty;
            s.Model = ((string)so["model"] ?? string.Empty).Trim();

            double? temperature = ReadNumber(so["temperature"]);
            if (temperature.HasValue)
            {
                s.Temperature = Clamp(temperature.Value, 0.0, 2.0);
            }

            int? maxTokens = ReadInt(so["maxTokens"]);
            if (maxTokens.HasValue && maxTokens.Value > 0)
            {
                s.MaxTokens = maxTokens.Value;
            }

            int? timeout = ReadInt(so["timeoutSeconds"]);
            if (timeout.HasValue && timeout.Value >= 10)
            {
                s.TimeoutSeconds = timeout.Value;
            }

            JArray am = so["availableModels"] as JArray;
            if (am != null)
            {
                foreach (JToken item in am)
                {
                    string m = (string)item;
                    if (!string.IsNullOrEmpty(m) && !s.AvailableModels.Contains(m))
                    {
                        s.AvailableModels.Add(m);
                    }
                }
            }

            string sre = (string)so["reasoningEffort"] ?? (string)so["reasoning"];
            if (!string.IsNullOrEmpty(sre))
            {
                s.ReasoningEffort = sre.Trim();
            }

            // Per-provider stored settings
            JObject providersObj = so["providers"] as JObject;
            if (providersObj != null)
            {
                foreach (JProperty prop in providersObj.Properties())
                {
                    JObject po = prop.Value as JObject;
                    if (po != null)
                    {
                        s.Providers[prop.Name] = ReadProviderSettings(po);
                    }
                }
            }
            s.SaveCurrentToProvider(s.Provider);

            return s;
        }

        private static void WriteEntry(JObject entry, Profile profile)
        {
            profile.SaveCurrentToProvider(profile.Provider);
            profile.SaveCurrentImageProvider(profile.ImageProvider);

            entry["name"] = profile.Name ?? "profile";
            entry["masterEnabled"] = profile.MasterEnabled;
            entry["logEnabled"] = profile.LogEnabled;
            entry["jsonRepair"] = profile.JsonRepairEnabled;
            entry["errorNotices"] = profile.ErrorNoticesEnabled;
            entry["provider"] = Profile.Label(profile.Provider);
            entry["rootUrl"] = profile.RootUrl ?? string.Empty;
            entry["apiKey"] = profile.ApiKey ?? string.Empty;
            entry["model"] = profile.Model ?? string.Empty;
            entry["temperature"] = profile.Temperature;
            entry["maxTokens"] = profile.MaxTokens;
            entry["timeoutSeconds"] = profile.TimeoutSeconds;
            if (!string.IsNullOrEmpty(profile.ReasoningEffort) &&
                !string.Equals(profile.ReasoningEffort, "default", StringComparison.OrdinalIgnoreCase))
            {
                entry["reasoningEffort"] = profile.ReasoningEffort;
            }

            entry["imageProvider"] = profile.ImageProvider ?? "OpenAI";
            entry["imageRootUrl"] = profile.ImageRootUrl ?? string.Empty;
            entry["imageApiKey"] = profile.ImageApiKey ?? string.Empty;
            entry["imageModel"] = profile.ImageModel ?? string.Empty;
            entry["imageArtStyle"] = profile.ImageArtStyle ?? "Bannerlord Concept Art";
            entry["imageFailSilently"] = profile.ImageFailSilently;
            entry["imageAutoFallbackFree"] = profile.ImageAutoFallbackFree;

            if (profile.ImageProviders != null && profile.ImageProviders.Count > 0)
            {
                JObject ipObj = new JObject();
                foreach (var kvp in profile.ImageProviders)
                {
                    if (kvp.Value != null)
                    {
                        JObject item = new JObject
                        {
                            { "rootUrl", kvp.Value.RootUrl ?? string.Empty },
                            { "apiKey", kvp.Value.ApiKey ?? string.Empty },
                            { "model", kvp.Value.Model ?? string.Empty }
                        };
                        if (kvp.Value.AvailableModels != null && kvp.Value.AvailableModels.Count > 0)
                        {
                            item["availableModels"] = JArray.FromObject(kvp.Value.AvailableModels);
                        }
                        ipObj[kvp.Key] = item;
                    }
                }
                entry["imageProviders"] = ipObj;
            }

            if (profile.Providers != null && profile.Providers.Count > 0)
            {
                JObject pObj = new JObject();
                foreach (var kvp in profile.Providers)
                {
                    if (kvp.Value != null)
                    {
                        pObj[kvp.Key] = SerializeProviderSettings(kvp.Value);
                    }
                }
                entry["providers"] = pObj;
            }

            if (!string.IsNullOrEmpty(profile.AntigravityCliPath))
            {
                entry["antigravityCliPath"] = profile.AntigravityCliPath;
            }

            if (!string.IsNullOrEmpty(profile.AntigravityModel))
            {
                entry["antigravityModel"] = profile.AntigravityModel;
            }

            if (profile.AntigravityTwoStageRequest)
            {
                entry["antigravityTwoStageRequest"] = true;
            }

            if (profile.Scopes != null && profile.Scopes.Count > 0)
            {
                JObject scopes = new JObject();
                foreach (var pair in profile.Scopes)
                {
                    ScopeSettings s = pair.Value;
                    s.SaveCurrentToProvider(s.Provider);
                    JObject so = new JObject
                    {
                        { "enabled", s.Enabled },
                        { "timerInChat", s.TimerInChat },
                        { "generateImages", s.GenerateImages },
                        { "imageProvider", s.ImageProvider ?? "Default" },
                        { "imageModel", s.ImageModel ?? string.Empty },
                        { "imageEndpoint", s.ImageEndpoint ?? string.Empty },
                        { "provider", Profile.Label(s.Provider) },
                        { "rootUrl", s.RootUrl ?? string.Empty },
                        { "apiKey", s.ApiKey ?? string.Empty },
                        { "model", s.Model ?? string.Empty },
                        { "temperature", s.Temperature },
                        { "maxTokens", s.MaxTokens },
                        { "timeoutSeconds", s.TimeoutSeconds }
                    };

                    if (!string.IsNullOrEmpty(s.ReasoningEffort) &&
                        !string.Equals(s.ReasoningEffort, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        so["reasoningEffort"] = s.ReasoningEffort;
                    }

                    if (s.AvailableModels != null && s.AvailableModels.Count > 0)
                    {
                        so["availableModels"] = JArray.FromObject(s.AvailableModels);
                    }

                    if (s.Providers != null && s.Providers.Count > 0)
                    {
                        JObject spObj = new JObject();
                        foreach (var kvp in s.Providers)
                        {
                            if (kvp.Value != null)
                            {
                                spObj[kvp.Key] = SerializeProviderSettings(kvp.Value);
                            }
                        }
                        so["providers"] = spObj;
                    }

                    scopes[ScopeFileName(pair.Key)] = so;
                }

                entry["scopes"] = scopes;
            }
        }

        private static ProviderSettings ReadProviderSettings(JObject obj)
        {
            ProviderSettings ps = new ProviderSettings
            {
                RootUrl = ((string)obj["rootUrl"] ?? string.Empty).Trim(),
                ApiKey = (string)obj["apiKey"] ?? string.Empty,
                Model = ((string)obj["model"] ?? string.Empty).Trim()
            };

            double? temp = ReadNumber(obj["temperature"]);
            if (temp.HasValue) ps.Temperature = Clamp(temp.Value, 0.0, 2.0);

            int? mt = ReadInt(obj["maxTokens"]);
            if (mt.HasValue && mt.Value >= 0) ps.MaxTokens = mt.Value;

            int? to = ReadInt(obj["timeoutSeconds"]);
            if (to.HasValue && to.Value >= 10) ps.TimeoutSeconds = to.Value;

            string re = (string)obj["reasoningEffort"] ?? (string)obj["reasoning"];
            if (!string.IsNullOrEmpty(re)) ps.ReasoningEffort = re.Trim();

            JArray am = obj["availableModels"] as JArray;
            if (am != null)
            {
                foreach (JToken item in am)
                {
                    string m = (string)item;
                    if (!string.IsNullOrEmpty(m) && !ps.AvailableModels.Contains(m))
                    {
                        ps.AvailableModels.Add(m);
                    }
                }
            }

            return ps;
        }

        private static JObject SerializeProviderSettings(ProviderSettings ps)
        {
            JObject obj = new JObject
            {
                { "rootUrl", ps.RootUrl ?? string.Empty },
                { "apiKey", ps.ApiKey ?? string.Empty },
                { "model", ps.Model ?? string.Empty },
                { "temperature", ps.Temperature },
                { "maxTokens", ps.MaxTokens },
                { "timeoutSeconds", ps.TimeoutSeconds }
            };

            if (!string.IsNullOrEmpty(ps.ReasoningEffort) &&
                !string.Equals(ps.ReasoningEffort, "default", StringComparison.OrdinalIgnoreCase))
            {
                obj["reasoningEffort"] = ps.ReasoningEffort;
            }

            if (ps.AvailableModels != null && ps.AvailableModels.Count > 0)
            {
                obj["availableModels"] = JArray.FromObject(ps.AvailableModels);
            }

            return obj;
        }

        /// <summary>Scope id to the JSON file name (keys in the scopes object).</summary>
        private static string ScopeFileName(ScopeId id)
        {
            switch (id)
            {
                case ScopeId.Dialogue: return "dialogue";
                case ScopeId.Diplomacy: return "diplomacy";
                case ScopeId.Events: return "events";
                case ScopeId.MemoryBook: return "memoryBook";
                case ScopeId.BattleTactics: return "battleTactics";
                case ScopeId.GroupConversation: return "groupConversation";
                case ScopeId.UniqueCharacters: return "uniqueCharacters";
                default: return id.ToString();
            }
        }

        private static double? ReadNumber(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            // Value<double> parses invariantly; ToString() would depend on locale.
            try
            {
                return token.Value<double>();
            }
            catch
            {
                return null;
            }
        }

        private static int? ReadInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            try
            {
                return token.Value<int>();
            }
            catch
            {
                return null;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}