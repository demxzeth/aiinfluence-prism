using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIInfluencePrism.Post
{
    /// <summary>
    /// Repairs malformed JSON bodies and dialogue outputs before parsing. Local LLM servers
    /// and proxies misbehave in predictable ways: BOMs, control characters, markdown code fences,
    /// garbage around the payload, unquoted bare values, trailing commas, and bodies cut off mid-answer.
    /// </summary>
    public static class JsonHealer
    {
        public static string Heal(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string stripped = Clean(StripCodeFences(raw));
            int start = stripped.IndexOf('{');
            if (start < 0)
            {
                return null;
            }

            string text = stripped.Substring(start);

            int? end = FindBalancedEnd(text);
            if (end != null)
            {
                string balanced = StripInnerTrailingCommas(text.Substring(0, end.Value + 1));
                if (TryParseExact(balanced, out string valid))
                {
                    return valid;
                }
                string bareQuoted = QuoteMalformedBareValues(balanced);
                if (TryParseExact(bareQuoted, out valid))
                {
                    return valid;
                }
                text = balanced;
            }
            else
            {
                text = Complete(QuoteMalformedBareValues(text));
                if (text == null)
                {
                    return null;
                }
            }

            return text.Length > 0 ? text : null;
        }

        private static bool TryParseExact(string json, out string result)
        {
            result = null;
            try
            {
                JToken.Parse(json);
                result = json;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Universal content repair: strips code fences, extracts and repairs JSON if present,
        /// or returns clean text when the input is not JSON.
        /// </summary>
        public static string RepairContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            string healed = Heal(content);
            if (!string.IsNullOrEmpty(healed))
            {
                return healed;
            }
            return StripCodeFences(content);
        }

        /// <summary>
        /// Strips markdown code fences (```json ... ``` or ``` ... ```).
        /// </summary>
        public static string StripCodeFences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string t = text.Trim();

            if (t.Contains("```"))
            {
                int firstFence = t.IndexOf("```", StringComparison.Ordinal);
                int lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
                if (firstFence >= 0 && lastFence > firstFence)
                {
                    int newlineAfterFirst = t.IndexOf('\n', firstFence);
                    if (newlineAfterFirst > 0 && newlineAfterFirst < lastFence)
                    {
                        return t.Substring(newlineAfterFirst + 1, lastFence - newlineAfterFirst - 1).Trim();
                    }
                    return t.Substring(firstFence + 3, lastFence - firstFence - 3).Trim();
                }
            }

            return Regex.Replace(
                Regex.Replace(t, @"^\s*```[a-zA-Z0-9_-]*\s*", string.Empty),
                @"\s*```\s*$", string.Empty).Trim();
        }

        /// <summary>
        /// Quotes unquoted bare string values in key-value pairs (e.g. "emotion": neutral -> "emotion": "neutral").
        /// </summary>
        public static string QuoteMalformedBareValues(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"(""(?<field>[A-Za-z0-9_]+)""\s*:\s*)(?<value>[a-zA-Z_][a-zA-Z0-9_-]*)", match =>
            {
                string val = match.Groups["value"].Value.Trim();
                if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(val, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(val, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }
                return match.Groups[1].Value + "\"" + val + "\"";
            });
        }

        public static bool TryParse(string raw, out JObject json)
        {
            json = null;
            string healed = Heal(raw);
            if (healed == null)
            {
                return false;
            }

            try
            {
                json = JObject.Parse(healed);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }

            StringBuilder sb = new StringBuilder(raw.Length + 64);
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\uFEFF' || c == '﻿')
                {
                    continue;
                }

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                        sb.Append(c);
                        continue;
                    }

                    if (c == '\r')
                    {
                        if (i + 1 < raw.Length && raw[i + 1] == '\n')
                        {
                            i++;
                        }
                        sb.Append("\\n");
                        continue;
                    }

                    if (c == '\n')
                    {
                        sb.Append("\\n");
                        continue;
                    }

                    if (c == '\t')
                    {
                        sb.Append("\\t");
                        continue;
                    }

                    if (c < ' ')
                    {
                        continue;
                    }

                    sb.Append(c);
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                        sb.Append(c);
                        continue;
                    }

                    if (c < ' ')
                    {
                        continue;
                    }

                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static int? FindBalancedEnd(string text)
        {
            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                    if (depth < 0) return null;
                }
            }

            return null;
        }

        private static string StripInnerTrailingCommas(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    sb.Append(c);
                    continue;
                }

                if (c == ',')
                {
                    int j = i + 1;
                    while (j < text.Length && char.IsWhiteSpace(text[j]))
                    {
                        j++;
                    }

                    if (j < text.Length && (text[j] == '}' || text[j] == ']'))
                    {
                        continue;
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string Complete(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length + 8);
            Stack<char> open = new Stack<char>();
            bool inString = false;
            bool escaped = false;
            int stringStart = -1;
            bool stringIsKey = false;
            int danglingKeyStart = -1;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                sb.Append(c);

                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"')
                    {
                        inString = false;
                        if (!stringIsKey) danglingKeyStart = -1;
                    }

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    stringStart = sb.Length - 1;
                    stringIsKey = IsKeyPosition(sb);
                    if (stringIsKey)
                    {
                        danglingKeyStart = stringStart;
                    }

                    continue;
                }

                if (c == '{' || c == '[')
                {
                    open.Push(c);
                    danglingKeyStart = -1;
                }
                else if (c == '}' || c == ']')
                {
                    if (open.Count == 0 || !Matches(open.Peek(), c))
                    {
                        return null;
                    }

                    open.Pop();
                    danglingKeyStart = -1;
                }
                else if (c == ',')
                {
                    danglingKeyStart = -1;
                }
                else if (!char.IsWhiteSpace(c) && c != ':')
                {
                    danglingKeyStart = -1;
                }
            }

            if (inString)
            {
                if (stringIsKey)
                {
                    sb.Length = stringStart;
                    TrimSeparators(sb);
                }
                else
                {
                    if (escaped)
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] == '\\')
                        {
                            sb.Length--;
                        }
                    }
                    else
                    {
                        int lastSlash = -1;
                        for (int k = sb.Length - 1; k >= Math.Max(0, sb.Length - 5); k--)
                        {
                            if (sb[k] == '\\') { lastSlash = k; break; }
                        }
                        if (lastSlash >= 0 && lastSlash < sb.Length - 1 && sb[lastSlash + 1] == 'u')
                        {
                            int hexChars = sb.Length - 1 - (lastSlash + 1);
                            if (hexChars < 4)
                            {
                                sb.Length = lastSlash;
                            }
                        }
                    }

                    sb.Append('"');
                }
            }
            else if (danglingKeyStart >= 0)
            {
                sb.Length = danglingKeyStart;
                TrimSeparators(sb);
            }

            TrimSeparators(sb);

            while (open.Count > 0)
            {
                sb.Append(open.Pop() == '{' ? '}' : ']');
            }

            return sb.ToString();
        }

        private static bool IsKeyPosition(StringBuilder sb)
        {
            for (int i = sb.Length - 2; i >= 0; i--)
            {
                char c = sb[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    continue;
                }

                return c == '{' || c == ',' || c == '[';
            }

            return true;
        }

        private static void TrimSeparators(StringBuilder sb)
        {
            while (sb.Length > 0)
            {
                char c = sb[sb.Length - 1];
                if (c == ',' || c == ':' || c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    sb.Length--;
                }
                else
                {
                    break;
                }
            }
        }

        private static bool Matches(char open, char close)
        {
            return (open == '{' && close == '}') || (open == '[' && close == ']');
        }
    }
}
