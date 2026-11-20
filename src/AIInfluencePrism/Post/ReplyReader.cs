using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Post
{
    /// <summary>
    /// One place that turns a raw provider body into answer text.
    /// Three reply shapes cover every transport: a JSON object with
    /// the answer at a known token path, an object whose answer is
    /// split over an array of typed content blocks, and an SSE
    /// stream of incremental frames.
    /// </summary>
    public static class ReplyReader
    {
        public static bool ParseJson(string rawBody, out JObject json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return false;
            }

            if (ProfileBook.Current.JsonRepairEnabled)
            {
                return JsonHealer.TryParse(rawBody, out json);
            }

            try
            {
                json = JObject.Parse(rawBody);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ReadJson(string rawBody, params string[] tokenPaths)
        {
            if (!ParseJson(rawBody, out JObject json))
            {
                return null;
            }

            foreach (string path in tokenPaths)
            {
                string value = (string)json.SelectToken(path);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
        }

        public static string ReadBlocks(
            string rawBody, string arrayPath, string textField,
            string typeField = null, string wantedType = "text")
        {
            if (!ParseJson(rawBody, out JObject json))
            {
                return null;
            }

            JArray parts = json.SelectToken(arrayPath) as JArray;
            if (parts == null)
            {
                return null;
            }

            StringBuilder text = new StringBuilder();
            foreach (JToken part in parts)
            {
                string piece = (string)part[textField];
                if (piece == null)
                {
                    continue;
                }

                if (typeField != null)
                {
                    string type = (string)part[typeField];
                    if (type != null && type != wantedType)
                    {
                        continue;
                    }
                }

                text.Append(piece);
            }

            return text.Length > 0 ? text.ToString() : null;
        }

        public static string ReadSse(TextReader reader, params string[] framePaths)
        {
            return ReadSse(reader, null, framePaths);
        }

        public static string ReadSse(TextReader reader, Action<string> onDelta, params string[] framePaths)
        {
            if (reader == null)
            {
                return null;
            }

            StringBuilder assembled = new StringBuilder();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string payload = trimmed.Substring(5).Trim();
                if (payload.Length == 0)
                {
                    continue;
                }

                if (string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!ParseJson(payload, out JObject frame))
                {
                    continue;
                }

                foreach (string path in framePaths)
                {
                    string delta = (string)frame.SelectToken(path);
                    if (delta != null)
                    {
                        assembled.Append(delta);
                        onDelta?.Invoke(delta);
                        break;
                    }
                }
            }

            return assembled.Length > 0 ? assembled.ToString() : null;
        }

        public static async Task<string> ReadSseAsync(TextReader reader, params string[] framePaths)
        {
            return await ReadSseAsync(reader, null, framePaths).ConfigureAwait(false);
        }

        public static async Task<string> ReadSseAsync(TextReader reader, Action<string> onDelta, params string[] framePaths)
        {
            if (reader == null)
            {
                return null;
            }

            StringBuilder assembled = new StringBuilder();
            string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string payload = trimmed.Substring(5).Trim();
                if (payload.Length == 0)
                {
                    continue;
                }

                if (string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!ParseJson(payload, out JObject frame))
                {
                    continue;
                }

                foreach (string path in framePaths)
                {
                    string delta = (string)frame.SelectToken(path);
                    if (delta != null)
                    {
                        assembled.Append(delta);
                        onDelta?.Invoke(delta);
                        break;
                    }
                }
            }

            return assembled.Length > 0 ? assembled.ToString() : null;
        }

        public static string ReadSse(string rawBody, params string[] framePaths)
        {
            return ReadSse(rawBody, null, framePaths);
        }

        public static string ReadSse(string rawBody, Action<string> onDelta, params string[] framePaths)
        {
            if (string.IsNullOrEmpty(rawBody))
            {
                return null;
            }

            using (StringReader reader = new StringReader(rawBody))
            {
                return ReadSse(reader, onDelta, framePaths);
            }
        }

        public static string ReadField(string rawBody, string fieldName)
        {
            if (!ParseJson(rawBody, out JObject json))
            {
                return null;
            }

            string value = (string)json[fieldName];
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public static bool LooksLikeSse(string rawBody)
        {
            if (string.IsNullOrEmpty(rawBody))
            {
                return false;
            }

            return rawBody.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                   || rawBody.Contains("\ndata:")
                   || rawBody.Contains("\rdata:");
        }
    }
}
