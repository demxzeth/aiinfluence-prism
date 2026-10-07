namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Directions (scopes) Prism sorts requests into.
    /// Each has its own provider settings and generation parameters.
    /// </summary>
    public enum ScopeId
    {
        /// <summary>Undefined (the stack probe found nothing).</summary>
        None = 0,

        /// <summary>Dialogues with NPCs.</summary>
        Dialogue,

        /// <summary>Ruler statements, wars and peace.</summary>
        Diplomacy,

        /// <summary>Dynamic world events.</summary>
        Events,

        /// <summary>Memory book and recollections.</summary>
        MemoryBook,

        /// <summary>Tactical orders in battle.</summary>
        BattleTactics,

        /// <summary>Group conversations (several scene participants).</summary>
        GroupConversation,

        /// <summary>Generation of unique AI characters.</summary>
        UniqueCharacters,
    }
}
