using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// In-game chat notices. Uses Taleworlds' InformationManager to show
    /// messages in the game UI feed. Falls back to the log in any
    /// situation where the UI bridge is unavailable.
    /// </summary>
    public static class ChatNotices
    {
        public static void Show(string text, string scope = null,
            float? r = null, float? g = null, float? b = null)
        {
            if (!string.IsNullOrEmpty(text) && text.StartsWith("[" + PrismInfo.ChatPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int closeBracket = text.IndexOf(']');
                if (closeBracket >= 0 && closeBracket < text.Length - 1)
                {
                    text = text.Substring(closeBracket + 1).TrimStart();
                }
            }

            string cleanPrefix = FormatPrefix(scope);
            string cleanMessage = cleanPrefix + text;

            if (!r.HasValue || !g.HasValue || !b.HasValue)
            {
                GetScopeColor(scope, out float sr, out float sg, out float sb);
                r = r ?? sr;
                g = g ?? sg;
                b = b ?? sb;
            }

            string styleName = "Prism." + (!string.IsNullOrEmpty(scope) ? scope.Replace(" ", "") : "Default");
            string formattedPrefix = "<span style=\"" + styleName + "\">" + cleanPrefix.TrimEnd() + "</span> ";
            string formattedMessage = formattedPrefix + text;

            TryDisplay(formattedMessage);
            LogSink.Always(cleanMessage);
        }

        public static void Error(string text, string scope = null,
            float? r = null, float? g = null, float? b = null)
        {
            if (!string.IsNullOrEmpty(text) && text.StartsWith("[" + PrismInfo.ChatPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int closeBracket = text.IndexOf(']');
                if (closeBracket >= 0 && closeBracket < text.Length - 1)
                {
                    text = text.Substring(closeBracket + 1).TrimStart();
                }
            }

            string cleanPrefix = FormatPrefix(scope);
            string cleanMessage = cleanPrefix + text;

            string styleName = !string.IsNullOrEmpty(scope) ? ("Prism." + scope.Replace(" ", "")) : "Prism.Error";
            string formattedPrefix = "<span style=\"" + styleName + "\">" + cleanPrefix.TrimEnd() + "</span> ";
            string formattedMessage = formattedPrefix + text;

            TryDisplay(formattedMessage);
            LogSink.Warn(cleanMessage);
        }

        public static void RequestFailed(string scope, int code, string detail)
        {
            string cleanPrefix = FormatPrefix(scope);
            string safeDetail = (detail ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");

            string cleanMessage = cleanPrefix + "Request failed (code " + code + "): " + detail;

            string scopeStyle = !string.IsNullOrEmpty(scope) ? ("Prism." + scope.Replace(" ", "")) : "Prism.Default";
            string formattedMessage = "<span style=\"" + scopeStyle + "\">" + cleanPrefix.TrimEnd() + "</span> "
                + "<span style=\"Prism.Error\">Request failed (code " + code + "):</span> "
                + safeDetail;

            TryDisplay(formattedMessage);
            LogSink.Warn(cleanMessage);
        }

        public static void GetScopeColor(string scope, out float r, out float g, out float b)
        {
            if (string.IsNullOrEmpty(scope))
            {
                r = 0.88f; g = 0.88f; b = 0.88f;
                return;
            }

            string s = scope.Trim().ToLowerInvariant();

            // 1. Dialogue (Heroes, Townsfolk & Group Conversation): Green (0.25f, 0.95f, 0.45f)
            if (s.Contains("heroes") || s.Contains("townsfolk") || s.Contains("group") || s.Contains("background") || s.Contains("dialogue"))
            {
                r = 0.25f; g = 0.95f; b = 0.45f;
            }
            // 3. NpcLetters: Sky Blue (0.35f, 0.75f, 1.00f)
            else if (s.Contains("letter") || s.Equals("npcletters"))
            {
                r = 0.35f; g = 0.75f; b = 1.00f;
            }
            // 4. Initiative & Dynamic Events: Amber/Yellow (1.00f, 0.82f, 0.20f)
            else if (s.Contains("initiative") || s.Equals("events") || s.Equals("dynamicevents"))
            {
                r = 1.00f; g = 0.82f; b = 0.20f;
            }
            // 5. CharacterDevelopment: Violet/Purple (0.75f, 0.40f, 0.95f)
            else if (s.Contains("character") || s.Contains("development") || s.Equals("characterdevelopment"))
            {
                r = 0.75f; g = 0.40f; b = 0.95f;
            }
            // 6. Tactics: Orange/Red (0.95f, 0.35f, 0.20f)
            else if (s.Contains("battle") || s.Contains("tactics") || s.Equals("battletactics"))
            {
                r = 0.95f; g = 0.35f; b = 0.20f;
            }
            // 7. Diplomacy: Gold (0.95f, 0.75f, 0.25f)
            else if (s.Contains("diplomacy"))
            {
                r = 0.95f; g = 0.75f; b = 0.25f;
            }
            // 8. MemoryBook: Warm Silver/Beige (0.88f, 0.84f, 0.76f)
            else if (s.Contains("memory") || s.Equals("memorybook"))
            {
                r = 0.88f; g = 0.84f; b = 0.76f;
            }
            else if (s.Contains("image"))
            {
                r = 0.95f; g = 0.45f; b = 0.75f; // Rose
            }
            else if (s.Contains("antigravity"))
            {
                r = 0.60f; g = 0.80f; b = 1.00f; // Soft Ice Blue
            }
            else
            {
                r = 0.88f; g = 0.88f; b = 0.88f;
            }
        }

        public static void ShowTimer(string scope, long elapsedMs)
        {
            Show("Timer: " + FormatTimer(elapsedMs), scope);
        }

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

        public static string FormatPrefix(string scope)
        {
            string prefix = "[" + PrismInfo.ChatPrefix;
            if (string.IsNullOrEmpty(scope))
            {
                return prefix + "] ";
            }
            string normalized = ScopeCatalog.NormalizeTitle(scope);
            int parenIdx = normalized.IndexOf('(');
            if (parenIdx > 0)
            {
                normalized = normalized.Substring(0, parenIdx).Trim();
            }
            return prefix + " - " + normalized + "] ";
        }

        private struct QueuedNotice
        {
            public string Message;
            public float? R;
            public float? G;
            public float? B;
        }

        private static readonly ConcurrentQueue<QueuedNotice> PendingNotices =
            new ConcurrentQueue<QueuedNotice>();

        private static Type _infoManagerType;
        private static Type _infoMessageType;
        private static Type _colorType;
        private static MethodInfo _displayMessageMethod;
        private static bool _typesResolved;
        private static bool _stylesRegistered;
        private static volatile bool _isUiReady;

        public static void MarkUiReady()
        {
            _isUiReady = true;
        }

        public static void FlushPending()
        {
            if (!_isUiReady)
            {
                return;
            }

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
            if (!_isUiReady || _stylesRegistered)
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
                    "DialogueHeroes",
                    "DialogueTownsfolk",
                    "GroupConversation",
                    "NpcLetters",
                    "NpcInitiative",
                    "CharacterDevelopment",
                    "BattleTactics",
                    "Diplomacy",
                    "Diplomacy & Events",
                    "Diplomacy&Events",
                    "DiplomacyEvents",
                    "MemoryBook",
                    "Dialogue",
                    "Events",
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
            }
        }

        private static void TryDisplayDirect(string message, float? r = null, float? g = null, float? b = null)
        {
            if (!_isUiReady)
            {
                return;
            }

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
            }
        }
    }
}
