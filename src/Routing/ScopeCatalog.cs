using System;
using System.Collections.Generic;
using System.Linq;

namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Description of one scope as data: how it is recognized in the
    /// call stack and which marker file it reads. All declarative —
    /// adding a scope is one entry in the catalog, not a new settings class.
    /// </summary>
    public sealed class ScopeDescriptor
    {
        public ScopeDescriptor(
            ScopeId id,
            string title,
            string markerFile,
            string[] stackPrefixes,
            string[] typeHints)
        {
            Id = id;
            Title = title ?? id.ToString();
            MarkerFile = markerFile;              // null = the scope has no markers
            StackPrefixes = stackPrefixes ?? new string[0];
            TypeHints = typeHints ?? new string[0];
        }

        public ScopeId Id { get; }

        /// <summary>Human-readable name (for settings and chat).</summary>
        public string Title { get; }

        /// <summary>Marker file name in new-prompts-detection (or null).</summary>
        public string MarkerFile { get; }

        /// <summary>Namespace prefixes of stack frames that identify the scope.</summary>
        public string[] StackPrefixes { get; }

        /// <summary>Substrings of the frame's full type name (a fallback way to spot the scope).</summary>
        public string[] TypeHints { get; }
    }

    /// <summary>
    /// Scope catalog. The order in MatchOrder matters: the most specific
    /// marker scopes are checked first, so "unique characters" do not eat
    /// ordinary dialogues on accidental word overlaps.
    /// </summary>
    public static class ScopeCatalog
    {
        /// <summary>All scopes in canonical order.</summary>
        public static readonly ScopeDescriptor[] All =
        {
            new ScopeDescriptor(
                ScopeId.Dialogue, "Dialogue", null,
                new string[0], new string[0]),

            new ScopeDescriptor(
                ScopeId.Diplomacy, "Diplomacy", null,
                new[] { "AIInfluence.Diplomacy" },
                new[] { "KingdomStatement", "Diplom" }),

            new ScopeDescriptor(
                ScopeId.Events, "Events", null,
                new[] { "AIInfluence.DynamicEvents" },
                new[] { "DynamicEvent", "EventCreator" }),

            new ScopeDescriptor(
                ScopeId.MemoryBook, "Memory Book", null,
                new[] { "AIInfluence.MemoryBook", "AIInfluence.MemorySystem", "AIInfluence.Memory" },
                new[] { "MemoryBook", "MemoryEntry", "PromptMemory" }),

            new ScopeDescriptor(
                ScopeId.BattleTactics, "Battle Tactics",
                "battle-tactics.txt",
                new string[0], new string[0]),

            new ScopeDescriptor(
                ScopeId.GroupConversation, "Group Conversations",
                "group-conversations.txt",
                new string[0], new string[0]),

            new ScopeDescriptor(
                ScopeId.UniqueCharacters, "Unique Characters",
                "unique-characters.txt",
                new[] { "AIInfluence.Behaviors.UniqueRoleCharacters", "AIInfluence.PromptUniqueRoleCharacterStorage" },
                new[] { "UniqueRole", "UniqueCharacter" }),
        };

        /// <summary>Marker-scope check order: specific before general.</summary>
        public static readonly ScopeId[] MatchOrder =
        {
            ScopeId.UniqueCharacters,
            ScopeId.BattleTactics,
            ScopeId.GroupConversation,
        };

        public static ScopeDescriptor Get(ScopeId id)
        {
            return All.FirstOrDefault(d => d.Id == id);
        }

        /// <summary>Formats a ScopeId into a human-readable title with spaces.</summary>
        public static string FormatScopeName(ScopeId scope)
        {
            switch (scope)
            {
                case ScopeId.Dialogue: return "Dialogue";
                case ScopeId.Diplomacy: return "Diplomacy";
                case ScopeId.Events: return "Events";
                case ScopeId.MemoryBook: return "Memory Book";
                case ScopeId.BattleTactics: return "Battle Tactics";
                case ScopeId.GroupConversation: return "Group Conversations";
                case ScopeId.UniqueCharacters: return "Unique Characters";
                default: return scope.ToString();
            }
        }

        /// <summary>Normalizes unspaced scope identifiers (e.g. MemoryBook -> Memory Book).</summary>
        public static string NormalizeTitle(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string trimmed = name.Trim();
            if (string.Equals(trimmed, "MemoryBook", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Memory Book", StringComparison.OrdinalIgnoreCase))
                return "Memory Book";
            if (string.Equals(trimmed, "BattleTactics", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Battle Tactics", StringComparison.OrdinalIgnoreCase))
                return "Battle Tactics";
            if (string.Equals(trimmed, "GroupConversation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "GroupConversations", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Group Conversation", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Group Conversations", StringComparison.OrdinalIgnoreCase))
                return "Group Conversations";
            if (string.Equals(trimmed, "UniqueCharacters", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Unique Characters", StringComparison.OrdinalIgnoreCase))
                return "Unique Characters";
            return trimmed;
        }

        /// <summary>Descriptors that have a marker file, in MatchOrder.</summary>
        public static IEnumerable<ScopeDescriptor> MarkerScopes()
        {
            foreach (ScopeId id in MatchOrder)
            {
                ScopeDescriptor descriptor = Get(id);
                if (descriptor != null && descriptor.MarkerFile != null)
                {
                    yield return descriptor;
                }
            }
        }
    }
}
