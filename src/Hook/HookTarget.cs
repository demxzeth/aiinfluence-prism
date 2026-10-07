using System.Reflection;

namespace AIInfluencePrism.Hook
{
    /// <summary>The target method with its role.</summary>
    public sealed class HookTarget
    {
        public HookTarget(MethodBase method, TargetRole role)
        {
            Method = method;
            Role = role;
        }

        public MethodBase Method { get; }

        public TargetRole Role { get; }
    }
}
