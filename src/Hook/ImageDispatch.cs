using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.Hook
{
    /// <summary>
    /// Harmony hooks for image generation requests originating from AIInfluence.
    /// Intercepts:
    /// 1. Player2Client.GenerateImageAsync and GenerateImageWithReferencesAsync -> routes to Prism's ImagePipeline.
    /// 2. Player2Client.CheckHealthAsync -> returns true when Prism image generation is enabled so AIInfluence does not abort.
    /// 3. DialogueSceneImageService.IsImageGenerationAvailable -> returns true when Dialogue images are enabled in Prism.
    /// 4. DialogueSceneImageService.ShouldGenerate -> returns true when Dialogue images are enabled and requested in context.
    /// 5. DynamicEventImageService.GenerateAndAttachImageAsync -> generates dynamic event image via Prism, saves to disk, and attaches to DynamicEvent.
    /// </summary>
    public static class ImageDispatch
    {
        private static bool _p2Patched;
        private static bool _dialoguePatched;
        private static bool _eventsPatched;

        public static bool Installed => _p2Patched && _dialoguePatched && _eventsPatched;

        public static void Reset()
        {
            _p2Patched = false;
            _dialoguePatched = false;
            _eventsPatched = false;
        }

        public static bool GenerateImagePrefix(
            string prompt, int width, int height, string logSource, ref Task<byte[]> __result)
        {
            try
            {
                if (!Config.ProfileBook.Current.MasterEnabled)
                {
                    return true;
                }

                // Check for concurrent pre-warmed dialogue image task
                if (DialogueSceneCoordinator.TryTakePendingImageTask(out Task<byte[]> prewarmedTask))
                {
                    LogSink.Always("image[Dialogue]: utilizing concurrent pre-warmed image generation task!");
                    __result = prewarmedTask;
                    return false;
                }

                Task<byte[]> task = ImagePipeline.GenerateImageAsync(
                    prompt, width, height, string.IsNullOrEmpty(logSource) ? "DialogueSceneImage" : logSource);
                if (task != null)
                {
                    __result = task;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogSink.Error("image dispatch failed; passing through.", ex);
                return true;
            }
        }

        public static bool GenerateImageWithReferencesPrefix(
            string prompt, IList<byte[]> referenceImagesPng, CancellationToken ct, string logSource, ref Task<byte[]> __result)
        {
            try
            {
                if (!Config.ProfileBook.Current.MasterEnabled)
                {
                    return true;
                }

                // Cache reference images for concurrent turns in this conversation
                if (referenceImagesPng != null && referenceImagesPng.Count > 0)
                {
                    object npc = DialogueSceneCoordinator.ResolveConversationHero();
                    string heroId = DialogueSceneCoordinator.GetHeroStringId(npc) ?? "default_dialogue";
                    DialogueSceneCoordinator.SetDialogueReferences(heroId, referenceImagesPng);
                }

                // Check for concurrent pre-warmed dialogue image task
                if (DialogueSceneCoordinator.TryTakePendingImageTask(out Task<byte[]> prewarmedTask))
                {
                    LogSink.Always("image[Dialogue]: utilizing concurrent pre-warmed image generation task!");
                    __result = prewarmedTask;
                    return false;
                }

                Task<byte[]> task = ImagePipeline.GenerateImageAsync(
                    prompt, 1024, 1024, string.IsNullOrEmpty(logSource) ? "DialogueSceneImage" : logSource, referenceImagesPng, ct);
                if (task != null)
                {
                    __result = task;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogSink.Error("image with references dispatch failed; passing through.", ex);
                return true;
            }
        }

        public static bool CheckHealthAsyncPrefix(ref Task<bool> __result)
        {
            try
            {
                if (Config.ProfileBook.Current.MasterEnabled)
                {
                    __result = Task.FromResult(true);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("health check dispatch failed; passing through.", ex);
            }
            return true;
        }

        public static void DialogueSceneImageService_IsImageGenerationAvailable_Postfix(ref bool __result)
        {
            try
            {
                if (Config.ProfileBook.Current.MasterEnabled &&
                    Config.ProfileBook.Current.Scope(ScopeId.Dialogue).GenerateImages)
                {
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("IsImageGenerationAvailable postfix error", ex);
            }
        }

        public static void DialogueSceneImageService_ShouldGenerate_Postfix(object context, ref bool __result)
        {
            try
            {
                if (Config.ProfileBook.Current.MasterEnabled &&
                    Config.ProfileBook.Current.Scope(ScopeId.Dialogue).GenerateImages)
                {
                    if (context != null)
                    {
                        PropertyInfo pi = context.GetType().GetProperty("RequestDialogueSceneImage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pi != null)
                        {
                            object val = pi.GetValue(context, null);
                            if (val is bool b && b)
                            {
                                __result = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("ShouldGenerate postfix error", ex);
            }
        }

        public static bool DynamicEventImageService_GenerateAndAttachImageAsync_Prefix(object dynamicEvent, ref Task __result)
        {
            try
            {
                if (!Config.ProfileBook.Current.MasterEnabled ||
                    !Config.ProfileBook.Current.Scope(ScopeId.Events).GenerateImages)
                {
                    return true;
                }

                if (dynamicEvent == null)
                {
                    return true;
                }

                __result = GenerateAndAttachDynamicEventImageAsync(dynamicEvent);
                return false;
            }
            catch (Exception ex)
            {
                LogSink.Error("DynamicEventImageService dispatch error; passing through", ex);
                return true;
            }
        }

        private static async Task GenerateAndAttachDynamicEventImageAsync(object dynamicEvent)
        {
            try
            {
                PropertyInfo imgPathProp = dynamicEvent.GetType().GetProperty("EventImagePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string existingPath = imgPathProp?.GetValue(dynamicEvent, null) as string;
                if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                {
                    return;
                }

                string targetPath = ResolveDynamicEventImagePath(dynamicEvent);
                if (string.IsNullOrEmpty(targetPath))
                {
                    LogSink.Warn("hooks[image]: could not resolve target path for dynamic event image.");
                    return;
                }

                if (File.Exists(targetPath))
                {
                    imgPathProp?.SetValue(dynamicEvent, targetPath, null);
                    return;
                }

                string prompt = BuildDynamicEventImagePrompt(dynamicEvent);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    LogSink.Warn("hooks[image]: prompt empty for dynamic event image.");
                    return;
                }

                byte[] imageBytes = await ImagePipeline.GenerateImageAsync(prompt, 1024, 1024, "DynamicEvents").ConfigureAwait(false);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    string dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllBytes(targetPath, imageBytes);
                    imgPathProp?.SetValue(dynamicEvent, targetPath, null);
                    LogSink.Always($"hooks[image]: dynamic event image saved to {targetPath}");
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("hooks[image]: failed to generate and attach dynamic event image", ex);
            }
        }

        private static string ResolveDynamicEventImagePath(object dynamicEvent)
        {
            try
            {
                Type deServiceType = FindType("AIInfluence.DynamicEvents.DynamicEventImageService");
                if (deServiceType != null)
                {
                    foreach (MethodInfo m in deServiceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (m.ReturnType == typeof(string))
                        {
                            ParameterInfo[] pars = m.GetParameters();
                            if (pars.Length == 1 && pars[0].ParameterType.Name == "DynamicEvent")
                            {
                                object res = m.Invoke(null, new object[] { dynamicEvent });
                                if (res is string s && !string.IsNullOrEmpty(s)) return s;
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                PropertyInfo idProp = dynamicEvent.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string id = idProp?.GetValue(dynamicEvent, null) as string ?? Guid.NewGuid().ToString("N");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string savePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "Modules", "AIInfluence", "save_data", "default_save", "event_images", id + ".png"));
                return savePath;
            }
            catch { }

            return null;
        }

        private static string BuildDynamicEventImagePrompt(object dynamicEvent)
        {
            try
            {
                Type pdeType = FindType("AIInfluence.PromptDynamicEventImageStorage");
                if (pdeType != null)
                {
                    MethodInfo mPrompt = pdeType.GetMethod("BuildImagePrompt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (mPrompt != null)
                    {
                        object res = mPrompt.Invoke(null, new object[] { dynamicEvent, null });
                        if (res is string s && !string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }
            catch { }

            try
            {
                PropertyInfo titleProp = dynamicEvent.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                PropertyInfo descProp = dynamicEvent.GetType().GetProperty("Description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                string title = titleProp?.GetValue(dynamicEvent, null) as string ?? string.Empty;
                string desc = descProp?.GetValue(dynamicEvent, null) as string ?? string.Empty;
                return $"Photorealistic scene in the world of Mount & Blade II: Bannerlord. Medieval Calradia. Event: {title}. {desc}. No text, no UI, photorealistic rendering.";
            }
            catch { }

            return null;
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

            // Early exit if AIInfluence assembly is not loaded yet in AppDomain
            if (TargetScanner.TargetAssembly == null) return;

            // 1. Player2Client hooks
            if (!_p2Patched)
            {
                try
                {
                    Type p2Type = FindType("AIInfluence.API.Player2Client");
                    if (p2Type != null)
                    {
                        MethodInfo m1 = AccessTools.Method(p2Type, "GenerateImageAsync", new[] { typeof(string), typeof(int), typeof(int), typeof(string) });
                        if (m1 != null)
                        {
                            MethodInfo prefix1 = AccessTools.Method(typeof(ImageDispatch), nameof(GenerateImagePrefix));
                            harmony.Patch(m1, prefix: new HarmonyMethod(prefix1));
                            LogSink.Always("hooks[image]: patched Player2Client.GenerateImageAsync successfully.");
                        }

                        MethodInfo m2 = AccessTools.Method(p2Type, "GenerateImageWithReferencesAsync", new[] { typeof(string), typeof(IList<byte[]>), typeof(CancellationToken), typeof(string) });
                        if (m2 != null)
                        {
                            MethodInfo prefix2 = AccessTools.Method(typeof(ImageDispatch), nameof(GenerateImageWithReferencesPrefix));
                            harmony.Patch(m2, prefix: new HarmonyMethod(prefix2));
                            LogSink.Always("hooks[image]: patched Player2Client.GenerateImageWithReferencesAsync successfully.");
                        }

                        MethodInfo mHealth = AccessTools.Method(p2Type, "CheckHealthAsync");
                        if (mHealth != null)
                        {
                            MethodInfo prefixHealth = AccessTools.Method(typeof(ImageDispatch), nameof(CheckHealthAsyncPrefix));
                            harmony.Patch(mHealth, prefix: new HarmonyMethod(prefixHealth));
                            LogSink.Always("hooks[image]: patched Player2Client.CheckHealthAsync successfully.");
                        }

                        _p2Patched = true;
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Warn("hooks[image]: failed to hook Player2Client: " + ex.Message);
                }
            }

            // 2. DialogueSceneImageService hooks
            if (!_dialoguePatched)
            {
                try
                {
                    Type dtType = FindType("AIInfluence.DialogueImages.DialogueSceneImageService");
                    if (dtType != null)
                    {
                        MethodInfo mAvail = AccessTools.Method(dtType, "IsImageGenerationAvailable");
                        if (mAvail != null)
                        {
                            MethodInfo postfixAvail = AccessTools.Method(typeof(ImageDispatch), nameof(DialogueSceneImageService_IsImageGenerationAvailable_Postfix));
                            harmony.Patch(mAvail, postfix: new HarmonyMethod(postfixAvail));
                            LogSink.Always("hooks[image]: patched DialogueSceneImageService.IsImageGenerationAvailable successfully.");
                        }

                        MethodInfo mShould = AccessTools.Method(dtType, "ShouldGenerate");
                        if (mShould != null)
                        {
                            MethodInfo postfixShould = AccessTools.Method(typeof(ImageDispatch), nameof(DialogueSceneImageService_ShouldGenerate_Postfix));
                            harmony.Patch(mShould, postfix: new HarmonyMethod(postfixShould));
                            LogSink.Always("hooks[image]: patched DialogueSceneImageService.ShouldGenerate successfully.");
                        }

                        _dialoguePatched = true;
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Warn("hooks[image]: failed to hook DialogueSceneImageService: " + ex.Message);
                }
            }

            // 3. DynamicEventImageService hooks
            if (!_eventsPatched)
            {
                try
                {
                    Type deType = FindType("AIInfluence.DynamicEvents.DynamicEventImageService");
                    if (deType != null)
                    {
                        MethodInfo mGenAttach = AccessTools.Method(deType, "GenerateAndAttachImageAsync");
                        if (mGenAttach != null)
                        {
                            MethodInfo prefixGenAttach = AccessTools.Method(typeof(ImageDispatch), nameof(DynamicEventImageService_GenerateAndAttachImageAsync_Prefix));
                            harmony.Patch(mGenAttach, prefix: new HarmonyMethod(prefixGenAttach));
                            LogSink.Always("hooks[image]: patched DynamicEventImageService.GenerateAndAttachImageAsync successfully.");
                        }

                        _eventsPatched = true;
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Warn("hooks[image]: failed to hook DynamicEventImageService: " + ex.Message);
                }
            }
        }
    }
}
