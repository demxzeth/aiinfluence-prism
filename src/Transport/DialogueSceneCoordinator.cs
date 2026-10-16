using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIInfluencePrism.Config;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;

namespace AIInfluencePrism.Transport
{
    /// <summary>
    /// Coordinates dialogue image generation for Mount & Blade II: Bannerlord:
    /// 1. Concurrent Generation: Triggers background image generation simultaneously
    ///    with LLM text generation so dialogue text and scene image generate in parallel,
    ///    eliminating sequential waiting delays.
    /// 2. Game-Accurate Bannerlord Prompt Optimization: Sanitizes and enriches the
    ///    image prompt with concrete in-game hero appearance, equipment, culture,
    ///    settlement environment, and dialogue context instead of confusing reference sheet instructions.
    /// </summary>
    public static class DialogueSceneCoordinator
    {
        private static readonly object SyncLock = new object();
        private static Task<byte[]> _pendingImageTask;
        private static DateTime _pendingTaskTimestamp = DateTime.MinValue;
        private static string _pendingHeroId;

        private static IList<byte[]> _cachedDialogueReferences;
        private static string _cachedHeroId;

        /// <summary>
        /// Stores character reference image sheets captured during dialogue so that subsequent
        /// concurrent generation turns in the same conversation retain exact character likeness.
        /// </summary>
        public static void SetDialogueReferences(string heroId, IList<byte[]> references)
        {
            if (references == null || references.Count == 0) return;
            lock (SyncLock)
            {
                _cachedHeroId = heroId ?? "default_dialogue";
                _cachedDialogueReferences = references;
            }
        }

        /// <summary>
        /// Retrieves character reference images for the given hero if previously cached.
        /// </summary>
        public static IList<byte[]> GetDialogueReferences(string heroId)
        {
            lock (SyncLock)
            {
                if (_cachedDialogueReferences != null &&
                    (_cachedHeroId == null || string.Equals(_cachedHeroId, heroId, StringComparison.OrdinalIgnoreCase)))
                {
                    return _cachedDialogueReferences;
                }
                return null;
            }
        }

        /// <summary>
        /// Initiates background image generation concurrently with LLM text generation.
        /// Strictly requires reference images to preserve character likeness in dialogue.
        /// </summary>
        public static void StartConcurrentDialogueImage(string llmPrompt, string userMessage = null)
        {
            try
            {
                if (!ProfileBook.Current.MasterEnabled ||
                    !ProfileBook.Current.Scope(ScopeId.Dialogue).GenerateImages)
                {
                    return;
                }

                object npc = ResolveConversationHero();
                object player = ResolveMainHero();

                string heroId = GetHeroStringId(npc) ?? "default_dialogue";
                IList<byte[]> references = GetDialogueReferences(heroId);

                // For dialogue, we strictly avoid text-to-image without character reference sheets.
                // If references have not been captured yet for this hero, defer to the first turn's
                // render pipeline which passes the freshly rendered reference sheets.
                if (references == null || references.Count == 0)
                {
                    LogSink.Info($"[Concurrent Image] No cached character references yet for {heroId}; deferring to capture with references.");
                    return;
                }

                string imagePrompt = BuildOptimizedDialoguePrompt(llmPrompt, userMessage, npc, player);
                if (string.IsNullOrWhiteSpace(imagePrompt))
                {
                    return;
                }

                lock (SyncLock)
                {
                    _pendingHeroId = heroId;
                    _pendingTaskTimestamp = DateTime.UtcNow;

                    string heroName = GetHeroName(npc) ?? "NPC";
                    LogSink.Always($"[Concurrent Image] Spawning background dialogue scene generation for {heroName} with {references.Count} references concurrently with text...");

                    _pendingImageTask = Task.Run(async () =>
                    {
                        try
                        {
                            return await ImagePipeline.GenerateImageAsync(
                                imagePrompt, 1024, 1024, "DialogueSceneImage", references, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            LogSink.Warn($"[Concurrent Image] Background generation failed: {ex.Message}");
                            return null;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogSink.Error("[Concurrent Image] Failed to start concurrent generation", ex);
            }
        }

        /// <summary>
        /// Retrieves and consumes the pending pre-warmed image generation task if available and fresh.
        /// </summary>
        public static bool TryTakePendingImageTask(out Task<byte[]> task)
        {
            lock (SyncLock)
            {
                task = null;
                if (_pendingImageTask == null)
                {
                    return false;
                }

                // Discard tasks older than 90 seconds
                if ((DateTime.UtcNow - _pendingTaskTimestamp).TotalSeconds > 90)
                {
                    _pendingImageTask = null;
                    _pendingHeroId = null;
                    return false;
                }

                task = _pendingImageTask;
                _pendingImageTask = null;
                _pendingHeroId = null;
                return true;
            }
        }

        /// <summary>
        /// Resets any pending pre-warmed image task.
        /// </summary>
        public static void ClearPending()
        {
            lock (SyncLock)
            {
                _pendingImageTask = null;
                _pendingHeroId = null;
                _pendingTaskTimestamp = DateTime.MinValue;
                _cachedDialogueReferences = null;
                _cachedHeroId = null;
            }
        }

        /// <summary>
        /// Sanitizes and enriches an existing prompt from AIInfluence or caller,
        /// removing confusing multi-angle reference sheet directives that ruin text-to-image models
        /// and injecting game-accurate character and location context.
        /// </summary>
        public static string SanitizeAndEnrichPrompt(string rawPrompt, IList<byte[]> references = null)
        {
            if (string.IsNullOrWhiteSpace(rawPrompt))
            {
                return string.Empty;
            }

            string result = rawPrompt;

            // 1. Remove confusing reference sheet instructions that cause text-to-image models to draw floating head sheets
            // Only keep reference instructions if references are actually provided to Gemini multimodal
            bool hasUsableReferences = references != null && references.Count > 0;
            if (!hasUsableReferences)
            {
                // Strip the entire REFERENCE IMAGES block
                result = Regex.Replace(result,
                    @"REFERENCE IMAGES\s*\(mandatory\):[\s\S]*?(?=(CONTINUITY|SETTING|CHARACTERS|DIALOGUE|IMAGE REQUIREMENTS|PHYSICAL AND SPATIAL|\Z))",
                    "", RegexOptions.IgnoreCase);

                // Strip individual sentences mentioning attached reference images
                result = Regex.Replace(result,
                    @"-?\s*Match clothing, gear and appearance from the attached reference images\.?",
                    "", RegexOptions.IgnoreCase);

                result = Regex.Replace(result,
                    @"-?\s*The helmet is hidden in the face sheet[^\r\n]*",
                    "", RegexOptions.IgnoreCase);

                result = Regex.Replace(result,
                    @"-?\s*Keep both characters clearly recognizable and faithful to these references[^\r\n]*",
                    "", RegexOptions.IgnoreCase);

                result = Regex.Replace(result,
                    @"-?\s*If a previously generated scene is attached[^\r\n]*",
                    "", RegexOptions.IgnoreCase);
            }

            // 2. Enrich CHARACTERS section if heroes can be resolved and prompt lacks detailed descriptions
            object npc = ResolveConversationHero();
            object player = ResolveMainHero();

            if (npc != null || player != null)
            {
                string npcDesc = npc != null ? GetHeroVisualDescription(npc) : null;
                string playerDesc = player != null ? GetHeroVisualDescription(player) : null;

                if (!string.IsNullOrEmpty(npcDesc) || !string.IsNullOrEmpty(playerDesc))
                {
                    StringBuilder charBlock = new StringBuilder();
                    charBlock.AppendLine("CHARACTERS IN SCENE (appearance, attire & gear):");
                    if (!string.IsNullOrEmpty(npcDesc))
                    {
                        charBlock.AppendLine("- " + npcDesc);
                    }
                    if (!string.IsNullOrEmpty(playerDesc))
                    {
                        charBlock.AppendLine("- " + playerDesc);
                    }

                    // Replace existing generic CHARACTERS section or prepend
                    if (Regex.IsMatch(result, @"CHARACTERS:[\s\S]*?(?=(DIALOGUE|SETTING|IMAGE REQUIREMENTS|PHYSICAL AND SPATIAL|\Z))", RegexOptions.IgnoreCase))
                    {
                        result = Regex.Replace(result,
                            @"CHARACTERS:[\s\S]*?(?=(DIALOGUE|SETTING|IMAGE REQUIREMENTS|PHYSICAL AND SPATIAL|\Z))",
                            charBlock.ToString() + "\n", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        result = charBlock.ToString() + "\n" + result;
                    }
                }
            }

            // 3. Enrich SETTING section if settlement/location is available
            string locationDesc = GetLocationDescription();
            if (!string.IsNullOrEmpty(locationDesc))
            {
                if (result.IndexOf("{location}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = result.Replace("{location}", locationDesc);
                }
                else if (result.IndexOf("Location: Unknown", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = result.Replace("Location: Unknown", "Location: " + locationDesc);
                }
            }

            // 4. Clean up any leftover empty brackets or template placeholders
            result = result.Replace("{previous_scene}", "");
            result = Regex.Replace(result, @"\n{3,}", "\n\n");

            return result.Trim();
        }

        /// <summary>
        /// Builds a rich, game-accurate dialogue image prompt from scratch for concurrent generation.
        /// </summary>
        public static string BuildOptimizedDialoguePrompt(string llmPrompt, string userMessage, object npc, object player)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("A cinematic dialogue scene set in the world of Mount & Blade II: Bannerlord (Calradia).");
            sb.AppendLine("Late medieval / early feudal grounded historical fiction — gritty, lived-in realism, authentic Calradia atmosphere.");
            sb.AppendLine();

            // Setting
            string location = GetLocationDescription();
            sb.AppendLine("SETTING:");
            sb.AppendLine("Location: " + (string.IsNullOrEmpty(location) ? "Medieval castle great hall / encampment in Calradia" : location));
            sb.AppendLine();

            // Characters
            sb.AppendLine("CHARACTERS:");
            if (npc != null)
            {
                sb.AppendLine("- " + GetHeroVisualDescription(npc));
            }
            else
            {
                sb.AppendLine("- A medieval Calradian noble / warrior in period armor");
            }

            if (player != null)
            {
                sb.AppendLine("- " + GetHeroVisualDescription(player));
            }
            else
            {
                sb.AppendLine("- The player character, a mercenary captain in mail armor");
            }
            sb.AppendLine();

            // Dialogue / Context
            sb.AppendLine("SCENE CONTEXT & DIALOGUE:");
            string extractedDialogue = ExtractDialogueSnippet(llmPrompt, userMessage);
            if (!string.IsNullOrWhiteSpace(extractedDialogue))
            {
                sb.AppendLine(extractedDialogue);
            }
            else
            {
                sb.AppendLine("The two characters are standing face to face in an intense, dramatic conversation.");
            }
            sb.AppendLine();

            // Image requirements
            sb.AppendLine("IMAGE REQUIREMENTS:");
            sb.AppendLine("- Wide cinematic composition (16:9) showing both characters interacting.");
            sb.AppendLine("- Dramatic, realistic lighting matching the environment (firelight, torches or natural sunlight).");
            sb.AppendLine("- Natural body posture, gestures and facial expressions that reflect the dialogue tone.");
            sb.AppendLine("- Strictly pre-industrial: swords, mail, lamellar, linen, wool, iron. No modern elements, UI, speech bubbles, or text.");
            sb.AppendLine("- Highly detailed, sharp focus, anatomical and physical coherence.");

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Generates a detailed visual description of a Hero based on their in-game properties, culture, and equipment.
        /// </summary>
        public static string GetHeroVisualDescription(object hero)
        {
            if (hero == null)
            {
                return string.Empty;
            }

            try
            {
                Type heroType = hero.GetType();
                StringBuilder sb = new StringBuilder();

                string name = GetHeroName(hero) ?? "Warrior";
                string culture = GetHeroCulture(hero) ?? "Calradian";
                bool isFemale = GetPropertyValue<bool>(hero, "IsFemale");
                string gender = isFemale ? "female" : "male";

                float ageFloat = GetPropertyValue<float>(hero, "Age");
                int age = Math.Max(18, (int)ageFloat);

                string title = ResolveHeroRoleTitle(hero);
                sb.Append($"{name} ({age}-year-old {culture} {gender} {title})");

                // Equipment: battle equipment or civilian equipment
                object eq = GetPropertyValue<object>(hero, "BattleEquipment") ??
                            GetPropertyValue<object>(hero, "CivilianEquipment");

                if (eq != null)
                {
                    List<string> gearParts = new List<string>();

                    string body = GetEquipmentItemName(eq, 12); // EquipmentIndex.Body = 12
                    if (!string.IsNullOrEmpty(body))
                    {
                        gearParts.Add("wearing " + body);
                    }

                    string cape = GetEquipmentItemName(eq, 15); // EquipmentIndex.Cape = 15
                    if (!string.IsNullOrEmpty(cape))
                    {
                        gearParts.Add(cape);
                    }

                    string head = GetEquipmentItemName(eq, 8); // EquipmentIndex.Head = 8
                    if (!string.IsNullOrEmpty(head))
                    {
                        gearParts.Add(head);
                    }

                    string weapon0 = GetEquipmentItemName(eq, 1); // EquipmentIndex.Weapon0 = 1
                    string weapon1 = GetEquipmentItemName(eq, 2); // EquipmentIndex.Weapon1 = 2
                    string weapon = !string.IsNullOrEmpty(weapon0) ? weapon0 : weapon1;
                    if (!string.IsNullOrEmpty(weapon))
                    {
                        gearParts.Add("carrying " + weapon);
                    }

                    if (gearParts.Count > 0)
                    {
                        sb.Append(", ").Append(string.Join(", ", gearParts));
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                LogSink.Warn($"[DialogueSceneCoordinator] Failed to get hero description: {ex.Message}");
                return GetHeroName(hero) ?? "Character";
            }
        }

        private static string GetEquipmentItemName(object equipment, int slotIndex)
        {
            try
            {
                if (equipment == null) return null;
                Type eqType = equipment.GetType();

                // ItemIndex indexer equipment[index]
                PropertyInfo itemProp = eqType.GetProperty("Item", new[] { typeof(int) });
                object eqElement = null;

                if (itemProp != null)
                {
                    eqElement = itemProp.GetValue(equipment, new object[] { slotIndex });
                }
                else
                {
                    // Try indexer with EquipmentIndex enum type
                    foreach (var prop in eqType.GetProperties())
                    {
                        var indexParams = prop.GetIndexParameters();
                        if (indexParams.Length == 1 && indexParams[0].ParameterType.IsEnum)
                        {
                            object enumVal = Enum.ToObject(indexParams[0].ParameterType, slotIndex);
                            eqElement = prop.GetValue(equipment, new object[] { enumVal });
                            break;
                        }
                    }
                }

                if (eqElement != null)
                {
                    object item = GetPropertyValue<object>(eqElement, "Item");
                    if (item != null)
                    {
                        object itemName = GetPropertyValue<object>(item, "Name");
                        return itemName != null ? itemName.ToString() : item.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ResolveHeroRoleTitle(object hero)
        {
            try
            {
                bool isLeader = GetPropertyValue<bool>(hero, "IsFactionLeader");
                bool isFemale = GetPropertyValue<bool>(hero, "IsFemale");
                if (isLeader)
                {
                    return isFemale ? "queen / ruler" : "king / warlord";
                }

                bool isLord = GetPropertyValue<bool>(hero, "IsLord");
                if (isLord)
                {
                    return isFemale ? "noble lady" : "nobleman / lord";
                }

                bool isMerchant = GetPropertyValue<bool>(hero, "IsMerchant");
                if (isMerchant)
                {
                    return "wealthy merchant";
                }

                bool isWanderer = GetPropertyValue<bool>(hero, "IsWanderer");
                if (isWanderer)
                {
                    return "veteran mercenary companion";
                }
            }
            catch { }
            return "commander / warrior";
        }

        /// <summary>
        /// Gets a human-readable description of current settlement, scene, time of day and season.
        /// </summary>
        public static string GetLocationDescription()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                object settlement = ResolveCurrentSettlement();
                if (settlement != null)
                {
                    object nameObj = GetPropertyValue<object>(settlement, "Name");
                    string name = nameObj != null ? nameObj.ToString() : "Settlement";

                    object cultureObj = GetPropertyValue<object>(settlement, "Culture");
                    object cultureNameObj = cultureObj != null ? GetPropertyValue<object>(cultureObj, "Name") : null;
                    string culture = cultureNameObj != null ? cultureNameObj.ToString() : "Calradian";

                    bool isCastle = GetPropertyValue<bool>(settlement, "IsCastle");
                    bool isTown = GetPropertyValue<bool>(settlement, "IsTown");
                    bool isVillage = GetPropertyValue<bool>(settlement, "IsVillage");

                    if (isCastle)
                    {
                        sb.Append($"Inside the great stone keep of {name} ({culture} castle)");
                    }
                    else if (isTown)
                    {
                        sb.Append($"In the tavern or lord's hall of {name} ({culture} walled city)");
                    }
                    else if (isVillage)
                    {
                        sb.Append($"In the rustic village of {name} ({culture} rural settlement)");
                    }
                    else
                    {
                        sb.Append($"In {name} ({culture} settlement)");
                    }
                }
                else
                {
                    sb.Append("In a military war encampment with canvas tents and watch fires");
                }

                // Time of day & Season from CampaignTime
                try
                {
                    Type ctType = Type.GetType("TaleWorlds.CampaignSystem.CampaignTime, TaleWorlds.CampaignSystem");
                    if (ctType != null)
                    {
                        PropertyInfo nowProp = ctType.GetProperty("Now", BindingFlags.Public | BindingFlags.Static);
                        if (nowProp != null)
                        {
                            object now = nowProp.GetValue(null);
                            if (now != null)
                            {
                                float hour = GetPropertyValue<float>(now, "CurrentHourInDay");
                                string tod;
                                if (hour >= 5f && hour < 9f) tod = "early misty dawn";
                                else if (hour >= 9f && hour < 17f) tod = "broad daylight";
                                else if (hour >= 17f && hour < 21f) tod = "golden sunset / evening twilight";
                                else tod = "night time, warmly lit by blazing torches and fireplaces";

                                sb.Append(", at ").Append(tod);

                                PropertyInfo seasonProp = now.GetType().GetProperty("GetSeasonOfYear");
                                if (seasonProp != null)
                                {
                                    object seasonVal = seasonProp.GetValue(now);
                                    if (seasonVal != null)
                                    {
                                        sb.Append($", during {seasonVal}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                LogSink.Warn($"[DialogueSceneCoordinator] Failed to get location description: {ex.Message}");
                return "Medieval castle hall or encampment in Calradia";
            }
        }

        private static string ExtractDialogueSnippet(string llmPrompt, string userMessage)
        {
            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                return "Player says: \"" + userMessage.Trim() + "\"";
            }

            if (string.IsNullOrWhiteSpace(llmPrompt))
            {
                return string.Empty;
            }

            Match match = Regex.Match(llmPrompt, @"(?:DIALOGUE|Conversation|Last messages)[\s\S]*?(?:(?=\n\n)|$)", RegexOptions.IgnoreCase);
            if (match.Success && match.Length > 20)
            {
                string snippet = match.Value.Trim();
                if (snippet.Length > 400)
                {
                    snippet = snippet.Substring(snippet.Length - 400);
                }
                return snippet;
            }

            return string.Empty;
        }

        public static object ResolveConversationHero()
        {
            try
            {
                Type campType = Type.GetType("TaleWorlds.CampaignSystem.Campaign, TaleWorlds.CampaignSystem");
                if (campType != null)
                {
                    object camp = campType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (camp != null)
                    {
                        object convMgr = camp.GetType().GetProperty("ConversationManager")?.GetValue(camp);
                        if (convMgr != null)
                        {
                            object hero = convMgr.GetType().GetProperty("OneToOneConversationHero")?.GetValue(convMgr);
                            if (hero != null) return hero;
                        }
                    }
                }

                Type heroType = Type.GetType("TaleWorlds.CampaignSystem.Hero, TaleWorlds.CampaignSystem");
                if (heroType != null)
                {
                    return heroType.GetProperty("OneToOneConversationHero", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                }
            }
            catch { }
            return null;
        }

        private static object ResolveMainHero()
        {
            try
            {
                Type heroType = Type.GetType("TaleWorlds.CampaignSystem.Hero, TaleWorlds.CampaignSystem");
                if (heroType != null)
                {
                    return heroType.GetProperty("MainHero", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                }
            }
            catch { }
            return null;
        }

        private static object ResolveCurrentSettlement()
        {
            try
            {
                Type setType = Type.GetType("TaleWorlds.CampaignSystem.Settlements.Settlement, TaleWorlds.CampaignSystem");
                if (setType != null)
                {
                    object curr = setType.GetProperty("CurrentSettlement", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (curr != null) return curr;
                }

                object player = ResolveMainHero();
                if (player != null)
                {
                    return player.GetType().GetProperty("CurrentSettlement")?.GetValue(player);
                }
            }
            catch { }
            return null;
        }

        private static string GetHeroName(object hero)
        {
            if (hero == null) return null;
            object nameObj = GetPropertyValue<object>(hero, "Name");
            return nameObj != null ? nameObj.ToString() : null;
        }

        private static string GetHeroCulture(object hero)
        {
            if (hero == null) return null;
            object cultObj = GetPropertyValue<object>(hero, "Culture");
            if (cultObj != null)
            {
                object nameObj = GetPropertyValue<object>(cultObj, "Name");
                return nameObj != null ? nameObj.ToString() : null;
            }
            return null;
        }

        public static string GetHeroStringId(object hero)
        {
            if (hero == null) return null;
            return GetPropertyValue<string>(hero, "StringId");
        }

        private static T GetPropertyValue<T>(object target, string propertyName)
        {
            if (target == null || string.IsNullOrEmpty(propertyName)) return default;
            try
            {
                PropertyInfo pi = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null)
                {
                    object val = pi.GetValue(target);
                    if (val is T typed) return typed;
                    if (val != null && typeof(T) == typeof(string)) return (T)(object)val.ToString();
                    if (val != null && typeof(T) == typeof(float)) return (T)(object)Convert.ToSingle(val);
                    if (val != null && typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(val);
                }
            }
            catch { }
            return default;
        }
    }
}
