using System.Reflection;

namespace AIInfluencePrism.Hook
{
    public enum HookKind
    {
        Streaming,
        String,
        AiResponse,
        RawPayload,
        SuppressStreaming
    }

    /// <summary>
    /// Target method with hook classification (streaming vs string).
    /// </summary>
    public sealed class HookTarget
    {
        public HookTarget(MethodBase method, HookKind kind)
        {
            Method = method;
            Kind = kind;
        }

        public MethodBase Method { get; }
        public HookKind Kind { get; }
    }
}
