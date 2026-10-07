using System;

namespace AIInfluencePrism.Hook
{
    /// <summary>The role of the target method in the request pipeline.</summary>
    public enum TargetRole
    {
        /// <summary>Not our request.</summary>
        None,

        /// <summary>Dialogue entry point: GetAIResponse(npc, faction, prompt[, cache]).</summary>
        Dialogue,

        /// <summary>Raw text: GetRawTextResponse(...).</summary>
        Raw,

        /// <summary>Raw text with an explicit backend: GetRawTextResponseWithBackend(...).</summary>
        RawWithBackend,

        /// <summary>Direct call of a specific backend: Get&lt;Name&gt;Response(...).</summary>
        BackendDirect,
    }
}
