namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Scopes Prism 1.0.0 sorts AIInfluence 7.0.6 requests into.
    /// Each has its own provider settings and generation parameters.
    /// </summary>
    public enum ScopeId
    {
        None = 0,

        /// <summary>Dialogue with major heroes (player2Feature: "heroes").</summary>
        DialogueHeroes,

        /// <summary>Townsfolk and ambient background dialogues (player2Feature: "townsfolk", "background-talk").</summary>
        DialogueTownsfolk,

        /// <summary>Letters written by NPCs (player2Feature: "npc-letters").</summary>
        NpcLetters,

        /// <summary>NPC initiative actions and spontaneous decisions (player2Feature: "npc-initiative").</summary>
        NpcInitiative,

        /// <summary>Character traits and personality development (player2Feature: "character-development").</summary>
        CharacterDevelopment,

        /// <summary>Tactical battlefield decisions and orders (player2Feature: "battle-tactics").</summary>
        BattleTactics,

        /// <summary>Diplomacy and dynamic world events (player2Feature: "diplomacy", "dynamic-events").</summary>
        Diplomacy,

        /// <summary>NPC memories and memory book generation (player2Feature: "memory-book").</summary>
        MemoryBook
    }
}
