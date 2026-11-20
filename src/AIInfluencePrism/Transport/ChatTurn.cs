using AIInfluence.API;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// One intercepted AI call in chat form: system instruction
    /// (optional) + user text.
    /// </summary>
    public sealed class ChatTurn
    {
        public ChatTurn(string system, string user, string prompt = null)
        {
            System = system;
            User = user ?? string.Empty;
            Prompt = prompt ?? (!string.IsNullOrEmpty(system) ? (system + "\n\n" + user) : user ?? string.Empty);
        }

        public string System { get; }
        public string User { get; }
        public string Prompt { get; }
        public ScopeId Scope { get; set; } = ScopeId.DialogueHeroes;
        public string SplitSystem { get; set; }
        public string SplitUser { get; set; }
        public bool HasSplit => !string.IsNullOrEmpty(SplitSystem) && !string.IsNullOrEmpty(SplitUser);

        public static ChatTurn FromPrompt(string prompt)
        {
            return new ChatTurn(null, prompt, prompt);
        }

        public static ChatTurn FromPrompt(string prompt, string splitSystem, string splitUser)
        {
            var turn = new ChatTurn(splitSystem, prompt, prompt);
            turn.SplitSystem = splitSystem;
            turn.SplitUser = splitUser;
            return turn;
        }

        public static ChatTurn FromPayload(PromptPayload payload)
        {
            if (string.IsNullOrEmpty(payload.FullText))
            {
                return new ChatTurn(null, string.Empty);
            }

            // Zero Mutation / Pure Passthrough: Send the ENTIRE prompt text
            // (full system prompt + context + user input) directly as one single unified prompt string!
            // Do not split, do not slice, do not search for static prefix length.
            return FromPrompt(payload.FullText);
        }
    }
}
