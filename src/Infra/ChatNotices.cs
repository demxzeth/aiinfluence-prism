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
        /// <param name="scope">Scope name (appears in the prefix).</param>
        /// <param name="r">Red channel for the UI colour (0-1), or null for default.</param>
        /// <param name="g">Green channel for the UI colour (0-1), or null for default.</param>
        /// <param name="b">Blue channel for the UI colour (0-1), or null for default.</param>
        public static void Show(string text, string scope = null,
            float? r = null, float? g = null, float? b = null)
        {
            string message = FormatPrefix(scope) + text;
            TryDisplay(message, r, g, b);
            LogSink.Always(message);
        }

        /// <summary>Show an error-tinged message.</summary>
        public static void Error(string text, string scope = null,
            float? r = null, float? g = null, float? b = null)
        {
            string message = FormatPrefix(scope) + text;
            TryDisplay(message, r ?? 0.7f, g ?? 0.3f, b ?? 0.3f); // reddish default
            LogSink.Warn(message);
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

        private static string FormatPrefix(string scope)
        {
            string prefix = "[" + PrismInfo.ChatPrefix;
            return string.IsNullOrEmpty(scope)
                ? prefix + "] "
                : prefix + " - " + scope + "] ";
        }

        private static void TryDisplay(string message, float? r = null, float? g = null, float? b = null)
        {
            try
            {
                // Reflection to avoid a hard JIT dependency on TaleWorlds.Library
                // in unit-test / early-load environments. At runtime the assembly
                // is always present.
                Type infoManager = Type.GetType(
                    "TaleWorlds.Library.InformationManager, TaleWorlds.Library", throwOnError: false);
                if (infoManager == null)
                {
                    return;
                }

                Type infoMessage = Type.GetType(
                    "TaleWorlds.Library.InformationMessage, TaleWorlds.Library", throwOnError: false);
                if (infoMessage == null)
                {
                    return;
                }

                object msg;
                if (r.HasValue && g.HasValue && b.HasValue)
                {
                    // InformationMessage(string, Color) — build the Color via
                    // its (float red, float green, float blue, float alpha) ctor.
                    Type colorType = Type.GetType(
                        "TaleWorlds.Library.Color, TaleWorlds.Library", throwOnError: false);
                    object color = Activator.CreateInstance(
                        colorType, new object[] { r.Value, g.Value, b.Value, 1f });
                    msg = Activator.CreateInstance(
                        infoMessage, new object[] { message, color });
                }
                else
                {
                    msg = Activator.CreateInstance(infoMessage, new object[] { message });
                }

                infoManager.InvokeMember("DisplayMessage",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.InvokeMethod,
                    null, null, new[] { msg });
            }
            catch
            {
                // The game UI is not ready (early load / menu / unit tests);
                // the log line written by the caller carries the message.
            }
        }
    }
}