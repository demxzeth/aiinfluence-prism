using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Provider protocol contract.
    /// Supports streaming response callback via Action&lt;string&gt; onDelta
    /// and CancellationToken.
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
        void ConfigureRequest(HttpRequestMessage request, Profile profile);

        /// <summary>Extract the reply text from the HTTP body.</summary>
        string ParseReply(string rawBody);

        /// <summary>Send the request and return the reply text, optionally invoking onDelta for stream tokens.</summary>
        Task<string> SendAsync(ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken, Action<string> onDelta = null);

        /// <summary>Connection test.</summary>
        Task<bool> ProbeAsync(Profile profile, CancellationToken cancellationToken);
    }
}
