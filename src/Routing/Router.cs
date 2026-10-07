using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Request classification: which scope it belongs to.
    ///
    /// Two strategies, because there are two kinds of calls:
    ///  - ClassifyChatPrompt — the dialogue path. Markers in the text are
    ///    more specific, so they are checked first; the stack is the second
    ///    channel; the fallback is Dialogue.
    ///  - ClassifyPipelinePrompt — utility pipelines (diplomacy, events,
    ///    memory book). There the call stack is unambiguous and dominant:
    ///    markers in their prompts can overlap with ordinary text.
    ///
    /// No route throws: classification errors must degrade softly
    /// to Dialogue, not crash the game.
    /// </summary>
    public static class Router
    {
        public static ScopeId ClassifyChatPrompt(string prompt, out string matchedMarker)
        {
            if (MarkerTable.TryMatch(prompt, out ScopeId byMarker, out matchedMarker))
            {
                LogSink.Info("route[chat]: marker '" + matchedMarker + "' -> " + byMarker);
                return byMarker;
            }

            ScopeId byStack = CallSiteProbe.Probe();
            if (byStack != ScopeId.None)
            {
                LogSink.Info("route[chat]: stack -> " + byStack);
                return byStack;
            }

            LogSink.Info("route[chat]: fallback -> Dialogue");
            return ScopeId.Dialogue;
        }

        public static ScopeId ClassifyPipelinePrompt(string prompt, out string matchedMarker)
        {
            ScopeId byStack = CallSiteProbe.Probe();
            if (byStack != ScopeId.None)
            {
                matchedMarker = string.Empty;
                LogSink.Info("route[pipeline]: stack -> " + byStack);
                return byStack;
            }

            if (MarkerTable.TryMatch(prompt, out ScopeId byMarker, out matchedMarker))
            {
                LogSink.Info("route[pipeline]: marker '" + matchedMarker + "' -> " + byMarker);
                return byMarker;
            }

            LogSink.Info("route[pipeline]: fallback -> Dialogue");
            return ScopeId.Dialogue;
        }
    }
}
