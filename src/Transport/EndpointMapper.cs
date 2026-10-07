using System;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// URL auto-building: the user gives an API root (sometimes with
    /// /v1, sometimes a full address), and the mapper normalizes it to
    /// the provider's canonical endpoint. Full addresses are not
    /// re-processed.
    /// </summary>
    public static class EndpointMapper
    {
        /// <summary>OpenAI-compatible chat: POST {root}/v1/chat/completions.</summary>
        public static string ChatCompletionsUrl(string root)
        {
            string base_ = Normalize(root);
            if (EndsWith(base_, "/chat/completions"))
            {
                return base_;
            }

            if (EndsWith(base_, "/v1"))
            {
                return base_ + "/chat/completions";
            }

            return base_ + "/v1/chat/completions";
        }

        /// <summary>Base "/v1" for child endpoints (models, health, login).</summary>
        public static string V1BaseUrl(string root)
        {
            string base_ = Normalize(root);
            return EndsWith(base_, "/v1") ? base_ : base_ + "/v1";
        }

        /// <summary>Anthropic messages: POST {root}/v1/messages.</summary>
        public static string AnthropicMessagesUrl(string root)
        {
            string base_ = Normalize(root);
            if (EndsWith(base_, "/v1/messages"))
            {
                return base_;
            }

            if (EndsWith(base_, "/v1"))
            {
                return base_ + "/messages";
            }

            return base_ + "/v1/messages";
        }

        /// <summary>Gemini generateContent: POST {root}/v1beta/models/{model}:generateContent.</summary>
        public static string GeminiGenerateUrl(string root, string model, string apiKey)
        {
            string base_ = Normalize(root);
            if (!EndsWith(base_, "/v1beta"))
            {
                base_ += "/v1beta";
            }

            string name = string.IsNullOrEmpty(model) ? "gemini-2.0-flash" : model.Trim();
            string url = base_ + "/models/" + Uri.EscapeDataString(name) + ":generateContent";

            string key = apiKey == null ? string.Empty : apiKey.Trim();
            if (key.Length > 0)
            {
                url = WithQuery(url, "key=" + Uri.EscapeDataString(key));
            }

            return url;
        }

        /// <summary>Ollama chat: POST {root}/api/chat.</summary>
        public static string OllamaChatUrl(string root)
        {
            string base_ = Normalize(root);
            return EndsWith(base_, "/api/chat") ? base_ : base_ + "/api/chat";
        }

        /// <summary>Ollama model list: GET {root}/api/tags.</summary>
        public static string OllamaTagsUrl(string root)
        {
            return Normalize(root) + "/api/tags";
        }

        /// <summary>KoboldCpp generation: POST {root}/api/v1/generate.</summary>
        public static string KoboldGenerateUrl(string root)
        {
            string base_ = Normalize(root);
            return EndsWith(base_, "/api/v1/generate") ? base_ : base_ + "/api/v1/generate";
        }

        /// <summary>KoboldCpp current model: GET {root}/api/v1/model.</summary>
        public static string KoboldModelUrl(string root)
        {
            return Normalize(root) + "/api/v1/model";
        }

        /// <summary>Player2 health: GET {root}/v1/health.</summary>
        public static string Player2HealthUrl(string root)
        {
            return V1BaseUrl(root) + "/health";
        }

        /// <summary>Player2 auto-login: POST {root}/v1/login/web/{clientId}.</summary>
        public static string Player2LoginUrl(string root, string clientId)
        {
            return V1BaseUrl(root) + "/login/web/" + Uri.EscapeDataString(clientId ?? string.Empty);
        }

        /// <summary>Append a query parameter carefully (respecting an existing "?").</summary>
        public static string WithQuery(string url, string query)
        {
            if (url == null)
            {
                return query;
            }

            return url.IndexOf('?') >= 0 ? url + "&" + query : url + "?" + query;
        }

        private static string Normalize(string root)
        {
            string value = root == null ? string.Empty : root.Trim();
            if (value.Length == 0)
            {
                value = "http://127.0.0.1:4315";
            }

            return value.TrimEnd('/');
        }

        private static bool EndsWith(string value, string suffix)
        {
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
    }
}