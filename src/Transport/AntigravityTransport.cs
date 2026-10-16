using System;
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

        public async Task<string> SendAsync(
            ChatTurn turn, ModelOptions options, Profile profile, CancellationToken cancellationToken)
        {
            string cli = ResolveCliPath(profile.AntigravityCliPath);
            string model = ResolveModel(profile);
            int timeoutSeconds = profile.TimeoutSeconds >= 10 ? profile.TimeoutSeconds : 180;
            string effortSource = options != null && !string.IsNullOrWhiteSpace(options.ReasoningEffort)
                ? options.ReasoningEffort
                : profile.ReasoningEffort;
            string effort = ResolveEffort(effortSource, model);

            // Two-stage request: built-in for Antigravity whenever turn has split content
            if (turn != null && turn.HasSplit)
            {
                return await SendTwoStageAsync(
                    turn, options, profile, cli, model, effort, timeoutSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await SendSingleStageAsync(
                turn, options, profile, cli, model, effort, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<string> SendTwoStageAsync(
            ChatTurn turn, ModelOptions options, Profile profile,
            string cli, string model, string effort, int timeoutSeconds, CancellationToken cancellationToken)
        {
            LogSink.Info("antigravity: two-stage request starting (stage 1 context="
                + turn.SplitSystem.Length + " chars, stage 2 dialogue=" + turn.SplitUser.Length + " chars)");

            // Stage 1: Send static context, world info, rules and character data.
            string stage1Prompt = "[System instruction: The following is background context, character data, game state, and rules for a video game dialogue scene. Read and memorize all information. Acknowledge with a brief confirmation; do not generate in-game dialogue yet.]\n\n"
                + turn.SplitSystem;

            string args1 = BuildCliArguments(model, effort, continueSession: false);

            ProcessResult res1 = await RunProcessAsync(cli, args1, timeoutSeconds, cancellationToken, stage1Prompt)
                .ConfigureAwait(false);

            if (res1.ExitCode != 0)
            {
                string errMsg = !string.IsNullOrWhiteSpace(res1.Error) ? res1.Error : res1.Output;
                if (string.IsNullOrWhiteSpace(errMsg))
                {
                    errMsg = "agy.exe exited with code " + res1.ExitCode;
                }
                LogSink.Warn("antigravity stage 1 error: " + errMsg);
                throw new TransportException(res1.ExitCode, "Stage 1 failed: " + errMsg);
            }

            LogSink.Info("antigravity: stage 1 completed (" + (res1.Output ?? string.Empty).Length + " chars reply), starting stage 2 (--continue)");

            // Stage 2: Send dialogue history and prompt for NPC response using --continue.
            string stage2Prompt = "[System instruction: Now generate the character's in-game response to the latest message in the dialogue history below, following all formatting rules and constraints provided earlier. Do not invoke any tools or external commands.]\n\n"
                + turn.SplitUser;

            string args2 = BuildCliArguments(model, effort, continueSession: true);

            ProcessResult res2 = await RunProcessAsync(cli, args2, timeoutSeconds, cancellationToken, stage2Prompt)
                .ConfigureAwait(false);

            if (res2.ExitCode != 0)
            {
                string errMsg = !string.IsNullOrWhiteSpace(res2.Error) ? res2.Error : res2.Output;
                if (string.IsNullOrWhiteSpace(errMsg))
                {
                    errMsg = "agy.exe exited with code " + res2.ExitCode;
                }
                LogSink.Warn("antigravity stage 2 error: " + errMsg);
                throw new TransportException(res2.ExitCode, "Stage 2 failed: " + errMsg);
            }

            if (string.IsNullOrWhiteSpace(res2.Output))
            {
                if (!string.IsNullOrWhiteSpace(res2.Error))
                {
                    LogSink.Warn("antigravity stage 2 empty output, stderr: " + res2.Error);
                    throw new TransportException(0, res2.Error);
                }
            }

            return ParseReply(res2.Output);
        }

        private async Task<string> SendSingleStageAsync(
            ChatTurn turn, ModelOptions options, Profile profile,
            string cli, string model, string effort, int timeoutSeconds, CancellationToken cancellationToken)
        {
            string prompt = BuildRequestBody(turn, options, profile);
            string args = BuildCliArguments(model, effort, continueSession: false);

            ProcessResult res = await RunProcessAsync(cli, args, timeoutSeconds, cancellationToken, prompt)
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
            return "gemini-3.8-flash-high";
        }

        /// <summary>
        /// Normalizes common typos and display names into valid agy model identifiers.
        /// </summary>
        public static string NormalizeModelName(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return "gemini-3.8-flash-high";
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
        /// Checks whether the model name/id already embeds a reasoning effort tier (e.g. -low, -medium, -high, -thinking).
        /// When true, passing --effort to agy.exe produces a CLI conflict error.
        /// </summary>
        public static bool ModelHasEmbeddedEffort(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            string lower = model.Trim().ToLowerInvariant();
            return lower.EndsWith("-low") ||
                   lower.EndsWith("-medium") ||
                   lower.EndsWith("-high") ||
                   lower.EndsWith("-xhigh") ||
                   lower.EndsWith("-max") ||
                   lower.Contains("-thinking") ||
                   lower.Contains("(low)") ||
                   lower.Contains("(medium)") ||
                   lower.Contains("(high)") ||
                   lower.Contains("(thinking)");
        }

        /// <summary>
        /// Maps reasoning effort string to agy --effort value (low, medium, high, xhigh, max).
        /// If the model already specifies reasoning effort embedded in its name/tier, returns null to avoid CLI conflict.
        /// </summary>
        public static string ResolveEffort(string effort, string model = null)
        {
            if (!string.IsNullOrEmpty(model) && ModelHasEmbeddedEffort(model))
            {
                return null;
            }

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
        /// Builds the command-line argument string for agy.exe, avoiding conflicts between --model and --effort.
        /// </summary>
        public static string BuildCliArguments(string model, string effort, bool continueSession = false)
        {
            StringBuilder sb = new StringBuilder();
            if (continueSession)
            {
                sb.Append("--continue --input-format text");
            }
            else
            {
                sb.Append("--input-format text");
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                sb.Append(" --model ");
                sb.Append(EscapeArgument(model));
            }

            string resolvedEffort = ResolveEffort(effort, model);
            if (!string.IsNullOrEmpty(resolvedEffort) && !ModelHasEmbeddedEffort(model))
            {
                sb.Append(" --effort ");
                sb.Append(resolvedEffort);
            }

            sb.Append(" --disable-slash-commands");
            return sb.ToString();
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
                WindowStyle = ProcessWindowStyle.Hidden,
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
