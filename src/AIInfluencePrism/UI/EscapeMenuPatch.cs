using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.ViewModelCollection.EscapeMenu;
using AIInfluencePrism.Logging;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// Injects "Prism Settings" button into TaleWorlds Bannerlord Escape menus
    /// (both World Map and Missions / Battles / Settlements) and ensures the escape
    /// menu frame height and padding are comfortably sized so buttons are never crowded.
    /// </summary>
    public static class EscapeMenuPatch
    {
        private static bool _installed;
        private static readonly object _syncLock = new object();

        public static bool Installed => _installed;

        public static void Install(Harmony harmony)
        {
            lock (_syncLock)
            {
                if (_installed || harmony == null)
                {
                    return;
                }

                int patched = 0;

                // 1. World Map Escape Menu: SandBox.View.Map.MapScreen.GetEscapeMenuItems()
                try
                {
                    Type mapScreenType = AccessTools.TypeByName("SandBox.View.Map.MapScreen");
                    if (mapScreenType != null)
                    {
                        MethodInfo mapGetItems = AccessTools.Method(mapScreenType, "GetEscapeMenuItems");
                        if (mapGetItems != null)
                        {
                            MethodInfo postfix = AccessTools.Method(typeof(EscapeMenuPatch), nameof(MapScreen_GetEscapeMenuItems_Postfix));
                            harmony.Patch(mapGetItems, postfix: new HarmonyMethod(postfix, Priority.Last));
                            patched++;
                            LogSink.Always("[EscapeMenuPatch] Patched SandBox.View.Map.MapScreen.GetEscapeMenuItems [Postfix, Priority.Last]");
                        }
                        else
                        {
                            LogSink.Warn("[EscapeMenuPatch] MapScreen.GetEscapeMenuItems method not found.");
                        }
                    }
                    else
                    {
                        LogSink.Info("[EscapeMenuPatch] SandBox.View.Map.MapScreen type not loaded yet.");
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Error("[EscapeMenuPatch] Error patching MapScreen: " + ex.Message, ex);
                }

                // 2. Missions / Battles / Scenes Escape Menu:
                // TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSingleplayerEscapeMenu.GetEscapeMenuItems()
                try
                {
                    Type missionMenuType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSingleplayerEscapeMenu");
                    if (missionMenuType != null)
                    {
                        MethodInfo missionGetItems = AccessTools.Method(missionMenuType, "GetEscapeMenuItems");
                        if (missionGetItems != null)
                        {
                            MethodInfo postfix = AccessTools.Method(typeof(EscapeMenuPatch), nameof(MissionEscapeMenu_GetEscapeMenuItems_Postfix));
                            harmony.Patch(missionGetItems, postfix: new HarmonyMethod(postfix, Priority.Last));
                            patched++;
                            LogSink.Always("[EscapeMenuPatch] Patched MissionGauntletSingleplayerEscapeMenu.GetEscapeMenuItems [Postfix, Priority.Last]");
                        }
                        else
                        {
                            LogSink.Warn("[EscapeMenuPatch] MissionGauntletSingleplayerEscapeMenu.GetEscapeMenuItems method not found.");
                        }
                    }
                    else
                    {
                        LogSink.Info("[EscapeMenuPatch] MissionGauntletSingleplayerEscapeMenu type not loaded yet.");
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Error("[EscapeMenuPatch] Error patching MissionEscapeMenu: " + ex.Message, ex);
                }

                // 3. GauntletLayer.LoadMovie: Stretch frame height and adjust padding for "EscapeMenu"
                try
                {
                    MethodInfo loadMovieMethod = AccessTools.Method(typeof(GauntletLayer), "LoadMovie", new Type[] { typeof(string), typeof(ViewModel) });
                    if (loadMovieMethod != null)
                    {
                        MethodInfo postfix = AccessTools.Method(typeof(EscapeMenuPatch), nameof(GauntletLayer_LoadMovie_Postfix));
                        harmony.Patch(loadMovieMethod, postfix: new HarmonyMethod(postfix));
                        patched++;
                        LogSink.Always("[EscapeMenuPatch] Patched GauntletLayer.LoadMovie [Postfix]");
                    }
                    else
                    {
                        LogSink.Warn("[EscapeMenuPatch] GauntletLayer.LoadMovie method not found.");
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Error("[EscapeMenuPatch] Error patching GauntletLayer.LoadMovie: " + ex.Message, ex);
                }

                // 4. SandBox.GauntletUI.Map.GauntletMapEscapeMenuView.CreateLayout
                try
                {
                    Type mapEscapeViewType = AccessTools.TypeByName("SandBox.GauntletUI.Map.GauntletMapEscapeMenuView");
                    if (mapEscapeViewType != null)
                    {
                        MethodInfo createLayoutMethod = AccessTools.Method(mapEscapeViewType, "CreateLayout");
                        if (createLayoutMethod != null)
                        {
                            MethodInfo postfix = AccessTools.Method(typeof(EscapeMenuPatch), nameof(GauntletMapEscapeMenuView_CreateLayout_Postfix));
                            harmony.Patch(createLayoutMethod, postfix: new HarmonyMethod(postfix));
                            patched++;
                            LogSink.Always("[EscapeMenuPatch] Patched GauntletMapEscapeMenuView.CreateLayout [Postfix]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogSink.Warn("[EscapeMenuPatch] Error patching GauntletMapEscapeMenuView: " + ex.Message);
                }

                if (patched > 0)
                {
                    _installed = true;
                    LogSink.Always($"[EscapeMenuPatch] Escape Menu injection active ({patched} hook(s)).");
                }
            }
        }

        public static void Reset()
        {
            lock (_syncLock)
            {
                _installed = false;
            }
        }

        public static void MapScreen_GetEscapeMenuItems_Postfix(object __instance, ref List<EscapeMenuItemVM> __result)
        {
            try
            {
                if (__result == null)
                {
                    return;
                }

                var item = new EscapeMenuItemVM(
                    new TextObject("{=Prism_Settings}Prism Settings"),
                    delegate
                    {
                        try
                        {
                            if (__instance != null)
                            {
                                MethodInfo closeMethod = AccessTools.Method(__instance.GetType(), "OnEscapeMenuToggled");
                                if (closeMethod != null)
                                {
                                    ParameterInfo[] pars = closeMethod.GetParameters();
                                    if (pars.Length == 0)
                                    {
                                        closeMethod.Invoke(__instance, null);
                                    }
                                    else if (pars.Length == 1 && pars[0].ParameterType == typeof(bool))
                                    {
                                        closeMethod.Invoke(__instance, new object[] { false });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogSink.Warn("[EscapeMenuPatch] MapScreen close failed: " + ex.Message);
                        }

                        PrismWebUiHost.Open();
                    },
                    null,
                    () => new Tuple<bool, TextObject>(false, null),
                    false);

                InsertPrismMenuItem(__result, item);
            }
            catch (Exception ex)
            {
                LogSink.Error("[EscapeMenuPatch] Error in MapScreen_GetEscapeMenuItems_Postfix: " + ex.Message, ex);
            }
        }

        public static void MissionEscapeMenu_GetEscapeMenuItems_Postfix(object __instance, ref List<EscapeMenuItemVM> __result)
        {
            try
            {
                if (__result == null)
                {
                    return;
                }

                var item = new EscapeMenuItemVM(
                    new TextObject("{=Prism_Settings}Prism Settings"),
                    delegate
                    {
                        try
                        {
                            if (__instance != null)
                            {
                                MethodInfo closeMethod = AccessTools.Method(__instance.GetType(), "OnEscapeMenuToggled", new Type[] { typeof(bool) });
                                if (closeMethod != null)
                                {
                                    closeMethod.Invoke(__instance, new object[] { false });
                                }
                                else
                                {
                                    MethodInfo onEscape = AccessTools.Method(__instance.GetType(), "OnEscape");
                                    onEscape?.Invoke(__instance, null);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogSink.Warn("[EscapeMenuPatch] Mission escape close failed: " + ex.Message);
                        }

                        PrismWebUiHost.Open();
                    },
                    null,
                    () => new Tuple<bool, TextObject>(false, null),
                    false);

                InsertPrismMenuItem(__result, item);
            }
            catch (Exception ex)
            {
                LogSink.Error("[EscapeMenuPatch] Error in MissionEscapeMenu_GetEscapeMenuItems_Postfix: " + ex.Message, ex);
            }
        }

        public static void GauntletLayer_LoadMovie_Postfix(GauntletLayer __instance, string movieName, object __result)
        {
            try
            {
                if (string.Equals(movieName, "EscapeMenu", StringComparison.OrdinalIgnoreCase))
                {
                    Widget root = null;
                    if (__result != null)
                    {
                        var movieProp = AccessTools.Property(__result.GetType(), "Movie");
                        var movie = movieProp?.GetValue(__result, null);
                        if (movie != null)
                        {
                            var rootProp = AccessTools.Property(movie.GetType(), "RootWidget");
                            root = rootProp?.GetValue(movie, null) as Widget;
                        }
                    }
                    if (root == null && __instance != null)
                    {
                        root = __instance.UIContext?.Root;
                    }
                    if (root != null)
                    {
                        AdjustEscapeMenuLayout(root);
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[EscapeMenuPatch] GauntletLayer_LoadMovie_Postfix error: " + ex.Message);
            }
        }

        public static void GauntletMapEscapeMenuView_CreateLayout_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                Widget root = null;
                var movieField = AccessTools.Field(__instance.GetType(), "_escapeMenuMovie");
                var movieIdent = movieField?.GetValue(__instance);
                if (movieIdent != null)
                {
                    var movieProp = AccessTools.Property(movieIdent.GetType(), "Movie");
                    var movie = movieProp?.GetValue(movieIdent, null);
                    if (movie != null)
                    {
                        var rootProp = AccessTools.Property(movie.GetType(), "RootWidget");
                        root = rootProp?.GetValue(movie, null) as Widget;
                    }
                }

                if (root == null)
                {
                    var layerField = AccessTools.Field(__instance.GetType(), "_layerAsGauntletLayer");
                    var layer = layerField?.GetValue(__instance) as GauntletLayer;
                    root = layer?.UIContext?.Root;
                }

                if (root != null)
                {
                    AdjustEscapeMenuLayout(root);
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[EscapeMenuPatch] GauntletMapEscapeMenuView_CreateLayout_Postfix error: " + ex.Message);
            }
        }

        /// <summary>
        /// Inserts "Prism Settings" directly AFTER "Настройки AI Influence" (aiInfluenceIndex + 1).
        /// If AI Influence is at index 2, Prism is placed at index 3 (between "Настройки AI Influence" and "Редактор Войск").
        /// If AI Influence is not found, checks for "Редактор Войск" and places Prism directly before it.
        /// Otherwise falls back to index 2.
        /// </summary>
        public static void InsertPrismMenuItem(List<EscapeMenuItemVM> list, EscapeMenuItemVM item)
        {
            if (list == null || item == null) return;

            // Remove any existing Prism Settings item so we can position it cleanly
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var it = list[i];
                if (it == null) continue;
                string s = GetItemText(it);
                if (!string.IsNullOrEmpty(s) && s.IndexOf("Prism Settings", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.RemoveAt(i);
                }
            }

            int aiIdx = -1;
            int troopEditorIdx = -1;

            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) continue;

                if (IsAiInfluenceItem(it))
                {
                    aiIdx = i;
                    break;
                }

                if (troopEditorIdx < 0 && IsTroopEditorItem(it))
                {
                    troopEditorIdx = i;
                }
            }

            if (aiIdx >= 0)
            {
                int insertPos = aiIdx + 1;
                list.Insert(insertPos, item);
                LogSink.Always($"[EscapeMenuPatch] Injected 'Prism Settings' directly after AI Influence button at index {insertPos} (between AI Influence at {aiIdx} and next item).");
            }
            else if (troopEditorIdx >= 0)
            {
                list.Insert(troopEditorIdx, item);
                LogSink.Always($"[EscapeMenuPatch] Injected 'Prism Settings' directly before Troop Editor at index {troopEditorIdx}.");
            }
            else
            {
                // Fallback: place right after Campaign Options / second item
                int fallbackIdx = Math.Min(2, list.Count);
                list.Insert(fallbackIdx, item);
                LogSink.Always($"[EscapeMenuPatch] AI Influence button not detected; injected 'Prism Settings' at fallback index {fallbackIdx}.");
            }
        }

        public static string GetItemText(object it)
        {
            if (it == null) return string.Empty;

            try
            {
                if (it is EscapeMenuItemVM vm && !string.IsNullOrEmpty(vm.ActionText))
                {
                    return vm.ActionText;
                }

                var tProp = it.GetType().GetProperty("Text") ?? it.GetType().GetProperty("ActionText");
                var tVal = tProp?.GetValue(it, null);
                if (tVal != null)
                {
                    string s = tVal.ToString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }

                var aField = AccessTools.Field(it.GetType(), "_actionText");
                var aVal = aField?.GetValue(it) as string;
                if (!string.IsNullOrEmpty(aVal)) return aVal;

                var itemField = AccessTools.Field(it.GetType(), "_itemObj");
                if (itemField?.GetValue(it) is TextObject to)
                {
                    string str = to.ToString();
                    if (!string.IsNullOrEmpty(str)) return str;
                    if (!string.IsNullOrEmpty(to.Value)) return to.Value;
                }

                Type curr = it.GetType();
                while (curr != null && curr != typeof(object))
                {
                    var fields = curr.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        if (f.FieldType == typeof(TextObject))
                        {
                            if (f.GetValue(it) is TextObject fto)
                            {
                                string s = fto.ToString();
                                if (!string.IsNullOrEmpty(s)) return s;
                                if (!string.IsNullOrEmpty(fto.Value)) return fto.Value;
                            }
                        }
                    }
                    curr = curr.BaseType;
                }
            }
            catch { }

            return it.ToString() ?? string.Empty;
        }

        public static bool IsAiInfluenceItem(object it)
        {
            if (it == null) return false;

            // 1. Check text from all sources
            string text = GetItemText(it);
            if (!string.IsNullOrEmpty(text))
            {
                string lower = text.ToLowerInvariant();
                if (lower.Contains("ai influence") || lower.Contains("aiinfluence") ||
                    lower.Contains("настройки ai") || lower.Contains("ai_influence") ||
                    lower.Contains("influence"))
                {
                    return true;
                }
            }

            // 2. Check TextObject raw value
            try
            {
                var itemField = AccessTools.Field(it.GetType(), "_itemObj");
                if (itemField?.GetValue(it) is TextObject to && !string.IsNullOrEmpty(to.Value))
                {
                    string rawLower = to.Value.ToLowerInvariant();
                    if (rawLower.Contains("aiinfluence") || rawLower.Contains("contenteditor") ||
                        rawLower.Contains("настройки ai") || rawLower.Contains("influence"))
                    {
                        return true;
                    }
                }
            }
            catch { }

            // 3. Check _identifier
            try
            {
                var idField = AccessTools.Field(it.GetType(), "_identifier");
                var idVal = idField?.GetValue(it)?.ToString();
                if (!string.IsNullOrEmpty(idVal))
                {
                    string idLower = idVal.ToLowerInvariant();
                    if (idLower.Contains("aiinfluence") || idLower.Contains("contenteditor") ||
                        idLower.Contains("ai_influence") || idLower.Contains("influence"))
                    {
                        return true;
                    }
                }
            }
            catch { }

            // 4. Check _onExecute delegate method/target
            try
            {
                var execField = AccessTools.Field(it.GetType(), "_onExecute");
                if (execField?.GetValue(it) is Delegate del)
                {
                    string target = (del.Method?.DeclaringType?.FullName ?? "") + " " + (del.Target?.GetType()?.FullName ?? "");
                    string targetLower = target.ToLowerInvariant();
                    if (targetLower.Contains("aiinfluence") || targetLower.Contains("contenteditor") || targetLower.Contains("influence"))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool IsTroopEditorItem(object it)
        {
            if (it == null) return false;
            string text = GetItemText(it);
            if (!string.IsNullOrEmpty(text))
            {
                string lower = text.ToLowerInvariant();
                if (lower.Contains("редактор войск") || lower.Contains("troop editor") || lower.Contains("troopeditor"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Stretches the Escape Menu frame height and adjusts padding so that
        /// extra buttons fit comfortably without crowding or shifting up into the top stone frame.
        /// </summary>
        public static void AdjustEscapeMenuLayout(Widget rootWidget)
        {
            if (rootWidget == null) return;

            try
            {
                // 1. Find EscapeMenu frame widget and stretch its height so all buttons fit comfortably
                Widget escapeMenu = FindWidgetById(rootWidget, "EscapeMenu");
                if (escapeMenu != null)
                {
                    // Native escape_panel height is ~700-720. Stretch to 880 to prevent button crowding.
                    if (escapeMenu.SuggestedHeight < 880f)
                    {
                        SetWidgetSuggestedHeight(escapeMenu, 880f);
                        LogSink.Info($"[EscapeMenuPatch] Stretched EscapeMenu SuggestedHeight to {escapeMenu.SuggestedHeight}");
                    }
                }

                // 2. Find ButtonsContainer and adjust padding & item margins
                Widget buttonsContainer = FindWidgetById(rootWidget, "ButtonsContainer");
                if (buttonsContainer != null)
                {
                    // Preserve native top margin (140-160f) so buttons do NOT shift up towards the top stone frame!
                    if (buttonsContainer.MarginTop < 100f)
                    {
                        SetWidgetMarginTop(buttonsContainer, 100f);
                    }
                    // Reduce bottom padding to allow buttons to flow nicely down the stretched frame
                    SetWidgetMarginBottom(buttonsContainer, 60f);

                    // Adjust existing child items spacing to 18f
                    for (int i = 0; i < buttonsContainer.ChildCount; i++)
                    {
                        Widget child = buttonsContainer.GetChild(i);
                        if (child != null && child.MarginBottom > 18f)
                        {
                            SetWidgetMarginBottom(child, 18f);
                        }
                    }

                    // Hook child addition for items added later by data binding
                    try
                    {
                        buttonsContainer.EventFire -= OnButtonsContainerEvent;
                        buttonsContainer.EventFire += OnButtonsContainerEvent;
                    }
                    catch { }

                    LogSink.Info("[EscapeMenuPatch] Adjusted ButtonsContainer layout (MarginTop=100, MarginBottom=60, item spacing=18).");
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[EscapeMenuPatch] AdjustEscapeMenuLayout warning: " + ex.Message);
            }
        }

        public static void SetWidgetSuggestedHeight(Widget widget, float height)
        {
            if (widget == null) return;
            try
            {
                if (widget.Context != null)
                {
                    widget.SuggestedHeight = height;
                }
                else
                {
                    AccessTools.Field(typeof(Widget), "_suggestedHeight")?.SetValue(widget, height);
                }
            }
            catch
            {
                AccessTools.Field(typeof(Widget), "_suggestedHeight")?.SetValue(widget, height);
            }
        }

        public static void SetWidgetMarginTop(Widget widget, float margin)
        {
            if (widget == null) return;
            try
            {
                if (widget.Context != null)
                {
                    widget.MarginTop = margin;
                }
                else
                {
                    AccessTools.Field(typeof(Widget), "_marginTop")?.SetValue(widget, margin);
                }
            }
            catch
            {
                AccessTools.Field(typeof(Widget), "_marginTop")?.SetValue(widget, margin);
            }
        }

        public static void SetWidgetMarginBottom(Widget widget, float margin)
        {
            if (widget == null) return;
            try
            {
                if (widget.Context != null)
                {
                    widget.MarginBottom = margin;
                }
                else
                {
                    AccessTools.Field(typeof(Widget), "_marginBottom")?.SetValue(widget, margin);
                }
            }
            catch
            {
                AccessTools.Field(typeof(Widget), "_marginBottom")?.SetValue(widget, margin);
            }
        }

        private static void OnButtonsContainerEvent(Widget widget, string eventName, object[] args)
        {
            try
            {
                if (string.Equals(eventName, "ItemAdd", StringComparison.OrdinalIgnoreCase) && args != null && args.Length > 0 && args[0] is Widget child)
                {
                    if (child.MarginBottom > 18f)
                    {
                        SetWidgetMarginBottom(child, 18f);
                    }
                }
            }
            catch { }
        }

        public static Widget FindWidgetById(Widget root, string id)
        {
            if (root == null || string.IsNullOrEmpty(id)) return null;
            if (string.Equals(root.Id, id, StringComparison.OrdinalIgnoreCase)) return root;

            int count = root.ChildCount;
            for (int i = 0; i < count; i++)
            {
                Widget found = FindWidgetById(root.GetChild(i), id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
