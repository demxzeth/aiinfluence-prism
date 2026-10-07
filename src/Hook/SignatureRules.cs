using System;
using System.Reflection;
using System.Threading.Tasks;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Signature recognition rules for AIInfluence.API.AIClient.
    /// Public method and type names of the target assembly are preserved —
    /// that is its public surface, which any overlay relies on.
    ///
    /// All rules are pure functions over ParameterInfo[]: covered by unit
    /// tests without launching the game, on mannequin classes with the
    /// same parameter shapes.
    /// </summary>
    public static class SignatureRules
    {
        /// <summary>Prompt-like parameter: a string or a payload object.</summary>
        public static bool LooksLikePrompt(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type == typeof(string))
            {
                return true;
            }

            return IsPayloadType(type);
        }

        /// <summary>Full name of the payload class of the target assembly.</summary>
        public const string PayloadTypeFullName = "AIInfluence.API.PromptPayload";

        /// <summary>Is it a payload object? We decide by structure (FullText), not by name.</summary>
        public static bool IsPayloadType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            return type.GetProperty("FullText", BindingFlags.Public | BindingFlags.Instance) != null
                && type.GetProperty("UserContent", BindingFlags.Public | BindingFlags.Instance) != null;
        }

        /// <summary>Determines the method's role by name and parameter shape.</summary>
        public static TargetRole Classify(MethodBase method)
        {
            if (method == null)
            {
                return TargetRole.None;
            }

            ParameterInfo[] p = method.GetParameters();
            string name = method.Name;

            // Primary entry points first — they outweigh the general rule.
            if (name == "GetAIResponse" && IsDialogueShape(p))
            {
                return TargetRole.Dialogue;
            }

            if (name == "GetRawTextResponse" && IsRawShape(p))
            {
                return TargetRole.Raw;
            }

            if (name == "GetRawTextResponseWithBackend" && IsWithBackendShape(p))
            {
                return TargetRole.RawWithBackend;
            }

            // Direct backend calls: Get&lt;Name&gt;Response with a prompt parameter.
            // The names are not known in advance (they depend on the backend
            // set of the target assembly), so we catch them by shape, not by list.
            if (name.Length > "GetResponse".Length
                && name.StartsWith("Get", StringComparison.Ordinal)
                && name.EndsWith("Response", StringComparison.Ordinal)
                && HasPromptParam(p)
                && ReturnsText(method))
            {
                return TargetRole.BackendDirect;
            }

            return TargetRole.None;
        }

        /// <summary>Dialogue shape: (npc, faction, prompt) or (npc, faction, prompt, cache) —
        /// prompt as a string or as a payload object.</summary>
        public static bool IsDialogueShape(ParameterInfo[] p)
        {
            if (p == null)
            {
                return false;
            }

            // String forms.
            if (p.Length == 3
                && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(string)
                && LooksLikePrompt(p[2].ParameterType))
            {
                return true;
            }

            if (p.Length == 4
                && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(string)
                && p[2].ParameterType == typeof(string)
                && p[3].ParameterType == typeof(int))
            {
                return true;
            }

            // Payload form: (npc, faction, payload).
            if (p.Length == 3
                && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(string)
                && IsPayloadType(p[2].ParameterType))
            {
                return true;
            }

            return false;
        }

        /// <summary>Raw shape: a prompt (string or payload) plus any utility tails.</summary>
        public static bool IsRawShape(ParameterInfo[] p)
        {
            if (p == null)
            {
                return false;
            }

            // (prompt)
            if (p.Length == 1 && (LooksLikePrompt(p[0].ParameterType) || IsPayloadType(p[0].ParameterType)))
            {
                return true;
            }

            // (prompt, string tag) and (payload, string label)
            if (p.Length == 2
                && (LooksLikePrompt(p[0].ParameterType) || IsPayloadType(p[0].ParameterType))
                && p[1].ParameterType == typeof(string))
            {
                return true;
            }

            // (prompt, int cache, string tag)
            if (p.Length == 3 && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(int)
                && p[2].ParameterType == typeof(string))
            {
                return true;
            }

            return false;
        }

        /// <summary>With-backend shape: a prompt (string or payload) + backend name + an optional tail.</summary>
        public static bool IsWithBackendShape(ParameterInfo[] p)
        {
            if (p == null)
            {
                return false;
            }

            // (prompt, backend) and (payload, backend)
            if (p.Length == 2
                && (LooksLikePrompt(p[0].ParameterType) || IsPayloadType(p[0].ParameterType))
                && p[1].ParameterType == typeof(string))
            {
                return true;
            }

            // (prompt, backend, tag) and (payload, backend, label)
            if (p.Length == 3
                && (LooksLikePrompt(p[0].ParameterType) || IsPayloadType(p[0].ParameterType))
                && p[1].ParameterType == typeof(string)
                && p[2].ParameterType == typeof(string))
            {
                return true;
            }

            // (prompt, backend, cache)
            if (p.Length == 3
                && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(string)
                && p[2].ParameterType == typeof(int))
            {
                return true;
            }

            return false;
        }

        /// <summary>Whether any of the parameters is prompt-like.</summary>
        public static bool HasPromptParam(ParameterInfo[] p)
        {
            if (p == null)
            {
                return false;
            }

            foreach (ParameterInfo parameter in p)
            {
                if (LooksLikePrompt(parameter.ParameterType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ReturnsText(MethodBase method)
        {
            MethodInfo info = method as MethodInfo;
            if (info == null)
            {
                return false;
            }

            return info.ReturnType == typeof(Task<string>) || info.ReturnType == typeof(string);
        }
    }
}
