using System;
using System.IO;
using TaleWorlds.MountAndBlade;
using AIInfluencePrism.Config;
using AIInfluencePrism.Hook;
using AIInfluencePrism.Infra;

namespace AIInfluencePrism.PrismCore
{
    /// <summary>
    /// Entry point of the mod. It only handles the lifecycle:
    /// find the mod root -&gt; turn on the log -&gt; start the hook engine,
    /// which locates the target assembly itself, even if it loads later.
    /// The actual work lives in the Hook / Transport / Post layers.
    /// </summary>
    public sealed class SubModule : MBSubModuleBase
    {
        private const float TickerFlushIntervalSeconds = 60f;
        private float _tickerNextFlushIn = TickerFlushIntervalSeconds;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            string root = ModuleLocator.Root;
            if (root.Length == 0)
            {
                // Fallback: log to the current directory so the cause
                // of the unresolved root at least reaches the developer.
                LogSink.Initialize(Directory.GetCurrentDirectory(), verbose: true);
                LogSink.Error("module root could not be resolved; logging to current directory.");
                LogSink.Always("loaded (fallback location); module root unresolved.");
            }
            else
            {
                LogSink.Initialize(root, verbose: true);
                LogSink.Always("loaded; root: " + root);
            }

            int markers = AIInfluencePrism.Routing.MarkerTable.Warmup();
            LogSink.Always("routing ready: " + AIInfluencePrism.Routing.ScopeCatalog.All.Length
                + " scopes, " + markers + " markers.");

            AIInfluencePrism.Config.ProfileBook.Warmup();
            LogSink.Enabled = AIInfluencePrism.Config.ProfileBook.Current.LogEnabled;

            // Warm up JsonVault (pre-reads the vault file; thread-safe).
            JsonVault.GetString("_warmup", "");

            Engine.Create();
        }

        private bool _uiInitialized;

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

            Engine.PrimeStep(1f); // one decent attempt on the main screen
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            ChatNotices.FlushPending();
            Engine.PrimeStep(dt);

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
            ElapsedTicker.FlushToLog();   // final session summary in the log
            Engine.TearDown();
            LogSink.Always("unloaded; patches removed.");
            LogSink.Flush();
            LogSink.Shutdown();
            base.OnSubModuleUnloaded();
        }
    }
}
