using System;
using System.ComponentModel;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.Config
{
    /// <summary>
    /// MCM Screen 1: General & Providers.
    /// Manages master switch, default provider profile, Antigravity CLI, diagnostics, and support links.
    /// </summary>
    public sealed class McmProviderSettings : AttributeGlobalSettings<McmProviderSettings>
    {
        public override string Id => "AIInfluencePrism_v1_Providers";
        public override string DisplayName => "AIInfluence Prism - Providers";
        public override string FolderName => "AIInfluencePrism";
        public override string FormatType => "json";

        // ─── Support ──────────────────────────────────────────

        private Action _openNexus;
        private Action _openGitHub;
        private Action _showDiscord;

        [SettingPropertyGroup("Support", GroupOrder = 1)]
        [SettingPropertyButton("Nexus Page", Order = 1, Content = "Nexus Page", RequireRestart = false,
            HintText = "Opens the Nexus Mods page of AIInfluence Prism in your browser.")]
        public Action OpenNexus
        {
            get { return _openNexus ?? (_openNexus = () => McmUiHelpers.OpenUrl(PrismInfo.NexusUrl)); }
            set { _openNexus = value; }
        }

        [SettingPropertyGroup("Support", GroupOrder = 1)]
        [SettingPropertyButton("GitHub", Order = 2, Content = "GitHub", RequireRestart = false,
            HintText = "Opens the GitHub repository of AIInfluence Prism in your browser.")]
        public Action OpenGitHub
        {
            get { return _openGitHub ?? (_openGitHub = () => McmUiHelpers.OpenUrl(PrismInfo.GitHubUrl)); }
            set { _openGitHub = value; }
        }

        [SettingPropertyGroup("Support", GroupOrder = 1)]
        [SettingPropertyButton("Contact Me on Discord", Order = 3, Content = "@demxzeth", RequireRestart = false,
            HintText = "Reach me on Discord: @demxzeth")]
        public Action ShowDiscord
        {
            get { return _showDiscord ?? (_showDiscord = () => ChatNotices.Show("Discord: " + PrismInfo.DiscordContact)); }
            set { _showDiscord = value; }
        }

        // ─── Master ───────────────────────────────────────────

        private Action _testAll;
        private Action _openLogs;

        [SettingPropertyGroup("Master", GroupOrder = 2)]
        [SettingPropertyBool("Master Switch", Order = 1, RequireRestart = false,
            HintText = "When on, Prism routes requests to your configured providers. When off, requests go to AIInfluence's own backend.")]
        public bool MasterEnabled
        {
            get => ProfileBook.Current.MasterEnabled;
            set
            {
                if (ProfileBook.Current.MasterEnabled != value)
                {
                    ProfileBook.Current.MasterEnabled = value;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Master", GroupOrder = 2)]
        [SettingPropertyButton("Test All Connections", Order = 2, Content = "Test All", RequireRestart = false,
            HintText = "Runs a connection test against every enabled scope and reports each result in chat.")]
        public Action TestAll
        {
            get { return _testAll ?? (_testAll = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeAllEnabledScopes())); }
            set { _testAll = value; }
        }

        [SettingPropertyGroup("Master", GroupOrder = 2)]
        [SettingPropertyButton("Open Log Folder", Order = 3, Content = "Open Logs", RequireRestart = false,
            HintText = "Opens the folder where prism_log.txt is written.")]
        public Action OpenLogs
        {
            get { return _openLogs ?? (_openLogs = McmUiHelpers.OpenLogFolder); }
            set { _openLogs = value; }
        }

        // ─── Default Provider Profile ─────────────────────────

        private Dropdown<string> _defaultProviderDropdown;
        private Action _testDefaultProvider;

        [SettingPropertyGroup("Default Provider Profile", GroupOrder = 3)]
        [SettingPropertyDropdown("Provider", Order = 1, RequireRestart = false,
            HintText = "Default provider used when a scope does not override it.")]
        public Dropdown<string> DefaultProviderDropdown
        {
            get
            {
                if (_defaultProviderDropdown == null)
                {
                    string current = Profile.Label(ProfileBook.Current.Provider);
                    int idx = Array.FindIndex(McmUiHelpers.AvailableProviders, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = 0;
                    _defaultProviderDropdown = new Dropdown<string>(McmUiHelpers.AvailableProviders, idx);
                    _defaultProviderDropdown.PropertyChanged += (sender, args) =>
                    {
                        var dd = (Dropdown<string>)sender;
                        if (dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Count)
                        {
                            string selected = dd.SelectedValue;
                            if (Profile.TryParseProvider(selected, out ProviderKind kind))
                            {
                                if (ProfileBook.Current.Provider != kind)
                                {
                                    ProfileBook.Current.SwitchProvider(kind);
                                    Save();
                                    NotifyDefaultProviderProperties();
                                }
                            }
                        }
                    };
                }
                else
                {
                    string current = Profile.Label(ProfileBook.Current.Provider);
                    int idx = Array.FindIndex(McmUiHelpers.AvailableProviders, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && _defaultProviderDropdown.SelectedIndex != idx)
                    {
                        _defaultProviderDropdown.SelectedIndex = idx;
                    }
                }
                return _defaultProviderDropdown;
            }
            set => _defaultProviderDropdown = value;
        }

        [SettingPropertyGroup("Default Provider Profile", GroupOrder = 3)]
        [SettingPropertyText("API Key", Order = 2, RequireRestart = false,
            HintText = "API key for the default provider.")]
        public string DefaultApiKey
        {
            get => ProfileBook.Current.ApiKey ?? string.Empty;
            set
            {
                string v = value ?? string.Empty;
                if (ProfileBook.Current.ApiKey != v)
                {
                    ProfileBook.Current.ApiKey = v;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Default Provider Profile", GroupOrder = 3)]
        [SettingPropertyText("API Address", Order = 3, RequireRestart = false,
            HintText = "Base address. Editable only when Provider is set to 'Custom' (for other providers the default address is used automatically).")]
        public string DefaultRootUrl
        {
            get => ProfileBook.Current.RootUrl ?? string.Empty;
            set
            {
                string v = (value ?? string.Empty).Trim();
                if (ProfileBook.Current.Provider != ProviderKind.Custom)
                {
                    if (v.Length > 0 && v != ProfileBook.Current.RootUrl)
                    {
                        ChatNotices.Show("The default address is used for this provider. To set a custom URL, select Provider: Custom.");
                        ProfileBook.Current.RootUrl = string.Empty;
                        Save();
                        OnPropertyChanged(nameof(DefaultRootUrl));
                        return;
                    }
                    if (ProfileBook.Current.RootUrl != string.Empty)
                    {
                        ProfileBook.Current.RootUrl = string.Empty;
                        Save();
                        OnPropertyChanged(nameof(DefaultRootUrl));
                    }
                    return;
                }

                if (ProfileBook.Current.RootUrl != v)
                {
                    ProfileBook.Current.RootUrl = v;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Default Provider Profile", GroupOrder = 3)]
        [SettingPropertyText("Model", Order = 4, RequireRestart = false,
            HintText = "Default model name (e.g. gpt-4o-mini, claude-3-5-haiku-latest, gemini-2.0-flash). For Player2: leave empty.")]
        public string DefaultModel
        {
            get => ProfileBook.Current.Model ?? string.Empty;
            set
            {
                string v = (value ?? string.Empty).Trim();
                if (ProfileBook.Current.Model != v)
                {
                    ProfileBook.Current.Model = v;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Default Provider Profile", GroupOrder = 3)]
        [SettingPropertyButton("Test Connection to Provider", Order = 5, Content = "Test", RequireRestart = false,
            HintText = "Sends a ping to the default provider endpoint and reports the result in chat.")]
        public Action TestDefaultProvider
        {
            get { return _testDefaultProvider ?? (_testDefaultProvider = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeCurrent())); }
            set { _testDefaultProvider = value; }
        }

        private void NotifyDefaultProviderProperties()
        {
            try
            {
                OnPropertyChanged(nameof(DefaultApiKey));
                OnPropertyChanged(nameof(DefaultRootUrl));
                OnPropertyChanged(nameof(DefaultModel));

                McmUiHelpers.TryRefreshActiveScreenUi();
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm[provider]: NotifyDefaultProviderProperties failed: " + ex.Message);
            }
        }

        // ─── Image Generation Provider ────────────────────────

        private Dropdown<string> _imageProviderDropdown;
        private Dropdown<string> _imageModelDropdown;
        private bool _suppressImageDropdownSync = false;
        private Action _fetchImageModels;
        private Action _clearImageModels;
        private Action _testImageProvider;

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyDropdown("Provider", Order = 1, RequireRestart = false,
            HintText = "Default image generation provider (OpenAI, Google Gemini, Custom).")]
        public Dropdown<string> ImageProviderDropdown
        {
            get
            {
                if (_imageProviderDropdown == null)
                {
                    string current = ProfileBook.Current.ImageProvider ?? "OpenAI";
                    int idx = Array.FindIndex(McmUiHelpers.AvailableImageProviders, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = 0;
                    _imageProviderDropdown = new Dropdown<string>(McmUiHelpers.AvailableImageProviders, idx);
                    _imageProviderDropdown.PropertyChanged += (sender, args) =>
                    {
                        var dd = (Dropdown<string>)sender;
                        if (dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Count)
                        {
                            string selected = dd.SelectedValue;
                            if (!string.Equals(ProfileBook.Current.ImageProvider, selected, StringComparison.OrdinalIgnoreCase))
                            {
                                ProfileBook.Current.SwitchImageProvider(selected);
                                Save();
                                NotifyImageProviderProperties();
                            }
                        }
                    };
                }
                else
                {
                    string current = ProfileBook.Current.ImageProvider ?? "OpenAI";
                    int idx = Array.FindIndex(McmUiHelpers.AvailableImageProviders, p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && _imageProviderDropdown.SelectedIndex != idx)
                    {
                        _imageProviderDropdown.SelectedIndex = idx;
                    }
                }
                return _imageProviderDropdown;
            }
            set => _imageProviderDropdown = value;
        }

        private Dropdown<string> _imageArtStyleDropdown;

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyDropdown("Art Style", Order = 2, RequireRestart = false,
            HintText = "Artistic style modifier appended to prompts for immersive medieval visuals.")]
        public Dropdown<string> ImageArtStyleDropdown
        {
            get
            {
                if (_imageArtStyleDropdown == null)
                {
                    string current = ProfileBook.Current.ImageArtStyle ?? "Bannerlord Concept Art";
                    int idx = Array.FindIndex(McmUiHelpers.ImageArtStyles, s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = 0;
                    _imageArtStyleDropdown = new Dropdown<string>(McmUiHelpers.ImageArtStyles, idx);
                    _imageArtStyleDropdown.PropertyChanged += (sender, args) =>
                    {
                        var dd = (Dropdown<string>)sender;
                        if (dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Count)
                        {
                            string selected = dd.SelectedValue;
                            if (!string.Equals(ProfileBook.Current.ImageArtStyle, selected, StringComparison.OrdinalIgnoreCase))
                            {
                                ProfileBook.Current.ImageArtStyle = selected;
                                Save();
                            }
                        }
                    };
                }
                return _imageArtStyleDropdown;
            }
            set => _imageArtStyleDropdown = value;
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyText("API Key", Order = 3, RequireRestart = false,
            HintText = "API key for image generation. Not needed for Free (Pollinations). If left empty, inherits key from text provider.")]
        public string ImageApiKey
        {
            get => ProfileBook.Current.ImageApiKey ?? string.Empty;
            set
            {
                string v = value ?? string.Empty;
                if (ProfileBook.Current.ImageApiKey != v)
                {
                    ProfileBook.Current.ImageApiKey = v;
                    ProfileBook.Current.SaveCurrentImageProvider(ProfileBook.Current.ImageProvider);
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyText("API Address", Order = 4, RequireRestart = false,
            HintText = "Base endpoint URL (for Custom or proxies). For OpenAI, Gemini, and Free, defaults are used automatically.")]
        public string ImageRootUrl
        {
            get => ProfileBook.Current.ImageRootUrl ?? string.Empty;
            set
            {
                string v = (value ?? string.Empty).Trim();
                if (ProfileBook.Current.ImageRootUrl != v)
                {
                    ProfileBook.Current.ImageRootUrl = v;
                    ProfileBook.Current.SaveCurrentImageProvider(ProfileBook.Current.ImageProvider);
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyText("Model", Order = 5, RequireRestart = false,
            HintText = "Image generation model name (e.g. flux for Free, dall-e-3 for OpenAI, gemini-2.5-flash-image for Gemini).")]
        public string ImageModel
        {
            get => ProfileBook.Current.ImageModel ?? string.Empty;
            set
            {
                string v = (value ?? string.Empty).Trim();
                if (ProfileBook.Current.ImageModel != v)
                {
                    ProfileBook.Current.ImageModel = v;
                    ProfileBook.Current.SaveCurrentImageProvider(ProfileBook.Current.ImageProvider);
                    Save();

                    if (_imageModelDropdown != null && v.Length > 0)
                    {
                        int idx = -1;
                        for (int i = 0; i < _imageModelDropdown.Count; i++)
                        {
                            if (string.Equals(_imageModelDropdown[i], v, StringComparison.OrdinalIgnoreCase))
                            {
                                idx = i;
                                break;
                            }
                        }
                        if (idx >= 0 && _imageModelDropdown.SelectedIndex != idx)
                        {
                            _suppressImageDropdownSync = true;
                            try { _imageModelDropdown.SelectedIndex = idx; }
                            finally { _suppressImageDropdownSync = false; }
                        }
                    }
                }
            }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyDropdown("Select Model", Order = 6, RequireRestart = false,
            HintText = "Select an available image model fetched from the endpoint.")]
        public Dropdown<string> ImageModelDropdown
        {
            get
            {
                if (_imageModelDropdown == null)
                {
                    Profile p = ProfileBook.Current;
                    string curProv = p.ImageProvider ?? "OpenAI";
                    System.Collections.Generic.List<string> items = new System.Collections.Generic.List<string>();
                    int selIdx = 0;

                    System.Collections.Generic.List<string> available = null;
                    if (p.ImageProviders != null && p.ImageProviders.TryGetValue(curProv, out var ips) && ips?.AvailableModels != null && ips.AvailableModels.Count > 0)
                    {
                        available = ips.AvailableModels;
                    }

                    if (available != null && available.Count > 0)
                    {
                        items.AddRange(available);
                        if (!string.IsNullOrEmpty(p.ImageModel))
                        {
                            if (!items.Contains(p.ImageModel)) items.Insert(0, p.ImageModel);
                            selIdx = items.IndexOf(p.ImageModel);
                        }
                    }
                    else if (!string.IsNullOrEmpty(p.ImageModel))
                    {
                        items.Add(p.ImageModel);
                        selIdx = 0;
                    }
                    else
                    {
                        items.Add("(Click Fetch to load)");
                        selIdx = 0;
                    }

                    _imageModelDropdown = new Dropdown<string>(items, (selIdx >= 0 && selIdx < items.Count) ? selIdx : 0);
                    _imageModelDropdown.PropertyChanged += (sender, args) =>
                    {
                        if (_suppressImageDropdownSync) return;
                        var dd = (Dropdown<string>)sender;
                        if (dd.SelectedIndex >= 0 && dd.SelectedIndex < dd.Count)
                        {
                            string selected = dd.SelectedValue;
                            if (!string.IsNullOrEmpty(selected) && !selected.StartsWith("(", StringComparison.Ordinal))
                            {
                                if (ProfileBook.Current.ImageModel != selected)
                                {
                                    ProfileBook.Current.ImageModel = selected;
                                    ProfileBook.Current.SaveCurrentImageProvider(ProfileBook.Current.ImageProvider);
                                    Save();
                                    OnPropertyChanged(nameof(ImageModel));
                                }
                            }
                        }
                    };
                }
                return _imageModelDropdown;
            }
            set => _imageModelDropdown = value;
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyButton("Refresh Models from Endpoint", Order = 7, Content = "Fetch", RequireRestart = false,
            HintText = "Queries the endpoint to retrieve all available image models.")]
        public Action FetchImageModels
        {
            get { return _fetchImageModels ?? (_fetchImageModels = () => FetchImageModelsAction()); }
            set { _fetchImageModels = value; }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyButton("Clear Fetched Models", Order = 8, Content = "Clear", RequireRestart = false,
            HintText = "Clears the list of fetched models and resets the image model dropdown.")]
        public Action ClearImageModels
        {
            get { return _clearImageModels ?? (_clearImageModels = () => ClearImageModelsAction()); }
            set { _clearImageModels = value; }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyBool("Fail Silently on Error", Order = 9, RequireRestart = false,
            HintText = "When enabled, image generation errors will not disrupt gameplay or display red chat warnings.")]
        public bool ImageFailSilently
        {
            get => ProfileBook.Current.ImageFailSilently;
            set
            {
                if (ProfileBook.Current.ImageFailSilently != value)
                {
                    ProfileBook.Current.ImageFailSilently = value;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyBool("Auto-Fallback to Free", Order = 10, RequireRestart = false,
            HintText = "If the primary provider fails (e.g. Gemini quota exhausted or network error), automatically attempt generation using Free (Pollinations).")]
        public bool ImageAutoFallbackFree
        {
            get => ProfileBook.Current.ImageAutoFallbackFree;
            set
            {
                if (ProfileBook.Current.ImageAutoFallbackFree != value)
                {
                    ProfileBook.Current.ImageAutoFallbackFree = value;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Image Generation Provider", GroupOrder = 4)]
        [SettingPropertyButton("Test Image Generation", Order = 11, Content = "Test", RequireRestart = false,
            HintText = "Sends a test image prompt to the selected image provider and reports the result in chat.")]
        public Action TestImageProvider
        {
            get { return _testImageProvider ?? (_testImageProvider = () => System.Threading.Tasks.Task.Run(() => ImagePipeline.ProbeImageGeneration())); }
            set { _testImageProvider = value; }
        }

        private void FetchImageModelsAction()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    Profile profile = ProfileBook.Current;
                    string provider = profile.ImageProvider ?? "OpenAI";
                    ImageProviderSettings config = profile.GetEffectiveImageConfig(provider);
                    ChatNotices.Show($"- Fetching available image models for {provider}...", "Image");

                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    {
                        var models = await ModelDiscovery.FetchImageModelsAsync(
                            provider, config.RootUrl, config.ApiKey, cts.Token).ConfigureAwait(false);

                        if (models != null && models.Count > 0)
                        {
                            if (profile.ImageProviders == null)
                            {
                                profile.ImageProviders = new System.Collections.Generic.Dictionary<string, ImageProviderSettings>(StringComparer.OrdinalIgnoreCase);
                            }
                            if (!profile.ImageProviders.TryGetValue(provider, out ImageProviderSettings saved) || saved == null)
                            {
                                saved = config.Clone();
                                profile.ImageProviders[provider] = saved;
                            }
                            saved.AvailableModels = new System.Collections.Generic.List<string>(models);
                            Save();

                            var items = new System.Collections.Generic.List<string>();
                            int selIdx = 0;
                            string cur = profile.ImageModel;
                            if (!string.IsNullOrEmpty(cur))
                            {
                                items.AddRange(models);
                                if (!items.Contains(cur)) items.Insert(0, cur);
                                selIdx = items.IndexOf(cur);
                            }
                            else
                            {
                                items.Add("(select model)");
                                items.AddRange(models);
                                selIdx = 0;
                            }

                            if (_imageModelDropdown != null)
                            {
                                _suppressImageDropdownSync = true;
                                try
                                {
                                    _imageModelDropdown.Clear();
                                    foreach (string m in items) _imageModelDropdown.Add(m);
                                    _imageModelDropdown.SelectedIndex = (selIdx >= 0 && selIdx < items.Count) ? selIdx : 0;
                                }
                                finally
                                {
                                    _suppressImageDropdownSync = false;
                                }
                            }

                            OnPropertyChanged(nameof(ImageModelDropdown));
                            McmUiHelpers.TryRefreshDropdownWithItems(nameof(ImageModelDropdown), items, selIdx, nameof(ImageModel), profile.ImageModel ?? string.Empty);
                            McmUiHelpers.TryRefreshActiveScreenUi();

                            ChatNotices.Show($"Found {models.Count} image models for {provider}.", "Image", 0.35f, 0.85f, 0.35f);
                        }
                        else
                        {
                            ChatNotices.Error($"No image models found for {provider}.", "Image");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ChatNotices.Error("Fetch image models error: " + ex.Message, "Image");
                }
            });
        }

        private void ClearImageModelsAction()
        {
            try
            {
                Profile profile = ProfileBook.Current;
                string provider = profile.ImageProvider ?? "OpenAI";
                if (profile.ImageProviders != null && profile.ImageProviders.TryGetValue(provider, out var saved) && saved != null)
                {
                    saved.AvailableModels?.Clear();
                    Save();
                }

                var items = new System.Collections.Generic.List<string> { "(no models fetched)" };
                if (!string.IsNullOrEmpty(profile.ImageModel))
                {
                    items.Insert(0, profile.ImageModel);
                }
                int selIdx = 0;

                if (_imageModelDropdown != null)
                {
                    _suppressImageDropdownSync = true;
                    try
                    {
                        _imageModelDropdown.Clear();
                        foreach (string m in items) _imageModelDropdown.Add(m);
                        _imageModelDropdown.SelectedIndex = selIdx;
                    }
                    finally
                    {
                        _suppressImageDropdownSync = false;
                    }
                }

                OnPropertyChanged(nameof(ImageModelDropdown));
                McmUiHelpers.TryRefreshDropdownWithItems(nameof(ImageModelDropdown), items, selIdx, nameof(ImageModel), profile.ImageModel ?? string.Empty);
                McmUiHelpers.TryRefreshActiveScreenUi();
                ChatNotices.Show($"Cleared fetched models for {provider}.", "Image");
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm[provider]: ClearImageModelsAction error: " + ex.Message);
            }
        }

        private void NotifyImageProviderProperties()
        {
            try
            {
                OnPropertyChanged(nameof(ImageApiKey));
                OnPropertyChanged(nameof(ImageRootUrl));
                OnPropertyChanged(nameof(ImageModel));

                Profile p = ProfileBook.Current;
                string curProv = p.ImageProvider ?? "OpenAI";
                System.Collections.Generic.List<string> available = null;
                if (p.ImageProviders != null && p.ImageProviders.TryGetValue(curProv, out var ips) && ips?.AvailableModels != null)
                {
                    available = ips.AvailableModels;
                }

                var items = new System.Collections.Generic.List<string>();
                int selIdx = 0;
                if (available != null && available.Count > 0)
                {
                    items.AddRange(available);
                    if (!string.IsNullOrEmpty(p.ImageModel))
                    {
                        if (!items.Contains(p.ImageModel)) items.Insert(0, p.ImageModel);
                        selIdx = items.IndexOf(p.ImageModel);
                    }
                }
                else if (!string.IsNullOrEmpty(p.ImageModel))
                {
                    items.Add(p.ImageModel);
                    selIdx = 0;
                }
                else
                {
                    items.Add("(Click Fetch to load)");
                    selIdx = 0;
                }

                if (_imageModelDropdown != null)
                {
                    _suppressImageDropdownSync = true;
                    try
                    {
                        _imageModelDropdown.Clear();
                        foreach (string it in items) _imageModelDropdown.Add(it);
                        _imageModelDropdown.SelectedIndex = (selIdx >= 0 && selIdx < items.Count) ? selIdx : 0;
                    }
                    finally
                    {
                        _suppressImageDropdownSync = false;
                    }
                }

                OnPropertyChanged(nameof(ImageModelDropdown));
                McmUiHelpers.TryRefreshDropdownWithItems(nameof(ImageModelDropdown), items, selIdx, nameof(ImageModel), p.ImageModel ?? string.Empty);
                McmUiHelpers.TryRefreshActiveScreenUi();
            }
            catch (Exception ex)
            {
                LogSink.Warn("mcm[provider]: NotifyImageProviderProperties failed: " + ex.Message);
            }
        }

        // ─── Antigravity ──────────────────────────────────────

        private Action _testAntigravityCli;
        private Action _testAntigravityModel;

        [SettingPropertyGroup("Antigravity", GroupOrder = 5)]
        [SettingPropertyText("Antigravity CLI Path", Order = 1, RequireRestart = false,
            HintText = "Path to agy.exe (Antigravity CLI). Defaults to 'agy' if in PATH or local app data.")]
        public string AntigravityCliPath
        {
            get => ProfileBook.Current.AntigravityCliPath ?? "agy";
            set
            {
                string v = (value ?? string.Empty).Trim();
                if (ProfileBook.Current.AntigravityCliPath != v)
                {
                    ProfileBook.Current.AntigravityCliPath = v;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Antigravity", GroupOrder = 5)]
        [SettingPropertyText("Model", Order = 2, RequireRestart = false,
            HintText = "Model name for Antigravity CLI (e.g. gemini-3.7-flash-high).")]
        public string AntigravityModel
        {
            get => ProfileBook.Current.AntigravityModel ?? "gemini-3.7-flash-high";
            set
            {
                string v = (value ?? string.Empty).Trim();
                if (ProfileBook.Current.AntigravityModel != v)
                {
                    ProfileBook.Current.AntigravityModel = v;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Antigravity", GroupOrder = 5)]
        [SettingPropertyButton("Test Antigravity CLI", Order = 3, Content = "Test CLI", RequireRestart = false,
            HintText = "Verifies that agy.exe can be executed and reports its version in chat.")]
        public Action TestAntigravityCli
        {
            get { return _testAntigravityCli ?? (_testAntigravityCli = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeAntigravityCli())); }
            set { _testAntigravityCli = value; }
        }

        [SettingPropertyGroup("Antigravity", GroupOrder = 5)]
        [SettingPropertyButton("Test Antigravity Model", Order = 4, Content = "Test Model", RequireRestart = false,
            HintText = "Sends a test ping to the specified model via Antigravity CLI and reports the reply in chat.")]
        public Action TestAntigravityModel
        {
            get { return _testAntigravityModel ?? (_testAntigravityModel = () => System.Threading.Tasks.Task.Run(() => Pipeline.ProbeAntigravityModel())); }
            set { _testAntigravityModel = value; }
        }

        // ─── Diagnostics ──────────────────────────────────────

        [SettingPropertyGroup("Diagnostics", GroupOrder = 6)]
        [SettingPropertyBool("Write Log File", Order = 1, RequireRestart = false,
            HintText = "When on, Prism writes a verbose log to logs/prism_log.txt.")]
        public bool LogEnabled
        {
            get => ProfileBook.Current.LogEnabled;
            set
            {
                if (ProfileBook.Current.LogEnabled != value)
                {
                    ProfileBook.Current.LogEnabled = value;
                    LogSink.Enabled = value;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Diagnostics", GroupOrder = 6)]
        [SettingPropertyBool("Repair JSON Replies", Order = 2, RequireRestart = false,
            HintText = "When on, malformed or truncated model responses are repaired before parsing.")]
        public bool JsonRepairEnabled
        {
            get => ProfileBook.Current.JsonRepairEnabled;
            set
            {
                if (ProfileBook.Current.JsonRepairEnabled != value)
                {
                    ProfileBook.Current.JsonRepairEnabled = value;
                    Save();
                }
            }
        }

        [SettingPropertyGroup("Diagnostics", GroupOrder = 6)]
        [SettingPropertyBool("Show Provider Errors in Chat", Order = 3, RequireRestart = false,
            HintText = "When on, provider errors (HTTP failures, timeouts) are shown as chat notices.")]
        public bool ErrorNoticesEnabled
        {
            get => ProfileBook.Current.ErrorNoticesEnabled;
            set
            {
                if (ProfileBook.Current.ErrorNoticesEnabled != value)
                {
                    ProfileBook.Current.ErrorNoticesEnabled = value;
                    Save();
                }
            }
        }

        // ─── About ────────────────────────────────────────────

        private Action _about;

        [SettingPropertyGroup("About", GroupOrder = 7)]
        [SettingPropertyButton("About AIInfluence Prism", Order = 1, Content = "Version " + PrismInfo.VersionText, RequireRestart = false,
            HintText = "Shows version and author information in chat.")]
        public Action About
        {
            get { return _about ?? (_about = () => ChatNotices.Show("AIInfluence Prism " + PrismInfo.VersionText + " - made by Demxzeth")); }
            set { _about = value; }
        }

        // ─── Plumbing ─────────────────────────────────────────

        public void PullFromProfile()
        {
            Profile p = ProfileBook.Current;
            LogSink.Enabled = p.LogEnabled;
            LogSink.Info("mcm[providers]: pulled from profile \"" + p.Name + "\".");
        }

        private static void Save()
        {
            ProfileBook.Save();
        }
    }
}
