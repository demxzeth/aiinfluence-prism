using System;
using System.IO;
using System.Reflection;
using System.Text;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Logging
{
    /// <summary>
    /// File log of the mod: logs/prism_log.txt next to the mod root.
    /// The stream stays open; all calls are serialized by a lock.
    /// If the file is unavailable (disk, permissions) the log is
    /// silently disabled — the mod must not crash the game over logging.
    /// </summary>
    public static class LogSink
    {
        private static readonly object Gate = new object();
        private static StreamWriter _writer;
        private static string _path;

        /// <summary>Open the log. A repeated call retargets it.</summary>
        public static void Initialize(string moduleRoot, bool verbose = true)
        {
            lock (Gate)
            {
                CloseNoLock();

                try
                {
                    if (string.IsNullOrEmpty(moduleRoot))
                    {
                        return;
                    }

                    _path = Path.Combine(moduleRoot, PrismInfo.LogRelativePath);
                    string folder = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    _writer = new StreamWriter(_path, append: true) { AutoFlush = true };
                }
                catch
                {
                    _writer = null;
                }

                Verbose = verbose;
            }
        }

        /// <summary>Is file logging enabled by configuration?</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Are verbose (Info) entries enabled?</summary>
        public static bool Verbose { get; private set; }

        /// <summary>Where the log is written (diagnostics; empty — log is off).</summary>
        public static string LogFile
        {
            get { return _path ?? string.Empty; }
        }

        public static void Always(string message)
        {
            Write("INFO ", message);
        }

        public static void Info(string message)
        {
            if (Verbose)
            {
                Write("info ", message);
            }
        }

        public static void Warn(string message)
        {
            Write("WARN ", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message + (ex == null ? string.Empty : Environment.NewLine + FormatException(ex)));
        }

        /// <summary>
        /// Formats an exception hierarchy with full details, including TypeLoadException,
        /// LoaderExceptions (if ReflectionTypeLoadException), InnerException, and StackTrace.
        /// </summary>
        public static string FormatException(Exception ex)
        {
            if (ex == null) return string.Empty;
            StringBuilder sb = new StringBuilder();
            DumpException(ex, sb, 0);
            return sb.ToString().TrimEnd();
        }

        private static void DumpException(Exception ex, StringBuilder sb, int depth)
        {
            if (ex == null) return;
            string indent = new string(' ', depth * 2);
            sb.AppendLine(indent + "Exception: " + ex.GetType().FullName + ": " + ex.Message);
            if (ex is TypeLoadException tle && !string.IsNullOrEmpty(tle.TypeName))
            {
                sb.AppendLine(indent + "  TypeName: " + tle.TypeName);
            }
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine(indent + "StackTrace:\n" + ex.StackTrace);
            }
            if (ex is ReflectionTypeLoadException rtle && rtle.LoaderExceptions != null)
            {
                sb.AppendLine(indent + "LoaderExceptions (" + rtle.LoaderExceptions.Length + "):");
                for (int i = 0; i < rtle.LoaderExceptions.Length; i++)
                {
                    Exception lex = rtle.LoaderExceptions[i];
                    if (lex != null)
                    {
                        sb.AppendLine(indent + "  [" + i + "]:");
                        DumpException(lex, sb, depth + 2);
                    }
                }
            }
            if (ex is AggregateException agg && agg.InnerExceptions != null && agg.InnerExceptions.Count > 0)
            {
                sb.AppendLine(indent + "Aggregate InnerExceptions (" + agg.InnerExceptions.Count + "):");
                for (int i = 0; i < agg.InnerExceptions.Count; i++)
                {
                    Exception inner = agg.InnerExceptions[i];
                    if (inner != null)
                    {
                        sb.AppendLine(indent + "  [" + i + "]:");
                        DumpException(inner, sb, depth + 2);
                    }
                }
            }
            else if (ex.InnerException != null)
            {
                sb.AppendLine(indent + "InnerException:");
                DumpException(ex.InnerException, sb, depth + 1);
            }
        }

        public static void Flush()
        {
            lock (Gate)
            {
                if (_writer != null)
                {
                    try
                    {
                        _writer.Flush();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>Close the log (on mod unload).</summary>
        public static void Shutdown()
        {
            lock (Gate)
            {
                CloseNoLock();
            }
        }

        private static void Write(string level, string message)
        {
            if (!Enabled)
            {
                return;
            }

            lock (Gate)
            {
                StreamWriter writer = _writer;
                if (writer == null)
                {
                    return;
                }

                try
                {
                    writer.WriteLine(
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message);

                    // Immediate flush to disk on ERROR/WARN to avoid losing entries on freeze/crash
                    if (level.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                        level.StartsWith("WARN", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.Flush();
                    }
                }
                catch
                {
                    CloseNoLock();
                }
            }
        }

        private static void CloseNoLock()
        {
            StreamWriter writer = _writer;
            _writer = null;
            _path = null;
            if (writer != null)
            {
                try
                {
                    writer.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
