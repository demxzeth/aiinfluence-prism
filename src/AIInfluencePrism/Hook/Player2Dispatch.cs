using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Neutralizes all Player2 local HTTP calls and RAG embedding operations.
    /// When Player2 local server is not running, vanilla AIInfluence would stall for 10 seconds
    /// on http://localhost:5000/v1/embeddings and throw an unhandled exception in
    /// PromptGenerator.PrepareForPromptAsync / RagQueryEmbeddingCache.PrepareAsync, crashing
    /// dialogue before AIClient.GetAIResponse is ever reached.
    /// </summary>
    public static class Player2Dispatch
    {
        private static bool _p2Patched;
        private static bool _ragPatched;

        public static bool Installed => _p2Patched && _ragPatched;

        public static void Reset()
        {
            _p2Patched = false;
            _ragPatched = false;
        }

        // ==========================================
        // Harmony Prefixes for Player2Client
        // ==========================================

        public static bool CreateEmbeddingsAsyncPrefix(ref Task<float[][]> __result)
        {
            return CreateEmbeddingsAsyncPrefix(null, ref __result);
        }

        public static bool CreateEmbeddingsAsyncPrefix(IReadOnlyList<string> inputs, ref Task<float[][]> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    int count = inputs != null ? inputs.Count : 0;
                    float[][] dummy = new float[count][];
                    for (int i = 0; i < count; i++)
                    {
                        dummy[i] = new float[0];
                    }
                    __result = Task.FromResult(dummy);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: CreateEmbeddingsAsync prefix error", ex);
            }
            return true;
        }

        public static bool GetLlmModelIdsAsyncPrefix(ref Task<HashSet<string>> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: GetLlmModelIdsAsync prefix error", ex);
            }
            return true;
        }

        public static bool ResolvePreferredModelAsyncPrefix(ref Task<string> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult("default");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: ResolvePreferredModelAsync prefix error", ex);
            }
            return true;
        }

        public static bool SendHeartbeatAsyncPrefix(ref Task<bool> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: SendHeartbeatAsync prefix error", ex);
            }
            return true;
        }

        public static bool StartHeartbeatTimerPrefix()
        {
            if (ProfileBook.Current.MasterEnabled)
            {
                return false; // Suppress heartbeat timer
            }
            return true;
        }

        public static bool TestConnectionPrefix(ref Task<bool> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: TestConnection prefix error", ex);
            }
            return true;
        }

        public static bool GenerateAndPlayTTSPrefix(ref Task<bool> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: GenerateAndPlayTTS prefix error", ex);
            }
            return true;
        }

        public static bool GenerateTTSAudioBytesAsyncPrefix(ref Task<byte[]> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult<byte[]>(null);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: GenerateTTSAudioBytesAsync prefix error", ex);
            }
            return true;
        }

        public static bool StartSpeechToTextAsyncPrefix(ref Task<bool> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(false);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: StartSpeechToTextAsync prefix error", ex);
            }
            return true;
        }

        public static bool StopSpeechToTextAsyncPrefix(ref Task<string> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult<string>(null);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: StopSpeechToTextAsync prefix error", ex);
            }
            return true;
        }

        // ==========================================
        // Harmony Prefixes for RagQueryEmbeddingCache
        // ==========================================

        public static bool RagPrepareAsyncPrefix(ref Task __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.CompletedTask;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: RagQueryEmbeddingCache.PrepareAsync prefix error", ex);
            }
            return true;
        }

        public static bool RagGetAsyncPrefix(ref Task<float[]> __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(new float[0]);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: RagQueryEmbeddingCache.GetAsync prefix error", ex);
            }
            return true;
        }

        public static bool RagGetPrefix(ref float[] __result)
        {
            try
            {
                if (ProfileBook.Current.MasterEnabled)
                {
                    __result = new float[0];
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("Player2Dispatch: RagQueryEmbeddingCache.Get prefix error", ex);
            }
            return true;
        }

        private static Type FindType(string fullName)
        {
            Assembly asm = TargetScanner.TargetAssembly;
            if (asm != null)
            {
                try
                {
                    Type t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }

            Type direct = Type.GetType(fullName, false);
            if (direct != null) return direct;

            return AccessTools.TypeByName(fullName);
        }

        public static void Install(Harmony harmony)
        {
            if (harmony == null || Installed) return;

            if (TargetScanner.TargetAssembly == null) return;

            // 1. Patch Player2Client methods
            if (!_p2Patched)
            {
                try
                {
                    Type p2Type = FindType("AIInfluence.API.Player2Client");
                    if (p2Type != null)
                    {
                        PatchPrefix(harmony, p2Type, "CreateEmbeddingsAsync", nameof(CreateEmbeddingsAsyncPrefix));
                        PatchPrefix(harmony, p2Type, "GetLlmModelIdsAsync", nameof(GetLlmModelIdsAsyncPrefix));
                        PatchPrefix(harmony, p2Type, "ResolvePreferredModelAsync", nameof(ResolvePreferredModelAsyncPrefix));
                        PatchPrefix(harmony, p2Type, "SendHeartbeatAsync", nameof(SendHeartbeatAsyncPrefix));
                        PatchPrefix(harmony, p2Type, "StartHeartbeatTimer", nameof(StartHeartbeatTimerPrefix));
                        PatchPrefix(harmony, p2Type, "TestConnection", nameof(TestConnectionPrefix));
                        PatchPrefix(harmony, p2Type, "GenerateAndPlayTTS", nameof(GenerateAndPlayTTSPrefix));
                        PatchPrefix(harmony, p2Type, "GenerateTTSAudioBytesAsync", nameof(GenerateTTSAudioBytesAsyncPrefix));
                        PatchPrefix(harmony, p2Type, "StartSpeechToTextAsync", nameof(StartSpeechToTextAsyncPrefix));
                        PatchPrefix(harmony, p2Type, "StopSpeechToTextAsync", nameof(StopSpeechToTextAsyncPrefix));
                        _p2Patched = true;
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Error($"[ERROR] [Hook/Player2Dispatch] Failed to hook Player2Client: {LogSink.FormatException(ex)}", ex);
                }
            }

            // 2. Patch RagQueryEmbeddingCache methods
            if (!_ragPatched)
            {
                try
                {
                    Type ragType = FindType("AIInfluence.RagSystem.RagQueryEmbeddingCache");
                    if (ragType != null)
                    {
                        PatchPrefix(harmony, ragType, "PrepareAsync", nameof(RagPrepareAsyncPrefix));
                        PatchPrefix(harmony, ragType, "GetAsync", nameof(RagGetAsyncPrefix));
                        PatchPrefix(harmony, ragType, "Get", nameof(RagGetPrefix));
                        _ragPatched = true;
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Error($"[ERROR] [Hook/Player2Dispatch] Failed to hook RagQueryEmbeddingCache: {LogSink.FormatException(ex)}", ex);
                }
            }
        }

        private static void PatchPrefix(Harmony harmony, Type type, string methodName, string prefixName)
        {
            try
            {
                MethodInfo m = AccessTools.Method(type, methodName);
                if (m == null)
                {
                    m = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                            .FirstOrDefault(x => x.Name == methodName);
                }

                if (m != null)
                {
                    MethodInfo prefix = null;
                    if (prefixName == nameof(CreateEmbeddingsAsyncPrefix))
                    {
                        prefix = typeof(Player2Dispatch).GetMethod(
                            prefixName,
                            new Type[] { typeof(IReadOnlyList<string>), typeof(Task<float[][]>).MakeByRefType() });
                    }
                    if (prefix == null)
                    {
                        prefix = AccessTools.Method(typeof(Player2Dispatch), prefixName);
                    }

                    if (prefix != null)
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(prefix));
                        LogSink.Always($"[Hook/Player2Dispatch] Patched method: {FormatMethodSignature(m)} [Prefix]. Status: SUCCESS");
                    }
                }
                else
                {
                    LogSink.Warn($"[Hook/Player2Dispatch] Method {type.FullName}.{methodName} not found.");
                }
            }
            catch (Exception ex)
            {
                LogSink.Error($"[Hook/Player2Dispatch] Failed to patch {type.FullName}.{methodName}", ex);
            }
        }

        private static string FormatMethodSignature(MethodInfo m)
        {
            if (m == null) return "null";
            string declaring = m.DeclaringType != null ? m.DeclaringType.FullName : "UnknownClass";
            string retType = m.ReturnType != null ? (m.ReturnType.FullName ?? m.ReturnType.Name) : "void";
            ParameterInfo[] pars = m.GetParameters();
            string args = pars != null && pars.Length > 0
                ? string.Join(", ", pars.Select(p => (p.ParameterType.FullName ?? p.ParameterType.Name) + " " + p.Name))
                : string.Empty;
            return declaring + "." + m.Name + "(" + args + ") -> " + retType;
        }
    }
}
