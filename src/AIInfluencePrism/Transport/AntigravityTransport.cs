using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Post;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Transport
{
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }

    public sealed class AntigravityTransport : IChatTransport
    {
        public ProviderKind Kind => ProviderKind.Antigravity;

        public string EndpointUrl(Profile profile)
        {
            string cli = ResolveCliPath(profile?.AntigravityCliPath);
            string model = ResolveModel(profile);
            return "Antigravity CLI (" + Path.GetFileName(cli) + ", " + model + ")";
        }

        public string BuildRequestBody(ChatTurn turn, ModelOptions options, Profile profile)
        {
            if (turn == null) return string.Empty;
            if (!string.IsNullOrEmpty(turn.Prompt))
            {
                return turn.Prompt;
            }
            if (turn.HasSplit)
            {
                return turn.SplitSystem + "\n\n" + turn.SplitUser;
            }
            return !string.IsNullOrEmpty(turn.System)
                ? turn.System + "\n\n" + (turn.User ?? string.Empty)
                : turn.User ?? string.Empty;
        }

        public void ConfigureRequest(System.Net.Http.HttpRequestMessage request, Profile profile)
        {
        }

        public string ParseReply(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody)) return rawBody;
            string trimmed = rawBody.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = trimmed.IndexOf('\n');
                if (firstNewline > 0)
                {
                    int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                    if (lastFence > firstNewline)
                    {
                        trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                    }
                }
            }

            if (ProfileBook.Current.JsonRepairEnabled && LooksLikeJsonObject(trimmed))
            {
                string healed = JsonHealer.Heal(trimmed);
                if (!string.IsNullOrEmpty(healed))
                {
                    return healed;
                }
            }

            return trimmed;
        }

        public static bool LooksLikeJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int brace = text.IndexOf('{');
            if (brace < 0) return false;
            int colon = text.IndexOf(':', brace);
            if (colon < 0) return false;
            for (int i = brace + 1; i < colon; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) continue;
                return c == '"';
            }
            return false;
        }

        public static bool IsCompleteJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int start = text.IndexOf('{');
            if (start < 0) return false;

            int colon = text.IndexOf(':', start);
            if (colon < 0) return false;

            bool inString = false;
            bool escaped = false;
            int depth = 0;
            bool started = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }
                else if (c == '{')
                {
                    depth++;
                    started = true;
                }
                else if (c == '}')
                {
                    depth--;
                    if (started && depth == 0)
                    {
                        return true;
                    }
                    if (depth < 0) return false;
                }
            }

            return false;
        }

        public static bool IsCompleteResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();

            bool endsWithClosingBrace = trimmed.EndsWith("}")
                || (trimmed.EndsWith("`") && trimmed.TrimEnd('`', ' ', '\r', '\n').EndsWith("}"));

            if (!endsWithClosingBrace)
            {
                return false;
            }

            if ((trimmed.IndexOf("\"error\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 trimmed.IndexOf("\"short_error\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 trimmed.IndexOf("Eligibility check failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 trimmed.IndexOf("Bad Gateway", StringComparison.OrdinalIgnoreCase) >= 0) &&
                trimmed.IndexOf("\"response\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (trimmed.IndexOf("\"response\"", StringComparison.OrdinalIgnoreCase) < 0 &&
                (trimmed.IndexOf("\"name\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 trimmed.IndexOf("\"internal_thoughts\"", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            return IsCompleteJson(trimmed);
        }

        public const int MaxSafePromptChars = 190000;

        public static string EnsurePromptUnderSafeLimit(string prompt, int maxLimit = 190000)
        {
            if (string.IsNullOrEmpty(prompt) || prompt.Length <= maxLimit)
            {
                return prompt;
            }

            if (Hook.Dispatch.TryDeriveTextSplit(prompt, 0, out string sys, out string usr))
            {
                // Step 1: Progressively reduce dialogue history turns (30 -> 20 -> 10 -> 4 -> 2)
                int[] turnBudgets = new int[] { 30, 20, 10, 4, 2 };
                foreach (int turns in turnBudgets)
                {
                    string safeUsr = Routing.DialogueHistoryManager.ApplyEmergencyGeometricTrim(usr, turns);
                    string combined = !string.IsNullOrEmpty(sys) ? (sys + "\n\n" + safeUsr) : safeUsr;
                    if (combined.Length <= maxLimit)
                    {
                        return combined;
                    }
                }

                // Step 2: If even with minimal history (2 turns) the prompt still exceeds maxLimit,
                // prune intermediate world lore while strictly protecting the persona at the head
                // and the action rules / formatting directives / JSON schema at the tail!
                string minimalUsr = Routing.DialogueHistoryManager.ApplyEmergencyGeometricTrim(usr, 2);
                int userLen = minimalUsr.Length;
                int allowedSysChars = maxLimit - userLen - 500;

                if (allowedSysChars > 5000 && sys.Length > allowedSysChars)
                {
                    // Action rules and formatting instructions live at the tail of sys.
                    // Allocate generous budget to the tail (up to 40 000 chars) to keep actionrules.txt intact.
                    int keepSysTail = Math.Min(40000, allowedSysChars / 2);
                    int keepSysHead = allowedSysChars - keepSysTail - 200;
                    if (keepSysHead > 2000 && keepSysTail > 2000)
                    {
                        sys = sys.Substring(0, keepSysHead)
                            + "\n\n[... intermediate world lore omitted to fit buffer ...]\n\n"
                            + sys.Substring(sys.Length - keepSysTail);
                        string res = sys + "\n\n" + minimalUsr;
                        if (res.Length <= maxLimit) return res;
                    }
                }

                string candidate = !string.IsNullOrEmpty(sys) ? (sys + "\n\n" + minimalUsr) : minimalUsr;
                if (candidate.Length <= maxLimit)
                {
                    return candidate;
                }
            }

            int headChars = Math.Min(30000, maxLimit / 3);
            int tailChars = maxLimit - headChars - 200;
            if (tailChars > 0 && prompt.Length > (headChars + tailChars))
            {
                return prompt.Substring(0, headChars)
                    + "\n\n[... earlier context omitted to fit buffer ...]\n\n"
                    + prompt.Substring(prompt.Length - tailChars);
            }

            return prompt;
        }

        public const int MaxRetries = 2;
        public static int InitialRetryDelayMs = 500;

        public static Func<string, string, int, CancellationToken, string, Task<ProcessResult>> RunProcessForTests;

        private static readonly SemaphoreSlim AntigravityLock = new SemaphoreSlim(1, 1);

        public static bool IsRetryableFailure(ProcessResult res, Exception ex = null)
        {
            if (ex != null)
            {
                if (ex is TimeoutException)
                {
                    return false;
                }

                string exMsg = ex.Message ?? string.Empty;
                if (exMsg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("Eligibility check failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("loadCodeAssist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("Google Antigravity connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("Bad Gateway", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                if (exMsg.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("network issue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    exMsg.IndexOf("EOF", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            if (res == null)
            {
                return false;
            }

            if (res.ExitCode == 0)
            {
                return false;
            }

            string combined = ((res.Error ?? string.Empty) + " " + (res.Output ?? string.Empty)).Trim();
            if (combined.Length == 0)
            {
                return false;
            }

            if (combined.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("deadline exceeded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Eligibility check failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("loadCodeAssist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Google Antigravity connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Bad Gateway", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (combined.IndexOf("\"retryable\": true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("\"retryable\":true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (combined.IndexOf("EOF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("network issue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("streamGenerateContent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("broken pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("Error while dialing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("network is unreachable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (combined.IndexOf("HTTP 502", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("HTTP 503", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("HTTP 504", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("HTTP 429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("UNAVAILABLE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        public async Task<string> SendAsync(
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken, Action<string> onDelta = null)
        {
            string cli = ResolveCliPath(profile.AntigravityCliPath);
            string model = ResolveModel(profile);
            int timeoutSeconds = profile.TimeoutSeconds >= 10 ? profile.TimeoutSeconds : 180;
            string effort = ResolveEffort(profile.ReasoningEffort);

            string singlePrompt = BuildRequestBody(turn, options, profile);

            if (turn?.Scope == ScopeId.DialogueHeroes ||
                turn?.Scope == ScopeId.DialogueTownsfolk ||
                singlePrompt.IndexOf("Conversation history", StringComparison.OrdinalIgnoreCase) >= 0 ||
                singlePrompt.IndexOf("Dialogue history", StringComparison.OrdinalIgnoreCase) >= 0 ||
                singlePrompt.IndexOf("ТЕКУЩАЯ ИСТОРИЯ ДИАЛОГА", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                singlePrompt = Routing.DialogueHistoryManager.ApplyActiveTurnAnchors(singlePrompt);
            }

            string reply = await SendSingleStagePromptAsync(
                singlePrompt, cli, model, effort, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            onDelta?.Invoke(reply);
            return reply;
        }

        private async Task<string> SendSingleStagePromptAsync(
            string prompt,
            string cli, string model, string effort, int timeoutSeconds, CancellationToken cancellationToken)
        {
            if (prompt != null && prompt.Length > MaxSafePromptChars)
            {
                LogSink.Warn("antigravity: prompt length (" + prompt.Length
                    + " chars) exceeds safe single-stage CLI limit (" + MaxSafePromptChars
                    + "). Applying emergency pruning.");
                prompt = EnsurePromptUnderSafeLimit(prompt, MaxSafePromptChars);
            }

            await AntigravityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool lockHeld = true;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("--dangerously-skip-permissions --input-format text");

                if (!string.IsNullOrWhiteSpace(model))
                {
                    sb.Append(" --model ");
                    sb.Append(EscapeArgument(model));
                }

                if (!string.IsNullOrEmpty(effort))
                {
                    sb.Append(" --effort ");
                    sb.Append(effort);
                }

                sb.Append(" --disable-slash-commands");

                string args = sb.ToString();
                ProcessResult res = null;
                int maxAttempts = 1 + MaxRetries;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    Exception caughtEx = null;
                    try
                    {
                        res = await RunProcessAsync(cli, args, timeoutSeconds, cancellationToken, prompt)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        caughtEx = ex;
                    }

                    bool failed = (caughtEx != null) || (res == null) || (res.ExitCode != 0);

                    if (!failed)
                    {
                        if (string.IsNullOrWhiteSpace(res.Output))
                        {
                            if (!string.IsNullOrWhiteSpace(res.Error))
                            {
                                LogSink.Warn("antigravity: empty output, stderr: " + res.Error);
                                throw TransportException.ForCli(0, res.Error, stderr: res.Error);
                            }
                        }
                        return ParseReply(res.Output);
                    }

                    string errMsg = caughtEx != null
                        ? caughtEx.Message
                        : (!string.IsNullOrWhiteSpace(res.Error) ? res.Error : res.Output);

                    if (string.IsNullOrWhiteSpace(errMsg))
                    {
                        errMsg = "agy.exe exited with code " + (res != null ? res.ExitCode : -1);
                    }

                    bool isRetryable = IsRetryableFailure(res, caughtEx);

                    if (isRetryable && attempt < maxAttempts)
                    {
                        int delayMs = InitialRetryDelayMs * attempt;
                        LogSink.Warn("antigravity single-stage transient error on attempt " + attempt + "/" + maxAttempts
                            + ". Retrying in " + delayMs + "ms... Reason: " + errMsg);

                        AntigravityLock.Release();
                        lockHeld = false;
                        try
                        {
                            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                            await AntigravityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                            lockHeld = true;
                        }
                        catch
                        {
                            throw;
                        }
                        continue;
                    }

                    int exitCode = res != null ? res.ExitCode : (caughtEx is TransportException te ? te.StatusCode : 1);
                    if (errMsg.IndexOf("loadCodeAssist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("Eligibility check failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("Google Antigravity connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("Bad Gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("Error while dialing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("streamGenerateContent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        errMsg.IndexOf("EOF", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        errMsg = "Google Antigravity connection closed. Check VPN/Antigravity Unlocker and try again.";
                    }
                    string rawStderr = !string.IsNullOrWhiteSpace(res?.Error) ? res.Error : (caughtEx is TransportException cte ? cte.RawResponse : null);
                    throw TransportException.ForCli(exitCode, errMsg, stderr: rawStderr);
                }

                return string.Empty;
            }
            finally
            {
                if (lockHeld)
                {
                    AntigravityLock.Release();
                }
            }
        }

        public async Task<bool> ProbeAsync(Profile profile, CancellationToken cancellationToken)
        {
            try
            {
                string cli = ResolveCliPath(profile.AntigravityCliPath);
                ProcessResult res = await RunProcessAsync(cli, "--version", 10, cancellationToken)
                    .ConfigureAwait(false);
                return res.ExitCode == 0;
            }
            catch (Exception ex)
            {
                LogSink.Warn("antigravity: probe failed: " + ex.Message);
                return false;
            }
        }

        public static string ResolveCliPath(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string trimmed = configured.Trim().Trim('"', '\'').Trim();
                if (File.Exists(trimmed))
                {
                    return trimmed;
                }
                if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed + ".exe"))
                {
                    return trimmed + ".exe";
                }

                if (trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                    trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
                {
                    return trimmed;
                }
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                string candidate = Path.Combine(localAppData, "agy", "bin", "agy.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string part in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string trimmedPart = part.Trim().Trim('"', '\'').Trim();
                        if (trimmedPart.Length > 0)
                        {
                            string candidate = Path.Combine(trimmedPart, "agy.exe");
                            if (File.Exists(candidate))
                            {
                                return candidate;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return !string.IsNullOrWhiteSpace(configured) ? configured.Trim().Trim('"', '\'').Trim() : "agy";
        }

        public static string ResolveModel(Profile profile)
        {
            if (profile != null)
            {
                if (!string.IsNullOrWhiteSpace(profile.Model))
                {
                    return NormalizeModelName(profile.Model);
                }
                if (!string.IsNullOrWhiteSpace(profile.AntigravityModel))
                {
                    return NormalizeModelName(profile.AntigravityModel);
                }
            }
            return "gemini-3.8-flash-high";
        }

        public static string NormalizeModelName(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return "gemini-3.8-flash-high";
            string trimmed = model.Trim().Trim('"', '\'').Trim();

            switch (trimmed.ToLowerInvariant())
            {
                case "gemini-3.8-low":
                case "gemini 3.8 flash (low)":
                case "gemini 3.8 flash low":
                case "gemini-3.8-flash-low":
                    return "gemini-3.8-flash-low";

                case "gemini-3.8-medium":
                case "gemini 3.8 flash (medium)":
                case "gemini 3.8 flash medium":
                case "gemini-3.8-flash-medium":
                    return "gemini-3.8-flash-medium";

                case "gemini-3.8-high":
                case "gemini 3.8 flash (high)":
                case "gemini 3.8 flash high":
                case "gemini-3.8-flash-high":
                case "gemini-3.8":
                case "gemini 3.8":
                case "gemini 3.8 flash":
                    return "gemini-3.8-flash-high";

                default:
                    return trimmed;
            }
        }

        public static string ResolveEffort(string effort)
        {
            if (string.IsNullOrWhiteSpace(effort))
            {
                return null;
            }

            string lower = effort.Trim().ToLowerInvariant();
            switch (lower)
            {
                case "low":
                case "medium":
                case "high":
                case "xhigh":
                case "max":
                    return lower;
                default:
                    return null;
            }
        }

        public static string EscapeArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append('\"');

            for (int i = 0; i < argument.Length; i++)
            {
                int backslashCount = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashCount++;
                    i++;
                }

                if (i == argument.Length)
                {
                    sb.Append('\\', backslashCount * 2);
                    break;
                }

                if (argument[i] == '\"')
                {
                    sb.Append('\\', backslashCount * 2 + 1);
                    sb.Append('\"');
                }
                else
                {
                    sb.Append('\\', backslashCount);
                    sb.Append(argument[i]);
                }
            }

            sb.Append('\"');
            return sb.ToString();
        }

        #region Win32 Process and Window Management

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_HIDE = 0;

        private static void HideProcessWindows(int pid)
        {
            try
            {
                HashSet<int> pids = new HashSet<int> { pid };
                foreach (int childPid in GetChildProcessIds(pid))
                {
                    pids.Add(childPid);
                }

                EnumWindows((hWnd, lParam) =>
                {
                    uint windowPid;
                    GetWindowThreadProcessId(hWnd, out windowPid);
                    if (pids.Contains((int)windowPid))
                    {
                        ShowWindow(hWnd, SW_HIDE);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        private static List<int> GetChildProcessIds(int parentPid)
        {
            List<int> children = new List<int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            {
                return children;
            }

            try
            {
                PROCESSENTRY32 pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

                if (Process32First(snapshot, ref pe))
                {
                    do
                    {
                        if (pe.th32ParentProcessID == parentPid && pe.th32ProcessID != parentPid && pe.th32ProcessID > 0)
                        {
                            children.Add((int)pe.th32ProcessID);
                        }
                    } while (Process32Next(snapshot, ref pe));
                }
            }
            catch { }
            finally
            {
                CloseHandle(snapshot);
            }

            return children;
        }

        private static void KillProcessTree(Process process)
        {
            if (process == null) return;
            try
            {
                int pid = process.Id;
                KillProcessTreeById(pid);
            }
            catch { }
        }

        private static void KillProcessTreeById(int parentPid)
        {
            try
            {
                foreach (int childPid in GetChildProcessIds(parentPid))
                {
                    KillProcessTreeById(childPid);
                }

                using (Process proc = Process.GetProcessById(parentPid))
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                    }
                }
            }
            catch { }
        }

        private static readonly object ProcessListLock = new object();
        private static readonly HashSet<Process> ActiveProcesses = new HashSet<Process>();

        public static void AbortAllRunningProcesses()
        {
            lock (ProcessListLock)
            {
                foreach (Process proc in ActiveProcesses)
                {
                    try { KillProcessTree(proc); } catch { }
                }
                ActiveProcesses.Clear();
            }
        }

        #endregion

        public static async Task<ProcessResult> RunProcessAsync(
            string fileName, string arguments, int timeoutSeconds, CancellationToken cancellationToken,
            string standardInput = null)
        {
            if (RunProcessForTests != null)
            {
                return await RunProcessForTests(fileName, arguments, timeoutSeconds, cancellationToken, standardInput)
                    .ConfigureAwait(false);
            }

            string workingDir = !string.IsNullOrEmpty(ModuleLocator.Root) && Directory.Exists(ModuleLocator.Root)
                ? ModuleLocator.Root
                : AppDomain.CurrentDomain.BaseDirectory;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = (standardInput != null),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.EnvironmentVariables["AGY_CLI_NONINTERACTIVE_HEADLESS"] = "true";
            startInfo.EnvironmentVariables["AGY_CLI_INTERACTIVE_HEADLESS"] = "true";

            Process process = new Process { StartInfo = startInfo };
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            object syncLock = new object();
            bool earlyCompleted = false;
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            int firstResponseTimeoutSeconds = Math.Max(15, Math.Min(30, timeoutSeconds));
            bool receivedInitialOutput = false;
            Timer initialSilenceWatchdog = null;

            void OnInitialSilenceTimeout(object state)
            {
                lock (syncLock)
                {
                    if (receivedInitialOutput || tcs.Task.IsCompleted) return;
                }

                LogSink.Warn("antigravity: no response from CLI backend after "
                    + firstResponseTimeoutSeconds + "s. Aborting hung process.");

                lock (syncLock)
                {
                    errorBuilder.AppendLine("Google Antigravity connection closed. Check VPN/Antigravity Unlocker and try again.");
                }

                KillProcessTree(process);
                tcs.TrySetResult(true);
            }

            void OnSilenceTimeout(object state)
            {
                string text;
                lock (syncLock)
                {
                    text = outputBuilder.ToString();
                }

                if (string.IsNullOrWhiteSpace(text)) return;

                lock (syncLock)
                {
                    if (earlyCompleted || tcs.Task.IsCompleted) return;
                    earlyCompleted = true;
                }

                LogSink.Info("antigravity: stdout silence timeout (5000ms, "
                    + text.Length + " chars). Releasing without waiting for CLI telemetry shutdown.");
                tcs.TrySetResult(true);
            }

            using (Timer silenceTimer = new Timer(OnSilenceTimeout, null, Timeout.Infinite, Timeout.Infinite))
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;

                    string currentOutput;
                    lock (syncLock)
                    {
                        if (!receivedInitialOutput && !string.IsNullOrWhiteSpace(e.Data))
                        {
                            receivedInitialOutput = true;
                            try { initialSilenceWatchdog?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                        }

                        outputBuilder.AppendLine(e.Data);
                        currentOutput = outputBuilder.ToString();
                    }

                    bool isComplete = IsCompleteResponse(currentOutput);
                    if (isComplete)
                    {
                        lock (syncLock)
                        {
                            if (!earlyCompleted && !tcs.Task.IsCompleted)
                            {
                                earlyCompleted = true;
                                LogSink.Info("antigravity: complete JSON response detected on stdout ("
                                    + currentOutput.Length + " chars). Releasing immediately.");
                                tcs.TrySetResult(true);
                            }
                        }
                    }
                    else if (receivedInitialOutput)
                    {
                        lock (syncLock)
                        {
                            if (!earlyCompleted && !tcs.Task.IsCompleted)
                            {
                                try
                                {
                                    silenceTimer?.Change(5000, Timeout.Infinite);
                                }
                                catch (ObjectDisposedException) { }
                            }
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        lock (syncLock)
                        {
                            errorBuilder.AppendLine(e.Data);
                        }

                        if (e.Data.IndexOf("Eligibility check failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("loadCodeAssist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("Google Antigravity connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("connection closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("connection reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("Bad Gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("Error while dialing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("streamGenerateContent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("EOF", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            KillProcessTree(process);
                            tcs.TrySetResult(true);
                        }
                    }
                };

                try
                {
                    if (!process.Start())
                    {
                        process.Dispose();
                        throw TransportException.ForCli(0, "Failed to start process: " + fileName);
                    }

                    lock (ProcessListLock)
                    {
                        ActiveProcesses.Add(process);
                    }

                    initialSilenceWatchdog = new Timer(OnInitialSilenceTimeout, null, firstResponseTimeoutSeconds * 1000, Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    process.Dispose();
                    throw TransportException.ForCli(0, "Could not start " + fileName + ": " + ex.Message);
                }

                try
                {
                    int pid = process.Id;
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            HideProcessWindows(pid);
                            await Task.Delay(100).ConfigureAwait(false);
                        }
                    });
                }
                catch { }

                try
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (standardInput != null)
                    {
                        try
                        {
                            byte[] inputBytes = Encoding.UTF8.GetBytes(standardInput);
                            await process.StandardInput.BaseStream.WriteAsync(inputBytes, 0, inputBytes.Length, cancellationToken).ConfigureAwait(false);
                            await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LogSink.Warn("antigravity: failed writing to stdin: " + ex.Message);
                        }
                        finally
                        {
                            try { process.StandardInput.Close(); } catch { }
                        }
                    }

                    process.EnableRaisingEvents = true;
                    process.Exited += (s, e) => tcs.TrySetResult(true);

                    if (process.HasExited)
                    {
                        tcs.TrySetResult(true);
                    }

                    using (CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                    using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                    {
                        using (linkedCts.Token.Register(() =>
                        {
                            KillProcessTree(process);
                            tcs.TrySetCanceled();
                        }))
                        {
                            try
                            {
                                await tcs.Task.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                if (timeoutCts.IsCancellationRequested)
                                {
                                    throw TransportException.ForCli(0, "Antigravity CLI timed out after " + timeoutSeconds + "s.");
                                }
                                throw;
                            }
                        }
                    }

                    try { silenceTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

                    if (!process.HasExited)
                    {
                        try { process.WaitForExit(50); } catch { }
                        if (!process.HasExited)
                        {
                            KillProcessTree(process);
                        }
                    }

                    int exitCode = process.HasExited ? process.ExitCode : -1;
                    if (earlyCompleted)
                    {
                        string currentOut;
                        lock (syncLock) { currentOut = outputBuilder.ToString(); }
                        if (currentOut.IndexOf("\"error\"", StringComparison.OrdinalIgnoreCase) < 0 &&
                            currentOut.IndexOf("Eligibility check failed", StringComparison.OrdinalIgnoreCase) < 0 &&
                            currentOut.IndexOf("Bad Gateway", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            exitCode = 0;
                        }
                    }

                    string outStr;
                    string errStr;
                    lock (syncLock)
                    {
                        outStr = outputBuilder.ToString().Trim();
                        errStr = errorBuilder.ToString().Trim();
                    }

                    return new ProcessResult
                    {
                        ExitCode = exitCode,
                        Output = outStr,
                        Error = errStr
                    };
                }
                finally
                {
                    try { initialSilenceWatchdog?.Dispose(); } catch { }
                    lock (ProcessListLock)
                    {
                        ActiveProcesses.Remove(process);
                    }
                    try
                    {
                        if (!process.HasExited)
                        {
                            KillProcessTree(process);
                        }
                    }
                    catch { }
                    try { process.CancelOutputRead(); } catch { }
                    try { process.CancelErrorRead(); } catch { }
                    try { process.Dispose(); } catch { }
                }
            }
        }
    }
}
