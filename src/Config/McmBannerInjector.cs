using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Injects the official AIInfluence Prism banner into MCM's settings screen
    /// dynamically via Harmony and GauntletUI, without requiring external UI libraries.
    /// </summary>
    internal static class McmBannerInjector
    {
        private static bool _installed;
        private static TaleWorlds.TwoDimension.Texture _cachedTexture;
        private static bool _textureLoadAttempted;
        private static readonly object Lock = new object();

        /// <summary>
        /// Installs the Harmony patch on MCM's ModOptionsGauntletScreen if available.
        /// Safe to call multiple times; will only patch once.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            // Disabled: MCM v5 does not support images, and injecting an unmanaged TextureWidget
            // into Gauntlet UI causes an unmanaged access violation (0xc0000005) in TaleWorlds.Native.dll.
        }

        private static void OnFrameTickPostfix(object __instance)
        {
        }

        public static void HandleFrameTick(object screenInstance)
        {
        }

        /// <summary>
        /// Loads the banner texture from disk into a TaleWorlds Engine / TwoDimension Texture.
        /// </summary>
        public static TaleWorlds.TwoDimension.Texture GetBannerTexture()
        {
            if (_cachedTexture != null) return _cachedTexture;
            if (_textureLoadAttempted) return null;

            lock (Lock)
            {
                if (_cachedTexture != null || _textureLoadAttempted) return _cachedTexture;
                _textureLoadAttempted = true;

                try
                {
                    string root = ModuleLocator.Root;
                    string[] candidates = new[]
                    {
                        Path.Combine(root, "GUI", "prism_logo.png"),
                        Path.Combine(root, "GUI", "prism_banner.png"),
                        Path.Combine(root, "GUI", "prism_banner.jpg")
                    };

                    string found = null;
                    foreach (string path in candidates)
                    {
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            found = path;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(found))
                    {
                        LogSink.Warn("mcm[banner]: banner image not found on disk.");
                        return null;
                    }

                    string dir = Path.GetDirectoryName(found);
                    string filename = Path.GetFileName(found);

                    TaleWorlds.Engine.Texture engineTex = null;
                    try
                    {
                        engineTex = TaleWorlds.Engine.Texture.LoadTextureFromPath(filename, dir);
                    }
                    catch (Exception ex)
                    {
                        LogSink.Warn("mcm[banner]: LoadTextureFromPath(filename, dir) error: " + ex.Message);
                    }

                    if (engineTex == null)
                    {
                        try
                        {
                            engineTex = TaleWorlds.Engine.Texture.LoadTextureFromPath(found, string.Empty);
                        }
                        catch { }
                    }

                    if (engineTex == null)
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(found);
                            engineTex = TaleWorlds.Engine.Texture.CreateFromMemory(bytes);
                        }
                        catch (Exception ex)
                        {
                            LogSink.Warn("mcm[banner]: CreateFromMemory error: " + ex.Message);
                        }
                    }

                    if (engineTex != null)
                    {
                        var engineWrap = new TaleWorlds.Engine.GauntletUI.EngineTexture(engineTex);
                        _cachedTexture = new TaleWorlds.TwoDimension.Texture(engineWrap);
                        LogSink.Always("mcm[banner]: loaded texture successfully from " + found);
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Warn("mcm[banner]: failed to load banner texture: " + ex.Message);
                }

                return _cachedTexture;
            }
        }
    }

    /// <summary>
    /// Custom Gauntlet UI TextureWidget that exposes Texture setter to initialize with our banner.
    /// </summary>
    internal sealed class PrismBannerWidget : TextureWidget
    {
        public PrismBannerWidget(UIContext context, TaleWorlds.TwoDimension.Texture texture) : base(context)
        {
            Texture = texture;
        }

        public void UpdateTexture(TaleWorlds.TwoDimension.Texture texture)
        {
            Texture = texture;
        }
    }
}
