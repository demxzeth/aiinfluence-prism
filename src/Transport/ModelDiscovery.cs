using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Queries provider endpoints to retrieve a list of available models.
    /// Supports OpenAI-compatible, Google Gemini, Anthropic, Ollama, and KoboldCpp.
    /// </summary>
    public static class ModelDiscovery
    {
        /// <summary>
        /// Asynchronously fetches model IDs available on the provider endpoint configured in the profile.
        /// </summary>
        public static async Task<List<string>> FetchModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            switch (profile.Provider)
            {
                case ProviderKind.Gemini:
                    return await FetchGeminiModelsAsync(profile, cancellationToken).ConfigureAwait(false);

                case ProviderKind.Anthropic:
                    return await FetchAnthropicModelsAsync(profile, cancellationToken).ConfigureAwait(false);

                case ProviderKind.Ollama:
                    return await FetchOllamaModelsAsync(profile, cancellationToken).ConfigureAwait(false);

                case ProviderKind.KoboldCpp:
                    return await FetchKoboldModelsAsync(profile, cancellationToken).ConfigureAwait(false);

                case ProviderKind.Antigravity:
                    return await FetchAntigravityModelsAsync(profile, cancellationToken).ConfigureAwait(false);

                case ProviderKind.Nvidia:
                case ProviderKind.Custom:
                case ProviderKind.OpenAiCompat:
                case ProviderKind.OpenAiSse:
                case ProviderKind.Player2:
                default:
                    return await FetchOpenAiModelsAsync(profile, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<List<string>> FetchOpenAiModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            string root = profile.EffectiveRoot();
            string url = EndpointMapper.V1BaseUrl(root) + "/models";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string key = profile.ApiKey?.Trim() ?? string.Empty;
                if (key.Length > 0)
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                }

                string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                return ParseOpenAiModels(raw);
            }
        }

        public static List<string> ParseOpenAiModels(string json)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JArray data = obj["data"] as JArray ?? obj["models"] as JArray;
                if (data != null)
                {
                    foreach (JToken item in data)
                    {
                        string id = (string)item["id"] ?? (string)item["name"];
                        if (!string.IsNullOrEmpty(id) && !result.Contains(id))
                        {
                            result.Add(id);
                        }
                    }
                }
            }
            catch
            {
                // Unparseable JSON fallback
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static async Task<List<string>> FetchGeminiModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            string url = EndpointMapper.GeminiModelsUrl(profile.EffectiveRoot(), profile.ApiKey);
            string key = profile.ApiKey?.Trim() ?? string.Empty;

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (key.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
                }

                string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                return ParseGeminiModels(raw);
            }
        }

        public static List<string> ParseGeminiModels(string json)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JArray models = obj["models"] as JArray;
                if (models != null)
                {
                    foreach (JToken item in models)
                    {
                        JArray methods = item["supportedGenerationMethods"] as JArray;
                        if (methods != null)
                        {
                            bool supportsGenerate = false;
                            foreach (JToken m in methods)
                            {
                                if (string.Equals((string)m, "generateContent", StringComparison.OrdinalIgnoreCase))
                                {
                                    supportsGenerate = true;
                                    break;
                                }
                            }
                            if (!supportsGenerate)
                            {
                                continue;
                            }
                        }

                        string name = (string)item["name"];
                        if (!string.IsNullOrEmpty(name))
                        {
                            if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                            {
                                name = name.Substring(7);
                            }

                            if (!result.Contains(name))
                            {
                                result.Add(name);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Unparseable JSON fallback
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static async Task<List<string>> FetchAnthropicModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            string root = profile.EffectiveRoot().TrimEnd('/');
            string url = EndpointMapper.V1BaseUrl(root) + "/models";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string key = profile.ApiKey?.Trim() ?? string.Empty;
                if (key.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("x-api-key", key);
                    request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicTransport.ApiVersion);
                }

                string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                return ParseOpenAiModels(raw);
            }
        }

        private static async Task<List<string>> FetchOllamaModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            string root = profile.EffectiveRoot().TrimEnd('/');
            string url = root + "/api/tags";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                return ParseOllamaModels(raw);
            }
        }

        public static List<string> ParseOllamaModels(string json)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JArray models = obj["models"] as JArray;
                if (models != null)
                {
                    foreach (JToken item in models)
                    {
                        string name = (string)item["name"] ?? (string)item["model"];
                        if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch
            {
                // Unparseable JSON fallback
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static async Task<List<string>> FetchKoboldModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            string root = profile.EffectiveRoot().TrimEnd('/');
            string url = root + "/api/v1/model";
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                return ParseKoboldModels(raw);
            }
        }

        public static List<string> ParseKoboldModels(string json)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                string name = (string)obj["result"];
                if (!string.IsNullOrEmpty(name))
                {
                    result.Add(name);
                }
            }
            catch
            {
                // Unparseable JSON fallback
            }

            return result;
        }

        private static async Task<string> SendGetAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await HttpChatTransport.SharedClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false))
            {
                string body = response.Content == null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new TransportException((int)response.StatusCode, body);
                }

                return body;
            }
        }

        private static readonly string[] FallbackAntigravityModels = new[]
        {
            "gemini-3.8-flash-high",
            "gemini-3.8-flash-medium",
            "gemini-3.8-flash-low",
            "gemini-3.7-flash-high",
            "gemini-3.7-flash-medium",
            "gemini-3.7-flash-low",
            "gemini-3.6-flash-high",
            "gemini-3.6-flash-medium",
            "gemini-3.6-flash-low",
            "gemini-3.1-pro-high",
            "gemini-3.1-pro-low",
            "claude-sonnet-4-6",
            "claude-opus-4-6-thinking",
            "gpt-oss-120b-medium"
        };

        private static async Task<List<string>> FetchAntigravityModelsAsync(Profile profile, CancellationToken cancellationToken)
        {
            try
            {
                string cli = AntigravityTransport.ResolveCliPath(profile.AntigravityCliPath);
                ProcessResult res = await AntigravityTransport.RunProcessAsync(cli, "models", 10, cancellationToken)
                    .ConfigureAwait(false);
                if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.Output))
                {
                    List<string> models = ParseAntigravityModels(res.Output);
                    if (models.Count > 0)
                    {
                        return models;
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("antigravity: fetch models via cli failed: " + ex.Message);
            }

            return new List<string>(FallbackAntigravityModels);
        }

        public static List<string> ParseAntigravityModels(string output)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(output)) return result;

            using (var reader = new System.IO.StringReader(output))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (trimmed.StartsWith("Fetching", StringComparison.OrdinalIgnoreCase)) continue;

                    string[] parts = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        string modelId = parts[0].Trim();
                        if (modelId.Contains("-") ||
                            modelId.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) ||
                            modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase) ||
                            modelId.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!result.Contains(modelId))
                            {
                                result.Add(modelId);
                            }
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Fetches available models for an image generation provider (OpenAI, Google Gemini, Custom).
        /// </summary>
        public static async Task<List<string>> FetchImageModelsAsync(
            string providerName, string rootUrl, string apiKey, CancellationToken cancellationToken)
        {
            string p = (providerName ?? "OpenAI").ToLowerInvariant();
            if (p.Contains("gemini"))
            {
                return await FetchGeminiImageModelsAsync(rootUrl, apiKey, cancellationToken).ConfigureAwait(false);
            }
            if (p.Contains("free") || p.Contains("pollin"))
            {
                return new List<string> { "flux", "flux-realism", "flux-anime", "flux-3d", "any-dark", "turbo" };
            }

            // OpenAI and Custom
            return await FetchOpenAiImageModelsAsync(rootUrl, apiKey, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<List<string>> FetchOpenAiImageModelsAsync(
            string rootUrl, string apiKey, CancellationToken cancellationToken)
        {
            string root = !string.IsNullOrWhiteSpace(rootUrl) ? rootUrl.TrimEnd('/') : "https://api.openai.com";
            string url = EndpointMapper.V1BaseUrl(root) + "/models";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                string key = apiKey?.Trim() ?? string.Empty;
                if (key.Length > 0)
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                }

                try
                {
                    string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                    return ParseOpenAiImageModels(raw);
                }
                catch (Exception ex)
                {
                    LogSink.Warn("model-discovery: failed to fetch openai image models: " + ex.Message);
                    if (root.IndexOf("api.openai.com", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return new List<string> { "dall-e-3", "dall-e-2" };
                    }
                    throw;
                }
            }
        }

        public static List<string> ParseOpenAiImageModels(string json)
        {
            List<string> all = ParseOpenAiModels(json);
            if (all == null || all.Count == 0)
            {
                return new List<string> { "dall-e-3", "dall-e-2" };
            }

            var imageModels = new List<string>();
            var otherModels = new List<string>();

            foreach (string m in all)
            {
                string lower = m.ToLowerInvariant();
                if (lower.Contains("dall") ||
                    lower.Contains("image") ||
                    lower.Contains("flux") ||
                    lower.Contains("diffusion") ||
                    lower.Contains("sd") ||
                    lower.Contains("midjourney") ||
                    lower.Contains("paint"))
                {
                    imageModels.Add(m);
                }
                else
                {
                    otherModels.Add(m);
                }
            }

            imageModels.Sort((a, b) =>
            {
                if (a.IndexOf("dall-e-3", StringComparison.OrdinalIgnoreCase) >= 0) return -1;
                if (b.IndexOf("dall-e-3", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                return StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });

            if (imageModels.Count > 0)
            {
                var combined = new List<string>(imageModels);
                foreach (string o in otherModels)
                {
                    if (!combined.Contains(o)) combined.Add(o);
                }
                return combined;
            }

            return all;
        }

        private static async Task<List<string>> FetchGeminiImageModelsAsync(
            string rootUrl, string apiKey, CancellationToken cancellationToken)
        {
            string root = !string.IsNullOrWhiteSpace(rootUrl) ? rootUrl.TrimEnd('/') : "https://generativelanguage.googleapis.com";
            string url = EndpointMapper.GeminiModelsUrl(root, apiKey);
            string key = apiKey?.Trim() ?? string.Empty;

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (key.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
                }

                try
                {
                    string raw = await SendGetAsync(request, cancellationToken).ConfigureAwait(false);
                    return ParseGeminiImageModels(raw);
                }
                catch (Exception ex)
                {
                    LogSink.Warn("model-discovery: failed to fetch gemini image models: " + ex.Message);
                    return new List<string> { "gemini-2.5-flash-image", "gemini-3.1-flash-image", "gemini-3-pro-image", "imagen-3.0-generate-002" };
                }
            }
        }

        public static List<string> ParseGeminiImageModels(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string> { "gemini-2.5-flash-image", "gemini-3.1-flash-image", "gemini-3-pro-image", "imagen-3.0-generate-002" };
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JArray models = obj["models"] as JArray;
                if (models != null)
                {
                    foreach (JToken item in models)
                    {
                        string name = (string)item["name"];
                        if (string.IsNullOrEmpty(name)) continue;

                        if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                        {
                            name = name.Substring(7);
                        }

                        string lower = name.ToLowerInvariant();
                        bool isImage = lower.Contains("imagen") || lower.Contains("image");

                        JArray methods = item["supportedGenerationMethods"] as JArray;
                        if (methods != null)
                        {
                            foreach (JToken m in methods)
                            {
                                string sm = (string)m;
                                if (string.Equals(sm, "predict", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(sm, "imageGeneration", StringComparison.OrdinalIgnoreCase))
                                {
                                    isImage = true;
                                    break;
                                }
                            }
                        }

                        if (isImage && !result.Contains(name))
                        {
                            result.Add(name);
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            if (result.Count == 0)
            {
                result.Add("gemini-2.5-flash-image");
                result.Add("gemini-3.1-flash-image");
                result.Add("gemini-3-pro-image");
                result.Add("imagen-3.0-generate-002");
            }
            else
            {
                result.Sort((a, b) =>
                {
                    if (a.IndexOf("gemini-2.5-flash-image", StringComparison.OrdinalIgnoreCase) >= 0) return -1;
                    if (b.IndexOf("gemini-2.5-flash-image", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                    return StringComparer.OrdinalIgnoreCase.Compare(a, b);
                });
            }

            return result;
        }
    }
}
