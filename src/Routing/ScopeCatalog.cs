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
                ScopeId.MemoryBook, "MemoryBook", null,
                new[] { "AIInfluence.MemorySystem" },
                new string[0]),

            new ScopeDescriptor(
                ScopeId.BattleTactics, "BattleTactics",
                "battle-tactics.txt",
                new string[0], new string[0]),

            new ScopeDescriptor(
                ScopeId.GroupConversation, "GroupConversation",
                "group-conversations.txt",
                new string[0], new string[0]),

            new ScopeDescriptor(
                ScopeId.UniqueCharacters, "UniqueCharacters",
                "unique-characters.txt",
                new string[0], new string[0]),
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
