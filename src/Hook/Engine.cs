using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Hook lifecycle: one Harmony instance per session,
    /// installation with retries (the target assembly may load later),
    /// and unpatching on unload.
    ///
    /// At E1 the Engine could only locate the target type and report to
    /// the log; the concrete methods and prefixes are added by a single
    /// TargetScanner.CaptureTargets call (E3).
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

        /// <summary>The hook is installed.</summary>
        public static bool Installed
        {
            get { return _installed; }
        }

        /// <summary>How many attempts were spent.</summary>
        public static int Attempts
        {
            get { return _attempts; }
        }

        /// <summary>Create the Harmony instance. Called in OnSubModuleLoad.</summary>
        public static void Create()
        {
            if (_harmony == null)
            {
                _harmony = new Harmony(PrismInfo.HarmonyId);
            }
        }

        /// <summary>
        /// Installation attempt. It counts as successful when CaptureTargets
        /// returned at least one target method and all prefixes landed.
        /// </summary>
        public static void PrimeStep(float deltaTime)
        {
            if (_harmony == null || _installed)
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
            catch (Exception ex)
            {
                LogSink.Error("hook installation failed.", ex);
                _installed = true; // no retries on a broken installation.
            }
        }

        /// <summary>Unpatch (on mod unload).</summary>
        public static void TearDown()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(PrismInfo.HarmonyId);
                _harmony = null;
            }

            Installer.Reset();
            _installed = false;
            _attempts = 0;
            _nextAttemptIn = 0f;
        }
    }

    /// <summary>
    /// The installation itself: takes the target list from TargetScanner
    /// and puts the Dispatch.Prefix interceptor on each one.
    /// </summary>
    internal static class Installer
    {
        /// <summary>How many methods were patched last time.</summary>
        public static int PatchedCount { get; private set; }

        public static void Install(Harmony harmony)
        {
            PatchedCount = 0;

            IReadOnlyList<HookTarget> targets = TargetScanner.CaptureTargets();
            MethodInfo dispatch = typeof(Dispatch).GetMethod(
                nameof(Dispatch.Prefix), BindingFlags.Public | BindingFlags.Static);

            foreach (HookTarget target in targets)
            {
                // One prefix for all: Harmony will inject __originalMethod,
                // and the role is recovered by the Classify rule.
                harmony.Patch(target.Method, prefix: new HarmonyMethod(dispatch));
                PatchedCount++;
            }
        }

        public static void Reset()
        {
            PatchedCount = 0;
            TargetScanner.Reset();
        }
    }
}
