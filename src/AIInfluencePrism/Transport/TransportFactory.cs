using System;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Factory providing cached transport singletons by ProviderKind.
    /// </summary>
    public static class TransportFactory
    {
        private static readonly OpenAiTransport OpenAiPlain =
            new OpenAiTransport(ProviderKind.OpenAiCompat, stream: false);

        private static readonly OpenAiTransport OpenAiStream =
            new OpenAiTransport(ProviderKind.OpenAiSse, stream: true);

        private static readonly AnthropicTransport Anthropic = new AnthropicTransport();
        private static readonly GeminiTransport Gemini = new GeminiTransport();
        private static readonly OllamaTransport Ollama = new OllamaTransport();
        private static readonly KoboldCppTransport Kobold = new KoboldCppTransport();
        private static readonly Player2Transport Player2 = new Player2Transport();
        private static readonly AntigravityTransport Antigravity = new AntigravityTransport();

        static TransportFactory()
        {
            HttpChatTransport.EnsureTls12();
        }

        public static IChatTransport Resolve(ProviderKind kind)
        {
            switch (kind)
            {
                case ProviderKind.Nvidia:
                case ProviderKind.Custom:
                case ProviderKind.OpenAiCompat:
                    return OpenAiPlain;
                case ProviderKind.OpenAiSse:
                    return OpenAiStream;
                case ProviderKind.Anthropic:
                    return Anthropic;
                case ProviderKind.Gemini:
                    return Gemini;
                case ProviderKind.Ollama:
                    return Ollama;
                case ProviderKind.KoboldCpp:
                    return Kobold;
                case ProviderKind.Player2:
                    return Player2;
                case ProviderKind.Antigravity:
                    return Antigravity;
                default:
                    return Player2;
            }
        }
    }
}
