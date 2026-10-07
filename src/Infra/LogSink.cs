using System;
using System.IO;

namespace AIInfluencePrism.Infra
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
            Write("ERROR", message + (ex == null ? string.Empty : Environment.NewLine + ex));
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
                        // ignored: the log must not break the game.
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
                }
                catch
                {
                    // the disk fell off — disable the log, the game goes on.
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
                    // nothing to do: closing the log must not throw.
                }
            }
        }
    }
}
