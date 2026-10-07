namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// One intercepted AI call in chat form: system instruction
    /// (optional) + user text. The prompt arrives as a single
    /// block — it lands in User.
    /// </summary>
    public sealed class ChatTurn
    {
        public ChatTurn(string system, string user)
        {
            System = system;
            User = user ?? string.Empty;
        }

        /// <summary>System instruction or null.</summary>
        public string System { get; }

        /// <summary>The main request text.</summary>
        public string User { get; }

        /// <summary>From an intercepted prompt: no system part.</summary>
        public static ChatTurn FromPrompt(string prompt)
        {
            return new ChatTurn(null, prompt);
        }
    }
}