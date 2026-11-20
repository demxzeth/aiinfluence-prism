using System;
using System.Reflection;
using HarmonyLib;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Legacy Subtitle Hook Container.
    /// In accordance with pure transparent proxy architecture:
    /// ConversationManager.CurrentSentenceText is NO LONGER PATCHED.
    /// Bannerlord awaits AIClient.GetAIResponse natively without artificial subtitle replacements
    /// or race-condition parrot loops.
    /// </summary>
    public static class DialogueStubReplacer
    {
        public static volatile string LastPrismResponse = string.Empty;
        private static bool _installed;
        private static readonly object _syncLock = new object();

        public static bool Installed => _installed;

        /// <summary>
        /// Cleans and extracts dialogue speech text from raw LLM output using JsonHealer,
        /// removing markdown code fences, technical wrappers, and extracting inner responses.
        /// </summary>
        public static string CleanDialogueText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string text = JsonHealer.StripCodeFences(raw.Trim());

            // Try parsing as JSON to extract "response" or OpenAI "choices"
            int braceStart = text.IndexOf('{');
            int braceEnd = text.LastIndexOf('}');
            if (braceStart >= 0 && braceEnd > braceStart)
            {
                string jsonCandidate = text.Substring(braceStart, braceEnd - braceStart + 1);
                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(jsonCandidate);
                    if (obj["response"] != null)
                    {
                        string inner = (string)obj["response"];
                        if (!string.IsNullOrWhiteSpace(inner))
                        {
                            return CleanDialogueText(inner);
                        }
                    }
                    else if (obj["choices"] is Newtonsoft.Json.Linq.JArray arr && arr.Count > 0)
                    {
                        string c = (string)arr[0]?["message"]?["content"] ?? (string)arr[0]?["text"];
                        if (!string.IsNullOrEmpty(c))
                        {
                            return CleanDialogueText(c);
                        }
                    }
                    else if (obj["content"] != null)
                    {
                        return CleanDialogueText((string)obj["content"]);
                    }
                    else if (obj["text"] != null)
                    {
                        return CleanDialogueText((string)obj["text"]);
                    }
                    else if (obj["message"] != null && obj["message"] is Newtonsoft.Json.Linq.JObject msgObj)
                    {
                        return CleanDialogueText((string)msgObj["content"]);
                    }

                    // Pure technical object (e.g. {"name": "..."}) - no dialogue speech property
                    return string.Empty;
                }
                catch
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        jsonCandidate,
                        @"""response""\s*:\s*""(?<val>(?:[^""\\]|\\.)*)""",
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (m.Success)
                    {
                        string val = System.Text.RegularExpressions.Regex.Unescape(m.Groups["val"].Value);
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return CleanDialogueText(val);
                        }
                    }
                }
            }

            if (text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\""))
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            if (text.StartsWith("{") || text.EndsWith("}"))
            {
                return string.Empty;
            }

            return text;
        }

        public static bool IsValidDialogueSpeech(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            if (t.Length < 2) return false;

            if (t.StartsWith("{") || t.EndsWith("}") || t.StartsWith("[") || t.EndsWith("]"))
            {
                return false;
            }

            if (t.IndexOf("\"name\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("'name'", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("name:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("\"emotion\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("\"actions\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("\"traits\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("\"skills\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.IndexOf("\"internal_thoughts\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Subtitle replacement is decommissioned in favor of native async resolution.
        /// </summary>
        public static void SetLastResponse(string textOrJson, Routing.ScopeId scope = Routing.ScopeId.None)
        {
            // No-op: do not cache or repeat previous lines across conversation turns.
        }

        /// <summary>
        /// Decommissioned: no Harmony patches on ConversationManager.CurrentSentenceText.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            // Pure transparent proxy architecture: no subtitle patching.
            _installed = false;
        }

        public static void Reset()
        {
            lock (_syncLock)
            {
                _installed = false;
                LastPrismResponse = string.Empty;
            }
        }

        /// <summary>
        /// Decommissioned postfix: leaves the sentence text unmutated.
        /// </summary>
        public static void CurrentSentenceText_Postfix(ref string __result)
        {
            // Native passthrough: do not alter the game sentence text during async waiting.
        }
    }
}
