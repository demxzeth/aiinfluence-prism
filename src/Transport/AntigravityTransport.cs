using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Result of executing an external CLI process.
    /// </summary>
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Antigravity transport: interacts with Google Antigravity via the
    /// local CLI executable (agy.exe).
    /// </summary>
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
            string text = !string.IsNullOrEmpty(turn.System)
                ? turn.System + "\n\n" + (turn.User ?? string.Empty)
                : turn.User ?? string.Empty;

            return "[System instruction: You are generating responses for a video game AI dialogue system. Follow all instructions and output formats requested in the prompt. Do not invoke any tools or external commands.]\n\n" + text;
        }

        public void ConfigureRequest(System.Net.Http.HttpRequestMessage request, Profile profile)
        {
            // CLI transport does not use HTTP requests directly.
        }

        public string ParseReply(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody)) return rawBody;
            string trimmed = rawBody.Trim();

            // Strip markdown code fences (e.g. ```json ... ``` or ``` ... ```)
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

            // Heal JSON if needed and repair is enabled
            if (ProfileBook.Current.JsonRepairEnabled && trimmed.Contains("{"))
            {
                string healed = Post.JsonHealer.Heal(trimmed);
                if (!string.IsNullOrEmpty(healed))
                {
                    return healed;
                }
            }

            return trimmed;
        }

        /// <summary>
        /// Maximum characters per chunk when sending prompts to agy.exe.
        /// Chunks are split on newline boundaries to avoid breaking dialogue lines.
        /// Range: 130 000 - 150 000 characters (~250-300 KB in UTF-8).
        /// </summary>
        public const int DefaultMaxChunkChars = 140000;

        private static readonly SemaphoreSlim AntigravityLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Splits a long prompt or history text into chunks of at most maxChunkChars,
        /// breaking strictly on newline ('\n') boundaries without cutting dialogue lines or words.
        /// Falls back to space or hard split only if a single line exceeds maxChunkChars.
        /// </summary>
        public static List<string> SplitIntoChunks(string text, int maxChunkChars = DefaultMaxChunkChars)
        {
            List<string> chunks = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return chunks;
            }

            if (maxChunkChars <= 0)
            {
                maxChunkChars = DefaultMaxChunkChars;
            }

            if (text.Length <= maxChunkChars)
            {
                chunks.Add(text);
                return chunks;
            }

            int startIndex = 0;
            while (startIndex < text.Length)
            {
                int remaining = text.Length - startIndex;
                if (remaining <= maxChunkChars)
                {
                    chunks.Add(text.Substring(startIndex));
                    break;
                }

                int searchEnd = startIndex + maxChunkChars - 1;
                int searchStart = Math.Min(searchEnd, text.Length - 1);
                int searchCount = searchStart - startIndex + 1;

                // Priority 1: Search backwards for newline '\n'
                int newlineIndex = text.LastIndexOf('\n', searchStart, searchCount);
                int splitPoint;

                if (newlineIndex > startIndex)
                {
                    // Include the newline in the current chunk so lines are preserved cleanly
                    splitPoint = newlineIndex + 1;
                }
                else
                {
                    // Priority 2: Fallback to space
                    int spaceIndex = text.LastIndexOf(' ', searchStart, searchCount);
                    if (spaceIndex > startIndex)
                    {
                        splitPoint = spaceIndex + 1;
                    }
                    else
                    {
                        // Priority 3: Hard cut if no delimiter exists in the window
                        splitPoint = startIndex + maxChunkChars;
                    }
                }

                chunks.Add(text.Substring(startIndex, splitPoint - startIndex));
                startIndex = splitPoint;
            }

            return chunks;
        }

        /// <summary>
        /// Builds an ordered list of prompts for multi-stage execution with agy.exe.
        /// Chunks are generated strictly along newline boundaries with a maximum size of maxChunkChars.
        /// </summary>
        public static List<string> BuildMultiStagePrompts(
            ChatTurn turn, ModelOptions options, Profile profile, int maxChunkChars = DefaultMaxChunkChars)
        {
            List<string> stages = new List<string>();
            if (turn == null)
            {
                return stages;
            }

            if (maxChunkChars <= 0)
            {
                maxChunkChars = DefaultMaxChunkChars;
            }

            if (turn.HasSplit)
            {
                List<string> sysChunks = SplitIntoChunks(turn.SplitSystem, maxChunkChars);
                List<string> userChunks = SplitIntoChunks(turn.SplitUser, maxChunkChars);

                // Stage 1..K: Static background lore, character context, rules
                for (int i = 0; i < sysChunks.Count; i++)
                {
                    string sysPrompt = "[System instruction: The following is background context, character data, game state, and rules for a video game dialogue scene"
                        + (sysChunks.Count > 1 ? " (part " + (i + 1) + " of " + sysChunks.Count + ")" : "")
                        + ". Read and memorize all information. Acknowledge with a brief confirmation; do not generate in-game dialogue yet.]\n\n"
                        + sysChunks[i];
                    stages.Add(sysPrompt);
                }

                // Intermediate history stages (all user chunks except the final one)
                for (int i = 0; i < userChunks.Count - 1; i++)
                {
                    string histPrompt = "[System instruction: The following is a part of the earlier conversation history"
                        + (userChunks.Count > 2 ? " (part " + (i + 1) + " of " + (userChunks.Count - 1) + ")" : "")
                        + ". Read and memorize all information. Acknowledge with a brief confirmation (e.g. OK); do not generate in-game dialogue yet.]\n\n"
                        + userChunks[i];
                    stages.Add(histPrompt);
                }

                // Final stage (last dialogue history chunk + active prompt to generate reply)
                if (userChunks.Count > 0)
                {
                    string finalPrompt = "[System instruction: Now generate the character's in-game response to the latest message in the dialogue history below, following all formatting rules and constraints provided earlier. Do not invoke any tools or external commands.]\n\n"
                        + userChunks[userChunks.Count - 1];
                    stages.Add(finalPrompt);
                }
                else if (sysChunks.Count > 0)
                {
                    // Fallback in case userChunks was empty
                    stages[stages.Count - 1] = "[System instruction: Now generate the character's in-game response following all formatting rules and constraints provided earlier. Do not invoke any tools or external commands.]\n\n"
                        + sysChunks[sysChunks.Count - 1];
                }

                return stages;
            }

            // Unsplit turn: build full prompt
            string fullPrompt = !string.IsNullOrEmpty(turn.System)
                ? turn.System + "\n\n" + (turn.User ?? string.Empty)
                : turn.User ?? string.Empty;

            List<string> fullChunks = SplitIntoChunks(fullPrompt, maxChunkChars);
            if (fullChunks.Count <= 1)
            {
                string single = "[System instruction: You are generating responses for a video game AI dialogue system. Follow all instructions and output formats requested in the prompt. Do not invoke any tools or external commands.]\n\n" + fullPrompt;
                stages.Add(single);
                return stages;
            }

            for (int i = 0; i < fullChunks.Count - 1; i++)
            {
                string partPrompt = "[System instruction: The following is background context and history for a video game dialogue scene (part "
                    + (i + 1) + " of " + fullChunks.Count + "). Read and memorize all information. Acknowledge with a brief confirmation (e.g. OK); do not generate in-game dialogue yet.]\n\n"
                    + fullChunks[i];
                stages.Add(partPrompt);
            }

            string activePrompt = "[System instruction: Now generate the character's in-game response to the latest message in the dialogue scene below, following all formatting rules and constraints provided earlier. Do not invoke any tools or external commands.]\n\n"
                + fullChunks[fullChunks.Count - 1];
            stages.Add(activePrompt);

            return stages;
        }

        public async Task<string> SendAsync(
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken)
        {
            string cli = ResolveCliPath(profile.AntigravityCliPath);
            string model = ResolveModel(profile);
            int timeoutSeconds = profile.TimeoutSeconds >= 10 ? profile.TimeoutSeconds : 180;
            string effort = ResolveEffort(profile.ReasoningEffort);

            List<string> stages = BuildMultiStagePrompts(turn, options, profile, DefaultMaxChunkChars);

            if (stages.Count > 1)
            {
                return await SendMultiStageAsync(
                    stages, cli, model, effort, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            string singlePrompt = stages.Count == 1 ? stages[0] : BuildRequestBody(turn, options, profile);
            return await SendSingleStagePromptAsync(
                singlePrompt, cli, model, effort, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<string> SendMultiStageAsync(
            List<string> stages,
            string cli, string model, string effort, int timeoutSeconds, CancellationToken cancellationToken)
        {
            if (stages == null || stages.Count == 0)
            {
                return string.Empty;
            }

            await AntigravityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                LogSink.Info("antigravity: multi-stage request starting (" + stages.Count + " stages total)");

                string lastOutput = null;

                for (int i = 0; i < stages.Count; i++)
                {
                    bool isInitial = (i == 0);
                    string stagePrompt = stages[i];

                    StringBuilder sb = new StringBuilder();
                    sb.Append("--dangerously-skip-permissions");

                    if (!isInitial)
                    {
                        sb.Append(" --continue");
                    }

                    sb.Append(" --input-format text");

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

                    LogSink.Info("antigravity: executing stage " + (i + 1) + "/" + stages.Count
                        + " (prompt length=" + stagePrompt.Length + " chars, continue=" + (!isInitial) + ")");

                    ProcessResult res = await RunProcessAsync(cli, sb.ToString(), timeoutSeconds, cancellationToken, stagePrompt)
                        .ConfigureAwait(false);

                    if (res.ExitCode != 0)
                    {
                        string errMsg = !string.IsNullOrWhiteSpace(res.Error) ? res.Error : res.Output;
                        if (string.IsNullOrWhiteSpace(errMsg))
                        {
                            errMsg = "agy.exe exited with code " + res.ExitCode;
                        }
                        LogSink.Warn("antigravity stage " + (i + 1) + "/" + stages.Count + " error: " + errMsg);
                        throw new TransportException(res.ExitCode, "Stage " + (i + 1) + " failed: " + errMsg);
                    }

                    lastOutput = res.Output;
                    LogSink.Info("antigravity: stage " + (i + 1) + "/" + stages.Count + " completed ("
                        + (res.Output ?? string.Empty).Length + " chars reply)");
                }

                if (string.IsNullOrWhiteSpace(lastOutput))
                {
                    LogSink.Warn("antigravity final stage empty output");
                }

                return ParseReply(lastOutput);
            }
            finally
            {
                AntigravityLock.Release();
            }
        }

        private async Task<string> SendSingleStagePromptAsync(
            string prompt,
            string cli, string model, string effort, int timeoutSeconds, CancellationToken cancellationToken)
        {
            await AntigravityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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

                ProcessResult res = await RunProcessAsync(cli, sb.ToString(), timeoutSeconds, cancellationToken, prompt)
                    .ConfigureAwait(false);

                if (res.ExitCode != 0)
                {
                    string errMsg = !string.IsNullOrWhiteSpace(res.Error) ? res.Error : res.Output;
                    if (string.IsNullOrWhiteSpace(errMsg))
                    {
                        errMsg = "agy.exe exited with code " + res.ExitCode;
                    }
                    throw new TransportException(res.ExitCode, errMsg);
                }

                if (string.IsNullOrWhiteSpace(res.Output))
                {
                    if (!string.IsNullOrWhiteSpace(res.Error))
                    {
                        LogSink.Warn("antigravity: empty output, stderr: " + res.Error);
                        throw new TransportException(0, res.Error);
                    }
                }

                return ParseReply(res.Output);
            }
            finally
            {
                AntigravityLock.Release();
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

        /// <summary>
        /// Locates the agy.exe CLI executable on disk.
        /// Checks configured path, %LOCALAPPDATA%\agy\bin\agy.exe, and PATH.
        /// </summary>
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

                // If the user specified an explicit path (with directory separators),
                // do not silently fall back to LocalAppData/PATH — honor their configured path.
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
                        // Ignore malformed path entries in PATH environment variable.
                    }
                }
            }

            return !string.IsNullOrWhiteSpace(configured) ? configured.Trim().Trim('"', '\'').Trim() : "agy";
        }

        /// <summary>
        /// Resolves the model name to pass to agy.exe.
        /// Per-scope model takes precedence over global AntigravityModel.
        /// Automatically normalizes common typos (e.g. gemini-3.8-low -> gemini-3.8-flash-low).
        /// </summary>
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
            return "gemini-3.7-flash-high";
        }

        /// <summary>
        /// Normalizes common typos and display names into valid agy model identifiers.
        /// </summary>
        public static string NormalizeModelName(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return "gemini-3.7-flash-high";
            string trimmed = model.Trim().Trim('"', '\'').Trim();

            switch (trimmed.ToLowerInvariant())
            {
                // Typos missing "-flash-"
                case "gemini-3.8-low":
                case "gemini 3.8 flash (low)":
                case "gemini 3.8 flash low":
                    return "gemini-3.8-flash-low";

                case "gemini-3.8-medium":
                case "gemini 3.8 flash (medium)":
                case "gemini 3.8 flash medium":
                    return "gemini-3.8-flash-medium";

                case "gemini-3.8-high":
                case "gemini 3.8 flash (high)":
                case "gemini 3.8 flash high":
                    return "gemini-3.8-flash-high";

                case "gemini-3.7-low":
                case "gemini 3.7 flash (low)":
                case "gemini 3.7 flash low":
                    return "gemini-3.7-flash-low";

                case "gemini-3.7-medium":
                case "gemini 3.7 flash (medium)":
                case "gemini 3.7 flash medium":
                    return "gemini-3.7-flash-medium";

                case "gemini-3.7-high":
                case "gemini 3.7 flash (high)":
                case "gemini 3.7 flash high":
                    return "gemini-3.7-flash-high";

                case "gemini-3.6-low":
                case "gemini 3.6 flash (low)":
                case "gemini 3.6 flash low":
                    return "gemini-3.6-flash-low";

                case "gemini-3.6-medium":
                case "gemini 3.6 flash (medium)":
                case "gemini 3.6 flash medium":
                    return "gemini-3.6-flash-medium";

                case "gemini-3.6-high":
                case "gemini 3.6 flash (high)":
                case "gemini 3.6 flash high":
                    return "gemini-3.6-flash-high";

                case "gemini-3.1-pro-low":
                case "gemini 3.1 pro (low)":
                case "gemini 3.1 pro low":
                    return "gemini-3.1-pro-low";

                case "gemini-3.1-pro-high":
                case "gemini 3.1 pro (high)":
                case "gemini 3.1 pro high":
                    return "gemini-3.1-pro-high";

                case "claude-sonnet-4-6":
                case "claude sonnet 4.6 (thinking)":
                case "claude sonnet 4.6":
                    return "claude-sonnet-4-6";

                case "claude-opus-4-6-thinking":
                case "claude opus 4.6 (thinking)":
                case "claude opus 4.6":
                    return "claude-opus-4-6-thinking";

                case "gpt-oss-120b-medium":
                case "gpt-oss 120b (medium)":
                case "gpt-oss 120b":
                    return "gpt-oss-120b-medium";

                default:
                    return trimmed;
            }
        }

        /// <summary>
        /// Maps reasoning effort string to agy --effort value (low, medium, high, xhigh, max).
        /// </summary>
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

        /// <summary>
        /// Quotes and escapes a command line argument according to Win32 CommandLineToArgvW rules.
        /// </summary>
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

        /// <summary>
        /// Asynchronously executes an external CLI process with redirected UTF-8 I/O.
        /// </summary>
        public static async Task<ProcessResult> RunProcessAsync(
            string fileName, string arguments, int timeoutSeconds, CancellationToken cancellationToken,
            string standardInput = null)
        {
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
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            Process process = new Process { StartInfo = startInfo };
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    throw new TransportException(0, "Failed to start process: " + fileName);
                }
            }
            catch (Exception ex)
            {
                process.Dispose();
                throw new TransportException(0, "Could not start " + fileName + ": " + ex.Message);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (standardInput != null)
            {
                try
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(standardInput);
                    await process.StandardInput.BaseStream.WriteAsync(inputBytes, 0, inputBytes.Length).ConfigureAwait(false);
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

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
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
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                        // Process already terminated
                    }
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
                            throw new TransportException(0, "Antigravity CLI timed out after " + timeoutSeconds + "s.");
                        }
                        throw;
                    }
                }
            }

            process.WaitForExit();
            int exitCode = process.ExitCode;
            process.Dispose();

            return new ProcessResult
            {
                ExitCode = exitCode,
                Output = outputBuilder.ToString().Trim(),
                Error = errorBuilder.ToString().Trim()
            };
        }
    }
}
