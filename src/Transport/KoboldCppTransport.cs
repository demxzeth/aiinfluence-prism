using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Config;
using AIInfluencePrism.Post;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// KoboldCpp: POST {root}/api/v1/generate. Completion-style API:
    /// the chat collapses into a single prompt text. The token limit
    /// there is called max_length.
    /// </summary>
    public sealed class KoboldCppTransport : HttpChatTransport
    {
        public override ProviderKind Kind
        {
            get { return ProviderKind.KoboldCpp; }
        }

        public override string EndpointUrl(Profile profile)
        {
            return EndpointMapper.KoboldGenerateUrl(profile.EffectiveRoot());
        }

        public override string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile)
        {
            StringBuilder prompt = new StringBuilder();
            if (!string.IsNullOrEmpty(turn.System))
            {
                prompt.AppendLine(turn.System);
                prompt.AppendLine();
            }

            prompt.Append(turn.User);

            JObject body = new JObject
            {
                { "prompt", prompt.ToString() },
                { "temperature", options.Temperature }
            };

            // 0 = no limit: omit the token field so the provider uses its default.
            if (options.MaxTokens > 0)
            {
                body["max_length"] = options.MaxTokens;
            }

            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        public override void ConfigureRequest(HttpRequestMessage request, Profile profile)
        {
            // KoboldCpp usually needs no authentication.
        }

        public override string ParseReply(string rawBody)
        {
            return ReplyReader.ReadJson(rawBody, "results[0].text");
        }

        protected override string ProbeUrl(Profile profile)
        {
            return EndpointMapper.KoboldModelUrl(profile.EffectiveRoot());
        }
    }
}