using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Config;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Provider protocol contract. Each protocol is its own class:
    /// a new provider = a new file, not a new switch.
    /// </summary>
    public interface IChatTransport
    {
        /// <summary>The provider kind this transport serves.</summary>
        ProviderKind Kind { get; }

        /// <summary>Full URL of the chat endpoint for the profile.</summary>
        string EndpointUrl(Profile profile);

        /// <summary>Request body (JSON) in the protocol's format.</summary>
        string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile);

        /// <summary>Headers/authentication on the prepared request.</summary>
        void ConfigureRequest(System.Net.Http.HttpRequestMessage request, Profile profile);

        /// <summary>
        /// Extract the reply text from the HTTP body (JSON or SSE stream).
        /// Returns null when the content cannot be found.
        /// </summary>
        string ParseReply(string rawBody);

        /// <summary>Send the request and return the reply text.</summary>
        Task<string> SendAsync(ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken);

        /// <summary>Connection test (the "check" button at E7).</summary>
        Task<bool> ProbeAsync(Profile profile, CancellationToken cancellationToken);
    }
}