using System;
using System.Globalization;
using System.Reflection;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Infra
{
    /// <summary>
    /// In-game chat notices. Uses Taleworlds' InformationManager to show
    /// messages in the game UI feed. Falls back to the log in any
    /// situation where the UI bridge is unavailable (early load, menu,
    /// or when the DLL runs in unit tests).
    ///
    /// The prefix matches the format specified in the v0.1 plan
    /// ("[Prism - &lt;scope&gt;] Timer: 156s").
    /// </summary>
    public static class ChatNotices
    {
        /// <summary>Show a message in the game UI feed, falling back to the log.</summary>
        /// <param name="text">Message text.</param>
        /// <param name="scope">Scope name (appears in the prefix and sets signature color).</param>
        /// <param name="r">Red channel for the UI colour (0-1), or null for default.</param>
        /// <param name="g">Green channel for the UI colour (0-1), or null for default.</param>
        /// <param name="b">Blue channel for the UI colour (0-1), or null for default.</param>
        public static void Show(string text, string scope = null,
            float? r = null, float? g = null, float? b = null)
        {
            string cleanPrefix = FormatPrefix(scope);
            string cleanMessage = cleanPrefix + text;

            if (!r.HasValue || !g.HasValue || !b.HasValue)
            {
                GetScopeColor(scope, out float sr, out float sg, out float sb);
                r = r ?? sr;
                g = g ?? sg;
                b = b ?? sb;
            }

            string hex = ToHexColor(r.Value, g.Value, b.Value);
            string styleName = "Prism." + (!string.IsNullOrEmpty(scope) ? scope.Replace(" ", "") : "Default");
            string formattedPrefix = "<span style=\"" + styleName + "\">" + cleanPrefix.TrimEnd() + "</span> ";
            string formattedMessage = formattedPrefix + text;

            TryDisplay(formattedMessage);
            LogSink.Always(cleanMessage);
        }

        /// <summary>Show an error-tinged message.</summary>
        public static void Error(string text, string scope = null,
            float? r = null, float? g = null, float? b = null)
        {
            string cleanPrefix = FormatPrefix(scope);
            string cleanMessage = cleanPrefix + text;

            string styleName = !string.IsNullOrEmpty(scope) ? ("Prism." + scope.Replace(" ", "")) : "Prism.Error";
            string formattedPrefix = "<span style=\"" + styleName + "\">" + cleanPrefix.TrimEnd() + "</span> ";
            string formattedMessage = formattedPrefix + text;

            TryDisplay(formattedMessage);
            LogSink.Warn(cleanMessage);
        }

        /// <summary>
        /// Distinct signature color for each scope in game chat notices:
        /// Dialogue = Green, Events = Yellow, Diplomacy = Blue,
        /// BattleTactics = Coral/Red, MemoryBook = Purple,
        /// GroupConversation = Cyan, UniqueCharacters = Orange,
        /// Antigravity = Soft Ice Blue.
        /// </summary>
        public static void GetScopeColor(string scope, out float r, out float g, out float b)
        {
            if (string.IsNullOrEmpty(scope))
            {
                r = 0.88f; g = 0.88f; b = 0.88f; // Soft white/gray
                return;
            }

            if (string.Equals(scope, "Dialogue", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.30f; g = 0.85f; b = 0.30f; // Green
            }
            else if (string.Equals(scope, "Events", StringComparison.OrdinalIgnoreCase))
            {
                r = 1.0f; g = 0.83f; b = 0.31f; // Yellow / Gold
            }
            else if (string.Equals(scope, "Diplomacy", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.26f; g = 0.65f; b = 0.96f; // Blue
            }
            else if (string.Equals(scope, "BattleTactics", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "Battle Tactics", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.94f; g = 0.33f; b = 0.33f; // Red / Coral
            }
            else if (string.Equals(scope, "MemoryBook", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "Memory Book", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.73f; g = 0.41f; b = 0.78f; // Purple
            }
            else if (string.Equals(scope, "GroupConversation", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "Group Conversations", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "Group Conversation", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.15f; g = 0.78f; b = 0.85f; // Cyan / Teal
            }
            else if (string.Equals(scope, "UniqueCharacters", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "Unique Characters", StringComparison.OrdinalIgnoreCase))
            {
                r = 1.0f; g = 0.65f; b = 0.15f; // Orange / Amber
            }
            else if (string.Equals(scope, "Image", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scope, "Images", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.95f; g = 0.45f; b = 0.75f; // Magenta / Rose
            }
            else if (string.Equals(scope, "Antigravity", StringComparison.OrdinalIgnoreCase))
            {
                r = 0.60f; g = 0.80f; b = 1.0f; // Soft Ice Blue
            }
            else
            {
                r = 0.88f; g = 0.88f; b = 0.88f; // Default
            }
        }

        /// <summary>Timer notification from the pipeline.</summary>
        public static void ShowTimer(string scope, long elapsedMs)
        {
            Show("Timer: " + FormatTimer(elapsedMs), scope);
        }

        /// <summary>
        /// Human-readable duration for the timer notice.
        /// Sub-second values: "850 ms"; seconds: plan format "156s" or "12.3s".
        /// </summary>
        public static string FormatTimer(long elapsedMs)
        {
            if (elapsedMs < 1000)
            {
                return elapsedMs + " ms";
            }

            double seconds = elapsedMs / 1000.0;
            return seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s";
        }

        public static string ToHexColor(float r, float g, float b)
        {
            byte red = (byte)Math.Max(0, Math.Min(255, (int)(r * 255f)));
            byte green = (byte)Math.Max(0, Math.Min(255, (int)(g * 255f)));
            byte blue = (byte)Math.Max(0, Math.Min(255, (int)(b * 255f)));
            return string.Format("{0:X2}{1:X2}{2:X2}FF", red, green, blue);
        }

        private static string FormatPrefix(string scope)
        {
            string prefix = "[" + PrismInfo.ChatPrefix;
            if (string.IsNullOrEmpty(scope))
            {
                return prefix + "] ";
            }
            string normalized = AIInfluencePrism.Routing.ScopeCatalog.NormalizeTitle(scope);
            return prefix + " - " + normalized + "] ";
        }

        private struct QueuedNotice
        {
            public string Message;
            public float? R;
            public float? G;
            public float? B;
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<QueuedNotice> PendingNotices =
            new System.Collections.Concurrent.ConcurrentQueue<QueuedNotice>();

        private static Type _infoManagerType;
        private static Type _infoMessageType;
        private static Type _colorType;
        private static MethodInfo _displayMessageMethod;
        private static bool _typesResolved;
        private static bool _stylesRegistered;

        /// <summary>
        /// Flushes queued chat notices to the TaleWorlds UI.
        /// Must be called periodically on the main thread (e.g. from OnApplicationTick).
        /// </summary>
        public static void FlushPending()
        {
            while (PendingNotices.TryDequeue(out QueuedNotice notice))
            {
                TryDisplayDirect(notice.Message, notice.R, notice.G, notice.B);
            }
        }

        private static void TryDisplay(string message, float? r = null, float? g = null, float? b = null)
        {
            PendingNotices.Enqueue(new QueuedNotice { Message = message, R = r, G = g, B = b });
        }

        private static Type FindType(string fullName)
        {
            Type t = Type.GetType(fullName, false);
            if (t != null) return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch
                {
                }
            }
            return null;
        }

        private static void EnsureTypes()
        {
            if (!_typesResolved)
            {
                try
                {
                    _infoManagerType = FindType("TaleWorlds.Library.InformationManager");
                    _infoMessageType = FindType("TaleWorlds.Library.InformationMessage");
                    _colorType = FindType("TaleWorlds.Library.Color");
                    if (_infoManagerType != null)
                    {
                        _displayMessageMethod = _infoManagerType.GetMethod(
                            "DisplayMessage", BindingFlags.Public | BindingFlags.Static);
                    }
                }
                catch
                {
                    // Reflection fallback handled quietly.
                }
                finally
                {
                    _typesResolved = true;
                }
            }

            EnsurePrismStyles();
        }

        private static void EnsurePrismStyles()
        {
            if (_stylesRegistered)
            {
                return;
            }

            try
            {
                Type uiResType = FindType("TaleWorlds.Engine.GauntletUI.UIResourceManager");
                if (uiResType == null) return;

                PropertyInfo factoryProp = uiResType.GetProperty("BrushFactory", BindingFlags.Public | BindingFlags.Static);
                if (factoryProp == null) return;
                object factory = factoryProp.GetValue(null);
                if (factory == null) return;

                MethodInfo getBrushMethod = factory.GetType().GetMethod("GetBrush", new[] { typeof(string) });
                if (getBrushMethod == null) return;

                object chatBrush = getBrushMethod.Invoke(factory, new object[] { "ChatLog.Text" });
                if (chatBrush == null) return;

                Type brushType = chatBrush.GetType();
                PropertyInfo layersProp = brushType.GetProperty("Layers");
                object layers = layersProp?.GetValue(chatBrush);

                Type styleType = FindType("TaleWorlds.GauntletUI.Style");
                if (styleType == null || layers == null) return;

                ConstructorInfo[] ctors = styleType.GetConstructors();
                if (ctors.Length == 0) return;
                ConstructorInfo styleCtor = ctors[0];

                MethodInfo getStyleMethod = brushType.GetMethod("GetStyle", new[] { typeof(string) });
                MethodInfo removeStyleMethod = brushType.GetMethod("RemoveStyle", new[] { typeof(string) });
                MethodInfo addStyleMethod = brushType.GetMethod("AddStyle", new[] { styleType });
                if (addStyleMethod == null) return;

                object defaultStyle = brushType.GetProperty("DefaultStyle")?.GetValue(chatBrush);
                PropertyInfo nameProp = styleType.GetProperty("Name");
                PropertyInfo defaultStyleProp = styleType.GetProperty("DefaultStyle");
                PropertyInfo fontColorProp = styleType.GetProperty("FontColor");
                FieldInfo fieldColor = styleType.GetField("_fontColor", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo fieldChanged = styleType.GetField("_isFontColorChanged", BindingFlags.NonPublic | BindingFlags.Instance);

                string[] scopes = new[] {
                    "Dialogue",
                    "Events",
                    "Diplomacy",
                    "BattleTactics",
                    "MemoryBook",
                    "GroupConversation",
                    "GroupConversations",
                    "UniqueCharacters",
                    "Image",
                    "Antigravity",
                    "Default",
                    "Error"
                };

                foreach (string scope in scopes)
                {
                    string styleName = "Prism." + scope;
                    if (getStyleMethod != null && removeStyleMethod != null)
                    {
                        object existing = getStyleMethod.Invoke(chatBrush, new object[] { styleName });
                        if (existing != null)
                        {
                            removeStyleMethod.Invoke(chatBrush, new object[] { styleName });
                        }
                    }

                    object style = styleCtor.Invoke(new[] { layers });
                    if (defaultStyle != null && defaultStyleProp != null)
                    {
                        defaultStyleProp.SetValue(style, defaultStyle);
                    }
                    nameProp?.SetValue(style, styleName);

                    GetScopeColor(scope == "Default" || scope == "Error" ? null : scope, out float r, out float g, out float b);
                    if (scope == "Error") { r = 0.94f; g = 0.33f; b = 0.33f; }

                    if (_colorType != null)
                    {
                        object color = Activator.CreateInstance(_colorType, new object[] { r, g, b, 1f });
                        if (fieldColor != null && fieldChanged != null)
                        {
                            fieldColor.SetValue(style, color);
                            fieldChanged.SetValue(style, true);
                        }
                        if (fontColorProp != null)
                        {
                            try { fontColorProp.SetValue(style, color); } catch { }
                        }
                    }

                    addStyleMethod.Invoke(chatBrush, new[] { style });
                }

                _stylesRegistered = true;
            }
            catch
            {
                // UIResourceManager may not be initialized yet during early load.
            }
        }

        private static void TryDisplayDirect(string message, float? r = null, float? g = null, float? b = null)
        {
            try
            {
                EnsureTypes();
                if (_infoManagerType == null || _infoMessageType == null)
                {
                    return;
                }

                object msg;
                if (_colorType != null)
                {
                    // Body text defaults to white (#FFFFFFFF), prefix is colored via embedded rich text tags.
                    object whiteColor = Activator.CreateInstance(
                        _colorType, new object[] { 1f, 1f, 1f, 1f });
                    msg = Activator.CreateInstance(
                        _infoMessageType, new object[] { message, whiteColor });
                }
                else
                {
                    msg = Activator.CreateInstance(_infoMessageType, new object[] { message });
                }

                if (_displayMessageMethod != null)
                {
                    _displayMessageMethod.Invoke(null, new[] { msg });
                }
                else
                {
                    _infoManagerType.InvokeMember("DisplayMessage",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.InvokeMethod,
                        null, null, new[] { msg });
                }
            }
            catch
            {
                // The game UI is not ready (early load / menu / unit tests);
                // the log line written by the caller carries the message.
            }
        }
    }
}