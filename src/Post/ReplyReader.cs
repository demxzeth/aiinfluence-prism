using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AIInfluencePrism.Post
{
    /// <summary>
    /// One place that turns a raw provider body into answer text.
    /// Three reply shapes cover every transport: a JSON object with
    /// the answer at a known token path, an object whose answer is
    /// split over an array of typed content blocks, and an SSE
    /// stream of incremental frames. Request building stays in the
    /// transports; reply shapes are handled here.
    /// </summary>
    public static class ReplyReader
    {
        /// <summary>
        /// Read a scalar string from the first token path that
        /// yields a non-empty value ("choices[0].message.content").
        /// </summary>
        public static string ReadJson(string rawBody, params string[] tokenPaths)
        {
            JObject json;
            if (!JsonHealer.TryParse(rawBody, out json))
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

        /// <summary>
        /// Join text fields from an array of content blocks, optionally
        /// keeping only blocks whose type field equals the wanted type
        /// (Anthropic "text" blocks; Gemini parts have no type field).
        /// </summary>
        public static string ReadBlocks(
            string rawBody, string arrayPath, string textField,
            string typeField = null, string wantedType = "text")
        {
            JObject json;
            if (!JsonHealer.TryParse(rawBody, out json))
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

        /// <summary>
        /// Assemble an SSE body: join the values at the given paths of
        /// every "data:" frame until the [DONE] sentinel. Broken frames
        /// are skipped and comment lines ignored.
        /// </summary>
        public static string ReadSse(string rawBody, params string[] framePaths)
        {
            if (string.IsNullOrEmpty(rawBody))
            {
                return null;
            }

            StringBuilder assembled = new StringBuilder();
            using (StringReader reader = new StringReader(rawBody))
            {
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

                    JObject frame;
                    if (!JsonHealer.TryParse(payload, out frame))
                    {
                        continue;
                    }

                    foreach (string path in framePaths)
                    {
                        string delta = (string)frame.SelectToken(path);
                        if (delta != null)
                        {
                            assembled.Append(delta);
                            break;
                        }
                    }
                }
            }

            return assembled.Length > 0 ? assembled.ToString() : null;
        }

        /// <summary>Read a single named top-level string field.</summary>
        public static string ReadField(string rawBody, string fieldName)
        {
            JObject json;
            if (!JsonHealer.TryParse(rawBody, out json))
            {
                return null;
            }

            string value = (string)json[fieldName];
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>True when the body looks like an SSE stream.</summary>
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
