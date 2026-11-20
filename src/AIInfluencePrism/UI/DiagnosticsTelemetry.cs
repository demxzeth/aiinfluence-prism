using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// Thread-safe in-memory diagnostics telemetry buffer tracking active session dialogues,
    /// last interlocutor, dynamic events, diplomacy triggers, and real-time log events.
    /// Served to the WebUI via /api/telemetry and /api/config.
    /// </summary>
    public static class DiagnosticsTelemetry
    {
        private static int _sessionDialoguesCount;
        private static string _lastInterlocutor = "None";
        private static readonly ConcurrentQueue<string> _logs = new ConcurrentQueue<string>();
        private const int MaxLogs = 50;

        public static int SessionDialoguesCount => _sessionDialoguesCount;
        public static string LastInterlocutor => _lastInterlocutor;

        public static void RecordDialogue(string interlocutor, string scope, long latencyMs, string snippet = null)
        {
            System.Threading.Interlocked.Increment(ref _sessionDialoguesCount);
            if (!string.IsNullOrWhiteSpace(interlocutor))
            {
                _lastInterlocutor = interlocutor;
            }

            string time = DateTime.Now.ToString("HH:mm:ss");
            string name = string.IsNullOrWhiteSpace(interlocutor) ? "NPC" : interlocutor;
            string entry = $"[{time}] Dialogue with {name} ({scope})";
            if (latencyMs > 0) entry += $" — {latencyMs}ms";
            if (!string.IsNullOrWhiteSpace(snippet))
            {
                string clean = snippet.Replace("\r", " ").Replace("\n", " ").Trim();
                if (clean.Length > 500) clean = clean.Substring(0, 500) + "...";
                entry += $": \"{clean}\"";
            }
            AddLog(entry);
        }

        public static void RecordEvent(string eventType, string details = null)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string entry = $"[{time}] Dynamic Event: {eventType}";
            if (!string.IsNullOrWhiteSpace(details)) entry += $" — {details}";
            AddLog(entry);
        }

        public static void RecordDiplomacy(string statement, string target = null)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string entry = $"[{time}] Diplomacy";
            if (!string.IsNullOrWhiteSpace(target)) entry += $" to {target}";
            if (!string.IsNullOrWhiteSpace(statement))
            {
                string clean = statement.Replace("\r", " ").Replace("\n", " ").Trim();
                if (clean.Length > 500) clean = clean.Substring(0, 500) + "...";
                entry += $": \"{clean}\"";
            }
            AddLog(entry);
        }

        public static void AddLog(string line)
        {
            _logs.Enqueue(line);
            while (_logs.Count > MaxLogs && _logs.TryDequeue(out _)) { }
        }

        public static string[] GetRecentLogs()
        {
            return _logs.ToArray();
        }

        public static void ResetForTests()
        {
            _sessionDialoguesCount = 0;
            _lastInterlocutor = "None";
            while (_logs.TryDequeue(out _)) { }
        }
    }
}
