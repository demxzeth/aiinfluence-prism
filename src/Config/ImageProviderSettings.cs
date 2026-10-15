using System;
using System.Collections.Generic;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Stored settings for an image generation provider (OpenAI, Google Gemini, Custom).
    /// </summary>
    public sealed class ImageProviderSettings
    {
        public string RootUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public List<string> AvailableModels { get; set; } = new List<string>();

        public ImageProviderSettings Clone()
        {
            return new ImageProviderSettings
            {
                RootUrl = this.RootUrl,
                ApiKey = this.ApiKey,
                Model = this.Model,
                AvailableModels = new List<string>(this.AvailableModels ?? new List<string>())
            };
        }

        public static ImageProviderSettings CreateDefault(string provider)
        {
            string p = (provider ?? string.Empty).ToLowerInvariant();
            if (p.Contains("gemini"))
            {
                return new ImageProviderSettings
                {
                    RootUrl = "https://generativelanguage.googleapis.com",
                    Model = "gemini-2.5-flash-image",
                    ApiKey = string.Empty
                };
            }
            if (p.Contains("free") || p.Contains("pollin"))
            {
                return new ImageProviderSettings
                {
                    RootUrl = "https://image.pollinations.ai",
                    Model = "flux",
                    ApiKey = string.Empty
                };
            }
            if (p.Contains("custom"))
            {
                return new ImageProviderSettings
                {
                    RootUrl = "http://127.0.0.1:5000",
                    Model = "flux-1-schnell",
                    ApiKey = string.Empty
                };
            }

            // OpenAI default
            return new ImageProviderSettings
            {
                RootUrl = "https://api.openai.com",
                Model = "dall-e-3",
                ApiKey = string.Empty
            };
        }
    }
}
