using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace AIInfluencePrism.Routing
{
    /// <summary>
    /// Pure Passthrough Proxy dialogue history manager:
    /// - Zero Mutation Mode (default): passes the engine's original prompt 1-to-1 without touch.
    /// - Emergency Geometric Trim: activates ONLY if the prompt exceeds the safe CLI buffer (> 180 000 chars, e.g. PromptMaxHistory=300).
    ///   Cuts older middle dialogue turns while strictly preserving:
    ///   1. Header (world/scene context)
    ///   2. Last N turns in their 100% original, unmodified format
    ///   3. Trailing instructions / JSON schemas in their 100% original format
    ///   WITHOUT adding any synthetic banners, instructions, or altering [ABSOLUTE TRUTH] markers.
    /// </summary>
    public static class DialogueHistoryManager
    {
        /// <summary>
        /// Regular expression to identify dialogue turn boundaries across supported languages and formats.
        /// Matches:
        /// - "I (Apolitea, `lord_1_1_8`):"
        /// - "You (`main_hero`):"
        /// - "Lord / Player / You / User / Hero / MainHero / Игрок / Собеседник / Вы:"
        /// - Capitalized names: "Derthert:", "Derthert dey Meroc:" (rejects lowercase phrases like "in addition:", "note:")
        /// - "[Turn 1] / [Turn 100]:"
        /// - "Your last conversation was X days ago."
        /// - "Letter / Письмо / Message from ...:"
        /// </summary>
        public static readonly Regex TurnRegex = new Regex(
            @"(?:^|\r?\n)(?:[^\r\n]{1,80}?\s*\([^)\r\n]+?\)\s*:|(?i:(?:Player|You|User|Игрок|Собеседник|Hero|MainHero|Вы))\s*:|(?-i:[A-ZА-ЯЁ][A-Za-zА-Яа-яЁё0-9_\-]*(?:\s+[A-ZА-ЯЁ][A-Za-zА-Яа-яЁё0-9_\-]*){0,2})\s*:|\[(?:Turn\s*)?\d+\]|(?i:Your last conversation was \d+)|(?i:(?:Letter|Письмо|Message|Сообщение|From|От))\s+[^\r\n:]{1,40}:)",
            RegexOptions.Compiled);

        /// <summary>
        /// Regular expression specifically identifying the active player's utterance boundary.
        /// </summary>
        public static readonly Regex PlayerTurnRegex = new Regex(
            @"(?:^|\r?\n)(?<header>(?<speaker>[^\r\n:]{1,80}?\s*\([^)\r\n]*(?:main_hero|player)[^)\r\n]*\)|(?i:(?:Player|You|User|Игрок|Собеседник|Hero|MainHero|Вы)))\s*:)",
            RegexOptions.Compiled);

        /// <summary>
        /// Structural separator line of the AIInfluence engine.
        /// Isolates dialogue history from trailing technical instructions and JSON schemas.
        /// Handles both CRLF (\r\n) on Windows and LF (\n) on Unix.
        /// </summary>
        public static readonly Regex SeparatorRegex = new Regex(
            @"(?m)^[ \t]*={4,}[ \t]*\r?$",
            RegexOptions.Compiled);

        /// <summary>
        /// Threshold for emergency dialogue pruning (~180 000 chars).
        /// Standard prompts (<= 180 000 chars) are passed with ZERO MUTATION.
        /// Emergency geometric trimming triggers ONLY if the prompt exceeds this safe buffer.
        /// </summary>
        public const int EmergencyPruningThresholdChars = 180000;

        /// <summary>
        /// Number of recent dialogue turns retained when emergency geometric trimming is triggered.
        /// Default: 12 turns (6 full dialogue exchanges).
        /// </summary>
        public const int DefaultRetainedTurnsOnEmergency = 12;

        /// <summary>
        /// Default recent turns for backward compatibility.
        /// </summary>
        public const int DefaultRecentTurns = 15;

        /// <summary>
        /// Maximum safe character budget for dynamic dialogue history in a single-stage request (~45 000 chars).
        /// </summary>
        public const int MaxSafeUserCharBudget = 45000;

        /// <summary>
        /// Zero Mutation pass-through by default.
        /// If content exceeds EmergencyPruningThresholdChars (> 180 000 chars), applies emergency geometric trimming.
        /// </summary>
        public static string ApplyNativeAiInfluenceMemory(
            string userContent,
            int maxRecentTurns = DefaultRetainedTurnsOnEmergency,
            IReadOnlyList<string> memories = null)
        {
            if (string.IsNullOrEmpty(userContent))
            {
                return userContent;
            }

            // Zero Mutation Mode by default:
            // If the content is within the safe CLI buffer (<= 180 000 chars),
            // DO NOT TOUCH THE TEXT AT ALL. Return 100% pure engine prompt.
            if (userContent.Length <= EmergencyPruningThresholdChars)
            {
                return userContent;
            }

            return ApplyEmergencyGeometricTrim(userContent, maxRecentTurns);
        }

        /// <summary>
        /// Pure geometric emergency trim when dialogue history exceeds safe CLI buffer (> 180 000 chars).
        /// Cuts older middle dialogue turns while strictly preserving:
        /// 1. Dialogue header (lore / context intro)
        /// 2. Last N recent turns verbatim in their original format
        /// 3. Trailing instructions / JSON schemas verbatim
        /// WITHOUT adding any synthetic banners, Russian instructions, or altering [ABSOLUTE TRUTH] markers.
        /// </summary>
        public static string ApplyEmergencyGeometricTrim(
            string userContent,
            int maxRecentTurns = DefaultRetainedTurnsOnEmergency)
        {
            if (string.IsNullOrEmpty(userContent) || maxRecentTurns <= 0)
            {
                return userContent;
            }

            // 1. Structural separation of trailing instructions / JSON schemas
            string historyBlock = userContent;
            string instructionsBlock = string.Empty;

            MatchCollection sepMatches = SeparatorRegex.Matches(userContent);
            if (sepMatches.Count > 0)
            {
                Match lastSep = sepMatches[sepMatches.Count - 1];
                if (lastSep.Index > 0)
                {
                    int splitIndex = lastSep.Index;

                    // If preceded immediately by another separator line, capture above it
                    for (int i = sepMatches.Count - 2; i >= 0; i--)
                    {
                        Match prevSep = sepMatches[i];
                        if (prevSep.Index > 0 && (splitIndex - (prevSep.Index + prevSep.Length)) < 200)
                        {
                            string between = userContent.Substring(prevSep.Index + prevSep.Length, splitIndex - (prevSep.Index + prevSep.Length));
                            if (!TurnRegex.IsMatch(between))
                            {
                                splitIndex = prevSep.Index;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    historyBlock = userContent.Substring(0, splitIndex).TrimEnd();
                    instructionsBlock = userContent.Substring(splitIndex); // Preserves original separators and instructions!
                }
            }

            MatchCollection matches = TurnRegex.Matches(historyBlock);
            if (matches.Count == 0)
            {
                if (historyBlock.Length > MaxSafeUserCharBudget)
                {
                    int cut = historyBlock.Length - MaxSafeUserCharBudget;
                    int nl = historyBlock.IndexOf('\n', cut);
                    int safeStart = (nl > cut && nl < historyBlock.Length - 1000) ? (nl + 1) : cut;
                    string prunedHistory = historyBlock.Substring(safeStart);
                    return string.IsNullOrEmpty(instructionsBlock) ? prunedHistory : (prunedHistory + "\n\n" + instructionsBlock);
                }
                return userContent;
            }

            if (matches.Count <= maxRecentTurns)
            {
                // Already has fewer turns than requested cutoff
                return userContent;
            }

            int firstTurnIndex = matches[0].Index;
            string header = firstTurnIndex > 0 ? historyBlock.Substring(0, firstTurnIndex).TrimEnd() : string.Empty;

            int keepStartIndex = matches[matches.Count - maxRecentTurns].Index;
            string retainedTurnsText = historyBlock.Substring(keepStartIndex).Trim();

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(header))
            {
                sb.AppendLine(header);
                sb.AppendLine();
            }

            sb.Append(retainedTurnsText);

            if (!string.IsNullOrEmpty(instructionsBlock))
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(instructionsBlock.TrimStart('\r', '\n'));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Attempts to split a full prompt into static system/lore context and dynamic dialogue history.
        /// </summary>
        public static bool TrySplitPrompt(string prompt, out string systemContent, out string userContent)
        {
            systemContent = null;
            userContent = null;
            if (string.IsNullOrEmpty(prompt)) return false;

            string[] markers = new string[]
            {
                "### ТЕКУЩАЯ ИСТОРИЯ ДИАЛОГА",
                "### Conversation history ###",
                "### Conversation History ###",
                "### Group conversation ###",
                "### Dialogue history ###",
                "### CURRENT CONVERSATION HISTORY",
                "### Conversation history",
                "### Dialogue history",
                "### ИСТОРИЯ ДИАЛОГА"
            };

            foreach (string marker in markers)
            {
                int idx = prompt.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    int sep = prompt.LastIndexOf("============================================================", idx, StringComparison.Ordinal);
                    int split = (sep > 0 && (idx - sep) < 200) ? sep : idx;

                    systemContent = prompt.Substring(0, split).TrimEnd();
                    userContent = prompt.Substring(split).TrimStart();
                    return true;
                }
            }

            // Fallback: invoke AIInfluence's internal ExtractLastPlayerMessage if available
            try
            {
                Type aiClient = Hook.TargetScanner.EntryType;
                if (aiClient != null)
                {
                    MethodInfo m = aiClient.GetMethod("ExtractLastPlayerMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (m != null)
                    {
                        object res = m.Invoke(null, new object[] { prompt });
                        if (res != null)
                        {
                            Type resType = res.GetType();
                            FieldInfo f1 = resType.GetField("Item1");
                            FieldInfo f2 = resType.GetField("Item2");
                            FieldInfo f3 = resType.GetField("Item3");
                            if (f1 != null && f2 != null && f3 != null)
                            {
                                string sys = f1.GetValue(res) as string;
                                string usr = f2.GetValue(res) as string;
                                bool ok = f3.GetValue(res) is bool b && b;
                                if (ok && !string.IsNullOrEmpty(usr))
                                {
                                    systemContent = sys?.TrimEnd();
                                    userContent = usr?.TrimStart();
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Applies targeted structural anchoring to the active player turn:
        /// 1. If total prompt length exceeds safe buffer (> 180,000 chars), applies emergency geometric trim
        ///    using the specified emergencyLimit (default: 12 turns).
        /// 2. Locates the last player utterance in the dialogue history and inserts "[АКТУАЛЬНАЯ РЕПЛИКА] "
        ///    before the speaker header.
        /// 3. Appends a strict reply target anchor at the very end of the prompt:
        ///    "\n\n[АКТУАЛЬНАЯ РЕПЛИКА ДЛЯ ОТВЕТА]: Твой ответ (response) и ход мыслей (internal_thoughts) должны быть прямой реакцией СТРОГО на последнюю реплику игрока: \"{PlayerName}: {Snippet}...\"\n"
        /// 4. Historical [ABSOLUTE TRUTH:] markers are preserved intact.
        /// </summary>
        public static string ApplyActiveTurnAnchors(
            string prompt,
            int emergencyLimit = DefaultRetainedTurnsOnEmergency)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                return prompt;
            }

            // 1. Emergency geometric trimming if prompt exceeds safe CLI buffer
            if (prompt.Length > EmergencyPruningThresholdChars)
            {
                prompt = ApplyEmergencyGeometricTrim(prompt, emergencyLimit);
            }

            // 2. Identify dialogue history boundary (before trailing technical instructions)
            int historyBoundary = prompt.Length;
            MatchCollection seps = SeparatorRegex.Matches(prompt);
            if (seps.Count > 0)
            {
                historyBoundary = seps[seps.Count - 1].Index;
            }
            int step5Idx = prompt.IndexOf("**ШАГ 5:", StringComparison.OrdinalIgnoreCase);
            if (step5Idx > 0 && step5Idx < historyBoundary)
            {
                historyBoundary = step5Idx;
            }
            int partIdx = prompt.IndexOf("### Conversation participant", StringComparison.OrdinalIgnoreCase);
            if (partIdx > 0 && partIdx < historyBoundary)
            {
                historyBoundary = partIdx;
            }

            // 3. Find the last player turn in the history block
            Match targetTurn = null;
            MatchCollection playerMatches = PlayerTurnRegex.Matches(prompt);
            for (int i = playerMatches.Count - 1; i >= 0; i--)
            {
                if (playerMatches[i].Index < historyBoundary)
                {
                    targetTurn = playerMatches[i];
                    break;
                }
            }

            if (targetTurn == null)
            {
                // Fallback to any speaker that does not look like the active NPC ("I (")
                MatchCollection allTurns = TurnRegex.Matches(prompt);
                for (int i = allTurns.Count - 1; i >= 0; i--)
                {
                    if (allTurns[i].Index < historyBoundary)
                    {
                        string val = allTurns[i].Value.TrimStart('\r', '\n', ' ');
                        if (!val.StartsWith("I (", StringComparison.OrdinalIgnoreCase) &&
                            !val.StartsWith("I(", StringComparison.OrdinalIgnoreCase))
                        {
                            targetTurn = allTurns[i];
                            break;
                        }
                    }
                }
            }

            if (targetTurn == null)
            {
                return prompt;
            }

            // 4. Extract player speaker name and content snippet
            Group headerGrp = targetTurn.Groups["header"];
            int headerIdx = headerGrp.Success ? headerGrp.Index : targetTurn.Index;
            string headerText = headerGrp.Success ? headerGrp.Value : targetTurn.Value;

            string speaker = targetTurn.Groups["speaker"].Success ? targetTurn.Groups["speaker"].Value : headerText;
            string cleanSpeaker = speaker.Split('(')[0].Trim(' ', '*', '`', ':');
            if (string.IsNullOrWhiteSpace(cleanSpeaker))
            {
                cleanSpeaker = "Игрок";
            }

            int contentStart = headerIdx + headerText.Length;
            int nextBoundary = historyBoundary;
            Match nextTurn = TurnRegex.Match(prompt, contentStart);
            if (nextTurn.Success && nextTurn.Index < nextBoundary)
            {
                nextBoundary = nextTurn.Index;
            }

            int contentLen = Math.Max(0, nextBoundary - contentStart);
            string rawContent = prompt.Substring(contentStart, contentLen).Trim();
            string cleanContent = Regex.Replace(rawContent, @"\s+", " ").Trim();
            string snippet = cleanContent.Length > 100 ? cleanContent.Substring(0, 100) : cleanContent;

            // 5. Insert "[АКТУАЛЬНАЯ РЕПЛИКА] " before the speaker header if not already present
            const string activeMarker = "[АКТУАЛЬНАЯ РЕПЛИКА] ";
            if (!prompt.Contains(activeMarker))
            {
                prompt = prompt.Substring(0, headerIdx) + activeMarker + prompt.Substring(headerIdx);
            }

            // 6. Append strict reply target anchor to the very tail of the prompt
            const string tailHeader = "[АКТУАЛЬНАЯ РЕПЛИКА ДЛЯ ОТВЕТА]:";
            if (!prompt.Contains(tailHeader))
            {
                string tailAnchor = "\n\n" + tailHeader + " Твой ответ (response) и ход мыслей (internal_thoughts) должны быть прямой реакцией СТРОГО на последнюю реплику игрока: \""
                    + cleanSpeaker + ": " + snippet + "...\"\n"
                    + "ВАЖНО: Отвечай чистым диалоговым текстом или чистым валидным JSON без блоков кода markdown (никогда не используй ```json или ```).\n";
                prompt = prompt.TrimEnd() + tailAnchor;
            }

            return prompt;
        }
    }
}
