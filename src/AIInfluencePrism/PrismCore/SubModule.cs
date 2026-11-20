using System;
using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using AIInfluencePrism.Hook;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Security;
using AIInfluencePrism.UI;

namespace AIInfluencePrism.PrismCore
{
    /// <summary>
    /// Entry point of the mod (Prism 1.0.0).
    /// Lifecycle: resolves root -> initializes logging and security vault -> starts Harmony Hook Engine.
    /// Intercepts AIInfluence 7.0.6 calls deterministically via FastRouter.
    /// </summary>
    public sealed class SubModule : MBSubModuleBase
    {
        private const float TickerFlushIntervalSeconds = 60f;
        private float _tickerNextFlushIn = TickerFlushIntervalSeconds;
        private bool _uiInitialized;
        private bool _startMessageDisplayed;
        private float _startMessageTimer;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            try
            {
                if (System.Net.ServicePointManager.DefaultConnectionLimit < 16)
                {
                    System.Net.ServicePointManager.DefaultConnectionLimit = 16;
                }
            }
            catch { }

            string root = ModuleLocator.Root;
            if (root.Length == 0)
            {
                LogSink.Initialize(Directory.GetCurrentDirectory(), verbose: true);
                LogSink.Error("module root could not be resolved; logging to current directory.");
                LogSink.Always("loaded (fallback location); module root unresolved.");
            }
            else
            {
                LogSink.Initialize(root, verbose: true);
                LogSink.Always("loaded; root: " + root);
            }

            LogSink.Always("routing ready: " + ScopeCatalog.All.Length + " scopes via FastRouter.");

            ProfileBook.Warmup();
            LogSink.Enabled = ProfileBook.Current.LogEnabled;

            // Warm up JsonVault (pre-reads the vault file; thread-safe)
            JsonVault.GetString("_warmup", "");

            Engine.Create();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            if (!_uiInitialized)
            {
                _uiInitialized = true;
                ChatNotices.MarkUiReady();

                try
                {
                    McmProviderSettings providers = McmProviderSettings.Instance;
                    providers?.PullFromProfile();
                    McmScopeSettings scopes = McmScopeSettings.Instance;
                    scopes?.PullFromProfile();
                    LogSink.Always("mcm: settings synced on main menu screen.");
                }
                catch (Exception ex)
                {
                    LogSink.Warn("mcm: sync failed: " + ex.Message);
                }
            }

            Engine.PrimeStep(1f);
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            _startMessageDisplayed = false;
            _startMessageTimer = 0f;
            SettingsSanitizer.EnsureAiInfluenceKeys();
            Engine.PrimeStep(1f);
        }

        public override void OnGameEnd(Game game)
        {
            _startMessageDisplayed = false;
            _startMessageTimer = 0f;
            base.OnGameEnd(game);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            ChatNotices.FlushPending();
            Engine.PrimeStep(dt);
            PrismWebUiHost.Tick();

            // In-game visual confirmation on campaign / mission load
            if (Campaign.Current != null)
            {
                if (!_startMessageDisplayed)
                {
                    _startMessageTimer += dt;
                    if (_startMessageTimer >= 1f)
                    {
                        _startMessageDisplayed = true;
                        string banner = "[Prism] - Succesfully loaded in campaign";
                        try
                        {
                            InformationManager.DisplayMessage(new InformationMessage(banner, new Color(0.25f, 0.95f, 0.45f, 1f)));
                        }
                        catch
                        {
                            try
                            {
                                ChatNotices.Show(banner, null, 0.25f, 0.95f, 0.45f);
                            }
                            catch { }
                        }
                        LogSink.Always(banner);
                    }
                }
            }
            else
            {
                _startMessageDisplayed = false;
                _startMessageTimer = 0f;
            }

            if (_tickerNextFlushIn > 0f)
            {
                _tickerNextFlushIn -= dt;
                if (_tickerNextFlushIn <= 0f)
                {
                    _tickerNextFlushIn = TickerFlushIntervalSeconds;
                    if (ElapsedTicker.RequestCount > 0)
                    {
                        ElapsedTicker.FlushToLog();
                    }
                }
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            ChatNotices.FlushPending();
            ElapsedTicker.FlushToLog();
            Engine.TearDown();
            LogSink.Always("unloaded; patches removed.");
            LogSink.Flush();
            LogSink.Shutdown();
            base.OnSubModuleUnloaded();
        }
    }
}
