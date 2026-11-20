using System;
using System.Collections.Generic;
using System.Linq;

namespace AIInfluencePrism.Routing
{
    public sealed class ScopeDescriptor
    {
        public ScopeDescriptor(ScopeId id, string title, string featureTag)
        {
            Id = id;
            Title = title ?? id.ToString();
            FeatureTag = featureTag;
        }

        public ScopeId Id { get; }
        public string Title { get; }
        public string FeatureTag { get; }
    }

    public static class ScopeCatalog
    {
        public static readonly ScopeDescriptor[] All =
        {
            new ScopeDescriptor(ScopeId.DialogueHeroes, "Dialogue (Heroes)", "heroes"),
            new ScopeDescriptor(ScopeId.DialogueTownsfolk, "Dialogue (Townsfolk)", "townsfolk"),
            new ScopeDescriptor(ScopeId.NpcLetters, "NPC Letters", "npc-letters"),
            new ScopeDescriptor(ScopeId.NpcInitiative, "NPC Initiative", "npc-initiative"),
            new ScopeDescriptor(ScopeId.CharacterDevelopment, "Character Development", "character-development"),
            new ScopeDescriptor(ScopeId.BattleTactics, "Battle Tactics", "battle-tactics"),
            new ScopeDescriptor(ScopeId.Diplomacy, "Diplomacy & Events", "diplomacy"),
            new ScopeDescriptor(ScopeId.MemoryBook, "Memory Book", "memory-book"),
        };

        public static ScopeDescriptor Get(ScopeId id)
        {
            return All.FirstOrDefault(d => d.Id == id);
        }

        public static string FormatScopeName(ScopeId scope)
        {
            ScopeDescriptor desc = Get(scope);
            return desc != null ? desc.Title : scope.ToString();
        }

        public static string NormalizeTitle(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string trimmed = name.Trim();

            if (string.Equals(trimmed, "GroupConversation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "group-conversation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "group_conversation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "group conversation", StringComparison.OrdinalIgnoreCase) ||
                trimmed.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Dialogue (Townsfolk)";
            }

            if (string.Equals(trimmed, "Diplomacy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Diplomacy & Events", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "diplomacy & events", StringComparison.OrdinalIgnoreCase))
            {
                return "Diplomacy & Events";
            }

            ScopeId resolved = FastRouter.MapFeature(trimmed);
            if (resolved != ScopeId.None)
            {
                return FormatScopeName(resolved);
            }

            return trimmed;
        }
    }
}
