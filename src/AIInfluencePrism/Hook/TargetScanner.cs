using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AIInfluencePrism.Logging;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Scans AIInfluence assembly for target methods to intercept in 7.0.6:
    /// 1. AIClient.StreamsResponses.get -> HookKind.SuppressStreaming (Silver Bullet 1)
    /// 2. AIClient.GetStreamingAIResponse (returns Task&lt;StreamedAIResponse&gt;) -> HookKind.Streaming (Silver Bullet 2)
    /// 3. AIClient string methods (GetAIResponse, GetRawTextResponse*, GetPlayer2Response*, etc.) -> HookKind.String (Silver Bullet 3)
    /// </summary>
    public static class TargetScanner
    {
        public const string EntryTypeFullName = "AIInfluence.API.AIClient";

        public static readonly string[] EntryTypeFullNames = new[]
        {
            "AIInfluence.API.AIClient",
            "AIInfluence.API.Player2Client"
        };

        private static Assembly _targetAssembly;
        private static Type _entryType;

        public static Assembly TargetAssembly
        {
            get { return _targetAssembly ?? (_targetAssembly = FindTargetAssembly()); }
        }

        public static Type EntryType
        {
            get { return _entryType ?? (_entryType = FindTypeByName(EntryTypeFullName)); }
        }

        public static IReadOnlyList<HookTarget> CaptureTargets()
        {
            const BindingFlags scan =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

            List<HookTarget> targets = new List<HookTarget>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string entryName in EntryTypeFullNames)
            {
                Type entry = FindTypeByName(entryName);
                if (entry == null)
                {
                    LogSink.Info("TargetScanner: " + entryName + " not loaded yet.");
                    continue;
                }

                try
                {
                    foreach (MethodInfo method in entry.GetMethods(scan))
                    {
                        HookKind? kind = null;
                        ParameterInfo[] pars = method.GetParameters();
                        bool hasPromptPayload = pars.Any(p => p.ParameterType.Name == "PromptPayload");

                        // 0. Silver Bullet 1: Check for StreamsResponses getter returning bool
                        if (method.Name == "get_StreamsResponses" && method.ReturnType == typeof(bool))
                        {
                            kind = HookKind.SuppressStreaming;
                        }
                        // 1. Check for GetStreamingAIResponse returning Task<StreamedAIResponse>
                        else if (method.Name == "GetStreamingAIResponse" &&
                            (method.ReturnType.Name.Contains("StreamedAIResponse") || method.ReturnType.FullName.Contains("StreamedAIResponse")))
                        {
                            kind = HookKind.Streaming;
                        }
                        // 2. All Task<string> methods starting with "Get" and containing "Response"
                        // Captures:
                        // - GetAIResponse (all overloads, including PromptPayload primary hero dialogue, and Player2Client.GetAIResponse)
                        // - GetRawTextResponse* (including GetRawTextResponseWithBackend, GetRawTextResponsePreferModels)
                        // - Direct backend calls: GetPlayer2Response, GetPlayer2RawResponse,
                        //   GetOpenRouterResponse, GetDeepSeekResponse, GetOllamaResponse, GetKoboldCppResponse, etc.
                        else if (method.ReturnType == typeof(Task<string>) &&
                                 method.Name.StartsWith("Get", StringComparison.Ordinal) &&
                                 method.Name.IndexOf("Response", StringComparison.Ordinal) >= 0)
                        {
                            if (hasPromptPayload)
                            {
                                if (pars.Length >= 4 &&
                                    pars.Any(p => string.Equals(p.Name, "npcName", StringComparison.OrdinalIgnoreCase)) &&
                                    pars.Any(p => string.Equals(p.Name, "faction", StringComparison.OrdinalIgnoreCase)) &&
                                    pars.Any(p => string.Equals(p.Name, "payload", StringComparison.OrdinalIgnoreCase)) &&
                                    pars.Any(p => string.Equals(p.Name, "player2Feature", StringComparison.OrdinalIgnoreCase)))
                                {
                                    kind = HookKind.AiResponse;
                                }
                                else
                                {
                                    kind = HookKind.RawPayload;
                                }
                            }
                            else
                            {
                                kind = HookKind.String;
                            }
                        }

                        if (kind == null)
                        {
                            continue;
                        }

                        string key = method.Module.ModuleVersionId + ":" + method.MetadataToken;
                        if (!seen.Add(key))
                        {
                            continue;
                        }

                        targets.Add(new HookTarget(method, kind.Value));
                        string paramList = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                        LogSink.Info($"TargetScanner: discovered {kind.Value} target: {entry.FullName}.{method.Name}({paramList}) -> {method.ReturnType.Name}");
                    }

                    // Direct property fallback for StreamsResponses getter
                    PropertyInfo streamsProp = entry.GetProperty("StreamsResponses", scan);
                    if (streamsProp != null)
                    {
                        MethodInfo getter = streamsProp.GetGetMethod(true);
                        if (getter != null)
                        {
                            string key = getter.Module.ModuleVersionId + ":" + getter.MetadataToken;
                            if (seen.Add(key))
                            {
                                targets.Add(new HookTarget(getter, HookKind.SuppressStreaming));
                                LogSink.Info($"TargetScanner: discovered SuppressStreaming target: {entry.FullName}.StreamsResponses.get -> bool");
                            }
                        }
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    LogSink.Error("TargetScanner: ReflectionTypeLoadException getting methods from " + entry.FullName, rtle);
                }
                catch (Exception ex)
                {
                    LogSink.Error("TargetScanner: Exception getting methods from " + entry.FullName, ex);
                }
            }

            LogSink.Info("TargetScanner: found " + targets.Count + " total target(s)");
            return targets;
        }

        public static void Reset()
        {
            _entryType = null;
            _targetAssembly = null;
        }

        public static Assembly FindTargetAssembly()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (string.Equals(assembly.GetName().Name, "AIInfluence", StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly;
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    LogSink.Error("TargetScanner: ReflectionTypeLoadException checking assembly " + assembly.FullName, rtle);
                }
                catch (Exception ex)
                {
                    LogSink.Warn("TargetScanner: Exception inspecting assembly " + assembly.FullName + ": " + ex.Message);
                }
            }

            return null;
        }

        public static Type FindTypeByName(string fullName)
        {
            Assembly asm = TargetAssembly;
            if (asm != null)
            {
                try
                {
                    // Run module constructor in case the assembly uses obfuscation/anti-tamper
                    System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(asm.ManifestModule.ModuleHandle);
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    LogSink.Error("TargetScanner: ReflectionTypeLoadException running module constructor", rtle);
                }
                catch (Exception ex)
                {
                    LogSink.Warn("TargetScanner: Exception running module constructor: " + ex.Message);
                }

                try
                {
                    Type fromAsm = asm.GetType(fullName, throwOnError: false);
                    if (fromAsm != null)
                    {
                        return fromAsm;
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    LogSink.Error("TargetScanner: ReflectionTypeLoadException getting " + fullName, rtle);
                }
                catch (TypeLoadException tle)
                {
                    LogSink.Error("TargetScanner: TypeLoadException getting " + fullName, tle);
                }
                catch (Exception ex)
                {
                    LogSink.Error("TargetScanner: Exception getting " + fullName, ex);
                }
            }

            try
            {
                Type direct = Type.GetType(fullName, throwOnError: false);
                if (direct != null) return direct;
            }
            catch { }

            try
            {
                return HarmonyLib.AccessTools.TypeByName(fullName);
            }
            catch { }

            return null;
        }
    }
}
