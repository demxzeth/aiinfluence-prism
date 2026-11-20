using System;
using System.Collections.Generic;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Fast deterministic routing engine for AIInfluence 7.0.6.
    /// Maps player2Feature and requestLabel directly to ScopeId without
    /// inspecting call stacks (StackTrace), CallSiteProbe, or scanning prompt texts (MarkerTable).
    /// </summary>
    public static class FastRouter
    {
        private static readonly Dictionary<string, ScopeId> FeatureMap = new Dictionary<string, ScopeId>(StringComparer.OrdinalIgnoreCase)
        {
            // Dialogue Heroes
            { "heroes", ScopeId.DialogueHeroes },
            { "dialogue", ScopeId.DialogueHeroes },
            { "dialogue-heroes", ScopeId.DialogueHeroes },
            { "dialogueheroes", ScopeId.DialogueHeroes },
            { "roleplay", ScopeId.DialogueHeroes },

            // Dialogue Townsfolk, Group Conversation & Background
            { "townsfolk", ScopeId.DialogueTownsfolk },
            { "background-talk", ScopeId.DialogueTownsfolk },
            { "backgroundtalk", ScopeId.DialogueTownsfolk },
            { "background", ScopeId.DialogueTownsfolk },
            { "next-speaker", ScopeId.DialogueTownsfolk },
            { "scene-fights", ScopeId.DialogueTownsfolk },
            { "groupconversation", ScopeId.DialogueTownsfolk },
            { "group-conversation", ScopeId.DialogueTownsfolk },
            { "group_conversation", ScopeId.DialogueTownsfolk },
            { "group", ScopeId.DialogueTownsfolk },
            { "dialogue-townsfolk", ScopeId.DialogueTownsfolk },
            { "dialoguetownsfolk", ScopeId.DialogueTownsfolk },

            // NPC Letters
            { "npc-letters", ScopeId.NpcLetters },
            { "npcletters", ScopeId.NpcLetters },
            { "letters", ScopeId.NpcLetters },
            { "letter", ScopeId.NpcLetters },

            // NPC Initiative
            { "npc-initiative", ScopeId.NpcInitiative },
            { "npcinitiative", ScopeId.NpcInitiative },
            { "initiative", ScopeId.NpcInitiative },

            // Character Development
            { "character-development", ScopeId.CharacterDevelopment },
            { "characterdevelopment", ScopeId.CharacterDevelopment },
            { "character", ScopeId.CharacterDevelopment },
            { "development", ScopeId.CharacterDevelopment },

            // Battle Tactics
            { "battle-tactics", ScopeId.BattleTactics },
            { "battletactics", ScopeId.BattleTactics },
            { "battle", ScopeId.BattleTactics },
            { "tactics", ScopeId.BattleTactics },

            // Diplomacy & Dynamic Events
            { "diplomacy", ScopeId.Diplomacy },
            { "diplomacy & events", ScopeId.Diplomacy },
            { "diplomacy & dynamic events", ScopeId.Diplomacy },
            { "diplomacy and events", ScopeId.Diplomacy },
            { "dynamic-events", ScopeId.Diplomacy },
            { "dynamicevents", ScopeId.Diplomacy },
            { "events", ScopeId.Diplomacy },
            { "event", ScopeId.Diplomacy },

            // Memory Book
            { "memory-book", ScopeId.MemoryBook },
            { "memorybook", ScopeId.MemoryBook },
            { "memory", ScopeId.MemoryBook }
        };

        /// <summary>
        /// Resolves ScopeId deterministically from player2Feature and optional requestLabel.
        /// </summary>
        public static ScopeId Resolve(string player2Feature, string requestLabel = null)
        {
            ScopeId resolved = ScopeId.None;
            if (!string.IsNullOrEmpty(player2Feature))
            {
                ScopeId mapped = MapFeature(player2Feature);
                if (mapped != ScopeId.None)
                {
                    resolved = mapped;
                }
            }

            if (resolved == ScopeId.None && !string.IsNullOrEmpty(requestLabel))
            {
                ScopeId mapped = MapFeature(requestLabel);
                if (mapped != ScopeId.None)
                {
                    resolved = mapped;
                }
            }

            if (resolved == ScopeId.None)
            {
                // Default fallback
                resolved = ScopeId.DialogueHeroes;
            }

            try
            {
                Profile profile = ProfileBook.Current;
                string profileName = profile != null ? (profile.Name ?? "default") : "default";
                bool masterEnabled = profile != null && profile.MasterEnabled;
                bool scopeEnabled = resolved == ScopeId.None || (profile != null && profile.Scope(resolved).Enabled);
                bool effectivelyEnabled = masterEnabled && scopeEnabled;
                LogSink.Info($"[Router] Mapped feature='{player2Feature ?? ""}' / label='{requestLabel ?? ""}' -> scope={resolved}, active_profile='{profileName}', enabled={effectivelyEnabled.ToString().ToLowerInvariant()}");
            }
            catch { }

            return resolved;
        }

        /// <summary>
        /// Pure string tag classifier using dictionary lookup.
        /// </summary>
        public static ScopeId MapFeature(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return ScopeId.None;
            }

            string key = tag.Trim();
            if (FeatureMap.TryGetValue(key, out ScopeId id))
            {
                return id;
            }

            string lower = key.ToLowerInvariant();
            if (FeatureMap.TryGetValue(lower, out id))
            {
                return id;
            }

            if (lower.Contains("roleplay"))
            {
                return ScopeId.DialogueHeroes;
            }

            if (lower.Contains("group") || lower.Contains("social_") || lower.Contains("multi_dialogue"))
            {
                return ScopeId.DialogueTownsfolk;
            }

            return ScopeId.None;
        }
    }
}
