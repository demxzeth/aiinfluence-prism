using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Pipeline for generating images across supported scopes (Dialogue and Events).
    /// Supports OpenAI (DALL-E), Google Gemini (Imagen 3), and Custom endpoints.
    /// </summary>
    public static class ImagePipeline
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        /// <summary>
        /// Generates an image for the given prompt, reporting progress in-game.
        /// Returns raw PNG/JPEG image bytes, or null if skipped/failed.
        /// </summary>
        public static async Task<byte[]> GenerateImageAsync(
            string prompt,
            int width,
            int height,
            string logSource,
            IList<byte[]> references = null,
            CancellationToken ct = default)
        {
            ScopeId scope = ResolveScope(logSource);

            try
            {
                if (!ProfileBook.Current.MasterEnabled)
                {
                    LogSink.Info($"image[{scope}]: master switch disabled; passing through.");
                    return null;
                }

                ScopeSettings scopeCfg = ProfileBook.Current.Scope(scope);
                if (scopeCfg == null || !scopeCfg.Enabled || !scopeCfg.GenerateImages)
                {
                    LogSink.Info($"image[{scope}]: image generation disabled in Prism; skipping.");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    LogSink.Warn($"image[{scope}]: empty prompt provided.");
                    return null;
                }

                Profile profile = ProfileBook.Current;
                string requestedProvider = scopeCfg.ImageProvider;
                ImageProviderSettings imageConfig = profile.GetEffectiveImageConfig(requestedProvider);

                string providerName = ResolveProviderName(requestedProvider, profile, scope);
                string model = !string.IsNullOrWhiteSpace(scopeCfg.ImageModel) ? scopeCfg.ImageModel.Trim() : imageConfig.Model;
                string rootUrl = !string.IsNullOrWhiteSpace(scopeCfg.ImageEndpoint) ? scopeCfg.ImageEndpoint.Trim() : imageConfig.RootUrl;
                string apiKey = imageConfig.ApiKey;

                if (scope == ScopeId.Dialogue && !IsReferenceCapableProvider(providerName, rootUrl))
                {
                    string msg = $"Provider '{providerName}' is not supported in dialogues: model is text-only without photo reference support. Photo references are required for accurate character generation.";
                    LogSink.Warn($"image[Dialogue]: {msg}; skipping generation.");
                    ChatNotices.Error(msg, scope.ToString());
                    return null;
                }

                string style = profile.ImageArtStyle ?? "Bannerlord Concept Art";
                string optimizedPrompt = DialogueSceneCoordinator.SanitizeAndEnrichPrompt(prompt, references);
                string styledPrompt = ApplyArtStyle(optimizedPrompt, style);

                LogSink.Always($"image[{scope}]: generating image via {providerName} (model: {model}, style: {style}). Prompt: {styledPrompt.Substring(0, Math.Min(styledPrompt.Length, 100))}...");
                ChatNotices.Show($"Generating image via {providerName}...", scope.ToString());

                Stopwatch sw = Stopwatch.StartNew();
                byte[] imageBytes = null;

                try
                {
                    imageBytes = await RequestImageByProviderAsync(providerName, rootUrl, model, styledPrompt, apiKey, references, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    bool isFree = providerName.IndexOf("free", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  providerName.IndexOf("pollin", StringComparison.OrdinalIgnoreCase) >= 0;

                    // Option 5: Auto-fallback to Free (Pollinations) if enabled and primary was not already Free.
                    // Dialogue scope does NOT fallback to text-to-image Free (Pollinations) as it lacks character reference support.
                    if (profile.ImageAutoFallbackFree && !isFree && scope != ScopeId.Dialogue)
                    {
                        LogSink.Warn($"image[{scope}]: {providerName} failed ({ex.Message}), attempting auto-fallback to Free (Pollinations)...");
                        ChatNotices.Show($"Primary provider failed. Falling back to Free generator...", scope.ToString(), 1.0f, 0.75f, 0.25f);

                        try
                        {
                            imageBytes = await RequestPollinationsImageAsync("https://image.pollinations.ai", "flux", styledPrompt, ct).ConfigureAwait(false);
                            if (imageBytes != null && imageBytes.Length > 0)
                            {
                                LogSink.Always($"image[{scope}]: fallback to Free generator succeeded ({imageBytes.Length} bytes).");
                            }
                        }
                        catch (Exception fbEx)
                        {
                            LogSink.Error($"image[{scope}]: fallback to Free generator also failed", fbEx);
                        }
                    }

                    if (imageBytes == null)
                    {
                        if (profile.ImageFailSilently)
                        {
                            LogSink.Warn($"image[{scope}]: image generation failed silently: {ex.Message}");
                        }
                        else
                        {
                            ChatNotices.Error("Image generation error: " + ex.Message, scope.ToString());
                        }
                        return null;
                    }
                }
                sw.Stop();

                if (imageBytes != null && imageBytes.Length > 0)
                {
                    int seconds = Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds));
                    LogSink.Always($"image[{scope}]: image generated successfully ({imageBytes.Length} bytes in {sw.ElapsedMilliseconds} ms).");
                    ChatNotices.Show($"Image generated in {seconds}s", scope.ToString());
                    return imageBytes;
                }
                else
                {
                    LogSink.Warn($"image[{scope}]: provider returned empty image bytes.");
                    if (!profile.ImageFailSilently)
                    {
                        ChatNotices.Error("Image generation returned empty response", scope.ToString());
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error($"image[{scope}]: unexpected image generation exception", ex);
                if (!ProfileBook.Current.ImageFailSilently)
                {
                    ChatNotices.Error("Image generation error: " + ex.Message, scope.ToString());
                }
                return null;
            }
        }

        /// <summary>
        /// Probes connection to active image provider with a test prompt.
        /// </summary>
        public static async Task ProbeImageGeneration()
        {
            try
            {
                Profile profile = ProfileBook.Current;
                string providerName = profile.ImageProvider ?? "OpenAI";
                ImageProviderSettings imageConfig = profile.GetEffectiveImageConfig(providerName);

                string model = imageConfig.Model;
                string rootUrl = imageConfig.RootUrl;
                string apiKey = imageConfig.ApiKey;

                string style = profile.ImageArtStyle ?? "Bannerlord Concept Art";
                string testPrompt = ApplyArtStyle("A medieval knight banner with a golden lion crest", style);

                ChatNotices.Show($"Sending test prompt to {providerName} ({model})...", "Image");
                Stopwatch sw = Stopwatch.StartNew();
                byte[] imageBytes = await RequestImageByProviderAsync(
                    providerName, rootUrl, model, testPrompt, apiKey, null, CancellationToken.None).ConfigureAwait(false);
                sw.Stop();

                if (imageBytes != null && imageBytes.Length > 0)
                {
                    int sec = Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds));
                    ChatNotices.Show($"Image Generation test OK: received {imageBytes.Length} bytes in {sec}s.", "Image", 0.35f, 0.85f, 0.35f);
                }
                else
                {
                    ChatNotices.Error("Image Generation test returned empty response.", "Image");
                }
            }
            catch (Exception ex)
            {
                ChatNotices.Error("Image Generation test failed: " + ex.Message, "Image");
            }
        }

        public static string ResolveProviderName(string requested, Profile profile, ScopeId scope = ScopeId.None)
        {
            if (string.IsNullOrEmpty(requested) || string.Equals(requested, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return profile.ImageProvider ?? "OpenAI";
            }
            return requested;
        }

        public static bool IsReferenceCapableProvider(string providerName, string rootUrl = null)
        {
            if (string.IsNullOrWhiteSpace(providerName)) return false;
            string lower = providerName.ToLowerInvariant();
            if (lower.Contains("gemini")) return true;
            if (!string.IsNullOrEmpty(rootUrl) && rootUrl.IndexOf(":predict", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static ScopeId ResolveScope(string logSource)
        {
            if (!string.IsNullOrEmpty(logSource))
            {
                if (logSource.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ScopeId.Events;
                }
                if (logSource.IndexOf("Dialogue", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ScopeId.Dialogue;
                }
                if (logSource.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ScopeId.MemoryBook;
                }
            }

            ScopeId probed = CallSiteProbe.Probe();
            return probed != ScopeId.None ? probed : ScopeId.Dialogue;
        }

        public static string ApplyArtStyle(string prompt, string style)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            string s = (style ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(s) || s.StartsWith("None", StringComparison.OrdinalIgnoreCase))
            {
                return prompt;
            }

            string modifier = string.Empty;
            if (s.IndexOf("Bannerlord", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                modifier = ", Mount & Blade Bannerlord concept art style, medieval digital painting, historical Calradia aesthetic, cinematic lighting, detailed, artstation trending";
            }
            else if (s.IndexOf("Oil", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                modifier = ", classical oil painting on canvas, medieval fine art, Rembrandt lighting, dramatic chiaroscuro, rich textures";
            }
            else if (s.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                modifier = ", gritty dark medieval aesthetic, grim historical realism, weathered armor, cold atmosphere, photorealistic textures";
            }
            else if (s.IndexOf("Tapestry", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                modifier = ", authentic medieval manuscript illumination, woodcut engraving, historical tapestry style, antique parchment";
            }
            else if (s.IndexOf("Photo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                modifier = ", photorealistic medieval portrait, highly detailed, sharp focus, 8k resolution, cinematic lighting";
            }

            if (!string.IsNullOrEmpty(modifier))
            {
                if (prompt.IndexOf(modifier, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return prompt.TrimEnd('.', ' ') + modifier;
                }
            }

            return prompt;
        }

        private static async Task<byte[]> RequestPollinationsImageAsync(
            string rootUrl, string model, string prompt, CancellationToken ct)
        {
            string root = !string.IsNullOrWhiteSpace(rootUrl) ? rootUrl.TrimEnd('/') : "https://image.pollinations.ai";
            string m = !string.IsNullOrWhiteSpace(model) ? model.Trim().ToLowerInvariant() : "flux";
            string encodedPrompt = Uri.EscapeDataString(prompt ?? string.Empty);

            int seed = new Random().Next(1, 999999);
            string url = $"{root}/prompt/{encodedPrompt}?model={m}&width=1024&height=1024&nologo=true&seed={seed}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                using (HttpResponseMessage response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException($"Pollinations HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
                    }

                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        return imageBytes;
                    }

                    throw new InvalidOperationException("Pollinations returned empty image response.");
                }
            }
        }

        private static async Task<byte[]> RequestImageByProviderAsync(
            string providerName, string rootUrl, string model, string prompt, string apiKey, IList<byte[]> references, CancellationToken ct)
        {
            string lower = (providerName ?? string.Empty).ToLowerInvariant();

            if (lower.Contains("free") || lower.Contains("pollin"))
            {
                return await RequestPollinationsImageAsync(rootUrl, model, prompt, ct).ConfigureAwait(false);
            }

            if (lower.Contains("gemini") || (!string.IsNullOrEmpty(rootUrl) && rootUrl.IndexOf(":predict", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return await RequestGeminiImageAsync(rootUrl, model, prompt, apiKey, references, ct).ConfigureAwait(false);
            }

            // OpenAI / Custom standard
            return await RequestOpenAiImageAsync(rootUrl, model, prompt, apiKey, ct).ConfigureAwait(false);
        }

        private static async Task<byte[]> RequestOpenAiImageAsync(
            string rootUrl, string model, string prompt, string apiKey, CancellationToken ct)
        {
            string endpoint = ResolveOpenAiEndpoint(rootUrl);

            var payload = new JObject
            {
                ["prompt"] = prompt,
                ["n"] = 1,
                ["size"] = "1024x1024",
                ["response_format"] = "b64_json"
            };

            if (!string.IsNullOrWhiteSpace(model))
            {
                payload["model"] = model.Trim();
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                using (HttpResponseMessage response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(FormatOpenAiError((int)response.StatusCode, response.ReasonPhrase, body));
                    }

                    JObject json = JObject.Parse(body);
                    JArray data = json["data"] as JArray;
                    if (data != null && data.Count > 0)
                    {
                        JToken first = data[0];
                        string b64 = first["b64_json"]?.Value<string>();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            return Convert.FromBase64String(b64);
                        }

                        string url = first["url"]?.Value<string>();
                        if (!string.IsNullOrEmpty(url))
                        {
                            return await HttpClient.GetByteArrayAsync(url).ConfigureAwait(false);
                        }
                    }

                    // Check if response has Gemini-like format
                    JArray preds = json["predictions"] as JArray;
                    if (preds != null && preds.Count > 0)
                    {
                        string predB64 = preds[0]["bytesBase64Encoded"]?.Value<string>()
                                      ?? preds[0]["image"]?["imageBytes"]?.Value<string>();
                        if (!string.IsNullOrEmpty(predB64))
                        {
                            return Convert.FromBase64String(predB64);
                        }
                    }

                    throw new InvalidOperationException("API response did not contain image data ('b64_json' or 'url').");
                }
            }
        }

        private static async Task<byte[]> RequestGeminiImageAsync(
            string rootUrl, string model, string prompt, string apiKey, IList<byte[]> references, CancellationToken ct)
        {
            string m = !string.IsNullOrWhiteSpace(model) ? model.Trim() : "gemini-2.5-flash-image";
            string root = !string.IsNullOrWhiteSpace(rootUrl) ? rootUrl.TrimEnd('/') : "https://generativelanguage.googleapis.com";

            bool isPredict = root.IndexOf(":predict", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             m.StartsWith("imagen", StringComparison.OrdinalIgnoreCase);

            string endpoint;
            JObject payload;

            if (isPredict)
            {
                if (root.IndexOf(":predict", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    endpoint = root;
                }
                else
                {
                    string baseApi = root.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/v1beta";
                    endpoint = $"{baseApi}/models/{m}:predict";
                }

                payload = new JObject
                {
                    ["instances"] = new JArray
                    {
                        new JObject
                        {
                            ["prompt"] = prompt
                        }
                    },
                    ["parameters"] = new JObject
                    {
                        ["sampleCount"] = 1,
                        ["aspectRatio"] = "1:1",
                        ["outputMimeType"] = "image/jpeg"
                    }
                };
            }
            else
            {
                if (root.IndexOf(":generateContent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    endpoint = root;
                }
                else
                {
                    string baseApi = root.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase) ? root : $"{root}/v1beta";
                    endpoint = $"{baseApi}/models/{m}:generateContent";
                }

                JArray partsArray = new JArray
                {
                    new JObject
                    {
                        ["text"] = prompt
                    }
                };

                // Multimodal support: attach character reference PNGs directly so Gemini actually sees the game characters
                if (references != null && references.Count > 0)
                {
                    foreach (byte[] refBytes in references)
                    {
                        if (refBytes != null && refBytes.Length > 0)
                        {
                            partsArray.Add(new JObject
                            {
                                ["inlineData"] = new JObject
                                {
                                    ["mimeType"] = "image/png",
                                    ["data"] = Convert.ToBase64String(refBytes)
                                }
                            });
                        }
                    }
                }

                payload = new JObject
                {
                    ["contents"] = new JArray
                    {
                        new JObject
                        {
                            ["parts"] = partsArray
                        }
                    },
                    ["generationConfig"] = new JObject
                    {
                        ["responseModalities"] = new JArray { "IMAGE" }
                    }
                };
            }

            if (!string.IsNullOrWhiteSpace(apiKey) && endpoint.IndexOf("key=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                string sep = endpoint.Contains("?") ? "&" : "?";
                endpoint = $"{endpoint}{sep}key={apiKey.Trim()}";
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey.Trim());
                }

                using (HttpResponseMessage response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(FormatGeminiError((int)response.StatusCode, response.ReasonPhrase, body, m));
                    }

                    return ParseGeminiResponse(body);
                }
            }
        }

        public static byte[] ParseGeminiResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException("Gemini returned empty response.");
            }

            JObject json = JObject.Parse(body);

            // 1. candidates[].content.parts[].inlineData.data (Gemini generateContent)
            JArray candidates = json["candidates"] as JArray;
            if (candidates != null && candidates.Count > 0)
            {
                JToken parts = candidates[0]?["content"]?["parts"];
                if (parts != null)
                {
                    foreach (JToken part in parts)
                    {
                        string inlineB64 = part["inlineData"]?["data"]?.Value<string>()
                                        ?? part["inline_data"]?["data"]?.Value<string>()
                                        ?? part["data"]?.Value<string>();
                        if (!string.IsNullOrEmpty(inlineB64))
                        {
                            return Convert.FromBase64String(inlineB64);
                        }
                    }

                    string textPart = parts[0]?["text"]?.Value<string>();
                    if (!string.IsNullOrEmpty(textPart))
                    {
                        throw new InvalidOperationException($"Gemini returned text instead of an image: {textPart}");
                    }
                }
            }

            // 2. predictions[].bytesBase64Encoded (Imagen predict)
            JArray preds = json["predictions"] as JArray;
            if (preds != null && preds.Count > 0)
            {
                string b64 = preds[0]["bytesBase64Encoded"]?.Value<string>()
                          ?? preds[0]["image"]?["imageBytes"]?.Value<string>();
                if (!string.IsNullOrEmpty(b64))
                {
                    return Convert.FromBase64String(b64);
                }
            }

            // 3. steps[].content[] (Interactions API)
            JArray steps = json["steps"] as JArray;
            if (steps != null && steps.Count > 0)
            {
                JArray stepContent = steps[0]?["content"] as JArray;
                if (stepContent != null)
                {
                    foreach (JToken item in stepContent)
                    {
                        if (string.Equals(item["type"]?.Value<string>(), "image", StringComparison.OrdinalIgnoreCase))
                        {
                            string dataB64 = item["data"]?.Value<string>();
                            if (!string.IsNullOrEmpty(dataB64))
                            {
                                return Convert.FromBase64String(dataB64);
                            }
                        }
                    }
                }
            }

            // 4. data[].b64_json (OpenAI / proxy compatibility)
            JArray data = json["data"] as JArray;
            if (data != null && data.Count > 0)
            {
                string b64 = data[0]["b64_json"]?.Value<string>();
                if (!string.IsNullOrEmpty(b64))
                {
                    return Convert.FromBase64String(b64);
                }
            }

            throw new InvalidOperationException("Gemini response did not contain image data.");
        }

        public static string FormatGeminiError(int statusCode, string reasonPhrase, string body, string model)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return $"Google Gemini HTTP {statusCode} {reasonPhrase}";
            }

            try
            {
                JObject json = JObject.Parse(body);
                JToken err = json["error"];
                if (err != null)
                {
                    string msg = err["message"]?.Value<string>() ?? string.Empty;
                    string status = err["status"]?.Value<string>() ?? string.Empty;

                    if (statusCode == 429 ||
                        string.Equals(status, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                        msg.IndexOf("limit: 0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("Free Tier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("free_tier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "Google Gemini: Free Tier has a limit of 0 requests/day for image generation. Enable Pay-As-You-Go billing in Google AI Studio, or switch Image Provider to OpenAI / Custom.";
                    }

                    if (statusCode == 404 || msg.IndexOf("not supported for predict", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return $"Google Gemini: model '{model}' does not support this method or was not found (404). Please re-fetch and select an active image model.";
                    }

                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        int newlineIdx = msg.IndexOf('\n');
                        string summary = newlineIdx > 0 ? msg.Substring(0, newlineIdx).Trim() : msg.Trim();
                        return $"Google Gemini error ({statusCode}): {summary}";
                    }
                }
            }
            catch
            {
            }

            string truncated = body.Length > 200 ? body.Substring(0, 200) + "..." : body;
            return $"Google Gemini HTTP {statusCode} {reasonPhrase}: {truncated}";
        }

        public static string FormatOpenAiError(int statusCode, string reasonPhrase, string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return $"Image API HTTP {statusCode} {reasonPhrase}";
            }

            try
            {
                JObject json = JObject.Parse(body);
                JToken err = json["error"];
                if (err != null)
                {
                    string msg = err["message"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        return $"Image API error ({statusCode}): {msg.Trim()}";
                    }
                }
            }
            catch
            {
            }

            string truncated = body.Length > 200 ? body.Substring(0, 200) + "..." : body;
            return $"Image API HTTP {statusCode} {reasonPhrase}: {truncated}";
        }

        private static string ResolveOpenAiEndpoint(string rootUrl)
        {
            if (string.IsNullOrWhiteSpace(rootUrl))
            {
                return "https://api.openai.com/v1/images/generations";
            }

            string root = rootUrl.Trim().TrimEnd('/');
            if (root.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return root + "/images/generations";
            }

            return root + "/v1/images/generations";
        }
    }
}
