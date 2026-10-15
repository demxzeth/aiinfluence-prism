using System;
using System.Diagnostics;
using System.Reflection;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// Shared UI constants, process helpers, and Gauntlet reflection helpers for MCM screens.
    /// </summary>
    public static class McmUiHelpers
    {
        public static readonly string[] AvailableProviders = new[]
        {
            "Player2",
            "OpenAI",
            "OpenAI-SSE",
            "Gemini",
            "Anthropic",
            "Nvidia",
            "Ollama",
            "KoboldCpp",
            "Antigravity",
            "Custom"
        };

        public static readonly string[] ReasoningOptions = new[]
        {
            "Default",
            "None",
            "Low",
            "Medium",
            "High"
        };

        public static readonly string[] AvailableImageProviders = new[]
        {
            "OpenAI",
            "Google Gemini",
            "Free (Pollinations)",
            "Custom"
        };

        public static readonly string[] ScopeImageProviders = new[]
        {
            "Default",
            "OpenAI",
            "Google Gemini",
            "Free (Pollinations)",
            "Custom"
        };

        /// <summary>
        /// Image providers permitted for Dialogue scenes.
        /// Strictly limited to reference-capable multimodal models (Google Gemini, Custom) to ensure dialogue
        /// illustrations faithfully match in-game character models and portraits. Text-to-image models are excluded.
        /// </summary>
        public static readonly string[] DialogueScopeImageProviders = new[]
        {
            "Google Gemini",
            "Custom"
        };

        public static readonly string[] ImageArtStyles = new[]
        {
            "Bannerlord Concept Art",
            "Oil Painting",
            "Dark Medieval",
            "Medieval Tapestry",
            "Photorealistic Fantasy",
            "None (Raw Prompt)"
        };

        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm: failed to open URL: " + ex.Message);
            }
        }

        public static void OpenLogFolder()
        {
            string root = ModuleLocator.Root;
            string logsDir = !string.IsNullOrEmpty(root)
                ? System.IO.Path.Combine(root, "logs")
                : System.IO.Directory.GetCurrentDirectory();

            try
            {
                if (!System.IO.Directory.Exists(logsDir))
                {
                    System.IO.Directory.CreateDirectory(logsDir);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = logsDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm: failed to open logs folder: " + ex.Message);
            }
        }

        public static object CreateSelectorItem(Type itemType, string text)
        {
            if (itemType == null) return null;
            var to = new TaleWorlds.Localization.TextObject(text ?? string.Empty);
            foreach (var ctor in itemType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var pars = ctor.GetParameters();
                if (pars.Length == 1 && pars[0].ParameterType == typeof(TaleWorlds.Localization.TextObject))
                {
                    return ctor.Invoke(new object[] { to });
                }
                if (pars.Length == 1 && pars[0].ParameterType == typeof(string))
                {
                    return ctor.Invoke(new object[] { text });
                }
                if (pars.Length == 2 && pars[0].ParameterType == typeof(TaleWorlds.Localization.TextObject))
                {
                    object arg2 = pars[1].ParameterType == typeof(bool) ? (object)true : null;
                    return ctor.Invoke(new object[] { to, arg2 });
                }
            }
            try
            {
                return Activator.CreateInstance(itemType);
            }
            catch
            {
                return null;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> MainThreadQueue =
            new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public static void EnqueueMainThreadAction(Action action)
        {
            if (action != null)
            {
                MainThreadQueue.Enqueue(action);
            }
        }

        public static void ProcessMainThreadActions()
        {
            while (MainThreadQueue.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogSink.Warn("mcm: action execution failed: " + ex.Message);
                }
            }
        }

        public static bool IsMainThread
        {
            get
            {
                int mainId = AIInfluencePrism.PrismCore.SubModule.MainThreadId;
                return mainId == 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == mainId;
            }
        }

        /// <summary>
        /// Forces active MCM Gauntlet UI widgets to re-read and display current property values.
        /// </summary>
        public static void TryRefreshActiveScreenUi()
        {
            if (!IsMainThread)
            {
                EnqueueMainThreadAction(() => TryRefreshActiveScreenUi());
                return;
            }

            try
            {
                object topScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen;
                if (topScreen == null) return;
                Type topType = topScreen.GetType();
                if (!topType.Name.Contains("ModOptions")) return;
                var dsField = topType.GetField("_dataSource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dsField == null) return;
                object modOptionsVM = dsField.GetValue(topScreen);
                if (modOptionsVM == null) return;
                var selectedModProp = modOptionsVM.GetType().GetProperty("SelectedMod");
                if (selectedModProp == null) return;
                object settingsVM = selectedModProp.GetValue(modOptionsVM);
                if (settingsVM == null) return;

                var refreshSettings = settingsVM.GetType().GetMethod("RefreshValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (refreshSettings != null)
                {
                    refreshSettings.Invoke(settingsVM, null);
                }

                var groupsProp = settingsVM.GetType().GetProperty("SettingPropertyGroups");
                if (groupsProp == null) return;
                var groups = groupsProp.GetValue(settingsVM) as System.Collections.IEnumerable;
                if (groups == null) return;

                var groupList = new System.Collections.Generic.List<object>();
                foreach (object g in groups) { if (g != null) groupList.Add(g); }

                foreach (object group in groupList)
                {
                    var propsProp = group.GetType().GetProperty("SettingProperties");
                    if (propsProp == null) continue;
                    var props = propsProp.GetValue(group) as System.Collections.IEnumerable;
                    if (props == null) continue;

                    var propList = new System.Collections.Generic.List<object>();
                    foreach (object p in props) { if (p != null) propList.Add(p); }

                    foreach (object spVM in propList)
                    {
                        if (spVM == null) continue;

                        var refreshSp = spVM.GetType().GetMethod("RefreshValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (refreshSp != null)
                        {
                            refreshSp.Invoke(spVM, null);
                        }

                        var wrapperField = spVM.GetType().GetField("_selectorVMWrapper", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (wrapperField != null)
                        {
                            object wrapper = wrapperField.GetValue(spVM);
                            if (wrapper != null)
                            {
                                var refreshWrapper = wrapper.GetType().GetMethod("Refresh", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (refreshWrapper != null)
                                {
                                    refreshWrapper.Invoke(wrapper, null);
                                }
                            }
                        }

                        var onPropChanged = spVM.GetType().GetMethod("OnPropertyChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                        if (onPropChanged != null)
                        {
                            onPropChanged.Invoke(spVM, new object[] { "Value" });
                            onPropChanged.Invoke(spVM, new object[] { "StringValue" });
                            onPropChanged.Invoke(spVM, new object[] { "ValueString" });
                            onPropChanged.Invoke(spVM, new object[] { "DropdownValue" });
                            onPropChanged.Invoke(spVM, new object[] { "Selector" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm: TryRefreshActiveScreenUi error: " + ex.Message);
            }
        }

        /// <summary>
        /// Updates the items and selection of a dropdown control and refreshes corresponding text field in MCM.
        /// </summary>
        public static void TryRefreshDropdownWithItems(string dropdownPropertyName, System.Collections.Generic.List<string> items, int selectedIndex, string textPropertyName, string textValue)
        {
            if (!IsMainThread)
            {
                var copyItems = items != null ? new System.Collections.Generic.List<string>(items) : null;
                EnqueueMainThreadAction(() => TryRefreshDropdownWithItems(dropdownPropertyName, copyItems, selectedIndex, textPropertyName, textValue));
                return;
            }

            try
            {
                object topScreen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen;
                if (topScreen == null) return;
                Type topType = topScreen.GetType();
                if (!topType.Name.Contains("ModOptions")) return;
                var dsField = topType.GetField("_dataSource", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dsField == null) return;
                object modOptionsVM = dsField.GetValue(topScreen);
                if (modOptionsVM == null) return;
                var selectedModProp = modOptionsVM.GetType().GetProperty("SelectedMod");
                if (selectedModProp == null) return;
                object settingsVM = selectedModProp.GetValue(modOptionsVM);
                if (settingsVM == null) return;

                var groupsProp = settingsVM.GetType().GetProperty("SettingPropertyGroups");
                if (groupsProp == null) return;
                var groups = groupsProp.GetValue(settingsVM) as System.Collections.IEnumerable;
                if (groups == null) return;

                var groupList = new System.Collections.Generic.List<object>();
                foreach (object g in groups) { if (g != null) groupList.Add(g); }

                bool matchedDropdown = false;
                bool matchedText = false;

                foreach (object group in groupList)
                {
                    var propsProp = group.GetType().GetProperty("SettingProperties");
                    if (propsProp == null) continue;
                    var props = propsProp.GetValue(group) as System.Collections.IEnumerable;
                    if (props == null) continue;

                    var propList = new System.Collections.Generic.List<object>();
                    foreach (object p in props) { if (p != null) propList.Add(p); }

                    foreach (object spVM in propList)
                    {
                        if (spVM == null) continue;
                        var defProp = spVM.GetType().GetProperty("SettingPropertyDefinition");
                        if (defProp == null) continue;
                        object def = defProp.GetValue(spVM);
                        if (def == null) continue;
                        var idProp = def.GetType().GetProperty("Id");
                        string propId = idProp != null ? idProp.GetValue(def) as string : null;

                        if (!matchedDropdown && string.Equals(propId, dropdownPropertyName, StringComparison.OrdinalIgnoreCase))
                        {
                            var wrapperField = spVM.GetType().GetField("_selectorVMWrapper", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (wrapperField != null)
                            {
                                object wrapper = wrapperField.GetValue(spVM);
                                if (wrapper != null && items != null && items.Count > 0)
                                {
                                    var itemListProp = wrapper.GetType().GetProperty("ItemList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    var itemList = itemListProp != null ? itemListProp.GetValue(wrapper) as System.Collections.IList : null;
                                    if (itemList != null)
                                    {
                                        Type itemType = itemList.GetType().IsGenericType ? itemList.GetType().GetGenericArguments()[0] : null;
                                        itemList.Clear();
                                        foreach (string m in items)
                                        {
                                            object newItem = CreateSelectorItem(itemType, m);
                                            if (newItem != null) itemList.Add(newItem);
                                        }

                                        var selIdxProp = wrapper.GetType().GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (selIdxProp != null && selIdxProp.CanWrite && selectedIndex >= 0)
                                        {
                                            selIdxProp.SetValue(wrapper, selectedIndex);
                                        }

                                        var refreshMethod = wrapper.GetType().GetMethod("Refresh", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                        if (refreshMethod != null)
                                        {
                                            refreshMethod.Invoke(wrapper, null);
                                        }
                                    }
                                }
                            }

                            var onPropChangedMethod = spVM.GetType().GetMethod("OnPropertyChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                            if (onPropChangedMethod != null)
                            {
                                onPropChangedMethod.Invoke(spVM, new object[] { "DropdownValue" });
                                onPropChangedMethod.Invoke(spVM, new object[] { "Selector" });
                            }
                            matchedDropdown = true;
                        }
                        else if (!matchedText && !string.IsNullOrEmpty(textPropertyName) && string.Equals(propId, textPropertyName, StringComparison.OrdinalIgnoreCase))
                        {
                            string[] valPropNames = new[] { "Value", "StringValue", "ValueString" };
                            foreach (string vp in valPropNames)
                            {
                                var p = spVM.GetType().GetProperty(vp, BindingFlags.Public | BindingFlags.Instance);
                                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                                {
                                    try { p.SetValue(spVM, textValue ?? string.Empty); } catch { }
                                }
                            }
                            var onPropChangedMethod = spVM.GetType().GetMethod("OnPropertyChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                            if (onPropChangedMethod != null)
                            {
                                onPropChangedMethod.Invoke(spVM, new object[] { "Value" });
                                onPropChangedMethod.Invoke(spVM, new object[] { "StringValue" });
                                onPropChangedMethod.Invoke(spVM, new object[] { "ValueString" });
                            }
                            matchedText = true;
                        }

                        if (matchedDropdown && (string.IsNullOrEmpty(textPropertyName) || matchedText))
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm: TryRefreshDropdownWithItems error: " + ex.Message);
            }
        }
    }
}
