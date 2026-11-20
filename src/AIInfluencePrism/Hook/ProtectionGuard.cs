using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Neutralizes AIInfluence 7.0.6 internal self-protection / anti-tamper mechanisms.
    /// If AIInfluence detects hooks, it trips an internal lock flag (static int != 0)
    /// and cancels an internal CancellationTokenSource, causing all dialogue requests to stall or abort.
    /// 
    /// ProtectionGuard:
    /// 1. Hooks integrity scanner methods with SkipPrefix (return false)
    /// 2. Hooks lock and delivery guard methods with SuppressPrefix (return false)
    /// 3. In runtime watchdog tick: clears any active lock flag and replaces cancelled CancellationTokenSources
    /// 4. Provides self-healing verification: detects if AIInfluence stripped our patches and triggers reinstall
    /// </summary>
    public static class ProtectionGuard
    {
        private static readonly object Gate = new object();
        private static bool _applied;
        private static bool _resolved;
        private static bool _protectionPresent;

        private static Type _scannerType;
        private static Type _lockType;
        private static IReadOnlyList<MethodBase> _scannerMethods = Array.Empty<MethodBase>();
        private static MethodBase _lockMethod;
        private static FieldInfo _lockFlagField;
        private static FieldInfo _lockCancellationField;
        private static IReadOnlyList<MethodBase> _deliveryGuardMethods = Array.Empty<MethodBase>();

        private static readonly List<KeyValuePair<MethodBase, MethodInfo>> AppliedPatches =
            new List<KeyValuePair<MethodBase, MethodInfo>>();

        public static bool IsApplied
        {
            get { lock (Gate) { return _applied; } }
        }

        public static bool ProtectionPresent
        {
            get
            {
                EnsureResolved();
                return _protectionPresent;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                _applied = false;
                _resolved = false;
                _protectionPresent = false;
                _scannerType = null;
                _lockType = null;
                _scannerMethods = Array.Empty<MethodBase>();
                _lockMethod = null;
                _lockFlagField = null;
                _lockCancellationField = null;
                _deliveryGuardMethods = Array.Empty<MethodBase>();
                AppliedPatches.Clear();
            }
        }

        public static bool TryApply(Harmony harmony)
        {
            if (harmony == null) return false;

            lock (Gate)
            {
                if (_applied) return true;

                try
                {
                    EnsureResolved();
                    if (!_protectionPresent)
                    {
                        if (!_resolved) return false;
                        _applied = true;
                        return true;
                    }

                    MethodInfo skipPrefix = typeof(ProtectionPatch).GetMethod(
                        nameof(ProtectionPatch.SkipPrefix), BindingFlags.Public | BindingFlags.Static);
                    MethodInfo suppressPrefix = typeof(ProtectionPatch).GetMethod(
                        nameof(ProtectionPatch.SuppressPrefix), BindingFlags.Public | BindingFlags.Static);

                    if (skipPrefix == null || suppressPrefix == null)
                    {
                        LogSink.Error("[ProtectionGuard] Failed to find patch prefix methods.");
                        return false;
                    }

                    bool allOk = true;

                    // 1. Patch scanner methods
                    foreach (MethodBase scanner in _scannerMethods)
                    {
                        allOk &= TryRecordAndPatch(harmony, scanner, skipPrefix);
                    }

                    // 2. Patch lock method
                    if (_lockMethod != null)
                    {
                        TryRecordAndPatch(harmony, _lockMethod, suppressPrefix);
                    }

                    // 3. Patch delivery guards
                    foreach (MethodBase guard in _deliveryGuardMethods)
                    {
                        TryRecordAndPatch(harmony, guard, suppressPrefix);
                    }

                    _applied = allOk;
                    if (_applied)
                    {
                        LogSink.Always($"[ProtectionGuard] Anti-tamper bypass engaged: {AppliedPatches.Count} protection method(s) detoured.");
                    }
                    return _applied;
                }
                catch (Exception ex)
                {
                    LogSink.Error("[ProtectionGuard] TryApply encountered exception: " + ex.Message, ex);
                    return false;
                }
            }
        }

        public static bool VerifyStillApplied(Harmony harmony)
        {
            if (harmony == null) return true;

            lock (Gate)
            {
                if (!_applied || AppliedPatches.Count == 0) return true;

                foreach (KeyValuePair<MethodBase, MethodInfo> entry in AppliedPatches)
                {
                    try
                    {
                        Patches patchInfo = Harmony.GetPatchInfo(entry.Key);
                        if (patchInfo == null ||
                            patchInfo.Prefixes == null ||
                            !patchInfo.Prefixes.Any(p => p.owner == PrismInfo.HarmonyId && p.PatchMethod == entry.Value))
                        {
                            LogSink.Warn($"[ProtectionGuard] Protection patch missing on: {entry.Key.DeclaringType?.Name}.{entry.Key.Name}");
                            return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public static bool Tick(Harmony harmony)
        {
            EnsureResolved();

            bool lockRepaired = EnsureTamperLockCleared();
            if (lockRepaired)
            {
                LogSink.Always("[ProtectionGuard] Active AIInfluence lock detected & neutralized! Reset lock flag to 0 and refreshed CancellationTokenSource.");
            }

            if (_applied && !VerifyStillApplied(harmony))
            {
                LogSink.Warn("[ProtectionGuard] Protection detours were stripped by AIInfluence; re-installation required.");
                return true; // signals reinstall needed
            }

            return false;
        }

        public static bool EnsureTamperLockCleared()
        {
            EnsureResolved();
            bool cleared = false;

            // 1. Primary lock flag in detected LockType
            if (_lockFlagField != null)
            {
                try
                {
                    object val = _lockFlagField.GetValue(null);
                    if (val is int num && num != 0)
                    {
                        _lockFlagField.SetValue(null, 0);
                        cleared = true;
                    }
                }
                catch { }
            }

            // 2. Reset cancellation sources
            if (ResetCancellationSources())
            {
                cleared = true;
            }

            // 3. Sweep AIClient and Player2Client static fields for any secondary lock flags
            try
            {
                Assembly asm = FindAIInfluenceAssembly();
                if (asm != null)
                {
                    Type[] sweepTypes = new[]
                    {
                        asm.GetType("AIInfluence.API.AIClient", throwOnError: false),
                        asm.GetType("AIInfluence.API.Player2Client", throwOnError: false),
                        _scannerType,
                        _lockType
                    };

                    const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                    foreach (Type st in sweepTypes)
                    {
                        if (st == null) continue;
                        FieldInfo[] fields = st.GetFields(staticFlags);
                        foreach (FieldInfo f in fields)
                        {
                            try
                            {
                                if (f.FieldType == typeof(int))
                                {
                                    string fn = f.Name.ToLowerInvariant();
                                    if (fn.Contains("lock") || fn.Contains("tamper") || fn.Contains("integrity") || fn.Contains("guard"))
                                    {
                                        if (f.GetValue(null) is int fVal && fVal != 0)
                                        {
                                            f.SetValue(null, 0);
                                            cleared = true;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            return cleared;
        }

        public static bool ResetCancellationSources()
        {
            bool reset = false;
            if (_lockCancellationField != null)
            {
                try
                {
                    if (_lockCancellationField.GetValue(null) is CancellationTokenSource cts && cts.IsCancellationRequested)
                    {
                        _lockCancellationField.SetValue(null, new CancellationTokenSource());
                        reset = true;
                    }
                }
                catch { }
            }

            try
            {
                Assembly asm = FindAIInfluenceAssembly();
                if (asm != null)
                {
                    Type[] sweepTypes = new[]
                    {
                        asm.GetType("AIInfluence.API.AIClient", throwOnError: false),
                        asm.GetType("AIInfluence.API.Player2Client", throwOnError: false),
                        _scannerType,
                        _lockType
                    };

                    const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (Type st in sweepTypes)
                    {
                        if (st == null) continue;
                        FieldInfo[] fields = st.GetFields(staticFlags);
                        foreach (FieldInfo f in fields)
                        {
                            try
                            {
                                if (f.FieldType == typeof(CancellationTokenSource))
                                {
                                    if (f.GetValue(null) is CancellationTokenSource cts && cts.IsCancellationRequested)
                                    {
                                        f.SetValue(null, new CancellationTokenSource());
                                        reset = true;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            return reset;
        }

        public static bool TryClearLock() => EnsureTamperLockCleared();

        private static bool TryRecordAndPatch(Harmony harmony, MethodBase target, MethodInfo prefix)
        {
            if (target == null || prefix == null) return false;
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                AppliedPatches.Add(new KeyValuePair<MethodBase, MethodInfo>(target, prefix));
                LogSink.Info($"[ProtectionGuard] Detoured protection method: {target.DeclaringType?.Name}.{target.Name}");
                return true;
            }
            catch (Exception ex)
            {
                LogSink.Warn($"[ProtectionGuard] Failed detouring {target.DeclaringType?.Name}.{target.Name}: {ex.Message}");
                return false;
            }
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;

            lock (Gate)
            {
                if (_resolved) return;

                try
                {
                    Assembly assembly = FindAIInfluenceAssembly();
                    if (assembly == null) return;

                    Type[] types = GetTypesSafely(assembly);
                    Type aiClient = types.FirstOrDefault(t => string.Equals(t.FullName, "AIInfluence.API.AIClient", StringComparison.Ordinal));
                    Type player2Client = types.FirstOrDefault(t => string.Equals(t.FullName, "AIInfluence.API.Player2Client", StringComparison.Ordinal));

                    if (aiClient == null || player2Client == null)
                    {
                        _resolved = true;
                        return;
                    }

                    Resolve(types, aiClient, player2Client);
                    _resolved = true;
                }
                catch (Exception ex)
                {
                    LogSink.Warn("[ProtectionGuard] EnsureResolved failed: " + ex.Message);
                }
            }
        }

        private static void Resolve(Type[] types, Type aiClient, Type player2Client)
        {
            Type scanner;
            Type[] watchedTypes = FindWatchedTypeArray(types, aiClient, player2Client, out scanner);
            if (scanner == null || watchedTypes == null)
            {
                LogSink.Info("[ProtectionGuard] No anti-tamper scanner array detected in AIInfluence.");
                return;
            }

            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodBase[] scannerMethods = scanner.GetMethods(flags)
                .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0 && HasBody(m))
                .Cast<MethodBase>()
                .ToArray();

            if (scannerMethods.Length < 2)
            {
                return;
            }

            MethodBase[] noInlineMethods = scannerMethods
                .Where(m => (SafeImplementationFlags(m) & MethodImplAttributes.NoInlining) != 0)
                .ToArray();

            if (scannerMethods.Length > 8 && noInlineMethods.Length >= 2)
            {
                scannerMethods = noInlineMethods;
            }

            Type lockType = FindLockType(watchedTypes);
            _scannerType = scanner;
            _scannerMethods = scannerMethods;
            _protectionPresent = true;
            _deliveryGuardMethods = FindDeliveryGuardMethods(types);

            if (lockType != null)
            {
                _lockType = lockType;
                MethodInfo[] voidStringMethods = lockType.GetMethods(flags)
                    .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string) && HasBody(m))
                    .ToArray();

                _lockMethod = voidStringMethods.FirstOrDefault(m => HasIntegrityViolationDefault(m.GetParameters()[0])) ??
                              voidStringMethods.FirstOrDefault();

                _lockFlagField = lockType.GetFields(flags).FirstOrDefault(f => f.FieldType == typeof(int));
                _lockCancellationField = lockType.GetFields(flags).FirstOrDefault(f => f.FieldType == typeof(CancellationTokenSource));

                LogSink.Always($"[ProtectionGuard] Resolved Protection: Scanner='{scanner.FullName}', LockType='{lockType.FullName}', LockMethod='{_lockMethod?.Name ?? "none"}', LockField='{_lockFlagField?.Name ?? "none"}'");
            }
        }

        private static Type[] FindWatchedTypeArray(Type[] types, Type aiClient, Type player2Client, out Type scanner)
        {
            scanner = null;
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (Type type in types)
            {
                if (!type.IsAbstract || !type.IsSealed || type.ContainsGenericParameters)
                {
                    continue;
                }

                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(flags);
                }
                catch
                {
                    continue;
                }

                foreach (FieldInfo field in fields)
                {
                    Type[] watched = ReadWatchedTypes(field);
                    if (watched != null && watched.Contains(aiClient) && watched.Contains(player2Client))
                    {
                        scanner = type;
                        return watched;
                    }
                }
            }

            return null;
        }

        private static Type[] ReadWatchedTypes(FieldInfo field)
        {
            try
            {
                Type fieldType = field.FieldType;
                if (fieldType == typeof(Type[]))
                {
                    return field.GetValue(null) as Type[];
                }

                if (fieldType == typeof(RuntimeTypeHandle[]))
                {
                    if (field.GetValue(null) is RuntimeTypeHandle[] handles && handles.Length > 0)
                    {
                        List<Type> list = new List<Type>(handles.Length);
                        foreach (RuntimeTypeHandle handle in handles)
                        {
                            try
                            {
                                Type t = Type.GetTypeFromHandle(handle);
                                if (t != null) list.Add(t);
                            }
                            catch { }
                        }
                        return list.Count > 0 ? list.ToArray() : null;
                    }
                }
            }
            catch { }
            return null;
        }

        private static Type FindLockType(Type[] watched)
        {
            if (watched == null || watched.Length == 0) return null;
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            return watched.FirstOrDefault(type =>
            {
                if (type == null || type.IsEnum || type.IsInterface) return false;
                try
                {
                    FieldInfo[] fields = type.GetFields(flags);
                    bool hasIntLock = fields.Any(f => f.FieldType == typeof(int));
                    bool hasCts = fields.Any(f => f.FieldType == typeof(CancellationTokenSource));
                    return hasIntLock && hasCts;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static IReadOnlyList<MethodBase> FindDeliveryGuardMethods(Type[] types)
        {
            List<MethodBase> list = new List<MethodBase>();
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (Type type in types)
            {
                try
                {
                    if (type.IsAbstract && type.IsSealed && !type.ContainsGenericParameters &&
                        type.GetFields(flags).Any(f =>
                        {
                            try
                            {
                                Type ft = f.FieldType;
                                return ft.IsGenericType &&
                                       ft.GetGenericTypeDefinition() == typeof(AsyncLocal<>) &&
                                       ft.GetGenericArguments()[0].Name.StartsWith("StrongBox", StringComparison.Ordinal);
                            }
                            catch { return false; }
                        }))
                    {
                        MethodInfo[] methods = type.GetMethods(flags);
                        bool hasIntRet = methods.Any(m => m.ReturnType == typeof(int) && m.GetParameters().Length == 0 && HasBody(m));
                        bool hasVoidRet = methods.Any(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0 && HasBody(m));
                        MethodInfo guard = methods.FirstOrDefault(m =>
                            m.ReturnType == typeof(void) &&
                            m.GetParameters().Length == 2 &&
                            m.GetParameters()[0].ParameterType == typeof(int) &&
                            m.GetParameters()[1].ParameterType == typeof(string) &&
                            HasBody(m));

                        if (hasIntRet && hasVoidRet && guard != null)
                        {
                            list.Add(guard);
                        }
                    }
                }
                catch { }
            }

            return list;
        }

        private static Assembly FindAIInfluenceAssembly()
        {
            Assembly target = TargetScanner.TargetAssembly;
            if (target != null) return target;

            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            {
                try
                {
                    return string.Equals(a.GetName().Name, "AIInfluence", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            });
        }

        private static bool HasBody(MethodBase method)
        {
            try { return method.GetMethodBody() != null; }
            catch { return false; }
        }

        private static MethodImplAttributes SafeImplementationFlags(MethodBase method)
        {
            try { return method.GetMethodImplementationFlags(); }
            catch { return MethodImplAttributes.IL; }
        }

        private static bool HasIntegrityViolationDefault(ParameterInfo parameter)
        {
            try
            {
                return parameter.HasDefaultValue &&
                       parameter.DefaultValue is string text &&
                       text.IndexOf("integrity", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static Type[] GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }

    /// <summary>
    /// Harmony detour prefixes for AIInfluence protection methods.
    /// </summary>
    public static class ProtectionPatch
    {
        public static bool SkipPrefix()
        {
            return false;
        }

        public static bool SuppressPrefix()
        {
            return false;
        }
    }
}
