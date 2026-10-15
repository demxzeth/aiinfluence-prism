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

        /// <summary>Static background context part for multi-stage requests (null when no split).</summary>
        public string SplitSystem { get; set; }

        /// <summary>Dialogue history / active turn part for multi-stage requests (null when no split).</summary>
        public string SplitUser { get; set; }

        /// <summary>Whether this turn has a valid two-stage split.</summary>
        public bool HasSplit => !string.IsNullOrEmpty(SplitSystem) && !string.IsNullOrEmpty(SplitUser);

        /// <summary>From an intercepted prompt: no system part.</summary>
        public static ChatTurn FromPrompt(string prompt)
        {
            return new ChatTurn(null, prompt);
        }

        /// <summary>From an intercepted prompt with optional two-stage split.</summary>
        public static ChatTurn FromPrompt(string prompt, string splitSystem, string splitUser)
        {
            var turn = new ChatTurn(null, prompt);
            turn.SplitSystem = splitSystem;
            turn.SplitUser = splitUser;
            return turn;
        }
    }
}