using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using AIInfluence.API;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;
using AIInfluencePrism.UI;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Hook lifecycle: Harmony initialization, periodic priming attempts until AIInfluence is loaded,
    /// runtime watchdog for AIInfluence lock clearing & self-healing re-patching, and teardown upon mod unload.
    /// </summary>
    public static class Engine
    {
        /// <summary>Seconds between hook installation attempts.</summary>
        private const float PrimingIntervalSeconds = 1f;

        /// <summary>Fuse against endless attempts.</summary>
        private const int MaxPrimingAttempts = 60;

        private static Harmony _harmony;
        private static float _nextAttemptIn;
        private static int _attempts;
        private static bool _installed;
        private static float _protectionWatchSeconds;

        public static bool Installed => _installed;
        public static int Attempts => _attempts;
        public static Harmony HarmonyInstance => _harmony;

        public static void Create()
        {
            if (_harmony == null)
            {
                _harmony = new Harmony(PrismInfo.HarmonyId);
            }
            ImageDispatch.Install(_harmony);
            Player2Dispatch.Install(_harmony);
            HttpDispatch.Install(_harmony);
            EscapeMenuPatch.Install(_harmony);
            PrismWebUiHost.Start();
        }

        public static void PrimeStep(float deltaTime)
        {
            if (_harmony == null)
            {
                return;
            }

            // Continuous watchdog tick every 1.0 second:
            // 1. Clears any anti-tamper lock flag and refreshes cancelled CancellationTokenSources
            // 2. Detects if AIInfluence stripped our detours and triggers re-patching
            // 3. Periodically re-asserts HttpClient field replacement and sanitizes MCM settings
            _protectionWatchSeconds += deltaTime;
            if (_protectionWatchSeconds >= 1f)
            {
                _protectionWatchSeconds = 0f;

                try
                {
                    HttpDispatch.CheckAndReassertFields();
                    SettingsSanitizer.EnsureAiInfluenceKeys();
                }
                catch (Exception exSan)
                {
                    LogSink.Warn("[Engine] Periodic sanity check error: " + exSan.Message);
                }

                bool reinstallNeeded = false;
                try
                {
                    reinstallNeeded = ProtectionGuard.Tick(_harmony);
                }
                catch (Exception ex)
                {
                    LogSink.Warn("[Engine] ProtectionGuard.Tick error: " + ex.Message);
                }

                if (_installed && (reinstallNeeded || !Installer.VerifyStillApplied(_harmony)))
                {
                    LogSink.Always("[Engine] Hook loss or tamper lock detected! Re-installing Prism detours...");
                    ReinstallPatches();
                    return;
                }
            }

            if (_installed && ImageDispatch.Installed && Player2Dispatch.Installed && HttpDispatch.Installed)
            {
                return;
            }

            if (_attempts >= MaxPrimingAttempts)
            {
                return;
            }

            _nextAttemptIn -= deltaTime;
            if (_nextAttemptIn > 0f)
            {
                return;
            }

            _nextAttemptIn = PrimingIntervalSeconds;
            _attempts++;

            try
            {
                if (!ImageDispatch.Installed)
                {
                    ImageDispatch.Install(_harmony);
                }

                if (!Player2Dispatch.Installed)
                {
                    Player2Dispatch.Install(_harmony);
                }

                if (!HttpDispatch.Installed)
                {
                    HttpDispatch.Install(_harmony);
                }

                if (!EscapeMenuPatch.Installed)
                {
                    EscapeMenuPatch.Install(_harmony);
                }


                if (!_installed)
                {
                    Installer.Install(_harmony);
                    if (Installer.PatchedCount > 0)
                    {
                        _installed = true;
                        LogSink.Always("hooks installed: " + Installer.PatchedCount + " target method(s).");
                    }
                    else
                    {
                        LogSink.Info("priming attempt " + _attempts + ": target not ready yet.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Error($"[ERROR] [Hook/Engine] Hook installation failed: {LogSink.FormatException(ex)}", ex);
                _installed = true; // no retries on fatal installation error
            }
        }

        public static void ReinstallPatches()
        {
            if (_harmony == null) return;
            try
            {
                _installed = false;
                Installer.Reset();
                Installer.Install(_harmony);
                if (Installer.PatchedCount > 0)
                {
                    _installed = true;
                    LogSink.Always($"[Engine] Re-installed {Installer.PatchedCount} hook detours successfully.");
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("[Engine] ReinstallPatches failed: " + ex.Message, ex);
            }
        }

        public static void TearDown()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(PrismInfo.HarmonyId);
                _harmony = null;
            }

            try
            {
                Transport.AntigravityTransport.AbortAllRunningProcesses();
            }
            catch { }

            Installer.Reset();
            ProtectionGuard.Reset();
            ImageDispatch.Reset();
            Player2Dispatch.Reset();
            HttpDispatch.Reset();
            EscapeMenuPatch.Reset();
            DialogueStubReplacer.Reset();
            SettingsSanitizer.Reset();
            PrismWebUiHost.Stop();
            _installed = false;
            _attempts = 0;
            _nextAttemptIn = 0f;
            _protectionWatchSeconds = 0f;
        }
    }

    /// <summary>
    /// Installation helper: detours target methods with streaming, string, or suppression prefixes.
    /// Tracks all patched targets to provide self-healing verification.
    /// </summary>
    public static class Installer
    {
        private static readonly List<MethodBase> _appliedTargets = new List<MethodBase>();

        public static int PatchedCount { get; private set; }

        public static bool VerifyStillApplied(Harmony harmony)
        {
            if (harmony == null || _appliedTargets.Count == 0) return true;
            try
            {
                foreach (MethodBase m in _appliedTargets)
                {
                    Patches info = Harmony.GetPatchInfo(m);
                    if (info == null) return false;
                    bool hasOwner = (info.Prefixes != null && info.Prefixes.Any(p => p.owner == PrismInfo.HarmonyId)) ||
                                    (info.Postfixes != null && info.Postfixes.Any(p => p.owner == PrismInfo.HarmonyId));
                    if (!hasOwner) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Install(Harmony harmony)
        {
            PatchedCount = 0;
            _appliedTargets.Clear();

            // 1. First engage anti-tamper protection bypass before patching AIClient
            ProtectionGuard.TryApply(harmony);

            HttpDispatch.Install(harmony);
            IReadOnlyList<HookTarget> targets = TargetScanner.CaptureTargets();

            MethodInfo suppressPrefix = typeof(Dispatch).GetMethod(
                nameof(Dispatch.StreamsResponsesPrefix), BindingFlags.Public | BindingFlags.Static);
            MethodInfo suppressPostfix = typeof(Dispatch).GetMethod(
                nameof(Dispatch.StreamsResponsesPostfix), BindingFlags.Public | BindingFlags.Static);
            MethodInfo streamingPrefix = typeof(Dispatch).GetMethod(
                nameof(Dispatch.StreamingUntypedPrefix), BindingFlags.Public | BindingFlags.Static);
            MethodInfo stringPrefix = typeof(Dispatch).GetMethod(
                nameof(Dispatch.StringPrefix), BindingFlags.Public | BindingFlags.Static);

            foreach (HookTarget target in targets)
            {
                MethodBase m = target.Method;
                string sig = FormatMethodSignature(m);

                try
                {
                    if (target.Kind == HookKind.SuppressStreaming)
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(suppressPrefix), postfix: new HarmonyMethod(suppressPostfix));
                        PatchedCount++;
                        _appliedTargets.Add(m);
                        LogSink.Always($"[Hook/Installer] Patched method: {sig} [SuppressStreaming]. Status: SUCCESS");
                    }
                    else if (target.Kind == HookKind.Streaming)
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(streamingPrefix));
                        PatchedCount++;
                        _appliedTargets.Add(m);
                        LogSink.Always($"[Hook/Installer] Patched method: {sig} [Streaming/Untyped]. Status: SUCCESS");
                    }
                    else
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(stringPrefix));
                        PatchedCount++;
                        _appliedTargets.Add(m);
                        LogSink.Always($"[Hook/Installer] Patched method: {sig} [String/Universal]. Status: SUCCESS");
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Error($"[ERROR] [Hook/Installer] Failed to patch method: {sig}. Status: FAILED. Error details:\n{LogSink.FormatException(ex)}", ex);
                }
            }
        }

        public static string FormatMethodSignature(MethodBase m)
        {
            if (m == null) return "null";
            string declaring = m.DeclaringType != null ? m.DeclaringType.FullName : "UnknownClass";
            string retType = (m is MethodInfo mi && mi.ReturnType != null) ? (mi.ReturnType.FullName ?? mi.ReturnType.Name) : "void";
            ParameterInfo[] pars = m.GetParameters();
            string args = pars != null && pars.Length > 0
                ? string.Join(", ", pars.Select(p => (p.ParameterType.FullName ?? p.ParameterType.Name) + " " + p.Name))
                : string.Empty;
            return declaring + "." + m.Name + "(" + args + ") -> " + retType;
        }

        public static void Reset()
        {
            PatchedCount = 0;
            _appliedTargets.Clear();
            ProtectionGuard.Reset();
            HttpDispatch.Reset();
            TargetScanner.Reset();
        }
    }
}
